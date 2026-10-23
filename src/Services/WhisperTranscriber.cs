using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownEditor.Services
{
    /// <summary>
    /// Lokale Spracherkennung mit whisper.cpp (Open Source, MIT-Lizenz).
    /// Erwartet whisper-cli.exe samt DLLs in "whisper\" und die Modelle in "whisper\models\" neben der EXE.
    /// Es werden keine Daten über das Netzwerk gesendet.
    /// </summary>
    public static class WhisperTranscriber
    {
        public const string DefaultModel = "ggml-base.bin";
        public const string SetupHint = "Einmalig einrichten:  powershell -ExecutionPolicy Bypass -File .\\tools\\setup-whisper.ps1";

        /// <summary>Ordner "whisper" neben der EXE (beim SDK-Build auch in übergeordneten Ordnern gesucht).</summary>
        public static string WhisperDir
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 5 && dir != null; i++)
                {
                    string candidate = Path.Combine(dir, "whisper");
                    if (Directory.Exists(candidate)) return candidate;
                    if (Directory.Exists(Path.Combine(dir, "bin", "whisper"))) return Path.Combine(dir, "bin", "whisper");
                    dir = Path.GetDirectoryName(dir.TrimEnd('\\'));
                }
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper");
            }
        }

        public static string ModelsDir { get { return Path.Combine(WhisperDir, "models"); } }

        static string ExePath
        {
            get
            {
                string exe = Path.Combine(WhisperDir, "whisper-cli.exe");
                if (!File.Exists(exe) && File.Exists(Path.Combine(WhisperDir, "main.exe"))) exe = Path.Combine(WhisperDir, "main.exe"); // ältere Releases
                return exe;
            }
        }

        /// <summary>Alle vorhandenen Modelle (ggml-*.bin).</summary>
        public static string[] AvailableModels()
        {
            try
            {
                if (!Directory.Exists(ModelsDir)) return new string[0];
                string[] files = Directory.GetFiles(ModelsDir, "ggml-*.bin");
                for (int i = 0; i < files.Length; i++) files[i] = Path.GetFileName(files[i]);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files;
            }
            catch (Exception) { return new string[0]; }
        }

        public static bool IsInstalled(string model, out string problem)
        {
            problem = null;
            if (!File.Exists(ExePath))
                problem = "Die lokale Spracherkennung (whisper.cpp) ist noch nicht eingerichtet.\n\nErwartet: " + Path.Combine(WhisperDir, "whisper-cli.exe");
            else if (!File.Exists(Path.Combine(ModelsDir, model ?? DefaultModel)))
                problem = "Das Sprachmodell fehlt.\n\nErwartet: " + Path.Combine(ModelsDir, model ?? DefaultModel);
            if (problem == null) return true;
            problem += "\n\n" + SetupHint + "\n\nAlternativ whisper-cli.exe (mit DLLs) und ein ggml-Modell manuell in diesen Ordner kopieren.";
            return false;
        }

        /// <summary>Transkribiert eine WAV-Datei (16 kHz mono). <paramref name="language"/>: "de", "en" oder "auto".</summary>
        public static Task<string> TranscribeAsync(string wavPath, string model, string language, CancellationToken ct)
        {
            return Task.Run(delegate { return Transcribe(wavPath, model, language, ct); }, ct);
        }

        static string Transcribe(string wavPath, string model, string language, CancellationToken ct)
        {
            string outBase = Path.Combine(Path.GetDirectoryName(wavPath), Path.GetFileNameWithoutExtension(wavPath));
            string txt = outBase + ".txt";
            int threads = Math.Max(2, Math.Min(8, Environment.ProcessorCount / 2));
            var psi = new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = "-m " + Quote(Path.Combine(ModelsDir, model)) + " -f " + Quote(wavPath) +
                            " -l " + (string.IsNullOrEmpty(language) ? "de" : language) +
                            " -t " + threads + " -nt -np -otxt -of " + Quote(outBase),
                WorkingDirectory = WhisperDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var stderr = new StringBuilder();
            try
            {
                using (var p = new Process { StartInfo = psi })
                {
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
                    p.OutputDataReceived += delegate { };
                    p.Start();
                    p.BeginErrorReadLine();
                    p.BeginOutputReadLine();
                    while (!p.WaitForExit(100))
                    {
                        if (!ct.IsCancellationRequested) continue;
                        try { p.Kill(); } catch (Exception) { }
                        p.WaitForExit(2000);
                        ct.ThrowIfCancellationRequested();
                    }
                    p.WaitForExit(); // gepufferte Ausgaben abschließen
                    if (p.ExitCode != 0 || !File.Exists(txt))
                    {
                        string err;
                        lock (stderr) err = stderr.ToString().Trim();
                        if (err.Length > 600) err = "…" + err.Substring(err.Length - 600);
                        throw new InvalidOperationException("whisper.cpp ist mit Code " + p.ExitCode + " beendet worden." + (err.Length > 0 ? "\n\n" + err : ""));
                    }
                }
                return File.ReadAllText(txt, Encoding.UTF8);
            }
            finally
            {
                TryDelete(txt);
            }
        }

        static string Quote(string s) { return "\"" + s + "\""; }

        public static void TryDelete(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        /// <summary>Temporärer Ordner für Aufnahmen: %TEMP%\MarkdownEditor.</summary>
        public static string NewTempWavPath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "MarkdownEditor");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "diktat-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".wav");
        }
    }
}
