using System.Collections.Generic;
using System.Windows.Documents;
using MarkdownEditor.Themes;

namespace MarkdownEditor.Markdown
{
    public enum BlockKind { Paragraph, Heading, Code, Rule, Quote, List, Table }

    /// <summary>
    /// Verknüpft einen Block der Vorschau mit seinen Quellzeilen im Markdown.
    /// <see cref="Baseline"/> ist die Serialisierung beim Rendern: Ist sie unverändert,
    /// wird der Original-Markdown-Text übernommen (keine ungewollten Umformatierungen).
    /// </summary>
    public sealed class BlockInfo
    {
        public BlockKind Kind;
        public int Level;
        public int Start = -1;           // erste Quellzeile (nur Blöcke auf oberster Ebene)
        public int End = -1;             // exklusiv
        public string Baseline;
        public string FenceOpen = "```";
        public string FenceClose = "```";
        public List<ColumnAlign> Aligns;
    }

    /// <summary>Markdown-Quelltext eines nicht editierbaren Inline-Elements (Emoji-Bild, Bild).</summary>
    public sealed class InlineTag
    {
        public readonly string Markdown;
        public InlineTag(string markdown) { Markdown = markdown; }
    }

    public sealed class PreviewStyle
    {
        public Theme Theme;
        public double FontSize;
        public string BaseDir;
    }

    public sealed class RenderResult
    {
        public FlowDocument Document;
        public Dictionary<Block, BlockInfo> Map;
        public List<string> SourceLines;
    }
}
