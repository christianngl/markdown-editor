# Baut MarkdownEditor.exe mit dem in Windows enthaltenen C#-Compiler (.NET Framework 4.8).
# Keine Installation nötig.  Aufruf:  powershell -ExecutionPolicy Bypass -File .\build.ps1 [-Run]
param([switch]$Run)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $fw 'csc.exe'))) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc = Join-Path $fw 'csc.exe'
$wpf = Join-Path $fw 'WPF'

$out = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

$icon = Join-Path $root 'assets\app.ico'
if (-not (Test-Path $icon)) { & (Join-Path $root 'tools\make-icon.ps1') -Out $icon }

$refs = @(
    "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll",
    "$fw\System.Xaml.dll", "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Data.dll", "$fw\System.Xml.dll",
    "$fw\System.Runtime.Serialization.dll"
)
$sources = Get-ChildItem -Path (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }

$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/platform:anycpu',
             "/out:$out\MarkdownEditor.exe", "/win32manifest:$root\app.manifest")
if (Test-Path $icon) { $cscArgs += "/win32icon:$icon" }
$emoji = Join-Path $root 'assets\emoji.dat'
if (-not (Test-Path $emoji)) { & (Join-Path $root 'tools\build-emoji-assets.ps1') -Out $emoji }
$cscArgs += "/resource:$emoji,MarkdownEditor.emoji.dat"
$cscArgs += $refs | ForEach-Object { "/r:$_" }
$cscArgs += $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exit-Code $LASTEXITCODE)" }

$exe = Get-Item "$out\MarkdownEditor.exe"
Write-Host ("OK: {0}  ({1:N0} KB)" -f $exe.FullName, ($exe.Length / 1KB))
if ($Run) { Start-Process $exe.FullName }
