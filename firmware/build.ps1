<#
.SYNOPSIS
    Собирает проект nanoFramework без расширения Visual Studio и загружает его на ESP32-S3.

.DESCRIPTION
    Компилятор csc → Metadata Processor (.pe) → склейка .pe в образ .bin → nanoff.
    Пакеты берутся из packages.config проекта и кладутся в firmware\packages.
    Если в папке проекта есть uses-core.txt, к исходникам добавляются файлы firmware\PrintAgent.Core.
    Если есть admin.html, из него создаётся AdminPage.g.cs с константой AdminPage.Html.

.EXAMPLE
    firmware\build.ps1 -Project Blink -NoDeploy          # только собрать образ
    firmware\build.ps1 -Project Blink -Port COM5         # собрать и загрузить
    firmware\build.ps1 -Project Blink -Port COM5 -Firmware   # сначала заново записать nanoCLR
#>
param(
    [Parameter(Mandatory)][string]$Project,
    [string]$Port,
    [string]$Target = 'ESP32_S3_OCTAL',   # N16R8: 8 МБ PSRAM подключены по Octal SPI
    [switch]$Firmware,
    [switch]$NoDeploy
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$projectDir = if ([IO.Path]::IsPathRooted($Project)) { $Project } else { Join-Path $repoRoot $Project }
if (-not (Test-Path $projectDir -PathType Container)) { throw "Папка проекта не найдена: $projectDir" }
$projectDir = (Resolve-Path $projectDir).Path
$name = Split-Path $projectDir -Leaf
$packagesDir = Join-Path $PSScriptRoot 'packages'
$mdpVersion = '3.0.104'

if (-not $NoDeploy -and -not $Port) { throw 'Укажите порт платы (-Port COM5) или -NoDeploy.' }
if (-not $NoDeploy -and -not (Get-Command nanoff -ErrorAction SilentlyContinue)) {
    throw 'nanoff не найден. Установите: dotnet tool install -g nanoff'
}

Write-Host "== Пакеты для $name ==" -ForegroundColor Cyan
$packagesConfig = Join-Path $projectDir 'packages.config'
if (-not (Test-Path $packagesConfig)) { throw "Нет файла $packagesConfig" }
nuget restore $packagesConfig -PackagesDirectory $packagesDir -Verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "nuget restore завершился с кодом $LASTEXITCODE" }
nuget install nanoFramework.Tools.MetadataProcessor.CLI -Version $mdpVersion -OutputDirectory $packagesDir -Verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Не удалось получить Metadata Processor, код $LASTEXITCODE" }

# Берём библиотеки только тех пакетов, что перечислены в packages.config проекта:
# иначе чужие пакеты из общей папки попали бы в образ.
$libs = @()
$pes = @()
foreach ($package in ([xml](Get-Content $packagesConfig -Raw)).packages.package) {
    $lib = Join-Path $packagesDir "$($package.id).$($package.version)\lib"
    if (-not (Test-Path $lib)) { throw "Пакет не восстановлен: $($package.id) $($package.version)" }
    $libs += Get-ChildItem $lib -Filter *.dll
    $pes += Get-ChildItem $lib -Filter *.pe
}

Write-Host "== Компиляция $name ==" -ForegroundColor Cyan
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$csc = Join-Path (& $vswhere -latest -prerelease -property installationPath) 'MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path $csc)) { throw "Не найден компилятор C#: $csc" }

$adminHtml = Join-Path $projectDir 'admin.html'
if (Test-Path $adminHtml) {
    # Дословная строка C#: кавычки удваиваются, остальное остаётся как есть.
    $html = (Get-Content $adminHtml -Raw).Replace('"', '""')
    $generated = "// Создано build.ps1 из admin.html. Не править вручную.`nnamespace $name`n{`n    public static class AdminPage`n    {`n        public const string Html = @`"$html`";`n    }`n}`n"
    [IO.File]::WriteAllText((Join-Path $projectDir 'AdminPage.g.cs'), $generated, [Text.UTF8Encoding]::new($false))
}

$sources = @(Get-ChildItem $projectDir -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj|packages)\\' })
if (Test-Path (Join-Path $projectDir 'uses-core.txt')) {
    $sources += Get-ChildItem (Join-Path $PSScriptRoot 'PrintAgent.Core') -Filter *.cs
}
if ($sources.Count -eq 0) { throw "В проекте $name нет файлов .cs" }

$out = Join-Path $projectDir 'bin\Debug'
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\$name.exe", "$out\$name.pe", "$out\$name.bin" -ErrorAction SilentlyContinue

& $csc /nologo /nostdlib /noconfig /target:exe /debug:full /optimize- /utf8output "/out:$out\$name.exe" ($libs | ForEach-Object { "/r:$($_.FullName)" }) ($sources | ForEach-Object FullName)
if ($LASTEXITCODE -ne 0) { throw "Компиляция не прошла, код $LASTEXITCODE" }

# Metadata Processor превращает обычную .NET-сборку в компактный формат .pe, который понимает nanoCLR.
$mdp = Join-Path $packagesDir "nanoFramework.Tools.MetadataProcessor.CLI.$mdpVersion\content\MetadataProcessor\nanoFramework.Tools.MetadataProcessor.exe"
$hints = $libs | ForEach-Object { '-loadHints'; $_.BaseName; $_.FullName }
& $mdp @hints -parse "$out\$name.exe" -compile "$out\$name.pe" false | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path "$out\$name.pe")) { throw "Metadata Processor не создал $name.pe" }

# Образ для платы — все .pe подряд с выравниванием на 4 байта: сначала mscorlib, в конце программа.
$image = New-Object System.IO.MemoryStream
foreach ($pe in @($pes | Sort-Object { $_.Name -ne 'mscorlib.pe' }, Name) + (Get-Item "$out\$name.pe")) {
    $bytes = [IO.File]::ReadAllBytes($pe.FullName)
    $image.Write($bytes, 0, $bytes.Length)
    $pad = (4 - $bytes.Length % 4) % 4
    if ($pad) { $image.Write((New-Object byte[] $pad), 0, $pad) }
}
[IO.File]::WriteAllBytes("$out\$name.bin", $image.ToArray())
Write-Host "Образ: $out\$name.bin ($($image.Length) байт)"

if ($NoDeploy) { return }

if ($Firmware) {
    Write-Host "== Прошивка nanoCLR ($Target) на $Port ==" -ForegroundColor Cyan
    # --nobackupconfig: через встроенный USB платы резервная копия раздела настроек обрывается с E4000.
    # После успешной записи nanoff тоже завершается с E4000 (порт пропадает при сбросе),
    # поэтому код возврата здесь не проверяется: результат подтверждает опрос платы ниже.
    nanoff --target $Target --serialport $Port --update --nobackupconfig
    Start-Sleep -Seconds 4
}

Write-Host "== Загрузка $name на $Port ==" -ForegroundColor Cyan
nanoff --target $Target --serialport $Port --deploy --image "$out\$name.bin"
if ($LASTEXITCODE -ne 0) { throw "Загрузка не прошла, код $LASTEXITCODE" }

# Плата после загрузки перезапускается, первый опрос может не пройти — пробуем несколько раз.
$loaded = $false
foreach ($attempt in 1..4) {
    Start-Sleep -Seconds 4
    $details = nanoff --nanodevice --devicedetails --serialport $Port 2>&1 | Out-String
    if ($details -match "(?m)^\s+$([regex]::Escape($name)),") { $loaded = $true; break }
}
if (-not $loaded) { throw "Плата не показала сборку $name среди загруженных. Нажмите RST и проверьте версии пакетов." }

Write-Host "Готово: $name загружена и принята платой." -ForegroundColor Green
