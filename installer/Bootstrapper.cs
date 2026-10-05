using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("Bunker Tidy Up Trainer Setup")]
[assembly: AssemblyProduct("Bunker Tidy Up Trainer")]
[assembly: AssemblyVersion("1.0.3.0")]
[assembly: AssemblyFileVersion("1.0.3.0")]
[assembly: AssemblyInformationalVersion("1.0.3")]

namespace BunkerTidyUpTrainerSetup
{
    internal static class Bootstrapper
    {
        private const string ProductName = "Bunker Tidy Up Trainer";
        private const string InstallMarker = ".bunker-tidy-up-trainer-install";
        private const string Version = "1.0.3";
        private static readonly string[] UpgradeableVersions = { "1.0.0", "1.0.1", "1.0.2", "1.0.3" };
        private const string PackageResource = "PortablePackage.zip";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && string.Equals(args[0], "/self-test", StringComparison.OrdinalIgnoreCase))
                {
                    if (args.Length < 2) throw new InvalidOperationException("/self-test 需要测试目标目录。");
                    InstallPackage(Path.GetFullPath(args[1]), false, true);
                    return 0;
                }
                if (args.Length > 0 && string.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    UninstallDesktopTool();
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallerForm());
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static string DefaultInstallRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BunkerTidyUpTrainer");
        }

        private static void InstallPackage(string targetRoot, bool createShortcuts, bool selfTest)
        {
            targetRoot = Path.GetFullPath(targetRoot);
            var localPrograms = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            if (!selfTest && !targetRoot.StartsWith(localPrograms.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("安装目录必须位于当前 Windows 用户的 LocalAppData\\Programs 下。");

            if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
            {
                var marker = Path.Combine(targetRoot, InstallMarker);
                var installedVersion = File.Exists(marker) ? File.ReadAllText(marker, Encoding.UTF8).Trim() : string.Empty;
                if (!UpgradeableVersions.Contains(installedVersion, StringComparer.Ordinal))
                    throw new InvalidOperationException("目标目录已有非本安装器管理的文件，为避免覆盖其他内容，安装已停止：" + targetRoot);
            }

            var stagingRoot = Path.Combine(Path.GetTempPath(), "BunkerTidyUpTrainer-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            try
            {
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PackageResource))
                {
                    if (resource == null) throw new InvalidDataException("安装包缺少便携文件资源。");
                    using (var archive = new ZipArchive(resource, ZipArchiveMode.Read, false))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.FullName)) continue;
                            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                            var destination = Path.GetFullPath(Path.Combine(stagingRoot, relative));
                            var stagingPrefix = Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                            if (!destination.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("安装包内含越界路径，已中止安装。");
                            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                            {
                                Directory.CreateDirectory(destination);
                                continue;
                            }
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            using (var input = entry.Open())
                            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                                input.CopyTo(output);
                        }
                    }
                }

                VerifyPackage(stagingRoot);
                Directory.CreateDirectory(targetRoot);
                CopyDirectory(stagingRoot, targetRoot);
                File.WriteAllText(Path.Combine(targetRoot, InstallMarker), Version + Environment.NewLine, new UTF8Encoding(false));
                var installerPath = Assembly.GetExecutingAssembly().Location;
                File.Copy(installerPath, Path.Combine(targetRoot, "Uninstall.exe"), true);
                if (createShortcuts) CreateShortcuts(targetRoot);
                if (selfTest)
                {
                    var testReport = "PASS\r\nVersion=1.0.3\r\nController=" + FileVersion(Path.Combine(targetRoot, "BunkerTidyUpTrainer.exe")) +
                        "\r\nPlugin=" + FileVersion(Path.Combine(targetRoot, "Payload", "BepInEx", "plugins", "BunkerTidyUpTrainer", "BunkerTidyUp.Mod.dll")) +
                        "\r\nPayload=BepInEx 5.4.23.5\r\n";
                    File.WriteAllText(Path.Combine(targetRoot, "installer-self-test.txt"), testReport, new UTF8Encoding(false));
                }
            }
            finally
            {
                try { Directory.Delete(stagingRoot, true); } catch { }
            }
        }

        private static void VerifyPackage(string root)
        {
            var controller = Path.Combine(root, "BunkerTidyUpTrainer.exe");
            var plugin = Path.Combine(root, "Payload", "BepInEx", "plugins", "BunkerTidyUpTrainer", "BunkerTidyUp.Mod.dll");
            var shared = Path.Combine(root, "Payload", "BepInEx", "plugins", "BunkerTidyUpTrainer", "Trainer.Shared.dll");
            var proxy = Path.Combine(root, "Payload", "winhttp.dll");
            if (!File.Exists(controller) || !File.Exists(plugin) || !File.Exists(shared) || !File.Exists(proxy))
                throw new InvalidDataException("便携包缺少控制器、插件或 BepInEx 启动文件。");
            if (!FileVersion(controller).Contains(Version))
                throw new InvalidDataException("控制器文件版本不是 1.0.3。");
            if (!FileVersion(plugin).Contains(Version))
                throw new InvalidDataException("插件文件版本不是 1.0.3。");
            if (!File.Exists(Path.Combine(root, "LICENSE")) || !File.Exists(Path.Combine(root, "THIRD-PARTY-NOTICES.md")) ||
                !File.Exists(Path.Combine(root, "验证报告.md")))
                throw new InvalidDataException("便携包缺少许可证或验证说明。");
        }

        private static string FileVersion(string path)
        {
            return FileVersionInfo.GetVersionInfo(path).ProductVersion ?? string.Empty;
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(target, directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar)));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, true);
            }
        }

        private static void CreateShortcuts(string installRoot)
        {
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName);
            Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, ProductName + ".lnk"), Path.Combine(installRoot, "BunkerTidyUpTrainer.exe"), installRoot);
            CreateShortcut(Path.Combine(startMenu, "卸载桌面工具.lnk"), Path.Combine(installRoot, "Uninstall.exe"), installRoot, "/uninstall");
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string arguments = "")
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                var type = shortcut.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                type.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments ?? string.Empty });
                type.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { ProductName });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void UninstallDesktopTool()
        {
            var expectedRoot = Path.GetFullPath(DefaultInstallRoot()).TrimEnd(Path.DirectorySeparatorChar);
            var executable = Process.GetCurrentProcess().MainModule.FileName;
            var currentRoot = Path.GetFullPath(Path.GetDirectoryName(executable)).TrimEnd(Path.DirectorySeparatorChar);
            if (!currentRoot.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(currentRoot, InstallMarker)))
                throw new InvalidOperationException("卸载程序不在本工具的用户级安装目录中，已停止。");

            var answer = MessageBox.Show(
                "这只会移除当前用户目录中的桌面控制器文件，不会恢复或删除游戏插件。\r\n\r\n若游戏中仍安装本工具，请先在控制器中完成“恢复原版并移除”。确认继续卸载桌面工具？",
                ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName);
            try { if (Directory.Exists(startMenu)) Directory.Delete(startMenu, true); } catch { }
            var cleanupScript = Path.Combine(Path.GetTempPath(), "BunkerTidyUpTrainer-uninstall-" + Guid.NewGuid().ToString("N") + ".ps1");
            var escapedRoot = currentRoot.Replace("'", "''");
            var script = "$target = '" + escapedRoot + "'\r\nStart-Sleep -Seconds 2\r\nRemove-Item -LiteralPath $target -Recurse -Force\r\nRemove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force\r\n";
            File.WriteAllText(cleanupScript, script, new UTF8Encoding(false));
            var command = "-NoProfile -ExecutionPolicy Bypass -File \"" + cleanupScript + "\"";
            Process.Start(new ProcessStartInfo("powershell.exe", command) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }

        private sealed class InstallerForm : Form
        {
            private readonly string _targetRoot = DefaultInstallRoot();

            internal InstallerForm()
            {
                Text = ProductName + " 安装程序";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new System.Drawing.Size(530, 300);

                var title = new Label { Text = "安装 Bunker Tidy Up 游戏增强工具", AutoSize = false, Location = new System.Drawing.Point(24, 22), Size = new System.Drawing.Size(482, 36), Font = new System.Drawing.Font("Microsoft YaHei UI", 15F, System.Drawing.FontStyle.Bold) };
                var directory = new Label { Text = "当前用户安装目录：\r\n" + _targetRoot, AutoSize = false, Location = new System.Drawing.Point(26, 76), Size = new System.Drawing.Size(478, 50), Font = new System.Drawing.Font("Microsoft YaHei UI", 9F) };
                var information = new Label { Text = "安装程序只安装桌面控制器，不会自动改动游戏文件。安装后请在控制器中选择游戏目录，并在游戏退出时安装插件。\r\n\r\n首次安装的所有功能均关闭。", AutoSize = false, Location = new System.Drawing.Point(26, 137), Size = new System.Drawing.Size(478, 68), Font = new System.Drawing.Font("Microsoft YaHei UI", 9F) };
                var install = new Button { Text = "安装并启动控制器", Location = new System.Drawing.Point(284, 238), Size = new System.Drawing.Size(220, 38), DialogResult = DialogResult.None };
                var cancel = new Button { Text = "取消", Location = new System.Drawing.Point(174, 238), Size = new System.Drawing.Size(96, 38), DialogResult = DialogResult.Cancel };
                var uninstallHint = new Label { Text = "卸载桌面工具不会移除游戏插件；需先在控制器中完整恢复。", AutoSize = false, Location = new System.Drawing.Point(26, 210), Size = new System.Drawing.Size(478, 21), ForeColor = System.Drawing.Color.FromArgb(120, 70, 42), Font = new System.Drawing.Font("Microsoft YaHei UI", 8.5F) };
                Controls.Add(title);
                Controls.Add(directory);
                Controls.Add(information);
                Controls.Add(uninstallHint);
                Controls.Add(install);
                Controls.Add(cancel);
                AcceptButton = install;
                CancelButton = cancel;
                install.Click += InstallClick;
            }

            private void InstallClick(object sender, EventArgs e)
            {
                try
                {
                    InstallPackage(_targetRoot, true, false);
                    var result = MessageBox.Show("桌面工具已安装到当前用户目录。现在启动控制器并选择游戏根目录？", ProductName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (result == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo(Path.Combine(_targetRoot, "BunkerTidyUpTrainer.exe")) { UseShellExecute = true, WorkingDirectory = _targetRoot });
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
