using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MarkdownEditor.Themes
{
    /// <summary>
    /// Linien-Symbole im 24er-Raster (Lucide-Stil, ISC-Lizenz), passend zu coss ui.
    /// Die Strichfarbe folgt der Vordergrundfarbe des umgebenden Steuerelements.
    /// </summary>
    public static class Icons
    {
        public const string PanelLeft = "M5,3 H19 A2,2 0 0 1 21,5 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V5 A2,2 0 0 1 5,3 Z M9,3 V21";
        public const string Bold = "M6,12 H15 A4,4 0 0 1 15,20 H7 A1,1 0 0 1 6,19 V5 A1,1 0 0 1 7,4 H14 A4,4 0 0 1 14,12";
        public const string Italic = "M19,4 H10 M14,20 H5 M15,4 L9,20";
        public const string Underline = "M6,4 V10 A6,6 0 0 0 18,10 V4 M4,20 H20";
        public const string Strike = "M16,4 H9 A3,3 0 0 0 6.17,8 M14,12 A4,4 0 0 1 14,20 H6 M4,12 H20";
        public const string Code = "M16,18 L22,12 L16,6 M8,6 L2,12 L8,18";
        public const string Bullets = "M8,6 H21 M8,12 H21 M8,18 H21 M3,6 H3.01 M3,12 H3.01 M3,18 H3.01";
        public const string Numbers = "M10,6 H21 M10,12 H21 M10,18 H21 M4,6 H5 V10 M4,10 H6 M6,18 H4 C4,17 6,16 6,15 C6,14 5,13.5 4,14";
        public const string Task = "M6,3 H18 A3,3 0 0 1 21,6 V18 A3,3 0 0 1 18,21 H6 A3,3 0 0 1 3,18 V6 A3,3 0 0 1 6,3 Z M8.5,12 L11,14.5 L15.5,9.5";
        public const string Link = "M10,13 A5,5 0 0 0 17.54,13.54 L20.54,10.54 A5,5 0 0 0 13.47,3.47 L11.75,5.18 M14,11 A5,5 0 0 0 6.46,10.46 L3.46,13.46 A5,5 0 0 0 10.53,20.53 L12.24,18.82";
        public const string Image = "M6,3 H18 A3,3 0 0 1 21,6 V18 A3,3 0 0 1 18,21 H6 A3,3 0 0 1 3,18 V6 A3,3 0 0 1 6,3 Z M7,9 A2,2 0 1 0 11,9 A2,2 0 1 0 7,9 M21,15 L17.9,11.9 A2,2 0 0 0 15.1,11.9 L6,21";
        public const string Table = "M6,3 H18 A3,3 0 0 1 21,6 V18 A3,3 0 0 1 18,21 H6 A3,3 0 0 1 3,18 V6 A3,3 0 0 1 6,3 Z M3,9 H21 M3,15 H21 M12,3 V21";
        public const string Smile = "M2,12 A10,10 0 1 0 22,12 A10,10 0 1 0 2,12 M8,14 C8,14 9.5,16 12,16 C14.5,16 16,14 16,14 M9,9 H9.01 M15,9 H15.01";
        public const string Mic = "M12,2 A3,3 0 0 0 9,5 V12 A3,3 0 0 0 15,12 V5 A3,3 0 0 0 12,2 Z M19,10 V12 A7,7 0 0 1 5,12 V10 M12,19 V22";
        public const string Search = "M4,11 A7,7 0 1 0 18,11 A7,7 0 1 0 4,11 M20,20 L16.5,16.5";
        public const string ChevronDown = "M6,9 L12,15 L18,9";
        public const string ChevronUp = "M6,15 L12,9 L18,15";
        public const string ChevronRight = "M9,18 L15,12 L9,6";
        public const string Close = "M6,6 L18,18 M18,6 L6,18";
        public const string File = "M15,2 H6 A2,2 0 0 0 4,4 V20 A2,2 0 0 0 6,22 H18 A2,2 0 0 0 20,20 V7 Z M14,2 V6 A2,2 0 0 0 16,8 H20 M16,13 H8 M16,17 H8";
        public const string Folder = "M20,20 A2,2 0 0 0 22,18 V8 A2,2 0 0 0 20,6 H12.1 A2,2 0 0 1 10.4,5.1 L9.6,3.9 A2,2 0 0 0 7.93,3 H4 A2,2 0 0 0 2,5 V18 A2,2 0 0 0 4,20 Z";
        public const string FolderUp = "M20,20 A2,2 0 0 0 22,18 V8 A2,2 0 0 0 20,6 H12.1 A2,2 0 0 1 10.4,5.1 L9.6,3.9 A2,2 0 0 0 7.93,3 H4 A2,2 0 0 0 2,5 V18 A2,2 0 0 0 4,20 Z M12,17 V11 M9,14 L12,11 L15,14";
        public const string Plus = "M12,5 V19 M5,12 H19";
        public const string ClearFormat = "M4,7 V4 H20 V7 M9,20 H15 M12,4 V20 M3,3 L21,21";
        public const string Trash = "M3,6 H21 M19,6 V20 A2,2 0 0 1 17,22 H7 A2,2 0 0 1 5,20 V6 M8,6 V4 A2,2 0 0 1 10,2 H14 A2,2 0 0 1 16,4 V6";
        public const string Heading = "M6,12 H18 M6,20 V4 M18,20 V4";
        public const string Quote = "M3,21 C6,21 10,20 10,13 V5 C10,3.75 9.24,3 8,3 H4 C2.75,3 2,3.75 2,5 V11 C2,12.25 2.75,13 4,13 H7 M14,21 C17,21 21,20 21,13 V5 C21,3.75 20.24,3 19,3 H15 C13.75,3 13,3.75 13,5 V11 C13,12.25 13.75,13 15,13 H18";
        public const string Rule = "M3,12 H21";

        /// <summary>Symbol in <paramref name="size"/> px; Strichstärke 2 im 24er-Raster (bei 16 px ≈ 1,33 px).</summary>
        public static FrameworkElement Make(string data, double size = 16, double stroke = 2)
        {
            var path = new Path
            {
                Data = Geometry.Parse(data),
                StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = null
            };
            path.SetBinding(Shape.StrokeProperty, new Binding
            {
                Path = new PropertyPath(TextElement.ForegroundProperty),
                RelativeSource = RelativeSource.Self
            });
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            return new Viewbox
            {
                Width = size,
                Height = size,
                Child = canvas,
                SnapsToDevicePixels = true,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
    }
}
