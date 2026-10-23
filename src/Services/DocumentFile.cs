using System;
using System.IO;
using System.Text;

namespace MarkdownEditor.Services
{
    /// <summary>Laden/Speichern von Markdown-Dateien inkl. Erkennung von Kodierung und Zeilenende.</summary>
    public sealed class DocumentFile
    {
        public string Text;          // immer mit "\n" als Zeilenende
        public string NewLine = "\r\n";
        public bool HasBom;

        public static DocumentFile Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            var doc = new DocumentFile();
            string text;

            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                doc.HasBom = true;
                text = new UTF8Encoding(false).GetString(data, 3, data.Length - 3);
            }
            else if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
            {
                using (var reader = new StreamReader(new MemoryStream(data), true))
                    text = reader.ReadToEnd();
            }
            else
            {
                try
                {
                    text = new UTF8Encoding(false, true).GetString(data);
                }
                catch (DecoderFallbackException)
                {
                    text = Encoding.Default.GetString(data); // Fallback für ältere ANSI-Dateien
                }
            }

            if (text.Contains("\r\n")) doc.NewLine = "\r\n";
            else if (text.Contains("\n")) doc.NewLine = "\n";
            doc.Text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return doc;
        }

        /// <summary>Schreibt atomar (temporäre Datei + Ersetzen), damit bei Abstürzen nichts verloren geht.</summary>
        public void Save(string path)
        {
            string content = Text.Replace("\n", NewLine);
            byte[] bytes = new UTF8Encoding(HasBom).GetPreamble();
            byte[] body = new UTF8Encoding(false).GetBytes(content);
            string temp = path + ".tmp~";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Write(body, 0, body.Length);
            }
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }
            try
            {
                File.Replace(temp, path, null);
            }
            catch (IOException)
            {
                // z. B. auf Netzlaufwerken/OneDrive nicht unterstützt
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
        }
    }
}
