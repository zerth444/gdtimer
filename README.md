# GDTimer

GDTimer отслеживает время, проведённое в Geometry Dash: показывает общее время за день и статистику по уровням. Программа работает в системном трее и поддерживает горячую клавишу, запуск вместе с Windows и русский и английский языки.

## Установка

Скачайте и запустите [GDTimer-Setup-1.0.0.exe](dist/GDTimer-Setup-1.0.0.exe). Установщик поддерживает Windows x64.

## Сборка

Для сборки установщика нужны Inno Setup 6 и собранный `GDTimer.exe`:

```powershell
& "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe" installer.iss
```

Результат появится в папке `dist`.

## Лицензия

Исходный код GDTimer распространяется по лицензии MIT. Шрифты Comfortaa распространяются по SIL Open Font License 1.1; текст лицензии находится в `fonts/OFL.txt`.

