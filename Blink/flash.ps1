<#
.SYNOPSIS
    Прошивает ESP32-S3-WROOM-1 N16R8 средой nanoFramework и загружает программу Blink.
    Расширение Visual Studio не требуется: сборка идёт компилятором csc и Metadata Processor из NuGet.

.EXAMPLE
    .\flash.ps1 -Port COM5               # только сборка и загрузка Blink (обычный случай)
    .\flash.ps1 -Port COM5 -Firmware     # сначала заново записать среду nanoCLR
    .\flash.ps1 -ListPorts               # показать COM-порты
#>
param(
    [string]$Port,
    [string]$Target = 'ESP32_S3_OCTAL',   # N16R8: 8 МБ PSRAM подключены по Octal SPI
    [switch]$Firmware,
    [switch]$ListPorts
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command nanoff -ErrorAction SilentlyContinue)) {
    throw 'nanoff не найден. Установите: dotnet tool install -g nanoff'
}

if ($ListPorts -or -not $Port) {
    nanoff --listports
    if (-not $Port) { Write-Host "`nУкажите порт платы: .\flash.ps1 -Port COM5" }
    return
}

if ($Firmware) {
    Write-Host "== Прошивка nanoCLR ($Target) на $Port ==" -ForegroundColor Cyan
    # --nobackupconfig: через встроенный USB платы резервная копия раздела настроек обрывается с E4000.
    # Настройки Wi-Fi, сохранённые на плате, при этом стираются.
    nanoff --target $Target --serialport $Port --update --nobackupconfig
    # Через встроенный USB nanoff завершается с E4000 уже после успешной записи (порт пропадает при сбросе),
    # поэтому результат проверяется ответом самой среды, а не кодом возврата.
    Start-Sleep -Seconds 4
    $details = nanoff --nanodevice --devicedetails --serialport $Port 2>&1 | Out-String
    if ($details -notmatch 'CLR Version') {
        throw 'Среда nanoFramework не отвечает. Зажмите BOOT, нажмите RST, отпустите BOOT и повторите.'
    }
}

Write-Host '== Сборка Blink ==' -ForegroundColor Cyan
nuget restore packages.config -PackagesDirectory packages -Verbosity quiet
nuget install nanoFramework.Tools.MetadataProcessor.CLI -Version 3.0.104 -OutputDirectory packages -Verbosity quiet

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$csc = Join-Path (& $vswhere -latest -prerelease -property installationPath) 'MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path $csc)) { throw "Не найден компилятор C#: $csc" }

$mdp = Join-Path $PSScriptRoot 'packages\nanoFramework.Tools.MetadataProcessor.CLI.3.0.104\content\MetadataProcessor\nanoFramework.Tools.MetadataProcessor.exe'
$out = Join-Path $PSScriptRoot 'bin\Debug'
New-Item -ItemType Directory -Force $out | Out-Null

$libs = Get-ChildItem packages -Recurse -Filter *.dll | Where-Object { $_.FullName -notmatch 'MetadataProcessor' }

& $csc /nologo /nostdlib /noconfig /target:exe /debug:full /optimize- "/out:$out\Blink.exe" ($libs | ForEach-Object { "/r:$($_.FullName)" }) Program.cs Properties\AssemblyInfo.cs
if ($LASTEXITCODE -ne 0) { throw "Компиляция не прошла, код $LASTEXITCODE" }

# Metadata Processor превращает обычную .NET-сборку в компактный формат .pe, который понимает nanoCLR.
$hints = $libs | ForEach-Object { '-loadHints'; $_.BaseName; $_.FullName }
& $mdp @hints -parse "$out\Blink.exe" -compile "$out\Blink.pe" false | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path "$out\Blink.pe")) { throw 'Metadata Processor не создал Blink.pe' }

# Образ для платы — это все .pe подряд с выравниванием на 4 байта: сначала mscorlib, в конце программа.
$pes = @(Get-ChildItem packages -Recurse -Filter *.pe | Sort-Object { $_.Name -ne 'mscorlib.pe' }, Name) + (Get-Item "$out\Blink.pe")
$image = New-Object System.IO.MemoryStream
foreach ($pe in $pes) {
    $bytes = [IO.File]::ReadAllBytes($pe.FullName)
    $image.Write($bytes, 0, $bytes.Length)
    $pad = (4 - $bytes.Length % 4) % 4
    if ($pad) { $image.Write((New-Object byte[] $pad), 0, $pad) }
}
[IO.File]::WriteAllBytes("$out\Blink.bin", $image.ToArray())

Write-Host "== Загрузка Blink на $Port ==" -ForegroundColor Cyan
nanoff --target $Target --serialport $Port --deploy --image "$out\Blink.bin"
if ($LASTEXITCODE -ne 0) { throw "Загрузка не прошла, код $LASTEXITCODE" }

Write-Host 'Готово. Программа загружена. Если светодиоды не мигают — нажмите RST на плате.' -ForegroundColor Green
