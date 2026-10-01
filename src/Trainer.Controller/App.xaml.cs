using System;
using System.IO;
using System.Text;
using System.Windows;

namespace BunkerTidyUpTrainer.Controller
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (TryInstallCommand(e.Args)) return;
            new MainWindow().Show();
        }

        private bool TryInstallCommand(string[] args)
        {
            var rootIndex = Array.FindIndex(args, value => string.Equals(value, "--install-root", StringComparison.OrdinalIgnoreCase));
            var reportIndex = Array.FindIndex(args, value => string.Equals(value, "--report", StringComparison.OrdinalIgnoreCase));
            if (rootIndex < 0 || rootIndex + 1 >= args.Length || reportIndex < 0 || reportIndex + 1 >= args.Length) return false;
            var reportPath = Path.GetFullPath(args[reportIndex + 1]);
            try
            {
                var result = new TrainerControllerService().Install(args[rootIndex + 1]);
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, result, new UTF8Encoding(false));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, ex.ToString(), new UTF8Encoding(false));
                Shutdown(1);
            }
            return true;
        }
    }
}
