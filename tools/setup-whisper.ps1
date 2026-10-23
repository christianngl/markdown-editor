# Richtet die lokale Spracherkennung für das Diktat ein (einmalig, danach läuft alles offline).
#   whisper.cpp (MIT-Lizenz)  https://github.com/ggml-org/whisper.cpp
#   Modelle (MIT-Lizenz)      https://huggingface.co/ggerganov/whisper.cpp
# Aufruf:  powershell -ExecutionPolicy Bypass -File .\tools\setup-whisper.ps1 [-Model base|small|medium] [-Target <Ordner>]
param(
    [ValidateSet('tiny', 'base', 'small', 'medium', 'large-v3-turbo')][string]$Model = 'base',
    [string]$Target
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # beschleunigt Invoke-WebRequest erheblich
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Target) { $Target = Join-Path $root 'bin\whisper' }
$models = Join-Path $Target 'models'
New-Item -ItemType Directory -Force $models | Out-Null

# ---------------------------------------------------------------- whisper-cli.exe
if (Test-Path (Join-Path $Target 'whisper-cli.exe')) {
    Write-Host "whisper-cli.exe ist bereits vorhanden: $Target"
} else {
    Write-Host 'Suche aktuelles whisper.cpp-Release …'
    # Nicht jedes Release enthält Windows-Binärdateien – das neueste mit whisper-bin-x64.zip nehmen.
    $releases = Invoke-RestMethod -Uri 'https://api.github.com/repos/ggml-org/whisper.cpp/releases?per_page=20' -Headers @{ 'User-Agent' = 'MarkdownEditor-Setup' }
    $release = $null; $asset = $null
    foreach ($r in $releases) {
        if ($r.prerelease -or $r.draft) { continue }
        $a = $r.assets | Where-Object { $_.name -eq 'whisper-bin-x64.zip' } | Select-Object -First 1
        if ($a) { $release = $r; $asset = $a; break }
    }
    if (-not $asset) { throw 'In den letzten Releases wurde whisper-bin-x64.zip nicht gefunden.' }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('whisper-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $tmp | Out-Null
    try {
        $zip = Join-Path $tmp 'whisper.zip'
        Write-Host "Lade $($asset.name) ($($release.tag_name)) …"
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing
        Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp 'x') -Force
        $cli = Get-ChildItem (Join-Path $tmp 'x') -Recurse -Filter 'whisper-cli.exe' | Select-Object -First 1
        if (-not $cli) { throw 'whisper-cli.exe ist nicht im Archiv enthalten.' }
        # Programm mit allen DLLs aus demselben Ordner übernehmen
        Get-ChildItem $cli.DirectoryName -File | Where-Object { $_.Extension -in '.exe', '.dll' } |
            Copy-Item -Destination $Target -Force
        Write-Host "OK: whisper.cpp $($release.tag_name) → $Target"
    } finally {
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------- Modell
$modelFile = Join-Path $models "ggml-$Model.bin"
if (Test-Path $modelFile) {
    Write-Host "Modell ist bereits vorhanden: $modelFile"
} else {
    $url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$Model.bin"
    Write-Host "Lade Modell ggml-$Model.bin (kann einige Minuten dauern) …"
    $part = "$modelFile.part"
    Invoke-WebRequest -Uri $url -OutFile $part -UseBasicParsing
    Move-Item -Force $part $modelFile
    Write-Host ("OK: {0}  ({1:N0} MB)" -f $modelFile, ((Get-Item $modelFile).Length / 1MB))
}

Write-Host ''
Write-Host 'Fertig. Diktat im Editor mit Strg+Umschalt+M starten und beenden.'
if ($Model -ne 'base') { Write-Host "Hinweis: Modell ggml-$Model.bin unter Diktat → Mikrofon / Modell / Sprache… auswählen." }
