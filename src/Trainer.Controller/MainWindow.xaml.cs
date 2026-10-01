using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BunkerTidyUp.Trainer.Shared;
using Microsoft.Win32;

namespace BunkerTidyUpTrainer.Controller
{
    public partial class MainWindow : Window
    {
        private readonly TrainerControllerService _service = new TrainerControllerService();
        private readonly DispatcherTimer _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private ModSettings _settings = new ModSettings();
        private bool _loadingControls;

        public MainWindow()
        {
            InitializeComponent();
            GamePathBox.Text = _service.DefaultGamePath();
            _statusTimer.Tick += (_, _) => RefreshStatus();
            _statusTimer.Start();
            LoadSettings();
            RefreshStatus();
            Loaded += (_, _) =>
            {
                var arguments = Environment.GetCommandLineArgs();
                var option = Array.FindIndex(arguments, value => string.Equals(value, "--render-preview", StringComparison.OrdinalIgnoreCase));
                if (option >= 0 && option + 1 < arguments.Length)
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => RenderPreview(arguments[option + 1])));
            };
        }

        private void RenderPreview(string path)
        {
            try
            {
                CarryCheck.IsEnabled = PullCheck.IsEnabled = SortCheck.IsEnabled = RepeatCheck.IsEnabled = StarsCheck.IsEnabled = DropHoldCheck.IsEnabled = true;
                CooldownCheck.IsEnabled = MaxAllCheck.IsEnabled = InfiniteButton.IsEnabled = true;
                TopStatusText.Text = "游戏未运行";
                StatusDot.Fill = Brushes.Gray;
                PluginStateText.Text = "未检测到游戏进程；启动游戏后会在此显示插件回执。";
                UpdateLayout();
                var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
                var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(this);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (var stream = File.Create(path)) encoder.Save(stream);
                InstallStatusText.Text = "界面预览已导出。";
                Close();
                Application.Current.Shutdown(0);
            }
            catch (Exception ex)
            {
                InstallStatusText.Text = "界面预览导出失败：" + ex.Message;
                Application.Current.Shutdown(1);
            }
        }

        private string GameRoot => string.IsNullOrWhiteSpace(GamePathBox.Text) ? string.Empty : Path.GetFullPath(GamePathBox.Text.Trim());
        private bool IsInstalled => SafeHasInstallation();

        private void LoadSettings()
        {
            _loadingControls = true;
            try
            {
                _settings = Directory.Exists(GameRoot) ? _service.ReadSettings(GameRoot) : new ModSettings();
                CarryCheck.IsChecked = _settings.ExpandCarry;
                PullCheck.IsChecked = _settings.ExpandAutoPickup;
                SortCheck.IsChecked = _settings.ExpandAutoPlace;
                RepeatCheck.IsChecked = _settings.ExpandContinuousPlace;
                StarsCheck.IsChecked = _settings.MoreStars;
                DropHoldCheck.IsChecked = _settings.HoldToDrop;
                CooldownCheck.IsChecked = _settings.NoCooldown;
                MaxAllCheck.IsChecked = _settings.MaxAll;
                UpdateInfiniteButton();
            }
            catch (Exception ex)
            {
                _settings = new ModSettings();
                InstallStatusText.Text = "设置无法读取：" + ex.Message;
            }
            finally
            {
                _loadingControls = false;
                UpdateEnabledState();
            }
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (_loadingControls || !IsInstalled) return;
            _settings.ExpandCarry = CarryCheck.IsChecked == true;
            _settings.ExpandAutoPickup = PullCheck.IsChecked == true;
            _settings.ExpandAutoPlace = SortCheck.IsChecked == true;
            _settings.ExpandContinuousPlace = RepeatCheck.IsChecked == true;
            _settings.MoreStars = StarsCheck.IsChecked == true;
            _settings.HoldToDrop = DropHoldCheck.IsChecked == true;
            _settings.NoCooldown = CooldownCheck.IsChecked == true;
            _settings.MaxAll = MaxAllCheck.IsChecked == true;
            SaveSettingsAndRefresh();
        }

        private void InfiniteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsInstalled) return;
            _settings.InfiniteStars = !_settings.InfiniteStars;
            SaveSettingsAndRefresh();
            UpdateInfiniteButton();
        }

        private void SaveSettingsAndRefresh()
        {
            try
            {
                _service.SaveSettings(GameRoot, _settings);
                _settings = _service.ReadSettings(GameRoot);
                InstallStatusText.Text = "设置已保存，游戏插件正在读取版本 " + _settings.Revision + "。";
                UpdateInfiniteButton();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                InstallStatusText.Text = "设置写入失败：" + ex.Message;
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "选择 Bunker Tidy Up 游戏根目录", InitialDirectory = Directory.Exists(GamePathBox.Text) ? GamePathBox.Text : null };
            if (dialog.ShowDialog(this) == true) GamePathBox.Text = dialog.FolderName;
        }

        private void GamePathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (!string.IsNullOrWhiteSpace(GameRoot))
            {
                try { _service.RememberGamePath(GameRoot); } catch { }
            }
            LoadSettings();
            RefreshStatus();
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = _service.Install(GameRoot);
                InstallStatusText.Text = result;
                _service.RememberGamePath(GameRoot);
                LoadSettings();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                InstallStatusText.Text = ex.Message;
                RefreshStatus();
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshStatus();

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsInstalled) return;
            var answer = MessageBox.Show(this,
                "这会关闭全部修改效果。请随后启动游戏、载入要恢复的存档并完成一次游戏内保存；退出游戏后，再点击“完成恢复并移除”。\n\n现有购买记录会保留，玩家存档不会被控制器直接改写。现在请求恢复吗？",
                "请求完整恢复", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            try
            {
                _settings = _service.RequestRestore(GameRoot);
                LoadSettings();
                InstallStatusText.Text = "恢复请求已提交。等待游戏以原版等级成功保存；状态会显示当前槽位和回执版本。";
                RefreshStatus();
            }
            catch (Exception ex) { InstallStatusText.Text = ex.Message; }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(this,
                "只有在当前恢复版本已经保存成功且游戏已退出时才会继续。系统会先备份本地存档与插件数据，然后按安装清单恢复原有文件。继续吗？",
                "完成恢复并移除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            try
            {
                InstallStatusText.Text = _service.RemoveAfterRestore(GameRoot);
                LoadSettings();
                RefreshStatus();
            }
            catch (Exception ex) { InstallStatusText.Text = ex.Message; }
        }

        private void RefreshStatus()
        {
            try
            {
                var hasRoot = Directory.Exists(GameRoot);
                var processState = hasRoot ? _service.GetGameProcessState(GameRoot) : "stopped";
                var status = hasRoot ? _service.ReadStatus(GameRoot) : null;
                if (status == null)
                {
                    TopStatusText.Text = IsInstalled ? processState == "running" ? "等待插件回执" : "游戏未运行" : "未安装";
                    StatusDot.Fill = IsInstalled ? Brushes.DarkGoldenrod : Brushes.Gray;
                    PluginStateText.Text = IsInstalled
                        ? processState == "running" ? "游戏正在运行；等待插件写入真实状态回执。" : "未检测到游戏进程，启动并载入存档后会显示插件回执。"
                        : "未找到本工具的安装记录。";
                    PluginDetailsText.Text = "";
                }
                else
                {
                    var restored = status.RestoreSaved && status.RestoreRevision == _settings.Revision && status.RestoreSlot == status.Slot;
                    var age = DateTime.UtcNow - status.UpdatedUtc.ToUniversalTime();
                    var fresh = age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(5);
                    var good = status.State == "active" || status.State == "loaded" || status.State == "restored";
                    var revisionPending = status.ActiveRevision < _settings.Revision || status.SeenRevision < _settings.Revision;
                    if (processState == "unknown")
                    {
                        TopStatusText.Text = "无法确认游戏进程";
                        StatusDot.Fill = Brushes.DarkGoldenrod;
                        PluginStateText.Text = "系统无法读取 Bunker 进程路径，因此不会报告插件在线。";
                    }
                    else if (processState != "running")
                    {
                        TopStatusText.Text = restored ? "已保存原版状态" : "游戏未运行";
                        StatusDot.Fill = restored ? Brushes.DarkOrange : Brushes.Gray;
                        PluginStateText.Text = "游戏未运行；以下为最后一次插件回执：" + status.Message;
                    }
                    else if (!fresh)
                    {
                        TopStatusText.Text = "插件回执已过期";
                        StatusDot.Fill = Brushes.DarkGoldenrod;
                        PluginStateText.Text = "游戏正在运行，但状态文件超过 5 秒未更新；暂不视为已连接。";
                    }
                    else if (revisionPending)
                    {
                        TopStatusText.Text = "等待设置生效";
                        StatusDot.Fill = Brushes.DarkGoldenrod;
                        PluginStateText.Text = "控制器设置版本较新，等待插件在游戏主线程应用。";
                    }
                    else
                    {
                        TopStatusText.Text = restored ? "已保存原版状态" : good ? "插件已连接" : status.State;
                        StatusDot.Fill = restored ? Brushes.DarkOrange : good ? Brushes.SeaGreen : Brushes.DarkGoldenrod;
                        PluginStateText.Text = status.Message;
                    }
                    var handStatus = status.HandsCount >= 0 && status.HandsCapacity >= 0 ? $"   ·   手持 {status.HandsCount}/{status.HandsCapacity}" : "";
                    PluginDetailsText.Text = $"Unity {status.GameVersion}   ·   槽位 {status.Slot}{handStatus}   ·   回执 {age.TotalSeconds:F0} 秒前   ·   已应用版本 {status.ActiveRevision} / 控制器版本 {_settings.Revision}";
                }
            }
            catch (Exception ex)
            {
                TopStatusText.Text = "状态读取失败";
                StatusDot.Fill = Brushes.IndianRed;
                PluginStateText.Text = ex.Message;
            }
            UpdateEnabledState();
        }

        private void UpdateEnabledState()
        {
            var installed = SafeHasInstallation();
            CarryCheck.IsEnabled = installed;
            PullCheck.IsEnabled = installed;
            SortCheck.IsEnabled = installed;
            RepeatCheck.IsEnabled = installed;
            StarsCheck.IsEnabled = installed;
            DropHoldCheck.IsEnabled = installed;
            CooldownCheck.IsEnabled = installed;
            MaxAllCheck.IsEnabled = installed;
            InfiniteButton.IsEnabled = installed;
            var processState = "unknown";
            try { processState = Directory.Exists(GameRoot) ? _service.GetGameProcessState(GameRoot) : "stopped"; }
            catch { }
            InstallButton.Content = processState != "stopped" ? "请先退出游戏" : installed ? "检查 / 修复" : "安装 / 检查";
            InstallButton.IsEnabled = processState == "stopped";
            RestoreButton.IsEnabled = installed;
            try
            {
                var status = installed ? _service.ReadStatus(GameRoot) : null;
                var gameStopped = installed && _service.GetGameProcessState(GameRoot) == "stopped";
                RemoveButton.IsEnabled = gameStopped && status != null && status.RestoreSaved &&
                    status.RestoreRevision == _settings.Revision && status.RestoreSlot == status.Slot &&
                    status.HandsCount >= 0 && status.HandsCapacity >= 0 && status.HandsCount <= status.HandsCapacity;
            }
            catch { RemoveButton.IsEnabled = false; }
        }

        private bool SafeHasInstallation()
        {
            try { return Directory.Exists(GameRoot) && _service.HasInstallation(GameRoot); }
            catch { return false; }
        }

        private void UpdateInfiniteButton()
        {
            var enabled = _settings.InfiniteStars;
            InfiniteButton.Content = enabled ? "关闭无限星星" : "开启无限星星";
            InfiniteButton.Background = enabled ? new SolidColorBrush(Color.FromRgb(129, 82, 48)) : (Brush)FindResource("OrangeBrush");
            InfiniteText.Text = enabled ? "已开启：消费仍显示和保留真实余额。" : "消费仍显示和保留真实余额。";
        }
    }
}
