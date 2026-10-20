using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace KryticalNova.Release
{
    /// <summary>
    /// The launcher's one window, laid out as a comic page (owner's pick B, 18 Oct 2026): a painting panel that turns
    /// through the game's loading pictures, concept art and manga pages (a new one slides in every minute; arrows turn
    /// them by hand), a paper panel with the release's news and the news of earlier releases, and a strip that says what
    /// is going on with the PLAY / UPDATE button. It checks for a new build when it opens, updates on a press and starts
    /// the game.
    /// </summary>
    public sealed class MainForm : Form
    {
        private enum Stage { Checking, Ready, UpdateReady, NotInstalled, Working, Failed }
        private enum NewsView { Current, PastList, PastRelease }

        private static readonly Color Page = ComicFrame.Night;
        private static readonly Color Panel = ComicFrame.PanelFill;
        private static readonly Color Edge = Color.FromArgb(0x2b, 0x31, 0x5c);
        private static readonly Color Ink = ComicFrame.Ink;
        private static readonly Color Paper = ComicFrame.Paper;
        private static readonly Color Muted = Color.FromArgb(0xa9, 0xad, 0xc2);
        // The name logo's colours (owner, 18 Oct 2026): its cyan for what to press, its red for warnings; no yellow or pink.
        private static readonly Color Pop = ComicFrame.Cyan;
        private static readonly Color Rose = ComicFrame.Red;
        private static readonly Color RoseText = Color.FromArgb(0xff, 0x4a, 0x5c); // the red, lifted to read on the dark panel
        private static readonly Color Cyan = ComicFrame.Cyan;
        private static readonly Color DeepCyan = Color.FromArgb(0x0b, 0x8f, 0x9a); // the cyan, darkened to read on paper
        private static readonly Image Logo = LoadLogo();

        // The page's panels (owner, 19 Oct 2026): the panels as before, inside a 38 px border where the frame, the dots and the
        // ink slashes show, under the launcher's own title bar. The window is the page's angular shape (ComicFrame.Silhouette).
        private const int Pad = ComicFrame.Pad;
        private const int PageMargin = Pad + 38;
        private const int PageTop = Pad + 58;
        private static readonly Size PageSize = new Size(1160 + Pad * 2, PageTop + 604 + PageMargin);
        private static readonly Rectangle ArtBox = new Rectangle(PageMargin, PageTop, 700, 440);
        private const int ArtSlant = 42;
        private static readonly Rectangle NewsBox = new Rectangle(PageMargin + 718, PageTop, 366, 604);
        private const int NewsSlant = 11;
        private const int NewsFold = 26;
        private static readonly Rectangle StripBox = new Rectangle(PageMargin, PageTop + 458, 704, 146);

        // The panels' fixed shapes: the strip's cut corners, the painting's frame, the news sheet and its folded corner.
        private static readonly Point[] StripShape = ComicFrame.ClippedRect(StripBox, ComicFrame.Clip, 20, ComicFrame.Clip, 0);
        private static readonly PointF[] NewsPaper =
        {
            new PointF(NewsBox.Left + NewsSlant, NewsBox.Top), new PointF(NewsBox.Right - NewsFold, NewsBox.Top),
            new PointF(NewsBox.Right, NewsBox.Top + NewsFold), new PointF(NewsBox.Right, NewsBox.Bottom), new PointF(NewsBox.Left, NewsBox.Bottom),
        };
        // The navy frame the painting sits in.
        private static readonly Point[] ArtFrame =
        {
            new Point(ArtBox.Left - 10, ArtBox.Top - 10), new Point(ArtBox.Right + 11, ArtBox.Top - 10),
            new Point(ArtBox.Right - ArtSlant + 11, ArtBox.Bottom + 10), new Point(ArtBox.Left - 10, ArtBox.Bottom + 10),
        };
        // The folded-over corner of the news sheet.
        private static readonly PointF[] NewsFoldShape =
        {
            new PointF(NewsBox.Right - NewsFold, NewsBox.Top), new PointF(NewsBox.Right - NewsFold + 2, NewsBox.Top + NewsFold - 2),
            new PointF(NewsBox.Right, NewsBox.Top + NewsFold),
        };

        private static PrivateFontCollection fonts;
        private readonly FontFamily display;
        private readonly Updater updater;
        private readonly LauncherConfig config;
        private readonly bool justUpdated;
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private readonly ComicButton main, repair, folder;
        private readonly Bitmap page;
        private readonly ArtShow art;
        private readonly System.Windows.Forms.Timer frame = new System.Windows.Forms.Timer { Interval = 33 };

        private Stage stage = Stage.Checking;
        private string headline = "CHECKING FOR UPDATES", body = "One moment.", footer = "";
        private UpdatePlan plan;
        private long progressDone, progressTotal;
        private bool applyAfterCheck;

        // The news: the release's own, and the news of earlier releases (both from the signed update list).
        private readonly LauncherNews preview;
        private List<NewsItem> news = new List<NewsItem>();
        private List<NewsRelease> pastNews = new List<NewsRelease>();
        private string newsLabel = "";
        private NewsView newsView = NewsView.Current;
        private int openItem, pastIndex;

        // What can be clicked on the page, rebuilt with every paint.
        private readonly List<(Rectangle Area, Action Click)> clickable = new List<(Rectangle, Action)>();
        private DateTime pressedAt = DateTime.MinValue;

        public MainForm(bool justUpdated, LauncherNews preview = null)
        {
            this.justUpdated = justUpdated;
            this.preview = preview;
            string folderPath = Path.GetDirectoryName(Updater.LauncherExe);
            config = LauncherConfig.Load(folderPath);
            updater = new Updater(folderPath, config, LauncherConfig.BuiltIn("UpdateKey.pub"));
            display = LoadDisplayFont();
            art = new ArtShow(new Size(ArtBox.Width, ArtBox.Height));

            base.Text = "Krytical Nova";
            // No Windows border: the window is the comic page's own angular shape, with its own title bar (owner, 19 Oct 2026).
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = PageSize;
            MaximizeBox = false;
            using (GraphicsPath outline = ComicFrame.Silhouette(PageSize)) Region = new Region(outline);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Page;
            DoubleBuffered = true;
            Icon = Icon.ExtractAssociatedIcon(Updater.LauncherExe);
            page = ComicFrame.Page(ClientSize);

            main = new ComicButton(display, 30f) { Bounds = new Rectangle(StripBox.Right - 238, StripBox.Top + 28, 220, 92), Fill = Pop, Label = "PLAY", Cut = 16, Outline = 4f, Lift = true, Dots = 70 };
            repair = new ComicButton(display, 17f) { Bounds = new Rectangle(StripBox.Left + 20, StripBox.Bottom - 50, 120, 40), Fill = Edge, Ink = Paper, Label = "REPAIR", Cut = 8, Dots = 30 };
            folder = new ComicButton(display, 17f) { Bounds = new Rectangle(StripBox.Left + 152, StripBox.Bottom - 50, 160, 40), Fill = Edge, Ink = Paper, Label = "GAME FOLDER", Cut = 8, Dots = 30 };
            main.Click += (s, e) => { pressedAt = DateTime.UtcNow; OnMain(); };
            main.HotChanged += (s, e) => Invalidate(Grow(main.Bounds, 60));
            repair.Click += (s, e) => { applyAfterCheck = true; Check(true); };
            folder.Click += (s, e) => { Directory.CreateDirectory(updater.GameFolder); Process.Start("explorer.exe", updater.GameFolder); };
            Controls.AddRange(new Control[] { main, repair, folder });

            frame.Tick += (s, e) => Tick();
            frame.Start();
            Shown += (s, e) => Check(false);
            if (PictureFile != null) Shown += (s, e) => TakePicture();
            FormClosing += (s, e) => { closing.Cancel(); frame.Stop(); };
            if (preview != null) ShowNews(preview.items, preview.history, "PREVIEW");
            SetStage(Stage.Checking);
        }

        /// <summary>Set by <c>--picture &lt;file&gt;</c>: the window saves a picture of itself there a moment after it opens, and closes.</summary>
        public static string PictureFile;

        private async void TakePicture()
        {
            await System.Threading.Tasks.Task.Delay(4000);
            using (var picture = new Bitmap(Width, Height))
            {
                DrawToBitmap(picture, new Rectangle(Point.Empty, Size));
                using (Graphics g = Graphics.FromImage(picture))
                using (var outside = new Region(new Rectangle(Point.Empty, Size)))
                {
                    using (GraphicsPath shape = ComicFrame.Silhouette(PageSize)) outside.Exclude(shape);
                    using (var grey = new SolidBrush(Color.FromArgb(0x60, 0x63, 0x6b))) g.FillRegion(grey, outside);
                }
                picture.Save(PictureFile, ImageFormat.Png);
            }
            Close();
        }

        // ------------------------------------------------------------------ flow

        private async void Check(bool repairing)
        {
            if (string.IsNullOrWhiteSpace(LauncherConfig.BuiltIn("UpdateKey.pub")) || string.IsNullOrWhiteSpace(config.source))
            {
                Fail("THIS LAUNCHER IS NOT SET UP", "It has no update address or key. Get the launcher again from Krytical Nova.");
                return;
            }
            SetStage(Stage.Checking, repairing ? "CHECKING EVERY FILE" : "CHECKING FOR UPDATES", repairing ? "Each game file is compared with the latest build. This takes a minute." : "One moment.");
            try
            {
                plan = await updater.Check(repairing, closing.Token);
                if (preview == null) ShowNews(plan.Manifest.news, plan.Manifest.newsHistory, NewsLabel(plan.Manifest));
                if (plan.LauncherOutdated && !justUpdated)
                {
                    SetStage(Stage.Working, "UPDATING THE LAUNCHER", "A new launcher is ready. It replaces this one and opens by itself.");
                    progressDone = 0;
                    progressTotal = plan.Manifest.launcher.packedSize;
                    string exe = await updater.UpdateLauncher(plan.Manifest.launcher, new Progress<long>(Advance), closing.Token);
                    Log.Write("launcher replaced by the new one");
                    Process.Start(exe, "--updated");
                    Close();
                    return;
                }
                ShowPlan();
                if (applyAfterCheck && plan.Needed.Count > 0) Apply();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Fail("CAN'T REACH THE UPDATES", Plain(e) + (GameExeExists() ? " You can still play the build you have." : ""));
            }
            finally
            {
                applyAfterCheck = false;
            }
        }

        private void ShowPlan()
        {
            Log.Write($"{config.channel}: latest {plan.Manifest.version}, installed build {plan.InstalledBuild}, {plan.Needed.Count} file(s) to get");
            string size = Megabytes(plan.DownloadBytes);
            footer = $"Latest: {plan.Manifest.version}" + (plan.InstalledBuild > 0 ? $"   ·   Installed: build {plan.InstalledBuild}" : "");
            if (plan.Needed.Count == 0)
                SetStage(Stage.Ready, "READY TO PLAY", $"You have the latest build ({plan.Manifest.version}).");
            else if (!plan.GameInstalled)
                SetStage(Stage.NotInstalled, "WELCOME TO KRYTICAL NOVA", $"The game is {size} to download. Press INSTALL; it goes into the Game folder beside this launcher.");
            else
                SetStage(Stage.UpdateReady, "NEW BUILD READY", $"{plan.Manifest.version} is out. Only what changed is downloaded: {size}.");
        }

        private void OnMain()
        {
            switch (stage)
            {
                case Stage.Ready:
                    Play();
                    break;
                case Stage.UpdateReady:
                case Stage.NotInstalled:
                    Apply();
                    break;
                case Stage.Failed:
                    if (!failedRetry && GameExeExists()) Play();
                    else Check(false);
                    break;
            }
        }

        private async void Apply()
        {
            if (updater.GameRunning(plan.Manifest))
            {
                Fail("CLOSE THE GAME FIRST", "The game is running from this folder. Close it, then press RETRY.", retryOnly: true);
                return;
            }
            progressDone = 0;
            progressTotal = Math.Max(1, plan.DownloadBytes);
            SetStage(Stage.Working, plan.GameInstalled ? "UPDATING" : "INSTALLING", "Downloading. You can leave this open and do something else.");
            try
            {
                await updater.Apply(plan, new Progress<long>(done => { progressDone = done; Invalidate(StripBox); }), closing.Token);
                ShowPlan();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Fail("THE UPDATE STOPPED", Plain(e) + " What was already downloaded is kept: press RETRY.", retryOnly: true);
            }
        }

        private void Play()
        {
            try
            {
                updater.Launch(plan.Manifest);
                Close();
            }
            catch (Exception e)
            {
                Fail("THE GAME DID NOT START", Plain(e), retryOnly: true);
            }
        }

        private bool GameExeExists() => plan != null && File.Exists(Path.Combine(updater.GameFolder, plan.Manifest.gameExe));

        private void Advance(long bytes)
        {
            progressDone += bytes;
            Invalidate(StripBox);
        }

        // A failure offers PLAY when the build already installed can still be played, otherwise RETRY.
        private void Fail(string title, string detail, bool retryOnly = false)
        {
            Log.Write(title + ": " + detail);
            SetStage(Stage.Failed, title, detail);
            failedRetry = retryOnly;
            main.Label = !retryOnly && GameExeExists() ? "PLAY" : "RETRY";
        }

        private bool failedRetry;

        private void SetStage(Stage next, string title = null, string detail = null)
        {
            stage = next;
            failedRetry = false;
            if (title != null) headline = title;
            if (detail != null) body = detail;
            main.Label = next == Stage.UpdateReady ? "UPDATE" : next == Stage.NotInstalled ? "INSTALL" : next == Stage.Failed ? "RETRY" : "PLAY";
            main.Enabled = next != Stage.Checking && next != Stage.Working;
            repair.Enabled = folder.Enabled = main.Enabled;
            Invalidate();
        }

        private static string Plain(Exception e)
        {
            while (e.InnerException != null && (e is System.Net.Http.HttpRequestException || e is AggregateException)) e = e.InnerException;
            return e.Message.TrimEnd('.') + ".";
        }

        private static string Megabytes(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{Math.Max(1, (bytes + (1L << 19)) >> 20)} MB";

        // ------------------------------------------------------------------ news

        private void ShowNews(List<NewsItem> items, List<NewsRelease> history, string label)
        {
            news = items ?? new List<NewsItem>();
            pastNews = history ?? new List<NewsRelease>();
            newsLabel = label;
            if (newsView == NewsView.PastRelease && pastIndex >= pastNews.Count) newsView = NewsView.Current;
            openItem = Math.Min(openItem, Math.Max(0, news.Count - 1));
            Invalidate(Grow(NewsBox, 8));
        }

        // "v0.1.0 build 8 · 19 Oct 2026": the release the news came with, and the day it was made.
        private static string NewsLabel(UpdateManifest manifest)
        {
            string when = DateTime.TryParse(manifest.created, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime made) ? " · " + made.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : "";
            return manifest.version + when;
        }

        // ------------------------------------------------------------------ the page's own motion

        private void Tick()
        {
            if (art.Step(DateTime.UtcNow)) Invalidate(Grow(ArtBox, 6));
            if (main.Hot || (DateTime.UtcNow - pressedAt).TotalMilliseconds < 400) Invalidate(Grow(main.Bounds, 60));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            TrackTitle(e.Location);
            bool over = false;
            foreach (var (area, _) in clickable)
                if (area.Contains(e.Location)) { over = true; break; }
            Cursor = over ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (var (area, click) in clickable.ToArray())
                if (area.Contains(e.Location)) { click(); return; }
            // The title bar moves the window, as Windows' own would.
            if (e.Y < PageTop - 4)
            {
                ReleaseCapture();
                SendMessage(Handle, WmNcLeftButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
            }
        }

        // ------------------------------------------------------------------ the launcher's own title bar

        private static readonly Rectangle TitleBand = new Rectangle(Pad + 12, Pad + 12, PageSize.Width - Pad * 2 - 24, PageTop - Pad - 18);
        private string titleHot = "";

        private Rectangle TitleButton(int fromRight) => new Rectangle(PageSize.Width - Pad - 30 - 46 * (fromRight + 1), Pad + 14, 44, 30);

        // The icon and the name on the left; minimise, maximise (greyed: the window keeps its size) and close on the right.
        private void DrawTitleBar(Graphics g)
        {
            if (Icon != null) g.DrawIcon(Icon, new Rectangle(Pad + 46, Pad + 19, 20, 20));
            using (var font = new Font("Segoe UI Semibold", 10.5f))
            using (var brush = new SolidBrush(Paper))
                g.DrawString("Krytical Nova", font, brush, Pad + 74, Pad + 19);
            Point mouse = PointToClient(MousePosition);
            string hot = "";
            void Button(int fromRight, string name, bool enabled, Action click, Action<Rectangle, Pen> glyph)
            {
                Rectangle box = TitleButton(fromRight);
                bool over = enabled && box.Contains(mouse);
                if (over)
                {
                    hot = name;
                    using (var fill = new SolidBrush(name == "close" ? Rose : Edge)) g.FillRectangle(fill, box);
                }
                using (var pen = new Pen(enabled ? Paper : Color.FromArgb(0x5a, 0x5e, 0x80), 1.6f)) glyph(box, pen);
                if (enabled) clickable.Add((box, click));
            }
            Button(0, "close", true, Close, (b, p) =>
            {
                int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;
                g.DrawLine(p, cx - 6, cy - 6, cx + 6, cy + 6);
                g.DrawLine(p, cx + 6, cy - 6, cx - 6, cy + 6);
            });
            Button(1, "max", false, null, (b, p) => g.DrawRectangle(p, b.X + b.Width / 2 - 6, b.Y + b.Height / 2 - 6, 12, 12));
            Button(2, "min", true, () => WindowState = FormWindowState.Minimized,
                (b, p) => g.DrawLine(p, b.X + b.Width / 2 - 6, b.Y + b.Height / 2, b.X + b.Width / 2 + 6, b.Y + b.Height / 2));
            titleHot = hot;
        }

        // Keeps the title buttons' hover current.
        private void TrackTitle(Point at)
        {
            string over = "";
            if (TitleButton(0).Contains(at)) over = "close";
            else if (TitleButton(2).Contains(at)) over = "min";
            if (over != titleHot) Invalidate(TitleBand);
        }

        // A window without Windows' border still minimises and comes back from the taskbar, and keeps Alt+F4.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00080000; // WS_MINIMIZEBOX | WS_SYSMENU
                return cp;
            }
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private const int WmNcLeftButtonDown = 0xA1, HtCaption = 2;

        // ------------------------------------------------------------------ drawing

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawImageUnscaled(page, 0, 0);
            clickable.Clear();
            DrawTitleBar(g);

            DrawArt(g);
            DrawStrip(g);
            DrawNews(g);
            DrawPlayEffect(g);
        }

        // The painting panel: the slideshow in a slanted frame, the dots of a printed page over it, the logo, the arrows.
        private void DrawArt(Graphics g)
        {
            Point[] shape =
            {
                new Point(ArtBox.Left, ArtBox.Top), new Point(ArtBox.Right, ArtBox.Top),
                new Point(ArtBox.Right - ArtSlant, ArtBox.Bottom), new Point(ArtBox.Left, ArtBox.Bottom),
            };
            using (var shadow = new SolidBrush(Ink)) g.FillPolygon(shadow, Offset(ArtFrame, ComicFrame.ShadowOffset, ComicFrame.ShadowOffset));
            using (var plate = new SolidBrush(ComicFrame.Plate)) g.FillPolygon(plate, ArtFrame);
            using (var edge = new Pen(ComicFrame.InnerEdge, 2f) { LineJoin = LineJoin.Miter }) g.DrawPolygon(edge, Inset(ArtFrame, 4));
            using (var pen = new Pen(Ink, ComicFrame.InkWidth) { LineJoin = LineJoin.Miter }) g.DrawPolygon(pen, ArtFrame);
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(shape);
                GraphicsState state = g.Save();
                g.SetClip(path);
                art.Draw(g, ArtBox.Location);
                g.Restore(state);
            }
            using (var pen = new Pen(Ink, 3f) { LineJoin = LineJoin.Miter }) g.DrawPolygon(pen, shape);

            if (Logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Logo, new Rectangle(ArtBox.Left + 14, ArtBox.Top + 6, 230, 115));
            }
            DrawTag(g, "LAUNCHER", new Point(ArtBox.Left + 24, ArtBox.Top + 116), Rose, 15f);
            if (config.channel != "live") DrawTag(g, config.channel.ToUpperInvariant() + " LINE", new Point(ArtBox.Left + 136, ArtBox.Top + 116), Cyan, 15f);

            if (art.Count > 1)
            {
                int mid = ArtBox.Top + ArtBox.Height / 2;
                DrawArrow(g, new Rectangle(ArtBox.Left + 14, mid - 22, 44, 44), false, () => art.Turn(-1, DateTime.UtcNow));
                DrawArrow(g, new Rectangle(ArtBox.Right - ArtSlant / 2 - 62, mid - 22, 44, 44), true, () => art.Turn(1, DateTime.UtcNow));
                // Which painting this is: a small square for each, the one showing in cyan.
                for (int i = 0; i < art.Count; i++)
                {
                    var square = new Rectangle(ArtBox.Left + 22 + i * 16, ArtBox.Bottom - 26, 10, 10);
                    using (var fill = new SolidBrush(i == art.Showing ? Cyan : Color.FromArgb(200, Paper))) g.FillRectangle(fill, square);
                    using (var pen = new Pen(Ink, 2f)) g.DrawRectangle(pen, square);
                    int target = i;
                    clickable.Add((Grow(square, 3), () => art.Show(target, DateTime.UtcNow)));
                }
            }
        }

        private void DrawArrow(Graphics g, Rectangle box, bool right, Action click)
        {
            bool hot = box.Contains(PointToClient(MousePosition));
            using (var shadow = new SolidBrush(Ink)) g.FillEllipse(shadow, box.X + 3, box.Y + 3, box.Width, box.Height);
            using (var fill = new SolidBrush(hot ? Cyan : Color.FromArgb(230, Paper))) g.FillEllipse(fill, box);
            using (var pen = new Pen(Ink, 3f)) g.DrawEllipse(pen, box);
            int cx = box.X + box.Width / 2 + (right ? 2 : -2), cy = box.Y + box.Height / 2;
            Point[] head = right
                ? new[] { new Point(cx - 6, cy - 10), new Point(cx + 8, cy), new Point(cx - 6, cy + 10) }
                : new[] { new Point(cx + 6, cy - 10), new Point(cx - 8, cy), new Point(cx + 6, cy + 10) };
            using (var ink = new SolidBrush(Ink)) g.FillPolygon(ink, head);
            clickable.Add((box, click));
        }

        // The strip: what is going on, the progress, REPAIR, GAME FOLDER and the main button.
        private void DrawStrip(Graphics g)
        {
            Point[] shape = StripShape;
            using (var shadow = new SolidBrush(Ink)) g.FillPolygon(shadow, Offset(shape, ComicFrame.ShadowOffset, ComicFrame.ShadowOffset));
            using (var fill = new SolidBrush(Panel)) g.FillPolygon(fill, shape);
            using (var edge = new Pen(Edge, 2f)) g.DrawPolygon(edge, Inset(shape, 6));
            using (var pen = new Pen(Ink, ComicFrame.InkWidth) { LineJoin = LineJoin.Miter }) g.DrawPolygon(pen, shape);

            int left = StripBox.Left + 20, width = main.Left - left - 24;
            if (stage == Stage.UpdateReady) DrawTag(g, "NEW", new Point(main.Left - 70, StripBox.Top + 14), Rose, 15f);
            using (var title = new Font(display, 26f))
            using (var brush = new SolidBrush(stage == Stage.Failed ? RoseText : Pop))
                g.DrawString(headline, title, brush, left, StripBox.Top + 10);
            bool busy = stage == Stage.Working || stage == Stage.Checking;
            using (var text = new Font("Segoe UI Semibold", 10.5f))
            using (var brush = new SolidBrush(Paper))
                g.DrawString(body, text, brush, new RectangleF(left + 2, StripBox.Top + 52, width, busy ? 22 : 42));

            if (busy)
            {
                var bar = new Rectangle(left + 2, StripBox.Top + 76, width, 16);
                using (var back = new SolidBrush(Page)) g.FillRectangle(back, bar);
                if (stage == Stage.Working && progressTotal > 0)
                {
                    int done = (int)(bar.Width * Math.Min(1.0, progressDone / (double)progressTotal));
                    using (var fill = new SolidBrush(Cyan)) g.FillRectangle(fill, bar.Left, bar.Top, done, bar.Height);
                    using (var small = new Font("Segoe UI Semibold", 8.5f))
                    using (var brush = new SolidBrush(done > 140 ? Ink : Paper))
                        g.DrawString($"{Megabytes(progressDone)} of {Megabytes(progressTotal)}", small, brush, bar.Left + 6, bar.Top + 1);
                }
                using (var pen = new Pen(Ink, 3f)) g.DrawRectangle(pen, bar);
            }

            using (var small = new Font("Segoe UI", 9f))
            using (var brush = new SolidBrush(Muted))
                g.DrawString(footer, small, brush, folder.Right + 12, StripBox.Bottom - 38);
        }

        // Round the main button while the pointer is on it: a comic's speed lines, rushing in at it from every side and
        // breathing a little, in ink and the logo's red. A press flashes a white impact frame. Nothing at rest.
        private void DrawPlayEffect(Graphics g)
        {
            double sincePress = (DateTime.UtcNow - pressedAt).TotalMilliseconds;
            bool pressed = sincePress < 400;
            if (!main.Enabled || (!main.Hot && !pressed)) return;
            Rectangle b = main.Bounds;
            float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
            double t = Environment.TickCount / 1000.0;
            var random = new Random(7);
            const int lines = 34;
            for (int i = 0; i < lines; i++)
            {
                double angle = i * Math.PI * 2 / lines + random.NextDouble() * 0.08;
                float dx = (float)Math.Cos(angle), dy = (float)Math.Sin(angle);
                // From just outside the button's edge, outwards.
                float edge = EdgeDistance(b.Width / 2f, b.Height / 2f, dx, dy) + 6;
                float length = 14 + (float)(random.NextDouble() * 22) + (float)(Math.Sin(t * 6 + i) * 5);
                if (pressed) length += 18;
                float half = 2.2f + (float)random.NextDouble() * 1.6f;
                PointF near = new PointF(cx + dx * edge, cy + dy * edge);
                PointF far = new PointF(cx + dx * (edge + length), cy + dy * (edge + length));
                PointF side = new PointF(-dy * half, dx * half);
                PointF[] wedge = { new PointF(far.X + side.X, far.Y + side.Y), near, new PointF(far.X - side.X, far.Y - side.Y) };
                using (var fill = new SolidBrush(i % 3 == 0 ? Rose : Ink)) g.FillPolygon(fill, wedge);
            }
            if (pressed)
            {
                int grow = (int)(sincePress / 400 * 22);
                using (var flash = new Pen(Color.FromArgb((int)(255 * (1 - sincePress / 400)), Paper), 5f))
                    g.DrawRectangle(flash, Grow(b, 6 + grow));
            }
        }

        // How far from a box's centre its edge is, along a direction.
        private static float EdgeDistance(float halfWidth, float halfHeight, float dx, float dy)
        {
            float tx = Math.Abs(dx) < 1e-4f ? float.MaxValue : halfWidth / Math.Abs(dx);
            float ty = Math.Abs(dy) < 1e-4f ? float.MaxValue : halfHeight / Math.Abs(dy);
            return Math.Min(tx, ty);
        }

        // The paper panel: this release's news, the list of earlier releases, or one earlier release's news.
        private void DrawNews(Graphics g)
        {
            using (var shadow = new SolidBrush(Ink)) g.FillPolygon(shadow, ComicFrame.Offset(NewsPaper, ComicFrame.ShadowOffset, ComicFrame.ShadowOffset));
            using (var paper = new SolidBrush(Paper)) g.FillPolygon(paper, NewsPaper);
            using (var clip = new GraphicsPath())
            {
                clip.AddPolygon(NewsPaper);
                // Faint print dots along the top right and the lower right, gone before the text.
                ComicFrame.CornerDots(g, clip, new PointF(NewsBox.Right, NewsBox.Top), 150, 7f, 2.6f, Color.FromArgb(46, Page));
                ComicFrame.CornerDots(g, clip, new PointF(NewsBox.Right, NewsBox.Bottom - 40), 170, 7f, 2.8f, Color.FromArgb(40, Page));
            }
            using (var pen = new Pen(Ink, ComicFrame.InkWidth) { LineJoin = LineJoin.Miter }) g.DrawPolygon(pen, NewsPaper);
            using (var fold = new SolidBrush(Color.FromArgb(0xd9, 0xd0, 0xb8))) g.FillPolygon(fold, NewsFoldShape);
            using (var pen = new Pen(Ink, 3f) { LineJoin = LineJoin.Miter }) g.DrawPolygon(pen, NewsFoldShape);

            int left = NewsBox.Left + 24, right = NewsBox.Right - 18, y = NewsBox.Top + 16;
            using (var big = new Font(display, 30f))
            using (var ink = new SolidBrush(Ink))
            using (var red = new SolidBrush(Rose))
            using (var small = new Font("Segoe UI Semibold", 9f))
            using (var muted = new SolidBrush(Color.FromArgb(0x5a, 0x5e, 0x72)))
            {
                if (newsView == NewsView.Current)
                {
                    g.DrawString("WHAT'S", big, ink, left, y);
                    float w = g.MeasureString("WHAT'S ", big).Width - 8;
                    g.DrawString("NEW!", big, red, left + w, y);
                    if (newsLabel.Length > 0) g.DrawString(newsLabel, small, muted, left + 2, y + 46);
                    y += 74;
                    if (news.Count == 0)
                    {
                        using (var text = new Font("Segoe UI Semibold", 10.5f))
                            g.DrawString(plan == null ? "Checking for news..." : "The next build brings its news here.", text, ink, new RectangleF(left, y, right - left, 60));
                    }
                    else y = DrawItems(g, news, left, right, y);
                    if (pastNews.Count > 0) Link(g, "PAST NEWS  ▸", NewsBox.Bottom - 46, true, () => { newsView = NewsView.PastList; Invalidate(Grow(NewsBox, 8)); });
                }
                else if (newsView == NewsView.PastList)
                {
                    g.DrawString("PAST NEWS", big, ink, left, y);
                    g.DrawString("What came before, newest first", small, muted, left + 2, y + 46);
                    y += 76;
                    var oneLine = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    for (int i = 0; i < pastNews.Count && y < NewsBox.Bottom - 110; i++)
                    {
                        NewsRelease release = pastNews[i];
                        SplitLabel(release.label, out string when, out string name);
                        var row = new Rectangle(left - 6, y - 4, right - left + 6, 70);
                        bool hot = row.Contains(PointToClient(MousePosition));
                        // The date on a line of its own, in red; the release's name under it; its headlines under that.
                        using (var date = new Font(display, 14f)) g.DrawString(when.ToUpperInvariant(), date, red, left, y);
                        using (var font = new Font(display, 19f))
                        using (var brush = new SolidBrush(hot ? DeepCyan : Ink))
                            g.DrawString(name.ToUpperInvariant(), font, brush, new RectangleF(left, y + 18, right - left, 26), oneLine);
                        string titles = string.Join("  ·  ", release.items.ConvertAll(n => n.title));
                        g.DrawString(titles, small, muted, new RectangleF(left + 2, y + 44, right - left, 18), oneLine);
                        int index = i;
                        clickable.Add((row, () => { pastIndex = index; openItem = 0; newsView = NewsView.PastRelease; Invalidate(Grow(NewsBox, 8)); }));
                        y += 78;
                        using (var dash = new Pen(Color.FromArgb(64, Ink), 2f) { DashStyle = DashStyle.Dash }) g.DrawLine(dash, left, y - 8, right, y - 8);
                    }
                    Link(g, "◂  BACK TO WHAT'S NEW", NewsBox.Bottom - 46, false, () => { newsView = NewsView.Current; openItem = 0; Invalidate(Grow(NewsBox, 8)); });
                }
                else
                {
                    NewsRelease release = pastNews[pastIndex];
                    SplitLabel(release.label, out string when, out string name);
                    using (var font = new Font(display, 24f))
                        g.DrawString(name.ToUpperInvariant(), font, ink, new RectangleF(left, y + 4, right - left, 64));
                    float nameHeight;
                    using (var font = new Font(display, 24f)) nameHeight = g.MeasureString(name.ToUpperInvariant(), font, right - left).Height;
                    using (var date = new Font(display, 14f)) g.DrawString(when.ToUpperInvariant(), date, red, left, y + 8 + nameHeight);
                    y += (int)nameHeight + 40;
                    DrawItems(g, release.items, left, right, y);
                    Link(g, "◂  ALL PAST NEWS", NewsBox.Bottom - 46, false, () => { newsView = NewsView.PastList; Invalidate(Grow(NewsBox, 8)); });
                }
            }
        }

        // "16 Oct 2026 · Krytical Nova goes online": the date, and the name of what came out then.
        private static void SplitLabel(string label, out string when, out string name)
        {
            label = label ?? "";
            int dot = label.IndexOf('·');
            when = dot < 0 ? label.Trim() : label.Substring(0, dot).Trim();
            name = dot < 0 ? "" : label.Substring(dot + 1).Trim();
            if (name.Length == 0) { name = when; when = ""; }
        }

        // Numbered headlines; the open one shows what changed. A click opens another (or closes the open one).
        private int DrawItems(Graphics g, List<NewsItem> items, int left, int right, int y)
        {
            using (var title = new Font(display, 20f))
            using (var text = new Font("Segoe UI Semibold", 10f))
            using (var ink = new SolidBrush(Ink))
            using (var deep = new SolidBrush(DeepCyan))
            using (var number = new Font(display, 18f))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    bool open = i == openItem;
                    var circle = new Rectangle(left, y, 32, 32);
                    using (var red = new SolidBrush(Rose)) g.FillEllipse(red, circle);
                    using (var pen = new Pen(Ink, 3f)) g.DrawEllipse(pen, circle);
                    using (var paper = new SolidBrush(Paper))
                        g.DrawString((i + 1).ToString(), number, paper, circle, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

                    int textLeft = left + 42, textWidth = right - textLeft;
                    var row = new Rectangle(left - 4, y - 4, right - left + 8, 40);
                    bool hot = row.Contains(PointToClient(MousePosition));
                    g.DrawString(items[i].title.ToUpperInvariant(), title, open || hot ? deep : ink, textLeft, y + 2);
                    int index = i;
                    clickable.Add((row, () => { openItem = openItem == index ? -1 : index; Invalidate(Grow(NewsBox, 8)); }));
                    y += 38;
                    if (open)
                    {
                        SizeF size = g.MeasureString(items[i].body, text, textWidth);
                        g.DrawString(items[i].body, text, ink, new RectangleF(textLeft, y, textWidth, size.Height + 4));
                        y += (int)size.Height + 10;
                    }
                    if (i < items.Count - 1)
                        using (var dash = new Pen(Color.FromArgb(64, Ink), 2f) { DashStyle = DashStyle.Dash }) g.DrawLine(dash, left, y, right, y);
                    y += 12;
                }
            }
            return y;
        }

        private void Link(Graphics g, string text, int y, bool alignRight, Action click)
        {
            using (var font = new Font(display, 19f))
            {
                SizeF size = g.MeasureString(text, font);
                float x = alignRight ? NewsBox.Right - 20 - size.Width : NewsBox.Left + 22;
                var area = new Rectangle((int)x - 4, y - 2, (int)size.Width + 8, (int)size.Height + 4);
                bool hot = area.Contains(PointToClient(MousePosition));
                using (var brush = new SolidBrush(hot ? DeepCyan : Rose)) g.DrawString(text, font, brush, x, y);
                clickable.Add((area, click));
            }
        }

        private void DrawTag(Graphics g, string text, Point at, Color fill, float size)
        {
            using (var font = new Font(display, size))
            {
                SizeF measured = g.MeasureString(text, font);
                var box = new RectangleF(at.X, at.Y, measured.Width + 10, measured.Height + 2);
                var state = g.Save();
                g.TranslateTransform(box.X, box.Y);
                g.MultiplyTransform(new Matrix(1, 0, -0.14f, 1, 0, 0));
                using (var shadow = new SolidBrush(Ink)) g.FillRectangle(shadow, 4, 4, box.Width, box.Height);
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, 0, 0, box.Width, box.Height);
                using (var ink = new SolidBrush(fill == Rose ? Paper : Ink)) g.DrawString(text, font, ink, 5, 2);
                g.Restore(state);
            }
        }

        private static Point[] Offset(Point[] points, int dx, int dy) => Array.ConvertAll(points, p => new Point(p.X + dx, p.Y + dy));

        // A shape drawn a few pixels inside another (the panels' inner rule); good enough for these near-rectangles.
        private static Point[] Inset(Point[] points, int by)
        {
            int cx = 0, cy = 0;
            foreach (Point p in points) { cx += p.X; cy += p.Y; }
            cx /= points.Length;
            cy /= points.Length;
            return Array.ConvertAll(points, p => new Point(p.X + Math.Sign(cx - p.X) * by, p.Y + Math.Sign(cy - p.Y) * by));
        }

        private static Rectangle Grow(Rectangle r, int by) => new Rectangle(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);


        private static Image LoadLogo()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Logo.png"))
                    return stream != null ? new Bitmap(Image.FromStream(stream)) : null;
            }
            catch (Exception) { return null; }
        }

        // Windows draws the top bar: dark, in the launcher's own navy where Windows 11 allows it (owner, 18 Oct 2026).
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        private const int DarkMode = 20, CaptionColour = 35, CaptionTextColour = 36;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1, caption = ColorRef(Page), text = ColorRef(Paper);
                DwmSetWindowAttribute(Handle, DarkMode, ref on, sizeof(int));
                DwmSetWindowAttribute(Handle, CaptionColour, ref caption, sizeof(int));
                DwmSetWindowAttribute(Handle, CaptionTextColour, ref text, sizeof(int));
            }
            catch (Exception) { /* an older Windows keeps its own bar */ }
        }

        private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        // Bangers, the game's comic font, travels inside the launcher.
        private static FontFamily LoadDisplayFont()
        {
            if (fonts == null)
            {
                fonts = new PrivateFontCollection();
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Bangers.ttf"))
                {
                    if (stream != null)
                    {
                        var data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                        IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
                        Marshal.Copy(data, 0, memory, data.Length);
                        fonts.AddMemoryFont(memory, data.Length); // kept for the life of the program, as GDI+ needs
                    }
                }
            }
            return fonts.Families.Length > 0 ? fonts.Families[0] : new FontFamily("Impact");
        }
    }

    /// <summary>
    /// The painting panel's slideshow (owner, 18 Oct 2026): every painting built into the launcher (<c>Art\*.jpg</c>: the
    /// loading pictures, the concept art, the manga pages), in a shuffled order that starts somewhere new each time. A new
    /// one slides in from the right every <see cref="ShowSeconds"/>; the arrows and the squares turn them by hand. A
    /// landscape painting fills the panel; a tall page (the manga) is read slowly from top to bottom. The dots of a
    /// printed page lie over all of them.
    /// </summary>
    internal sealed class ArtShow
    {
        public const double ShowSeconds = 60;
        private const double SlideSeconds = 0.7;

        private readonly Size size;
        private readonly List<string> names = new List<string>();
        private readonly Dictionary<string, Bitmap> scaled = new Dictionary<string, Bitmap>();
        private readonly Bitmap screen;
        private int showing, from = -1, direction = 1;
        private DateTime shownAt = DateTime.UtcNow, slideStart = DateTime.MinValue;

        public ArtShow(Size size)
        {
            this.size = size;
            foreach (string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (name.StartsWith("Art.", StringComparison.Ordinal) && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) names.Add(name);
            names.Sort(StringComparer.Ordinal);
            var random = new Random();
            for (int i = names.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (names[i], names[j]) = (names[j], names[i]);
            }
            screen = Halftone(size);
        }

        public int Count => names.Count;
        public int Showing => showing;

        /// <summary>Moves the show on in time. True when the panel needs drawing again.</summary>
        public bool Step(DateTime now)
        {
            if (names.Count == 0) return false;
            bool sliding = from >= 0;
            if (sliding && (now - slideStart).TotalSeconds >= SlideSeconds) from = -1;
            if (!sliding && (now - shownAt).TotalSeconds >= ShowSeconds) Turn(1, now);
            return sliding || from >= 0 || IsTall(names[showing]);
        }

        public void Turn(int step, DateTime now) => Show((showing + step + names.Count) % names.Count, now, step);

        public void Show(int index, DateTime now, int step = 0)
        {
            if (names.Count < 2 || index == showing) return;
            from = showing;
            direction = step != 0 ? Math.Sign(step) : index > showing ? 1 : -1;
            showing = index;
            slideStart = now;
            shownAt = now;
        }

        public void Draw(Graphics g, Point at)
        {
            if (names.Count == 0) return;
            DateTime now = DateTime.UtcNow;
            g.InterpolationMode = InterpolationMode.Low;
            if (from >= 0)
            {
                double t = Math.Min(1, (now - slideStart).TotalSeconds / SlideSeconds);
                double eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                int shift = (int)(size.Width * eased) * direction;
                DrawOne(g, from, new Point(at.X - shift, at.Y), now);
                DrawOne(g, showing, new Point(at.X + size.Width * direction - shift, at.Y), now);
            }
            else DrawOne(g, showing, at, now);
            g.DrawImageUnscaled(screen, at);
        }

        private void DrawOne(Graphics g, int index, Point at, DateTime now)
        {
            Bitmap picture = Scaled(names[index]);
            if (picture == null) return;
            // A tall page is read from top to bottom over the time it is shown.
            int travel = picture.Height - size.Height;
            int top = travel <= 0 ? 0 : (int)(travel * Math.Min(1, (now - shownAt).TotalSeconds / ShowSeconds));
            if (index != showing) top = travel <= 0 ? 0 : travel; // the page leaving keeps where it was read to
            g.DrawImage(picture, new Rectangle(at, size), new Rectangle(0, top, size.Width, size.Height), GraphicsUnit.Pixel);
        }

        private bool IsTall(string name) => Scaled(name) is Bitmap b && b.Height > size.Height + 4;

        // Each painting is scaled once to the panel's width (a tall page) or to cover it (a wide one).
        private Bitmap Scaled(string name)
        {
            if (scaled.TryGetValue(name, out Bitmap done)) return done;
            Bitmap result = null;
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                using (var source = new Bitmap(stream))
                {
                    bool tall = source.Height > source.Width;
                    float scale = tall ? size.Width / (float)source.Width : Math.Max(size.Width / (float)source.Width, size.Height / (float)source.Height);
                    int w = (int)Math.Ceiling(source.Width * scale), h = (int)Math.Ceiling(source.Height * scale);
                    result = new Bitmap(size.Width, tall ? h : size.Height, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(result))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, new Rectangle((size.Width - w) / 2, tall ? 0 : (size.Height - h) / 2, w, h));
                    }
                }
            }
            catch (Exception) { /* a painting that cannot be read is skipped */ }
            scaled[name] = result;
            return result;
        }

        // The dots of a printed comic over the painting, and a shade at the bottom so the squares read.
        private static Bitmap Halftone(Size size)
        {
            var screen = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(screen))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var dot = new SolidBrush(Color.FromArgb(70, 5, 7, 13)))
                    for (int y = 0; y < size.Height; y += 6)
                        for (int x = (y / 6) % 2 * 3; x < size.Width; x += 6)
                            g.FillEllipse(dot, x, y, 2.6f, 2.6f);
                using (var shade = new LinearGradientBrush(new Rectangle(0, size.Height / 2, size.Width, size.Height / 2 + 1),
                           Color.FromArgb(0, 5, 7, 13), Color.FromArgb(150, 5, 7, 13), LinearGradientMode.Vertical))
                    g.FillRectangle(shade, 0, size.Height / 2 + 1, size.Width, size.Height / 2);
                using (var top = new LinearGradientBrush(new Rectangle(0, 0, size.Width / 2, 150),
                           Color.FromArgb(120, 5, 7, 13), Color.FromArgb(0, 5, 7, 13), LinearGradientMode.ForwardDiagonal))
                    g.FillRectangle(top, 0, 0, size.Width / 2, 150);
            }
            return screen;
        }
    }

    /// <summary>A button drawn like the control page's: a coloured block with an ink edge and a hard shadow.</summary>
    internal sealed class ComicButton : Control
    {
        private readonly Font font;
        private bool hot, pressed;
        private string label = "";

        public Color Fill = Color.Gold;
        public Color Ink = Color.FromArgb(0x10, 0x13, 0x1a);
        /// <summary>How much of the top-left and bottom-right corners is cut off, in pixels.</summary>
        public int Cut;
        public float Outline = 3f;
        /// <summary>On hover the button rises a little and its shadow grows (the main button).</summary>
        public bool Lift;
        /// <summary>How dark the halftone in the face is at its left, fading to nothing at the right (0: none).</summary>
        public int Dots;
        public string Label { get => label; set { label = value; Invalidate(); } }

        /// <summary>True while the pointer is on the button.</summary>
        public bool Hot => hot;

        /// <summary>Raised when the pointer comes onto the button or leaves it.</summary>
        public event EventHandler HotChanged;

        public ComicButton(FontFamily family, float size)
        {
            font = new Font(family, size);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); HotChanged?.Invoke(this, EventArgs.Empty); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = pressed = false; Invalidate(); HotChanged?.Invoke(this, EventArgs.Empty); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        // A printed comic's dots in the face: largest at the left edge, gone by the middle.
        private void Halftone(Graphics g, Point[] face, Rectangle box)
        {
            GraphicsState state = g.Save();
            using (var clip = new GraphicsPath())
            {
                clip.AddPolygon(face);
                g.SetClip(clip, CombineMode.Intersect);
                using (var dot = new SolidBrush(Color.FromArgb(Dots, 0x05, 0x06, 0x0d)))
                {
                    const float spacing = 6f;
                    int row = 0;
                    for (float y = box.Top; y <= box.Bottom; y += spacing * 0.866f, row++)
                        for (float x = box.Left + (row % 2) * spacing / 2; x <= box.Left + box.Width * 0.6f; x += spacing)
                        {
                            float r = 2.1f * (1 - (x - box.Left) / (box.Width * 0.6f));
                            if (r > 0.4f) g.FillEllipse(dot, x - r, y - r, r * 2, r * 2);
                        }
                }
            }
            g.Restore(state);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // At rest the face sits 2 px in with its shadow 6 px below it; lifted on hover it moves up to the corner and the
            // shadow falls 8 px; pressed it drops onto its shadow.
            int room = Lift ? 10 : 6, shadowAt = Lift ? 8 : 5;
            int shift = pressed ? (Lift ? 7 : 3) : Lift && hot && Enabled ? 0 : Lift ? 2 : 0;
            var box = new Rectangle(shift, shift, Width - room, Height - room);
            Point[] face = ComicFrame.ClippedRect(new Rectangle(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2), Cut, 0, Cut, 0);
            Point[] under = ComicFrame.ClippedRect(new Rectangle(shadowAt + 1, shadowAt + 1, box.Width - 2, box.Height - 2), Cut, 0, Cut, 0);
            if (!pressed) using (var shadow = new SolidBrush(Color.FromArgb(0x05, 0x06, 0x0d))) g.FillPolygon(shadow, under);
            Color fill = !Enabled ? Color.FromArgb(0x3a, 0x40, 0x58) : hot ? ControlPaint.Light(Fill, 0.15f) : Fill;
            using (var brush = new SolidBrush(fill)) g.FillPolygon(brush, face);
            if (Dots > 0 && Enabled) Halftone(g, face, box);
            using (var pen = new Pen(Color.FromArgb(0x05, 0x06, 0x0d), Outline) { LineJoin = LineJoin.Miter }) g.DrawPolygon(pen, face);
            using (var ink = new SolidBrush(Enabled ? Ink : Color.FromArgb(0x8a, 0x8e, 0xa3)))
            {
                var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, ink, box, format);
            }
        }
    }
}
