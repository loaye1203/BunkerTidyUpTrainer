using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BunkerTidyUp.Trainer.Shared;
using HarmonyLib;

namespace BunkerTidyUp.Mod
{
    internal static class RuntimePatches
    {
        private static Harmony? _harmony;
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static readonly Dictionary<string, int> TargetCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        internal static void Install(Harmony harmony)
        {
            _harmony = harmony;
            var game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Bunker");
            if (game == null) throw new InvalidOperationException("未找到 Bunker.dll，无法加载玩法补丁");
            var types = SafeGetTypes(game);
            try
            {
                foreach (var type in types)
                {
                    var typeName = type.Name.ToLowerInvariant();
                    foreach (var method in SafeMethods(type))
                    {
                        var name = method.Name.ToLowerInvariant();
                        if ((typeName == "abilitycontroller" || typeName == "upgradecontroller") && name == "awake")
                        {
                            Patch(method, postfix: nameof(ControllerAwakePostfix));
                            Count("controllerAwake");
                        }
                        if ((typeName == "ability" || typeName == "upgrade") && name == "setlevel")
                        {
                            Patch(method, prefix: nameof(SetLevelPrefix));
                            Count("setLevel");
                        }
                        if ((typeName == "ability" || typeName == "upgrade") && name == "get_maxlevel" && method.ReturnType == typeof(int))
                        {
                            Patch(method, prefix: nameof(MaxLevelPrefix));
                            Count("maxLevel");
                        }
                        if (typeName == "ability" && name == "trylevelup" && method.ReturnType == typeof(bool))
                        {
                            Patch(method, postfix: nameof(PurchasePostfix));
                            Count("purchase");
                        }
                        if (typeName == "upgrade" && name == "trybuy" && method.ReturnType == typeof(bool))
                        {
                            Patch(method, postfix: nameof(PurchasePostfix));
                            Count("purchase");
                        }
                        if ((typeName == "abilitycontroller" || typeName == "upgradecontroller") && name == "getsavedata" && method.ReturnType != typeof(void))
                        {
                            Patch(method, postfix: nameof(GetSaveDataPostfix));
                            Count("getSaveData");
                        }
                        if (name == "loadfromsavedata" && (type.Namespace == "Bunker" || (type.Namespace != null && type.Namespace.StartsWith("Bunker.", StringComparison.Ordinal))))
                        {
                            Patch(method, prefix: nameof(LoadFromSaveDataPrefix), postfix: nameof(LoadFromSaveDataPostfix));
                            Count("loadSaveData");
                        }
                        if (typeName == "savesystem" && name == "save" && typeof(Task).IsAssignableFrom(method.ReturnType))
                        {
                            Patch(method, prefix: nameof(SavePrefix), postfix: nameof(SavePostfix));
                            Count("save");
                        }
                        if (typeName == "savesystem" && name == "trydelete" && method.ReturnType == typeof(bool))
                        {
                            Patch(method, postfix: nameof(TryDeletePostfix));
                            Count("deleteSlot");
                        }
                        if (TrainerPlugin.TestMode && typeName == "savesystem" && name == "initialize" && method.GetParameters().Length == 0)
                        {
                            Patch(method, postfix: nameof(TestSaveSystemInitializePostfix));
                            Count("testSaveRoot");
                        }
                        if (TrainerPlugin.TestMode && typeName == "audioplayer" && type.Namespace == "Bunker.Audio" && name == "savestate" && method.ReturnType == typeof(void))
                        {
                            Patch(method, prefix: nameof(BlockTestOnlyMethod));
                            Count("testAudioPrefs");
                        }
                        if (typeName == "level" && name == "initialize")
                        {
                            Patch(method, prefix: nameof(LevelInitializePrefix));
                            Count("levelInitialize");
                        }
                        if (typeName.Contains("shelfscoring") && name == "awake")
                        {
                            Patch(method, postfix: nameof(ShelfScoringAwakePostfix));
                            Count("reward");
                        }
                        if (typeName.Contains("shelfscoring") && name == "onshelfchanged")
                        {
                            Patch(method, prefix: nameof(ShelfScoringPrefix), postfix: nameof(ShelfScoringPostfix));
                            Count("rewardAward");
                        }
                        if (typeName == "playerwallet" && name == "get_isallfree" && method.ReturnType == typeof(bool))
                        {
                            Patch(method, prefix: nameof(AllFreePrefix));
                            Count("walletFree");
                        }
                        if (typeName == "playerwallet" && name == "canspend" && method.ReturnType == typeof(bool))
                        {
                            Patch(method, prefix: nameof(CanSpendPrefix));
                            Count("walletCanSpend");
                        }
                        if (typeName == "playerwallet" && name == "spend")
                        {
                            if (method.ReturnType == typeof(bool)) Patch(method, prefix: nameof(SpendBoolPrefix));
                            else if (method.ReturnType == typeof(void)) Patch(method, prefix: nameof(SpendVoidPrefix));
                            Count("walletSpend");
                        }
                        if (name == "tryinteract" && typeName == "pickableitem")
                        {
                            Patch(method, prefix: nameof(TryInteractPrefix), postfix: nameof(TryInteractPostfix));
                            Count("rangePickup");
                        }
                        if (typeName == "playerinteractor" && name == "ondropkeypressed" && method.GetParameters().Length == 0)
                        {
                            Patch(method, postfix: nameof(DropKeyPressedPostfix));
                            Count("dropInput");
                        }
                        if (TrainerPlugin.TestMode && RuntimeBridge.IsInputEventTestPhase && typeName == "inputprovider" &&
                            (name == "oninteractactionstarted" || name == "oninteractactionperformed" || name == "oninteractactioncanceled"))
                        {
                            Patch(method, postfix: nameof(InputActionCallbackPostfix));
                            Count("testInputCallback");
                        }
                        if (TrainerPlugin.TestMode && RuntimeBridge.IsInputEventTestPhase &&
                            type.FullName == "Bunker.Interaction.Behaviours.DefaultInteractionBehaviour" && name == "oninteractrequested")
                        {
                            Patch(method, prefix: nameof(InteractionRequestPrefix));
                            Count("testInputRequest");
                        }
                        if (typeName == "hands" && name == "tryadopt" && method.ReturnType == typeof(bool))
                        {
                            Patch(method, prefix: nameof(TryAdoptPrefix), finalizer: nameof(TryAdoptFinalizer));
                            Count("handAdopt");
                        }
                        if (typeName == "ability" && name == "tick")
                        {
                            Patch(method, postfix: nameof(AbilityTickPostfix));
                            Count("abilityTick");
                        }
                        if (typeName == "ability" && name == "get_isready" && method.ReturnType == typeof(bool))
                        {
                            Patch(method, prefix: nameof(AbilityReadyPrefix));
                            Count("abilityReady");
                        }
                    }
                }

                if (TrainerPlugin.TestMode)
                {
                    var manager = types.FirstOrDefault(t => t.Namespace == "Bunker.Achievements" && t.Name == "AchievementsManager");
                    var tracker = types.FirstOrDefault(t => t.Namespace == "Bunker.Achievements" && t.Name == "AchievementsTracker");
                    if (manager == null || tracker == null) throw new InvalidOperationException("测试隔离模式未找到游戏成就接口");
                    var guarded = 0;
                    foreach (var method in SafeMethods(manager))
                    {
                        var name = method.Name.ToLowerInvariant();
                        if (name == "unlock" || name == "grantpending" || name == "savepending" || name == "addpending")
                        {
                            Patch(method, prefix: nameof(BlockTestOnlyMethod));
                            guarded++;
                        }
                        else if (name == "trysetachievement" && method.ReturnType == typeof(bool))
                        {
                            Patch(method, prefix: nameof(BlockTestOnlyResult));
                            guarded++;
                        }
                    }
                    var trackerUnlock = SafeMethods(tracker).FirstOrDefault(m => m.Name == "Unlock");
                    if (trackerUnlock == null) throw new InvalidOperationException("测试隔离模式未找到成就计数入口");
                    Patch(trackerUnlock, prefix: nameof(BlockTestOnlyMethod));
                    guarded++;
                    if (guarded < 6) throw new InvalidOperationException("测试隔离模式成就保护接口不完整");
                    TrainerPlugin.Log.LogWarning("Test mode is active: achievement unlocks and pending PlayerPrefs writes are blocked.");
                }

                var required = new[] { "controllerAwake", "setLevel", "maxLevel", "purchase", "getSaveData", "loadSaveData", "save", "deleteSlot", "levelInitialize", "rangePickup", "dropInput", "handAdopt", "abilityTick", "abilityReady", "reward", "rewardAward", "walletFree", "walletCanSpend", "walletSpend" };
                var missing = required.Where(key => !TargetCounts.TryGetValue(key, out var count) || count == 0).ToArray();
                if (missing.Length != 0) throw new InvalidOperationException("缺少必需游戏接口：" + string.Join(", ", missing));
                if (TargetCounts["controllerAwake"] < 2 || TargetCounts["getSaveData"] < 2 || TargetCounts["loadSaveData"] < 2)
                    throw new InvalidOperationException("技能/升级存档控制器接口不完整");
                if (TrainerPlugin.TestMode && (!TargetCounts.TryGetValue("testSaveRoot", out var saveInitCount) || saveInitCount == 0))
                    throw new InvalidOperationException("测试隔离模式没有找到 SaveSystem.Initialize");
                if (TrainerPlugin.TestMode && (!TargetCounts.TryGetValue("testAudioPrefs", out var audioSaveCount) || audioSaveCount == 0))
                    throw new InvalidOperationException("测试隔离模式未阻止音频设置写入 PlayerPrefs");
                if (RuntimeBridge.IsInputEventTestPhase && (!TargetCounts.TryGetValue("testInputCallback", out var inputCallbackCount) || inputCallbackCount < 3 ||
                    !TargetCounts.TryGetValue("testInputRequest", out var inputRequestCount) || inputRequestCount == 0))
                    throw new InvalidOperationException("测试隔离模式未完整观测 InteractAction started/performed/canceled 与游戏交互请求路径");
                if (TrainerPlugin.TestMode) TrainerPlugin.Log.LogInfo("Test mode is blocking the game's audio PlayerPrefs writer for the isolated identity.");
            }
            catch
            {
                harmony.UnpatchSelf();
                Patched.Clear();
                TargetCounts.Clear();
                throw;
            }

            TrainerPlugin.Status.Message = "插件接口已绑定";
            TrainerPlugin.Log.LogInfo("Harmony targets patched: " + Patched.Count);
        }

        private static void Count(string key) => TargetCounts[key] = TargetCounts.TryGetValue(key, out var count) ? count + 1 : 1;

        private static void Patch(MethodBase target, string? prefix = null, string? postfix = null, string? finalizer = null)
        {
            if (_harmony == null || !Patched.Add(target)) return;
            var prefixMethod = prefix == null ? null : AccessTools.Method(typeof(RuntimePatches), prefix);
            var postfixMethod = postfix == null ? null : AccessTools.Method(typeof(RuntimePatches), postfix);
            var finalizerMethod = finalizer == null ? null : AccessTools.Method(typeof(RuntimePatches), finalizer);
            _harmony.Patch(target,
                prefix: prefixMethod == null ? null : new HarmonyMethod(prefixMethod),
                postfix: postfixMethod == null ? null : new HarmonyMethod(postfixMethod),
                finalizer: finalizerMethod == null ? null : new HarmonyMethod(finalizerMethod));
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>(); }
        }

        private static IEnumerable<MethodInfo> SafeMethods(Type type)
        {
            try { return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); }
            catch { return Array.Empty<MethodInfo>(); }
        }

        private static void ControllerAwakePostfix(object __instance)
        {
            RuntimeBridge.ExtendController(__instance);
            RuntimeBridge.ApplyLevels();
        }

        private static void GetSaveDataPostfix(object __instance, object __result)
        {
            RuntimeBridge.RecordSaveData(__instance, __result);
        }

        private static void LoadFromSaveDataPrefix(object __instance, object[] __args)
        {
            RuntimeBridge.ExtendController(__instance);
            RuntimeBridge.PrepareLoad(__instance, __args);
        }

        private static void LoadFromSaveDataPostfix(object __instance)
        {
            RuntimeBridge.ApplyLevels();
            if (__instance.GetType().Name == "ShelfScoring") RuntimeBridge.ApplyRewardValue(__instance);
        }

        private static void SetLevelPrefix(object __instance, object[] __args)
        {
            if (__args.Length == 0 || !int.TryParse(Convert.ToString(__args[0]), out var requested)) return;
            if (RuntimeBridge.TryGetMaxLevel(__instance, out var maximum) && requested > maximum)
                __args[0] = Convert.ChangeType(maximum, __args[0].GetType());
        }

        private static bool MaxLevelPrefix(object __instance, ref int __result)
        {
            if (!RuntimeBridge.TryGetMaxLevel(__instance, out var maximum)) return true;
            __result = maximum;
            return false;
        }

        private static void PurchasePostfix(object __instance, bool __result)
        {
            if (__result) RuntimeBridge.RecordPurchase(__instance);
        }

        private static void SavePrefix(object[] __args, out RuntimeBridge.SaveSnapshot __state)
        {
            __state = RuntimeBridge.BeginSave(__args);
        }

        private static void SavePostfix(RuntimeBridge.SaveSnapshot __state, ref Task __result)
        {
            __result = RuntimeBridge.CompleteSaveAsync(__state, __result);
        }

        private static void ShelfScoringAwakePostfix(object __instance) => RuntimeBridge.RegisterRewardSource(__instance);
        private static void ShelfScoringPrefix(object __instance) => RuntimeBridge.ApplyRewardValue(__instance);
        private static void ShelfScoringPostfix(object __instance) => RuntimeBridge.ApplyRewardValue(__instance);

        private static bool AllFreePrefix(ref bool __result)
        {
            if (!TrainerPlugin.Settings.InfiniteStars || TrainerPlugin.Settings.RestoreRequested) return true;
            __result = true;
            return false;
        }

        private static bool CanSpendPrefix(ref bool __result)
        {
            if (!TrainerPlugin.Settings.InfiniteStars || TrainerPlugin.Settings.RestoreRequested) return true;
            __result = true;
            return false;
        }

        private static bool SpendBoolPrefix(ref bool __result)
        {
            if (!TrainerPlugin.Settings.InfiniteStars || TrainerPlugin.Settings.RestoreRequested) return true;
            __result = true;
            return false;
        }

        private static bool SpendVoidPrefix() => !TrainerPlugin.Settings.InfiniteStars || TrainerPlugin.Settings.RestoreRequested;
        private static bool BlockTestOnlyMethod() => !TrainerPlugin.TestMode;
        private static bool BlockTestOnlyResult(ref bool __result)
        {
            if (!TrainerPlugin.TestMode) return true;
            __result = false;
            return false;
        }
        private static void AbilityTickPostfix(object __instance) => RuntimeBridge.ApplyNoCooldown(__instance);

        private static bool AbilityReadyPrefix(object __instance, ref bool __result)
        {
            if ((!TrainerPlugin.Settings.NoCooldown || TrainerPlugin.Settings.RestoreRequested) && !RuntimeBridge.IsRestoreLevelPending(__instance)) return true;
            __result = RuntimeBridge.IsAbilityReady(__instance);
            return false;
        }

        private static bool TryInteractPrefix(object __instance, object[] __args, ref bool __result, out RuntimeBridge.ClickContext __state)
        {
            __state = RuntimeBridge.BeginClick(__instance, __args);
            RuntimeBridge.ObserveTestTryInteract(__instance, __args);
            if (!__state.BlockOriginal) return true;
            __result = false;
            return false;
        }

        private static void TryInteractPostfix(object __instance, object[] __args, bool __result, RuntimeBridge.ClickContext __state)
        {
            RuntimeBridge.EndClick(__instance, __args, __result, __state);
        }

        private static void DropKeyPressedPostfix(object __instance)
        {
            RuntimeBridge.AfterOriginalDropKeyPressed(__instance);
        }

        private static void InputActionCallbackPostfix(object __instance, object[] __args, MethodBase __originalMethod)
        {
            RuntimeBridge.ObserveTestInputActionCallback(__originalMethod.Name, __instance, __args);
        }

        private static void InteractionRequestPrefix(object __instance, object[] __args)
        {
            RuntimeBridge.ObserveTestInteractionRequest(__instance, __args);
        }

        private static void TryAdoptPrefix(object __instance, out int __state)
        {
            __state = RuntimeBridge.BeginSavedHandAdoption(__instance);
        }

        private static Exception? TryAdoptFinalizer(object __instance, int __state, Exception? __exception)
        {
            RuntimeBridge.EndSavedHandAdoption(__instance, __state);
            return __exception;
        }

        private static void TryDeletePostfix(int index, ref bool __result)
        {
            if (__result) RuntimeBridge.DeleteSlotProgress(index);
        }

        private static void TestSaveSystemInitializePostfix() => RuntimeBridge.RedirectTestSaveRoot();

        private static void LevelInitializePrefix(object[] __args) => RuntimeBridge.OnLevelInitialize(__args);
    }
}
