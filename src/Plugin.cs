using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace BurglinGnomesRuAutoTranslate
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.glebtikhiy.monouniversallocalizer";
        public const string PluginName = "Mono Universal Localizer";
        public const string PluginVersion = "2.1.0";

        private const string CyrillicProbe = "АБВГДЕЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеежзийклмнопрстуфхцчшщъыьэюя";

        private static Plugin _instance;
        private readonly Harmony _harmony = new Harmony(PluginGuid);

        private RuntimeTranslator _translator;
        private Font _runtimeFont;
        private TMP_FontAsset _runtimeTmpFont;
        private float _nextScanAt;
        private bool _isInternalTextSet;
        private bool _tmpFallbackInjected;
        private readonly Dictionary<int, int> _uiBaseFontSize = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _tmpBaseFontSize = new Dictionary<int, float>();

        private ConfigEntry<bool> _enableTranslator;
        private ConfigEntry<bool> _enableFontOverride;
        private ConfigEntry<string> _fontCandidates;
        private ConfigEntry<string> _fontFilePath;
        private ConfigEntry<int> _fontSize;
        private ConfigEntry<float> _scanInterval;
        private ConfigEntry<string> _dictionaryPath;
        private ConfigEntry<bool> _enableWebTranslator;
        private ConfigEntry<string> _webEndpoint;
        private ConfigEntry<string> _targetLanguage;
        private ConfigEntry<int> _webRetryDelaySeconds;
        private ConfigEntry<string> _translationCachePath;
        private ConfigEntry<int> _maxWebRequestsPerSession;

        public static Plugin Instance => _instance;
        internal bool IsInternalTextSet => _isInternalTextSet;
        internal bool EnableFontOverride => _enableFontOverride.Value;

        private void Awake()
        {
            _instance = this;
            BindConfig();
            CreateTranslator();
            CreateRuntimeFonts();

            _harmony.PatchAll(typeof(TextPatches));
            _harmony.PatchAll(typeof(TmpPatches));
            _harmony.PatchAll(typeof(TextMeshPatches));

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScanAt)
            {
                return;
            }

            _nextScanAt = Time.unscaledTime + Mathf.Max(0.2f, _scanInterval.Value);

            if (!_tmpFallbackInjected && _runtimeTmpFont != null)
            {
                InjectTmpFallbacks();
                _tmpFallbackInjected = true;
            }

            ScanAndTranslateVisibleTexts();
        }

        private void BindConfig()
        {
            _enableTranslator = Config.Bind("Translation", "Enable", true, "Enable runtime translation.");
            _targetLanguage = Config.Bind("Translation", "TargetLanguage", "ru", "Translation language code.");
            _dictionaryPath = Config.Bind("Translation", "DictionaryPath", "MonoUniversal.dictionary.txt", "Dictionary file path in BepInEx/config.");
            _enableWebTranslator = Config.Bind("Translation", "EnableWebTranslator", true, "Use internet translation for unknown strings.");
            _webEndpoint = Config.Bind("Translation", "WebEndpoint", "", "Optional custom LibreTranslate-compatible endpoint URL. Leave empty to use public fallback provider.");
            _webRetryDelaySeconds = Config.Bind("Translation", "WebRetryDelaySeconds", 30, "Delay before retrying failed internet translations.");
            _translationCachePath = Config.Bind("Translation", "PersistentCachePath", "MonoUniversal.translation.cache.txt", "Persistent translation cache file in BepInEx/config.");
            _maxWebRequestsPerSession = Config.Bind("Translation", "MaxWebRequestsPerSession", 0, "Max new web translation requests per launch. Set 0 for unlimited.");

            _enableFontOverride = Config.Bind("Font", "EnableOverride", true, "Force runtime font replacement to support Cyrillic.");
            _fontCandidates = Config.Bind("Font", "PreferredFonts", "Arial,Tahoma,Segoe UI,Verdana,Times New Roman,Calibri,Noto Sans,Arial Unicode MS", "Installed OS font candidates, comma-separated.");
            _fontFilePath = Config.Bind("Font", "TmpFontFilePath", "C:\\Windows\\Fonts\\arial.ttf", "TTF path for TMP fallback (can be multiple paths separated by ';').");
            _fontSize = Config.Bind("Font", "DefaultSize", 22, "Runtime dynamic font size.");

            _scanInterval = Config.Bind("General", "ScanIntervalSeconds", 0.8f, "How often scene texts are rescanned.");
        }

        private void CreateTranslator()
        {
            var dictionaryFullPath = Path.Combine(Paths.ConfigPath, _dictionaryPath.Value);
            var legacyDictionary = Path.Combine(Paths.ConfigPath, "BurglinGnomesRU.dictionary.txt");
            if (!File.Exists(dictionaryFullPath) && File.Exists(legacyDictionary))
            {
                dictionaryFullPath = legacyDictionary;
            }

            var cacheFullPath = Path.Combine(Paths.ConfigPath, _translationCachePath.Value);
            _translator = new RuntimeTranslator(
                Logger,
                _enableTranslator.Value,
                _enableWebTranslator.Value,
                _webEndpoint.Value,
                _targetLanguage.Value,
                dictionaryFullPath,
                cacheFullPath,
                _webRetryDelaySeconds.Value,
                _maxWebRequestsPerSession.Value);
        }

        private void CreateRuntimeFonts()
        {
            var names = BuildFontCandidateList(_fontCandidates.Value);
            var desiredSize = Mathf.Max(14, _fontSize.Value);

            _runtimeFont = PickCyrillicCapableFont(names, desiredSize);
            if (_runtimeFont == null)
            {
                Logger.LogWarning("Could not find OS font with Cyrillic support. Font override is disabled.");
            }
            else
            {
                Logger.LogInfo($"Selected Cyrillic font: {_runtimeFont.name}");
            }

            _runtimeTmpFont = CreateTmpFontAssetForCyrillic(_runtimeFont, _fontFilePath.Value);
            if (_runtimeTmpFont == null)
            {
                Logger.LogWarning("Could not create TMP Cyrillic font asset. TMP fallback injection is disabled.");
            }
        }

        internal void ProcessText(Text uiText)
        {
            if (uiText == null)
            {
                return;
            }

            if (EnableFontOverride && _runtimeFont != null && uiText.font != _runtimeFont)
            {
                uiText.font = _runtimeFont;
                uiText.supportRichText = true;
                uiText.SetAllDirty();
            }

            var baseSize = GetUiBaseFontSize(uiText);
            uiText.resizeTextForBestFit = true;
            uiText.resizeTextMaxSize = baseSize;
            uiText.resizeTextMinSize = Mathf.Max(8, Mathf.RoundToInt(baseSize * 0.55f));

            ApplyTranslatedText(
                () => uiText.text,
                t =>
                {
                    uiText.text = t;
                    uiText.SetAllDirty();
                });
        }

        internal void ProcessText(TMP_Text tmp)
        {
            if (tmp == null)
            {
                return;
            }

            if (EnableFontOverride && _runtimeTmpFont != null)
            {
                if (tmp.font != null)
                {
                    AddFallbackOnce(tmp.font, _runtimeTmpFont);
                }

                if (tmp.font == null || !FontAssetHasCyrillic(tmp.font))
                {
                    tmp.font = _runtimeTmpFont;
                }

                TryAddCharactersSafe(_runtimeTmpFont, tmp.text);
                var baseSize = GetTmpBaseFontSize(tmp);
                tmp.enableAutoSizing = true;
                tmp.fontSizeMax = baseSize;
                tmp.fontSizeMin = Mathf.Max(8f, baseSize * 0.55f);
                tmp.UpdateMeshPadding();
            }

            ApplyTranslatedText(
                () => tmp.text,
                t =>
                {
                    if (_runtimeTmpFont != null)
                    {
                        TryAddCharactersSafe(_runtimeTmpFont, t);
                    }

                    tmp.text = t;
                });
        }

        internal void ProcessText(TextMesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (EnableFontOverride && _runtimeFont != null && mesh.font != _runtimeFont)
            {
                mesh.font = _runtimeFont;
            }

            ApplyTranslatedText(
                () => mesh.text,
                t => mesh.text = t);
        }

        private void ApplyTranslatedText(Func<string> getter, Action<string> setter)
        {
            if (!_enableTranslator.Value || _isInternalTextSet)
            {
                return;
            }

            var src = getter();
            var translated = _translator.Translate(src);
            if (string.IsNullOrEmpty(src) || string.Equals(src, translated, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                _isInternalTextSet = true;
                setter(translated);
            }
            finally
            {
                _isInternalTextSet = false;
            }
        }

        private void ScanAndTranslateVisibleTexts()
        {
            foreach (var text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (text != null && text.gameObject != null && text.gameObject.scene.IsValid())
                {
                    ProcessText(text);
                }
            }

            foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (text != null && text.gameObject != null && text.gameObject.scene.IsValid())
                {
                    ProcessText(text);
                }
            }

            foreach (var text in Resources.FindObjectsOfTypeAll<TextMesh>())
            {
                if (text != null && text.gameObject != null && text.gameObject.scene.IsValid())
                {
                    ProcessText(text);
                }
            }
        }

        private void InjectTmpFallbacks()
        {
            if (_runtimeTmpFont == null)
            {
                return;
            }

            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var f in fonts)
            {
                AddFallbackOnce(f, _runtimeTmpFont);
            }

            if (TMP_Settings.defaultFontAsset != null)
            {
                AddFallbackOnce(TMP_Settings.defaultFontAsset, _runtimeTmpFont);
            }
        }

        private TMP_FontAsset CreateTmpFontAssetForCyrillic(Font sourceFont, string fontFilePaths)
        {
            var fromFile = TryCreateTmpFontFromFile(fontFilePaths);
            if (fromFile != null)
            {
                Logger.LogInfo($"TMP Cyrillic font from file: {fromFile.name}");
                return fromFile;
            }

            TMP_FontAsset candidate = null;
            if (sourceFont != null)
            {
                try
                {
                    candidate = TMP_FontAsset.CreateFontAsset(sourceFont, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                    if (PrepareTmpAsset(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"TMP create from OS font failed: {ex.Message}");
                }
            }

            try
            {
                var builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (builtin != null)
                {
                    var builtinAsset = TMP_FontAsset.CreateFontAsset(builtin, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                    if (PrepareTmpAsset(builtinAsset))
                    {
                        Logger.LogInfo("TMP Cyrillic fallback font: built-in Arial.ttf");
                        return builtinAsset;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"TMP create from built-in Arial failed: {ex.Message}");
            }

            var existing = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var f in existing)
            {
                if (FontAssetHasCyrillic(f))
                {
                    Logger.LogInfo($"TMP Cyrillic fallback font: existing asset {f.name}");
                    return f;
                }
            }

            return null;
        }

        private TMP_FontAsset TryCreateTmpFontFromFile(string configuredPaths)
        {
            var paths = SplitPathList(configuredPaths);
            AddIfMissingPath(paths, "C:\\Windows\\Fonts\\arial.ttf");
            AddIfMissingPath(paths, "C:\\Windows\\Fonts\\tahoma.ttf");
            AddIfMissingPath(paths, "C:\\Windows\\Fonts\\segoeui.ttf");

            foreach (var p in paths)
            {
                try
                {
                    if (!File.Exists(p))
                    {
                        continue;
                    }

                    var fa = TMP_FontAsset.CreateFontAsset(p, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                    if (PrepareTmpAsset(fa))
                    {
                        return fa;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"TMP create from file failed [{p}]: {ex.Message}");
                }
            }

            return null;
        }

        
        private int GetUiBaseFontSize(Text uiText)
        {
            var key = uiText.GetInstanceID();
            int size;
            if (_uiBaseFontSize.TryGetValue(key, out size))
            {
                return size;
            }

            size = uiText.fontSize > 0 ? uiText.fontSize : Mathf.Max(12, _fontSize.Value);
            _uiBaseFontSize[key] = size;
            return size;
        }

        private float GetTmpBaseFontSize(TMP_Text tmp)
        {
            var key = tmp.GetInstanceID();
            float size;
            if (_tmpBaseFontSize.TryGetValue(key, out size))
            {
                return size;
            }

            size = tmp.fontSize > 0f ? tmp.fontSize : Mathf.Max(12f, _fontSize.Value);
            _tmpBaseFontSize[key] = size;
            return size;
        }
        private static bool PrepareTmpAsset(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return false;
            }

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            TryAddCharactersSafe(fontAsset, CyrillicProbe);
            return FontAssetHasCyrillic(fontAsset);
        }

        private static void TryAddCharactersSafe(TMP_FontAsset fontAsset, string text)
        {
            if (fontAsset == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            fontAsset.TryAddCharacters(text, out _);
        }

        private static bool FontAssetHasCyrillic(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return false;
            }

            return fontAsset.HasCharacter('Д', true, true) && fontAsset.HasCharacter('я', true, true);
        }

        private static void AddFallbackOnce(TMP_FontAsset baseAsset, TMP_FontAsset fallbackAsset)
        {
            if (baseAsset == null || fallbackAsset == null || baseAsset == fallbackAsset)
            {
                return;
            }

            if (baseAsset.fallbackFontAssetTable == null)
            {
                baseAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            }

            for (var i = 0; i < baseAsset.fallbackFontAssetTable.Count; i++)
            {
                if (baseAsset.fallbackFontAssetTable[i] == fallbackAsset)
                {
                    return;
                }
            }

            baseAsset.fallbackFontAssetTable.Add(fallbackAsset);
        }

        private static List<string> BuildFontCandidateList(string configCsv)
        {
            var result = SplitCsv(configCsv);
            AddIfMissing(result, "Arial");
            AddIfMissing(result, "Tahoma");
            AddIfMissing(result, "Segoe UI");
            AddIfMissing(result, "Verdana");
            AddIfMissing(result, "Times New Roman");
            AddIfMissing(result, "Calibri");
            AddIfMissing(result, "Arial Unicode MS");
            return result;
        }

        private static void AddIfMissing(List<string> names, string value)
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            names.Add(value);
        }

        private static void AddIfMissingPath(List<string> paths, string value)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            paths.Add(value);
        }

        private Font PickCyrillicCapableFont(List<string> names, int size)
        {
            foreach (var name in names)
            {
                Font f;
                try
                {
                    f = Font.CreateDynamicFontFromOSFont(name, size);
                }
                catch
                {
                    continue;
                }

                if (f != null && SupportsCyrillic(f, size))
                {
                    return f;
                }
            }

            try
            {
                var all = Font.CreateDynamicFontFromOSFont(names.ToArray(), size);
                if (all != null && SupportsCyrillic(all, size))
                {
                    return all;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static bool SupportsCyrillic(Font font, int size)
        {
            if (font == null)
            {
                return false;
            }

            font.RequestCharactersInTexture(CyrillicProbe, size, FontStyle.Normal);
            for (var i = 0; i < CyrillicProbe.Length; i++)
            {
                CharacterInfo info;
                if (!font.GetCharacterInfo(CyrillicProbe[i], out info, size, FontStyle.Normal))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> SplitCsv(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        private static List<string> SplitPathList(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            var parts = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }
    }

    [HarmonyPatch]
    internal static class TextPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Text), "set_text")]
        private static void PostfixSetText(Text __instance)
        {
            if (Plugin.Instance != null && !Plugin.Instance.IsInternalTextSet)
            {
                Plugin.Instance.ProcessText(__instance);
            }
        }
    }

    [HarmonyPatch]
    internal static class TmpPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TMP_Text), "set_text")]
        private static void PostfixSetText(TMP_Text __instance)
        {
            if (Plugin.Instance != null && !Plugin.Instance.IsInternalTextSet)
            {
                Plugin.Instance.ProcessText(__instance);
            }
        }
    }

    [HarmonyPatch]
    internal static class TextMeshPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TextMesh), "set_text")]
        private static void PostfixSetText(TextMesh __instance)
        {
            if (Plugin.Instance != null && !Plugin.Instance.IsInternalTextSet)
            {
                Plugin.Instance.ProcessText(__instance);
            }
        }
    }
}








