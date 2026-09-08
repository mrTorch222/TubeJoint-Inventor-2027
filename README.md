# TubeJoint for Autodesk Inventor 2027

Надстройка для создания редактируемых соединений «шип-паз» между трубами Frame Generator в Autodesk Inventor 2027.

## Возможности

- создание одного или двух противоположных шипов и пазов;
- выбор сторон соединения и поворот расположения на 90°;
- настройка размеров, зазора и дополнительного отверстия;
- предварительный просмотр и 3D-манипуляторы;
- поддержка профилей из линий, дуг и сплайнов;
- сохранение штатных операций Frame Generator Trim, Miter и Notch.

Профили хранятся в редактируемом шаблоне Inventor. При первом запуске создаётся простой базовый шаблон.

## Требования

- Autodesk Inventor Professional 2027;
- Windows 10 или Windows 11;
- .NET 8 SDK для сборки.

## Сборка и установка

Закройте Inventor и выполните в PowerShell из корня проекта:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

После запуска Inventor команда находится на вкладке **Design** в панели **Шип-паз труб**.
