# Erzeugt assets\emoji.dat: vollständige Emoji-Liste (Unicode), gruppiert nach den Kategorien der Apple-Tastatur,
# mit deutschen Namen/Suchbegriffen (Unicode CLDR) und farbigen Vorschaubildern:
#   - Microsoft Fluent Emoji 3D (MIT-Lizenz)       https://github.com/microsoft/fluentui-emoji
#   - Twemoji (CC-BY 4.0) als Ersatz, z. B. Flaggen https://github.com/jdecked/twemoji
# Wird nur einmalig bzw. bei Unicode-Updates benötigt; das Ergebnis liegt im Repository.
param([int]$Size = 48, [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\emoji.dat'))

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$cache = Join-Path $env:TEMP 'mdeditor-emoji-cache'
New-Item -ItemType Directory -Force $cache | Out-Null

function Get-Cached([string]$url, [string]$name) {
    $f = Join-Path $cache $name
    if (-not (Test-Path $f)) { Invoke-WebRequest $url -UseBasicParsing -OutFile $f -TimeoutSec 300 }
    return $f
}

Write-Host 'Lade Quelldaten ...'
$testFile = Get-Cached 'https://unicode.org/Public/emoji/latest/emoji-test.txt' 'emoji-test.txt'
$annFile  = Get-Cached 'https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-annotations-full/annotations/de/annotations.json' 'de.json'
$derFile  = Get-Cached 'https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-annotations-derived-full/annotationsDerived/de/annotations.json' 'de-derived.json'
$treeFile = Get-Cached 'https://api.github.com/repos/microsoft/fluentui-emoji/git/trees/main?recursive=1' 'fluent-tree.json'

# ------------------------------------------------------------------ Deutsche Namen
Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$ser.MaxJsonLength = [int]::MaxValue
$names = @{}
foreach ($f in @($annFile, $derFile)) {
    $json = $ser.DeserializeObject([IO.File]::ReadAllText($f, [Text.Encoding]::UTF8))
    $root = $json['annotations']; if (-not $root) { $root = $json['annotationsDerived'] }
    foreach ($k in $root['annotations'].Keys) {
        $e = $root['annotations'][$k]
        $tts = if ($e.ContainsKey('tts')) { $e['tts'][0] } else { $null }
        $kw = if ($e.ContainsKey('default')) { ($e['default'] -join ' ') } else { '' }
        $names[$k] = @($tts, $kw)
    }
}

# ------------------------------------------------------------------ Fluent-Pfade
$tree = $ser.DeserializeObject([IO.File]::ReadAllText($treeFile, [Text.Encoding]::UTF8))
$fluent = @{}
foreach ($t in $tree['tree']) {
    $p = $t['path']
    if ($p -notmatch '^assets/([^/]+)/(Default/)?3D/[^/]+\.png$') { continue }
    $key = ($Matches[1].ToLowerInvariant() -replace '[^a-z0-9]', '')
    if (-not $fluent.ContainsKey($key) -or $Matches[2]) { $fluent[$key] = $p }
}

# ------------------------------------------------------------------ Emoji-Liste (Apple-Kategorien)
# Umlaut als Zeichencode: Windows PowerShell 5.1 liest Skripte ohne BOM als ANSI
$activity = 'Aktivit' + [char]0x00E4 + 't'
$catMap = @{
    'Smileys & Emotion' = 'Smileys & Personen'; 'People & Body' = 'Smileys & Personen'
    'Animals & Nature' = 'Tiere & Natur'; 'Food & Drink' = 'Essen & Trinken'; 'Activities' = $activity
    'Travel & Places' = 'Reisen & Orte'; 'Objects' = 'Objekte'; 'Symbols' = 'Symbole'; 'Flags' = 'Flaggen'
}
$catOrder = 'Smileys & Personen', 'Tiere & Natur', 'Essen & Trinken', $activity, 'Reisen & Orte', 'Objekte', 'Symbole', 'Flaggen'
$items = New-Object System.Collections.ArrayList
$group = $null
foreach ($line in [IO.File]::ReadAllLines($testFile, [Text.Encoding]::UTF8)) {
    if ($line -match '^# group: (.+)$') { $group = $Matches[1].Trim(); continue }
    if ($line -notmatch '^([0-9A-F ]+?)\s*;\s*fully-qualified\s*#\s*\S+\s+E[\d.]+\s+(.+)$') { continue }
    if (-not $catMap.ContainsKey($group)) { continue }
    $cps = $Matches[1].Trim() -split ' '
    if ($cps | Where-Object { $_ -match '^1F3F[B-F]$' }) { continue }   # Hauttöne: wie bei Apple nur Grundform
    $en = $Matches[2].Trim()
    $chars = -join ($cps | ForEach-Object { [char]::ConvertFromUtf32([Convert]::ToInt32($_, 16)) })
    $plain = $chars.Replace([string][char]0xFE0F, '')
    $de = $names[$chars]; if (-not $de) { $de = $names[$plain] }
    $deName = if ($de -and $de[0]) { $de[0] } else { $en }
    $kw = if ($de) { $de[1] } else { '' }
    $fkey = ($en.ToLowerInvariant() -replace '[^a-z0-9]', '')
    $furl = if ($fluent.ContainsKey($fkey)) { 'https://cdn.jsdelivr.net/gh/microsoft/fluentui-emoji@main/' + ($fluent[$fkey] -replace ' ', '%20') } else { $null }
    $tw = (($cps | Where-Object { $_ -ne 'FE0F' -or $cps.Count -gt 2 } | ForEach-Object { $_.ToLowerInvariant() }) -join '-')
    [void]$items.Add([pscustomobject]@{
        Chars = $chars; Cat = $catMap[$group]; Name = $deName; Keywords = ($kw + ' ' + $en).Trim()
        Url = $furl; Fallback = "https://cdn.jsdelivr.net/gh/jdecked/twemoji@latest/assets/72x72/$tw.png"; Png = $null
    })
}
$sorted = foreach ($c in $catOrder) { $items | Where-Object { $_.Cat -eq $c } }
Write-Host ("{0} Emojis, davon {1} mit Fluent-Bild" -f $items.Count, ($items | Where-Object Url).Count)

# ------------------------------------------------------------------ Bilder laden + verkleinern (parallel)
Add-Type -ReferencedAssemblies System.Drawing, System.Net.Http -TypeDefinition @"
using System; using System.IO; using System.Net.Http; using System.Threading; using System.Threading.Tasks;
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
public static class EmojiFetcher {
    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    public static byte[][] FetchAll(string[] urls, string[] fallbacks, string cacheDir, int size, int parallel) {
        var result = new byte[urls.Length][];
        var gate = new SemaphoreSlim(parallel);
        var tasks = new Task[urls.Length];
        for (int i = 0; i < urls.Length; i++) {
            int idx = i;
            tasks[i] = Task.Run(async () => {
                await gate.WaitAsync();
                try {
                    byte[] raw = await Get(urls[idx], cacheDir) ?? await Get(fallbacks[idx], cacheDir);
                    if (raw != null) result[idx] = Resize(raw, size);
                } finally { gate.Release(); }
            });
        }
        Task.WaitAll(tasks);
        return result;
    }
    static async Task<byte[]> Get(string url, string cacheDir) {
        if (string.IsNullOrEmpty(url)) return null;
        string file = Path.Combine(cacheDir, ((uint)url.GetHashCode()).ToString("x8") + "_" + Path.GetFileName(Uri.UnescapeDataString(url)));
        if (File.Exists(file)) return File.ReadAllBytes(file);
        for (int attempt = 0; attempt < 3; attempt++) {
            bool failed = false;
            try {
                var resp = await Http.GetAsync(url);
                if ((int)resp.StatusCode == 404) return null;
                resp.EnsureSuccessStatusCode();
                byte[] data = await resp.Content.ReadAsByteArrayAsync();
                File.WriteAllBytes(file, data);
                return data;
            } catch { failed = true; }
            if (failed) await Task.Delay(1000);
        }
        return null;
    }
    static byte[] Resize(byte[] raw, int size) {
        using (var src = new Bitmap(new MemoryStream(raw)))
        using (var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb)) {
            using (var g = Graphics.FromImage(dst)) {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, size, size));
            }
            var ms = new MemoryStream();
            dst.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }
}
"@
Write-Host 'Lade und verkleinere Bilder (einmalig, wird zwischengespeichert) ...'
$urls = [string[]]($sorted | ForEach-Object { if ($_.Url) { $_.Url } else { '' } })
$fbs = [string[]]($sorted | ForEach-Object { $_.Fallback })
$pngs = [EmojiFetcher]::FetchAll($urls, $fbs, $cache, $Size, 16)

# ------------------------------------------------------------------ Datei schreiben
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
$fs = [IO.File]::Create($Out)
$w = New-Object IO.BinaryWriter($fs, [Text.Encoding]::UTF8)
$w.Write('MDEMOJI1'); $w.Write([int]$sorted.Count)
$missing = 0
for ($i = 0; $i -lt $sorted.Count; $i++) {
    $e = $sorted[$i]
    $w.Write($e.Chars); $w.Write($e.Cat); $w.Write($e.Name); $w.Write($e.Keywords)
    $png = $pngs[$i]
    if ($png) { $w.Write([int]$png.Length); $w.Write($png) } else { $w.Write([int]0); $missing++ }
}
$w.Close()
Write-Host ("Fertig: {0} ({1:N0} KB), ohne Bild: {2}" -f $Out, ((Get-Item $Out).Length / 1KB), $missing)
