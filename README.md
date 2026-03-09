# Mono Universal Localizer (BepInEx, Unity Mono)

Универсальный мод для Unity Mono игр:
- автоперевод интерфейса/текста на русский,
- исправление кириллицы (UI.Text, TMP, TextMesh),
- постоянный кэш переводов между запусками,
- работа через BepInEx 5.x.

## Что важно
- Мод универсальный (не только для Burglin' Gnomes).
- Название плагина в логе: `Mono Universal Localizer`.
- GUID: `com.glebtikhiy.monouniversallocalizer`.

## Быстрая установка (для игроков)
1. Убедись, что в игре установлен **BepInEx 5.x**.
2. Скачай ZIP из GitHub Releases.
3. Скопируй `BurglinGnomesRuAutoTranslate.dll` в:
   - `<GAME>\BepInEx\plugins\MonoUniversalLocalizer\`
4. (Опционально) Скопируй `MonoUniversal.dictionary.txt` в:
   - `<GAME>\BepInEx\config\`
5. Запусти игру.

## Почему теперь не переводит с нуля каждый запуск
Добавлен постоянный кэш:
- файл: `<GAME>\BepInEx\config\MonoUniversal.translation.cache.txt`
- при следующем запуске переводы берутся из файла, а не заново из интернета.

## Лимит веб-переводов
Параметр:
- `MaxWebRequestsPerSession = 0` (по умолчанию без ограничений)
- если нужно ограничение, поставь число `> 0`.

## Словарь (опционально)
`MonoUniversal.dictionary.txt` не обязателен, но полезен:
- даёт мгновенный перевод частых строк,
- повышает стабильность формулировок,
- уменьшает количество API-запросов.

Формат строк:
- `Original|Перевод`

Пример:
- `New Game|Новая игра`

## Главные настройки
Файл конфига:
- `<GAME>\BepInEx\config\com.glebtikhiy.monouniversallocalizer.cfg`

Ключи:
- `Enable = true`
- `TargetLanguage = ru`
- `EnableWebTranslator = true`
- `WebEndpoint =` (пусто = публичный fallback)
- `WebRetryDelaySeconds = 30`
- `PersistentCachePath = MonoUniversal.translation.cache.txt`
- `MaxWebRequestsPerSession = 0`
- `DictionaryPath = MonoUniversal.dictionary.txt`
- `TmpFontFilePath = C:\Windows\Fonts\arial.ttf;C:\Windows\Fonts\tahoma.ttf;C:\Windows\Fonts\segoeui.ttf`

## Сборка (для разработчиков)
1. Подтянуть DLL игры:
```powershell
powershell -ExecutionPolicy Bypass -File .\prepare-libs.ps1 -GamePath "D:\Games\YourGame"
```
2. Сборка:
```powershell
dotnet build .\BurglinGnomesRuAutoTranslate.csproj -c Release
```

## Упаковка релиза
```powershell
powershell -ExecutionPolicy Bypass -File .\pack-release.ps1
```
Архив появится в `dist`.

## Лицензия
MIT
