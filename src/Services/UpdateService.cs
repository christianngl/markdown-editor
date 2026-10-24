using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace MarkdownEditor.Services
{
    /// <summary>Ein veröffentlichtes GitHub-Release mit der angehängten MarkdownEditor.exe.</summary>
    public sealed class ReleaseInfo
    {
        public Version Version;
        public string Tag;
        public string Notes;
        public string PageUrl;
        public string AssetUrl;     // API-URL der EXE (lädt mit Accept: application/octet-stream)
        public string AssetDigest;  // "sha256:…", falls GitHub ihn liefert
        public long AssetSize;
    }

    /// <summary>
    /// Sucht auf GitHub nach einem neueren Release, lädt dessen EXE und tauscht sie nach dem Beenden aus.
    /// Das Repo ist privat: Zugriff über GH_TOKEN/GITHUB_TOKEN oder das Token der angemeldeten gh-CLI.
    /// </summary>
    public static class UpdateService
    {
        public const string Repo = "christianngl/markdown-editor";
        const string AssetName = "MarkdownEditor.exe";
        const string UserAgent = "MarkdownEditor-Updater";

        /// <summary>Version dieser EXE (build.ps1 übernimmt sie aus dem neuesten Abschnitt in CHANGELOG.md).</summary>
        public static Version CurrentVersion
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        public static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string s = tag.TrimStart('v', 'V');
            int dash = s.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0) s = s.Substring(0, dash);
            Version v;
            if (!Version.TryParse(s, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        /// <summary>Neuestes Release; null, wenn keins veröffentlicht ist. Wirft bei Netz- oder Zugriffsfehlern.</summary>
        public static ReleaseInfo FetchLatest()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest req = Request("https://api.github.com/repos/" + Repo + "/releases/latest", "application/vnd.github+json", true);
            GhRelease r;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                    r = (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(s);
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                {
                    if (Token() == null)
                        throw new InvalidOperationException("Kein Zugriff auf das private Repository " + Repo +
                            ".\nMit der GitHub-CLI anmelden (gh auth login) oder GH_TOKEN setzen.");
                    return null; // angemeldet, aber noch kein Release
                }
                throw new InvalidOperationException("GitHub nicht erreichbar: " + ex.Message);
            }

            var info = new ReleaseInfo { Version = ParseTag(r.tag_name), Tag = r.tag_name, Notes = r.body ?? "", PageUrl = r.html_url };
            if (r.assets != null)
                foreach (GhAsset a in r.assets)
                    if (string.Equals(a.name, AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        info.AssetUrl = a.url;
                        info.AssetDigest = a.digest;
                        info.AssetSize = a.size;
                    }
            return info.Version == null ? null : info;
        }

        public static bool IsNewer(ReleaseInfo r)
        {
            return r != null && r.Version != null && r.Version > CurrentVersion;
        }

        /// <summary>Lädt die EXE des Releases in einen Temp-Ordner und prüft Größe und Prüfsumme.</summary>
        public static string Download(ReleaseInfo r)
        {
            if (r.AssetUrl == null) throw new InvalidOperationException("Das Release " + r.Tag + " enthält keine " + AssetName + ".");
            string dir = Path.Combine(Path.GetTempPath(), "MarkdownEditor", "update");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, AssetName);

            // GitHub leitet auf einen signierten Speicher-Link um; der darf das Token nicht mitbekommen.
            HttpWebRequest req = Request(r.AssetUrl, "application/octet-stream", true);
            req.AllowAutoRedirect = false;
            HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
            int code = (int)resp.StatusCode;
            if (code >= 300 && code < 400)
            {
                string location = resp.Headers["Location"];
                resp.Close();
                resp = (HttpWebResponse)Request(location, "application/octet-stream", false).GetResponse();
            }
            using (resp)
            using (Stream src = resp.GetResponseStream())
            using (FileStream dst = File.Create(file))
                src.CopyTo(dst);

            var fi = new FileInfo(file);
            if (fi.Length < 64 * 1024 || (r.AssetSize > 0 && fi.Length != r.AssetSize))
                throw new InvalidOperationException("Download unvollständig (" + fi.Length + " Bytes).");
            using (FileStream fs = File.OpenRead(file))
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z')
                    throw new InvalidOperationException("Die heruntergeladene Datei ist keine Windows-Programmdatei.");
            if (!string.IsNullOrEmpty(r.AssetDigest) && r.AssetDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                string want = r.AssetDigest.Substring(7);
                if (!string.Equals(Sha256(file), want, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Prüfsumme der heruntergeladenen Datei stimmt nicht.");
            }
            return file;
        }

        /// <summary>
        /// Startet ein unsichtbares Hilfsskript, das auf das Ende dieses Prozesses wartet, die EXE ersetzt
        /// (die alte bleibt als .old liegen) und den Editor neu startet. Danach muss die Anwendung sich beenden.
        /// </summary>
        public static void ScheduleReplace(string newExe, string reopenFile)
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string script = Path.Combine(Path.GetDirectoryName(newExe), "update.ps1");
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("try { Wait-Process -Id " + Process.GetCurrentProcess().Id + " -Timeout 60 } catch { }");
            sb.AppendLine("$exe = " + Ps(exe) + "; $neu = " + Ps(newExe));
            sb.AppendLine("$ok = $false");
            sb.AppendLine("for ($i = 0; $i -lt 20 -and -not $ok; $i++) {");
            sb.AppendLine("    try { Copy-Item $exe ($exe + '.old') -Force; Copy-Item $neu $exe -Force; $ok = $true } catch { Start-Sleep -Milliseconds 500 }");
            sb.AppendLine("}");
            sb.AppendLine("if (-not $ok) { Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show(\"Update fehlgeschlagen: $exe ist schreibgeschützt oder noch in Benutzung.\", 'Markdown Editor') | Out-Null }");
            if (reopenFile != null) sb.AppendLine("Start-Process $exe -ArgumentList ('\"' + " + Ps(reopenFile) + " + '\"')");
            else sb.AppendLine("Start-Process $exe");
            File.WriteAllText(script, sb.ToString(), new UTF8Encoding(true));

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        // ---------------------------------------------------------------- Hilfsfunktionen

        static HttpWebRequest Request(string url, string accept, bool auth)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Accept = accept;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 60000;
            string token = auth ? Token() : null;
            if (token != null) req.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            return req;
        }

        static string _token;
        static bool _tokenLoaded;

        static string Token()
        {
            if (_tokenLoaded) return _token;
            _tokenLoaded = true;
            foreach (string name in new[] { "GH_TOKEN", "GITHUB_TOKEN" })
            {
                string t = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(t)) return _token = t.Trim();
            }
            foreach (string gh in new[] { "gh.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe") })
            {
                try
                {
                    var psi = new ProcessStartInfo(gh, "auth token -h github.com")
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    using (Process p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd().Trim();
                        if (!p.WaitForExit(10000)) { try { p.Kill(); } catch (Exception) { } continue; }
                        if (p.ExitCode == 0 && output.Length > 0) return _token = output;
                    }
                }
                catch (Exception)
                {
                    // gh nicht installiert oder nicht im PATH
                }
            }
            return null;
        }

        static string Sha256(string file)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        static string Ps(string s)
        {
            return "'" + s.Replace("'", "''") + "'";
        }

        [DataContract]
        sealed class GhRelease
        {
            [DataMember] public string tag_name = null;
            [DataMember] public string body = null;
            [DataMember] public string html_url = null;
            [DataMember] public GhAsset[] assets = null;
        }

        [DataContract]
        sealed class GhAsset
        {
            [DataMember] public string name = null;
            [DataMember] public string url = null;
            [DataMember] public string digest = null;
            [DataMember] public long size = 0;
        }
    }
}
