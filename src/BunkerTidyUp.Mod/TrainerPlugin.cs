using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Concurrent;
using BepInEx;
using BepInEx.Logging;
using BunkerTidyUp.Trainer.Shared;
using HarmonyLib;
using UnityEngine;

namespace BunkerTidyUp.Mod
{
    [BepInPlugin("com.codex.bunkertidyuptool", "Bunker Tidy Up Trainer", "1.0.0")]
    public sealed class TrainerPlugin : BaseUnityPlugin
    {
        internal static TrainerPlugin Instance = null!;
        internal static ManualLogSource Log = null!;
        internal static string DataDirectory = "";
        internal static string SettingsPath = "";
        internal static string StatusPath = "";
        internal static ModSettings Settings = new ModSettings();
        internal static PluginStatus Status = new PluginStatus();
        internal static string GameVersion = "";
        internal static readonly ConcurrentQueue<Action> MainThreadActions = new ConcurrentQueue<Action>();
        internal static long AppliedRevision = -1;
        internal static float NextConfigRead;
        internal static float NextStatusWrite;
        internal static bool IsApplyingLevels;
        internal static bool IsRangeBatch;
        internal static bool Ready;
        internal static bool TestMode;

        private Harmony? _harmony;
        private static DateTime _invalidSettingsWriteUtc = DateTime.MinValue;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            TestMode = string.Equals(Environment.GetEnvironmentVariable("BUNKER_TIDY_UP_TEST_MODE"), "1", StringComparison.Ordinal);
            GameVersion = Application.unityVersion;
            DataDirectory = Path.Combine(Paths.ConfigPath, "BunkerTidyUpTrainer");
            SettingsPath = Path.Combine(DataDirectory, "settings.json");
            StatusPath = Path.Combine(DataDirectory, "status.json");
            Directory.CreateDirectory(Path.Combine(DataDirectory, "slots"));
            if (TestMode && string.Equals(Environment.GetEnvironmentVariable("BUNKER_TIDY_UP_TEST_PROGRESS"), "1", StringComparison.Ordinal))
            {
                try
                {
                    RuntimeBridge.RunProgressCorruptionFallbackSelfTest();
                    Log.LogInfo("Extension sidecar corruption recovery self-test passed.");
                }
                catch (Exception ex)
                {
                    Log.LogError("Extension sidecar corruption recovery self-test failed: " + ex);
                }
            }
            PluginStatus? previousStatus = null;
            try { previousStatus = JsonFile.Read<PluginStatus>(StatusPath); }
            catch { }
            Status = new PluginStatus
            {
                State = "loaded",
                Message = "插件已加载，正在检查游戏接口",
                PluginVersion = "1.0.0",
                GameVersion = GameVersion,
                Slot = RuntimeBridge.GetSaveSlot(),
                RestoreSaved = previousStatus?.RestoreSaved ?? false,
                RestoreRevision = previousStatus?.RestoreRevision ?? -1,
                RestoreSlot = previousStatus?.RestoreSlot ?? -1
            };
            string settingsError = "";
            try
            {
                Settings = JsonFile.Read<ModSettings>(SettingsPath) ?? new ModSettings();
                if (Settings.Schema != 1) throw new InvalidDataException("设置版本不受支持");
            }
            catch (Exception ex)
            {
                Settings = new ModSettings();
                settingsError = "设置文件损坏或版本不受支持，已按关闭全部功能启动：" + ex.GetType().Name;
                _invalidSettingsWriteUtc = File.Exists(SettingsPath) ? File.GetLastWriteTimeUtc(SettingsPath) : DateTime.UtcNow;
                Log.LogError(settingsError);
            }

            try
            {
                RuntimeBridge.ConfigureAutomatedGameTest();
                _harmony = new Harmony("com.codex.bunkertidyuptool.runtime");
                RuntimePatches.Install(_harmony);
                Ready = true;
                if (TestMode) RuntimeBridge.RedirectTestSaveRoot();
                RuntimeBridge.InstallExistingControllers();
                ApplySettings(Settings, true);
                if (settingsError.Length != 0)
                {
                    Status.State = "configError";
                    Status.Message = settingsError;
                }
                WriteStatus();
                Log.LogInfo("Bunker Tidy Up Trainer loaded. Settings revision " + Settings.Revision);
            }
            catch (Exception ex)
            {
                Ready = false;
                try { _harmony?.UnpatchSelf(); } catch { }
                Status.State = "incompatible";
                Status.Message = "插件初始化失败：" + ex.GetType().Name + "；功能保持关闭";
                Settings = new ModSettings();
                Log.LogError(ex);
                WriteStatus();
            }
        }

        private void Update()
        {
            try
            {
                if (!Ready)
                {
                    if (Time.unscaledTime >= NextStatusWrite)
                    {
                        NextStatusWrite = Time.unscaledTime + 1.0f;
                        WriteStatus();
                    }
                    return;
                }
                if (Time.unscaledTime >= NextConfigRead)
                {
                    NextConfigRead = Time.unscaledTime + 0.5f;
                    ReadSettings();
                    RuntimeBridge.ApplyLevels();
                }

                RuntimeBridge.ProcessHeldInputs();
                RuntimeBridge.ProcessMainThreadActions();
                RuntimeBridge.UpdateHandsStatus();
                RuntimeBridge.ProcessAutomatedGameTest();

                if (Time.unscaledTime >= NextStatusWrite)
                {
                    NextStatusWrite = Time.unscaledTime + 1.0f;
                    Status.Slot = RuntimeBridge.GetSaveSlot();
                    WriteStatus();
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning("主线程状态更新失败：" + ex.Message);
            }
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); }
            catch (Exception ex) { Log.LogWarning("卸载补丁失败：" + ex.Message); }
            Status.State = "stopped";
            Status.Message = "插件已停止";
            WriteStatus();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) RuntimeBridge.OnApplicationFocusLost();
        }

        private static void ReadSettings()
        {
            if (!File.Exists(SettingsPath)) return;
            try
            {
                var modifiedUtc = File.GetLastWriteTimeUtc(SettingsPath);
                if (modifiedUtc == _invalidSettingsWriteUtc) return;
                var latest = JsonFile.Read<ModSettings>(SettingsPath);
                if (latest == null || latest.Schema != 1) throw new InvalidDataException("设置文件结构或版本无效");
                _invalidSettingsWriteUtc = DateTime.MinValue;
                if (latest.Revision == AppliedRevision) return;
                Settings = latest;
                ApplySettings(latest, false);
            }
            catch (Exception ex)
            {
                Settings = new ModSettings();
                _invalidSettingsWriteUtc = File.Exists(SettingsPath) ? File.GetLastWriteTimeUtc(SettingsPath) : DateTime.UtcNow;
                RuntimeBridge.ApplyLevels();
                RuntimeBridge.ApplyRewardValue();
                Status.State = "configError";
                Status.Message = "设置文件损坏或无法读取，插件功能已关闭：" + ex.GetType().Name;
                Log.LogError(ex);
                WriteStatus();
            }
        }

        internal static void ApplySettings(ModSettings latest, bool initial)
        {
            Settings = latest;
            AppliedRevision = latest.Revision;
            RuntimeBridge.ApplyLevels();
            RuntimeBridge.ApplyRewardValue();
            Status.ActiveRevision = AppliedRevision;
            Status.SeenRevision = latest.Revision;
            Status.ActiveFeatures = RuntimeBridge.GetActiveFeatures(latest);
            if (latest.RestoreRequested)
            {
                var restoreConfirmed = Status.RestoreSaved && Status.RestoreRevision == latest.Revision && Status.RestoreSlot == RuntimeBridge.GetSaveSlot();
                Status.State = restoreConfirmed ? "restored" : "restorePending";
                Status.Message = restoreConfirmed
                    ? "原版等级已保存；可以恢复或移除本工具文件"
                    : "正在使用原版等级；等待游戏保存成功后完成恢复";
            }
            else
            {
                Status.RestoreSaved = false;
                Status.RestoreRevision = -1;
                Status.RestoreSlot = -1;
                Status.State = initial ? "loaded" : "active";
                Status.Message = initial ? "插件已加载，功能按已保存设置运行" : "设置已在游戏主线程生效";
            }
            WriteStatus();
        }

        internal static void WriteStatus()
        {
            try
            {
                Status.UpdatedUtc = DateTime.UtcNow;
                Status.GameVersion = GameVersion;
                Status.PluginVersion = "1.0.0";
                JsonFile.WriteAtomic(StatusPath, Status);
            }
            catch (Exception ex)
            {
                Log?.LogWarning("状态回执写入失败：" + ex.Message);
            }
        }
    }
}
