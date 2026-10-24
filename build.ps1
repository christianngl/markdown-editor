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
$sources = @(Get-ChildItem -Path (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName })

# Programmversion = neuester Versionsabschnitt in CHANGELOG.md (die Update-Suche vergleicht sie mit dem neuesten GitHub-Release)
$version = '0.0.0'
$changelog = Join-Path $root 'CHANGELOG.md'
if (Test-Path $changelog) {
    $m = Select-String -Path $changelog -Pattern '^## \[(\d+\.\d+\.\d+)\]' | Select-Object -First 1
    if ($m) { $version = $m.Matches[0].Groups[1].Value }
}
$obj = Join-Path $root 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null
$versionFile = Join-Path $obj 'AppVersion.g.cs'
Set-Content -Path $versionFile -Encoding UTF8 -Value @(
    "[assembly: System.Reflection.AssemblyVersion(""$version.0"")]",
    "[assembly: System.Reflection.AssemblyFileVersion(""$version.0"")]",
    "[assembly: System.Reflection.AssemblyInformationalVersion(""$version"")]",
    "[assembly: System.Reflection.AssemblyProduct(""Markdown Editor"")]")
$sources += $versionFile

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
Write-Host ("OK: {0}  ({1:N0} KB, Version {2})" -f $exe.FullName, ($exe.Length / 1KB), $version)
if ($Run) { Start-Process $exe.FullName }
