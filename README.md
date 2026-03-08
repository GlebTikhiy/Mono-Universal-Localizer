# Mono Universal Localizer (BepInEx, Unity Mono)

Универсальный мод для Unity Mono игр:
- автоперевод интерфейса/текста на русский,
- исправление кириллицы (UI.Text, TMP, TextMesh),
- постоянный кэш переводов между запусками,
- работа через BepInEx 5.x.

## Что важно
- Мод теперь **универсальный** (не только для Burglin' Gnomes).
- Название плагина в логе: `Mono Universal Localizer`.
- GUID: `com.glebtikhiy.monouniversallocalizer`.

## Быстрая установка (для игроков)
1. Убедись, что в игре установлен **BepInEx 5.x**.
2. Скачай ZIP из GitHub Releases.
3. Скопируй `BurglinGnomesRuAutoTranslate.dll` в:
   - `<GAME>\BepInEx\plugins\MonoUniversalLocalizer\`
4. Скопируй `MonoUniversal.dictionary.txt` в:
   - `<GAME>\BepInEx\config\`
5. Запусти игру.

## Почему теперь не переводит с нуля каждый запуск
Добавлен постоянный кэш:
- файл: `<GAME>\BepInEx\config\MonoUniversal.translation.cache.txt`
- при следующем запуске переводы берутся из файла, а не заново из интернета.

## Ограничение на веб-переводы
Есть лимит запросов за 1 запуск (чтобы не упираться в rate limit API):
- `MaxWebRequestsPerSession = 0`
- параметр в конфиге можно увеличить/уменьшить.

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

## Словарь
Формат строк:
- `Original|Перевод`

Пример:
- `New Game|Новая игра`

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

