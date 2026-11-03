using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>
    /// A scrolling list of custom-painted rows with a 2px scrollbar, mouse + keyboard selection
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

        bool Scrolls { get { return contentHeight > Height; } }
        int ScrollbarSpace { get { return Scrolls ? Theme.Si(12) : 0; } }

        public void Relayout()
        {
            int n = ItemCount;
            tops = new int[n];
            heights = new int[n];
            int y = 0, w = Math.Max(10, Width);
            for (int i = 0; i < n; i++)
            {
                tops[i] = y;
                heights[i] = MeasureItem(i, w);
                y += heights[i];
            }
            contentHeight = y;
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
            else if (tops[i] + heights[i] > targetY + Height) ScrollTo(tops[i] + heights[i] - Height, true);
        }

        public virtual void Tick(float now)
        {
            if (Math.Abs(targetY - scrollY) > 0.5f)
            {
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
            if (!Scrolls) return RectangleF.Empty;
            float trackH = Height;
            float h = Math.Max(Theme.S(30), trackH * Height / contentHeight);
            float y = (trackH - h) * (scrollY / Math.Max(1, MaxScroll));
            return new RectangleF(Width - Theme.S(2), y, Theme.S(2), h);
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
                Theme.Rect(g, Theme.Line, thumb.X, 0, thumb.Width, Height);
                Theme.Rect(g, draggingThumb ? Theme.Fg : Theme.Muted, thumb.X, thumb.Y, thumb.Width, thumb.Height);
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
                var thumb = ThumbRect();
                float frac = (e.Y - dragOffset) / Math.Max(1, Height - thumb.Height);
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

        /// <summary>Selected = inverted; hover = a surface shift; a hairline under every row.</summary>
        protected void RowBack(Graphics g, RectangleF r, bool selected, bool hover)
        {
            if (selected) Theme.Rect(g, Theme.Fg, r.X, r.Y, r.Width, r.Height - Theme.S(1));
            else if (hover) Theme.Rect(g, Theme.Hover, r.X, r.Y, r.Width, r.Height - Theme.S(1));
            Theme.HLine(g, Theme.Line, r.X, r.Right, r.Bottom - Theme.S(1));
            if (selected && Focused && ShowFocusCues)
                Theme.Box(g, Theme.Bg, RectangleF.Inflate(new RectangleF(r.X, r.Y, r.Width, r.Height - Theme.S(1)), -Theme.S(3), -Theme.S(3)));
        }
    }

    /// <summary>The suspects: ranked places (most likely first), then the system-space rows.</summary>
    internal sealed class SuspectList : ScrollList
    {
        sealed class Entry
        {
            public string Header, HeaderNote;
            public Culprit C;
            public SystemArea A;
        }

        readonly List<Entry> entries = new List<Entry>();
        public string EmptyTitle = "No suspects.";
        public string EmptyText = "";

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
            foreach (var c in culprits) entries.Add(new Entry { C = c });
            var asx = areas.ToList();
            if (asx.Count > 0)
                entries.Add(new Entry { Header = "system space", HeaderNote = "restore points · hibernation · page file · recycle bin" });
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
            return entries[index].Header != null ? Theme.Si(44) : Theme.Si(65);
        }

        // the four columns: # / likely / added / name + location
        public static float ColRank { get { return Theme.S(14); } }
        public static float ColLikely { get { return ColRank + Theme.S(24 + 12); } }
        public static float ColAdded { get { return ColLikely + Theme.S(68 + 12); } }
        public static float ColName { get { return ColAdded + Theme.S(78 + 12); } }

        protected override void PaintItem(Graphics g, int index, RectangleF r, bool selected, bool hover)
        {
            var e = entries[index];
            if (e.Header != null)
            {
                float hy = r.Bottom - Theme.S(14);
                float w = Theme.Caps(g, e.Header, Theme.Fg, r.X + ColRank, hy, 11, 1.2f);
                Theme.Text(g, e.HeaderNote, Theme.Mono(11), Theme.Muted, new RectangleF(r.X + ColRank + w + Theme.S(16), hy - Theme.S(9), Math.Max(0, r.Right - (r.X + ColRank + w + Theme.S(16)) - Theme.S(14)), Theme.S(18)));
                Theme.HLine(g, Theme.Line, r.X, r.Right, r.Bottom - Theme.S(1));
                return;
            }
            RowBack(g, r, selected, hover);
            Color ink = selected ? Theme.Bg : Theme.Fg;
            Color sub = selected ? Theme.Bg : Theme.Muted;
            string rank, likely = null, added, name, path, tag;
            double share = 0;
            bool small = false;
            if (e.C != null)
            {
                var c = e.C;
                rank = c.Kind == CulpritKind.Scattered ? "··" : c.Rank.ToString("00");
                likely = Format.Percent(c.Likelihood);
                share = c.Likelihood;
                added = c.AddedBytes > 0 ? "+" + Format.Bytes(c.AddedBytes)
                      : c.EstimateBytes > 0 ? "~" + Format.Bytes(c.EstimateBytes)
                      : c.PossibleBytes > 0 ? "≤" + Format.Bytes(c.PossibleBytes)
                      : "±0";
                small = c.AddedBytes == 0 && c.EstimateBytes == 0;
                name = Names.Of(c);
                path = c.Kind == CulpritKind.Folder ? c.Path : c.Kind == CulpritKind.SystemArea ? c.Note : c.Label;
                tag = Names.Tag(c);
            }
            else
            {
                var a = e.A;
                rank = "—";
                added = a.Delta.HasValue ? (a.Delta.Value == 0 ? "±0" : (a.Delta.Value > 0 ? "+" : "−") + Format.Bytes(Math.Abs(a.Delta.Value))) : "new";
                small = !a.Delta.HasValue || a.Delta.Value == 0;
                name = a.Drive + " " + Names.Area(a.Name);
                path = "uses " + Format.Bytes(a.CurrentBytes) + " now" + (a.PreviousUtc.HasValue ? " · compared with " + Theme.Day(a.PreviousUtc.Value) : " · first measurement");
                tag = a.ActiveInWindow ? "busy" : "system";
            }

            float cy = r.Y + r.Height / 2;
            Theme.Label(g, rank, Theme.Display(16, 400), ink, r.X + ColRank, cy);
            if (likely != null)
            {
                Theme.Label(g, likely, Theme.Mono(14, true), ink, r.X + ColLikely, cy - Theme.S(6));
                float tw = Theme.S(48), ty = cy + Theme.S(8);
                Theme.Rect(g, selected ? Theme.Muted : Theme.Line, r.X + ColLikely, ty, tw, Theme.S(3));
                Theme.Rect(g, ink, r.X + ColLikely, ty, (float)(tw * Math.Max(0, Math.Min(1, share))), Theme.S(3));
            }
            if (small) Theme.Label(g, added, Theme.Mono(13, true), ink, r.X + ColAdded, cy, ColName - ColAdded - Theme.S(6));
            else Theme.Label(g, added, Theme.Display(19, 500), ink, r.X + ColAdded, cy, ColName - ColAdded - Theme.S(6));

            float nx = r.X + ColName, right = r.Right - Theme.S(14);
            float tagW = 0;
            var tf = Theme.Mono(11);
            if (!string.IsNullOrEmpty(tag) && right - nx - (Theme.Width(g, tag, tf) + Theme.S(10)) < Theme.S(150)) tag = null;
            if (!string.IsNullOrEmpty(tag))
            {
                tagW = Theme.Width(g, tag, tf) + Theme.S(10);
                var tr = new RectangleF(right - tagW, cy - Theme.S(10) - Theme.S(8), tagW, Theme.S(16));
                Theme.Box(g, ink, tr);
                Theme.Text(g, tag, tf, ink, tr, StringAlignment.Center);
            }
            Theme.Text(g, name, Theme.Sans(16, true), ink, new RectangleF(nx, cy - Theme.S(10) - Theme.S(11), Math.Max(0, right - tagW - Theme.S(8) - nx), Theme.S(22)));
            Theme.Text(g, path, Theme.Mono(12), sub, new RectangleF(nx, cy + Theme.S(10) - Theme.S(9), Math.Max(0, right - nx), Theme.S(18)),
                StringAlignment.Near, StringAlignment.Center, e.C != null && e.C.Kind == CulpritKind.Folder);
        }

        protected override void PaintEmpty(Graphics g)
        {
            float y = Theme.S(28);
            Theme.Text(g, EmptyTitle, Theme.Display(28, 500), Theme.Fg, new RectangleF(Theme.S(14), y, Width - Theme.S(28), Theme.S(40)), StringAlignment.Near, StringAlignment.Near);
            float th = Theme.WrappedHeight(g, EmptyText, Theme.Sans(14), Width - Theme.S(28));
            Theme.Text(g, EmptyText, Theme.Sans(14), Theme.Muted, new RectangleF(Theme.S(14), y + Theme.S(50), Width - Theme.S(28), th + Theme.S(4)),
                StringAlignment.Near, StringAlignment.Near, false, true);
        }
    }

    /// <summary>Short names and one-word tags for the suspects.</summary>
    internal static class Names
    {
        public static string Of(Culprit c)
        {
            if (c.Kind == CulpritKind.Scattered) return "Little bits, here and there";
            if (c.Kind == CulpritKind.SystemArea) return Area(c.Label);
            return Labeler.FriendlyName(c);
        }

        /// <summary>"System Restore points / shadow copies" → "Restore points", "Page file (pagefile.sys)" → "Page file".</summary>
        public static string Area(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name.StartsWith("System Restore", StringComparison.OrdinalIgnoreCase)) return "Restore points";
            int paren = name.IndexOf(" (");
            return paren > 0 ? name.Substring(0, paren) : name;
        }

        public static string Tag(Culprit c)
        {
            if (c.Kind == CulpritKind.Scattered) return "scattered";
            if (c.Kind == CulpritKind.SystemArea) return c.NewFiles > 0 ? (c.NewFiles == 1 ? "new restore point" : c.NewFiles + " new restore points") : "system";
            if (c.EstimateBytes > 0 && c.AddedBytes == 0) return "estimate";
            if (c.HiddenReason != null) return "hidden";
            if (c.IsNewFolder) return "new folder";
            var cat = Labeler.CategoryOf(c);
            if (cat == Category.Caches) return "cache";
            if (c.NewFiles == 0 && c.GrewFiles > 0) return "grew";
            return "new files";
        }
    }

    /// <summary>The biggest additions inside the selected suspect: name on the left, size on the right.</summary>
    internal sealed class FileList : ScrollList
    {
        List<ChangedFile> files = new List<ChangedFile>();

        protected override Color Ground { get { return Theme.Surface; } }
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

        protected override int MeasureItem(int index, int width) { return Theme.Si(31); }

        protected override void PaintItem(Graphics g, int index, RectangleF r, bool selected, bool hover)
        {
            var f = files[index];
            if (selected) Theme.Rect(g, Theme.Fg, r.X, r.Y, r.Width, r.Height - Theme.S(1));
            else if (hover) Theme.Rect(g, Theme.Hover, r.X, r.Y, r.Width, r.Height - Theme.S(1));
            Theme.HLine(g, Theme.Subtle, r.X, r.Right, r.Bottom - Theme.S(1));
            Color ink = selected ? Theme.Bg : Theme.Fg, sub = selected ? Theme.Bg : Theme.Muted;
            var mono = Theme.Mono(11);
            float cy = r.Y + r.Height / 2, right = r.Right - Theme.S(selected || hover ? 6 : 0);
            string size, kind;
            switch (f.Kind)
            {
                case ChangeKind.New: size = Format.Bytes(f.SizeOnDisk); kind = "new"; break;
                case ChangeKind.GrewKnown: size = "+" + Format.Bytes(f.AddedBytes); kind = "grew"; break;
                default: size = Format.Bytes(f.SizeOnDisk); kind = "changed"; break;
            }
            float sw = Theme.LabelRight(g, size, mono, ink, right, cy);
            float kw = Theme.LabelRight(g, kind, mono, sub, right - sw - Theme.S(14), cy);
            float x = r.X + Theme.S(selected || hover ? 6 : 0);
            Theme.Text(g, f.Name, mono, ink, new RectangleF(x, cy - Theme.S(9), Math.Max(0, right - sw - kw - Theme.S(26) - x), Theme.S(18)));
        }

        protected override void PaintEmpty(Graphics g)
        {
            Theme.Label(g, "no individual files to show for this one.", Theme.Mono(11), Theme.Muted, 0, Theme.S(16));
        }
    }
}
