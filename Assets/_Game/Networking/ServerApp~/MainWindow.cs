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
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KryticalNova.ServerApp
{
    /// <summary>
    /// Krytical Nova Server (owner, 18 Oct 2026): opening it starts the save service (when the server's settings name one)
    /// and then the game servers, all hidden inside this window, as Start Server.bat did with console windows. A tab per
    /// game server (online phase 5: the listed server, CONCORD, and each map's server from mapServers, such as TRANSIT
    /// ROW: status, log, commands, start / restart / stop, the control page), then SAVE SERVICE (status, log, start / stop)
    /// and DATABASE (Postgres, the saves' count and size, the backups). A restart a server asks for (exit code 3) starts it
    /// again; closing the window stops every server safely (everyone saved) after asking.
    /// </summary>
    public sealed class MainWindow : Form
    {
        private const int RestartExitCode = 3;
        private const int AnotherCopyExitCode = 4;
        private const string PostgresService = "postgresql-x64-18";
        private const string ConcordMap = "Zone01_StarterTown";

        private static readonly Color Page = Color.FromArgb(0x14, 0x1a, 0x2b);
        private static readonly Color Panel = Color.FromArgb(0x1d, 0x25, 0x40);
        private static readonly Color Edge = Color.FromArgb(0x2b, 0x35, 0x60);
        private static readonly Color Ink = Color.FromArgb(0x05, 0x07, 0x0d);
        private static readonly Color Paper = Color.FromArgb(0xf4, 0xee, 0xda);
        private static readonly Color Muted = Color.FromArgb(0xa9, 0xad, 0xc2);
        private static readonly Color Cyan = Color.FromArgb(0x3e, 0xd4, 0xdf);
        private static readonly Color Red = Color.FromArgb(0xde, 0x0d, 0x22);
        private static readonly Color RedText = Color.FromArgb(0xff, 0x4a, 0x5c);
        private static readonly Color Green = Color.FromArgb(0x7b, 0xe0, 0x7b);

        /// <summary>One game server the app runs: the listed one, or a map's (online phase 5), each with its own tab.</summary>
        private sealed class GameServer
        {
            public string Title, Map;
            public int Port, PagePort;
            public bool IsMap;
            public Hosted Process;
            public readonly RichTextBox Log = MainWindow.Log();
            public readonly Label Info = MainWindow.Info();
            public readonly TextBox Command = new TextBox();
            public long Seen;
            public int Online;
            public string State = "STOPPED";
            public bool Stopping;
        }

        private readonly FontFamily display;
        private readonly Image logo;
        private readonly string root;
        private readonly string serverFolder;
        private readonly Settings settings;
        private readonly List<GameServer> servers = new List<GameServer>();
        private readonly Hosted saves;
        private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        private readonly Timer tick = new Timer { Interval = 400 };

        private readonly Comic[] tabs;
        private readonly Control[] pages;
        private int shown;

        private readonly Label savesInfo = Info(), databaseInfo = Info();
        private readonly RichTextBox savesLog = Log();
        private readonly ListView backups = new ListView();
        private long savesSeen;
        private bool closingAfterStop, finishing;
        private string savesState = "-", databaseState = "-", lastHealth = "";
        private DateTime nextStatus = DateTime.MinValue;

        private readonly string selfTest;

        /// <summary>The listed server (the one players see, Nova): it publishes the list and makes the backups.</summary>
        private GameServer Listed => servers[0];
        private int SavesTab => servers.Count;
        private int DatabaseTab => servers.Count + 1;

        public MainWindow(string root, string selfTest = null)
        {
            this.root = root;
            this.selfTest = selfTest;
            display = Fonts.Display();
            logo = LoadImage("Logo.png");
            serverFolder = FindServerFolder(root);
            settings = Settings.Load(serverFolder);

            Text = "Krytical Nova Server";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ClientSize = new Size(1080, 700);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Page;
            DoubleBuffered = true;

            string serverExe = serverFolder != null ? Path.Combine(serverFolder, "KryticalNovaServer.exe") : "";
            string folder = serverFolder ?? root;
            string listedMap = string.IsNullOrWhiteSpace(settings.Map) ? ConcordMap : settings.Map;
            AddServer(new GameServer
            {
                Title = listedMap == ConcordMap ? "CONCORD" : "SERVER", Map = listedMap, Port = settings.Port, PagePort = settings.PagePort,
                Process = new Hosted("Server", serverExe, "-logFile -", folder),
            });
            // Each map's server: the same server, its own map and ports, never in the server list (online phase 5).
            foreach (Settings.MapServer map in settings.MapServers ?? new List<Settings.MapServer>())
            {
                if (map == null || string.IsNullOrWhiteSpace(map.Map) || map.Port <= 0) continue;
                string title = string.IsNullOrWhiteSpace(map.Name) ? map.Map : map.Name;
                AddServer(new GameServer
                {
                    Title = title.ToUpperInvariant(), Map = map.Map, Port = map.Port, PagePort = map.PagePort, IsMap = true,
                    Process = new Hosted(title, serverExe, $"-logFile - -role map -map \"{map.Map}\" -port {map.Port} -pageport {map.PagePort}", folder),
                });
            }
            string savesExe = Path.Combine(root, "SaveService", "SaveService.exe");
            saves = new Hosted("Save service", savesExe, settings.TestSaves ? "--test" : "", Path.Combine(root, "SaveService"));
            tabs = new Comic[servers.Count + 2];
            pages = new Control[servers.Count + 2];

            BuildTabs();
            for (int i = 0; i < servers.Count; i++) BuildServerPage(i);
            BuildSavesPage();
            BuildDatabasePage();
            Show(0);

            tick.Tick += (s, e) => Tick();
            tick.Start();
            Shown += (s, e) => { StartAll(); if (selfTest != null) RunSelfTest(); };
            FormClosing += OnClosing;
            Resize += (s, e) => Layout2();
            Layout2();
        }

        private void AddServer(GameServer server)
        {
            servers.Add(server);
            server.Process.Exited += code => BeginInvoke((Action)(() => ServerEnded(server, code)));
        }

        // ------------------------------------------------------------------ layout

        private void BuildTabs()
        {
            var names = new List<string>();
            foreach (GameServer server in servers) names.Add(server.Title);
            names.Add("SAVE SERVICE");
            names.Add("DATABASE");
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                tabs[i] = new Comic(display, 18f) { Label = names[i] };
                tabs[i].Click += (s, e) => Show(index);
                Controls.Add(tabs[i]);
            }
        }

        private void Show(int index)
        {
            shown = index;
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Fill = i == index ? Cyan : Edge;
                tabs[i].InkColor = i == index ? Ink : Paper;
                tabs[i].Invalidate();
                if (pages[i] != null) pages[i].Visible = i == index;
            }
            if (index == DatabaseTab) RefreshBackups();
        }

        private void Layout2()
        {
            int top = 112, left = 24, width = ClientSize.Width - 48;
            int tabWidth = Math.Min(186, (width - (tabs.Length - 1) * 10) / tabs.Length);
            for (int i = 0; i < tabs.Length; i++) tabs[i].Bounds = new Rectangle(left + i * (tabWidth + 10), top, tabWidth, 44);
            var area = new Rectangle(left, top + 58, width, ClientSize.Height - top - 58 - 20);
            foreach (Control page in pages) if (page != null) page.Bounds = area;
            Invalidate();
        }

        private Panel NewPage(int index)
        {
            var page = new Panel { BackColor = Panel, Padding = new Padding(16) };
            pages[index] = page;
            Controls.Add(page);
            return page;
        }

        private void BuildServerPage(int index)
        {
            GameServer server = servers[index];
            Panel page = NewPage(index);
            var buttons = Row(
                Button("START", Cyan, () => StartServer(server)),
                Button("RESTART", Edge, () => Restart(server)),
                Button("STOP", Red, () => StopServer(server, false)),
                Button("CONTROL PAGE", Edge, () => OpenUrl($"http://localhost:{server.PagePort}")));
            var commandRow = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = Panel };
            TextBox command = server.Command;
            command.Font = new Font("Consolas", 11f);
            command.BackColor = Page;
            command.ForeColor = Paper;
            command.BorderStyle = BorderStyle.FixedSingle;
            command.Dock = DockStyle.Fill;
            command.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunCommand(server); } };
            var run = Button("RUN", Cyan, () => RunCommand(server));
            run.Dock = DockStyle.Right;
            run.Width = 90;
            commandRow.Controls.Add(command);
            commandRow.Controls.Add(run);
            var hint = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Muted, Font = new Font("Segoe UI", 9f), Text = "Type a GM command and press Enter (gm.help lists them)." };
            server.Log.Dock = DockStyle.Fill;
            page.Controls.Add(server.Log);
            page.Controls.Add(hint);
            page.Controls.Add(commandRow);
            page.Controls.Add(buttons);
            page.Controls.Add(server.Info);
        }

        private void BuildSavesPage()
        {
            Panel page = NewPage(SavesTab);
            var buttons = Row(Button("START", Cyan, () => StartSaves()), Button("STOP", Red, () => StopSaves()));
            savesLog.Dock = DockStyle.Fill;
            page.Controls.Add(savesLog);
            page.Controls.Add(buttons);
            page.Controls.Add(savesInfo);
        }

        private void BuildDatabasePage()
        {
            Panel page = NewPage(DatabaseTab);
            var buttons = Row(
                Button("BACK UP NOW", Cyan, BackUpNow),
                Button("BACKUPS FOLDER", Edge, () => OpenFolder(settings.BackupFolder(serverFolder))),
                Button("REFRESH", Edge, RefreshBackups));
            backups.View = View.Details;
            backups.FullRowSelect = true;
            backups.BackColor = Page;
            backups.ForeColor = Paper;
            backups.BorderStyle = BorderStyle.None;
            backups.Font = new Font("Segoe UI", 10f);
            backups.OwnerDraw = true;
            backups.DrawColumnHeader += (s, e) =>
            {
                using (var fill = new SolidBrush(Edge)) e.Graphics.FillRectangle(fill, e.Bounds);
                using (var ink = new SolidBrush(Cyan)) e.Graphics.DrawString(e.Header.Text, new Font("Segoe UI Semibold", 10f), ink, e.Bounds.X + 6, e.Bounds.Y + 3);
            };
            backups.DrawItem += (s, e) => e.DrawDefault = true;
            backups.DrawSubItem += (s, e) => e.DrawDefault = true;
            backups.Columns.Add("Backup", 420);
            backups.Columns.Add("Made", 180);
            backups.Columns.Add("Size", 120);
            backups.Dock = DockStyle.Fill;
            var title = new Label { Dock = DockStyle.Top, Height = 30, ForeColor = Cyan, Font = new Font(display, 16f), Text = "BACKUPS (NEWEST FIRST)" };
            page.Controls.Add(backups);
            page.Controls.Add(title);
            page.Controls.Add(buttons);
            page.Controls.Add(databaseInfo);
        }

        private Comic Button(string label, Color fill, Action click)
        {
            var b = new Comic(display, 15f) { Label = label, Fill = fill, InkColor = fill == Edge ? Paper : fill == Red ? Paper : Ink, Width = 170, Height = 42 };
            b.Click += (s, e) => click();
            return b;
        }

        private static Panel Row(params Control[] items)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 6) };
            foreach (Control item in items) { item.Margin = new Padding(0, 0, 12, 0); row.Controls.Add(item); }
            return row;
        }

        private static Label Info() => new Label
        {
            Dock = DockStyle.Top, Height = 66, ForeColor = Paper, Font = new Font("Segoe UI Semibold", 11f), BackColor = Color.Transparent,
        };

        private static RichTextBox Log() => new RichTextBox
        {
            ReadOnly = true, BackColor = Color.FromArgb(0x0d, 0x12, 0x22), ForeColor = Paper, BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9.5f), WordWrap = false, DetectUrls = false,
        };

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
            Banner(g, "SERVER", new Point(222, 60), Red);
            // The three at a glance, top right; with map servers, how many of the servers are on.
            int x0 = ClientSize.Width - 24;
            x0 = Pill(g, "DATABASE " + databaseState, x0, databaseState == "RUNNING");
            x0 = Pill(g, "SAVES " + savesState, x0, savesState == "OK" || savesState == "FILES");
            int on = servers.Count(s => s.State == "ONLINE");
            Pill(g, servers.Count == 1 ? "SERVER " + Listed.State : $"SERVERS {on}/{servers.Count} ONLINE", x0, on == servers.Count);
        }

        private int Pill(Graphics g, string text, int right, bool good)
        {
            using (var font = new Font(display, 14f))
            {
                SizeF size = g.MeasureString(text, font);
                var box = new RectangleF(right - size.Width - 22, 28, size.Width + 18, size.Height + 6);
                using (var pen = new Pen(good ? Green : RedText, 2.5f)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                using (var brush = new SolidBrush(good ? Green : RedText)) g.DrawString(text, font, brush, box.X + 9, box.Y + 3);
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
                using (var ink = new SolidBrush(Paper)) g.DrawString(text, font, ink, 5, 1);
                g.Restore(state);
            }
        }

        // ------------------------------------------------------------------ starting and stopping

        private void StartAll()
        {
            if (serverFolder == null)
            {
                Listed.Info.Text = "No server found beside this app (a folder holding KryticalNovaServer.exe). Build the server first.";
                return;
            }
            if (!settings.UsesSaves) { savesState = "FILES"; StartServers(); return; }
            Task.Run(async () =>
            {
                bool already = await Answers();
                BeginInvoke((Action)(() =>
                {
                    if (already) saves.Note("A save service is already answering on " + settings.SaveService + " (started outside this app): using it.");
                    else StartSaves();
                }));
                // The save service needs a moment to open its port before the servers ask it anything.
                for (int i = 0; i < 20 && !already && !await Answers(); i++) await Task.Delay(500);
                BeginInvoke((Action)StartServers);
            });
        }

        private void StartServers()
        {
            foreach (GameServer server in servers) StartServer(server);
        }

        private void StartSaves()
        {
            if (!settings.UsesSaves) { saves.Note("This server saves to files (no saveService in server-settings.json): no save service needed."); return; }
            if (saves.Running) return;
            if (!saves.Start(out string problem)) saves.Note("Could not start the save service: " + problem);
        }

        private void StopSaves()
        {
            if (settings.UsesSaves && servers.Any(s => s.Process.Running))
            {
                MessageBox.Show(this, "The servers are saving through the save service. Stop them first.", "Save service", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            saves.Kill();
        }

        private void StartServer(GameServer server)
        {
            if (server.Process.Running) return;
            // A map server saves only through the save service (two servers cannot share a save folder): it would stop at once.
            if (server.IsMap && !settings.UsesSaves)
            {
                server.Process.Note("Map servers save through the save service only: put \"saveService\" in server-settings.json (the Server Guide says how), then press START.");
                return;
            }
            server.Stopping = false;
            server.Online = 0;
            server.State = "STARTING";
            if (!server.Process.Start(out string problem)) { server.Process.Note("Could not start the server: " + problem); server.State = "STOPPED"; }
            Invalidate();
        }

        private void Restart(GameServer server)
        {
            if (!server.Process.Running) { StartServer(server); return; }
            if (!Ask($"Restart {Called(server)}? Everyone on it is saved first, and they are told to join again in a minute.")) return;
            server.Process.Send("gm.restart 0 \"The server is restarting. Join again in a minute.\"");
            server.State = "RESTARTING";
            Invalidate();
        }

        /// <summary>Stops a server safely: the GM shutdown saves everyone (and backs up), then the server ends by itself.</summary>
        private void StopServer(GameServer server, bool forClosing)
        {
            if (!server.Process.Running) { if (forClosing) ClosedOne(); return; }
            if (!forClosing && !Ask($"Stop {Called(server)}? Everyone on it is saved first.")) return;
            server.Stopping = true;
            server.State = "STOPPING";
            server.Process.Send("gm.shutdown 0 \"The server is closing.\"");
            Invalidate();
            Task.Run(() => server.Process.StopWithin(90000));
        }

        private static string Called(GameServer server) => server.IsMap ? $"the {server.Title} server" : "the server";

        private void ServerEnded(GameServer server, int code)
        {
            server.Online = 0;
            if (code == RestartExitCode && !server.Stopping && !closingAfterStop)
            {
                server.State = "RESTARTING";
                server.Process.Note("--- restarting ---");
                Task.Delay(1500).ContinueWith(_ => BeginInvoke((Action)(() => StartServer(server))));
                return;
            }
            server.State = "STOPPED";
            if (code == AnotherCopyExitCode) server.Process.Note("Another copy of this server is already running on this PC (a Start Server.bat window?). Close it, then press START.");
            Invalidate();
            if (closingAfterStop) ClosedOne();
        }

        // While closing: once every server has stopped, the save service goes and the window closes.
        private void ClosedOne()
        {
            if (servers.Any(s => s.Process.Running)) return;
            FinishClosing();
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            bool running = servers.Any(s => s.Process.Running);
            if (closingAfterStop && !running) return;
            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                foreach (GameServer server in servers)
                    if (server.Process.Running) server.Process.Send("gm.shutdown 0 \"The server PC is shutting down.\"");
                foreach (GameServer server in servers) server.Process.StopWithin(20000);
                saves.Kill();
                return;
            }
            if (running)
            {
                e.Cancel = true;
                if (closingAfterStop) return; // already on its way
                if (!Ask("Close Krytical Nova Server? Every server stops safely first (everyone is saved), and the save service with them.")) return;
                closingAfterStop = true;
                foreach (GameServer server in servers) StopServer(server, true);
                return;
            }
            saves.Kill();
        }

        private void FinishClosing()
        {
            if (finishing) return;
            finishing = true;
            saves.Kill();
            closingAfterStop = true;
            BeginInvoke((Action)Close);
        }

        private void RunCommand(GameServer server)
        {
            string line = server.Command.Text.Trim();
            if (line.Length == 0) return;
            if (!server.Process.Send(line)) server.Process.Note("The server is not running.");
            else server.Process.Note("> " + line);
            server.Command.Clear();
        }

        // The listed server's own zip of its saves (when it runs; map servers make none) and, on the real database, a copy
        // of the whole database as the nightly one (online phase 4, milestone 6) made by the save service.
        private void BackUpNow()
        {
            bool database = settings.UsesSaves && !settings.TestSaves && saves.Running;
            Hosted server = Listed.Process;
            if (server.Running) { server.Send("gm.backup"); server.Note("> gm.backup"); Task.Delay(4000).ContinueWith(_ => BeginInvoke((Action)RefreshBackups)); }
            if (database) Task.Run(DatabaseCopyNow);
            if (!server.Running && !database)
                MessageBox.Show(this, "Backups are made by the running server and the save service: press START first.", "Back up", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task DatabaseCopyNow()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, settings.SaveService.TrimEnd('/') + "/backup");
                request.Headers.Add("X-KN-Key", ServiceKey());
                using (HttpResponseMessage response = await http.SendAsync(request))
                {
                    string said = await response.Content.ReadAsStringAsync();
                    Match line = Regex.Match(said, @"""line""\s*:\s*""((?:[^""\\]|\\.)*)""");
                    BeginInvoke((Action)(() => saves.Note(line.Success ? Regex.Unescape(line.Groups[1].Value) : "[Backup] The save service answered " + (int)response.StatusCode + ".")));
                }
            }
            catch (Exception e)
            {
                BeginInvoke((Action)(() => saves.Note("[Backup] The save service did not answer: " + e.Message)));
            }
        }

        // The nightly copies of the database, from the save service's health check: when the last was made, whether it is
        // in Google Drive, and anything wrong.
        private static string NightlyLine(string health)
        {
            if (string.IsNullOrEmpty(health)) return "";
            if (Regex.IsMatch(health, @"""backup""\s*:\s*null")) return "Nightly copies: none for the TEST database.";
            Match last = Regex.Match(health, @"""last""\s*:\s*""([^""]+)""");
            Match problem = Regex.Match(health, @"""problem""\s*:\s*""((?:[^""\\]|\\.)*)""");
            string trouble = problem.Success ? "  ·  PROBLEM: " + Regex.Unescape(problem.Groups[1].Value) : "";
            if (!last.Success || !DateTime.TryParse(last.Groups[1].Value, out DateTime made))
                return "Nightly copies: none yet (the first is made a few minutes after the save service starts, then every night at 4 AM)." + trouble;
            bool offsite = Regex.IsMatch(health, @"""offsite""\s*:\s*true");
            double hours = (DateTime.Now - made).TotalHours;
            return $"Nightly copy: last {made:d MMM HH:mm} ({(hours < 1 ? "under an hour" : $"{hours:0} h")} ago)  ·  {Number(health, "kept")} kept (14 days)  ·  " +
                   (offsite ? "in Google Drive" : "NOT copied off this PC") + trouble;
        }

        // ------------------------------------------------------------------ the regular look at everything

        private void Tick()
        {
            foreach (GameServer server in servers)
            {
                long seen = server.Seen;
                Pour(server.Process, server.Log, ref seen, server);
                server.Seen = seen;
            }
            Pour(saves, savesLog, ref savesSeen, null);
            if (DateTime.Now < nextStatus) return;
            nextStatus = DateTime.Now.AddSeconds(3);
            Task.Run(Check);
        }

        // New lines into the tab's log; a server's join and leave lines keep its player count.
        private void Pour(Hosted from, RichTextBox into, ref long seen, GameServer counted)
        {
            var fresh = from.LinesSince(seen, out seen);
            if (fresh.Count == 0) return;
            bool atEnd = into.SelectionStart >= into.TextLength - 2;
            var sb = new StringBuilder();
            foreach (string line in fresh)
            {
                if (Noise(line)) continue;
                sb.AppendLine(line);
                if (counted != null)
                {
                    Match m = Regex.Match(line, @"Online: (\d+)");
                    if (m.Success) counted.Online = int.Parse(m.Groups[1].Value);
                    if (line.Contains("is listening on port") && counted.Process.Running) counted.State = "ONLINE";
                }
            }
            if (sb.Length == 0) return;
            into.AppendText(sb.ToString());
            if (into.Lines.Length > Hosted.MaxLines) { into.Select(0, into.GetFirstCharIndexFromLine(into.Lines.Length - Hosted.MaxLines)); into.SelectedText = ""; }
            if (atEnd) { into.SelectionStart = into.TextLength; into.ScrollToCaret(); }
        }

        // Unity's start-up chatter that is no problem (the control page hides the same).
        private static readonly Regex NoisePattern = new Regex(@"memorysetup|serialization layout|Did you #if UNITY_EDITOR|referenced script|UnloadTime|Unloading \d+|shader|IMGUI module|Unknown managed type", RegexOptions.IgnoreCase);
        private static bool Noise(string line) => NoisePattern.IsMatch(line);

        private async Task<bool> Answers()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, settings.SaveService.TrimEnd('/') + "/health");
                request.Headers.Add("X-KN-Key", ServiceKey());
                using (HttpResponseMessage response = await http.SendAsync(request)) return response.IsSuccessStatusCode;
            }
            catch (Exception) { return false; }
        }

        private async Task Check()
        {
            // The save service and its database, through the service's own health check.
            string health = "", savesNow, databaseNow;
            if (!settings.UsesSaves) savesNow = "FILES";
            else
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, settings.SaveService.TrimEnd('/') + "/health");
                    request.Headers.Add("X-KN-Key", ServiceKey());
                    using (HttpResponseMessage response = await http.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            health = await response.Content.ReadAsStringAsync();
                            savesNow = "OK";
                        }
                        else savesNow = response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "KEY?" : "DB DOWN";
                    }
                }
                catch (Exception) { savesNow = saves.Running ? "STARTING" : "DOWN"; }
            }
            try
            {
                using (var service = new ServiceController(PostgresService))
                    databaseNow = service.Status == ServiceControllerStatus.Running ? "RUNNING" : service.Status.ToString().ToUpperInvariant();
            }
            catch (Exception) { databaseNow = "NOT INSTALLED"; }

            BeginInvoke((Action)(() =>
            {
                savesState = savesNow;
                databaseState = databaseNow;
                lastHealth = health;
                foreach (GameServer server in servers)
                {
                    if (server.Process.Running && server.State == "STOPPED") server.State = "STARTING";
                    if (!server.Process.Running && server.State != "RESTARTING" && server.State != "STARTING") server.State = "STOPPED";
                }
                UpdateInfo();
                Invalidate(new Rectangle(0, 0, ClientSize.Width, 105));
            }));
        }

        private void UpdateInfo()
        {
            foreach (GameServer server in servers)
            {
                string up = server.Process.Running ? $"up {(int)(DateTime.Now - server.Process.StartedAt).TotalMinutes} min" : "not running";
                string what = server.IsMap
                    ? $"map server of {server.Map}: never in the server list (players see one server)"
                    : $"the listed server{(servers.Count > 1 ? " (the one players see)" : "")}  ·  saves: {(settings.UsesSaves ? "the save database" : "files")}";
                server.Info.Text = $"{server.State}  ·  {server.Online} player(s) on  ·  {up}  ·  port {server.Port}\n" +
                                   $"Control page: http://localhost:{server.PagePort}  ·  {what}  ·  {serverFolder}";
            }
            long documents = Number(lastHealth, "documents"), bytes = Number(lastHealth, "bytes");
            savesInfo.Text = !settings.UsesSaves
                ? "This server saves to files beside it, so it needs no save service.\nTo use the database: \"saveService\" in server-settings.json (the Server Guide says how)."
                : $"{savesState}  ·  {settings.SaveService}  ·  {(settings.TestSaves ? "TEST database (krytical_nova_test)" : "REAL database (krytical_nova)")}\n" +
                  (savesState == "OK" ? $"{documents} saves  ·  {bytes / 1048576.0:0.0} MB" : saves.Running ? "Starting..." : "Not running: press START.");
            databaseInfo.Text = $"Postgres: {databaseState} (a Windows service; it starts with Windows by itself)\n" +
                                (savesState == "OK" ? $"{documents} saves in it  ·  {bytes / 1048576.0:0.0} MB on disk\n" + NightlyLine(lastHealth)
                                    : "The save service is not answering, so the database cannot be read from here.");
        }

        private void RefreshBackups()
        {
            backups.Items.Clear();
            string folder = settings.BackupFolder(serverFolder);
            if (folder == null || !Directory.Exists(folder)) return;
            foreach (FileInfo file in new DirectoryInfo(folder).GetFiles("save_*.zip").OrderByDescending(f => f.Name))
                backups.Items.Add(new ListViewItem(new[] { file.Name, file.LastWriteTime.ToString("d MMM yyyy HH:mm"), $"{file.Length / 1024} KB" }));
        }

        // ------------------------------------------------------------------ self-test (for building the app, not for the owner)

        /// <summary>
        /// --selftest &lt;folder&gt;: waits for every server, pictures each tab, runs gm.players through each server's command
        /// box, makes a backup, then closes the safe way, writing the logs into the folder. Lets the app be checked without a
        /// screen.
        /// </summary>
        private async void RunSelfTest()
        {
            Directory.CreateDirectory(selfTest);
            var report = new StringBuilder();
            void Say(string line) { report.AppendLine(DateTime.Now.ToString("HH:mm:ss ") + line); File.WriteAllText(Path.Combine(selfTest, "report.txt"), report.ToString()); }
            void Picture(string name) { using (var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize)); bitmap.Save(Path.Combine(selfTest, name + ".png")); } }
            Say("started; server folder " + serverFolder + "; saves " + (settings.UsesSaves ? settings.SaveService : "files") + "; " + servers.Count + " server(s): " +
                string.Join(", ", servers.Select(s => $"{s.Title} {s.Map} port {s.Port} page {s.PagePort}")));
            for (int i = 0; i < 240 && servers.Any(s => s.State != "ONLINE"); i++) await Task.Delay(500);
            Say("servers " + string.Join(", ", servers.Select(s => s.Title + " " + s.State)) + ", saves " + savesState + ", database " + databaseState);
            await Task.Delay(3500);
            for (int i = 0; i < servers.Count; i++)
            {
                Show(i);
                servers[i].Command.Text = "gm.players";
                RunCommand(servers[i]);
                await Task.Delay(3000);
                Picture($"1-server-{i}-{servers[i].Title.ToLowerInvariant().Replace(' ', '-')}");
            }
            Show(SavesTab); await Task.Delay(3500); Picture("3-save-service");
            Show(DatabaseTab); await Task.Delay(500); Picture("4-database");
            int before = backups.Items.Count;
            BackUpNow();
            await Task.Delay(7000);
            RefreshBackups();
            Say($"backups {before} -> {backups.Items.Count}");
            Picture("5-database-after-backup");
            Show(0);
            Say("closing the safe way");
            for (int i = 0; i < servers.Count; i++)
            {
                GameServer server = servers[i];
                string log = Path.Combine(selfTest, $"server-log-{i}.txt");
                File.WriteAllText(log, server.Log.Text);
                server.Process.Exited += code =>
                {
                    Say(server.Title + " exit code " + code);
                    File.WriteAllText(log, string.Join(Environment.NewLine, server.Process.LinesSince(0, out _)));
                };
            }
            File.WriteAllText(Path.Combine(selfTest, "saves-log.txt"), savesLog.Text);
            closingAfterStop = true;
            foreach (GameServer server in servers) StopServer(server, true);
        }

        // ------------------------------------------------------------------ helpers

        private bool Ask(string question) => MessageBox.Show(this, question, "Krytical Nova Server", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { /* no browser */ }
        }

        private static void OpenFolder(string folder)
        {
            if (folder != null && Directory.Exists(folder)) Process.Start("explorer.exe", folder);
        }

        private static long Number(string json, string name)
        {
            Match m = Regex.Match(json ?? "", "\"" + name + "\"\\s*:\\s*(\\d+)");
            return m.Success ? long.Parse(m.Groups[1].Value) : 0;
        }

        private static string ServiceKey()
        {
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KryticalNova-Secrets", "save-service-key.txt");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        }

        // The server is the folder beside this app that holds KryticalNovaServer.exe (KryticalNovaServer, MeasureServer, ...).
        private static string FindServerFolder(string root)
        {
            string usual = Path.Combine(root, "KryticalNovaServer");
            if (File.Exists(Path.Combine(usual, "KryticalNovaServer.exe"))) return usual;
            foreach (string folder in Directory.GetDirectories(root))
                if (File.Exists(Path.Combine(folder, "KryticalNovaServer.exe"))) return folder;
            return null;
        }

        private static Image LoadImage(string name)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    return stream != null ? new Bitmap(Image.FromStream(stream)) : null;
            }
            catch (Exception) { return null; }
        }

        // Dark top bar like the launcher's.
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1, caption = Page.R | (Page.G << 8) | (Page.B << 16), text = Paper.R | (Paper.G << 8) | (Paper.B << 16);
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
                DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
            }
            catch (Exception) { /* an older Windows keeps its own bar */ }
        }
    }

    /// <summary>What the app reads from the server's server-settings.json.</summary>
    [DataContract]
    public sealed class Settings
    {
        [DataMember(Name = "port")] public int Port = 7777;
        [DataMember(Name = "pagePort")] public int PagePort = 8090;
        [DataMember(Name = "saveFolder")] public string SaveFolder = "";
        [DataMember(Name = "saveService")] public string SaveService = "";
        [DataMember(Name = "map")] public string Map = "";
        [DataMember(Name = "mapServers")] public List<MapServer> MapServers = new List<MapServer>();

        /// <summary>One map's server (online phase 5): started beside the listed one, never in the server list.</summary>
        [DataContract]
        public sealed class MapServer
        {
            [DataMember(Name = "name")] public string Name = "";
            [DataMember(Name = "map")] public string Map = "";
            [DataMember(Name = "port")] public int Port;
            [DataMember(Name = "pagePort")] public int PagePort;
        }

        public bool UsesSaves => !string.IsNullOrWhiteSpace(SaveService);
        public bool TestSaves => UsesSaves && SaveService.Contains(":7802");

        public string BackupFolder(string serverFolder)
        {
            if (serverFolder == null) return null;
            string save = string.IsNullOrWhiteSpace(SaveFolder)
                ? Path.Combine(Path.GetDirectoryName(serverFolder) ?? serverFolder, "KryticalNovaServer-Save")
                : Path.IsPathRooted(SaveFolder) ? SaveFolder : Path.Combine(serverFolder, SaveFolder);
            save = Path.GetFullPath(save).TrimEnd('\\', '/');
            return Path.Combine(Path.GetDirectoryName(save) ?? save, Path.GetFileName(save) + "-Backups");
        }

        public static Settings Load(string serverFolder)
        {
            string file = serverFolder != null ? Path.Combine(serverFolder, "server-settings.json") : null;
            if (file == null || !File.Exists(file)) return new Settings();
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(File.ReadAllText(file));
                using (var stream = new MemoryStream(bytes))
                    return (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream) ?? new Settings();
            }
            catch (Exception) { return new Settings(); }
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
                        fonts.AddMemoryFont(memory, data.Length); // kept for the life of the program, as GDI+ needs
                    }
                }
            }
            return fonts.Families.Length > 0 ? fonts.Families[0] : new FontFamily("Impact");
        }
    }
}
