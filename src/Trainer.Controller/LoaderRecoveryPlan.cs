using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BunkerTidyUpTrainer.Controller
{
    // A matching loader is shared infrastructure, not proof that we installed it.
    // Keep its originals so removal of this trainer never removes another mod's loader.
    internal sealed class LoaderRecoveryPlan
    {
        internal bool HasExistingFiles { get; private set; }
        internal byte[]? DoorstopConfiguration { get; private set; }

        private static readonly HashSet<string> PublishedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "14B26D28F6B7B7065DE927B3DF22504D4A29B89335EE6E71CD0A88ED20FE9F19",
            "272B234FF2D96CC0932D8A153E3788C2D5F2799138F0F98EBACB7FC7FED0339A",
            "1D86942131EE3F71D6B199B5A3AC62C17C631B14840697022EB1A0D5A204956E",
            "FEFF9AFB6BA9462034EDA7E821102FD88570E7C2C00D91F54FC5B5B19BD3483D",
            "B5888DCA2645B9CB1383E7FEEAD5C0AC3A7C28ADB72E75C65217F95919E8F12B",
            "4621D4E1EF1157BDB2B24DB9D84AFB10783C0FDA9D7BE12D580E260590BCEA67",
            "AA5CD0F250D306D5CA1876478DE8220D9F964B3629C1D4667B93E0A8490B544E",
            "12A2FBE3CCA8A73853DC9BE892924A2A13B7AAE07B72237E727780077F96C071",
            "B85CDAE6BFB656076FF9FFBA2AC815991B6608EF9276E368393B890EF916AA12",
            "6F46C15C6FF2F2A32193BFD619BFB4FCBADF1D38B6E798192DCAD7BBDD3CECF8",
            "122FF1F3108544C2385CFD518260DF51F0A5DE90810E7ECB15BB200B0917B455",
            "E69E8ECE3469A9235F47B369EC55ACF5031DAC3E070B85C626FC19A0F3BADD05"
        };

        internal static bool IsTrainerPlugin(string relative) => relative.Replace('\\', '/').StartsWith(
            "BepInEx/plugins/BunkerTidyUpTrainer/", StringComparison.OrdinalIgnoreCase);

        internal static LoaderRecoveryPlan Inspect(string gameRoot, string payloadRoot, string[] payloadFiles)
        {
            var plan = new LoaderRecoveryPlan();
            foreach (var marker in new[] { "MelonLoader", "version.dll", "dobby.dll" })
                if (File.Exists(Path.Combine(gameRoot, marker)) || Directory.Exists(Path.Combine(gameRoot, marker)))
                    throw Conflict(marker);

            foreach (var source in payloadFiles)
            {
                var relative = Path.GetRelativePath(payloadRoot, source);
                var target = Path.Combine(gameRoot, relative);
                if (Directory.Exists(target)) throw Conflict(relative);
                if (!File.Exists(target)) continue;
                plan.HasExistingFiles = true;
                var currentHash = Hash(target);
                if (IsTrainerPlugin(relative))
                {
                    if (currentHash != Hash(source) && !PublishedPlugins.Contains(currentHash)) throw Conflict(relative);
                }
                else if (relative.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase))
                {
                    plan.DoorstopConfiguration = InspectConfiguration(gameRoot, target);
                }
                else if (currentHash != Hash(source))
                {
                    throw Conflict(relative);
                }
            }
            return plan;
        }

        private static byte[]? InspectConfiguration(string gameRoot, string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var lines = text.Split('\n');
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var section = "";
            var enabledLine = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                if (line[0] == '[' && line.EndsWith("]", StringComparison.Ordinal)) { section = line[1..^1]; continue; }
                var equals = line.IndexOf('=');
                if (equals <= 0) throw Conflict("doorstop_config.ini");
                var key = section + "/" + line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (!values.TryAdd(key, value)) throw Conflict("doorstop_config.ini");
                if (key.Equals("General/enabled", StringComparison.OrdinalIgnoreCase)) enabledLine = i;
            }
            if (!values.TryGetValue("General/target_assembly", out var assembly) ||
                !values.TryGetValue("General/enabled", out var enabled) || !bool.TryParse(enabled, out var isEnabled))
                throw Conflict("doorstop_config.ini");
            string target;
            try { target = Path.GetFullPath(Path.Combine(gameRoot, assembly.Replace('/', Path.DirectorySeparatorChar))); }
            catch (ArgumentException) { throw Conflict("doorstop_config.ini"); }
            var expected = Path.GetFullPath(Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.Preloader.dll"));
            if (!target.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                Nonempty("General/boot_config_override") || Nonempty("UnityMono/dll_search_path_override") ||
                (values.TryGetValue("UnityMono/debug_suspend", out var suspend) && !suspend.Equals("false", StringComparison.OrdinalIgnoreCase)))
                throw Conflict("doorstop_config.ini");
            if (isEnabled) return null; // Preserve the exact bytes of an already enabled shared configuration.
            lines[enabledLine] = "enabled = true" + (lines[enabledLine].EndsWith("\r", StringComparison.Ordinal) ? "\r" : "");
            return new UTF8Encoding(false).GetBytes(string.Join("\n", lines));

            bool Nonempty(string key) => values.TryGetValue(key, out var value) && value.Length != 0;
        }

        private static InvalidOperationException Conflict(string file) => new InvalidOperationException(
            "检测到当前版本无法确认兼容的启动组件或已修改文件（" + file + "）。已保留现有文件，本次未覆盖；无需重装游戏。请使用支持该加载器的版本，或恢复原加载器配置后重试。");

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}
