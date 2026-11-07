using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppNetworkSwitch
{
    public sealed class AppEntry
    {
        public string Path;
        public string Name;
        public override string ToString() { return Name; }
    }

    public static class AppDiscovery
    {
        private static readonly string[] RelativeExecutables = {
            @"Support Files\Contents\Windows\Illustrator.exe", "Photoshop.exe",
            "Adobe Premiere Pro.exe", @"Support Files\AfterFX.exe", "AfterFX.exe",
            "Lightroom.exe", "InDesign.exe", "Bridge.exe", "Adobe Audition.exe",
            "Adobe Media Encoder.exe", "Animate.exe", "Dreamweaver.exe",
            @"Acrobat\Acrobat.exe", "Acrobat.exe"
        };

        public static IList<AppEntry> Find(IList<RuleRecord> rules)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RuleRecord rule in rules)
            {
                if (rule.Group != RuleManager.Group) continue;
                try {
                    string path = RuleManager.NormalizePath(rule.Program, false);
                    if (RuleManager.Owns(rule, path)) paths.Add(path);
                } catch (ArgumentException) { }
            }
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null) continue;
                        foreach (string name in uninstall.GetSubKeyNames())
                        using (RegistryKey app = uninstall.OpenSubKey(name))
                        {
                            if (app == null) continue;
                            string title = Convert.ToString(app.GetValue("DisplayName"));
                            if (title.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            FindInDirectory(Convert.ToString(app.GetValue("InstallLocation")), paths);
                        }
                    }
                }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            foreach (string programFiles in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                string adobe = System.IO.Path.Combine(programFiles, "Adobe");
                if (!Directory.Exists(adobe)) continue;
                try {
                    foreach (string directory in Directory.GetDirectories(adobe)) FindInDirectory(directory, paths);
                } catch (UnauthorizedAccessException) { } catch (IOException) { }
            }
            return paths.Select(CreateEntry).OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void FindInDirectory(string directory, HashSet<string> paths)
        {
            if (String.IsNullOrWhiteSpace(directory)) return;
            directory = directory.Trim().Trim('"');
            if (!System.IO.Path.IsPathRooted(directory) || directory.StartsWith(@"\\")) return;
            foreach (string relative in RelativeExecutables)
            {
                try {
                    string path = RuleManager.NormalizePath(System.IO.Path.Combine(directory, relative), true);
                    paths.Add(path);
                } catch (ArgumentException) { } catch (FileNotFoundException) { } catch (IOException) { }
            }
        }

        public static AppEntry CreateEntry(string path)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            try {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                if (!String.IsNullOrWhiteSpace(info.ProductName)) name = info.ProductName;
                if (!String.IsNullOrWhiteSpace(info.ProductVersion)) name += "  " + info.ProductVersion;
            } catch (FileNotFoundException) { } catch (ArgumentException) { } catch (Win32Exception) { }
            if (!File.Exists(path)) name += "（文件已移动，可撤销规则）";
            return new AppEntry { Path = path, Name = name };
        }
    }

    public sealed class MainForm : Form
    {
        private readonly ComboBox applications = new RoundedComboBox();
        private readonly Label status = new Label();
        private readonly Label details = new Label();
        private readonly Button block = new RoundedButton();
        private readonly Button unblock = new RoundedButton();
        private readonly Button browse = new RoundedButton();
        private readonly Label operation = new Label();
        private readonly ToolTip hints = new ToolTip();
        private readonly Image mascotImage;
        private string path;
        private bool busy;
        private int normalClientHeight;
        private readonly bool previewMode;

        protected override bool ShowWithoutActivation { get { return previewMode; } }

        public MainForm() : this(false) { }

        public MainForm(bool preview)
        {
            previewMode = preview;
            Text = Program.DisplayName + " · v" + typeof(Program).Assembly.GetName().Version.ToString(3);
            Icon = Icon.ExtractAssociatedIcon(typeof(MainForm).Assembly.Location);
            ClientSize = new Size(760, 314);
            MinimumSize = new Size(720, 354);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Palette.Cream;
            ForeColor = Palette.Ink;
            StartPosition = FormStartPosition.CenterScreen;
            if (previewMode) {
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-30000, -30000);
                Opacity = 0;
            }

            var root = new TableLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 16),
                ColumnCount = 1, RowCount = 5
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int width in new[] { 56, 56, 56 }) header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
            header.Controls.Add(new Label {
                Text = Program.DisplayName, AutoSize = true,
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), Margin = new Padding(0)
            }, 0, 0);
            var pathLink = new LinkLabel { Text = "路径", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
            StyleLink(pathLink);
            pathLink.LinkClicked += delegate {
                if (path != null) MessageBox.Show(this, path, "程序路径", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            var refreshLink = new LinkLabel { Text = "刷新", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
            StyleLink(refreshLink);
            refreshLink.LinkClicked += delegate { if (!busy) LoadApplications(); };
            var helpLink = new LinkLabel { Text = "帮助", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
            StyleLink(helpLink);
            helpLink.LinkClicked += delegate {
                MessageBox.Show(this,
                    "选择程序，然后点击“阻止联网”。\r\n需要恢复时，点击“撤销”。\r\n\r\n" +
                    "阻止后，登录、云文档等在线功能可能不可用。\r\n规则持续有效，工具可以关闭。\r\n程序升级换路径后，请重新选择。\r\n\r\n" +
                    "本工具只撤销自己创建的规则。\r\n软件内的实际表现需要打开软件验证。",
                    "使用帮助", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            header.Controls.Add(pathLink, 1, 0);
            header.Controls.Add(refreshLink, 2, 0);
            header.Controls.Add(helpLink, 3, 0);
            root.Controls.Add(header, 0, 0);

            var picker = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            picker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            applications.DropDownStyle = ComboBoxStyle.DropDownList;
            applications.DrawMode = DrawMode.OwnerDrawFixed;
            applications.ItemHeight = 26;
            applications.DrawItem += delegate(object sender, DrawItemEventArgs args) {
                using (var background = new SolidBrush((args.State & DrawItemState.Selected) != 0 ? Palette.Peach : Color.White))
                    args.Graphics.FillRectangle(background, args.Bounds);
                if (args.Index >= 0) {
                    TextRenderer.DrawText(args.Graphics, applications.Items[args.Index].ToString(), applications.Font,
                        args.Bounds, Palette.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                args.DrawFocusRectangle();
            };
            applications.Dock = DockStyle.Fill;
            applications.Margin = new Padding(0, 6, 12, 0);
            applications.AccessibleName = "选择应用";
            applications.BackColor = Color.White;
            applications.ForeColor = Palette.Ink;
            applications.DropDownWidth = 650;
            applications.SelectedIndexChanged += delegate {
                AppEntry entry = applications.SelectedItem as AppEntry;
                path = entry == null ? null : entry.Path;
                hints.SetToolTip(applications, path ?? "选择需要管理的程序");
                operation.Text = "";
                RefreshStatus();
            };
            ConfigureButton(browse, "选择程序…", false);
            browse.Dock = DockStyle.Fill;
            browse.Margin = new Padding(0, 2, 0, 7);
            browse.Click += delegate { Browse(); };
            picker.Controls.Add(applications, 0, 0);
            picker.Controls.Add(browse, 1, 0);
            root.Controls.Add(picker, 0, 1);

            status.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Margin = new Padding(0);
            status.Text = "请选择一个程序";

            var bottom = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                Margin = new Padding(0), BackColor = Palette.Card
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var stateText = new TableLayoutPanel {
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 68,
                ColumnCount = 1, RowCount = 2, Margin = new Padding(0)
            };
            stateText.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stateText.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            stateText.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            operation.Dock = DockStyle.Fill;
            operation.ForeColor = Palette.Muted;
            operation.TextAlign = ContentAlignment.MiddleLeft;
            operation.Margin = new Padding(0);
            operation.AutoEllipsis = true;
            stateText.Controls.Add(status, 0, 0);
            stateText.Controls.Add(operation, 0, 1);
            ConfigureButton(block, "阻止联网", true);
            ConfigureButton(unblock, "撤销", false);
            unblock.EnabledChanged += delegate {
                unblock.BackColor = unblock.Enabled ? Color.FromArgb(179, 58, 58) : Color.White;
                unblock.ForeColor = unblock.Enabled ? Color.White : Palette.Ink;
                unblock.FlatAppearance.BorderSize = unblock.Enabled ? 0 : 1;
            };
            block.Anchor = unblock.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            block.Height = unblock.Height = 68;
            block.Margin = new Padding(12, 0, 8, 0);
            unblock.Margin = new Padding(0);
            block.Click += delegate { ChangeRule(true); };
            unblock.Click += delegate { ChangeRule(false); };
            using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("AppNetworkSwitch.Mascot"))
            using (Image source = Image.FromStream(stream)) mascotImage = new Bitmap(source);
            var mascot = new BottomPictureBox {
                Image = mascotImage, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill,
                Margin = new Padding(8, 0, 6, 0), TabStop = false, AccessibleName = "虎耳小伙伴"
            };
            bottom.Controls.Add(stateText, 0, 0);
            bottom.Controls.Add(mascot, 1, 0);
            bottom.Controls.Add(block, 2, 0);
            bottom.Controls.Add(unblock, 3, 0);
            var card = new RoundedPanel {
                Dock = DockStyle.Fill, BackColor = Palette.Card,
                Padding = new Padding(18, 12, 18, 0), Margin = new Padding(0, 10, 0, 0)
            };
            card.Controls.Add(bottom);
            root.Controls.Add(card, 0, 2);

            details.AutoSize = true;
            details.Dock = DockStyle.Fill;
            details.ForeColor = Palette.Orange;
            details.Margin = new Padding(0, 8, 0, 0);
            details.Visible = false;
            details.SizeChanged += delegate { ResizeForWarnings(); };
            root.Controls.Add(details, 0, 3);
            LoadApplications();
        }

        private static void ConfigureButton(Button button, string title, bool primary)
        {
            button.Text = title;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Palette.Border;
            button.BackColor = primary ? Palette.Orange : Color.White;
            button.ForeColor = primary ? Color.White : Palette.Ink;
            button.Cursor = Cursors.Hand;
            if (primary) button.EnabledChanged += delegate {
                button.BackColor = button.Enabled ? Palette.Orange : Palette.Disabled;
            };
        }

        private static void StyleLink(LinkLabel link)
        {
            link.LinkColor = Palette.Muted;
            link.ActiveLinkColor = Palette.Orange;
            link.VisitedLinkColor = Palette.Muted;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                hints.Dispose();
                if (mascotImage != null) mascotImage.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnLoad(EventArgs args)
        {
            base.OnLoad(args);
            normalClientHeight = ClientSize.Height;
            ResizeForWarnings();
        }

        private void ResizeForWarnings()
        {
            if (normalClientHeight == 0) return;
            int warningHeight = String.IsNullOrEmpty(details.Text) ? 0 :
                details.GetPreferredSize(new Size(Math.Max(1, details.Width), 0)).Height + details.Margin.Vertical;
            int requiredHeight = normalClientHeight + warningHeight;
            MinimumSize = new Size(MinimumSize.Width, requiredHeight + Height - ClientSize.Height);
            if (ClientSize.Height < requiredHeight) ClientSize = new Size(ClientSize.Width, requiredHeight);
        }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (busy) {
                args.Cancel = true;
                operation.Text = "请先完成或取消管理员授权。";
            }
            base.OnFormClosing(args);
        }

        private void LoadApplications()
        {
            string previous = path;
            try
            {
                IList<AppEntry> entries;
                using (var store = new WindowsRuleStore()) { entries = AppDiscovery.Find(store.Read()); }
                if (previous != null && !entries.Any(entry => String.Equals(entry.Path, previous, StringComparison.OrdinalIgnoreCase)))
                    entries.Add(AppDiscovery.CreateEntry(previous));
                applications.BeginUpdate();
                applications.Items.Clear();
                foreach (AppEntry entry in entries) applications.Items.Add(entry);
                applications.EndUpdate();
                int index = entries.ToList().FindIndex(entry => String.Equals(entry.Path, previous, StringComparison.OrdinalIgnoreCase));
                applications.SelectedIndex = index >= 0 ? index : entries.Count > 0 ? 0 : -1;
                if (entries.Count == 0) path = null;
                RefreshStatus();
            }
            catch (Exception error) { ShowReadError(error); }
        }

        private void Browse()
        {
            using (var dialog = new OpenFileDialog {
                Title = "选择需要管理联网的主程序", Filter = "Windows 程序 (*.exe)|*.exe",
                CheckFileExists = true, Multiselect = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string selected = RuleManager.NormalizePath(dialog.FileName, true);
                    foreach (AppEntry entry in applications.Items)
                    {
                        if (String.Equals(entry.Path, selected, StringComparison.OrdinalIgnoreCase)) {
                            applications.SelectedItem = entry;
                            return;
                        }
                    }
                    AppEntry added = AppDiscovery.CreateEntry(selected);
                    applications.Items.Add(added);
                    applications.SelectedItem = added;
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "无法选择程序", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        private void RefreshStatus()
        {
            block.Enabled = false;
            unblock.Enabled = false;
            details.Text = "";
            details.Visible = false;
            if (path == null) { status.Text = "请选择一个程序"; operation.Text = "可从列表或本机文件选择。"; return; }
            try
            {
                using (var store = new WindowsRuleStore())
                {
                    RuleRecord owned = RuleManager.FindOwned(store, path);
                    bool blocked = RuleManager.IsBlocked(owned);
                    int otherBlocks = store.Read().Count(rule => rule.Enabled && rule.Direction == 2 && rule.Action == 0 &&
                        String.Equals(rule.Program, path, StringComparison.OrdinalIgnoreCase) && !RuleManager.Owns(rule, path));
                    status.Text = blocked ? "断网规则已启用" : owned != null ? "规则已更改" : otherBlocks > 0 ? "已有其他断网规则" : "未启用断网规则";
                    if (!busy && String.IsNullOrEmpty(operation.Text)) {
                        operation.Text = blocked ? "请重新打开目标程序。" : owned != null ? "可撤销后重新设置。" :
                            otherBlocks > 0 ? "其他规则请用原工具解除。" : "点击“阻止联网”开始。";
                    }
                    status.ForeColor = Palette.Ink;
                    string health = store.GetHealth();
                    var warnings = new List<string>();
                    if (otherBlocks > 0 && owned != null) warnings.Add("仍有其他规则限制联网。");
                    if (health != "Windows 防火墙已开启") warnings.Add(health);
                    details.Text = String.Join("\r\n", warnings);
                    details.Visible = warnings.Count > 0;
                    ResizeForWarnings();
                    block.Enabled = !busy && owned == null && File.Exists(path);
                    unblock.Enabled = !busy && owned != null;
                }
            }
            catch (Exception error) { ShowReadError(error); }
        }

        private void ShowReadError(Exception error)
        {
            status.Text = "无法读取规则";
            status.ForeColor = Palette.Orange;
            details.Text = "请检查防火墙服务，或联系管理员。";
            details.Visible = true;
            ResizeForWarnings();
            hints.SetToolTip(status, error.Message);
            block.Enabled = false;
            unblock.Enabled = false;
        }

        private async void ChangeRule(bool shouldBlock)
        {
            if (busy || path == null) return;
            string target = path;
            string question = shouldBlock
                ? "将阻止以下 EXE 在所有网络类型下的全部出站连接。\r\n登录、云文档及其他在线功能可能不可用。\r\n\r\n" + target + "\r\n\r\n是否继续？"
                : "将删除本工具为以下程序创建的规则。\r\n其他规则仍可能影响联网。\r\n\r\n" + target + "\r\n\r\n是否继续？";
            if (MessageBox.Show(this, question, shouldBlock ? "阻止程序联网" : "撤销本工具规则",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            busy = true;
            applications.Enabled = browse.Enabled = false;
            RefreshStatus();
            operation.Text = "等待管理员授权…";
            try
            {
                string encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(target));
                var startInfo = new ProcessStartInfo {
                    FileName = Application.ExecutablePath, UseShellExecute = true, Verb = "runas",
                    Arguments = "--change " + (shouldBlock ? "block " : "unblock ") + encodedPath
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null) throw new InvalidOperationException("无法启动管理员操作。");
                    await System.Threading.Tasks.Task.Run(delegate { process.WaitForExit(); });
                    if (process.ExitCode != 0) throw new InvalidOperationException("管理员操作未完成，请检查状态后重试。");
                }
                operation.Text = "请重新打开目标程序。";
            }
            catch (Win32Exception error) {
                operation.Text = error.NativeErrorCode == 1223 ? "已取消。" : "启动失败，请重试。";
            }
            catch (Exception error) { operation.Text = error.Message; }
            finally {
                busy = false;
                applications.Enabled = browse.Enabled = true;
                RefreshStatus();
            }
        }
    }
}
