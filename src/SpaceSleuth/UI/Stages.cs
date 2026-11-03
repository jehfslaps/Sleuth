using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SpaceSleuth.UI
{
    /// <summary>"The suspects": the panel head, the column labels, the ranked list and a footnote.</summary>
    internal sealed class SuspectsPanel : Painted
    {
        public readonly SuspectList List = new SuspectList();
        public string Foot = "likely = share of all the growth found in this window";

        public SuspectsPanel()
        {
            Controls.Add(List);
        }

        float ListTop { get { return Theme.S(40 + 28); } }
        float FootH { get { return Theme.S(26); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            List.SetBounds(0, (int)ListTop, Width, Math.Max(10, (int)(Height - ListTop - FootH)));
        }

        protected override void Draw(Graphics g)
        {
            float cy = Theme.S(10);
            Theme.Caps(g, "the suspects", Theme.Fg, 0, cy, 14, 1.2f);
            Theme.LabelRight(g, "ranked by likelihood ↓", Theme.Mono(12), Theme.Muted, Width, cy);

            float hy = Theme.S(40) + Theme.S(14);
            Theme.Caps(g, "#", Theme.Muted, SuspectList.ColRank, hy, 11, 0.7f, false);
            Theme.Caps(g, "likely", Theme.Muted, SuspectList.ColLikely, hy, 11, 0.7f, false);
            Theme.Caps(g, "added", Theme.Muted, SuspectList.ColAdded, hy, 11, 0.7f, false);
            Theme.Caps(g, "name / location", Theme.Muted, SuspectList.ColName, hy, 11, 0.7f, false);
            Theme.HLine(g, Theme.Line, 0, Width, ListTop - Theme.S(1));

            Theme.Text(g, Foot, Theme.Mono(12), Theme.Muted, new RectangleF(0, Height - FootH + Theme.S(8), Width, Theme.S(16)));
        }
    }

    /// <summary>The empty stage: before the first scan, after stopping, or when something got in the way.</summary>
    internal sealed class FirstStage : Painted
    {
        public string Kicker = "a new case";
        public string Title = "Your space went\nsomewhere.\nLet’s follow the trail.";
        public string Body1 = "Select a drive, then press ";
        public string BodyBold = "find my space";
        public string Body2 = " above.";
        public string Body3 = "Sleuth will take a first look and save a baseline for the next investigation.";
        public string Hint = "↑   start with local disk (C:)";
        public string Index = "01";
        public string[,] Meta =
        {
            { "read-only investigation", "your files stay where they are" },
            { "a clearer picture next time", "exact growth needs two scans" },
            { "recent clues, when available", "change history can offer a lead" }
        };

        protected override void Draw(Graphics g)
        {
            bool small = Height < Theme.S(520) || Width < Theme.S(900);
            var h1 = Theme.Display(small ? 58 : 78, 500);
            float lineH = h1.Size * 1.12f;
            string[] titleLines = Title.Split('\n');
            float bodyH = Theme.S(29) * 2;
            float contentH = Theme.S(16 + 24) + titleLines.Length * lineH + Theme.S(24) + bodyH + Theme.S(24) + Theme.S(20) + Theme.S(48 + 20 + 44);
            float cw = Math.Min(Theme.S(810), Width - Theme.S(160));
            float x = Math.Max(Theme.S(16), (Width - cw) / 2 - Theme.S(60));
            float y = Math.Max(Theme.S(16), (Height - contentH) / 2);

            // the big grey case number behind, on the right
            if (!small && !string.IsNullOrEmpty(Index))
            {
                var idx = Theme.Display(190, 600);
                float iw = Theme.Width(g, Index, idx);
                Theme.OnBaseline(g, Index, idx, Theme.Subtle, Math.Min(Width - iw, x + cw + Theme.S(120) - iw), Theme.BaselineIn(idx, y + Theme.S(66), idx.Size * 1.2f));
            }

            // kicker
            float ky = y + Theme.S(8);
            Theme.Rect(g, Theme.Muted, x, ky, Theme.S(48), Theme.S(1));
            Theme.Caps(g, Kicker, Theme.Muted, x + Theme.S(64), ky, 12, 1.5f);
            y += Theme.S(16 + 24);

            using (var b = new SolidBrush(Theme.Fg))
            using (var f = Theme.Format(StringAlignment.Near, StringAlignment.Near))
                foreach (var line in titleLines)
                {
                    g.DrawString(line, h1, b, x, Theme.BaselineIn(h1, y, lineH) - Theme.Ascent(h1), f);
                    y += lineH;
                }
            y += Theme.S(24);

            var body = Theme.Sans(18);
            float bcy = y + Theme.S(14.5f);
            float bx = x + Theme.Label(g, Body1, body, Theme.Muted, x, bcy);
            if (!string.IsNullOrEmpty(BodyBold)) bx += Theme.Label(g, BodyBold, Theme.Sans(18, true), Theme.Fg, bx, bcy);
            Theme.Label(g, Body2, body, Theme.Muted, bx, bcy);
            Theme.Text(g, Body3, body, Theme.Muted, new RectangleF(x, bcy + Theme.S(29) - Theme.S(14.5f), Math.Max(10, Width - x - Theme.S(16)), Theme.S(29)));
            y += bodyH + Theme.S(24);

            if (!string.IsNullOrEmpty(Hint)) Theme.Label(g, Hint, Theme.Mono(13), Theme.Fg, x, y + Theme.S(8));
            y += Theme.S(20) + Theme.S(48);

            if (Meta != null && Meta.GetLength(0) > 0)
            {
                Theme.HLine(g, Theme.Line, x, x + cw, y);
                y += Theme.S(20);
                float mx = x;
                for (int i = 0; i < Meta.GetLength(0); i++)
                {
                    float w1 = Theme.Label(g, Meta[i, 0], Theme.Sans(14, true), Theme.Fg, mx, y + Theme.S(10));
                    float w2 = Theme.Label(g, Meta[i, 1], Theme.Mono(12), Theme.Muted, mx, y + Theme.S(30));
                    mx += Math.Max(w1, w2) + Theme.S(36);
                }
            }
        }
    }

    /// <summary>The investigation in progress: big words, a progress bar, what's being checked, and the procedure.</summary>
    internal sealed class InvestigationStage : Painted, IAnimated
    {
        public sealed class Step
        {
            public string Title, Detail;
            public bool Done, Active;
        }

        public string CaseLabel = "case 001 / in progress";
        public string DriveText = "C:";
        public double Progress;
        public string ProgressLeft = "";
        public string Elapsed = "00:00";
        public string Checking = "";
        public readonly List<KeyValuePair<string, string>> Facts = new List<KeyValuePair<string, string>>();
        public readonly List<Step> Steps = new List<Step>();
        int dots = -1;

        public void Tick(float now)
        {
            if (!Visible) return;
            int d = Theme.ReducedMotion ? 3 : (int)(now * 2.5f) % 4;
            if (d != dots) { dots = d; Invalidate(); }
        }

        protected override void Draw(Graphics g)
        {
            bool small = Height < Theme.S(520);
            float logW = Theme.S(360), gap = Theme.S(small ? 48 : 90), padX = Theme.S(80);
            if (Width < Theme.S(1100)) { padX = Theme.S(32); gap = Theme.S(40); logW = Theme.S(300); }
            float x = padX, y = Theme.S(small ? 36 : 70);
            float mainW = Width - padX * 2 - gap - logW;

            Theme.Caps(g, CaseLabel, Theme.Muted, x, y + Theme.S(8), 12, 1.5f);
            y += Theme.S(16 + 24);

            var h1 = Theme.Display(small ? 58 : 78, 500);
            float hb = Theme.BaselineIn(h1, y, h1.Size * 1.12f);
            float w = Theme.OnBaseline(g, "investigating", h1, Theme.Fg, x, hb);
            string d = new string('.', Math.Max(0, Math.Min(3, dots < 0 ? 3 : dots)));
            Theme.OnBaseline(g, d, h1, Theme.Muted, x + w, hb);
            y += h1.Size * 1.12f + Theme.S(24);

            var p = Theme.Sans(18);
            Theme.Label(g, "Following the changes on " + DriveText + ".", p, Theme.Muted, x, y + Theme.S(14));
            Theme.Label(g, "The evidence is still coming in.", p, Theme.Muted, x, y + Theme.S(42));
            y += Theme.S(56) + Theme.S(36);

            Theme.Meter(g, x, y, mainW, Theme.S(8), Progress, Theme.Subtle, Theme.Fg);
            y += Theme.S(8 + 16);
            var mono = Theme.Mono(14);
            Theme.Text(g, ProgressLeft, mono, Theme.Fg, new RectangleF(x, y, mainW - Theme.S(140), Theme.S(20)));
            Theme.LabelRight(g, Elapsed + " elapsed", mono, Theme.Fg, x + mainW, y + Theme.S(10));
            y += Theme.S(20 + 26);

            Theme.Label(g, "checking", Theme.Mono(12), Theme.Muted, x, y + Theme.S(8));
            Theme.Text(g, Checking, Theme.Mono(12), Theme.Muted, new RectangleF(x, y + Theme.S(15), mainW, Theme.S(18)), StringAlignment.Near, StringAlignment.Center, true);
            y += Theme.S(34) + Theme.S(small ? 24 : 60);

            if (y + Theme.S(80) < Height)
            {
                Theme.HLine(g, Theme.Line, x, x + mainW, y);
                y += Theme.S(20);
                float fx = x;
                foreach (var f in Facts)
                {
                    var big = Theme.Display(36, 500);
                    float w1 = Theme.OnBaseline(g, f.Key, big, Theme.Fg, fx, y + Theme.Ascent(big));
                    float w2 = Theme.Label(g, f.Value, Theme.Mono(11), Theme.Muted, fx, y + big.Height + Theme.S(8));
                    fx += Math.Max(w1, w2) + Theme.S(60);
                }
            }

            // the procedure
            float lx = x + mainW + gap, ly = Theme.S(small ? 36 : 70) + Theme.S(16);
            float logBottom = ly + Theme.S(52) + Steps.Count * Theme.S(76) + Theme.S(60);
            Theme.VLine(g, Theme.Line, lx, ly, Math.Min(Height - Theme.S(20), logBottom));
            float tx = lx + Theme.S(32), ty = ly + Theme.S(8);
            Theme.Tracked(g, "THE PROCEDURE", Theme.Mono(14, true), Theme.Fg, tx, ty, 1);
            ty += Theme.S(42);
            for (int i = 0; i < Steps.Count; i++)
            {
                var s = Steps[i];
                Color c = s.Active ? Theme.Fg : Theme.Muted;
                string t = (i + 1).ToString("00") + "   " + s.Title + (s.Done ? "  ✓" : "");
                Theme.Text(g, t, Theme.Sans(14), c, new RectangleF(tx, ty, logW - Theme.S(32), Theme.S(22)));
                Theme.Text(g, s.Done || s.Active ? s.Detail : "waiting", Theme.Mono(11), c, new RectangleF(tx, ty + Theme.S(26), logW - Theme.S(32), Theme.S(16)));
                ty += Theme.S(76);
            }
            if (ty + Theme.S(60) < Height) Theme.Stamp(g, "case open", Theme.Muted, tx + Theme.CapsWidth(g, "case open", 12, 2) + Theme.S(32), ty + Theme.S(8));
        }
    }
}
