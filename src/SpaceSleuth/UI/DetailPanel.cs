using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>
    /// The white card on the right: what the selected place is, three stat tiles, a gentle explanation,
    /// buttons, and the files inside. Pebble the mascot peeks over its top edge.
    /// </summary>
    internal sealed class DetailPanel : Painted, IAnimated
    {
        sealed class Stat
        {
            public string Label, Value;
            public Color Back, Fore;
        }

        public readonly FileList Files = new FileList();
        public readonly PillButton OpenButton = new PillButton("Open in File Explorer", PillKind.Secondary);
        public readonly PillButton CopyButton = new PillButton("Copy path", PillKind.Secondary);
        public Mood MascotMood = Mood.Happy;
        public string PlaceholderTitle = "Pick a card to see what's inside.";

        object current;
        string title, path, info;
        readonly List<Stat> stats = new List<Stat>();
        string filesHeader;
        bool hasFiles, showOpen, showCopy;

        // layout
        RectangleF card, titleRect, pathRect, statsRect, infoRect, filesHeaderRect;
        RectangleF mascotArea;

        public DetailPanel()
        {
            Controls.Add(Files);
            Controls.Add(OpenButton);
            Controls.Add(CopyButton);
            OpenButton.BackColor = Theme.White;
            CopyButton.BackColor = Theme.White;
            SetItem(null, null);
        }

        public object Current { get { return current; } }

        float MascotSize { get { return Theme.S(84); } }

        public void SetItem(object tag, SystemArea areaOfCulprit)
        {
            current = tag;
            stats.Clear();
            title = path = info = filesHeader = null;
            List<ChangedFile> fileList = null;
            showOpen = showCopy = false;

            var c = tag as Culprit;
            var a = tag as SystemArea;
            if (c != null)
            {
                var lines = new List<string>();
                if (c.Kind == CulpritKind.Folder)
                {
                    string p = c.Path.TrimEnd('\\');
                    int cut = p.LastIndexOf('\\');
                    title = cut >= 0 && cut < p.Length - 1 ? p.Substring(cut + 1) : c.Path;
                    path = c.Path;
                    lines.Add(c.Label + (string.IsNullOrEmpty(c.Contents) ? "." : " — " + c.Contents.Substring(0, 1).ToLowerInvariant() + c.Contents.Substring(1) + "."));
                    OpenButton.Text = "Open in File Explorer";
                    showOpen = showCopy = true;
                }
                else if (c.Kind == CulpritKind.SystemArea)
                {
                    title = c.Label;
                    path = c.Note;
                    if (areaOfCulprit != null && !string.IsNullOrEmpty(areaOfCulprit.Explanation)) lines.Add(areaOfCulprit.Explanation);
                    SetOpenText(c.OpenTarget);
                }
                else
                {
                    title = "Little bits, here and there";
                    path = c.Note;
                    lines.Add("App updates, caches and logs each added a small amount. None of them is a big culprit on its own.");
                }

                if (!string.IsNullOrEmpty(c.Note) && c.Kind == CulpritKind.Folder) lines.Add(c.Note + ".");
                if (c.HiddenReason != null && c.Kind == CulpritKind.Folder) lines.Add(c.HiddenReason + " — easy to miss.");
                if (c.GrewFiles > 0 && c.PossibleBytes > 0)
                    lines.Add(c.GrewFiles.ToString("N0") + " existing file" + (c.GrewFiles == 1 ? "" : "s") + " also grew (now " + Format.Bytes(c.PossibleBytes) +
                              " in total). Their earlier size isn't known yet, so this part is a guess — your next scan can measure it exactly.");
                info = string.Join("\n", lines);

                if (c.AddedBytes > 0)
                    AddStat("ADDED", Format.Bytes(c.AddedBytes), Theme.Forest, Theme.Lime);
                else if (c.EstimateBytes > 0)
                    AddStat("ABOUT (ESTIMATE)", "~" + Format.Bytes(c.EstimateBytes), Theme.Forest, Theme.Lime);
                else if (c.PossibleBytes > 0)
                    AddStat("MAYBE", "up to " + Format.Bytes(c.PossibleBytes), Theme.Forest, Theme.Lime);
                else
                    AddStat("ADDED", c.Kind == CulpritKind.SystemArea ? "~0" : "\u2014", Theme.Forest, Theme.Lime);
                if (c.Kind == CulpritKind.SystemArea && areaOfCulprit != null)
                    AddStat("USES NOW", Format.Bytes(areaOfCulprit.CurrentBytes), Theme.Indigo, Color.White);
                else
                    AddStat(c.NewFiles > 0 || c.GrewFiles == 0 ? "NEW FILES" : "FILES THAT GREW",
                            (c.NewFiles > 0 || c.GrewFiles == 0 ? c.NewFiles : c.GrewFiles).ToString("N0"), Theme.Indigo, Color.White);
                AddStat("LIKELIHOOD", Format.Percent(c.Likelihood), Theme.Pink, Theme.Ink);

                if (c.Files.Count > 0)
                {
                    fileList = c.Files;
                    int total = c.NewFiles + c.GrewFiles;
                    filesHeader = total > c.Files.Count
                        ? "Biggest " + c.Files.Count.ToString("N0") + " of " + total.ToString("N0") + " files"
                        : (total == 1 ? "1 file" : total.ToString("N0") + " files");
                }
            }
            else if (a != null)
            {
                title = a.Drive + "  " + a.Name;
                path = "Uses " + Format.Bytes(a.CurrentBytes) + " right now";
                AddStat("USES NOW", Format.Bytes(a.CurrentBytes), Theme.Indigo, Color.White);
                AddStat("CHANGE", a.Delta.HasValue ? (a.Delta.Value == 0 ? "none" : Format.SignedBytes(a.Delta.Value)) : "first look", Theme.Forest, Theme.Lime);
                AddStat("COMPARED WITH", a.PreviousUtc.HasValue ? Format.When(a.PreviousUtc.Value) : "next scan", Theme.Pink, Theme.Ink);
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(a.Activity)) lines.Add(a.Activity + ".");
                if (!string.IsNullOrEmpty(a.Explanation)) lines.Add(a.Explanation);
                if (!a.Delta.HasValue) lines.Add("This is the first time it's been measured — next scan will show how much it changed.");
                info = string.Join("\n", lines);
                SetOpenText(a.OpenTarget);
            }
            else
            {
                title = PlaceholderTitle;
                path = "Here's what the colors mean:";
            }

            Files.SetFiles(fileList);
            hasFiles = fileList != null;
            Files.Visible = hasFiles;
            OpenButton.Visible = showOpen;
            CopyButton.Visible = showCopy;
            PerformLayout();
            Invalidate();
        }

        void SetOpenText(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            OpenButton.Text = target.IndexOf("RecycleBin", StringComparison.OrdinalIgnoreCase) >= 0 ? "Open Recycle Bin"
                            : target.StartsWith("cleanmgr", StringComparison.OrdinalIgnoreCase) ? "Open Disk Cleanup"
                            : "Open settings";
            showOpen = true;
        }

        void AddStat(string label, string value, Color back, Color fore)
        {
            stats.Add(new Stat { Label = label, Value = value, Back = back, Fore = fore });
        }

        public void Tick(float now)
        {
            if (!Theme.ReducedMotion && mascotArea.Width > 0)
                Invalidate(Rectangle.Ceiling(mascotArea));
        }

        protected override void OnLayout(System.Windows.Forms.LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width <= 0 || Height <= 0) return;
            using (var g = CreateGraphics())
            {
                float top = Theme.S(76);
                card = new RectangleF(Theme.S(4), top, Width - Theme.S(36), Height - top - Theme.S(14));
                float x = card.X + Theme.S(24), w = card.Width - Theme.S(48);
                float y = card.Y + Theme.S(22);
                float bottom = card.Bottom - Theme.S(12);

                titleRect = new RectangleF(x, y, w, Theme.S(30));
                y += Theme.S(30);
                pathRect = new RectangleF(x, y, w, Theme.S(20));
                y += Theme.S(20) + Theme.S(14);

                int bh = Theme.Si(36);
                float buttonsW = (showOpen ? OpenButton.PreferredWidth() : 0) + (showCopy ? CopyButton.PreferredWidth() + Theme.S(8) : 0);
                float filesMin = hasFiles ? Theme.S(38 + 130) : 0;
                float buttonsRow = !hasFiles && (showOpen || showCopy) ? bh + Theme.S(14) : 0;
                float statsH = Theme.S(74);
                var infoFont = Theme.Ui(13.5f, Weight.Regular);
                float lineH = infoFont.GetHeight(g);
                float infoFull = string.IsNullOrEmpty(info) ? 0 : Theme.WrappedHeight(g, info, infoFont, w);

                // When space is tight: files and buttons win, then the stat tiles, then the explanation.
                float room = bottom - y - filesMin - buttonsRow;
                if (stats.Count > 0 && room >= statsH + Theme.S(12))
                {
                    statsRect = new RectangleF(x, y, w, statsH);
                    y += statsH + Theme.S(12);
                    room -= statsH + Theme.S(12);
                }
                else statsRect = RectangleF.Empty;

                if (infoFull > 0 && room >= lineH)
                {
                    float maxInfo = hasFiles ? Math.Min(room - Theme.S(10), lineH * 3) : room - Theme.S(10);
                    float ih = Math.Min(infoFull, Math.Max(lineH, maxInfo));
                    ih = Math.Max(lineH, (float)Math.Floor(ih / lineH + 0.05f) * lineH);
                    infoRect = new RectangleF(x, y, w, ih + Theme.S(2));
                    y += ih + Theme.S(12);
                }
                else infoRect = RectangleF.Empty;

                filesHeaderRect = RectangleF.Empty;
                bool filesFit = false;
                if (hasFiles)
                {
                    // "Files here" on the left, the buttons on the right of the same row
                    float bx = x + w - buttonsW;
                    if (showOpen)
                    {
                        OpenButton.SetBounds((int)bx, (int)(y - Theme.S(4)), OpenButton.PreferredWidth(), bh);
                        bx += OpenButton.Width + Theme.S(8);
                    }
                    if (showCopy) CopyButton.SetBounds((int)bx, (int)(y - Theme.S(4)), CopyButton.PreferredWidth(), bh);
                    filesHeaderRect = new RectangleF(x, y - Theme.S(4), Math.Max(0, w - buttonsW - Theme.S(12)), bh);
                    y += Theme.S(38);
                    float fh = bottom - y;
                    if (fh >= Theme.S(50))
                    {
                        filesFit = true;
                        Files.SetBounds((int)(card.X + Theme.S(12)), (int)y, (int)(card.Width - Theme.S(18)), (int)fh);
                    }
                }
                else
                {
                    float bx = x;
                    if (showOpen)
                    {
                        OpenButton.SetBounds((int)bx, (int)y, OpenButton.PreferredWidth(), bh);
                        bx += OpenButton.Width + Theme.S(8);
                    }
                    if (showCopy) CopyButton.SetBounds((int)bx, (int)y, CopyButton.PreferredWidth(), bh);
                }
                Files.Visible = filesFit;

                float size = MascotSize;
                mascotArea = new RectangleF(card.Right - Theme.S(110) - size * 1.0f, 0, size * 2.0f, top + size * 0.2f);
            }
        }

        protected override void Draw(Graphics g)
        {
            if (card.Width <= 0) return;
            float size = MascotSize, mcx = card.Right - Theme.S(150);
            float t = Theme.Now;
            bool anim = !Theme.ReducedMotion;

            // Confetti near the corner, then Pebble peeking over the card
            Theme.Sparkle(g, card.Right - Theme.S(40), card.Y - Theme.S(48), Theme.S(10), Theme.Purple);
            Theme.Flower(g, card.Right - Theme.S(18), card.Y - Theme.S(18), Theme.S(10), Theme.Sun, Theme.Orange, 6);
            Theme.Clover(g, card.X + Theme.S(40), card.Y - Theme.S(16), Theme.S(12), Theme.Mint);
            Mascot.DrawBody(g, mcx, card.Y - size * 0.16f, size, MascotMood, t, true, anim);

            Theme.Card(g, Theme.White, card, Theme.S(30));
            Mascot.DrawHands(g, mcx, card.Y, size, t, anim);

            Theme.Text(g, title, Theme.Ui(22, Weight.Bold), Theme.Ink, titleRect);
            Theme.Text(g, path, Theme.Ui(13, Weight.Regular), Theme.InkA(150), pathRect, System.Drawing.StringAlignment.Near, System.Drawing.StringAlignment.Center, current is Culprit && ((Culprit)current).Kind == CulpritKind.Folder);

            if (!statsRect.IsEmpty)
            {
                float gap = Theme.S(10);
                float tw = (statsRect.Width - gap * (stats.Count - 1)) / stats.Count;
                for (int i = 0; i < stats.Count; i++)
                {
                    var s = stats[i];
                    var r = new RectangleF(statsRect.X + i * (tw + gap), statsRect.Y, tw, statsRect.Height);
                    Theme.Fill(g, s.Back, r, Theme.S(22));
                    Theme.Text(g, s.Label, Theme.Ui(11.5f, Weight.Semibold), Color.FromArgb(215, s.Fore),
                        new RectangleF(r.X + Theme.S(14), r.Y + Theme.S(10), r.Width - Theme.S(24), Theme.S(18)));
                    var vf = Theme.Ui(s.Value.Length > 9 ? 19 : 26, Weight.Light);
                    Theme.Text(g, s.Value, vf, s.Fore, new RectangleF(r.X + Theme.S(8), r.Bottom - Theme.S(42), r.Width - Theme.S(20), Theme.S(36)),
                        System.Drawing.StringAlignment.Far, System.Drawing.StringAlignment.Center);
                }
            }

            if (!infoRect.IsEmpty)
                Theme.Text(g, info, Theme.Ui(13.5f, Weight.Regular), Theme.InkA(200), infoRect,
                    System.Drawing.StringAlignment.Near, System.Drawing.StringAlignment.Near, false, true);

            if (current == null)
            {
                // Color legend: one color per kind of place, used everywhere in the app.
                float lx = pathRect.X, ly = pathRect.Bottom + Theme.S(12), maxX = card.Right - Theme.S(24);
                foreach (Category cat in Enum.GetValues(typeof(Category)))
                {
                    string name = Labeler.CategoryName(cat);
                    float cw = Theme.Width(g, name, Theme.Ui(12, Weight.Medium)) + Theme.S(34);
                    if (lx + cw > maxX) { lx = pathRect.X; ly += Theme.S(32); }
                    lx += Theme.Chip(g, lx, ly, name, Theme.For(cat), cw, 26) + Theme.S(8);
                }
                if (card.Height > Theme.S(300))
                {
                    Theme.Sticker(g, new PointF(card.Right - Theme.S(150), card.Bottom - Theme.S(70)), "calm", -14, Theme.Forest, Theme.Lime, false, 18);
                    Theme.Sticker(g, new PointF(card.Right - Theme.S(80), card.Bottom - Theme.S(110)), "steady", 16, Theme.Orange, Theme.Butter, true, 18);
                    Theme.Sparkle(g, card.Right - Theme.S(215), card.Bottom - Theme.S(120), Theme.S(12), Theme.Purple);
                    Theme.Flower(g, card.Right - Theme.S(40), card.Bottom - Theme.S(40), Theme.S(13), Theme.Pink, Theme.Sun);
                }
            }

            if (!filesHeaderRect.IsEmpty)
                Theme.Text(g, filesHeader, Theme.Ui(14, Weight.Bold), Theme.Ink, filesHeaderRect);
        }
    }
}
