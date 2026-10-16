using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace KryticalNova.Release
{
    /// <summary>
    /// The launcher's one window, in the comic page by night of the server's control page (a first look; the owner
    /// will bring it closer to the game later). It checks for a new build when it opens, says so in plain words,
    /// updates on a press and starts the game.
    /// </summary>
    public sealed class MainForm : Form
    {
        private enum Stage { Checking, Ready, UpdateReady, NotInstalled, Working, Failed }

        private static readonly Color Page = Color.FromArgb(0x14, 0x1a, 0x2b);
        private static readonly Color Panel = Color.FromArgb(0x1d, 0x25, 0x40);
        private static readonly Color Edge = Color.FromArgb(0x2b, 0x35, 0x60);
        private static readonly Color Ink = Color.FromArgb(0x05, 0x07, 0x0d);
        private static readonly Color Paper = Color.FromArgb(0xf4, 0xee, 0xda);
        private static readonly Color Muted = Color.FromArgb(0xa9, 0xad, 0xc2);
        private static readonly Color Pop = Color.FromArgb(0xff, 0xc9, 0x28);
        private static readonly Color Rose = Color.FromArgb(0xff, 0x5c, 0x8f);
        private static readonly Color Cyan = Color.FromArgb(0x3f, 0xd2, 0xe2);

        private static PrivateFontCollection fonts;
        private readonly FontFamily display;
        private readonly Updater updater;
        private readonly LauncherConfig config;
        private readonly bool justUpdated;
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private readonly ComicButton main, repair, folder;
        private readonly TextureBrush dots;

        private Stage stage = Stage.Checking;
        private string headline = "CHECKING FOR UPDATES", body = "One moment.", footer = "";
        private UpdatePlan plan;
        private long progressDone, progressTotal;
        private bool applyAfterCheck;

        public MainForm(bool justUpdated)
        {
            this.justUpdated = justUpdated;
            string folderPath = Path.GetDirectoryName(Updater.LauncherExe);
            config = LauncherConfig.Load(folderPath);
            updater = new Updater(folderPath, config, LauncherConfig.BuiltIn("UpdateKey.pub"));
            display = LoadDisplayFont();

            base.Text = "Krytical Nova";
            ClientSize = new Size(760, 440);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Page;
            DoubleBuffered = true;
            Icon = Icon.ExtractAssociatedIcon(Updater.LauncherExe);
            dots = new TextureBrush(DotTile());

            main = new ComicButton(display, 26f) { Bounds = new Rectangle(506, 352, 226, 62), Fill = Pop, Label = "PLAY" };
            repair = new ComicButton(display, 17f) { Bounds = new Rectangle(28, 366, 120, 40), Fill = Edge, Ink = Paper, Label = "REPAIR" };
            folder = new ComicButton(display, 17f) { Bounds = new Rectangle(160, 366, 160, 40), Fill = Edge, Ink = Paper, Label = "GAME FOLDER" };
            main.Click += (s, e) => OnMain();
            repair.Click += (s, e) => { applyAfterCheck = true; Check(true); };
            folder.Click += (s, e) => { Directory.CreateDirectory(updater.GameFolder); Process.Start("explorer.exe", updater.GameFolder); };
            Controls.AddRange(new Control[] { main, repair, folder });
            Shown += (s, e) => Check(false);
            FormClosing += (s, e) => closing.Cancel();
            SetStage(Stage.Checking);
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
                await updater.Apply(plan, new Progress<long>(done => { progressDone = done; Invalidate(); }), closing.Token);
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
            Invalidate();
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

        // ------------------------------------------------------------------ drawing

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.FillRectangle(dots, ClientRectangle);

            // The title: yellow letters with an ink outline and a hard shadow, as on the control page.
            using (var path = new GraphicsPath())
            {
                path.AddString("KRYTICAL NOVA", display, 0, 66f, new Point(26, 18), StringFormat.GenericDefault);
                using (var shadow = (GraphicsPath)path.Clone())
                {
                    shadow.Transform(new Matrix(1, 0, 0, 1, 5, 5));
                    using (var ink = new SolidBrush(Ink)) g.FillPath(ink, shadow);
                }
                using (var outline = new Pen(Ink, 6f) { LineJoin = LineJoin.Round }) g.DrawPath(outline, path);
                using (var fill = new SolidBrush(Pop)) g.FillPath(fill, path);
            }
            DrawTag(g, "LAUNCHER", new Point(30, 96), Rose);
            if (config.channel != "live") DrawTag(g, config.channel.ToUpperInvariant() + " LINE", new Point(640, 32), Cyan);

            // The panel with what is going on.
            var panel = new Rectangle(28, 136, 704, 196);
            ComicBox(g, panel, Panel);
            if (stage == Stage.UpdateReady) DrawTag(g, "NEW", new Point(panel.Right - 78, panel.Top + 18), Rose);
            using (var title = new Font(display, 30f))
            using (var brush = new SolidBrush(stage == Stage.Failed ? Rose : Pop))
                g.DrawString(headline, title, brush, panel.Left + 20, panel.Top + 14);
            using (var text = new Font("Segoe UI Semibold", 11.5f))
            using (var brush = new SolidBrush(Paper))
                g.DrawString(body, text, brush, new RectangleF(panel.Left + 22, panel.Top + 68, panel.Width - 44, 64));

            if (stage == Stage.Working || stage == Stage.Checking)
            {
                var bar = new Rectangle(panel.Left + 22, panel.Bottom - 50, panel.Width - 44, 26);
                using (var back = new SolidBrush(Page)) g.FillRectangle(back, bar);
                if (stage == Stage.Working && progressTotal > 0)
                {
                    int width = (int)(bar.Width * Math.Min(1.0, progressDone / (double)progressTotal));
                    using (var fill = new SolidBrush(Cyan)) g.FillRectangle(fill, bar.Left, bar.Top, width, bar.Height);
                    using (var small = new Font("Segoe UI Semibold", 9.5f))
                    using (var brush = new SolidBrush(Paper))
                        g.DrawString($"{Megabytes(progressDone)} of {Megabytes(progressTotal)}", small, brush, bar.Left + 8, bar.Top + 4);
                }
                using (var pen = new Pen(Ink, 3f)) g.DrawRectangle(pen, bar);
            }

            using (var small = new Font("Segoe UI", 9.5f))
            using (var brush = new SolidBrush(Muted))
                g.DrawString(footer, small, brush, 30, 342);
        }

        private void DrawTag(Graphics g, string text, Point at, Color fill)
        {
            using (var font = new Font(display, 15f))
            {
                SizeF size = g.MeasureString(text, font);
                var box = new RectangleF(at.X, at.Y, size.Width + 10, size.Height + 2);
                var state = g.Save();
                g.TranslateTransform(box.X, box.Y);
                g.MultiplyTransform(new Matrix(1, 0, -0.14f, 1, 0, 0));
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, 0, 0, box.Width, box.Height);
                using (var ink = new SolidBrush(Ink)) g.DrawString(text, font, ink, 5, 2);
                g.Restore(state);
            }
        }

        internal static void ComicBox(Graphics g, Rectangle box, Color fill)
        {
            using (var shadow = new SolidBrush(Ink)) g.FillRectangle(shadow, box.X + 5, box.Y + 5, box.Width, box.Height);
            using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, box);
            using (var edge = new Pen(Edge, 2f)) g.DrawRectangle(edge, box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8);
            using (var pen = new Pen(Ink, 3f)) g.DrawRectangle(pen, box);
        }

        private static Bitmap DotTile()
        {
            var tile = new Bitmap(14, 14);
            using (Graphics g = Graphics.FromImage(tile))
            using (var dot = new SolidBrush(Color.FromArgb(18, 244, 238, 218)))
            {
                g.Clear(Page);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(dot, 5.5f, 5.5f, 3f, 3f);
            }
            return tile;
        }

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

    /// <summary>A button drawn like the control page's: a coloured block with an ink edge and a hard shadow.</summary>
    internal sealed class ComicButton : Control
    {
        private readonly Font font;
        private bool hot, pressed;
        private string label = "";

        public Color Fill = Color.Gold;
        public Color Ink = Color.FromArgb(0x10, 0x13, 0x1a);
        public string Label { get => label; set { label = value; Invalidate(); } }

        public ComicButton(FontFamily family, float size)
        {
            font = new Font(family, size);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int shift = pressed ? 3 : 0;
            var box = new Rectangle(shift, shift, Width - 6, Height - 6);
            if (!pressed) using (var shadow = new SolidBrush(Color.FromArgb(0x05, 0x07, 0x0d))) g.FillRectangle(shadow, 5, 5, Width - 6, Height - 6);
            Color fill = !Enabled ? Color.FromArgb(0x3a, 0x40, 0x58) : hot ? ControlPaint.Light(Fill, 0.15f) : Fill;
            using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, box);
            using (var pen = new Pen(Color.FromArgb(0x05, 0x07, 0x0d), 3f)) g.DrawRectangle(pen, box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2);
            using (var ink = new SolidBrush(Enabled ? Ink : Color.FromArgb(0x8a, 0x8e, 0xa3)))
            {
                var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, ink, box, format);
            }
        }
    }
}
