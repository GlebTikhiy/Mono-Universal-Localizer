using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;

namespace BurglinGnomesRuAutoTranslate
{
    internal sealed class RuntimeTranslator
    {
        private const string MyMemoryEndpoint = "https://api.mymemory.translated.net/get";

        private static readonly Regex CyrillicRegex = new Regex("[\\p{IsCyrillic}]", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);
        private static readonly Regex JsonTranslatedTextRegex = new Regex("\"translatedText\"\\s*:\\s*\"(?<v>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled);
        private static readonly Regex UnicodeEscapeRegex = new Regex("\\\\u(?<h>[0-9a-fA-F]{4})", RegexOptions.Compiled);

        private readonly ManualLogSource _logger;
        private readonly bool _enabled;
        private readonly bool _enableWeb;
        private readonly string _endpoint;
        private readonly string _targetLanguage;
        private readonly int _retryDelaySeconds;
        private readonly int _maxWebRequestsPerSession;
        private readonly string _cacheFilePath;

        private readonly ConcurrentDictionary<string, string> _cache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _retryAfter = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly HashSet<string> _queued = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _queueLock = new object();
        private readonly object _cacheFileLock = new object();
        private readonly HttpClient _http = new HttpClient();

        private int _webRequestsSent;
        private int _newCacheEntriesSinceSave;
        private bool _webLimitLogged;

        public RuntimeTranslator(
            ManualLogSource logger,
            bool enabled,
            bool enableWeb,
            string endpoint,
            string targetLanguage,
            string dictionaryPath,
            string cacheFilePath,
            int retryDelaySeconds,
            int maxWebRequestsPerSession)
        {
            _logger = logger;
            _enabled = enabled;
            _enableWeb = enableWeb;
            _endpoint = endpoint;
            _targetLanguage = string.IsNullOrWhiteSpace(targetLanguage) ? "ru" : targetLanguage.Trim();
            _retryDelaySeconds = Math.Max(10, retryDelaySeconds);
            _maxWebRequestsPerSession = maxWebRequestsPerSession <= 0 ? 0 : Math.Max(50, maxWebRequestsPerSession);
            _cacheFilePath = cacheFilePath;

            _http.Timeout = TimeSpan.FromSeconds(6);

            LoadBuiltInDictionary();
            LoadCustomDictionary(dictionaryPath);
            LoadPersistentCache();
        }

        public string Translate(string input)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            if (LooksAlreadyLocalized(input) || !LooksTranslatable(input))
            {
                return input;
            }

            var key = Normalize(input);
            if (_cache.TryGetValue(key, out var translated))
            {
                return string.IsNullOrEmpty(translated) ? input : translated;
            }

            if (_enableWeb && ShouldRetryNow(key))
            {
                QueueWebTranslation(key, input);
            }

            return input;
        }

        private static string Normalize(string value)
        {
            return WhitespaceRegex.Replace(value.Trim(), " ");
        }

        private static bool LooksAlreadyLocalized(string value)
        {
            return CyrillicRegex.IsMatch(value);
        }

        private static bool LooksTranslatable(string value)
        {
            if (value.Length <= 1)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsLetter(value[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldRetryNow(string key)
        {
            DateTime retryAt;
            if (!_retryAfter.TryGetValue(key, out retryAt))
            {
                return true;
            }

            return DateTime.UtcNow >= retryAt;
        }

        private void LoadBuiltInDictionary()
        {
            var preset = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Play"] = "Играть",
                ["Options"] = "Настройки",
                ["Settings"] = "Настройки",
                ["Quit"] = "Выход",
                ["Exit"] = "Выход",
                ["Resume"] = "Продолжить",
                ["Back"] = "Назад",
                ["Apply"] = "Применить",
                ["Cancel"] = "Отмена",
                ["Loading"] = "Загрузка",
                ["Connecting"] = "Подключение",
                ["Disconnected"] = "Отключено",
                ["Inventory"] = "Инвентарь",
                ["Health"] = "Здоровье",
                ["Score"] = "Счёт",
                ["Level"] = "Уровень",
                ["Nickname"] = "Ник",
                ["Player"] = "Игрок",
                ["Host"] = "Хост",
                ["Client"] = "Клиент"
            };

            foreach (var pair in preset)
            {
                _cache[Normalize(pair.Key)] = pair.Value;
            }
        }

        private void LoadCustomDictionary(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return;
                }

                foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = rawLine == null ? string.Empty : rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('|');
                    if (separatorIndex < 1 || separatorIndex >= line.Length - 1)
                    {
                        continue;
                    }

                    var src = Normalize(line.Substring(0, separatorIndex));
                    var dst = line.Substring(separatorIndex + 1).Trim();
                    if (!string.IsNullOrEmpty(src) && !string.IsNullOrEmpty(dst))
                    {
                        _cache[src] = dst;
                    }
                }

                _logger.LogInfo($"Loaded dictionary: {path}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load dictionary: {ex.Message}");
            }
        }

        private void LoadPersistentCache()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_cacheFilePath) || !File.Exists(_cacheFilePath))
                {
                    return;
                }

                var loaded = 0;
                foreach (var line in File.ReadAllLines(_cacheFilePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var idx = line.IndexOf('|');
                    if (idx <= 0 || idx >= line.Length - 1)
                    {
                        continue;
                    }

                    var key = DecodeBase64(line.Substring(0, idx));
                    var value = DecodeBase64(line.Substring(idx + 1));
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    {
                        _cache[Normalize(key)] = value;
                        loaded++;
                    }
                }

                _logger.LogInfo($"Loaded persistent translation cache: {loaded} entries");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load persistent translation cache: {ex.Message}");
            }
        }

        private void SavePersistentCacheIfNeeded(bool force)
        {
            if (!force && _newCacheEntriesSinceSave < 1)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_cacheFilePath))
            {
                return;
            }

            lock (_cacheFileLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_cacheFilePath);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var lines = new List<string>(_cache.Count);
                    foreach (var kv in _cache)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
                        {
                            continue;
                        }

                        lines.Add(EncodeBase64(kv.Key) + "|" + EncodeBase64(kv.Value));
                    }

                    File.WriteAllLines(_cacheFilePath, lines, Encoding.UTF8);
                    _newCacheEntriesSinceSave = 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to save translation cache: {ex.Message}");
                }
            }
        }

        private static string EncodeBase64(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string DecodeBase64(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return string.Empty;
            }
        }

        private void QueueWebTranslation(string key, string sourceText)
        {
            lock (_queueLock)
            {
                if (_queued.Contains(key))
                {
                    return;
                }

                _queued.Add(key);
            }

            var requestNumber = Interlocked.Increment(ref _webRequestsSent);
            if (_maxWebRequestsPerSession > 0 && requestNumber > _maxWebRequestsPerSession)
            {
                if (!_webLimitLogged)
                {
                    _webLimitLogged = true;
                    _logger.LogWarning($"Web translation limit reached for this session: {_maxWebRequestsPerSession}");
                }

                _retryAfter[key] = DateTime.UtcNow.AddMinutes(10);
                lock (_queueLock)
                {
                    _queued.Remove(key);
                }
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var translated = await TranslateByApi(sourceText).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(sourceText, translated, StringComparison.Ordinal))
                    {
                        _cache[key] = translated;
                        _newCacheEntriesSinceSave++;
                        SavePersistentCacheIfNeeded(false);
                        _retryAfter.TryRemove(key, out _);
                    }
                    else
                    {
                        _retryAfter[key] = DateTime.UtcNow.AddSeconds(_retryDelaySeconds);
                    }
                }
                catch
                {
                    _retryAfter[key] = DateTime.UtcNow.AddSeconds(_retryDelaySeconds);
                }
                finally
                {
                    lock (_queueLock)
                    {
                        _queued.Remove(key);
                    }
                }
            });
        }

        private async Task<string> TranslateByApi(string sourceText)
        {
            if (!string.IsNullOrWhiteSpace(_endpoint))
            {
                var endpointResult = await TryLibreEndpoint(_endpoint, sourceText).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(endpointResult))
                {
                    return endpointResult;
                }
            }

            var publicResult = await TryMyMemory(sourceText).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(publicResult) ? sourceText : publicResult;
        }

        private async Task<string> TryLibreEndpoint(string endpoint, string sourceText)
        {
            var payload = "{\"q\":\"" + EscapeJson(sourceText) + "\",\"source\":\"auto\",\"target\":\"" + EscapeJson(_targetLanguage) + "\",\"format\":\"text\"}";
            using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
            using (var response = await _http.PostAsync(endpoint, content).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ExtractTranslatedText(body);
            }
        }

        private async Task<string> TryMyMemory(string sourceText)
        {
            var url = MyMemoryEndpoint + "?q=" + Uri.EscapeDataString(sourceText) + "&langpair=" + Uri.EscapeDataString("en|" + _targetLanguage);
            using (var response = await _http.GetAsync(url).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ExtractTranslatedText(body);
            }
        }

        private static string ExtractTranslatedText(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var match = JsonTranslatedTextRegex.Match(body);
            if (!match.Success)
            {
                return null;
            }

            var raw = UnescapeJson(match.Groups["v"].Value);
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string UnescapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var basic = value
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");

            return UnicodeEscapeRegex.Replace(basic, m =>
            {
                var hex = m.Groups["h"].Value;
                int code;
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                {
                    return ((char)code).ToString();
                }

                return m.Value;
            });
        }
    }
}


