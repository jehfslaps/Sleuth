using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpaceSleuth.Core;
using SpaceSleuth.Windows;

namespace SpaceSleuth.UI
{
    public sealed class MainForm : Form
    {
        static readonly KeyValuePair<string, TimeSpan>[] TimeWindows =
        {
            // Zero = "since your last scan", worked out from the saved history when you press the button.
            new KeyValuePair<string, TimeSpan>("Since last scan", TimeSpan.Zero),
            new KeyValuePair<string, TimeSpan>("6h", TimeSpan.FromHours(6)),
            new KeyValuePair<string, TimeSpan>("12h", TimeSpan.FromHours(12)),
            new KeyValuePair<string, TimeSpan>("24h", TimeSpan.FromHours(24)),
            new KeyValuePair<string, TimeSpan>("48h", TimeSpan.FromHours(48)),
            new KeyValuePair<string, TimeSpan>("3d", TimeSpan.FromDays(3)),
            new KeyValuePair<string, TimeSpan>("7d", TimeSpan.FromDays(7)),
            new KeyValuePair<string, TimeSpan>("14d", TimeSpan.FromDays(14)),
            new KeyValuePair<string, TimeSpan>("30d", TimeSpan.FromDays(30)),
        };

        const int Default48h = 4;
        const string FindText = "Find my space";
        const string StopText = "Stop looking";

        readonly HeroPanel hero = new HeroPanel();
        readonly Toolbar toolbar;
        readonly Segmented windowPicker;
        readonly PillButton drivePicker = new PillButton("All drives", PillKind.Secondary) { ShowCaret = true };
        readonly ToggleChip thorough = new ToggleChip("Thorough check");
        readonly PillButton findButton = new PillButton(FindText, PillKind.Primary);
        readonly SearchBox filter = new SearchBox("Filter results…");
        readonly HeadlinePanel headline = new HeadlinePanel();
        readonly DriveBanner banner = new DriveBanner();
        readonly ResultList results = new ResultList();
        readonly DetailPanel detail = new DetailPanel();
        readonly StatusBar status = new StatusBar();
        readonly ContextMenuStrip driveMenu = Theme.Menu();
        readonly ContextMenuStrip itemMenu = Theme.Menu();
        readonly ToolTip tips = new ToolTip();
        readonly System.Windows.Forms.Timer frame = new System.Windows.Forms.Timer { Interval = 33 };
        readonly List<IAnimated> animated = new List<IAnimated>();

        List<DriveChoice> drives = new List<DriveChoice>();
        DriveChoice chosenDrive; // null = all drives
        ScanResult result;
        string windowPhrase = "in the last 48 hours";
        CancellationTokenSource cts;
        DateTime lastStatus = DateTime.MinValue;

        public MainForm()
        {
            using (var g = CreateGraphics()) Theme.Scale = g.DpiX / 96f;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Ui(14, Weight.Regular);
            Text = "Space Sleuth";
            BackColor = Theme.Cream;
            ForeColor = Theme.Ink;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            StartPosition = FormStartPosition.CenterScreen;
            var work = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Theme.Si(1340), work.Width - 40), Math.Min(Theme.Si(940), work.Height - 40));
            MinimumSize = new Size(Theme.Si(1000), Theme.Si(640));

            // If you've scanned before, pick up exactly where you left off.
            DateTime? previous = null;
            try { previous = ScanEngine.LastScanUtc(null); } catch { }
            windowPicker = new Segmented(TimeWindows.Select(w => w.Key).ToArray(), previous.HasValue ? 0 : Default48h);
            toolbar = new Toolbar();
            toolbar.Controls.AddRange(new Control[] { windowPicker, drivePicker, thorough, findButton, filter });
            tips.SetToolTip(windowPicker, "How far back to look. \u201cSince last scan\u201d covers everything since you last ran Space Sleuth.");
            tips.SetToolTip(thorough, "Also checks the dates on every file, not just Windows' change log.\nSlower, but catches anything the change log has already forgotten.");
            tips.SetToolTip(drivePicker, "Which drive to look at");

            Controls.AddRange(new Control[] { hero, toolbar, banner, headline, results, detail, status });

            BuildDriveMenu();
            BuildItemMenu();
            Wire();

            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            status.Version = "v" + (version != null ? version.InformationalVersion : "1.0");
            status.Status = Native.IsElevated() ? "Ready when you are." : "Not running as administrator — you'll only see part of the picture.";
            headline.Plain = "Where did your space go?";

            animated.AddRange(new IAnimated[] { windowPicker, status, banner, results, detail, detail.Files });
            frame.Tick += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized) return;
                // Stay light: idle animations (Pebble bobbing) only run while the window is in front or a scan is going.
                if (Form.ActiveForm != this && cts == null) return;
                float now = Theme.Now;
                foreach (var a in animated) a.Tick(now);
            };

            ResumeLayout(false);

            Shown += (s, e) =>
            {
                frame.Start();
#if PREVIEW
                if (Environment.GetEnvironmentVariable("PREVIEW_STATE") == "scan")
                {
                    status.Busy = true;
                    SetHeadline("Looking around your drives\u2026", null, null);
                    banner.WasFree = 245L << 30; banner.WasUtc = DateTime.UtcNow.AddDays(-3); banner.NowFree = 237L << 30;
                    banner.State = DriveBanner.Mode.Scanning; headline.HideLine = true;
                    results.State = ListMood.Scanning;
                    results.MoodTitle = "Looking around\u2026";
                    results.MoodText = "C: reading change journal... 412,300 records";
                    detail.MascotMood = Mood.Searching;
                    detail.PlaceholderTitle = "Hang tight \u2014 cards show up in a moment.";
                    detail.SetItem(null, null);
                    status.Status = "C: reading change journal... 412,300 records";
                    findButton.Text = StopText;
                    PerformLayout();
                }
                else
                {
                    result = PreviewData.Build();
                    ShowResult();
                }
#else
                StartScan();
#endif
            };
            FormClosing += (s, e) =>
            {
                frame.Stop();
                if (cts != null) cts.Cancel();
            };
        }

        // ---------------- Layout ----------------

        /// <summary>The row of controls under the hero, with a little "Look back" caption.</summary>
        sealed class Toolbar : Painted
        {
            public float CaptionWidth;

            protected override void Draw(Graphics g)
            {
                var f = Theme.Ui(13.5f, Weight.Medium);
                CaptionWidth = Theme.Width(g, "Look back", f);
                Theme.Text(g, "Look back", f, Theme.InkA(150), new RectangleF(Theme.S(32), 0, CaptionWidth + Theme.S(4), Theme.S(42)));
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (toolbar == null) return;
            int W = ClientSize.Width, H = ClientSize.Height;
            int y = 0;
            bool bigBanner = banner.State == DriveBanner.Mode.Result || banner.State == DriveBanner.Mode.Scanning;
            hero.SetBounds(0, y, W, Theme.Si(bigBanner ? 92 : 128));
            y = hero.Bottom + Theme.Si(14);

            // Toolbar: wraps onto a second row if the window is narrow
            int x = Theme.Si(32) + Theme.Si(84), rowH = Theme.Si(44), gap = Theme.Si(10);
            int ty = 0, maxX = W - Theme.Si(32);
            Action<Control, int> place = (c, w) =>
            {
                if (x + w > maxX && x > Theme.Si(120)) { x = Theme.Si(32); ty += rowH + Theme.Si(8); }
                c.SetBounds(x, ty, w, rowH);
                x += w + gap;
            };
            place(windowPicker, windowPicker.PreferredWidth());
            place(drivePicker, Math.Max(drivePicker.PreferredWidth(), Theme.Si(130)));
            place(thorough, thorough.PreferredWidth());
            place(findButton, findButton.PreferredWidth());
            // Filter box: right-aligned on the same row if there's room for at least a small one, otherwise on its own row.
            int room = maxX - x;
            if (room >= Theme.Si(120))
            {
                int fw = Math.Min(room, Theme.Si(300));
                filter.SetBounds(maxX - fw, ty, fw, rowH);
            }
            else place(filter, Theme.Si(240));
            toolbar.SetBounds(0, y, W, ty + rowH + Theme.Si(2));
            y = toolbar.Bottom + Theme.Si(10);

            int bh = banner.Measure(W);
            banner.SetBounds(0, y, W, bh);
            banner.Visible = bh > 0;
            if (bh > 0) y = banner.Bottom + Theme.Si(4);

            int hh = headline.Measure(W);
            headline.SetBounds(0, y, W, hh);
            y = headline.Bottom + Theme.Si(4);

            int statusH = Theme.Si(34);
            status.SetBounds(0, H - statusH, W, statusH);

            int mainH = Math.Max(Theme.Si(200), status.Top - y);
            int leftW = (int)(W * 0.57);
            int off = Theme.Si(6);
            results.SetBounds(0, y + off, leftW, mainH - off);
            detail.SetBounds(leftW, y, W - leftW, mainH);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 11: cream title bar with oxblood text so the window feels like one piece of paper.
            try
            {
                int caption = 0x00F2FCFF, text = 0x00120E4A, border = 0x00DAEBF3;
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(Handle, 36, ref text, 4);
                DwmSetWindowAttribute(Handle, 34, ref border, 4);
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ---------------- Wiring ----------------

        void Wire()
        {
            findButton.Click += (s, e) => StartScan();
            drivePicker.Click += (s, e) => driveMenu.Show(drivePicker, new Point(0, drivePicker.Height + Theme.Si(4)));
            filter.Box.TextChanged += (s, e) => FillResults(false);

            results.SelectionChanged += (s, e) => ShowDetails();
            results.ItemActivated += (s, e) => OpenTag(results.SelectedTag);
            detail.Files.ItemActivated += (s, e) => OpenTag(detail.Files.SelectedFile);
            detail.OpenButton.Click += (s, e) => OpenTag(detail.Current);
            detail.CopyButton.Click += (s, e) => CopyTag(detail.Current);

            results.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { CopyTag(results.SelectedTag); e.Handled = true; } };
            detail.Files.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { CopyTag(detail.Files.SelectedFile); e.Handled = true; } };
        }

        void BuildDriveMenu()
        {
            try { drives = ScanEngine.AvailableDrives(); } catch { drives = new List<DriveChoice>(); }
            driveMenu.Items.Clear();
            var all = Theme.MenuItem("All drives", (s, e) => ChooseDrive(null));
            all.Checked = chosenDrive == null;
            driveMenu.Items.Add(all);
            if (drives.Count > 0) driveMenu.Items.Add(new ToolStripSeparator());
            foreach (var d in drives)
            {
                var drive = d;
                var item = Theme.MenuItem(d.ToString(), (s, e) => ChooseDrive(drive));
                item.Checked = chosenDrive != null && chosenDrive.Drive == d.Drive;
                driveMenu.Items.Add(item);
            }
        }

        void ChooseDrive(DriveChoice d)
        {
            chosenDrive = d;
            drivePicker.Text = d == null ? "All drives" : d.Drive + (string.IsNullOrEmpty(d.Label) ? "" : " " + d.Label);
            BuildDriveMenu();
            PerformLayout();
        }

        void BuildItemMenu()
        {
            var open = Theme.MenuItem("Open in File Explorer", (s, e) => OpenTag(MenuTag()));
            open.Font = Theme.Ui(14, Weight.Semibold);
            var copy = Theme.MenuItem("Copy path", (s, e) => CopyTag(MenuTag()));
            itemMenu.Items.Add(open);
            itemMenu.Items.Add(copy);
            itemMenu.Opening += (s, e) =>
            {
                object tag = MenuTag();
                string target = OpenTargetFor(tag);
                if (tag == null || (target == null && PathFor(tag) == null)) { e.Cancel = true; return; }
                open.Enabled = target != null;
                open.Text = target != null && !target.Contains("\\") ? "Open settings" : "Open in File Explorer";
                copy.Enabled = PathFor(tag) != null;
            };
            results.ContextMenuStrip = itemMenu;
            detail.Files.ContextMenuStrip = itemMenu;
        }

        object MenuTag()
        {
            if (itemMenu.SourceControl == detail.Files) return detail.Files.SelectedFile;
            return results.SelectedTag;
        }

        // ---------------- Scanning ----------------

        async void StartScan()
        {
            if (cts != null)
            {
                cts.Cancel();
                findButton.Enabled = false;
                findButton.Text = "Stopping…";
                SetStatus("Wrapping up…", true);
                return;
            }

            var opt = new ScanOptions { Thorough = thorough.Checked };
            if (chosenDrive != null) opt.Drives.Add(chosenDrive.Drive);
            else opt.Drives.AddRange(drives.Select(d => d.Drive));

            var window = TimeWindows[Math.Max(0, windowPicker.SelectedIndex)].Value;
            if (window == TimeSpan.Zero)
            {
                DateTime? last = null;
                try { last = ScanEngine.LastScanUtc(opt.Drives); } catch { }
                if (last.HasValue)
                {
                    // A few minutes of overlap so nothing slips between scans; always look back at least an hour.
                    window = DateTime.UtcNow - last.Value + TimeSpan.FromMinutes(5);
                    if (window < TimeSpan.FromHours(1)) window = TimeSpan.FromHours(1);
                    windowPhrase = "since your last scan (" + Format.When(last.Value) + ")";
                }
                else
                {
                    window = TimeSpan.FromHours(48);
                    windowPhrase = "in the last 48 hours (no earlier scan yet)";
                }
            }
            else windowPhrase = "in the " + Format.Window(window);
            opt.Window = window;
            if (opt.Drives.Count == 0)
            {
                headline.Plain = "Hmm — there are no drives to look at.";
                headline.Invalidate();
                return;
            }

            cts = new CancellationTokenSource();
            var token = cts.Token;
            findButton.Text = StopText;
            windowPicker.Enabled = drivePicker.Enabled = thorough.Enabled = false;
            status.Busy = true;

            SetHeadline("Looking around your drives…", null, null);
            ShowScanningBanner(opt.Drives);
            results.Clear();
            results.State = ListMood.Scanning;
            results.MoodTitle = "Looking around…";
            results.MoodText = "Checking " + string.Join(", ", opt.Drives) + " for what changed " + windowPhrase + ". This usually takes a few seconds.";
            detail.MascotMood = Mood.Searching;
            detail.PlaceholderTitle = "Hang tight \u2014 cards show up in a moment.";
            detail.SetItem(null, null);
            PerformLayout();

            var progress = new Progress<string>(s => SetStatus(s, false));
            try
            {
                var res = await Task.Run(() => ScanEngine.Run(opt, progress, token));
                result = res;
                ShowResult();
                SetStatus("All done in " + res.Elapsed.TotalSeconds.ToString("0.0") + " s — double-click any card to open it.", true);
            }
            catch (OperationCanceledException)
            {
                SetHeadline("Okay — stopped.", null, null);
                banner.State = DriveBanner.Mode.Hidden;
                headline.HideLine = false;
                results.State = ListMood.Stopped;
                results.MoodTitle = "Okay — stopped.";
                results.MoodText = "Run it again whenever you like.";
                detail.MascotMood = Mood.Calm;
                SetStatus("Stopped.", true);
            }
            catch (Exception ex)
            {
                SetHeadline("Hmm, something got in the way.", null, null);
                banner.State = DriveBanner.Mode.Hidden;
                headline.HideLine = false;
                results.State = ListMood.Error;
                results.MoodTitle = "Hmm, something got in the way.";
                results.MoodText = ex.Message;
                detail.MascotMood = Mood.Worried;
                SetStatus("Problem: " + ex.Message, true);
            }
            finally
            {
                detail.PlaceholderTitle = "Pick a card to see what's inside.";
                if (results.SelectedTag == null) detail.SetItem(null, null);
                cts.Dispose();
                cts = null;
                findButton.Text = FindText;
                findButton.Enabled = true;
                windowPicker.Enabled = drivePicker.Enabled = thorough.Enabled = true;
                status.Busy = false;
                results.Invalidate();
                detail.Invalidate();
                PerformLayout();
            }
        }

        void SetStatus(string text, bool force)
        {
            if (!force && (DateTime.UtcNow - lastStatus).TotalMilliseconds < 100) return;
            lastStatus = DateTime.UtcNow;
            status.Status = text;
            if (cts != null && results.State == ListMood.Scanning && !force)
            {
                results.MoodText = text;
                results.Invalidate();
            }
        }

        void SetHeadline(string plain, string amount, string target)
        {
            headline.Plain = plain;
            headline.Amount = amount;
            headline.Target = target;
            headline.Chips.Clear();
            headline.Notes.Clear();
            headline.Invalidate();
        }

        // ---------------- Showing results ----------------

        static string LastSegment(string path)
        {
            string p = path.TrimEnd('\\');
            int cut = p.LastIndexOf('\\');
            return cut >= 0 && cut < p.Length - 1 ? p.Substring(cut + 1) : path;
        }

        const string MainDrive = "C:";

        void ShowScanningBanner(IList<string> scanned)
        {
            if (!scanned.Any(d => string.Equals(d, MainDrive, StringComparison.OrdinalIgnoreCase)))
            {
                banner.State = DriveBanner.Mode.Hidden;
                headline.HideLine = false;
                return;
            }
            long was; DateTime when;
            bool known = false;
            try { known = ScanEngine.TryLastScan(MainDrive, out was, out when); }
            catch { was = 0; when = DateTime.MinValue; }
            banner.Drive = MainDrive;
            banner.WasFree = known ? was : (long?)null;
            banner.WasUtc = known ? when : (DateTime?)null;
            try { banner.NowFree = ScanEngine.FreeNow(MainDrive); } catch { }
            banner.State = DriveBanner.Mode.Scanning;
            banner.Invalidate();
            headline.HideLine = true;
        }

        static bool OnDrive(Culprit c, string drive)
        {
            if (c.Kind == CulpritKind.SystemArea) return c.Path.StartsWith(drive + " ", StringComparison.OrdinalIgnoreCase);
            if (c.Kind == CulpritKind.Folder) return c.Path.StartsWith(drive + "\\", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        static long BytesOf(Culprit c)
        {
            return c.AddedBytes > 0 ? c.AddedBytes : c.EstimateBytes;
        }

        static string ShortName(Culprit c)
        {
            if (c.Kind == CulpritKind.Folder) return LastSegment(c.Path);
            if (c.Kind == CulpritKind.SystemArea && c.Label.StartsWith("System Restore")) return "Restore points";
            return c.Label;
        }

        /// <summary>C: was X free at your last scan, now Y: fills the banner and splits the drop into where it went.</summary>
        void FillBanner(ScanResult r)
        {
            var rep = r.Drives.FirstOrDefault(d => string.Equals(d.Drive, MainDrive, StringComparison.OrdinalIgnoreCase));
            banner.Slices.Clear();
            banner.NotFound = 0;
            banner.Footnote = null;
            banner.Amount = banner.Target = banner.Sentence = null;
            if (rep == null)
            {
                banner.State = DriveBanner.Mode.Hidden;
                headline.HideLine = false;
                return;
            }

            banner.Drive = MainDrive;
            banner.NowFree = rep.FreeBytes;
            banner.WasFree = rep.PrevFreeBytes;
            banner.WasUtc = rep.PrevScanUtc;
            banner.State = rep.PrevFreeBytes.HasValue ? DriveBanner.Mode.Result : DriveBanner.Mode.FirstLook;
            headline.HideLine = true;
            if (!rep.PrevFreeBytes.HasValue) return;

            long drop = rep.PrevFreeBytes.Value - rep.FreeBytes;
            var onC = r.Culprits.Where(c => c.Kind != CulpritKind.Scattered && OnDrive(c, MainDrive) && BytesOf(c) > 0)
                                .OrderByDescending(BytesOf).ToList();
            long scatteredOnC = r.Culprits.Where(c => c.Kind == CulpritKind.Scattered)
                                .SelectMany(c => c.Files)
                                .Where(f => f.Path.StartsWith(MainDrive + "\\", StringComparison.OrdinalIgnoreCase))
                                .Sum(f => f.AddedBytes);
            long found = onC.Sum(BytesOf) + scatteredOnC;

            if (drop <= 0)
            {
                if (found >= 1024L * 1024 * 1024)
                    banner.Footnote = "Still, " + Format.Bytes(found) + " of new data showed up on " + MainDrive + " \u2014 it's in the list below. Something else must have freed up even more.";
                return;
            }

            // Most likely place for the C: drop
            var top = onC.FirstOrDefault();
            if (top != null)
            {
                banner.Amount = (top.AddedBytes > 0 ? "" : "~") + Format.Bytes(BytesOf(top));
                banner.Target = top.Kind == CulpritKind.SystemArea && top.EstimateBytes > 0 && top.AddedBytes == 0 ? "a new restore point" : ShortName(top);
            }
            else
            {
                banner.Sentence = "Couldn't trace this one to specific files yet.";
            }

            foreach (var c in onC.Take(3))
                banner.Slices.Add(new DriveBanner.Slice { Name = ShortName(c), Bytes = BytesOf(c), Hue = Theme.For(c), Estimate = c.AddedBytes == 0 });
            long rest = found - banner.Slices.Sum(x => x.Bytes);
            if (rest > 0) banner.Slices.Add(new DriveBanner.Slice { Name = "Everything else", Bytes = rest, Hue = Theme.HNeutral });
            banner.NotFound = Math.Max(0, drop - found);

            bool partialWindow = windowPicker.SelectedIndex != 0 && rep.PrevScanUtc.HasValue && rep.PrevScanUtc.Value < r.CutoffUtc;
            if (partialWindow)
                banner.Footnote = "You're only looking " + windowPhrase + ". Pick \u201cSince last scan\u201d to cover the whole drop.";
            else if (found > drop + drop / 10)
                banner.Footnote = "Found " + Format.Bytes(found) + " of new data \u2014 more than the drop, because some space was also freed up in the same time (deleted files, cleanup).";
            else if (banner.NotFound > Math.Max(512L * 1024 * 1024, drop / 10))
                banner.Footnote = "\u201cNot found yet\u201d is space that couldn't be traced \u2014 usually temporary files that came and went, or restore points Windows already cleaned up.";
        }

        void ShowResult()
        {
            var r = result;
            var top = r.Culprits.FirstOrDefault(c => c.Kind != CulpritKind.Scattered);

            if (top == null)
                SetHeadline(r.Culprits.Count == 0 ? "Nothing big showed up — your drive's been calm."
                                                  : "Just little bits here and there — nothing big.", null, null);
            else if (top.AddedBytes > 0)
                SetHeadline(null, Format.Bytes(top.AddedBytes), top.Kind == CulpritKind.Folder ? LastSegment(top.Path) : top.Label);
            else if (top.EstimateBytes > 0)
                SetHeadline(null, "~" + Format.Bytes(top.EstimateBytes), "a new restore point");
            else if (top.Kind == CulpritKind.SystemArea)
                SetHeadline("A new restore point was made, but your free space barely changed.", null, null);
            else
                SetHeadline("Most likely, an existing file grew in " + LastSegment(top.Path) + ".", null, null);

            FillBanner(r);
            bool bannerShown = banner.State != DriveBanner.Mode.Hidden;
            // With only C: scanned, the banner already says all of this.
            if (!bannerShown || r.Drives.Count > 1)
                headline.Chips.Add(new KeyValuePair<string, Hue>(Format.Bytes(r.TotalAdded) + " new " + windowPhrase, Theme.HSky));
            foreach (var d in r.Drives)
            {
                if (bannerShown && string.Equals(d.Drive, MainDrive, StringComparison.OrdinalIgnoreCase)) continue;
                headline.Chips.Add(new KeyValuePair<string, Hue>(d.Drive + " has " + Format.Bytes(d.FreeBytes) + " free", Theme.HMint));
                if (d.PrevFreeBytes.HasValue && d.PrevScanUtc.HasValue)
                {
                    long change = d.FreeBytes - d.PrevFreeBytes.Value;
                    headline.Chips.Add(new KeyValuePair<string, Hue>(
                        (change == 0 ? "No change" : Format.SignedBytes(change)) + " since your last scan · " + Format.When(d.PrevScanUtc.Value),
                        change < 0 ? Theme.HCoral : Theme.HMint));
                }
            }
            foreach (var d in r.Drives) if (!string.IsNullOrEmpty(d.Note)) headline.Notes.Add(d.Note);
            headline.Notes.AddRange(r.Notes.Where(n => !(bannerShown && n.StartsWith("Free space on " + MainDrive))));
            if (r.Drives.All(d => !d.PrevScanUtc.HasValue))
                headline.Notes.Add("First look done. Next time, you'll also see exactly how much restore points, the page file and big existing files grew.");

            detail.MascotMood = r.Culprits.Count > 0 ? Mood.Grin : Mood.Calm;
            FillResults(true);
            PerformLayout();
        }

        void FillResults(bool fromScan)
        {
            if (result == null) return;
            string f = filter.Box.Text.Trim();
            Func<string[], bool> matches = fields =>
                f.Length == 0 || fields.Any(x => x != null && x.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);

            var cs = result.Culprits.Where(c => matches(new[] { c.Path, c.Label, c.Contents, c.Note, c.HiddenReason })).ToList();
            var areas = result.Areas.OrderByDescending(a => a.CurrentBytes).Where(a => matches(new[] { a.Name, a.Explanation, a.Activity, a.Drive })).ToList();
            results.SetResults(cs, areas);
            if (cs.Count == 0 && areas.Count == 0)
            {
                results.State = ListMood.Empty;
                if (f.Length > 0)
                {
                    results.MoodTitle = "Nothing matches “" + f + "”.";
                    results.MoodText = "Try a shorter word, or clear the filter.";
                }
                else
                {
                    results.MoodTitle = "Your drive's been calm.";
                    results.MoodText = "Nothing big showed up " + windowPhrase + ". Try a longer window if you're still missing space.";
                }
                results.Invalidate();
            }
            ShowDetails();
        }

        void ShowDetails()
        {
            object tag = results.SelectedTag;
            SystemArea area = null;
            var c = tag as Culprit;
            if (c != null && c.Kind == CulpritKind.SystemArea && result != null)
                area = result.Areas.FirstOrDefault(x => x.Drive + " " + x.Name == c.Path);
            detail.SetItem(tag, area);
        }

        // ---------------- Actions ----------------

        static string PathFor(object tag)
        {
            var c = tag as Culprit;
            if (c != null) return c.Kind == CulpritKind.Folder ? c.Path : null;
            var f = tag as ChangedFile;
            if (f != null) return f.Path;
            return null;
        }

        static string OpenTargetFor(object tag)
        {
            var c = tag as Culprit;
            if (c != null) return c.Kind == CulpritKind.Folder ? c.Path : c.OpenTarget;
            var a = tag as SystemArea;
            if (a != null) return a.OpenTarget;
            var f = tag as ChangedFile;
            if (f != null) return f.Path;
            return null;
        }

        void OpenTag(object tag)
        {
            string target = OpenTargetFor(tag);
            if (target == null) return;
            try
            {
                if (tag is ChangedFile)
                    Process.Start("explorer.exe", "/select,\"" + target + "\"");
                else if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || target.Contains("\\"))
                    Process.Start("explorer.exe", "\"" + target + "\"");
                else
                    Process.Start(target);
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't open that one: " + ex.Message, true);
            }
        }

        void CopyTag(object tag)
        {
            string p = PathFor(tag);
            if (p == null) return;
            try
            {
                Clipboard.SetText(p);
                SetStatus("Nice — copied: " + p, true);
            }
            catch
            {
            }
        }
    }
}
