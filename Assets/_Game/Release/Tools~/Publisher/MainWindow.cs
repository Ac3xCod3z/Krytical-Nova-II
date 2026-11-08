using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace KryticalNova.Publisher
{
    /// <summary>
    /// Krytical Nova Publisher (owner, 8 Nov 2026): game updates without opening Unity. Tabs: CHANNELS (what TEST, LIVE and
    /// DOTS hold; promote test to live; roll live back to an archived build), UPLOAD (pick a staged release folder, send it
    /// to TEST or DOTS), STAND-IN (the local bucket that stands in for Cloudflare R2: start, stop, the launcher settings
    /// that read it) and LOG. Every bucket action is one run of kn_publish.py, so the app holds no bucket logic of its own.
    /// </summary>
    public sealed class MainWindow : Form
    {
        private static readonly Color Page = Color.FromArgb(0x14, 0x1a, 0x2b);
        private static readonly Color PanelColor = Color.FromArgb(0x1d, 0x25, 0x40);
        private static readonly Color Edge = Color.FromArgb(0x2b, 0x35, 0x60);
        private static readonly Color Ink = Color.FromArgb(0x05, 0x07, 0x0d);
        private static readonly Color Paper = Color.FromArgb(0xf4, 0xee, 0xda);
        private static readonly Color Muted = Color.FromArgb(0xa9, 0xad, 0xc2);
        private static readonly Color Cyan = Color.FromArgb(0x3e, 0xd4, 0xdf);
        private static readonly Color Red = Color.FromArgb(0xde, 0x0d, 0x22);
        private static readonly Color RedText = Color.FromArgb(0xff, 0x4a, 0x5c);
        private static readonly Color Green = Color.FromArgb(0x7b, 0xe0, 0x7b);
        private static readonly Color Gold = Color.FromArgb(0xf2, 0xb8, 0x3a);
        private static readonly string[] Lines = { "test", "live", "dots" };

        private readonly string root;
        private readonly Settings settings;
        private readonly SelfTest selfTest;
        private readonly FontFamily display;
        private readonly Image logo;
        private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        private readonly Comic[] tabs = new Comic[4];
        private readonly Control[] pages = new Control[4];
        private readonly List<Comic> actionButtons = new List<Comic>();
        private readonly ChannelCard[] cards = new ChannelCard[3];
        private readonly ComboBox archive = new ComboBox();
        private readonly RichTextBox log = new RichTextBox();
        private readonly TextBox storeBox = new TextBox();
        private readonly Label storeInfo = new Label();
        private readonly Label standInInfo = new Label();
        private readonly TextBox launcherJson = new TextBox();
        private Comic uploadTest, uploadDots;
        private string uploadLine = "test";

        private Process standIn;
        private bool standInUp, busy;
        private string busyText = "";
        private Dictionary<string, object> lastStatus;

        public MainWindow(string root, SelfTest selfTest)
        {
            this.root = root;
            this.selfTest = selfTest;
            settings = Settings.Load(root);
            if (selfTest != null) settings.target = "local"; // the self-test never goes near a real bucket
            display = Fonts.Display();
            logo = LoadImage("Logo.png");

            Text = "Krytical Nova Publisher";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            ClientSize = new Size(1080, 720);
            MinimumSize = new Size(940, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Page;
            DoubleBuffered = true;

            string[] names = { "CHANNELS", "UPLOAD", "STAND-IN", "LOG" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                tabs[i] = new Comic(display, 18f) { Label = names[i] };
                tabs[i].Click += (s, e) => ShowTab(index);
                Controls.Add(tabs[i]);
            }
            BuildChannels();
            BuildUpload();
            BuildStandIn();
            BuildLog();
            ShowTab(0);
            Resize += (s, e) => LayoutPages();
            LayoutPages();

            Shown += async (s, e) =>
            {
                Say(settings.IsLocal ? "Target: the LOCAL stand-in bucket on this PC (" + settings.LocalEndpoint + "). Nothing leaves this PC."
                                     : settings.R2Ready ? "Target: Cloudflare R2 (" + settings.r2Endpoint + ", bucket " + settings.r2Bucket + ")."
                                                        : "Target r2 is chosen but not set up (publisher-settings.json): nothing will be sent.");
                if (settings.IsLocal) await StartStandIn();
                await RefreshStatus();
                if (selfTest != null) await RunSelfTest();
            };
            FormClosing += (s, e) => StopStandIn();
        }

        // ------------------------------------------------------------------ layout

        private void ShowTab(int index)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Fill = i == index ? Cyan : Edge;
                tabs[i].InkColor = i == index ? Ink : Paper;
                tabs[i].Invalidate();
                pages[i].Visible = i == index;
            }
        }

        private void LayoutPages()
        {
            int top = 112, left = 24, width = ClientSize.Width - 48;
            int tabWidth = Math.Min(186, (width - (tabs.Length - 1) * 10) / tabs.Length);
            for (int i = 0; i < tabs.Length; i++) tabs[i].Bounds = new Rectangle(left + i * (tabWidth + 10), top, tabWidth, 44);
            var area = new Rectangle(left, top + 58, width, ClientSize.Height - top - 58 - 20);
            foreach (Control page in pages) page.Bounds = area;
            Invalidate();
        }

        private Panel NewPage(int index)
        {
            var page = new Panel { BackColor = PanelColor, Padding = new Padding(16) };
            pages[index] = page;
            Controls.Add(page);
            return page;
        }

        private void BuildChannels()
        {
            Panel page = NewPage(0);
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 250, ColumnCount = 3, BackColor = Color.Transparent };
            for (int i = 0; i < 3; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
                cards[i] = new ChannelCard(display) { Line = Lines[i], Dock = DockStyle.Fill, Margin = new Padding(0, 0, i < 2 ? 14 : 0, 0) };
                grid.Controls.Add(cards[i], i, 0);
            }
            archive.DropDownStyle = ComboBoxStyle.DropDownList;
            archive.Font = new Font("Segoe UI", 12f);
            archive.Width = 200;
            archive.BackColor = Page;
            archive.ForeColor = Paper;
            archive.FlatStyle = FlatStyle.Flat;
            var archiveLabel = new Label { Text = "ARCHIVED BUILD:", AutoSize = true, ForeColor = Muted, Font = new Font(display, 15f), UseCompatibleTextRendering = true, Margin = new Padding(0, 10, 8, 0) };
            var row1 = Row(Action("REFRESH", Edge, () => RefreshStatus()), Action("PROMOTE TEST > LIVE", Cyan, () => Promote(), 250));
            var row2 = Row(archiveLabel, archive, Action("ROLL BACK LIVE", Red, () => Rollback(), 200), Action("COPY TO TEST", Edge, () => RollbackTo("test"), 170));
            var hint = new Label
            {
                Dock = DockStyle.Top, Height = 64, ForeColor = Muted, Font = new Font("Segoe UI", 10f),
                Text = "Promote copies TEST's signed list to LIVE (the files are already there, so it is instant). Roll back puts an archived build back on LIVE. " +
                       "Both ask first. Launchers on the line pick it up the next time they open.",
            };
            page.Controls.Add(hint);
            page.Controls.Add(row2);
            page.Controls.Add(row1);
            page.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 14, BackColor = Color.Transparent });
            page.Controls.Add(grid);
        }

        private void BuildUpload()
        {
            Panel page = NewPage(1);
            var title = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Cyan, Font = new Font(display, 17f), UseCompatibleTextRendering = true, Text = "1. PICK A STAGED RELEASE (THE STORE FOLDER WITH MANIFEST.SIGNED)" };
            storeBox.Font = new Font("Consolas", 11f);
            storeBox.BackColor = Page;
            storeBox.ForeColor = Paper;
            storeBox.BorderStyle = BorderStyle.FixedSingle;
            storeBox.Width = 700;
            storeBox.Text = settings.storeFolder;
            storeBox.TextChanged += (s, e) => ReadStore();
            var pick = Row(storeBox, Action("BROWSE", Edge, Browse, 140));
            storeInfo.Dock = DockStyle.Top;
            storeInfo.Height = 70;
            storeInfo.ForeColor = Paper;
            storeInfo.Font = new Font("Segoe UI Semibold", 11f);
            var title2 = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Cyan, Font = new Font(display, 17f), UseCompatibleTextRendering = true, Text = "2. CHOOSE THE LINE, THEN UPLOAD" };
            uploadTest = new Comic(display, 15f) { Label = "TEST", Width = 140, Height = 42 };
            uploadDots = new Comic(display, 15f) { Label = "DOTS", Width = 140, Height = 42 };
            uploadTest.Click += (s, e) => PickLine("test");
            uploadDots.Click += (s, e) => PickLine("dots");
            var lines = Row(uploadTest, uploadDots, Action("UPLOAD", Cyan, () => Upload(), 180));
            var hint = new Label
            {
                Dock = DockStyle.Top, Height = 90, ForeColor = Muted, Font = new Font("Segoe UI", 10f),
                Text = "Only files the bucket does not have yet are sent (each file is stored once, by its fingerprint). The signed list goes up last, so a launcher " +
                       "never sees a list whose files are missing. LIVE is never uploaded to: a build reaches LIVE only through Promote on the CHANNELS tab.\n" +
                       "Make the folder with Krytical Nova > Release > Publish to Test (it stages into Builds\\Release\\Store).",
            };
            page.Controls.Add(hint);
            page.Controls.Add(lines);
            page.Controls.Add(title2);
            page.Controls.Add(storeInfo);
            page.Controls.Add(pick);
            page.Controls.Add(title);
            PickLine("test");
            ReadStore();
        }

        private void BuildStandIn()
        {
            Panel page = NewPage(2);
            standInInfo.Dock = DockStyle.Top;
            standInInfo.Height = 120;
            standInInfo.ForeColor = Paper;
            standInInfo.Font = new Font("Segoe UI Semibold", 11f);
            var buttons = Row(Action("START", Cyan, () => StartStandIn(), 140), Action("STOP", Red, StopStandIn, 140),
                              Action("OPEN FOLDER", Edge, () => Open(settings.localFolder), 180), Action("COPY LAUNCHER.JSON", Edge, CopyLauncherJson, 230));
            var title = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Cyan, Font = new Font(display, 17f), UseCompatibleTextRendering = true, Text = "A TEST LAUNCHER THAT READS THE STAND-IN: PUT THIS LAUNCHER.JSON BESIDE IT" };
            launcherJson.Multiline = true;
            launcherJson.ReadOnly = true;
            launcherJson.Dock = DockStyle.Fill;
            launcherJson.Font = new Font("Consolas", 11f);
            launcherJson.BackColor = Color.FromArgb(0x0d, 0x12, 0x22);
            launcherJson.ForeColor = Paper;
            launcherJson.BorderStyle = BorderStyle.None;
            launcherJson.Text = LauncherJson("test");
            page.Controls.Add(launcherJson);
            page.Controls.Add(title);
            page.Controls.Add(buttons);
            page.Controls.Add(standInInfo);
            UpdateStandInInfo();
        }

        private void BuildLog()
        {
            Panel page = NewPage(3);
            log.ReadOnly = true;
            log.BackColor = Color.FromArgb(0x0d, 0x12, 0x22);
            log.ForeColor = Paper;
            log.BorderStyle = BorderStyle.None;
            log.Font = new Font("Consolas", 9.5f);
            log.WordWrap = false;
            log.DetectUrls = false;
            log.Dock = DockStyle.Fill;
            page.Controls.Add(log);
            page.Controls.Add(Row(Action("OPEN LOG FILE", Edge, () => Open(Path.Combine(root, "publisher.log")), 200), Action("CLEAR", Edge, () => log.Clear(), 120)));
        }

        private Comic Action(string label, Color fill, Action click, int width = 170)
        {
            var b = new Comic(display, 15f) { Label = label, Fill = fill, InkColor = fill == Cyan ? Ink : Paper, Width = width, Height = 42 };
            b.Click += (s, e) => { if (!busy) click(); };
            actionButtons.Add(b);
            return b;
        }

        private static Panel Row(params Control[] items)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 6), WrapContents = false };
            foreach (Control item in items) { if (item.Margin == new Padding(3)) item.Margin = new Padding(0, 0, 12, 0); row.Controls.Add(item); }
            return row;
        }

        private void PickLine(string line)
        {
            uploadLine = line;
            uploadTest.Fill = line == "test" ? Gold : Edge;
            uploadTest.InkColor = line == "test" ? Ink : Paper;
            uploadDots.Fill = line == "dots" ? Gold : Edge;
            uploadDots.InkColor = line == "dots" ? Ink : Paper;
            uploadTest.Invalidate();
            uploadDots.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var dot = new SolidBrush(Color.FromArgb(18, Paper)))
                for (int y = 4; y < ClientSize.Height; y += 14)
                    for (int x = 4; x < ClientSize.Width; x += 14)
                        g.FillEllipse(dot, x, y, 3f, 3f);
            if (logo != null) g.DrawImage(logo, new Rectangle(20, 6, 190, 95));
            Banner(g, "PUBLISHER", new Point(222, 60), Red);
            int x0 = ClientSize.Width - 24;
            if (busy) x0 = Pill(g, busyText, x0, Gold);
            x0 = Pill(g, settings.IsLocal ? (standInUp ? "STAND-IN ONLINE" : "STAND-IN OFF") : "R2", x0, settings.IsLocal ? (standInUp ? Green : RedText) : (settings.R2Ready ? Green : RedText));
            Pill(g, settings.IsLocal ? "TARGET: LOCAL" : "TARGET: CLOUDFLARE R2", x0, settings.IsLocal ? Cyan : Gold);
        }

        private int Pill(Graphics g, string text, int right, Color color)
        {
            using (var font = new Font(display, 14f))
            {
                SizeF size = g.MeasureString(text, font);
                var box = new RectangleF(right - size.Width - 22, 28, size.Width + 18, size.Height + 6);
                using (var pen = new Pen(color, 2.5f)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                using (var brush = new SolidBrush(color)) g.DrawString(text, font, brush, box.X + 9, box.Y + 3);
                return (int)box.X - 10;
            }
        }

        private void Banner(Graphics g, string text, Point at, Color fill)
        {
            using (var font = new Font(display, 16f))
            {
                SizeF size = g.MeasureString(text, font);
                GraphicsState state = g.Save();
                g.TranslateTransform(at.X, at.Y);
                g.MultiplyTransform(new Matrix(1, 0, -0.14f, 1, 0, 0));
                using (var shadow = new SolidBrush(Ink)) g.FillRectangle(shadow, 4, 4, size.Width + 10, size.Height + 2);
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, 0, 0, size.Width + 10, size.Height + 2);
                using (var pen = new Pen(Ink, 2.5f)) g.DrawRectangle(pen, 0, 0, size.Width + 10, size.Height + 2);
                using (var ink = new SolidBrush(Paper)) g.DrawString(text, font, ink, 5, 2);
                g.Restore(state);
            }
        }

        // ------------------------------------------------------------------ the log

        private void Say(string line)
        {
            string stamped = DateTime.Now.ToString("HH:mm:ss  ") + line;
            if (InvokeRequired) { BeginInvoke((Action)(() => Say(line))); return; }
            log.SelectionStart = log.TextLength;
            log.SelectionColor = line.StartsWith("FAILED") || line.StartsWith("PROBLEM") ? RedText : line.StartsWith(">") ? Cyan : Paper;
            log.AppendText(stamped + Environment.NewLine);
            log.ScrollToCaret();
            try { File.AppendAllText(Path.Combine(root, "publisher.log"), DateTime.Now.ToString("yyyy-MM-dd ") + stamped + Environment.NewLine); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ the stand-in bucket

        private string Tool(string name) => Path.Combine(root, "KnPublish", name);
        private string StandInRoot => Path.Combine(settings.localFolder, "bucket");
        private string StandInKeys => Path.Combine(settings.localFolder, "standin-keys.json");

        private async Task<bool> ProbeStandIn()
        {
            try
            {
                using (HttpResponseMessage reply = await http.GetAsync(settings.LocalEndpoint + "/pub/probe"))
                    return (int)reply.StatusCode == 404;
            }
            catch (Exception) { return false; }
        }

        private async Task StartStandIn()
        {
            if (!settings.IsLocal) return;
            if (await ProbeStandIn()) { standInUp = true; Say("The stand-in bucket already answers on " + settings.LocalEndpoint + "."); UpdateStandInInfo(); return; }
            Directory.CreateDirectory(StandInRoot);
            // Not a secret: the stand-in's own fixed test key (fake_r2.py), so kn_publish signs every request as it will for R2.
            File.WriteAllText(StandInKeys, "{\"access_key_id\": \"TESTACCESSKEY\", \"secret_access_key\": \"test/secret+key\"}");
            var info = new ProcessStartInfo(settings.python, $"-I \"{Tool("fake_r2.py")}\" --root \"{StandInRoot}\" --port {settings.localPort}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Tool(""),
            };
            try
            {
                standIn = Process.Start(info);
                standIn.OutputDataReceived += (s, e) => { if (e.Data != null) Say("stand-in: " + e.Data); };
                standIn.ErrorDataReceived += (s, e) => { if (e.Data != null) Say("stand-in: " + e.Data); };
                standIn.BeginOutputReadLine();
                standIn.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Say("FAILED to start the stand-in (" + e.Message + "). Is Python installed? publisher-settings.json names it: " + settings.python);
                return;
            }
            for (int i = 0; i < 25 && !standInUp; i++)
            {
                await Task.Delay(200);
                standInUp = await ProbeStandIn();
            }
            Say(standInUp ? "Stand-in bucket started on " + settings.LocalEndpoint + " (files in " + StandInRoot + ")." : "FAILED: the stand-in did not answer on " + settings.LocalEndpoint + ".");
            UpdateStandInInfo();
        }

        private void StopStandIn()
        {
            try { if (standIn != null && !standIn.HasExited) standIn.Kill(); } catch (Exception) { }
            if (standIn != null) Say("Stand-in bucket stopped.");
            standIn = null;
            standInUp = false;
            UpdateStandInInfo();
        }

        private void UpdateStandInInfo()
        {
            if (IsDisposed) return;
            standInInfo.Text = settings.IsLocal
                ? $"{(standInUp ? "ONLINE" : "STOPPED")}  on {settings.LocalEndpoint}  (S3 address, every request signed like R2)\n" +
                  $"Public address (what launchers read): {settings.PublicBase}/<line>/manifest.signed and {settings.PublicBase}/files/<sha256>.gz\n" +
                  $"Files: {StandInRoot}\nFree, this PC only. It stops when the Publisher closes."
                : "The target is Cloudflare R2, so no stand-in runs. Set \"target\": \"local\" in publisher-settings.json to use it.";
            Invalidate();
        }

        private string LauncherJson(string line) =>
            "{\r\n  \"channel\": \"" + line + "\",\r\n  \"source\": \"" + settings.PublicBase + "/{channel}/{path}\",\r\n  \"files\": \"" + settings.PublicBase + "/{path}\"\r\n}";

        private void CopyLauncherJson()
        {
            Clipboard.SetText(launcherJson.Text);
            Say("launcher.json for the TEST line copied. Put it beside a copy of the launcher (not the players' one).");
        }

        // ------------------------------------------------------------------ running kn_publish

        private sealed class Result
        {
            public int Code;
            public Dictionary<string, object> Json = new Dictionary<string, object>();
            public bool Ok => Code == 0 && Json.TryGetValue("ok", out object ok) && ok is bool b && b;
            public string Error => Json.TryGetValue("error", out object e) ? e as string : "exit code " + Code;
        }

        private string ClientArgs()
        {
            if (settings.IsLocal) return $"--endpoint {settings.LocalEndpoint} --bucket kn-game --key-file \"{StandInKeys}\"";
            return $"--endpoint {settings.r2Endpoint} --bucket {settings.r2Bucket} --key-file \"{settings.r2KeyFile}\"";
        }

        private bool TargetReady()
        {
            if (settings.IsLocal && !standInUp) { Say("FAILED: the stand-in bucket is not running (STAND-IN tab, START)."); return false; }
            if (!settings.IsLocal && !settings.R2Ready) { Say("FAILED: R2 is not set up in publisher-settings.json (endpoint, bucket, key file, public address)."); return false; }
            return true;
        }

        private async Task<Result> Run(string what, string command)
        {
            busy = true;
            busyText = what.ToUpperInvariant() + "...";
            Invalidate();
            Say("> " + what);
            var result = new Result();
            var info = new ProcessStartInfo(settings.python, $"-I \"{Tool("kn_publish.py")}\" {ClientArgs()} {command}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = Tool(""),
            };
            string last = null;
            try
            {
                await Task.Run(() =>
                {
                    using (Process p = Process.Start(info))
                    {
                        p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Say(e.Data); };
                        p.BeginErrorReadLine();
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null)
                        {
                            if (line.StartsWith("{")) last = line;
                            else Say(line);
                        }
                        p.WaitForExit();
                        result.Code = p.ExitCode;
                    }
                });
                if (last != null) result.Json = new JavaScriptSerializer().DeserializeObject(last) as Dictionary<string, object> ?? result.Json;
            }
            catch (Exception e)
            {
                result.Code = -1;
                result.Json["error"] = e.Message;
            }
            busy = false;
            Invalidate();
            if (!result.Ok) Say("FAILED: " + result.Error);
            return result;
        }

        private async Task RefreshStatus()
        {
            if (!TargetReady()) return;
            Result r = await Run("Reading the lines", "status");
            if (!r.Ok) return;
            lastStatus = r.Json["channels"] as Dictionary<string, object>;
            for (int i = 0; i < 3; i++) cards[i].Show(lastStatus != null && lastStatus.TryGetValue(Lines[i], out object c) ? c as Dictionary<string, object> : null);
            object selected = archive.SelectedItem;
            archive.Items.Clear();
            foreach (object b in (r.Json["archive"] as object[]) ?? new object[0]) archive.Items.Add("build " + b);
            if (selected != null && archive.Items.Contains(selected)) archive.SelectedItem = selected;
            else if (archive.Items.Count > 1) archive.SelectedIndex = 1; // the one before the newest: the usual rollback
            else if (archive.Items.Count > 0) archive.SelectedIndex = 0;
        }

        private int LineBuild(string line) =>
            lastStatus != null && lastStatus.TryGetValue(line, out object c) && c is Dictionary<string, object> d ? Convert.ToInt32(d["build"]) : -1;

        // ------------------------------------------------------------------ actions

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = "The staged release folder (it holds manifest.signed and files\\)", SelectedPath = storeBox.Text })
                if (dialog.ShowDialog(this) == DialogResult.OK) storeBox.Text = dialog.SelectedPath;
        }

        private Dictionary<string, object> ReadStore()
        {
            string file = Path.Combine(storeBox.Text.Trim(), "manifest.signed");
            if (!File.Exists(file)) { storeInfo.ForeColor = RedText; storeInfo.Text = "No manifest.signed in this folder: pick the Store folder Publish to Test staged."; return null; }
            try
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                var m = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text.Substring(text.IndexOf('\n') + 1)) as Dictionary<string, object>;
                var files = (object[])m["files"];
                long packed = files.Cast<Dictionary<string, object>>().Sum(f => Convert.ToInt64(f["packedSize"]));
                long size = files.Cast<Dictionary<string, object>>().Sum(f => Convert.ToInt64(f["size"]));
                storeInfo.ForeColor = Paper;
                storeInfo.Text = $"Build {m["build"]}   {m["version"]}   made {Nice(m["created"] as string)}\n{files.Length} files, {Mb(packed)} packed ({Mb(size)} installed)";
                return m;
            }
            catch (Exception)
            {
                storeInfo.ForeColor = RedText;
                storeInfo.Text = "manifest.signed cannot be read.";
                return null;
            }
        }

        private async Task Upload()
        {
            Dictionary<string, object> m = ReadStore();
            if (m == null || !TargetReady()) return;
            if (!await Confirm("UPLOAD TO " + uploadLine.ToUpperInvariant(),
                    $"Send build {m["build"]} ({m["version"]}) to the {uploadLine.ToUpperInvariant()} line?\n\nLauncher copies that follow {uploadLine.ToUpperInvariant()} will download it the next time they open. LIVE does not change.",
                    "UPLOAD", Cyan)) return;
            settings.storeFolder = storeBox.Text.Trim();
            settings.Save(root);
            await Run("Uploading build " + m["build"] + " to " + uploadLine, $"upload --store \"{storeBox.Text.Trim()}\" --channel {uploadLine}");
            await RefreshStatus();
        }

        private async Task Promote()
        {
            if (!TargetReady()) return;
            await RefreshStatus();
            int test = LineBuild("test"), live = LineBuild("live");
            if (test < 0) { Say("FAILED: TEST holds nothing to promote."); return; }
            if (test == live) { Say("LIVE already holds build " + live + "."); return; }
            if (!await Confirm("PROMOTE TO LIVE",
                    $"Put TEST's build {test} on LIVE?\n\nLIVE now holds {(live < 0 ? "nothing" : "build " + live)}. Every player's launcher gets build {test} the next time it opens. " +
                    "Roll back puts the old one back if needed.\n\nThe game server must run the same build (it checks the version on join).", "PROMOTE", Red)) return;
            await Run("Promoting build " + test + " to live", "promote --from test --to live");
            await RefreshStatus();
        }

        private Task Rollback() => RollbackTo("live");

        private async Task RollbackTo(string line)
        {
            if (!TargetReady()) return;
            if (archive.SelectedItem == null) { Say("FAILED: no archived build to go back to."); return; }
            int build = int.Parse(archive.SelectedItem.ToString().Replace("build ", ""));
            int now = LineBuild(line);
            if (build == now) { Say(line.ToUpperInvariant() + " already holds build " + build + "."); return; }
            if (!await Confirm(line == "live" ? "ROLL BACK LIVE" : "COPY TO TEST",
                    $"Put archived build {build} on {line.ToUpperInvariant()}?\n\n{line.ToUpperInvariant()} now holds {(now < 0 ? "nothing" : "build " + now)}." +
                    (line == "live" ? " Players' launchers go back to build " + build + " the next time they open; roll the game server back to the same build too." : ""),
                    line == "live" ? "ROLL BACK" : "COPY", Red)) return;
            await Run($"Putting build {build} on {line}", $"rollback --channel {line} --build {build}");
            await RefreshStatus();
        }

        /// <summary>The confirm step, in the Krytical Nova look. In the self-test it takes its picture and answers yes.</summary>
        private async Task<bool> Confirm(string title, string body, string yes, Color yesColor)
        {
            using (var dialog = new ConfirmDialog(display, title, body, yes, yesColor))
            {
                if (selfTest == null) return dialog.ShowDialog(this) == DialogResult.OK;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(Left + (Width - dialog.Width) / 2, Top + (Height - dialog.Height) / 2);
                dialog.Show(this);
                await Task.Delay(400);
                PictureOf(dialog, "confirm-" + title.ToLowerInvariant().Replace(' ', '-'));
                dialog.Close();
                return true;
            }
        }

        // ------------------------------------------------------------------ self-test (local stand-in only)

        private int pictureCount;
        private readonly StringBuilder report = new StringBuilder();

        private void Note(string line)
        {
            Directory.CreateDirectory(selfTest.Folder);
            report.AppendLine(DateTime.Now.ToString("HH:mm:ss ") + line);
            File.WriteAllText(Path.Combine(selfTest.Folder, "report.txt"), report.ToString());
            Say("[self-test] " + line);
        }

        private void PictureOf(Control what, string name)
        {
            Directory.CreateDirectory(selfTest.Folder);
            using (var bitmap = new Bitmap(what.Width, what.Height))
            {
                what.DrawToBitmap(bitmap, new Rectangle(Point.Empty, what.Size));
                bitmap.Save(Path.Combine(selfTest.Folder, $"{++pictureCount:00}-{name}.png"));
            }
        }

        private async Task Picture(int tab, string name)
        {
            ShowTab(tab);
            await Task.Delay(350);
            Refresh();
            PictureOf(this, name);
        }

        private async Task<string> LauncherRun(string line)
        {
            if (string.IsNullOrEmpty(selfTest.Launcher)) return "(no launcher given)";
            File.WriteAllText(Path.Combine(selfTest.Launcher, "launcher.json"), LauncherJson(line));
            string exe = Path.Combine(selfTest.Launcher, "Krytical Nova Launcher.exe");
            int code = await Task.Run(() =>
            {
                using (Process p = Process.Start(new ProcessStartInfo(exe, "--headless --updated") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = selfTest.Launcher }))
                {
                    p.WaitForExit(600000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            });
            string[] lines = File.Exists(Path.Combine(selfTest.Launcher, "launcher.log")) ? File.ReadAllLines(Path.Combine(selfTest.Launcher, "launcher.log")) : new string[0];
            string tail = string.Join(" | ", lines.Skip(Math.Max(0, lines.Length - 2)));
            return $"launcher on {line.ToUpperInvariant()}: exit {code}; {tail}";
        }

        private async Task RunSelfTest()
        {
            Note("self-test on the LOCAL stand-in " + settings.LocalEndpoint + " (no other bucket is touched)");
            try
            {
                await Picture(0, "channels-start");
                storeBox.Text = selfTest.StoreA;
                PickLine("test");
                await Picture(1, "upload-picked-A");
                await Upload();
                Note("upload A -> test: TEST build " + LineBuild("test"));
                Note(await LauncherRun("test"));
                await Picture(0, "channels-after-upload-A");

                await Promote();
                Note("promote: LIVE build " + LineBuild("live"));
                Note(await LauncherRun("live"));
                await Picture(0, "channels-after-promote-A");

                if (!string.IsNullOrEmpty(selfTest.StoreB))
                {
                    storeBox.Text = selfTest.StoreB;
                    await Upload();
                    Note("upload B -> test: TEST build " + LineBuild("test"));
                    await Promote();
                    Note("promote: LIVE build " + LineBuild("live"));
                    Note(await LauncherRun("live"));
                    await Picture(0, "channels-after-promote-B");

                    archive.SelectedItem = "build " + LineBuildOf(selfTest.StoreA);
                    await Rollback();
                    Note("rollback: LIVE build " + LineBuild("live"));
                    Note(await LauncherRun("live"));
                    await Picture(0, "channels-after-rollback");
                }

                storeBox.Text = selfTest.StoreA;
                PickLine("dots");
                await Upload();
                Note("upload A -> dots: DOTS build " + LineBuild("dots"));
                await Picture(0, "channels-with-dots");
                await Picture(2, "stand-in");
                await Picture(3, "log");
                Note("DONE");
            }
            catch (Exception e)
            {
                Note("FAILED " + e);
            }
            Close();
        }

        private int LineBuildOf(string store)
        {
            string text = File.ReadAllText(Path.Combine(store, "manifest.signed"));
            var m = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text.Substring(text.IndexOf('\n') + 1)) as Dictionary<string, object>;
            return Convert.ToInt32(m["build"]);
        }

        // ------------------------------------------------------------------ helpers

        public static string Mb(long bytes) => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.00") + " GB" : (bytes / (double)(1 << 20)).ToString("0.0") + " MB";

        public static string Nice(string iso) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t) ? t.ToLocalTime().ToString("d MMM yyyy HH:mm") : iso ?? "";

        private void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception e) { Say("FAILED to open " + path + ": " + e.Message); }
        }

        private static Image LoadImage(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                return stream == null ? null : new Bitmap(Image.FromStream(stream));
        }
    }

    /// <summary>One line's card: what it holds now.</summary>
    public sealed class ChannelCard : Control
    {
        private readonly FontFamily display;
        public string Line = "";
        private Dictionary<string, object> data;

        public ChannelCard(FontFamily display)
        {
            this.display = display;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Show(Dictionary<string, object> holds) { data = holds; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Color fill = Line == "live" ? Color.FromArgb(0xde, 0x0d, 0x22) : Line == "test" ? Color.FromArgb(0x3e, 0xd4, 0xdf) : Color.FromArgb(0xf2, 0xb8, 0x3a);
            Color ink = Color.FromArgb(0x05, 0x07, 0x0d), paper = Color.FromArgb(0xf4, 0xee, 0xda);
            g.Clear(Color.FromArgb(0x1d, 0x25, 0x40));
            var box = new Rectangle(0, 0, Width - 7, Height - 7);
            using (var shadow = new SolidBrush(ink)) g.FillRectangle(shadow, 6, 6, Width - 7, Height - 7);
            using (var back = new SolidBrush(Color.FromArgb(0x14, 0x1a, 0x2b))) g.FillRectangle(back, box);
            using (var head = new SolidBrush(fill)) g.FillRectangle(head, 0, 0, box.Width, 50);
            using (var pen = new Pen(ink, 3f)) { g.DrawRectangle(pen, 1, 1, box.Width - 2, box.Height - 2); g.DrawLine(pen, 0, 50, box.Width, 50); }
            using (var font = new Font(display, 24f))
            using (var brush = new SolidBrush(Line == "live" ? paper : ink))
                g.DrawString(Line.ToUpperInvariant(), font, brush, 14, 7);
            using (var big = new Font(display, 34f))
            using (var small = new Font("Segoe UI Semibold", 10.5f))
            using (var muted = new SolidBrush(Color.FromArgb(0xa9, 0xad, 0xc2)))
            using (var text = new SolidBrush(paper))
            {
                if (data == null) { g.DrawString("EMPTY", big, muted, 14, 62); g.DrawString("Nothing published on this line yet.", small, muted, 16, 118); return; }
                g.DrawString("BUILD " + data["build"], big, text, 14, 60);
                float y = 116;
                foreach (string row in new[]
                         {
                             data["version"] as string,
                             "Made " + MainWindow.Nice(data["created"] as string),
                             $"{data["files"]} files, {MainWindow.Mb(Convert.ToInt64(data["packed"]))} to download",
                             "Installed size " + MainWindow.Mb(Convert.ToInt64(data["size"])),
                             "On the line since " + Since(data["modified"] as string),
                         })
                {
                    g.DrawString(row, small, row == data["version"] as string ? text : muted, new RectangleF(16, y, box.Width - 24, 22));
                    y += 23;
                }
            }
        }

        private static string Since(string http) => DateTime.TryParse(http, out DateTime t) ? t.ToLocalTime().ToString("d MMM yyyy HH:mm") : "-";
    }

    /// <summary>Confirm step: what will change, in plain words, and a red or cyan button to do it.</summary>
    public sealed class ConfirmDialog : Form
    {
        public ConfirmDialog(FontFamily display, string title, string body, string yes, Color yesColor)
        {
            Text = "Krytical Nova Publisher";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 330);
            BackColor = Color.FromArgb(0x1d, 0x25, 0x40);
            var head = new Label { Text = title, Dock = DockStyle.Top, Height = 56, Font = new Font(display, 26f), ForeColor = yesColor, UseCompatibleTextRendering = true, Padding = new Padding(18, 10, 0, 0) };
            var text = new Label { Text = body, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f), ForeColor = Color.FromArgb(0xf4, 0xee, 0xda), Padding = new Padding(20, 6, 20, 0) };
            var ok = new Comic(display, 16f) { Label = yes, Fill = yesColor, InkColor = yesColor == Color.FromArgb(0x3e, 0xd4, 0xdf) ? Color.FromArgb(0x05, 0x07, 0x0d) : Color.FromArgb(0xf4, 0xee, 0xda), Width = 170, Height = 44 };
            var cancel = new Comic(display, 16f) { Label = "CANCEL", Fill = Color.FromArgb(0x2b, 0x35, 0x60), InkColor = Color.FromArgb(0xf4, 0xee, 0xda), Width = 150, Height = 44 };
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 14, 0), BackColor = Color.Transparent };
            cancel.Margin = new Padding(0, 0, 0, 0);
            ok.Margin = new Padding(0, 0, 14, 0);
            row.Controls.Add(cancel);
            row.Controls.Add(ok);
            Controls.Add(text);
            Controls.Add(row);
            Controls.Add(head);
        }
    }

    /// <summary>A button drawn like the launcher's: a coloured block with an ink edge and a hard shadow.</summary>
    public sealed class Comic : Control
    {
        private readonly Font font;
        private bool hot, pressed;
        public Color Fill = Color.FromArgb(0x3e, 0xd4, 0xdf);
        public Color InkColor = Color.FromArgb(0x05, 0x07, 0x0d);
        public string Label = "";

        public Comic(FontFamily family, float size)
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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int shift = pressed ? 3 : 0;
            var box = new Rectangle(shift, shift, Width - 6, Height - 6);
            if (!pressed) using (var shadow = new SolidBrush(Color.FromArgb(0x05, 0x07, 0x0d))) g.FillRectangle(shadow, 5, 5, Width - 6, Height - 6);
            using (var brush = new SolidBrush(hot ? ControlPaint.Light(Fill, 0.15f) : Fill)) g.FillRectangle(brush, box);
            using (var pen = new Pen(Color.FromArgb(0x05, 0x07, 0x0d), 3f)) g.DrawRectangle(pen, box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2);
            using (var ink = new SolidBrush(InkColor))
                g.DrawString(Label, font, ink, box, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }
    }

    /// <summary>Bangers, the game's comic font, travels inside the app.</summary>
    public static class Fonts
    {
        private static PrivateFontCollection fonts;

        public static FontFamily Display()
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
                        fonts.AddMemoryFont(memory, data.Length);
                    }
                }
            }
            return fonts.Families.Length > 0 ? fonts.Families[0] : new FontFamily("Impact");
        }
    }
}
