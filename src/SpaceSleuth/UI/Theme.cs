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
    /// <summary>One semantic color: the solid core color, its pale tint, and a darker shade for text on the tint.</summary>
    internal sealed class Hue
    {
        public readonly Color Base, Tint, Dark;
        public Hue(Color b, Color t, Color d) { Base = b; Tint = t; Dark = d; }
    }

    internal enum Weight { Light, Regular, Medium, Semibold, Bold }

    /// <summary>
    /// "Soft Sticker Pop" design tokens: cream paper, oxblood ink, candy accents, pills and big rounded corners.
    /// </summary>
    internal static class Theme
    {
        // Core palette
        public static readonly Color Purple = Hex("#B177ED");
        public static readonly Color Orange = Hex("#FF7B03");
        public static readonly Color Sky = Hex("#7DB8F2");
        public static readonly Color Coral = Hex("#F47B6A");
        public static readonly Color Mint = Hex("#69E094");

        // Supporting
        public static readonly Color Cream = Hex("#FFFCF2");
        public static readonly Color Ink = Hex("#4A0E12");
        public static readonly Color Blue = Hex("#5B84F1");
        public static readonly Color BlueDeep = Hex("#4C74DE");
        public static readonly Color Indigo = Hex("#5A67D8");
        public static readonly Color Forest = Hex("#0E5640");
        public static readonly Color Lime = Hex("#D2F04A");
        public static readonly Color Pink = Hex("#F6A3E8");
        public static readonly Color Butter = Hex("#FCEFB0");
        public static readonly Color OffWhite = Hex("#FFF8E6");
        public static readonly Color Sun = Hex("#FFB10A");
        public static readonly Color Leaf = Hex("#4FA552");
        public static readonly Color Lavender = Hex("#CDBDF9");
        public static readonly Color White = Color.White;
        public static readonly Color Outline = Hex("#1E0B0C");
        public static readonly Color Sand = Hex("#F3EBDA");

        // Semantic hues (one consistent color per kind of result)
        public static readonly Hue HPurple = new Hue(Purple, Hex("#E4D2FB"), Hex("#6A3AA3"));
        public static readonly Hue HOrange = new Hue(Orange, Hex("#FDE3BC"), Hex("#A24B00"));
        public static readonly Hue HSky = new Hue(Sky, Hex("#D3E8FA"), Hex("#2C68A6"));
        public static readonly Hue HCoral = new Hue(Coral, Hex("#FAD0C9"), Hex("#A4392A"));
        public static readonly Hue HMint = new Hue(Mint, Hex("#C8F5D8"), Hex("#1D7443"));
        public static readonly Hue HIndigo = new Hue(Indigo, Hex("#DCDFF8"), Hex("#36409E"));
        public static readonly Hue HPink = new Hue(Pink, Hex("#FCE0F6"), Hex("#8A2F7C"));
        public static readonly Hue HNeutral = new Hue(Hex("#B9A99A"), Sand, Hex("#6E5A4C"));

        public static Color InkA(int alpha) { return Color.FromArgb(alpha, Ink); }

        public static Color Hex(string hex)
        {
            int v = Convert.ToInt32(hex.TrimStart('#'), 16);
            return Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
        }

        /// <summary>
        /// One consistent color per category, everywhere: libraries &amp; media = orange, your files = sky,
        /// apps = indigo, games = pink, caches = mint, system = purple. (Coral is kept for "hidden spot" warnings.)
        /// </summary>
        public static Hue For(Category c)
        {
            switch (c)
            {
                case Category.Libraries: return HOrange;
                case Category.YourFiles: return HSky;
                case Category.Programs: return HIndigo;
                case Category.Games: return HPink;
                case Category.Caches: return HMint;
                default: return HPurple;
            }
        }

        public static Hue For(Culprit c) { return For(Labeler.CategoryOf(c)); }

        // ---------------- Scale & motion ----------------

        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        /// <summary>Seconds since the app started; drives all animation.</summary>
        public static float Now { get { return (float)clock.Elapsed.TotalSeconds; } }

        public static float Scale = 1f;
        public static float S(float v) { return v * Scale; }
        public static int Si(float v) { return (int)Math.Round(v * Scale); }

        static bool? reducedMotion;

        /// <summary>True when Windows' "Animation effects" setting is off; then nothing bobs, slides or bounces.</summary>
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

        /// <summary>Springy ease with a little overshoot, like cubic-bezier(.34,1.56,.64,1).</summary>
        public static float Spring(float x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            const float c1 = 1.70158f * 1.1f, c3 = c1 + 1;
            float t = x - 1;
            return 1 + c3 * t * t * t + c1 * t * t;
        }

        // ---------------- Fonts ----------------

        static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();
        static HashSet<string> installed;
        static PrivateFontCollection privateFonts;
        static FontFamily display;

        static bool Has(string family)
        {
            if (installed == null)
            {
                installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using (var c = new InstalledFontCollection())
                        foreach (var f in c.Families) installed.Add(f.Name);
                }
                catch { }
            }
            return installed.Contains(family);
        }

        /// <summary>UI text in pixels. Uses Segoe UI (Windows' own clean system font), falling back to Inter.</summary>
        public static Font Ui(float px, Weight w)
        {
            string key = "ui" + px + w;
            Font f;
            if (fontCache.TryGetValue(key, out f)) return f;

            string family; FontStyle style = FontStyle.Regular;
            bool segoe = Has("Segoe UI");
            if (segoe)
            {
                switch (w)
                {
                    case Weight.Light: family = Has("Segoe UI Light") ? "Segoe UI Light" : "Segoe UI"; break;
                    case Weight.Medium:
                    case Weight.Semibold: family = Has("Segoe UI Semibold") ? "Segoe UI Semibold" : "Segoe UI"; if (family == "Segoe UI") style = FontStyle.Bold; break;
                    case Weight.Bold: family = "Segoe UI"; style = FontStyle.Bold; break;
                    default: family = "Segoe UI"; break;
                }
            }
            else
            {
                switch (w)
                {
                    case Weight.Light: family = Has("Inter Light") ? "Inter Light" : "Inter"; break;
                    case Weight.Medium: family = Has("Inter Medium") ? "Inter Medium" : "Inter"; break;
                    case Weight.Semibold: family = Has("Inter SemiBold") ? "Inter SemiBold" : "Inter"; if (family == "Inter") style = FontStyle.Bold; break;
                    case Weight.Bold: family = "Inter"; style = FontStyle.Bold; break;
                    default: family = "Inter"; break;
                }
                if (!Has(family)) family = FontFamily.GenericSansSerif.Name;
            }
            f = new Font(family, Math.Max(1f, px * Scale), style, GraphicsUnit.Pixel);
            fontCache[key] = f;
            return f;
        }

        /// <summary>The chunky display font (Unbounded Black, embedded) for the wordmark and big moments.</summary>
        public static Font Display(float px)
        {
            string key = "display" + px;
            Font f;
            if (fontCache.TryGetValue(key, out f)) return f;
            var fam = DisplayFamily();
            f = fam != null
                ? new Font(fam, px * Scale, FontStyle.Regular, GraphicsUnit.Pixel)
                : Ui(px, Weight.Bold);
            fontCache[key] = f;
            return f;
        }

        static FontFamily DisplayFamily()
        {
            if (display != null || privateFonts != null) return display;
            privateFonts = new PrivateFontCollection();
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpaceSleuth.Unbounded.ttf"))
                {
                    if (s == null) return null;
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
                    privateFonts.AddMemoryFont(mem, data.Length);
                    if (privateFonts.Families.Length > 0 && Usable(privateFonts.Families[0])) display = privateFonts.Families[0];

                    if (display == null)
                    {
                        // Some systems only accept private fonts from a file.
                        string file = Path.Combine(Path.GetTempPath(), "SpaceSleuth-Unbounded-Black.ttf");
                        File.WriteAllBytes(file, data);
                        privateFonts = new PrivateFontCollection();
                        privateFonts.AddFontFile(file);
                        if (privateFonts.Families.Length > 0 && Usable(privateFonts.Families[0])) display = privateFonts.Families[0];
                    }
                }
            }
            catch
            {
                display = null;
            }
            return display;
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

        // ---------------- Drawing helpers ----------------

        public static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        /// <summary>The soft card shadow: a 2px drop in 6% ink.</summary>
        public static void Card(Graphics g, Color fill, RectangleF r, float radius, int shadowAlpha = 16)
        {
            var sh = r;
            sh.Offset(0, S(2));
            Fill(g, Color.FromArgb(shadowAlpha, Ink), sh, radius);
            Fill(g, fill, r, radius);
        }

        public static void Stroke(Graphics g, Color c, float width, RectangleF r, float radius)
        {
            using (var p = Round(r, radius))
            using (var pen = new Pen(c, width) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, p);
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
            if (string.IsNullOrEmpty(text)) return;
            using (var b = new SolidBrush(color))
            using (var f = Format(h, v, ellipsisPath, wrap))
                g.DrawString(text, font, b, r, f);
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

        /// <summary>Dot chip: pale tinted pill, solid dot on the left, tonal label. Returns the width used.</summary>
        public static float Chip(Graphics g, float x, float y, string text, Hue hue, float maxWidth = 10000, float height = 24)
        {
            var font = Ui(12, Weight.Medium);
            float h = S(height), dot = S(10), padL = S(8), gap = S(6), padR = S(10);
            float w = Math.Min(maxWidth, padL + dot + gap + Width(g, text, font) + padR);
            if (w < padL + dot + gap + S(20)) return 0;
            var r = new RectangleF(x, y, w, h);
            Fill(g, hue.Tint, r, h / 2);
            using (var b = new SolidBrush(hue.Base))
                g.FillEllipse(b, x + padL, y + (h - dot) / 2, dot, dot);
            Text(g, text, font, hue.Dark, new RectangleF(x + padL + dot + gap, y, w - padL - dot - gap - padR + S(2), h));
            return w;
        }

        /// <summary>Highlighter pill behind a key phrase (leaf green, white text). Returns the width used.</summary>
        public static float Highlight(Graphics g, string text, Font font, float x, float y, float lineHeight)
        {
            float tw = Width(g, text, font);
            float padX = S(10);
            var r = new RectangleF(x, y + S(1), tw + padX * 2, lineHeight - S(2));
            Fill(g, Leaf, r, r.Height / 2);
            Text(g, text, font, Color.White, r, StringAlignment.Center);
            return r.Width;
        }

        /// <summary>Loose hand-drawn ellipse stroke (sun yellow) around something.</summary>
        public static void HandCircle(Graphics g, RectangleF r, Color c)
        {
            var pts = new List<PointF>();
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            for (int i = 0; i <= 26; i++)
            {
                double a = -Math.PI * 0.95 + i * (Math.PI * 2.15 / 26);
                float wob = 1 + 0.04f * (float)Math.Sin(i * 1.7);
                pts.Add(new PointF(cx + (float)Math.Cos(a) * r.Width / 2 * wob, cy + (float)Math.Sin(a) * r.Height / 2 * wob - i * 0.08f * S(1)));
            }
            using (var pen = new Pen(c, S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawCurve(pen, pts.ToArray(), 0.5f);
        }

        /// <summary>Tilted capsule sticker with one word. Outlined = die-cut look with a hard offset shadow.</summary>
        public static void Sticker(Graphics g, PointF center, string word, float angle, Color bg, Color fg, bool outlined, float px = 17)
        {
            var font = Ui(px, Weight.Semibold);
            float w = Width(g, word, font) + S(36), h = S(px * 2.1f);
            var state = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(angle);
            var r = new RectangleF(-w / 2, -h / 2, w, h);
            if (outlined)
            {
                var sh = r; sh.Offset(S(4), S(4));
                Fill(g, Outline, sh, h / 2);
            }
            Fill(g, bg, r, h / 2);
            if (outlined) Stroke(g, Outline, S(2.5f), r, h / 2);
            Text(g, word, font, fg, r, StringAlignment.Center);
            g.Restore(state);
        }

        public static void Flower(Graphics g, float cx, float cy, float r, Color petals, Color middle, int count = 7)
        {
            using (var b = new SolidBrush(petals))
            {
                for (int i = 0; i < count; i++)
                {
                    double a = i * Math.PI * 2 / count;
                    float px = cx + (float)Math.Cos(a) * r * 0.55f, py = cy + (float)Math.Sin(a) * r * 0.55f;
                    g.FillEllipse(b, px - r * 0.45f, py - r * 0.45f, r * 0.9f, r * 0.9f);
                }
            }
            using (var b = new SolidBrush(middle)) g.FillEllipse(b, cx - r * 0.35f, cy - r * 0.35f, r * 0.7f, r * 0.7f);
        }

        public static void Sparkle(Graphics g, float cx, float cy, float r, Color c)
        {
            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4 - Math.PI / 2;
                float rr = i % 2 == 0 ? r : r * 0.28f;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr);
            }
            using (var b = new SolidBrush(c)) g.FillClosedCurve(b, pts, FillMode.Winding, 0.15f);
        }

        public static void Starburst(Graphics g, float cx, float cy, float r, Color c, int spikes = 12)
        {
            var pts = new PointF[spikes * 2];
            for (int i = 0; i < pts.Length; i++)
            {
                double a = i * Math.PI / spikes;
                float rr = i % 2 == 0 ? r : r * 0.72f;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr);
            }
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        public static void HalfSun(Graphics g, float cx, float cy, float r, Color c, float angle)
        {
            using (var b = new SolidBrush(c))
                g.FillPie(b, cx - r, cy - r, r * 2, r * 2, angle, 180);
        }

        public static void Clover(Graphics g, float cx, float cy, float r, Color c)
        {
            using (var b = new SolidBrush(c))
            {
                for (int i = 0; i < 4; i++)
                {
                    double a = i * Math.PI / 2 + Math.PI / 4;
                    float px = cx + (float)Math.Cos(a) * r * 0.42f, py = cy + (float)Math.Sin(a) * r * 0.42f;
                    g.FillEllipse(b, px - r * 0.5f, py - r * 0.5f, r, r);
                }
            }
        }

        /// <summary>Thick round-capped looping ribbon used as background texture.</summary>
        public static void Ribbon(Graphics g, Color c, float width, params PointF[] pts)
        {
            using (var pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawCurve(pen, pts, 0.6f);
        }

        /// <summary>Little search glyph: circle and handle.</summary>
        public static void SearchGlyph(Graphics g, float x, float y, float size, Color c)
        {
            using (var pen = new Pen(c, S(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float d = size * 0.68f;
                g.DrawEllipse(pen, x, y, d, d);
                g.DrawLine(pen, x + d * 0.85f, y + d * 0.85f, x + size, y + size);
            }
        }

        public static void Caret(Graphics g, float cx, float cy, float size, Color c)
        {
            using (var pen = new Pen(c, S(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - size, cy - size / 2), new PointF(cx, cy + size / 2), new PointF(cx + size, cy - size / 2) });
        }

        /// <summary>Cream context menus with ink text to match the rest of the app.</summary>
        public sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return White; } }
            public override Color ImageMarginGradientBegin { get { return White; } }
            public override Color ImageMarginGradientMiddle { get { return White; } }
            public override Color ImageMarginGradientEnd { get { return White; } }
            public override Color MenuBorder { get { return Sand; } }
            public override Color MenuItemBorder { get { return HPurple.Tint; } }
            public override Color MenuItemSelected { get { return HPurple.Tint; } }
            public override Color MenuItemSelectedGradientBegin { get { return HPurple.Tint; } }
            public override Color MenuItemSelectedGradientEnd { get { return HPurple.Tint; } }
            public override Color SeparatorDark { get { return Sand; } }
            public override Color SeparatorLight { get { return White; } }
            public override Color CheckBackground { get { return HPurple.Tint; } }
            public override Color CheckSelectedBackground { get { return HPurple.Tint; } }
            public override Color CheckPressedBackground { get { return HPurple.Tint; } }
        }

        public static ContextMenuStrip Menu()
        {
            var m = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = true },
                Font = Ui(14, Weight.Regular),
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Padding = new Padding(Si(4))
            };
            return m;
        }

        public static ToolStripMenuItem MenuItem(string text, EventHandler click)
        {
            return new ToolStripMenuItem(text, null, click)
            {
                ForeColor = Ink,
                Padding = new Padding(Si(6), Si(6), Si(6), Si(6))
            };
        }
    }
}
