using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MarkdownEditor.Editor
{
    public sealed class Emoji
    {
        public string Symbol;
        public string Category;
        public string Name;       // deutscher Name (Unicode CLDR)
        public string Keywords;   // deutsche + englische Suchbegriffe
        internal byte[] Png;
        ImageSource _image;
        bool _imageLoaded;

        /// <summary>Farbiges Bild (Fluent Emoji / Twemoji) oder null, falls keins vorhanden.</summary>
        public ImageSource Image
        {
            get
            {
                if (_imageLoaded) return _image;
                _imageLoaded = true;
                if (Png == null || Png.Length == 0) return null;
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = new MemoryStream(Png);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _image = bmp;
                }
                catch (Exception)
                {
                    _image = null;
                }
                return _image;
            }
        }
    }

    public sealed class EmojiCategory
    {
        public string Name;
        public string Icon;
        public readonly List<Emoji> Items = new List<Emoji>();
    }

    /// <summary>
    /// Vollständige Emoji-Auswahl (Unicode), gruppiert wie auf der Apple-Tastatur.
    /// Daten und Bilder stammen aus assets/emoji.dat (erzeugt von tools/build-emoji-assets.ps1)
    /// und sind als Ressource in die EXE eingebettet. Eingefügt wird immer das Unicode-Zeichen.
    /// </summary>
    public static class EmojiCatalog
    {
        const string ResourceName = "MarkdownEditor.emoji.dat";

        static readonly Dictionary<string, string> CategoryIcons = new Dictionary<string, string>
        {
            { "Smileys & Personen", "😀" }, { "Tiere & Natur", "🐻" }, { "Essen & Trinken", "🍔" },
            { "Aktivität", "⚽" }, { "Reisen & Orte", "🚗" }, { "Objekte", "💡" },
            { "Symbole", "❤️" }, { "Flaggen", "🏁" }
        };

        static List<EmojiCategory> _categories;
        static Dictionary<string, Emoji> _lookup;
        static int _maxLength;

        public static List<EmojiCategory> Categories
        {
            get { EnsureLoaded(); return _categories; }
        }

        public static Emoji Find(string symbol)
        {
            EnsureLoaded();
            Emoji e;
            return symbol != null && _lookup.TryGetValue(symbol, out e) ? e : null;
        }

        /// <summary>Prüft, ob an Position <paramref name="index"/> ein Emoji beginnt (längster Treffer).</summary>
        public static Emoji MatchAt(string text, int index, out int length)
        {
            length = 0;
            char c = text[index];
            // Schnelltest: Emojis beginnen mit Surrogaten, Symbolen ab U+2000 oder Tastenkappen (#, *, 0-9)
            if (c < 0x2000 && c != '#' && c != '*' && (c < '0' || c > '9') && c != 0xA9 && c != 0xAE) return null;
            EnsureLoaded();
            int max = Math.Min(_maxLength, text.Length - index);
            for (int len = max; len >= 1; len--)
            {
                Emoji e;
                if (_lookup.TryGetValue(text.Substring(index, len), out e))
                {
                    length = len;
                    return e;
                }
            }
            return null;
        }

        static void EnsureLoaded()
        {
            if (_categories != null) return;
            _categories = new List<EmojiCategory>();
            _lookup = new Dictionary<string, Emoji>();
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (s != null) Read(s);
                }
            }
            catch (Exception)
            {
                // Ohne Emoji-Daten bleibt die Auswahl leer – der Editor funktioniert weiter.
            }
        }

        static void Read(Stream s)
        {
            using (var r = new BinaryReader(s, Encoding.UTF8))
            {
                if (r.ReadString() != "MDEMOJI1") return;
                int count = r.ReadInt32();
                EmojiCategory current = null;
                for (int i = 0; i < count; i++)
                {
                    var e = new Emoji
                    {
                        Symbol = r.ReadString(),
                        Category = r.ReadString(),
                        Name = r.ReadString(),
                        Keywords = r.ReadString()
                    };
                    int len = r.ReadInt32();
                    e.Png = len > 0 ? r.ReadBytes(len) : null;

                    if (current == null || current.Name != e.Category)
                    {
                        string icon;
                        current = new EmojiCategory { Name = e.Category, Icon = CategoryIcons.TryGetValue(e.Category, out icon) ? icon : e.Symbol };
                        _categories.Add(current);
                    }
                    current.Items.Add(e);
                    Register(e.Symbol, e);

                    // Variante ohne Variationsselektor U+FE0F (wird oft so eingegeben).
                    // Nur für echte Bild-Emojis, damit z. B. © oder → als Text erhalten bleiben.
                    string plain = e.Symbol.Replace("️", "");
                    if (plain != e.Symbol && char.IsSurrogate(plain[0]))
                        Register(plain, e);
                }
            }
        }

        static void Register(string key, Emoji e)
        {
            if (_lookup.ContainsKey(key)) return;
            _lookup[key] = e;
            if (key.Length > _maxLength) _maxLength = key.Length;
        }

        /// <summary>Durchsucht Name und Schlüsselwörter (deutsch/englisch).</summary>
        public static List<Emoji> Search(string query, int max)
        {
            var result = new List<Emoji>();
            if (string.IsNullOrWhiteSpace(query)) return result;
            string[] terms = query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var nameHits = new List<Emoji>();
            var keywordHits = new List<Emoji>();
            foreach (EmojiCategory cat in Categories)
            {
                foreach (Emoji e in cat.Items)
                {
                    string name = e.Name.ToLowerInvariant();
                    string all = name + " " + e.Keywords.ToLowerInvariant();
                    bool ok = true;
                    foreach (string t in terms)
                        if (!all.Contains(t)) { ok = false; break; }
                    if (!ok) continue;
                    if (name.StartsWith(terms[0])) nameHits.Add(e);
                    else keywordHits.Add(e);
                }
            }
            result.AddRange(nameHits);
            result.AddRange(keywordHits);
            if (result.Count > max) result.RemoveRange(max, result.Count - max);
            return result;
        }
    }
}
