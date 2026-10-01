using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BunkerTidyUp.Trainer.Shared;

namespace BunkerTidyUpTrainer.Controller
{
    internal sealed class InstallManifest
    {
        public string GameRoot { get; set; } = "";
        public string BackupRoot { get; set; } = "";
        public string InstalledUtc { get; set; } = "";
        public bool InstallComplete { get; set; }
        public List<string> Files { get; set; } = new List<string>();
        public List<string> OriginalFiles { get; set; } = new List<string>();
        public Dictionary<string, string> InstalledHashes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class TrainerControllerService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private readonly string? _saveRootOverride;
        private readonly string? _dataRootOverride;

        public TrainerControllerService() { }

        internal TrainerControllerService(string? saveRootOverride, string? dataRootOverride)
        {
            _saveRootOverride = string.IsNullOrWhiteSpace(saveRootOverride) ? null : Path.GetFullPath(saveRootOverride);
            _dataRootOverride = string.IsNullOrWhiteSpace(dataRootOverride) ? null : Path.GetFullPath(dataRootOverride);
        }

        public string RootDataDirectory(string gameRoot) => Path.Combine(
            _dataRootOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BunkerTidyUpTrainer", Identity(gameRoot));

        public string SettingsPath(string gameRoot) => Path.Combine(gameRoot, "BepInEx", "config", "BunkerTidyUpTrainer", "settings.json");
        public string StatusPath(string gameRoot) => Path.Combine(gameRoot, "BepInEx", "config", "BunkerTidyUpTrainer", "status.json");
        public string PluginDataPath(string gameRoot) => Path.Combine(gameRoot, "BepInEx", "config", "BunkerTidyUpTrainer");

        public ModSettings ReadSettings(string gameRoot) => JsonFile.Read<ModSettings>(SettingsPath(gameRoot)) ?? new ModSettings();
        public PluginStatus? ReadStatus(string gameRoot) => JsonFile.Read<PluginStatus>(StatusPath(gameRoot));

        public bool HasInstallation(string gameRoot)
        {
            var manifestPath = ManifestPath(gameRoot);
            if (!File.Exists(manifestPath)) return false;
            var manifest = ReadManifest(manifestPath);
            return manifest.InstallComplete && string.Equals(Path.GetFullPath(manifest.GameRoot), Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase);
        }

        public string Install(string gameRoot)
        {
            gameRoot = ValidateGameRoot(gameRoot);
            EnsureGameStopped(gameRoot);
            var manifestPath = ManifestPath(gameRoot);
            if (File.Exists(manifestPath))
            {
                var existing = ReadManifest(manifestPath);
                if (!string.Equals(Path.GetFullPath(existing.GameRoot), gameRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("现有安装清单指向另一个游戏目录；本次未修改文件。");
                if (existing.InstallComplete)
                {
                    return RepairCompletedInstall(gameRoot, existing);
                }
                RollbackInstall(gameRoot, existing);
                File.Delete(manifestPath);
            }

            RefuseExistingLoader(gameRoot);

            var payload = Path.Combine(AppContext.BaseDirectory, "Payload");
            var payloadFiles = Directory.Exists(payload) ? Directory.GetFiles(payload, "*", SearchOption.AllDirectories) : Array.Empty<string>();
            if (!payloadFiles.Any(path => Path.GetFileName(path).Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase)) ||
                !payloadFiles.Any(path => path.EndsWith(Path.Combine("plugins", "BunkerTidyUpTrainer", "BunkerTidyUp.Mod.dll"), StringComparison.OrdinalIgnoreCase)) ||
                !payloadFiles.Any(path => path.EndsWith(Path.Combine("plugins", "BunkerTidyUpTrainer", "Trainer.Shared.dll"), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("便携包缺少 BepInEx 启动文件或修改器插件文件；请重新解压完整发行包。");

            var backupRoot = Path.Combine(RootDataDirectory(gameRoot), "backups", "install-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupRoot);
            BackupUserSaves(gameRoot, Path.Combine(backupRoot, "player-saves"));

            var manifest = new InstallManifest { GameRoot = gameRoot, BackupRoot = backupRoot, InstalledUtc = DateTime.UtcNow.ToString("O") };
            WriteManifest(manifestPath, manifest);
            try
            {
                var payloadRoot = Path.GetFullPath(payload);
                foreach (var source in payloadFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var relative = Path.GetRelativePath(payloadRoot, source);
                    var target = SafeGamePath(gameRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (File.Exists(target))
                    {
                        var original = Path.Combine(backupRoot, "game-files", relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                        File.Copy(target, original, true);
                        if (!manifest.OriginalFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) manifest.OriginalFiles.Add(relative);
                    }
                    if (!manifest.Files.Contains(relative, StringComparer.OrdinalIgnoreCase)) manifest.Files.Add(relative);
                    WriteManifest(manifestPath, manifest);
                    File.Copy(source, target, true);
                    manifest.InstalledHashes[relative] = FileHash(target);
                    WriteManifest(manifestPath, manifest);
                }

                var savedPluginData = Path.Combine(RootDataDirectory(gameRoot), "plugin-data");
                var targetPluginData = PluginDataPath(gameRoot);
                if (Directory.Exists(savedPluginData) && !Directory.Exists(targetPluginData)) CopyDirectory(savedPluginData, targetPluginData);
                var settingsPath = SettingsPath(gameRoot);
                if (!File.Exists(settingsPath)) JsonFile.WriteAtomic(settingsPath, new ModSettings());
                manifest.InstallComplete = true;
                WriteManifest(manifestPath, manifest);
            }
            catch (Exception installError)
            {
                try
                {
                    RollbackInstall(gameRoot, manifest);
                    if (File.Exists(manifestPath)) File.Delete(manifestPath);
                    throw new IOException("安装失败，已回滚本次改动：" + installError.Message, installError);
                }
                catch (Exception rollbackError) when (!(rollbackError is IOException io && ReferenceEquals(io.InnerException, installError)))
                {
                    throw new IOException("安装失败且自动回滚未能完成。清单和逐文件备份已保留在 " + backupRoot + "，请勿手动删除。回滚错误：" + rollbackError.Message, installError);
                }
            }
            return "安装完成。游戏本地存档与被覆盖的启动文件已备份；所有功能初始关闭。";
        }

        public void SaveSettings(string gameRoot, ModSettings settings, bool preserveRestoreRequest = false)
        {
            var current = ReadSettings(gameRoot);
            var status = ReadStatus(gameRoot);
            settings.Schema = 1;
            if (!preserveRestoreRequest) settings.RestoreRequested = false;
            settings.Revision = Math.Max(current.Revision, status?.SeenRevision ?? 0) + 1;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath(gameRoot))!);
            JsonFile.WriteAtomic(SettingsPath(gameRoot), settings);
        }

        public ModSettings RequestRestore(string gameRoot)
        {
            var settings = ReadSettings(gameRoot);
            settings.ExpandCarry = false;
            settings.ExpandAutoPickup = false;
            settings.ExpandAutoPlace = false;
            settings.ExpandContinuousPlace = false;
            settings.MoreStars = false;
            settings.HoldToDrop = false;
            settings.InfiniteStars = false;
            settings.NoCooldown = false;
            settings.MaxAll = false;
            settings.RestoreRequested = true;
            SaveSettings(gameRoot, settings, preserveRestoreRequest: true);
            return ReadSettings(gameRoot);
        }

        public string RemoveAfterRestore(string gameRoot)
        {
            gameRoot = ValidateGameRoot(gameRoot);
            EnsureGameStopped(gameRoot);
            var settings = ReadSettings(gameRoot);
            var status = ReadStatus(gameRoot);
            if (!settings.RestoreRequested || status == null || !status.RestoreSaved ||
                status.RestoreRevision != settings.Revision || status.RestoreSlot != status.Slot)
                throw new InvalidOperationException("游戏尚未以当前恢复请求成功保存。请启动游戏、载入目标存档并完成一次保存，然后退出游戏再重试。");
            if (status.HandsCount < 0 || status.HandsCapacity < 0)
                throw new InvalidOperationException("恢复回执没有手持容量校验。请载入存档并重新保存，确认物品状态后再移除。");
            if (status.HandsCount > status.HandsCapacity)
                throw new InvalidOperationException("当前保存含 " + status.HandsCount + " 件手持物品，超过原版容量 " + status.HandsCapacity + "。请先整理至原版容量内，再保存并重试。");

            var manifestPath = ManifestPath(gameRoot);
            if (!File.Exists(manifestPath)) throw new InvalidOperationException("未找到此游戏目录的安装清单，不能安全恢复启动文件。");
            var manifest = ReadManifest(manifestPath);
            if (!manifest.InstallComplete || !string.Equals(Path.GetFullPath(manifest.GameRoot), gameRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("安装清单指向另一个游戏目录；为保护文件，本次未恢复。");

            ValidateAllKnownSlotInventories(gameRoot);

            var backupRoot = Path.Combine(RootDataDirectory(gameRoot), "backups", "remove-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(backupRoot);
            BackupUserSaves(gameRoot, Path.Combine(backupRoot, "player-saves"));
            var pluginData = PluginDataPath(gameRoot);
            if (Directory.Exists(pluginData)) CopyDirectory(pluginData, Path.Combine(RootDataDirectory(gameRoot), "plugin-data"));

            RestoreManifestFiles(gameRoot, manifest);

            if (Directory.Exists(pluginData)) Directory.Delete(pluginData, true);
            File.Delete(manifestPath);
            return "已完成恢复并移除本工具安装的文件。玩家存档未被修改；安装与恢复前的存档副本保存在本机备份目录。";
        }

        public string DefaultGamePath()
        {
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BunkerTidyUpTrainer", "recent-path.txt");
            try
            {
                if (File.Exists(local))
                {
                    var recent = File.ReadAllText(local, Encoding.UTF8).Trim();
                    if (File.Exists(Path.Combine(recent, "Bunker.exe"))) return recent;
                }
            }
            catch { }

            return string.Empty;
        }

        public void RememberGamePath(string path)
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BunkerTidyUpTrainer", "recent-path.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Path.GetFullPath(path), new UTF8Encoding(false));
        }

        public string BackupDirectory(string gameRoot) => Path.Combine(RootDataDirectory(gameRoot), "backups");

        public string GetGameProcessState(string gameRoot)
        {
            var expected = Path.GetFullPath(Path.Combine(gameRoot, "Bunker.exe"));
            var inaccessible = false;
            foreach (var process in Process.GetProcessesByName("Bunker"))
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(executable)) inaccessible = true;
                    else if (string.Equals(Path.GetFullPath(executable), expected, StringComparison.OrdinalIgnoreCase)) return "running";
                }
                catch { inaccessible = true; }
                finally { process.Dispose(); }
            }
            return inaccessible ? "unknown" : "stopped";
        }

        private static string ValidateGameRoot(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot)) throw new InvalidOperationException("请先选择游戏安装目录。");
            gameRoot = Path.GetFullPath(gameRoot.Trim());
            if (!File.Exists(Path.Combine(gameRoot, "Bunker.exe")) ||
                !File.Exists(Path.Combine(gameRoot, "Bunker_Data", "Managed", "Bunker.dll")))
                throw new InvalidOperationException("此目录没有找到 Bunker.exe 和 Bunker_Data\\Managed\\Bunker.dll。请选中游戏根目录。");
            return gameRoot;
        }

        private static string SafeGamePath(string gameRoot, string relative)
        {
            var root = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("便携包包含越界路径：" + relative);
            return target;
        }

        private static void RefuseExistingLoader(string gameRoot)
        {
            var markers = new[]
            {
                Path.Combine(gameRoot, "MelonLoader"),
                Path.Combine(gameRoot, "winhttp.dll"),
                Path.Combine(gameRoot, "doorstop_config.ini"),
                Path.Combine(gameRoot, "version.dll")
            };
            var existing = markers.FirstOrDefault(path => Directory.Exists(path) || File.Exists(path));
            var bepinexCore = Path.Combine(gameRoot, "BepInEx", "core");
            if (existing == null && Directory.Exists(bepinexCore) &&
                (File.Exists(Path.Combine(bepinexCore, "BepInEx.Preloader.dll")) || File.Exists(Path.Combine(bepinexCore, "BepInEx.dll"))))
                existing = bepinexCore;
            if (existing != null)
                throw new InvalidOperationException("检测到已有 mod 加载器或启动代理（" + Path.GetFileName(existing) + "）。为避免覆盖其他模组或启动配置，本工具不会替换它；游戏文件未修改。");
        }

        private static void RollbackInstall(string gameRoot, InstallManifest manifest)
        {
            foreach (var relative in manifest.Files.AsEnumerable().Reverse())
            {
                var target = SafeGamePath(gameRoot, relative);
                if (manifest.OriginalFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    var original = Path.Combine(manifest.BackupRoot, "game-files", relative);
                    if (!File.Exists(original)) throw new IOException("找不到安装前备份文件：" + original);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(original, target, true);
                }
                else if (File.Exists(target)) File.Delete(target);
            }
        }

        private static void RestoreManifestFiles(string gameRoot, InstallManifest manifest)
        {
            foreach (var relative in manifest.Files)
            {
                var target = SafeGamePath(gameRoot, relative);
                var hasOriginal = manifest.OriginalFiles.Contains(relative, StringComparer.OrdinalIgnoreCase);
                var original = Path.Combine(manifest.BackupRoot, "game-files", relative);
                var expected = manifest.InstalledHashes.TryGetValue(relative, out var hash) ? hash : "";
                if (hasOriginal && !File.Exists(original)) throw new IOException("找不到必须恢复的原文件备份：" + original);
                if (!File.Exists(target)) continue;
                var current = FileHash(target);
                if (current.Equals(expected, StringComparison.OrdinalIgnoreCase)) continue;
                if (hasOriginal && current.Equals(FileHash(original), StringComparison.OrdinalIgnoreCase)) continue;
                throw new IOException("目标文件在安装后发生改变，已停止恢复以免覆盖它：" + target);
            }

            foreach (var relative in manifest.Files.AsEnumerable().Reverse())
            {
                var target = SafeGamePath(gameRoot, relative);
                var hasOriginal = manifest.OriginalFiles.Contains(relative, StringComparer.OrdinalIgnoreCase);
                if (hasOriginal)
                {
                    var original = Path.Combine(manifest.BackupRoot, "game-files", relative);
                    if (!File.Exists(target) || !FileHash(target).Equals(FileHash(original), StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(original, target, true);
                    }
                }
                else if (File.Exists(target))
                {
                    var expected = manifest.InstalledHashes.TryGetValue(relative, out var hash) ? hash : "";
                    if (!FileHash(target).Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("目标文件已改变，拒绝删除：" + target);
                    File.Delete(target);
                }
            }
        }

        private static bool ManifestFilesIntact(string gameRoot, InstallManifest manifest)
        {
            foreach (var relative in manifest.Files)
            {
                var target = SafeGamePath(gameRoot, relative);
                if (!File.Exists(target) || !manifest.InstalledHashes.TryGetValue(relative, out var expected) ||
                    !FileHash(target).Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private string RepairCompletedInstall(string gameRoot, InstallManifest manifest)
        {
            var payloadRoot = Path.Combine(AppContext.BaseDirectory, "Payload");
            var updates = new List<(string Relative, string Source, string Target, string SourceHash, bool WasMissing)>();
            foreach (var relative in manifest.Files)
            {
                var target = SafeGamePath(gameRoot, relative);
                if (!manifest.InstalledHashes.TryGetValue(relative, out var expected) || string.IsNullOrWhiteSpace(expected))
                    throw new InvalidDataException("安装清单缺少文件哈希，不能安全修复。");
                var source = SafeGamePath(payloadRoot, relative);
                if (!File.Exists(source)) throw new InvalidDataException("便携包缺少安装文件，无法检查或修复：" + relative);
                var sourceHash = FileHash(source);
                if (File.Exists(target))
                {
                    if (!FileHash(target).Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("检测到安装文件已被修改；为保护改动，本次未覆盖任何文件：" + target);
                    if (sourceHash.Equals(expected, StringComparison.OrdinalIgnoreCase)) continue;
                    updates.Add((relative, source, target, sourceHash, false));
                }
                else
                {
                    updates.Add((relative, source, target, sourceHash, true));
                }
            }

            if (updates.Count == 0) return "安装文件检查通过；本次没有重复写入游戏文件。";

            var manifestPath = ManifestPath(gameRoot);
            var backupRoot = Path.Combine(BackupDirectory(gameRoot), "update-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupRoot);
            var previousHashes = new Dictionary<string, string>(manifest.InstalledHashes, StringComparer.OrdinalIgnoreCase);
            var written = new List<(string Relative, string Target, bool WasMissing)>();
            try
            {
                foreach (var file in updates)
                {
                    if (!file.WasMissing)
                    {
                        var backup = SafeGamePath(backupRoot, file.Relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(file.Target, backup, false);
                    }
                    written.Add((file.Relative, file.Target, file.WasMissing));
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                    File.Copy(file.Source, file.Target, !file.WasMissing);
                    if (!FileHash(file.Target).Equals(file.SourceHash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("修复后文件校验失败：" + file.Relative);
                    manifest.InstalledHashes[file.Relative] = file.SourceHash;
                    WriteManifest(manifestPath, manifest);
                }
            }
            catch (Exception updateError)
            {
                try
                {
                    foreach (var file in written.AsEnumerable().Reverse())
                    {
                        var backup = SafeGamePath(backupRoot, file.Relative);
                        if (file.WasMissing)
                        {
                            if (File.Exists(file.Target)) File.Delete(file.Target);
                        }
                        else if (File.Exists(backup)) File.Copy(backup, file.Target, true);
                    }
                    manifest.InstalledHashes = previousHashes;
                    WriteManifest(manifestPath, manifest);
                }
                catch (Exception rollbackError)
                {
                    throw new IOException("更新失败且自动回滚未能完成。新旧文件及清单仍保留在 " + backupRoot + "。", new AggregateException(updateError, rollbackError));
                }
                throw new IOException("已安全回滚工具更新：" + updateError.Message, updateError);
            }

            var missingCount = updates.Count(file => file.WasMissing);
            var upgradeCount = updates.Count - missingCount;
            return "检查完成：补回 " + missingCount + " 个缺失文件，更新 " + upgradeCount + " 个本工具文件；现有用户改动未覆盖。";
        }

        private static string FileHash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void EnsureGameStopped(string gameRoot)
        {
            foreach (var process in Process.GetProcessesByName("Bunker"))
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(executable) || string.Equals(Path.GetFullPath(executable), Path.Combine(gameRoot, "Bunker.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("检测到 Bunker 正在运行。请自行退出游戏后再安装或完成恢复；控制器不会关闭游戏。");
                }
                catch (InvalidOperationException) { throw; }
                catch { throw new InvalidOperationException("检测到无法确认路径的 Bunker 进程。为避免改动运行中的游戏，本次操作已取消。"); }
                finally { process.Dispose(); }
            }
        }

        private void BackupUserSaves(string gameRoot, string destination)
        {
            var saves = PlayerSaveDirectory();
            if (Directory.Exists(saves)) CopyDirectory(saves, destination);
        }

        private string PlayerSaveDirectory()
        {
            if (_saveRootOverride != null) return _saveRootOverride;
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(profile, "AppData", "LocalLow", "Tovarishch Games", "Bunker", "Saves");
        }

        private void ValidateAllKnownSlotInventories(string gameRoot)
        {
            var slotsDirectory = Path.Combine(PluginDataPath(gameRoot), "slots");
            if (!Directory.Exists(slotsDirectory)) return;

            var saveRoot = PlayerSaveDirectory();
            foreach (var sidecarPath in Directory.GetFiles(slotsDirectory, "Save-*.sav.extension.json", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(sidecarPath);
                const string prefix = "Save-";
                const string suffix = ".sav.extension.json";
                if (!fileName.StartsWith(prefix, StringComparison.Ordinal) || !fileName.EndsWith(suffix, StringComparison.Ordinal))
                    throw new InvalidOperationException("发现无法识别的存档扩展记录；为避免丢失超容量物品，本次保留插件安装。");
                var slotText = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
                if (!int.TryParse(slotText, NumberStyles.None, CultureInfo.InvariantCulture, out var slot) || slot < 0 ||
                    !string.Equals(fileName, "Save-" + slot.ToString(CultureInfo.InvariantCulture) + suffix, StringComparison.Ordinal))
                    throw new InvalidOperationException("发现无法识别的存档扩展记录 " + fileName + "；为避免丢失超容量物品，本次保留插件安装。");

                var savePath = Path.Combine(saveRoot, "Save-" + slot.ToString(CultureInfo.InvariantCulture) + ".sav");
                if (!File.Exists(savePath)) continue; // A deleted save slot no longer needs an inventory check.

                SlotProgress? progress;
                try { progress = JsonFile.Read<SlotProgress>(sidecarPath); }
                catch (Exception ex) { throw new InvalidOperationException("存档槽 " + slot + " 的扩展记录损坏，无法核对手持物品；请载入该档并重新保存后再卸载。", ex); }
                if (progress == null || progress.Schema != 1 || progress.Slot != slot ||
                    !string.Equals(progress.SaveFileName, "Save-" + slot.ToString(CultureInfo.InvariantCulture) + ".sav", StringComparison.Ordinal))
                    throw new InvalidOperationException("存档槽 " + slot + " 的扩展记录无效，无法核对手持物品；请载入该档并重新保存后再卸载。");

                string currentFingerprint;
                try { currentFingerprint = FileHash(savePath); }
                catch (Exception ex) { throw new InvalidOperationException("无法读取存档槽 " + slot + " 的存档指纹；请确认存档可访问后再卸载。", ex); }
                if (string.IsNullOrWhiteSpace(progress.SaveFingerprint) || !progress.SaveFingerprint.Equals(currentFingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("存档槽 " + slot + " 的扩展记录与当前存档指纹不匹配；请载入该档并重新保存后再卸载。");
                if (!progress.HandInventoryMetadataPresent || progress.HandsCount < 0 || progress.VanillaHandsCapacity <= 0)
                    throw new InvalidOperationException("存档槽 " + slot + " 没有可验证的原版手持容量记录；请载入该档并重新保存后再卸载。");
                if (progress.HandsCount > progress.VanillaHandsCapacity)
                    throw new InvalidOperationException("存档槽 " + slot + " 有 " + progress.HandsCount + " 件手持物品，超过原版容量 " + progress.VanillaHandsCapacity + "；请整理并重新保存该档后再卸载。");
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }

        private string ManifestPath(string gameRoot) => Path.Combine(RootDataDirectory(gameRoot), "install-manifest.json");
        private static string Identity(string gameRoot)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(gameRoot).ToUpperInvariant()));
            return BitConverter.ToString(bytes, 0, 8).Replace("-", "").ToLowerInvariant();
        }

        private static InstallManifest ReadManifest(string path) => JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path, Encoding.UTF8)) ?? throw new InvalidDataException("安装清单无法读取。");

        private static void WriteManifest(string path, InstallManifest manifest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".previous", true);
            else File.Move(temporary, path);
        }
    }
}
