using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>
    /// "Disk investigations": black and white, 1950s case-file minimalism.
    /// Square corners, hairlines, no shadows, no texture behind data. Emphasis comes from inversion,
    /// weight and size, never color. Oswald for headlines and numbers, a mono for data, a sans for reading.
    /// </summary>
    internal static class Theme
    {
        // ---------------- Mode ----------------

        public static bool Dark = true;
        public static event EventHandler Changed;

        public static void SetDark(bool dark)
        {
            if (Dark == dark) return;
            Dark = dark;
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        // ---------------- Color tokens (dark / paper) ----------------

        static Color L(string dark, string paper) { return Hex(Dark ? dark : paper); }

        public static Color Bg { get { return L("#000000", "#F4F4F4"); } }
        public static Color Surface { get { return L("#141414", "#FFFFFF"); } }
        public static Color Hover { get { return L("#242424", "#E0E0E0"); } }
        public static Color Fg { get { return L("#FFFFFF", "#000000"); } }
        public static Color Muted { get { return L("#B8B8B8", "#5C5C5C"); } }
        public static Color Line { get { return L("#5C5C5C", "#777777"); } }
        public static Color Subtle { get { return L("#242424", "#E0E0E0"); } }
        public static Color Disabled { get { return Hex("#777777"); } }

        public static Color Hex(string hex)
        {
            int v = Convert.ToInt32(hex.TrimStart('#'), 16);
            return Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
        }

        public static string Lc(string s) { return s == null ? null : s.ToLowerInvariant(); }
        public static string Uc(string s) { return s == null ? null : s.ToUpperInvariant(); }

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

        static readonly string[] FontFiles =
        {
            "SLOswald400", "SLOswald500", "SLOswald600",
            "SLCaseSans", "SLCaseSansMedium", "SLCaseMono", "SLCaseMonoMedium"
        };

        static void LoadFonts()
        {
            if (fonts != null) return;
            fonts = new PrivateFontCollection();
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in FontFiles)
            {
                try
                {
                    using (var s = asm.GetManifestResourceStream("Sleuth.Fonts." + name + ".ttf"))
                    {
                        if (s == null) continue;
                        var data = ReadAll(s);
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

        static byte[] ReadAll(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
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

        static Font Make(string family, string fallback, FontStyle fallbackStyle, float px)
        {
            string key = family + "|" + px.ToString(CultureInfo.InvariantCulture);
            Font f;
            if (fontCache.TryGetValue(key, out f)) return f;
            LoadFonts();
            FontFamily fam;
            float size = Math.Max(1f, px * Scale);
#if PREVIEW
            // the Linux preview can't use private fonts; it has them installed instead
            if (!families.ContainsKey(family)) { fontCache[key] = f = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel); return f; }
#endif
            f = families.TryGetValue(family, out fam)
                ? new Font(fam, size, FontStyle.Regular, GraphicsUnit.Pixel)
                : new Font(fallback, size, fallbackStyle, GraphicsUnit.Pixel);
            fontCache[key] = f;
            return f;
        }

        /// <summary>Oswald: headlines, big numbers, the primary action. Weight 400, 500 or 600.</summary>
        public static Font Display(float px, int weight = 500)
        {
            int w = weight >= 600 ? 600 : weight >= 500 ? 500 : 400;
            return Make("SL Oswald " + w, "Bahnschrift", w >= 600 ? FontStyle.Bold : FontStyle.Regular, px);
        }

        /// <summary>The reading face: names, sentences, buttons.</summary>
        public static Font Sans(float px, bool medium = false)
        {
            return Make(medium ? "SL Case Sans Medium" : "SL Case Sans", "Segoe UI", medium ? FontStyle.Bold : FontStyle.Regular, px);
        }

        /// <summary>The typewriter: paths, sizes, labels, dates.</summary>
        public static Font Mono(float px, bool medium = false)
        {
            return Make(medium ? "SL Case Mono Medium" : "SL Case Mono", "Consolas", medium ? FontStyle.Bold : FontStyle.Regular, px);
        }

        public static float Descent(Font f)
        {
            var fam = f.FontFamily;
            return f.Size * fam.GetCellDescent(f.Style) / fam.GetEmHeight(f.Style);
        }

        /// <summary>Where the baseline falls in a CSS-style line box of the given height starting at top.</summary>
        public static float BaselineIn(Font f, float top, float lineHeight)
        {
            return top + (lineHeight - (Ascent(f) + Descent(f))) / 2 + Ascent(f);
        }

        /// <summary>Distance from the top of a drawn line to its baseline.</summary>
        public static float Ascent(Font f)
        {
            var fam = f.FontFamily;
            return f.Size * fam.GetCellAscent(f.Style) / fam.GetEmHeight(f.Style);
        }

        // ---------------- The detective ----------------

        static Bitmap logo;

        public static Bitmap Logo
        {
            get
            {
                if (logo == null)
                {
                    try { logo = LoadBitmap("Sleuth.Art.logo_disc.png"); } catch { }
                }
                return logo;
            }
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

        // ---------------- Text ----------------

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
            float w = Math.Min(maxW, Width(g, text, font) + S(1));
            Text(g, text, font, color, new RectangleF(x, cy - font.Height, w, font.Height * 2));
            return w;
        }

        /// <summary>Right-aligned text ending at x, vertically centred at cy.</summary>
        public static float LabelRight(Graphics g, string text, Font font, Color color, float right, float cy)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float w = Width(g, text, font) + S(1);
            Text(g, text, font, color, new RectangleF(right - w, cy - font.Height, w, font.Height * 2), StringAlignment.Far);
            return w;
        }

        /// <summary>Text whose baseline sits at y. Returns the width.</summary>
        public static float OnBaseline(Graphics g, string text, Font font, Color color, float x, float baseline)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            using (var b = new SolidBrush(color))
            using (var f = Format(StringAlignment.Near, StringAlignment.Near))
                g.DrawString(text, font, b, x, baseline - Ascent(font), f);
            return Width(g, text, font);
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

        static readonly Dictionary<string, float> charWidths = new Dictionary<string, float>();

        static float CharWidth(Graphics g, char c, Font font)
        {
            string key = font.Name + font.Size + c;
            float w;
            if (charWidths.TryGetValue(key, out w)) return w;
            w = c == ' ' ? Width(g, "a a", font) - Width(g, "aa", font) : Width(g, c.ToString(), font);
            charWidths[key] = w;
            return w;
        }

        /// <summary>Letter-spaced text (GDI+ has no tracking), left edge at x, centred on cy. Returns the width.</summary>
        public static float Tracked(Graphics g, string text, Font font, Color color, float x, float cy, float tracking)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float x0 = x, t = S(tracking);
            using (var b = new SolidBrush(color))
            using (var f = Format(StringAlignment.Near, StringAlignment.Center))
                foreach (char c in text)
                {
                    if (c != ' ') g.DrawString(c.ToString(), font, b, new RectangleF(x, cy - font.Height, font.Height * 2, font.Height * 2), f);
                    x += CharWidth(g, c, font) + t;
                }
            return x - x0 - t;
        }

        public static float TrackedWidth(Graphics g, string text, Font font, float tracking)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float w = 0;
            foreach (char c in text) w += CharWidth(g, c, font) + S(tracking);
            return w - S(tracking);
        }

        /// <summary>The small uppercase mono label ("THE EVIDENCE", "BEFORE / FREE").</summary>
        public static float Caps(Graphics g, string text, Color color, float x, float cy, float px = 12, float tracking = 1.5f, bool medium = true)
        {
            return Tracked(g, Uc(text), Mono(px, medium), color, x, cy, tracking);
        }

        public static float CapsWidth(Graphics g, string text, float px = 12, float tracking = 1.5f, bool medium = true)
        {
            return TrackedWidth(g, Uc(text), Mono(px, medium), tracking);
        }

        // ---------------- Shapes ----------------

        public static void Rule(Graphics g, Color c, float x1, float y1, float x2, float y2, float w = 1)
        {
            using (var p = new Pen(c, S(w))) g.DrawLine(p, x1, y1, x2, y2);
        }

        /// <summary>A crisp 1px horizontal hairline (snapped to the pixel grid).</summary>
        public static void HLine(Graphics g, Color c, float x1, float x2, float y)
        {
            Rect(g, c, x1, (float)Math.Floor(y), x2 - x1, Math.Max(1, (float)Math.Round(S(1))));
        }

        public static void VLine(Graphics g, Color c, float x, float y1, float y2)
        {
            Rect(g, c, (float)Math.Floor(x), y1, Math.Max(1, (float)Math.Round(S(1))), y2 - y1);
        }

        public static void Rect(Graphics g, Color c, float x, float y, float w, float h)
        {
            if (w <= 0 || h <= 0) return;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h);
            g.SmoothingMode = old;
        }

        /// <summary>A square outline drawn inside r.</summary>
        public static void Box(Graphics g, Color c, RectangleF r, float width = 1)
        {
            float t = Math.Max(1, (float)Math.Round(S(width)));
            Rect(g, c, r.X, r.Y, r.Width, t);
            Rect(g, c, r.X, r.Bottom - t, r.Width, t);
            Rect(g, c, r.X, r.Y, t, r.Height);
            Rect(g, c, r.Right - t, r.Y, t, r.Height);
        }

        /// <summary>Keyboard focus: a 2px ring, 3px outside the control's shape.</summary>
        public static void Focus(Graphics g, RectangleF r)
        {
            Box(g, Fg, RectangleF.Inflate(r, S(3) + S(2), S(3) + S(2)), 2);
        }

        /// <summary>The magnifying glass: a ring and a short handle at 45 degrees.</summary>
        public static void Glass(Graphics g, Color c, float x, float y, float size)
        {
            float ring = size * 0.14f;
            using (var p = new Pen(c, ring))
            {
                g.DrawEllipse(p, x + ring / 2, y + ring / 2, size - ring, size - ring);
                p.Width = ring * 1.05f;
                p.StartCap = p.EndCap = LineCap.Flat;
                float k = (float)Math.Sqrt(0.5);
                float cx = x + size / 2, cy = y + size / 2, rr = size / 2;
                g.DrawLine(p, cx + rr * k * 0.92f, cy + rr * k * 0.92f, cx + rr * k + size * 0.36f, cy + rr * k + size * 0.36f);
            }
        }

        /// <summary>A rubber stamp: 2px rule, letter-spaced mono, tilted -3 degrees.</summary>
        public static SizeF Stamp(Graphics g, string text, Color c, float right, float top)
        {
            var f = Mono(12, true);
            float tw = TrackedWidth(g, Uc(text), f, 2);
            float w = tw + S(24) + S(4), h = S(16) + S(14) + S(4);
            var state = g.Save();
            g.TranslateTransform(right - w / 2, top + h / 2);
            g.RotateTransform(-3);
            var r = new RectangleF(-w / 2, -h / 2, w, h);
            using (var p = new Pen(c, S(2)) { Alignment = PenAlignment.Inset }) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            Tracked(g, Uc(text), f, c, r.X + S(14), 0, 2);
            g.Restore(state);
            return new SizeF(w, h);
        }

        /// <summary>Category swatch: label + pattern, never shade alone.</summary>
        public static void Pattern(Graphics g, Category cat, RectangleF r)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            var state = g.Save();
            g.SetClip(r);
            float step = Math.Max(3, (float)Math.Round(S(4))), one = Math.Max(1, (float)Math.Round(S(1)));
            switch (cat)
            {
                case Category.YourFiles: Rect(g, Fg, r.X, r.Y, r.Width, r.Height); break;
                case Category.Libraries:
                    using (var p = new Pen(Fg, one))
                        for (float d = -r.Height; d < r.Width + r.Height; d += step)
                            g.DrawLine(p, r.X + d, r.Bottom, r.X + d + r.Height, r.Y);
                    break;
                case Category.Programs: Rect(g, Muted, r.X, r.Y, r.Width, r.Height); break;
                case Category.Games:
                    for (float y = r.Y + step / 2; y < r.Bottom; y += step) Rect(g, Fg, r.X, (float)Math.Floor(y), r.Width, one);
                    break;
                case Category.Caches:
                    for (float y = r.Y + step / 2; y < r.Bottom; y += step)
                        for (float x = r.X + step / 2; x < r.Right; x += step)
                            Rect(g, Fg, (float)Math.Floor(x), (float)Math.Floor(y), one, one);
                    break;
            }
            g.Restore(state);
            Box(g, Fg, r);
            g.SmoothingMode = old;
        }

        public static string CategoryWord(Category c)
        {
            switch (c)
            {
                case Category.YourFiles: return "your files";
                case Category.Libraries: return "libraries";
                case Category.Programs: return "programs";
                case Category.Games: return "games";
                case Category.Caches: return "caches";
                default: return "system";
            }
        }

        /// <summary>A thin bar: track, then the filled share.</summary>
        public static void Meter(Graphics g, float x, float y, float w, float h, double fraction, Color track, Color fill)
        {
            fraction = Math.Max(0, Math.Min(1, fraction));
            Rect(g, track, x, y, w, h);
            Rect(g, fill, x, y, (float)(w * fraction), h);
        }

        // ---------------- Words ----------------

        /// <summary>"27 oct · 10:14 pm"</summary>
        public static string Day(DateTime utc)
        {
            var l = utc.ToLocalTime();
            return Lc(l.ToString("d MMM", CultureInfo.CurrentCulture) + " · " + l.ToString("h:mm tt", CultureInfo.CurrentCulture));
        }

        /// <summary>"3 days ago", "5 hours ago", "just now"</summary>
        public static string Ago(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " min ago";
            if (span.TotalHours < 48) { int h = (int)span.TotalHours; return h + (h == 1 ? " hour ago" : " hours ago"); }
            int d = (int)span.TotalDays;
            return d + " days ago";
        }

        /// <summary>Splits "7.2 GB" into "7.2" and "GB".</summary>
        public static void SplitBytes(string s, out string number, out string unit)
        {
            int sp = s == null ? -1 : s.LastIndexOf(' ');
            if (sp > 0) { number = s.Substring(0, sp); unit = s.Substring(sp + 1); }
            else { number = s ?? ""; unit = ""; }
        }

        // ---------------- Menus ----------------

        sealed class MenuRenderer : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var b = new SolidBrush(Surface)) e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var p = new Pen(Line)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                using (var b = new SolidBrush(Fg)) e.Graphics.FillRectangle(b, new Rectangle(1, 0, e.Item.Width - 2, e.Item.Height));
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                Color c = !e.Item.Enabled ? Disabled : e.Item.Selected ? Bg : Fg;
                Theme.Quality(e.Graphics);
                Text(e.Graphics, Lc(e.Text), e.TextFont, c, new RectangleF(Si(14), 0, e.Item.Width - Si(24), e.Item.Height));
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (var p = new Pen(Line)) e.Graphics.DrawLine(p, Si(10), y, e.Item.Width - Si(10), y);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        }

        public static ContextMenuStrip Menu()
        {
            return new ContextMenuStrip
            {
                Renderer = new MenuRenderer(),
                Font = Mono(13),
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Padding = new Padding(0, Si(4), 0, Si(4)),
                DropShadowEnabled = false
            };
        }

        public static ToolStripMenuItem MenuItem(string text, EventHandler click)
        {
            return new ToolStripMenuItem(text, null, click)
            {
                Padding = new Padding(Si(6), Si(6), Si(18), Si(6)),
                AutoSize = true
            };
        }
    }
}
