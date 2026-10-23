using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MarkdownEditor.Services
{
    /// <summary>Eine mit dem Programm erstellte, geöffnete oder gespeicherte Datei.</summary>
    public sealed class KnownFile
    {
        public string Path;
        public DateTime LastUsed;
    }

    /// <summary>Einfache Einstellungen als INI-Datei unter %APPDATA%\MarkdownEditor\settings.ini.</summary>
    public sealed class AppSettings
    {
        const int MaxKnownFiles = 500;
        const int MaxRecentEmojis = 32;

        public string Theme = "Minimal Light";
        public string LastLightTheme = "Minimal Light";
        public string LastDarkTheme = "Minimal Dark";
        public bool AutoSave;
        public bool ShowSidebar = true;
        /// <summary>Ansicht: "Both" (Markdown + Vorschau), "Markdown" oder "Preview".</summary>
        public string ViewMode = "Both";
        public bool SpellCheck;
        public double FontSize = 15;
        public double Width = 1180;
        public double Height = 780;
        public bool Maximized;
        public string LastFolder;

        /// <summary>Claude-API-Schlüssel (in der Datei per DPAPI verschlüsselt).</summary>
        public string ApiKey;
        public string AiModel = DefaultAiModel;
        public const string DefaultAiModel = "claude-sonnet-5-5";

        /// <summary>Diktat (lokal mit whisper.cpp): Mikrofon (−1 = Standardgerät), Modell, Sprache, Sprachbefehle.</summary>
        public int DictationDevice = -1;
        public string DictationModel = WhisperTranscriber.DefaultModel;
        public string DictationLanguage = "de";
        public bool DictationVoiceCommands = true;

        /// <summary>Gespeicherter Schlüssel, sonst die Umgebungsvariable ANTHROPIC_API_KEY.</summary>
        public string EffectiveApiKey
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ApiKey)) return ApiKey;
                return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            }
        }

        /// <summary>Alle bekannten Dateien, zuletzt verwendete zuerst.</summary>
        public readonly List<KnownFile> KnownFiles = new List<KnownFile>();

        /// <summary>Zuletzt eingefügte Emojis (für „Häufig verwendet“).</summary>
        public readonly List<string> RecentEmojis = new List<string>();

        static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarkdownEditor", "settings.ini");
            }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string value = raw.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "Theme": s.Theme = value; break;
                        case "LastLightTheme": s.LastLightTheme = value; break;
                        case "LastDarkTheme": s.LastDarkTheme = value; break;
                        case "AutoSave": s.AutoSave = value == "1"; break;
                        case "ShowSidebar": s.ShowSidebar = value == "1"; break;
                        case "ShowPreview": s.ViewMode = value == "1" ? "Both" : "Markdown"; break; // ältere Versionen
                        case "ViewMode": if (value.Length > 0) s.ViewMode = value; break;
                        case "SpellCheck": s.SpellCheck = value == "1"; break;
                        case "FontSize": s.FontSize = ParseDouble(value, s.FontSize); break;
                        case "Width": s.Width = ParseDouble(value, s.Width); break;
                        case "Height": s.Height = ParseDouble(value, s.Height); break;
                        case "Maximized": s.Maximized = value == "1"; break;
                        case "LastFolder": s.LastFolder = value; break;
                        case "ApiKey": s.ApiKey = Dpapi.Unprotect(value); break;
                        case "AiModel": if (value.Length > 0) s.AiModel = value; break;
                        case "DictationDevice": { int d; if (int.TryParse(value, out d)) s.DictationDevice = d; break; }
                        case "DictationModel": if (value.Length > 0) s.DictationModel = value; break;
                        case "DictationLanguage": if (value.Length > 0) s.DictationLanguage = value; break;
                        case "DictationVoiceCommands": s.DictationVoiceCommands = value == "1"; break;
                        case "Emoji": if (value.Length > 0) s.RecentEmojis.Add(value); break;
                        case "Recent": // Format der ersten Version
                            if (value.Length > 0) s.AddKnown(value, DateTime.Now.AddMinutes(-s.KnownFiles.Count), false);
                            break;
                        case "File":
                        {
                            int bar = value.IndexOf('|');
                            long ticks;
                            if (bar > 0 && long.TryParse(value.Substring(0, bar), out ticks))
                                s.AddKnown(value.Substring(bar + 1), new DateTime(ticks), false);
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Defekte Einstellungen ignorieren – Standardwerte verwenden.
            }
            return s;
        }

        /// <summary>Im Selbsttest werden keine Einstellungen geschrieben.</summary>
        public static bool ReadOnly;

        public void Save()
        {
            if (ReadOnly) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var sb = new StringBuilder();
                sb.AppendLine("Theme=" + Theme);
                sb.AppendLine("LastLightTheme=" + LastLightTheme);
                sb.AppendLine("LastDarkTheme=" + LastDarkTheme);
                sb.AppendLine("AutoSave=" + B(AutoSave));
                sb.AppendLine("ShowSidebar=" + B(ShowSidebar));
                sb.AppendLine("ViewMode=" + (ViewMode ?? "Both"));
                sb.AppendLine("SpellCheck=" + B(SpellCheck));
                sb.AppendLine("FontSize=" + FontSize.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Width=" + Width.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Height=" + Height.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Maximized=" + B(Maximized));
                sb.AppendLine("LastFolder=" + (LastFolder ?? ""));
                sb.AppendLine("AiModel=" + (AiModel ?? DefaultAiModel));
                sb.AppendLine("DictationDevice=" + DictationDevice.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("DictationModel=" + (DictationModel ?? WhisperTranscriber.DefaultModel));
                sb.AppendLine("DictationLanguage=" + (DictationLanguage ?? "de"));
                sb.AppendLine("DictationVoiceCommands=" + B(DictationVoiceCommands));
                if (!string.IsNullOrEmpty(ApiKey))
                {
                    string protectedKey = Dpapi.Protect(ApiKey);
                    if (!string.IsNullOrEmpty(protectedKey)) sb.AppendLine("ApiKey=" + protectedKey);
                }
                foreach (KnownFile f in KnownFiles) sb.AppendLine("File=" + f.LastUsed.Ticks + "|" + f.Path);
                foreach (string e in RecentEmojis) sb.AppendLine("Emoji=" + e);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // Einstellungen sind nicht kritisch.
            }
        }

        // ---------------------------------------------------------------- Bekannte Dateien

        /// <summary>Merkt sich den Ablageort einer erstellten, geöffneten oder gespeicherten Datei.</summary>
        public void TouchFile(string path)
        {
            AddKnown(path, DateTime.Now, true);
        }

        void AddKnown(string path, DateTime when, bool toFront)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            KnownFile existing = KnownFiles.Find(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!toFront) return;
                KnownFiles.Remove(existing);
            }
            var entry = new KnownFile { Path = path, LastUsed = when };
            if (toFront) KnownFiles.Insert(0, entry);
            else KnownFiles.Add(entry);
            while (KnownFiles.Count > MaxKnownFiles) KnownFiles.RemoveAt(KnownFiles.Count - 1);
        }

        public void ForgetFile(string path)
        {
            KnownFiles.RemoveAll(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Entfernt Verknüpfungen zu Dateien, die am gespeicherten Ort nicht mehr existieren.</summary>
        public int PruneMissingFiles()
        {
            return KnownFiles.RemoveAll(f => !SafeExists(f.Path));
        }

        static bool SafeExists(string path)
        {
            try { return File.Exists(path); }
            catch (Exception) { return false; }
        }

        // ---------------------------------------------------------------- Emojis

        public void TouchEmoji(string emoji)
        {
            RecentEmojis.Remove(emoji);
            RecentEmojis.Insert(0, emoji);
            while (RecentEmojis.Count > MaxRecentEmojis) RecentEmojis.RemoveAt(RecentEmojis.Count - 1);
        }

        static string B(bool v) { return v ? "1" : "0"; }

        static double ParseDouble(string s, double fallback)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : fallback;
        }
    }
}
