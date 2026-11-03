using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpaceSleuth.UI
{
    /// <summary>Anything that wants a frame of animation from the window's shared timer.</summary>
    internal interface IAnimated
    {
        void Tick(float now);
    }

    /// <summary>Base for every custom-painted control: double buffered, flat ground, nothing behind the data.</summary>
    internal abstract class Painted : Control
    {
        protected Painted()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Theme.Changed += (s, e) => { BackColor = Ground; Invalidate(); };
            BackColor = Theme.Bg;
        }

        protected virtual Color Ground { get { return Theme.Bg; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ground);
            Theme.Quality(g);
            try
            {
                Draw(g);
            }
            catch (Exception ex)
            {
                // Never take the whole app down over one panel's drawing: show what broke, small, and keep going.
                try { Theme.Text(g, "draw error: " + ex.Message, Theme.Mono(11), Theme.Muted, new RectangleF(4, 4, Width - 8, Theme.S(16))); } catch { }
            }
        }

        protected abstract void Draw(Graphics g);

        protected bool ShowFocus { get { return Focused && ShowFocusCues; } }
    }

    /// <summary>Base for anything you can press: hover, pressed, focus and keyboard activation.</summary>
    internal abstract class Pressable : Painted
    {
        protected bool hover, down;

        protected Pressable()
        {
            TabStop = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Enabled) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && Enabled) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    /// <summary>The 104px rail: the detective on his white disc, the wordmark, a rule, and two rotated captions.</summary>
    internal sealed class SideBar : Painted
    {
        public string Caption = "disk investigations";
        public string Footer = "est. 2026 / vol. 01";

        protected override void Draw(Graphics g)
        {
            float w = Width, h = Height, cx = w / 2f;
            Theme.VLine(g, Theme.Line, w - Theme.S(1), 0, h);

            float size = Theme.S(76), top = Theme.S(31);
            var logo = Theme.Logo;
            if (logo != null) g.DrawImage(logo, new RectangleF(cx - size / 2, top, size, size));
            else
            {
                using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, cx - size / 2, top, size, size);
                Theme.Glass(g, Color.Black, cx - Theme.S(16), top + Theme.S(22), Theme.S(32));
            }

            var mark = Theme.Display(27, 600);
            float mw = Theme.TrackedWidth(g, "SLEUTH", mark, 1.5f);
            Theme.Tracked(g, "SLEUTH", mark, Theme.Fg, cx - mw / 2, top + size + Theme.S(20), 1.5f);

            float ruleTop = top + size + Theme.S(72);
            Theme.VLine(g, Theme.Line, cx, ruleTop, ruleTop + Theme.S(56));
            Theme.Rect(g, Theme.Fg, cx - Theme.S(4), ruleTop + Theme.S(88), Theme.S(8), Theme.S(8));

            float footCy = h - Theme.S(124);
            float footW = Theme.CapsWidth(g, Footer, 11, 2, false);
            float capW = Theme.CapsWidth(g, Caption, 12, 3, false);
            float capCy = Math.Min(Theme.S(466), (ruleTop + Theme.S(110) + footCy - footW / 2) / 2);
            if (capCy - capW / 2 > ruleTop + Theme.S(104) && capCy + capW / 2 < footCy - footW / 2 - Theme.S(24))
                Rotated(g, Caption, 12, 3, Theme.Fg, cx, capCy);
            if (footCy - footW / 2 > ruleTop + Theme.S(104))
                Rotated(g, Footer, 11, 2, Theme.Muted, cx, footCy);
        }

        static void Rotated(Graphics g, string text, float px, float tracking, Color c, float cx, float cy)
        {
            float tw = Theme.CapsWidth(g, text, px, tracking, false);
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(-90);
            Theme.Caps(g, text, c, -tw / 2, 0, px, tracking, false);
            g.Restore(state);
        }
    }

    /// <summary>Breadcrumbs on the left, the dark / paper switch on the right, over a hairline.</summary>
    internal sealed class TopBar : Painted
    {
        public string[] Crumbs = { "this computer", "all drives", "new case" };
        public event EventHandler<bool> ThemePicked;
        RectangleF darkRect, paperRect;
        int hover = -1;

        protected override void Draw(Graphics g)
        {
            float pad = Theme.S(32), cy = Height / 2f;
            var mono = Theme.Mono(13);
            float x = pad;
            for (int i = 0; i < Crumbs.Length; i++)
            {
                if (i > 0) x += Theme.Label(g, "/", mono, Theme.Muted, x + Theme.S(16), cy) + Theme.S(32);
                x += Theme.Label(g, Theme.Lc(Crumbs[i]), mono, i == 0 ? Theme.Muted : Theme.Fg, x, cy);
            }

            var sw = new RectangleF(Width - pad - Theme.S(152), cy - Theme.S(14), Theme.S(152), Theme.S(28));
            darkRect = new RectangleF(sw.X, sw.Y, sw.Width / 2, sw.Height);
            paperRect = new RectangleF(sw.X + sw.Width / 2, sw.Y, sw.Width / 2, sw.Height);
            Theme.Box(g, Theme.Line, sw);
            var on = Theme.Dark ? darkRect : paperRect;
            Theme.Rect(g, Theme.Fg, on.X + Theme.S(1), on.Y + Theme.S(1), on.Width - Theme.S(2), on.Height - Theme.S(2));
            var f = Theme.Mono(12);
            Theme.Text(g, "dark", f, Theme.Dark ? Theme.Bg : hover == 0 ? Theme.Fg : Theme.Muted, darkRect, StringAlignment.Center);
            Theme.Text(g, "paper", f, !Theme.Dark ? Theme.Bg : hover == 1 ? Theme.Fg : Theme.Muted, paperRect, StringAlignment.Center);

            Theme.HLine(g, Theme.Line, 0, Width, Height - Theme.S(1));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = darkRect.Contains(e.Location) ? 0 : paperRect.Contains(e.Location) ? 1 : -1;
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (ThemePicked != null)
            {
                if (darkRect.Contains(e.Location)) ThemePicked(this, true);
                else if (paperRect.Contains(e.Location)) ThemePicked(this, false);
            }
            base.OnMouseClick(e);
        }
    }

    /// <summary>File-folder tabs for the drives (the active one inverted), a rule under them and a small note beside.</summary>
    internal sealed class DriveTabs : Painted
    {
        public readonly List<string> Items = new List<string>();
        public string Note = "";
        int selected, hover = -1;
        RectangleF[] rects = new RectangleF[0];
        public event EventHandler SelectedIndexChanged;

        public DriveTabs()
        {
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= Items.Count || value == selected) return;
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        float TabW(int i) { return Theme.S(Items[i].Length > 3 ? 116 : 100); }

        protected override void Draw(Graphics g)
        {
            rects = new RectangleF[Items.Count];
            float x = 0, bottom = Height;
            for (int i = 0; i < Items.Count; i++)
            {
                bool on = i == selected;
                float w = TabW(i), h = Theme.S(on ? 48 : 44);
                var r = new RectangleF(x, bottom - h, w, h);
                rects[i] = new RectangleF(x, bottom - Theme.S(54), w, Theme.S(54));
                var ear = new RectangleF(x, r.Y - Theme.S(6), Theme.S(46), Theme.S(7));
                if (on)
                {
                    Theme.Rect(g, Theme.Fg, r.X, r.Y, r.Width, r.Height);
                    Theme.Rect(g, Theme.Fg, ear.X, ear.Y, ear.Width, ear.Height);
                }
                else
                {
                    if (i == hover && Enabled) { Theme.Rect(g, Theme.Hover, r.X, r.Y, r.Width, r.Height); Theme.Rect(g, Theme.Hover, ear.X, ear.Y, ear.Width, ear.Height); }
                    float t = Theme.S(1);
                    Theme.Rect(g, Theme.Line, r.X, r.Y, t, r.Height);
                    Theme.Rect(g, Theme.Line, r.Right - t, r.Y, t, r.Height);
                    Theme.Rect(g, Theme.Line, ear.Right - t, r.Y, r.Right - ear.Right + t, t);
                    Theme.Rect(g, Theme.Line, ear.X, ear.Y, ear.Width, t);
                    Theme.Rect(g, Theme.Line, ear.X, ear.Y, t, ear.Height);
                    Theme.Rect(g, Theme.Line, ear.Right - t, ear.Y, t, ear.Height);
                }
                Color c = on ? Theme.Bg : Enabled ? Theme.Fg : Theme.Disabled;
                Theme.Label(g, Items[i], Theme.Mono(18, true), c, x + Theme.S(16), r.Y + r.Height / 2 + Theme.S(1));
                if (on && ShowFocus) Theme.Focus(g, r);
                x += w + Theme.S(6);
            }
            float lineEnd = Math.Max(Theme.S(280), x - Theme.S(6));
            Theme.HLine(g, Theme.Line, 0, lineEnd, bottom - Theme.S(1));
            Theme.Label(g, Theme.Lc(Note), Theme.Mono(12), Theme.Muted, lineEnd + Theme.S(24), bottom - Theme.S(21));
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            Cursor = h >= 0 && Enabled ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h >= 0 && Enabled) { Focus(); SelectedIndex = h; }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled) return;
            if (e.KeyCode == Keys.Left) SelectedIndex = selected - 1;
            if (e.KeyCode == Keys.Right) SelectedIndex = selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>The primary action, 56px: inverted block, Oswald words, the magnifying glass (or a stop square).</summary>
    internal sealed class PrimaryButton : Pressable
    {
        public bool StopIcon;

        public PrimaryButton(string text) { Text = text; }

        protected override void Draw(Graphics g)
        {
            var r = new RectangleF(0, 0, Width, Height);
            var inner = RectangleF.Inflate(r, -Theme.S(5), -Theme.S(5));
            Color fill = !Enabled ? Theme.Bg : down ? Theme.Bg : hover ? Theme.Muted : Theme.Fg;
            Color ink = !Enabled ? Theme.Disabled : down ? Theme.Fg : Theme.Bg;
            Theme.Rect(g, fill, r.X, r.Y, r.Width, r.Height);
            if (!Enabled) Theme.Box(g, Theme.Line, r);
            if (down && Enabled)
            {
                Theme.Box(g, Theme.Fg, r, 2);
                Theme.Box(g, Theme.Fg, RectangleF.Inflate(inner, Theme.S(1), Theme.S(1)));
            }

            var f = Theme.Display(24, 600);
            float tw = Theme.Width(g, Text, f), icon = Theme.S(22), gap = Theme.S(24);
            float x = (Width - tw - gap - icon) / 2, cy = Height / 2f;
            Theme.Label(g, Text, f, ink, x, cy - Theme.S(1));
            float ix = x + tw + gap;
            if (StopIcon) Theme.Rect(g, ink, ix + Theme.S(5), cy - Theme.S(6), Theme.S(12), Theme.S(12));
            else Theme.Glass(g, ink, ix, cy - icon / 2 - Theme.S(1), icon * 0.82f);
            if (ShowFocus) Theme.Focus(g, r);
        }
    }

    /// <summary>A 40px outlined button ("open folder ↗", "copy path").</summary>
    internal sealed class SecondaryButton : Pressable
    {
        public SecondaryButton(string text) { Text = text; }

        protected override Color Ground { get { return Theme.Surface; } }

        protected override void Draw(Graphics g)
        {
            var r = new RectangleF(0, 0, Width, Height);
            Color fill = !Enabled ? Theme.Surface : down ? Theme.Fg : hover ? Theme.Hover : Theme.Surface;
            Color border = !Enabled ? Theme.Line : down || hover ? Theme.Fg : Theme.Line;
            Color ink = !Enabled ? Theme.Disabled : down ? Theme.Bg : Theme.Fg;
            Theme.Rect(g, fill, 0, 0, Width, Height);
            Theme.Box(g, border, r);
            Theme.Text(g, Text, Theme.Sans(14, true), ink, r, StringAlignment.Center);
            if (ShowFocus) Theme.Focus(g, RectangleF.Inflate(r, -Theme.S(5), -Theme.S(5)));
        }
    }

    /// <summary>LOOK BACK, then one small square button per window; the chosen one inverted.</summary>
    internal sealed class LookBack : Painted
    {
        readonly string[] items;
        int selected, hover = -1;
        RectangleF[] rects = new RectangleF[0];
        public event EventHandler SelectedIndexChanged;

        public LookBack(string[] items, int selected)
        {
            this.items = items;
            this.selected = selected;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= items.Length || value == selected) return;
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        static float ItemW(string s) { return Theme.S(s.Length > 4 ? 140 : 44); }

        public int PreferredWidth()
        {
            float w = Theme.S(78 + 8);
            foreach (var s in items) w += ItemW(s) + Theme.S(4);
            return (int)Math.Ceiling(w);
        }

        protected override void Draw(Graphics g)
        {
            float cy = Height / 2f;
            Theme.Caps(g, "look back", Theme.Muted, 0, cy, 12, 1.5f);
            float x = Theme.S(86);
            rects = new RectangleF[items.Length];
            var f = Theme.Mono(12);
            for (int i = 0; i < items.Length; i++)
            {
                var r = new RectangleF(x, cy - Theme.S(16), ItemW(items[i]), Theme.S(32));
                rects[i] = r;
                bool on = i == selected;
                if (on) Theme.Rect(g, Enabled ? Theme.Fg : Theme.Disabled, r.X, r.Y, r.Width, r.Height);
                else if (i == hover && Enabled) Theme.Box(g, Theme.Line, r);
                Color c = on ? Theme.Bg : Enabled ? Theme.Fg : Theme.Disabled;
                Theme.Text(g, items[i], f, c, r, StringAlignment.Center);
                if (on && ShowFocus) Theme.Focus(g, r);
                x += r.Width + Theme.S(4);
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            Cursor = h >= 0 && Enabled ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h >= 0 && Enabled) { Focus(); SelectedIndex = h; }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled) return;
            if (e.KeyCode == Keys.Left) SelectedIndex = selected - 1;
            if (e.KeyCode == Keys.Right) SelectedIndex = selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>A 16px square checkbox with a mono label; checked = filled with a tick.</summary>
    internal sealed class CheckBox : Pressable
    {
        bool isChecked;
        public event EventHandler CheckedChanged;

        public CheckBox(string text) { Text = text; }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (value == isChecked) return;
                isChecked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public int PreferredWidth()
        {
            using (var g = CreateGraphics())
                return (int)Math.Ceiling(Theme.Width(g, Text, Theme.Mono(13)) + Theme.S(36));
        }

        protected override void Draw(Graphics g)
        {
            float cy = Height / 2f;
            var box = new RectangleF(Theme.S(4), cy - Theme.S(8), Theme.S(16), Theme.S(16));
            Color c = Enabled ? Theme.Fg : Theme.Disabled;
            if (isChecked)
            {
                Theme.Rect(g, c, box.X, box.Y, box.Width, box.Height);
                using (var p = new Pen(Theme.Bg, Theme.S(2)) { StartCap = LineCap.Square, EndCap = LineCap.Square, LineJoin = LineJoin.Miter })
                    g.DrawLines(p, new[] { new PointF(box.X + Theme.S(4), cy), new PointF(box.X + Theme.S(7), cy + Theme.S(3.5f)), new PointF(box.X + Theme.S(12), cy - Theme.S(4)) });
            }
            else
            {
                if (hover && Enabled) Theme.Rect(g, Theme.Hover, box.X, box.Y, box.Width, box.Height);
                Theme.Box(g, c, box);
            }
            Theme.Label(g, Text, Theme.Mono(13), Enabled ? Theme.Fg : Theme.Disabled, box.Right + Theme.S(9), cy);
            if (ShowFocus) Theme.Focus(g, box);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && Enabled) { Checked = !Checked; e.Handled = true; return; }
            base.OnKeyDown(e);
        }
    }

    /// <summary>The 36px filter field: surface fill, hairline border, a 2px ring while typing.</summary>
    internal sealed class SearchField : Painted
    {
        public readonly TextBox Box = new TextBox();
        bool hover;

        public SearchField()
        {
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Theme.Mono(12);
            ApplyColors();
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.MouseEnter += (s, e) => { hover = true; Invalidate(); };
            Box.MouseLeave += (s, e) => { hover = false; Invalidate(); };
            Theme.Changed += (s, e) => ApplyColors();
            Box.HandleCreated += (s, e) =>
            {
                try { SendMessage(Box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "filter suspects / paths"); }
                catch { }
            };
            Cursor = Cursors.IBeam;
        }

        void ApplyColors()
        {
            Box.BackColor = Theme.Surface;
            Box.ForeColor = Theme.Fg;
        }

        RectangleF Field { get { return new RectangleF(Theme.S(6), (Height - Theme.S(36)) / 2, Width - Theme.S(6), Theme.S(36)); } }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Box.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var f = Field;
            int left = (int)(f.X + Theme.S(12));
            Box.SetBounds(left, (int)(f.Y + (f.Height - Box.PreferredHeight) / 2 + Theme.S(1)), Math.Max(10, (int)(f.Right - Theme.S(10)) - left), Box.PreferredHeight);
            base.OnLayout(e);
        }

        protected override void Draw(Graphics g)
        {
            var f = Field;
            Theme.Rect(g, Theme.Surface, f.X, f.Y, f.Width, f.Height);
            Theme.Box(g, Enabled && (hover || Box.Focused) ? Theme.Fg : Theme.Line, f);
            if (Box.Focused) Theme.Box(g, Theme.Fg, RectangleF.Inflate(f, Theme.S(5), Theme.S(5)), 2);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }

    /// <summary>The 32px status line: a small square, what's happening, and a note on the right. 2px progress while busy.</summary>
    internal sealed class StatusBar : Painted
    {
        string left = "", right = "";
        public double? Progress;

        public string LeftText { get { return left; } set { left = value ?? ""; Invalidate(); } }
        public string RightText { get { return right; } set { right = value ?? ""; Invalidate(); } }

        protected override void Draw(Graphics g)
        {
            Theme.HLine(g, Theme.Line, 0, Width, 0);
            if (Progress.HasValue)
            {
                Theme.Meter(g, 0, 0, Width, Theme.S(2), Progress.Value, Theme.Subtle, Theme.Fg);
            }
            float pad = Theme.S(32), cy = Height / 2f + Theme.S(1);
            var f = Theme.Mono(11);
            float rw = Theme.LabelRight(g, Theme.Lc(right), f, Theme.Muted, Width - pad, cy);
            Theme.Rect(g, Theme.Fg, pad, cy - Theme.S(2.5f), Theme.S(5), Theme.S(5));
            float x = pad + Theme.S(15);
            Theme.Text(g, Theme.Lc(left), f, Theme.Muted, new RectangleF(x, 0, Math.Max(0, Width - pad - rw - Theme.S(24) - x), Height), StringAlignment.Near, StringAlignment.Center);
        }
    }

    /// <summary>
    /// The headline: an eyebrow, a huge number and a sentence on one baseline, a summary, a stamp,
    /// and any notes from the scan underneath.
    /// </summary>
    internal sealed class Hero : Painted
    {
        public string Eyebrow = "the missing space";
        public string Number;     // "7.2 GB" (drawn huge), or null for a sentence on its own
        public string Sentence = "";
        public string Strong;     // first part of the summary, in white
        public string Summary;    // the rest, muted
        public string StampText;
        public readonly List<string> Notes = new List<string>();
        /// <summary>0 = full size, 1 = compact, 2 = tight (short screens: smaller, no notes).</summary>
        public int Tier;
        bool Compact { get { return Tier > 0; } }

        float HugePx { get { return Tier == 0 ? 104 : Tier == 1 ? 80 : 60; } }
        float SentencePx { get { return Tier == 0 ? 43 : Tier == 1 ? 34 : 28; } }
        float TopPad { get { return Tier == 2 ? 14 : 26; } }
        int MaxNotes { get { return Tier == 2 ? 0 : Tier == 1 ? 1 : 2; } }

        public int Measure(int width)
        {
            float h = Theme.S(TopPad) + Theme.S(16 + 3) + Theme.S(HugePx * 1.15f) + Theme.S(7 + 24) + Theme.S(Tier == 0 ? 36 : Tier == 1 ? 20 : 14);
            h += Math.Min(MaxNotes, Notes.Count) * Theme.S(18);
            return (int)Math.Ceiling(h);
        }

        protected override void Draw(Graphics g)
        {
            float y = Theme.S(TopPad);
            float ey = y + Theme.S(8);
            Theme.Rect(g, Theme.Fg, 0, ey, Theme.S(24), Theme.S(1));
            Theme.Caps(g, Eyebrow, Theme.Fg, Theme.S(36), ey, 12, 1.5f);
            y += Theme.S(16 + 3);

            var huge = Theme.Display(HugePx, 600);
            float lineH = Theme.S(HugePx * 1.15f);
            float baseline = Theme.BaselineIn(huge, y, lineH);
            var sentence = Theme.Display(SentencePx, 400);
            float x = 0;
            if (!string.IsNullOrEmpty(Number))
            {
                string num, unit;
                Theme.SplitBytes(Number, out num, out unit);
                x += Theme.OnBaseline(g, num, huge, Theme.Fg, x, baseline);
                if (unit.Length > 0) x += Theme.S(Compact ? 18 : 22) + Theme.OnBaseline(g, unit, Theme.Display(HugePx * 0.46f, 600), Theme.Fg, x + Theme.S(Compact ? 18 : 22), baseline);
                x += Theme.S(Compact ? 26 : 34);
            }
            else sentence = Theme.Display(Tier == 0 ? 64 : Tier == 1 ? 52 : 40, 500);
            float stampRoom = string.IsNullOrEmpty(StampText) ? 0 : Theme.S(200);
            using (var b = new SolidBrush(Theme.Fg))
            using (var f = Theme.Format(StringAlignment.Near, StringAlignment.Near))
                g.DrawString(Sentence ?? "", sentence, b, new RectangleF(x, baseline - Theme.Ascent(sentence), Math.Max(10, Width - x - stampRoom), sentence.Height * 1.3f), f);
            y += lineH + Theme.S(7);

            float sy = y + Theme.S(12);
            float sx = 0;
            if (!string.IsNullOrEmpty(Strong)) sx += Theme.Label(g, Strong, Theme.Sans(15, true), Theme.Fg, 0, sy) + Theme.S(10);
            if (!string.IsNullOrEmpty(Summary))
                Theme.Text(g, Summary, Theme.Sans(15), Theme.Muted, new RectangleF(sx, sy - Theme.S(12), Math.Max(10, Width - sx), Theme.S(24)));
            y += Theme.S(24);

            foreach (var n in Notes.Take(MaxNotes))
            {
                y += Theme.S(18);
                Theme.Text(g, Theme.Lc(n), Theme.Mono(12), Theme.Muted, new RectangleF(0, y - Theme.S(12), Width, Theme.S(18)));
            }

            if (!string.IsNullOrEmpty(StampText))
                Theme.Stamp(g, StampText, Theme.Muted, Width - Theme.S(4), Theme.S(TopPad + 16));

            Theme.HLine(g, Theme.Line, 0, Width, Height - Theme.S(1));
        }
    }
}
