# Registriert den Markdown Editor fuer Textformate (nur aktueller Benutzer, keine Adminrechte noetig)
# und raeumt Eintraege alter Versionen auf, deren EXE nicht mehr existiert.
# Aufruf:  powershell -ExecutionPolicy Bypass -File .\tools\register-file-types.ps1
# Danach oeffnet sich "Standard-Apps", dort einmal "Als Standard festlegen" klicken
# (Windows laesst die Standard-App nur vom Benutzer selbst setzen).
param([string[]]$Extensions = @('.md', '.markdown', '.mdown', '.mkd', '.mkdn', '.txt'),
      [switch]$NoSettings)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\MarkdownEditor.exe'
if (-not (Test-Path $exe)) { & (Join-Path $root 'build.ps1') | Out-Null }
$exe = (Resolve-Path $exe).Path
$open = "`"$exe`" `"%1`""
$classes = 'HKCU:\Software\Classes'
$progId = 'MarkdownEditor.Document'
$appName = 'MarkdownEditor'

function Set-Default([string]$path, [string]$value) {
    New-Item -Path $path -Force | Out-Null
    Set-ItemProperty -LiteralPath $path -Name '(default)' -Value $value
}

# Pfad aus einer Befehlszeile ("C:\...\x.exe" "%1") herausloesen
function Get-CommandExe([string]$cmd) {
    if ($cmd -match '^\s*"([^"]+)"') { return $Matches[1] }
    if ($cmd -match '^\s*(\S+)') { return $Matches[1] }
}

# 1. Veraltete Eintraege entfernen: ProgIds, deren open-Befehl auf eine fehlende MarkdownEditor.exe zeigt
Get-ChildItem $classes -ErrorAction SilentlyContinue | ForEach-Object {
    $cmdKey = Join-Path $_.PSPath 'shell\open\command'
    $cmd = (Get-ItemProperty -LiteralPath $cmdKey -ErrorAction SilentlyContinue).'(default)'
    if ($cmd -and $cmd -match 'MarkdownEditor\.exe') {
        $target = Get-CommandExe $cmd
        if ($target -ne $exe -and $_.PSChildName -ne $progId) {
            Write-Host "Entferne veralteten Eintrag $($_.PSChildName) -> $target"
            Remove-Item -LiteralPath $_.PSPath -Recurse -Force
            foreach ($ext in $Extensions) {
                Remove-ItemProperty -LiteralPath "$classes\$ext\OpenWithProgids" -Name $_.PSChildName -ErrorAction SilentlyContinue
                Remove-ItemProperty -LiteralPath "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$ext\OpenWithProgids" -Name $_.PSChildName -ErrorAction SilentlyContinue
            }
        }
    }
}

# Anzeigenamen alter EXE-Pfade aus dem Shell-Cache loeschen (sonst tauchen sie im "Oeffnen mit"-Dialog auf)
$mui = 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache'
(Get-ItemProperty $mui).PSObject.Properties | Where-Object { $_.Name -match 'MarkdownEditor\.exe' } | ForEach-Object {
    $p = $_.Name -replace '\.(FriendlyAppName|ApplicationCompany)$', ''
    if ($p -ne $exe -and -not (Test-Path $p)) {
        Write-Host "Entferne Cache-Eintrag $p"
        Remove-ItemProperty -LiteralPath $mui -Name $_.Name
    }
}

# 2. Applications\MarkdownEditor.exe auf die aktuelle EXE setzen. Windows fuehrt alle Versionen unter diesem
#    Namen, und eine bestehende Standard-App-Wahl (UserChoice) verweist darauf; so bleibt sie gueltig.
$appKey = "$classes\Applications\MarkdownEditor.exe"
Set-Default "$appKey\shell\open\command" $open
Set-ItemProperty -LiteralPath $appKey -Name 'FriendlyAppName' -Value 'Markdown Editor'
New-Item "$appKey\SupportedTypes" -Force | Out-Null
foreach ($ext in $Extensions) { Set-ItemProperty -LiteralPath "$appKey\SupportedTypes" -Name $ext -Value '' }

# 3. Eigene ProgId und Capabilities, damit der Editor unter Einstellungen > Standard-Apps erscheint
Set-Default "$classes\$progId" 'Markdown-Dokument'
Set-Default "$classes\$progId\DefaultIcon" "`"$exe`",0"
Set-Default "$classes\$progId\shell\open\command" $open
$cap = "HKCU:\Software\$appName\Capabilities"
New-Item "$cap\FileAssociations" -Force | Out-Null
Set-ItemProperty -LiteralPath $cap -Name 'ApplicationName' -Value 'Markdown Editor'
Set-ItemProperty -LiteralPath $cap -Name 'ApplicationDescription' -Value 'Markdown-Editor mit bearbeitbarer Vorschau'
foreach ($ext in $Extensions) {
    Set-ItemProperty -LiteralPath "$cap\FileAssociations" -Name $ext -Value $progId
    New-Item "$classes\$ext\OpenWithProgids" -Force | Out-Null
    New-ItemProperty -LiteralPath "$classes\$ext\OpenWithProgids" -Name $progId -PropertyType None -Value ([byte[]]@()) -Force | Out-Null
}
New-Item 'HKCU:\Software\RegisteredApplications' -Force | Out-Null
Set-ItemProperty -LiteralPath 'HKCU:\Software\RegisteredApplications' -Name $appName -Value "Software\$appName\Capabilities"

# Explorer ueber die geaenderten Zuordnungen informieren
Add-Type -Namespace Win32 -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);'
[Win32.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Registriert: $exe"
if (-not $NoSettings) { Start-Process "ms-settings:defaultapps?registeredAppUser=$appName" }
