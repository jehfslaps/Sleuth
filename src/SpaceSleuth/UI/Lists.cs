using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>
    /// A scrolling list of custom-painted rows with a thin square scrollbar, mouse + keyboard selection
    /// and double-click / Enter to activate.
    /// </summary>
    internal abstract class ScrollList : Painted, IAnimated
    {
        int[] tops = new int[0];
        int[] heights = new int[0];
        int contentHeight;
        float scrollY, targetY;
        int selected = -1, hover = -1;
        bool draggingThumb;
        float dragOffset;

        public event EventHandler SelectionChanged;
        public event EventHandler ItemActivated;

        protected ScrollList()
        {
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        protected abstract int ItemCount { get; }
        protected abstract int MeasureItem(int index, int width);
        protected abstract void PaintItem(Graphics g, int index, RectangleF r, bool selected, bool hover);
        protected virtual bool IsSelectable(int index) { return true; }
        protected virtual void PaintEmpty(Graphics g) { }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < -1 || value >= ItemCount) value = -1;
                if (value >= 0 && !IsSelectable(value)) return;
                if (value == selected) return;
                selected = value;
                EnsureVisible(value);
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        int ScrollbarSpace { get { return Theme.Si(12); } }

        public void Relayout()
        {
            int n = ItemCount;
            tops = new int[n];
            heights = new int[n];
            int y = 0, w = Math.Max(10, Width - ScrollbarSpace);
            for (int i = 0; i < n; i++)
            {
                tops[i] = y;
                heights[i] = MeasureItem(i, w);
                y += heights[i];
            }
            contentHeight = y + Theme.Si(8);
            if (selected >= n) selected = -1;
            targetY = scrollY = Clamp(scrollY);
            Invalidate();
        }

        public void ResetScroll()
        {
            scrollY = targetY = 0;
            hover = -1;
        }

        float MaxScroll { get { return Math.Max(0, contentHeight - Height); } }
        float Clamp(float v) { return Math.Max(0, Math.Min(MaxScroll, v)); }

        void ScrollTo(float y, bool animate)
        {
            targetY = Clamp(y);
            if (!animate || Theme.ReducedMotion) scrollY = targetY;
            Invalidate();
        }

        public void EnsureVisible(int i)
        {
            if (i < 0 || i >= tops.Length) return;
            if (tops[i] < targetY) ScrollTo(tops[i], true);
            else if (tops[i] + heights[i] > targetY + Height) ScrollTo(tops[i] + heights[i] - Height + Theme.Si(8), true);
        }

        public virtual void Tick(float now)
        {
            if (Math.Abs(targetY - scrollY) > 0.5f)
            {
                // quick and hard: no floaty easing
                scrollY += (targetY - scrollY) * 0.5f;
                if (Math.Abs(targetY - scrollY) <= 0.5f) scrollY = targetY;
                Invalidate();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        RectangleF ItemRect(int i)
        {
            return new RectangleF(0, tops[i] - scrollY, Width - ScrollbarSpace, heights[i]);
        }

        RectangleF ThumbRect()
        {
            if (contentHeight <= Height) return RectangleF.Empty;
            float trackH = Height - Theme.S(4);
            float h = Math.Max(Theme.S(36), trackH * Height / contentHeight);
            float y = Theme.S(2) + (trackH - h) * (scrollY / Math.Max(1, MaxScroll));
            return new RectangleF(Width - Theme.S(5), y, Theme.S(3), h);
        }

        protected override void Draw(Graphics g)
        {
            if (ItemCount == 0)
            {
                PaintEmpty(g);
                return;
            }
            for (int i = 0; i < tops.Length; i++)
            {
                var r = ItemRect(i);
                if (r.Bottom < -Theme.S(10)) continue;
                if (r.Top > Height) break;
                PaintItem(g, i, r, i == selected, i == hover);
            }
            var thumb = ThumbRect();
            if (!thumb.IsEmpty)
            {
                Theme.Line(g, Theme.Hair2, Theme.S(1), thumb.X + thumb.Width / 2, 0, thumb.X + thumb.Width / 2, Height);
                Theme.Rect(g, draggingThumb ? Theme.Red : Theme.Ink, thumb.X, thumb.Y, thumb.Width, thumb.Height);
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < tops.Length; i++)
                if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollTo(targetY - e.Delta / 120f * Theme.S(90), true);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var thumb = ThumbRect();
            if (!thumb.IsEmpty && e.X >= Width - ScrollbarSpace)
            {
                if (thumb.Y <= e.Y && e.Y <= thumb.Bottom)
                {
                    draggingThumb = true;
                    dragOffset = e.Y - thumb.Y;
                }
                else ScrollTo(targetY + (e.Y < thumb.Y ? -Height : Height) * 0.9f, true);
                base.OnMouseDown(e);
                return;
            }
            int i = HitTest(e.Location);
            if (i >= 0 && IsSelectable(i)) SelectedIndex = i;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (draggingThumb)
            {
                float trackH = Height - Theme.S(4);
                var thumb = ThumbRect();
                float frac = (e.Y - dragOffset - Theme.S(2)) / Math.Max(1, trackH - thumb.Height);
                ScrollTo(frac * MaxScroll, false);
            }
            else
            {
                int h = HitTest(e.Location);
                if (h >= 0 && !IsSelectable(h)) h = -1;
                if (h != hover) { hover = h; Invalidate(); }
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (draggingThumb) { draggingThumb = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (e.Button == MouseButtons.Left && i >= 0 && i == selected && ItemActivated != null) ItemActivated(this, EventArgs.Empty);
            base.OnMouseDoubleClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int n = ItemCount;
            switch (e.KeyCode)
            {
                case Keys.Down: MoveBy(1); e.Handled = true; break;
                case Keys.Up: MoveBy(-1); e.Handled = true; break;
                case Keys.PageDown: MoveBy(6); e.Handled = true; break;
                case Keys.PageUp: MoveBy(-6); e.Handled = true; break;
                case Keys.Home: SelectFirstFrom(0, 1); e.Handled = true; break;
                case Keys.End: SelectFirstFrom(n - 1, -1); e.Handled = true; break;
                case Keys.Enter:
                    if (selected >= 0 && ItemActivated != null) ItemActivated(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
            base.OnKeyDown(e);
        }

        void MoveBy(int delta)
        {
            int step = Math.Sign(delta), remaining = Math.Abs(delta);
            int i = selected < 0 ? (step > 0 ? -1 : ItemCount) : selected;
            int last = selected;
            while (remaining > 0)
            {
                i += step;
                if (i < 0 || i >= ItemCount) break;
                if (IsSelectable(i)) { last = i; remaining--; }
            }
            if (last >= 0) SelectedIndex = last;
        }

        void SelectFirstFrom(int start, int step)
        {
            for (int i = start; i >= 0 && i < ItemCount; i += step)
                if (IsSelectable(i)) { SelectedIndex = i; return; }
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        /// <summary>Row background: mint for the current row (like a selected file in MERCY's bake list), a faint wash on hover.</summary>
        protected static void RowBack(Graphics g, RectangleF r, bool selected, bool hover)
        {
            if (selected) Theme.Rect(g, Theme.Mint, r.X, r.Y, r.Width, r.Height - Theme.S(1));
            else if (hover) Theme.Rect(g, Theme.HoverRow, r.X, r.Y, r.Width, r.Height - Theme.S(1));
            Theme.Line(g, Theme.Hair2, Theme.S(1), r.X, r.Bottom - 0.5f, r.Right, r.Bottom - 0.5f);
        }
    }

    internal enum ListMood { Idle, Scanning, Empty, Stopped, Error }

    /// <summary>The ranked places (most likely first), then the system-space rows.</summary>
    internal sealed class ResultList : ScrollList
    {
        sealed class Entry
        {
            public string Header, HeaderNote;
            public Culprit C;
            public SystemArea A;
        }

        readonly List<Entry> entries = new List<Entry>();
        public ListMood State = ListMood.Idle;
        public string MoodTitle = "ready.";
        public string MoodText = "pick a drive and a time window, then press find my space.";

        protected override int ItemCount { get { return entries.Count; } }

        public object SelectedTag
        {
            get
            {
                int i = SelectedIndex;
                if (i < 0 || i >= entries.Count) return null;
                return (object)entries[i].C ?? entries[i].A;
            }
        }

        public void SetResults(IEnumerable<Culprit> culprits, IEnumerable<SystemArea> areas)
        {
            entries.Clear();
            var cs = culprits.ToList();
            var asx = areas.ToList();
            if (cs.Count > 0)
                entries.Add(new Entry { Header = "most likely → least likely.", HeaderNote = cs.Count(c => c.Kind != CulpritKind.Scattered).ToString("00") + " places" });
            foreach (var c in cs) entries.Add(new Entry { C = c });
            if (asx.Count > 0)
                entries.Add(new Entry { Header = "system space.", HeaderNote = "restore points · hibernation · page file · recycle bin" });
            foreach (var a in asx) entries.Add(new Entry { A = a });
            ResetScroll();
            Relayout();
            SelectFirst();
        }

        public void SelectFirst()
        {
            for (int i = 0; i < entries.Count; i++)
                if (IsSelectable(i)) { SelectedIndex = i; return; }
            SelectedIndex = -1;
        }

        public void Clear()
        {
            entries.Clear();
            SelectedIndex = -1;
            ResetScroll();
            Relayout();
        }

        protected override bool IsSelectable(int index) { return entries[index].Header == null; }

        protected override int MeasureItem(int index, int width)
        {
            return entries[index].Header != null ? Theme.Si(index == 0 ? 34 : 54) : Theme.Si(80);
        }

        public override void Tick(float now)
        {
            base.Tick(now);
            if (ItemCount == 0 && State == ListMood.Scanning && !Theme.ReducedMotion) Invalidate();
        }

        protected override void PaintItem(Graphics g, int index, RectangleF r, bool selected, bool hover)
        {
            var e = entries[index];
            if (e.Header != null)
            {
                float cy = r.Bottom - Theme.S(14);
                float w = Theme.Label(g, e.Header, Theme.G(16, 600), Theme.Ink, r.X, cy);
                Theme.Label(g, e.HeaderNote, Theme.M(9.5f), Theme.Grey, r.X + w + Theme.S(12), cy);
                Theme.Line(g, Theme.Hair, Theme.S(1), r.X, r.Bottom - 0.5f, r.Right, r.Bottom - 0.5f);
                return;
            }
            RowBack(g, r, selected, hover);
            if (e.C != null) PaintCulprit(g, e.C, r, selected);
            else PaintArea(g, e.A, r, selected);
        }

        static Color InkOn(bool selected) { return selected ? Theme.OnMint : Theme.Ink; }
        static Color Ink2On(bool selected) { return selected ? Theme.OnMint2 : Theme.Ink2; }
        static Color GreyOn(bool selected) { return selected ? Theme.OnMint2 : Theme.Grey; }

        void PaintCulprit(Graphics g, Culprit c, RectangleF r, bool sel)
        {
            float x0 = r.X + Theme.S(8), line1 = r.Y + Theme.S(21), line2 = r.Y + Theme.S(45), line3 = r.Y + Theme.S(62);
            float rightW = Theme.S(176), right = r.Right - Theme.S(12);
            float textRight = right - rightW;

            Theme.Label(g, c.Kind == CulpritKind.Scattered ? "··" : c.Rank.ToString("00"), Theme.M(10), GreyOn(sel), x0, line1);
            Theme.Toner(g, Theme.For(c), new RectangleF(x0 + Theme.S(30), line1 - Theme.S(5), Theme.S(10), Theme.S(10)), c.Rank + 11);

            string name;
            if (c.Kind == CulpritKind.Folder)
            {
                string p = c.Path.TrimEnd('\\');
                int cut = p.LastIndexOf('\\');
                name = cut >= 0 && cut < p.Length - 1 ? p.Substring(cut + 1) : c.Path;
            }
            else name = c.Kind == CulpritKind.SystemArea ? c.Path : "little bits, here and there";
            float nx = x0 + Theme.S(50);
            Theme.Text(g, Theme.Lc(name), Theme.G(19, 550), InkOn(sel), new RectangleF(nx, line1 - Theme.S(14), textRight - nx, Theme.S(28)));

            string sub = c.Kind == CulpritKind.Folder ? c.Path : c.Kind == CulpritKind.SystemArea ? c.Note : c.Label;
            Theme.Text(g, Theme.Lc(sub), Theme.M(10), Ink2On(sel), new RectangleF(nx, line2 - Theme.S(8), textRight - nx, Theme.S(16)),
                StringAlignment.Near, StringAlignment.Center, c.Kind == CulpritKind.Folder);

            // tags
            var tags = new List<KeyValuePair<string, Color>>();
            tags.Add(MkTag(Labeler.CategoryName(Labeler.CategoryOf(c)), GreyOn(sel)));
            if (c.IsNewFolder) tags.Add(MkTag("new folder", GreyOn(sel)));
            if (c.HiddenReason != null && c.Kind == CulpritKind.Folder) tags.Add(MkTag("hidden spot", Theme.Red));
            if (c.Kind == CulpritKind.SystemArea && c.NewFiles > 0) tags.Add(MkTag(c.NewFiles == 1 ? "new restore point" : c.NewFiles + " new restore points", Theme.Red));
            if (c.EstimateBytes > 0 && c.AddedBytes == 0) tags.Add(MkTag("estimate", GreyOn(sel)));
            if (c.Kind == CulpritKind.Folder && !string.IsNullOrEmpty(c.Contents)) tags.Add(MkTag(ShortContents(c.Contents), GreyOn(sel)));
            if (c.Confidence != "High" && c.Kind != CulpritKind.Scattered) tags.Add(MkTag("partly a guess", GreyOn(sel)));
            if (c.Kind == CulpritKind.Scattered) tags.Add(MkTag((c.NewFiles + c.GrewFiles).ToString("N0") + " files", GreyOn(sel)));
            tags.Add(MkTag(WhenText(c), GreyOn(sel)));
            DrawTags(g, tags, nx, line3, textRight, sel);

            // right: size + likelihood meter
            string amount = c.AddedBytes > 0 ? Format.Bytes(c.AddedBytes)
                          : c.EstimateBytes > 0 ? "~" + Format.Bytes(c.EstimateBytes)
                          : c.PossibleBytes > 0 ? "up to " + Format.Bytes(c.PossibleBytes)
                          : c.Kind == CulpritKind.SystemArea ? "about the same" : "—";
            Theme.LabelRight(g, Theme.Lc(amount), Theme.M(c.AddedBytes > 0 || c.EstimateBytes > 0 ? 16 : 12, true), InkOn(sel), right, line1);
            float mw = Theme.S(104);
            Theme.Meter(g, right - mw - Theme.S(44), line2, mw, c.Likelihood, false, InkOn(sel));
            Theme.LabelRight(g, Format.Percent(c.Likelihood), Theme.M(10), Ink2On(sel), right, line2);
        }

        void PaintArea(Graphics g, SystemArea a, RectangleF r, bool sel)
        {
            float x0 = r.X + Theme.S(8), line1 = r.Y + Theme.S(21), line2 = r.Y + Theme.S(45), line3 = r.Y + Theme.S(62);
            float right = r.Right - Theme.S(12), textRight = right - Theme.S(176);
            Theme.Label(g, "--", Theme.M(10), GreyOn(sel), x0, line1);
            Theme.Toner(g, Theme.For(Category.System), new RectangleF(x0 + Theme.S(30), line1 - Theme.S(5), Theme.S(10), Theme.S(10)), 5);
            float nx = x0 + Theme.S(50);
            Theme.Text(g, Theme.Lc(a.Drive + " " + a.Name), Theme.G(19, 550), InkOn(sel), new RectangleF(nx, line1 - Theme.S(14), textRight - nx, Theme.S(28)));
            Theme.Text(g, "uses " + Theme.Lc(Format.Bytes(a.CurrentBytes)) + " now" + (a.PreviousUtc.HasValue ? " · vs. " + Theme.Lc(Format.When(a.PreviousUtc.Value)) : " · first look"),
                Theme.M(10), Ink2On(sel), new RectangleF(nx, line2 - Theme.S(8), textRight - nx, Theme.S(16)));
            var tags = new List<KeyValuePair<string, Color>> { MkTag("system", GreyOn(sel)) };
            if (a.ActiveInWindow) tags.Add(MkTag("busy in this window", Theme.Red));
            if (!string.IsNullOrEmpty(a.Activity)) tags.Add(MkTag(a.Activity, GreyOn(sel)));
            DrawTags(g, tags, nx, line3, textRight, sel);

            string change = a.Delta.HasValue ? (a.Delta.Value == 0 ? "no change" : Format.SignedBytes(a.Delta.Value)) : "first look";
            bool grew = a.Delta.HasValue && a.Delta.Value > 0;
            Theme.LabelRight(g, Theme.Lc(change), Theme.M(grew ? 16 : 12, true), grew ? (sel ? Theme.OnMint : Theme.Red) : GreyOn(sel), right, line1);
        }

        static KeyValuePair<string, Color> MkTag(string s, Color c) { return new KeyValuePair<string, Color>(Theme.Lc(s), c); }

        static void DrawTags(Graphics g, List<KeyValuePair<string, Color>> tags, float x, float cy, float right, bool sel)
        {
            var f = Theme.M(9.5f);
            for (int i = 0; i < tags.Count && x < right - Theme.S(20); i++)
            {
                if (i > 0) x += Theme.Label(g, " · ", f, sel ? Theme.OnMint2 : Theme.Grey, x, cy);
                x += Theme.Label(g, tags[i].Key, f, tags[i].Value, x, cy, right - x);
            }
        }

        static string WhenText(Culprit c)
        {
            if (c.Kind == CulpritKind.SystemArea) return "since last scan";
            string a = Format.When(c.FirstUtc), b = Format.When(c.LastUtc);
            return a == b ? b : a + " → " + b;
        }

        static string ShortContents(string s)
        {
            // "Mostly audio samples / instrument data (.wav 84%)" -> "mostly .wav (84%)"
            int open = s.LastIndexOf('(');
            if (s.StartsWith("Mostly") && open > 0 && s.EndsWith(")"))
            {
                string inner = s.Substring(open + 1, s.Length - open - 2);
                int sp = inner.LastIndexOf(' ');
                return sp > 0 ? "mostly " + inner.Substring(0, sp) + " (" + inner.Substring(sp + 1) + ")" : "mostly " + inner;
            }
            return s.Length > 40 ? s.Substring(0, 38) + "…" : s;
        }

        protected override void PaintEmpty(Graphics g)
        {
            float x = 0, y = Theme.S(26);
            Theme.Split(g, MoodTitle, Theme.W(34), Theme.Ink, x, y);
            float ty = y + Theme.S(56);
            float th = Theme.WrappedHeight(g, Theme.Lc(MoodText), Theme.M(10.5f), Width - Theme.S(30));
            Theme.Text(g, Theme.Lc(MoodText), Theme.M(10.5f), Theme.Ink2, new RectangleF(x, ty, Width - Theme.S(30), th + Theme.S(4)),
                StringAlignment.Near, StringAlignment.Near, false, true);

            // a field of "scanned" grey sound blobs; while scanning they step across, hard
            float top = ty + th + Theme.S(28);
            if (top > Height - Theme.S(60)) return;
            int shift = State == ListMood.Scanning && !Theme.ReducedMotion ? (int)(Theme.Now * 6) : 0;
            float rowH = Theme.S(34), blobW = Theme.S(86);
            int rows = Math.Min(6, (int)((Height - top - Theme.S(20)) / (rowH + Theme.S(10))));
            for (int row = 0; row < rows; row++)
            {
                float yy = top + row * (rowH + Theme.S(10));
                float offset = ((shift + row * 3) % 8) * Theme.S(12) * (row % 2 == 0 ? 1 : -1);
                for (float bx = -blobW + offset; bx < Width - Theme.S(20); bx += blobW + Theme.S(14))
                    Waves.Draw(g, new RectangleF(bx, yy, blobW, rowH), Theme.Scan, row * 31 + (int)(bx / Theme.S(1)), null);
            }
            if (State == ListMood.Scanning)
            {
                // the red dotted read head
                float hx = Theme.S(40) + ((shift * 37) % Math.Max(1, (int)(Width - Theme.S(80))));
                Theme.Dotted(g, Theme.Red, Theme.S(1), hx, top - Theme.S(10), hx, top + rows * (rowH + Theme.S(10)), 2, 3);
                Theme.Label(g, Theme.Coord(hx, top), Theme.M(8), Theme.Ink, hx + Theme.S(6), top - Theme.S(4));
            }
        }
    }

    /// <summary>The files inside the selected place.</summary>
    internal sealed class FileList : ScrollList
    {
        List<ChangedFile> files = new List<ChangedFile>();

        protected override int ItemCount { get { return files.Count; } }

        public ChangedFile SelectedFile
        {
            get { return SelectedIndex >= 0 && SelectedIndex < files.Count ? files[SelectedIndex] : null; }
        }

        public void SetFiles(List<ChangedFile> list)
        {
            files = list ?? new List<ChangedFile>();
            SelectedIndex = -1;
            ResetScroll();
            Relayout();
        }

        protected override int MeasureItem(int index, int width) { return Theme.Si(48); }

        protected override void PaintItem(Graphics g, int index, RectangleF r, bool selected, bool hover)
        {
            var f = files[index];
            RowBack(g, r, selected, hover);
            Color ink = selected ? Theme.OnMint : Theme.Ink, ink2 = selected ? Theme.OnMint2 : Theme.Ink2;
            float x = r.X + Theme.S(6), right = r.Right - Theme.S(8);
            float line1 = r.Y + Theme.S(16), line2 = r.Y + Theme.S(33);
            float rightW = Theme.S(130);
            Theme.Text(g, Theme.Lc(f.Name), Theme.G(14.5f, 500), ink, new RectangleF(x, line1 - Theme.S(11), right - rightW - x, Theme.S(22)));
            Theme.Text(g, Theme.Lc(Format.When(f.TimeUtc) + "  ·  " + f.Folder), Theme.M(9.5f), ink2,
                new RectangleF(x, line2 - Theme.S(8), right - rightW - x, Theme.S(16)), StringAlignment.Near, StringAlignment.Center, true);
            Theme.LabelRight(g, Theme.Lc(Format.Bytes(f.SizeOnDisk)), Theme.M(12, true), ink, right, line1);

            string change; Color sq;
            switch (f.Kind)
            {
                case ChangeKind.New: change = "new"; sq = Theme.Mint; break;
                case ChangeKind.GrewKnown: change = "grew " + Theme.Lc(Format.SignedBytes(f.AddedBytes)); sq = Theme.Yellow; break;
                default: change = "modified"; sq = Theme.Scan; break;
            }
            float cw = Theme.LabelRight(g, change, Theme.M(9.5f), ink2, right, line2);
            Theme.Rect(g, selected && sq == Theme.Mint ? Theme.OnMint : sq, right - cw - Theme.S(11), line2 - Theme.S(3.5f), Theme.S(7), Theme.S(7));
        }

        protected override void PaintEmpty(Graphics g)
        {
            Theme.Label(g, "no individual files to show here.", Theme.M(10), Theme.Grey, 0, Theme.S(14));
        }
    }
}
