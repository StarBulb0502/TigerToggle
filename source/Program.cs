using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle(AppNetworkSwitch.Program.DisplayName)]
[assembly: System.Reflection.AssemblyProduct(AppNetworkSwitch.Program.DisplayName)]
[assembly: System.Reflection.AssemblyVersion("1.0.8.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.8.0")]

namespace AppNetworkSwitch
{
    internal static class Program
    {
        internal const string DisplayName = "虎爪开关 TigerToggle";

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 3 && args[0] == "--change") return Change(args[1], args[2]);
                if (args.Length == 2 && args[0] == "--preview")
                {
                    using (var form = new MainForm(true))
                    {
                        form.Show();
                        Application.DoEvents();
                        using (var bitmap = new Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.GetFullPath(args[1]), ImageFormat.Png);
                        }
                    }
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--diagnose")
                {
                    using (var store = new WindowsRuleStore())
                    using (var output = new StreamWriter(Path.GetFullPath(args[1]), false, Encoding.UTF8))
                    {
                        output.WriteLine(store.GetHealth());
                        foreach (AppEntry entry in AppDiscovery.Find(store.Read()))
                            output.WriteLine(entry.Name + " | " + entry.Path);
                    }
                    return 0;
                }
                if (args.Length != 0) throw new ArgumentException("启动参数不正确，请直接双击程序。");
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception error)
            {
                if (args.Length > 0 && (args[0] == "--preview" || args[0] == "--diagnose"))
                    Console.Error.WriteLine(error.ToString());
                else
                    MessageBox.Show(error.Message, DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }

        private static int Change(string action, string encodedPath)
        {
            if (action != "block" && action != "unblock") throw new ArgumentException("操作参数不正确。");
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("修改规则需要管理员权限。");
            string path = RuleManager.NormalizePath(Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath)), action == "block");
            using (var mutex = new Mutex(false, @"Global\" + RuleManager.GetRuleName(path)))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new InvalidOperationException("同一程序的另一项操作仍在进行，请稍后重试。");
                    using (var store = new WindowsRuleStore()) {
                        if (action == "block") RuleManager.Block(store, path);
                        else RuleManager.Unblock(store, path);
                    }
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
            return 0;
        }
    }
}
