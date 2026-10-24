# Startet den Markdown Editor und haelt ihn aktuell (Ziel der Desktop-Verknuepfung):
# 1. holt neue Commits von GitHub, aber nur bei sauberem Arbeitsstand (git pull --ff-only, max. 20 s)
# 2. baut bin\MarkdownEditor.exe neu, wenn Quellcode, Assets, build.ps1 oder csproj neuer sind als die exe
# 3. startet die exe (Argumente, z. B. eine .md-Datei, werden durchgereicht)
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$exe = Join-Path $root 'bin\MarkdownEditor.exe'

if ((Test-Path (Join-Path $root '.git')) -and -not (git -C $root status --porcelain 2>$null)) {
    $env:GIT_TERMINAL_PROMPT = '0'
    $p = Start-Process git -ArgumentList '-C', "`"$root`"", 'pull', '--ff-only', '-q' -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(20000)) { try { $p.Kill() } catch { } }
}

$quellen = @(Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'assets') -Recurse -File -ErrorAction SilentlyContinue) +
           @(Get-Item (Join-Path $root 'build.ps1'), (Join-Path $root 'app.manifest'), (Join-Path $root 'MarkdownEditor.csproj'), (Join-Path $root 'CHANGELOG.md') -ErrorAction SilentlyContinue)
$neueste = ($quellen | Measure-Object LastWriteTime -Maximum).Maximum
if (-not (Test-Path $exe) -or $neueste -gt (Get-Item $exe).LastWriteTime) {
    try { & (Join-Path $root 'build.ps1') | Out-Null } catch { }   # schlaegt fehl, wenn der Editor gerade laeuft: dann alte exe starten
}

if (Test-Path $exe) {
    if ($args.Count) { Start-Process $exe -ArgumentList ($args | ForEach-Object { "`"$_`"" }) } else { Start-Process $exe }
}
