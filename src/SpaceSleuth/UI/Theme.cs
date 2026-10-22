using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>
    /// "Glitch Swiss: sound as data" (the MERCY design kit by jehf slaps), applied to Sleuth.
    /// A clean Swiss layout that has been through a broken photocopier: paper ground, hairlines,
    /// lowercase grotesk, tiny mono lab labels, flat accent blocks, pixel stairs and grain over everything.
    /// </summary>
    internal static class Theme
    {
        // ---------------- Mode ----------------

        public static bool Dark;
        public static event EventHandler Changed;

        public static void SetDark(bool dark)
        {
            if (Dark == dark) return;
            Dark = dark;
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        /// <summary>Follows Windows' "app mode" (light/dark) unless the user picked one in the app.</summary>
        public static bool WindowsPrefersDark()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        // ---------------- Color tokens (light / dark) ----------------

        static Color L(string light, string dark) { return Hex(Dark ? dark : light); }

        public static Color Paper { get { return L("#ECEAEB", "#110E12"); } }
        public static Color Paper2 { get { return L("#E4E1E3", "#18151A"); } }
        public static Color Side { get { return L("#E4E1E3", "#000000"); } }
        public static Color Ink { get { return L("#110E12", "#ECEAEB"); } }
        public static Color Ink2 { get { return Color.FromArgb(Dark ? 209 : 204, Ink); } }
        public static Color Grey { get { return L("#625D66", "#8F8A92"); } }
        public static Color Hair { get { return Color.FromArgb(Dark ? 66 : 77, Ink); } }
        public static Color Hair2 { get { return Color.FromArgb(Dark ? 28 : 33, Ink); } }
        public static Color Scan { get { return L("#CFCBD0", "#2C2830"); } }
        public static Color HoverRow { get { return Color.FromArgb(Dark ? 18 : 15, Ink); } }
        public static Color Blue { get { return L("#2630D1", "#3B46FF"); } }
        public static Color Violet { get { return L("#2A0F4A", "#6A3CB8"); } }

        // Shared accents
        public static readonly Color Red = Hex("#E2290F");
        public static readonly Color Pink = Hex("#DC7F89");
        public static readonly Color Mint = Hex("#47E4AE");
        public static readonly Color Yellow = Hex("#E2CC1E");
        public static readonly Color Orange = Hex("#BF3E23");
        public static readonly Color OnMint = Hex("#110E12");
        public static readonly Color OnMint2 = Color.FromArgb(158, Hex("#110E12"));

        public static Color Hex(string hex)
        {
            int v = Convert.ToInt32(hex.TrimStart('#'), 16);
            return Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
        }

        /// <summary>One small flat square per kind of place, used everywhere (red is kept for "selected" and alerts).</summary>
        public static Color For(Category c)
        {
            switch (c)
            {
                case Category.Libraries: return Blue;
                case Category.YourFiles: return Mint;
                case Category.Programs: return Violet;
                case Category.Games: return Yellow;
                case Category.Caches: return Scan;
                default: return Orange;
            }
        }

        public static Color For(Culprit c) { return For(Labeler.CategoryOf(c)); }

        /// <summary>Everything is lowercase in this language.</summary>
        public static string Lc(string s) { return s == null ? null : s.ToLowerInvariant(); }

        // ---------------- Scale & motion ----------------

        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        public static float Now { get { return (float)clock.Elapsed.TotalSeconds; } }

        public static float Scale = 1f;
        public static float S(float v) { return v * Scale; }
        public static int Si(float v) { return (int)Math.Round(v * Scale); }

        static bool? reducedMotion;

        /// <summary>True when Windows' "Animation effects" setting is off.</summary>
        public static bool ReducedMotion
        {
            get
            {
                if (reducedMotion.HasValue) return reducedMotion.Value;
                bool reduced = false;
                try
                {
                    bool on = true;
                    if (SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, ref on, 0)) reduced = !on;
                }
                catch { }
                reducedMotion = reduced;
                return reduced;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(int action, int param, ref bool value, int winIni);

        // ---------------- Fonts (embedded, SIL OFL) ----------------

        static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();
        static PrivateFontCollection fonts;
        static readonly Dictionary<string, FontFamily> families = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);

        static void LoadFonts()
        {
            if (fonts != null) return;
            fonts = new PrivateFontCollection();
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in new[] { "SLArchivo500", "SLArchivo550", "SLArchivo600", "SLArchivo700", "SLMono", "SLMonoBold", "SLUnbounded" })
            {
                try
                {
                    using (var s = asm.GetManifestResourceStream("Sleuth.Fonts." + name + ".ttf"))
                    {
                        if (s == null) continue;
                        var data = new byte[s.Length];
                        int read = 0;
                        while (read < data.Length)
                        {
                            int n = s.Read(data, read, data.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        // GDI+ needs the memory to stay alive for the life of the app, so it is never freed.
                        IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                        Marshal.Copy(data, 0, mem, data.Length);
                        fonts.AddMemoryFont(mem, data.Length);

                        // Some systems only accept private fonts from a file: keep one in temp as a fallback.
                        string file = Path.Combine(Path.GetTempPath(), "Sleuth-" + name + ".ttf");
                        try { if (!File.Exists(file) || new FileInfo(file).Length != data.Length) File.WriteAllBytes(file, data); } catch { }
                        try { fonts.AddFontFile(file); } catch { }
                    }
                }
                catch { }
            }
            foreach (var f in fonts.Families)
                if (!families.ContainsKey(f.Name) && Usable(f)) families[f.Name] = f;
        }

        static bool Usable(FontFamily family)
        {
            try
            {
                using (var f = new Font(family, 20, FontStyle.Regular, GraphicsUnit.Pixel))
                    return f.Height > 0;
            }
            catch
            {
                return false;
            }
        }

        static Font Make(string key, string family, string fallback, FontStyle fallbackStyle, float px)
        {
            Font f;
            if (fontCache.TryGetValue(key, out f)) return f;
            LoadFonts();
            FontFamily fam;
            float size = Math.Max(1f, px * Scale);
            f = families.TryGetValue(family, out fam)
                ? new Font(fam, size, FontStyle.Regular, GraphicsUnit.Pixel)
                : new Font(fallback, size, fallbackStyle, GraphicsUnit.Pixel);
            fontCache[key] = f;
            return f;
        }

        /// <summary>Archivo: the words people read and click. Weight 500, 550, 600 or 700.</summary>
        public static Font G(float px, int weight = 500)
        {
            int w = weight >= 700 ? 700 : weight >= 600 ? 600 : weight >= 550 ? 550 : 500;
            return Make("g" + px + "_" + w, "SL Archivo " + w, "Segoe UI", w >= 600 ? FontStyle.Bold : FontStyle.Regular, px);
        }

        /// <summary>JetBrains Mono: numbers, values, labels, coordinates.</summary>
        public static Font M(float px, bool bold = false)
        {
            return Make("m" + px + bold, bold ? "SL Mono Bold" : "SL Mono", "Consolas", bold ? FontStyle.Bold : FontStyle.Regular, px);
        }

        /// <summary>Unbounded 800: the wordmark and panel titles only.</summary>
        public static Font W(float px)
        {
            return Make("w" + px, "SL Unbounded", "Segoe UI Black", FontStyle.Regular, px);
        }

        // ---------------- Grain ----------------

        static Bitmap grainLight, grainDark;

        static Bitmap GrainTile()
        {
            try
            {
                if (Dark)
                {
                    if (grainDark == null) grainDark = LoadBitmap("Sleuth.Textures.grain_dark.png");
                    return grainDark;
                }
                if (grainLight == null) grainLight = LoadBitmap("Sleuth.Textures.grain_light.png");
                return grainLight;
            }
            catch { return null; }
        }

        static Bitmap LoadBitmap(string resource)
        {
            // GDI+ needs a stream-backed image's stream to stay open for its whole life. Copy it into a
            // standalone 32-bit bitmap instead, so the stream can be closed safely.
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) return null;
                using (var loaded = new Bitmap(s))
                {
                    var copy = new Bitmap(loaded.Width, loaded.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(copy))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
                    }
                    return copy;
                }
            }
        }

        static TextureBrush grainBrushLight, grainBrushDark;
        static bool grainBroken;

        /// <summary>
        /// Fine grain over everything, tiled from the window's corner so it lines up across every panel.
        /// The tiles already carry their opacity (dark specks in light mode, light specks in dark mode).
        /// </summary>
        public static void Grain(Graphics g, Control c, Rectangle area)
        {
            if (grainBroken) return;
            try
            {
                var tile = GrainTile();
                if (tile == null) return;
                TextureBrush brush = Dark ? grainBrushDark : grainBrushLight;
                if (brush == null)
                {
                    brush = new TextureBrush(tile, WrapMode.Tile);
                    if (Dark) grainBrushDark = brush; else grainBrushLight = brush;
                }
                Point origin = Point.Empty;
                try
                {
                    var form = c.FindForm();
                    if (form != null) origin = form.PointToClient(c.PointToScreen(Point.Empty));
                }
                catch { }
                float s = 0.5f * Scale; // 256 px tile = 128 design px
                brush.ResetTransform();
                brush.ScaleTransform(s, s, MatrixOrder.Append);
                brush.TranslateTransform(-origin.X, -origin.Y, MatrixOrder.Append);
                var state = g.Save();
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.FillRectangle(brush, area);
                g.Restore(state);
            }
            catch
            {
                // Texture is decoration: if this PC's graphics can't draw it, carry on without grain.
                grainBroken = true;
            }
        }

        // ---------------- Drawing helpers ----------------

        public static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static StringFormat Format(StringAlignment h, StringAlignment v, bool ellipsisPath = false, bool wrap = false)
        {
            var f = new StringFormat(StringFormat.GenericTypographic);
            f.Alignment = h;
            f.LineAlignment = v;
            f.Trimming = ellipsisPath ? StringTrimming.EllipsisPath : StringTrimming.EllipsisCharacter;
            f.FormatFlags = StringFormatFlags.MeasureTrailingSpaces | (wrap ? 0 : StringFormatFlags.NoWrap);
            return f;
        }

        public static void Text(Graphics g, string text, Font font, Color color, RectangleF r,
                                StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center,
                                bool ellipsisPath = false, bool wrap = false)
        {
            if (string.IsNullOrEmpty(text) || r.Width <= 1) return;
            using (var b = new SolidBrush(color))
            using (var f = Format(h, v, ellipsisPath, wrap))
                g.DrawString(text, font, b, r, f);
        }

        /// <summary>Text with its left edge at x and its vertical centre at cy. Returns the width.</summary>
        public static float Label(Graphics g, string text, Font font, Color color, float x, float cy, float maxW = 10000)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float w = Math.Min(maxW, Width(g, text, font) + S(2));
            Text(g, text, font, color, new RectangleF(x, cy - font.Height, w, font.Height * 2));
            return w;
        }

        /// <summary>Right-aligned text ending at x, vertically centred at cy.</summary>
        public static float LabelRight(Graphics g, string text, Font font, Color color, float right, float cy)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float w = Width(g, text, font) + S(2);
            Text(g, text, font, color, new RectangleF(right - w, cy - font.Height, w, font.Height * 2), StringAlignment.Far);
            return w;
        }

        /// <summary>Right-aligned in its box when it fits, otherwise left-aligned and cut with an ellipsis.</summary>
        public static void TextRightFit(Graphics g, string text, Font font, Color color, RectangleF r)
        {
            bool fits = Width(g, text, font) <= r.Width;
            Text(g, text, font, color, r, fits ? StringAlignment.Far : StringAlignment.Near);
        }

        public static float Width(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            using (var f = Format(StringAlignment.Near, StringAlignment.Near))
                return g.MeasureString(text, font, new PointF(0, 0), f).Width;
        }

        public static float WrappedHeight(Graphics g, string text, Font font, float width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            using (var f = Format(StringAlignment.Near, StringAlignment.Near, false, true))
                return (float)Math.Ceiling(g.MeasureString(text, font, new SizeF(width, 10000), f).Height);
        }

        public static void Line(Graphics g, Color c, float w, float x1, float y1, float x2, float y2)
        {
            using (var p = new Pen(c, w)) g.DrawLine(p, x1, y1, x2, y2);
        }

        public static void Rect(Graphics g, Color c, float x, float y, float w, float h)
        {
            using (var b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h);
        }

        public static void Outline(Graphics g, Color c, float width, RectangleF r)
        {
            using (var p = new Pen(c, width) { Alignment = PenAlignment.Inset }) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        }

        public static void Dotted(Graphics g, Color c, float w, float x1, float y1, float x2, float y2, float dash = 1.5f, float gap = 3f)
        {
            using (var p = new Pen(c, w) { DashPattern = new[] { dash / w * Scale, gap / w * Scale } })
                g.DrawLine(p, x1, y1, x2, y2);
        }

        /// <summary>A solid square with slightly ragged, photocopied edges.</summary>
        public static void Toner(Graphics g, Color c, RectangleF r, int seed = 1, float jitter = 0.3f)
        {
            var pts = new List<PointF>();
            var rnd = new Random(seed);
            Func<float> j = () => (float)(rnd.NextDouble() * 2 - 1) * S(jitter);
            for (int i = 0; i < 8; i++) pts.Add(new PointF(r.X + r.Width * i / 8f, r.Y + j()));
            for (int i = 0; i < 8; i++) pts.Add(new PointF(r.Right + j(), r.Y + r.Height * i / 8f));
            for (int i = 0; i < 8; i++) pts.Add(new PointF(r.Right - r.Width * i / 8f, r.Bottom + j()));
            for (int i = 0; i < 8; i++) pts.Add(new PointF(r.X + j(), r.Bottom - r.Height * i / 8f));
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts.ToArray());
        }

        // Pixel patterns from the kit (cells of 4-8 px)
        public static readonly int[,] StairA = { { 0, 0 }, { 1, 0 }, { 1, 1 }, { 2, 1 }, { 2, 2 }, { 3, 2 } };
        public static readonly int[,] StairB = { { 0, 1 }, { 1, 1 }, { 1, 0 }, { 2, 0 }, { 3, 0 }, { 3, 1 } };
        public static readonly int[,] Cluster = { { 0, 0 }, { 1, 0 }, { 2, 1 }, { 0, 2 }, { 1, 2 }, { 3, 2 }, { 2, 3 }, { 3, 3 }, { 4, 1 } };
        public static readonly int[,] Step = { { 0, 1 }, { 1, 0 } };
        public static readonly int[,] Corner = { { 0, 0 }, { 1, 0 }, { 1, 1 } };
        public static readonly int[,] Diag = { { 0, 0 }, { 1, 1 } };

        public static void Pixels(Graphics g, int[,] pattern, Color c, float x, float y, float cell)
        {
            using (var b = new SolidBrush(c))
                for (int i = 0; i < pattern.GetLength(0); i++)
                    g.FillRectangle(b, x + pattern[i, 0] * cell, y + pattern[i, 1] * cell, cell, cell);
        }

        /// <summary>Thin plus sign.</summary>
        public static void Cross(Graphics g, Color c, float cx, float cy, float size, float w = 1f)
        {
            Line(g, c, S(w), cx - size, cy, cx + size, cy);
            Line(g, c, S(w), cx, cy - size, cx, cy + size);
        }

        /// <summary>RGB-split text: red copy shifted left, mint copy shifted right, then the main color (hero words only).</summary>
        public static void Split(Graphics g, string text, Font font, Color main, float x, float y)
        {
            using (var f = Format(StringAlignment.Near, StringAlignment.Near))
            using (var red = new SolidBrush(Color.FromArgb(199, Red)))
            using (var mint = new SolidBrush(Color.FromArgb(184, Mint)))
            using (var soft = new SolidBrush(Color.FromArgb(90, main)))
            using (var b = new SolidBrush(main))
            {
                float d = S(2.5f);
                g.DrawString(text, font, red, x - d, y, f);
                g.DrawString(text, font, mint, x + d, y, f);
                // a hint of the photocopy blur
                g.DrawString(text, font, soft, x - S(0.4f), y + S(0.3f), f);
                g.DrawString(text, font, soft, x + S(0.4f), y - S(0.3f), f);
                g.DrawString(text, font, b, x, y, f);
            }
        }

        /// <summary>"x: 041 y: 366": three-digit, zero padded, like a lab sheet.</summary>
        public static string Coord(float x, float y)
        {
            int ix = Math.Max(0, Math.Min(999, (int)Math.Round(x / Scale)));
            int iy = Math.Max(0, Math.Min(999, (int)Math.Round(y / Scale)));
            return "x: " + ix.ToString("000") + " y: " + iy.ToString("000");
        }

        /// <summary>Hairline meter: track, 3px ink bar, 7px square marker (or a pink track with a red bar).</summary>
        public static void Meter(Graphics g, float x, float cy, float w, double fraction, bool redStyle, Color ink)
        {
            fraction = Math.Max(0, Math.Min(1, fraction));
            if (redStyle)
            {
                Rect(g, Pink, x, cy - S(3.5f), w, S(7));
                Rect(g, Red, x, cy - S(3.5f), (float)(w * fraction), S(7));
                return;
            }
            Line(g, Color.FromArgb(77, ink), S(1), x, cy, x + w, cy);
            float fw = (float)(w * fraction);
            Rect(g, ink, x, cy - S(1), fw, S(3));
            Rect(g, ink, x + fw - S(3.5f), cy - S(3.5f), S(7), S(7));
        }

        // ---------------- Menus ----------------

        sealed class MenuRenderer : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var b = new SolidBrush(Paper)) e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                using (var p = new Pen(Ink, S(1.5f))) e.Graphics.DrawRectangle(p, r);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                var r = new Rectangle(Si(3), 1, e.Item.Width - Si(6), e.Item.Height - 2);
                using (var b = new SolidBrush(HoverRow)) e.Graphics.FillRectangle(b, r);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var item = e.Item as ToolStripMenuItem;
                Color c = !e.Item.Enabled ? Grey : item != null && item.Checked ? Red : Ink;
                float x = Si(28); // room for the red "selected" square
                if (item != null && item.Checked)
                    Rect(e.Graphics, Red, Si(12), e.Item.Height / 2f - S(3.5f), S(7), S(7));
                Theme.Quality(e.Graphics);
                Text(e.Graphics, Lc(e.Text), e.TextFont, c, new RectangleF(x, 0, e.Item.Width - x - Si(10), e.Item.Height));
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (var p = new Pen(Hair2)) e.Graphics.DrawLine(p, Si(10), y, e.Item.Width - Si(10), y);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        }

        public static ContextMenuStrip Menu()
        {
            return new ContextMenuStrip
            {
                Renderer = new MenuRenderer(),
                Font = G(14, 500),
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Padding = new Padding(Si(2), Si(6), Si(2), Si(6)),
                DropShadowEnabled = false
            };
        }

        public static ToolStripMenuItem MenuItem(string text, EventHandler click)
        {
            return new ToolStripMenuItem(text, null, click)
            {
                Padding = new Padding(Si(6), Si(5), Si(18), Si(5)),
                AutoSize = true
            };
        }
    }
}
