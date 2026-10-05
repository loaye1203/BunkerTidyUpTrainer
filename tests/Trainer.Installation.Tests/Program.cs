using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BunkerTidyUp.Trainer.Shared;
using BunkerTidyUpTrainer.Controller;

if (args.Length != 2) throw new ArgumentException("Expected release Payload path and workspace root.");
var payload = Path.GetFullPath(args[0]);
var workspace = Path.GetFullPath(args[1]);
var runRoot = Path.Combine(workspace, ".artifacts", "install-recovery-tests", Guid.NewGuid().ToString("N"));
var passed = new List<string>();
Run("clean-install", f => { f.Service.Install(f.Game); Assert(f.Service.HasInstallation(f.Game), "missing manifest"); });
Run("known-winhttp-only", f => { Copy("winhttp.dll", f.Game); f.Service.Install(f.Game); Assert(File.Exists(Plugin(f.Game)), "missing plugin"); });
Run("manual-copy-and-missing-file", f =>
{
    CopyTree(payload, f.Game); File.Delete(Plugin(f.Game));
    var settings = new ModSettings { ExpandCarry = true, MoreStars = true, Revision = 48 };
    JsonFile.WriteAtomic(f.Service.SettingsPath(f.Game), settings);
    Write(Path.Combine(f.Service.PluginDataPath(f.Game), "slots", "keep.txt"), "keep extension records");
    var before = Hash(f.Service.SettingsPath(f.Game));
    f.Service.Install(f.Game);
    Assert(Hash(f.Service.SettingsPath(f.Game)) == before, "settings reset");
    Assert(File.Exists(Plugin(f.Game)) && File.Exists(Path.Combine(f.Service.PluginDataPath(f.Game), "slots", "keep.txt")), "progress lost");
    f.Service.Install(f.Game); // A repeat check must not replace shared configuration.
    Remove(f);
    Assert(!File.Exists(Plugin(f.Game)), "retired trainer was restored on removal");
    Assert(File.Exists(Path.Combine(f.Game, "winhttp.dll")), "adopted loader removed");
});
Run("shared-loader-with-other-mod", f =>
{
    CopyTree(payload, f.Game, p => !LoaderRecoveryPlan.IsTrainerPlugin(p));
    var other = Path.Combine(f.Game, "BepInEx", "plugins", "OtherMod.dll"); Write(other, "other mod sentinel");
    var config = Path.Combine(f.Game, "doorstop_config.ini");
    var oldConfig = File.ReadAllText(config).Replace("enabled = true", "enabled = false").Replace("redirect_output_log = false", "redirect_output_log = true");
    File.WriteAllText(config, oldConfig, new UTF8Encoding(false));
    var otherHash = Hash(other);
    f.Service.Install(f.Game);
    Assert(File.ReadAllText(config).Contains("enabled = true") && File.ReadAllText(config).Contains("redirect_output_log = true"), "shared settings changed");
    var enabledHash = Hash(config); f.Service.Install(f.Game); Assert(Hash(config) == enabledHash, "repeat check reset shared config");
    Remove(f);
    Assert(Hash(other) == otherHash && File.ReadAllText(config) == oldConfig, "shared loader/mod not preserved on removal");
});
Run("lost-and-corrupt-record", f =>
{
    f.Service.Install(f.Game);
    var manifest = Path.Combine(f.Service.RootDataDirectory(f.Game), "install-manifest.json");
    File.Delete(manifest); f.Service.Install(f.Game); Assert(f.Service.HasInstallation(f.Game), "lost record not repaired");
    File.WriteAllText(manifest, "{broken"); Assert(!f.Service.HasInstallation(f.Game), "corrupt record reported installed");
    f.Service.Install(f.Game); Assert(f.Service.HasInstallation(f.Game), "corrupt record not repaired");
    File.WriteAllText(manifest, "{}"); f.Service.Install(f.Game); Assert(f.Service.HasInstallation(f.Game), "invalid record not repaired");
});
Run("trailing-separator", f => { f.Service.Install(f.Game + Path.DirectorySeparatorChar); Assert(f.Service.HasInstallation(f.Game), "path identity changed"); f.Service.Install(f.Game); });
foreach (var conflict in new[] { "unknown-winhttp", "foreign-target", "inline-entry-comment", "different-core", "melonloader", "directory-collision", "modified-trainer" })
    Run(conflict, f =>
    {
        if (conflict == "unknown-winhttp") Write(Path.Combine(f.Game, "winhttp.dll"), "unrecognized proxy");
        if (conflict == "foreign-target") { Copy("doorstop_config.ini", f.Game); var p = Path.Combine(f.Game, "doorstop_config.ini"); File.WriteAllText(p, File.ReadAllText(p).Replace("BepInEx\\core\\BepInEx.Preloader.dll", "OtherLoader.dll")); }
        if (conflict == "inline-entry-comment") { Copy("doorstop_config.ini", f.Game); var p = Path.Combine(f.Game, "doorstop_config.ini"); File.WriteAllText(p, File.ReadAllText(p).Replace("enabled = true", "enabled = true ; unsupported inline value")); }
        if (conflict == "different-core") Write(Path.Combine(f.Game, "BepInEx", "core", "BepInEx.dll"), "different version");
        if (conflict == "melonloader") Directory.CreateDirectory(Path.Combine(f.Game, "MelonLoader"));
        if (conflict == "directory-collision") Directory.CreateDirectory(Path.Combine(f.Game, "winhttp.dll"));
        if (conflict == "modified-trainer") Write(Plugin(f.Game), "modified trainer");
        var before = Snapshot(f.Game); bool rejected = false;
        try { f.Service.Install(f.Game); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && before.SequenceEqual(Snapshot(f.Game)) && !f.Service.HasInstallation(f.Game), "conflict overwrote files or recorded installation");
    });

foreach (var version in new[] { "1.0.0", "1.0.1", "1.0.2" })
{
    var historical = Path.Combine(workspace, ".artifacts", "cache", "published-v" + version + ".zip");
    if (!File.Exists(historical)) continue; // Local optional fixtures use verified public archives; CI runs the mandatory cases above.
    Run("published-" + version + "-without-record", f =>
    {
        using var zip = ZipFile.OpenRead(historical);
        foreach (var name in new[] { "BunkerTidyUp.Mod.dll", "Trainer.Shared.dll" })
        {
            var rel = "BepInEx/plugins/BunkerTidyUpTrainer/" + name;
            var entry = zip.GetEntry("Payload/" + rel)!;
            var target = Path.Combine(f.Game, rel); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open(); using var destination = File.Create(target); source.CopyTo(destination);
        }
        Copy("winhttp.dll", f.Game);
        f.Service.Install(f.Game);
        Assert(Hash(Plugin(f.Game)) == Hash(Path.Combine(payload, "BepInEx/plugins/BunkerTidyUpTrainer/BunkerTidyUp.Mod.dll")), "historical plugin not upgraded");
        Remove(f); Assert(!File.Exists(Plugin(f.Game)), "historical plugin resurrected");
    });
}
File.WriteAllLines(Path.Combine(runRoot, "report.txt"), passed, new UTF8Encoding(false));
Console.WriteLine(string.Join(Environment.NewLine, passed));
Console.WriteLine("Report: " + Path.Combine(runRoot, "report.txt"));

void Run(string name, Action<Fixture> test)
{
    var root = Path.Combine(runRoot, name); var game = Path.Combine(root, "Game");
    Write(Path.Combine(game, "Bunker.exe"), "fake executable for installer-only tests");
    Write(Path.Combine(game, "Bunker_Data", "Managed", "Bunker.dll"), "fake assembly for installer-only tests");
    var saves = Path.Combine(root, "Saves"); Directory.CreateDirectory(saves);
    var f = new Fixture(game, new TrainerControllerService(saves, Path.Combine(root, "LocalData"), payload));
    test(f); passed.Add("PASS " + name);
}
void Copy(string relative, string game) { var target = Path.Combine(game, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(payload, relative), target); }
static void CopyTree(string source, string target, Func<string, bool>? predicate = null)
{
    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var rel = Path.GetRelativePath(source, file); if (predicate != null && !predicate(rel)) continue; var dst = Path.Combine(target, rel); Directory.CreateDirectory(Path.GetDirectoryName(dst)!); File.Copy(file, dst); }
}
static string Plugin(string root) => Path.Combine(root, "BepInEx", "plugins", "BunkerTidyUpTrainer", "BunkerTidyUp.Mod.dll");
static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
static string[] Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p).Select(p => Path.GetRelativePath(root, p) + ":" + Hash(p)).ToArray();
static void Assert(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
static void Remove(Fixture f)
{
    var settings = f.Service.RequestRestore(f.Game);
    JsonFile.WriteAtomic(f.Service.StatusPath(f.Game), new PluginStatus { Slot = 0, RestoreSaved = true, RestoreSlot = 0, RestoreRevision = settings.Revision, HandsCount = 0, HandsCapacity = 75 });
    f.Service.RemoveAfterRestore(f.Game);
}
record Fixture(string Game, TrainerControllerService Service);
