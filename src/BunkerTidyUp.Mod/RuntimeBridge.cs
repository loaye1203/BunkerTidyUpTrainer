using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BunkerTidyUp.Trainer.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BunkerTidyUp.Mod
{
    internal static class RuntimeBridge
    {
        private const int PickupFinalLevel = 10;
        private const float RangePickupRadius = 1.0f;
        private const float DropHeldActionInterval = 0.125f;
        private const float DefaultRangePickupInterval = 0.25f;
        private const int ExpandedCarryMaximum = 20;
        private static readonly int[] CarryOriginalCosts = { 5, 10, 20, 30, 50, 60, 80 };
        private static readonly int[] PullOriginalCosts = { 20, 30, 40, 50, 60, 70 };
        private static readonly int[] ScatterOriginalCosts = { 50, 75, 100 };
        private static readonly int[] RepeatOriginalCosts = { 10, 20, 30, 40, 50 };
        private static readonly int[] CarryExpansionCosts = { 84, 90, 95, 96, 97, 98, 99, 100, 103, 106, 109, 112, 115 };
        private static readonly int[] PullExpansionCosts = { 74, 80, 85, 90 };
        private static readonly int[] ScatterExpansionCosts = { 104, 110, 115, 120, 125, 130, 135 };
        private static readonly int[] RepeatExpansionCosts = { 54, 60, 65, 70, 75 };
        private static readonly object StateGate = new object();
        private static readonly ConcurrentDictionary<int, object> SlotGates = new ConcurrentDictionary<int, object>();
        private static readonly ConcurrentDictionary<int, long> CompletedSaveSequence = new ConcurrentDictionary<int, long>();
        private static readonly Dictionary<int, long> LatestSaveSequence = new Dictionary<int, long>();
        private static long _nextSaveSequence;
        private static long _automatedSaveSequenceBaseline;
        private static long _automatedTestRevisionBase;
        private static long _overflowRestoreRevision;
        private static readonly Dictionary<string, int> RealAbilities = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> RealUpgrades = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> BaseAbilityLimits = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> BaseUpgradeLimits = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<object, List<object>> OriginalLevelLists = new Dictionary<object, List<object>>(ReferenceComparer.Instance);
        private static readonly List<object> Controllers = new List<object>();
        private static readonly Dictionary<object, int> RewardSources = new Dictionary<object, int>(ReferenceComparer.Instance);
        private static readonly HashSet<int> RangePressCountedItems = new HashSet<int>();
        private static readonly HashSet<int> RangePressAttemptedItems = new HashSet<int>();
        private static readonly HashSet<int> FreshSlots = new HashSet<int>();
        private static bool IsRangeBatch { get => TrainerPlugin.IsRangeBatch; set => TrainerPlugin.IsRangeBatch = value; }
        private static int _rangeSlot = -1;
        private static object? _rangePressInput;
        private static object? _rangePressInteractor;
        private static object? _rangeReleaseConsumedInput;
        private static int _rangeReleaseConsumedFrame = -1;
        private static int _rangePressCount;
        private static int _rangePressLimit;
        private static float _lastRangePickupInterval;
        private static float _nextRangePickupAt;
        private static bool _rangePressActive;
        private static bool _rangePressSuppressedUntilRelease;
        private static object? _dropPressInput;
        private static float _nextDropAt;
        private static bool _dropPressActive;
        private static bool _dropPressSuppressedUntilRelease;
        private static string? _testHeldAction;
        private static Vector3? _testAimPoint;
        private static HashSet<int>? _testRangeCandidateIds;
        private static int _heldInputTestStage;
        private static int _heldInputSessionIndex;
        private static int _heldInputTestGrade;
        private static int _heldInputExpectedLimit;
        private static int _heldInputBaseline;
        private static int _heldInputLastCount;
        private static int _heldInputObservedAutoPicks;
        private static float _heldInputStartedAt;
        private static float _heldInputCapReachedAt;
        private static float _heldInputContextWaitStartedAt;
        private static bool _heldInputTutorialCloseLogged;
        private static bool _heldInputContextWaitLogged;
        private static float _heldInputReleaseAt;
        private static float _heldInputDropStartedAt;
        private static int _heldInputDropBaseline;
        private static int _heldInputDropLastCount;
        private static int _heldInputDropCount;
        private static object? _heldInputInteractor;
        private static object? _heldInputHands;
        private static object? _heldInputCarry;
        private static object? _heldInputInputProvider;
        private static List<object> _heldInputItems = new List<object>();
        private static List<float> _heldInputPickupTimes = new List<float>();
        private static List<float> _heldInputDropTimes = new List<float>();
        private static readonly Dictionary<int, float> HeldInputCadenceByGrade = new Dictionary<int, float>();
        private static float _heldInputMaxFrameDelta;
        private static float _heldInputDropMaxFrameDelta;
        private static Vector3 _heldInputTestAnchor;
        private static Vector3 _heldInputTestRight;
        private static float _heldInputTestFloorY;
        private static bool _heldInputTestFloorKnown;
        private static object? _heldInputTestDecoy;
        private static int _activeSlot = -2;
        private static SlotProgress? _loadedProgress;
        private const int AutomatedTestSlot = 666;
        private static int _automatedTestPhase;
        private static int _automatedTestStage;
        private static float _automatedTestStartedAt;
        private static float _automatedTestLevelLoadedAt;
        private static float _automatedInitializeObservedAt;
        private static bool _automatedInitializeSeen;
        private static bool _automatedWalletWaitLogged;
        private static object? _automatedTestLevel;
        private static Task? _automatedMetadataTask;
        private static object? _automatedMetadata;
        private static Task? _automatedSaveLoadTask;
        private static object? _automatedSaveModel;
        private static object? _rangeTestInteractor;
        private static object? _rangeTestHands;
        private static object? _rangeTestCarryUpgrade;
        private static object? _rangeTestControlItem;
        private static object? _rangeTestNegativeNeighbor;
        private static object? _rangeTestPositiveClick;
        private static object? _rangeTestPositiveNeighbor;
        private static int _rangeTestInitialHandsCount;
        private static float _rangeTestStartedAt;
        private static long _overflowSaveSequenceBaseline;
        private static int _overflowSavedHandItems;
        private static int _overflowExpectedHandsCount;
        private static object? _overflowTestLevel;

        internal sealed class SaveSnapshot
        {
            internal int Slot;
            internal long Sequence;
            internal long SettingsRevision;
            internal bool RestoreRequested;
            internal bool HasSaveModel;
            internal int HandsCount = -1;
            internal int HandsCapacity = -1;
            internal bool HandInventoryMetadataPresent;
            internal int VanillaHandsCapacity = -1;
            internal List<LevelEntry> Abilities = new List<LevelEntry>();
            internal List<LevelEntry> Upgrades = new List<LevelEntry>();
        }

        internal sealed class ClickContext
        {
            internal bool Enabled;
            internal bool BlockOriginal;
            internal Vector3 TargetPosition;
            internal object? Interactor;
        }

        internal static void InstallExistingControllers()
        {
            foreach (var controller in FindControllerObjects()) ExtendController(controller);
            CaptureCurrentLevels();
        }

        internal static void ConfigureAutomatedGameTest()
        {
            if (!TrainerPlugin.TestMode || !string.Equals(Environment.GetEnvironmentVariable("BUNKER_TIDY_UP_TEST_RUN_GAMEPLAY"), "1", StringComparison.Ordinal)) return;
            if (!int.TryParse(Environment.GetEnvironmentVariable("BUNKER_TIDY_UP_TEST_PHASE"), out _automatedTestPhase) || (_automatedTestPhase != 1 && _automatedTestPhase != 2 && _automatedTestPhase != 3 && _automatedTestPhase != 4))
                throw new InvalidOperationException("BUNKER_TIDY_UP_TEST_PHASE must be 1, 2, 3, or 4");
            if (_automatedTestPhase == 1 || _automatedTestPhase == 2)
            {
                _automatedTestRevisionBase = Math.Max(TrainerPlugin.Settings.Revision, TrainerPlugin.Status.SeenRevision) + 1;
                var settings = new ModSettings
                {
                    Schema = 1,
                    Revision = _automatedTestRevisionBase,
                    ExpandCarry = true,
                    ExpandAutoPickup = true,
                    ExpandAutoPlace = true,
                    ExpandContinuousPlace = true,
                    MoreStars = true
                };
                JsonFile.WriteAtomic(TrainerPlugin.SettingsPath, settings);
                TrainerPlugin.Settings = settings;
            }
            else if (_automatedTestPhase == 4)
            {
                _automatedTestRevisionBase = Math.Max(TrainerPlugin.Settings.Revision, TrainerPlugin.Status.SeenRevision) + 1;
                var settings = new ModSettings
                {
                    Schema = 1,
                    Revision = _automatedTestRevisionBase,
                    ExpandCarry = true,
                    ExpandAutoPickup = true,
                    ExpandAutoPlace = true,
                    ExpandContinuousPlace = true,
                    MoreStars = true,
                    HoldToDrop = true
                };
                JsonFile.WriteAtomic(TrainerPlugin.SettingsPath, settings);
                TrainerPlugin.Settings = settings;
            }
            else _automatedTestRevisionBase = TrainerPlugin.Settings.Revision;
            TrainerPlugin.Log.LogInfo("Automated isolated gameplay test phase " + _automatedTestPhase + " configured for save slot " + AutomatedTestSlot + ".");
        }

        internal static void RedirectTestSaveRoot()
        {
            if (!TrainerPlugin.TestMode) return;
            var saveSystem = FindGameType("SaveSystem") ?? throw new InvalidOperationException("SaveSystem type is unavailable for isolated test storage");
            var field = saveSystem.GetField("_persistentDataPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(string)) throw new MissingFieldException(saveSystem.FullName, "_persistentDataPath");
            var root = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "TrainerTestData"));
            var gameRoot = Path.GetFullPath(Paths.GameRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test save path escaped the isolated game directory");
            Directory.CreateDirectory(root);
            field.SetValue(null, root);
            Directory.CreateDirectory(Path.Combine(root, "Saves"));
            TrainerPlugin.Log.LogInfo("Test SaveSystem persistent path redirected to " + root);
        }

        internal static void ProcessAutomatedGameTest()
        {
            if (!TrainerPlugin.TestMode || _automatedTestPhase == 0) return;
            if (_automatedTestStartedAt <= 0f) _automatedTestStartedAt = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _automatedTestStartedAt > 180f)
            {
                FailAutomatedGameTest("Timed out waiting for the game scene or save operation");
                return;
            }

            try
            {
                if (_automatedTestPhase == 1) ProcessAutomatedNewGameTest();
                else if (_automatedTestPhase == 2) ProcessAutomatedContinueTest();
                else if (_automatedTestPhase == 3) ProcessAutomatedOverflowReloadTest();
                else ProcessAutomatedHeldInputTest();
            }
            catch (Exception ex)
            {
                FailAutomatedGameTest(ex.GetBaseException().ToString());
            }
        }

        private static void ProcessAutomatedHeldInputTest()
        {
            if (_heldInputTestStage == 0)
            {
                var menu = FindActiveGameComponent("MainMenu");
                if (menu == null) return;
                ClearAutomatedTestSlot();
                InvokeMenuStart(menu, "OnPlayClicked");
                _heldInputTestStage = 1;
                TrainerPlugin.Log.LogInfo("Focused held-input validation started in the isolated new-game slot.");
                return;
            }

            if (_heldInputTestStage == 1)
            {
                var level = FindActiveGameComponent("Level");
                if (level == null || GetSaveSlot() != AutomatedTestSlot || TrainerPlugin.AppliedRevision < _automatedTestRevisionBase) return;
                if (!_automatedInitializeSeen || IsLevelLoading(level) || Time.realtimeSinceStartup - _automatedInitializeObservedAt < 2f) return;
                if (!PrepareHeldInputTestContext(level)) return;
                var wallet = GetPlayerWalletInstance();
                if (wallet == null) return;
                var objects = FindLevelObjects().ToDictionary(item => item.Owner.GetType().Name, item => item.Owner, StringComparer.Ordinal);
                VerifyExpandedTables(objects);
                VerifyApprovedPriceAndRewardPlan(objects);
                VerifyProgressiveScoringSaveRoundTrip();
                _heldInputInteractor = FindActiveGameComponent("PlayerInteractor") ?? throw new InvalidOperationException("PlayerInteractor unavailable in the isolated held-input level");
                _heldInputHands = FindNestedByName(_heldInputInteractor, "Hands", 2) ?? throw new InvalidOperationException("Hands unavailable in the isolated held-input level");
                _heldInputCarry = objects.TryGetValue("HandCapacityUpgrade", out var carry) ? carry : throw new InvalidOperationException("HandCapacityUpgrade unavailable in the isolated held-input level");
                _heldInputInputProvider = ReadMember(_heldInputInteractor, "_input", "Input") ?? FindActiveGameComponent("InputProvider") ?? throw new InvalidOperationException("InputProvider unavailable in the isolated held-input level");
                _automatedTestLevel = level;

                PurchaseToLevel(objects, "HandCapacityUpgrade", ExpandedCarryMaximum, false);
                SetLevel(_heldInputCarry, 7);
                if (ReadInt(_heldInputHands, "MaxCount", "_maxCount") != 75)
                    throw new InvalidOperationException("Original Carry grade 7 did not restore the original capacity 75");
                SetLevel(_heldInputCarry, ExpandedCarryMaximum);
                if (GetCurrentLevel(_heldInputCarry) != ExpandedCarryMaximum || ReadInt(_heldInputHands, "MaxCount", "_maxCount") != 270)
                    throw new InvalidOperationException("Expanded Carry grade 20 did not provide capacity 270");

                _heldInputSessionIndex = 0;
                HeldInputCadenceByGrade.Clear();
                StartHeldInputRangeSession();
                return;
            }

            if (_heldInputTestStage == 2)
            {
                _heldInputMaxFrameDelta = Math.Max(_heldInputMaxFrameDelta, Math.Min(Time.unscaledDeltaTime, 0.08f));
                var count = ReadInt(_heldInputHands!, "Count", "_count");
                if (count > _heldInputLastCount)
                {
                    if (count - _heldInputLastCount != 1)
                        throw new InvalidOperationException("A held range-pick tick collected more than one item");
                    var now = Time.realtimeSinceStartup;
                    var elapsed = _heldInputPickupTimes.Count > 0 ? now - _heldInputPickupTimes[_heldInputPickupTimes.Count - 1] : float.MaxValue;
                    if (GetCurrentLevel(_heldInputCarry!) != _heldInputTestGrade)
                        throw new InvalidOperationException("Test Carry grade drifted during held session: expected=" + _heldInputTestGrade + ", actual=" + GetCurrentLevel(_heldInputCarry!));
                    var expectedInterval = GetRangePickupInterval(_heldInputTestGrade);
                    if (elapsed < expectedInterval - 0.03f)
                        throw new InvalidOperationException("Held range pickup was faster than its Carry grade target: grade=" + _heldInputTestGrade +
                            ", target=" + expectedInterval.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) +
                            "s, elapsed=" + elapsed.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) +
                            "s, realtimeNow=" + Time.realtimeSinceStartup.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) +
                            ", nextAllowed=" + _nextRangePickupAt.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + ".");
                    var nextIndex = 1 + _heldInputObservedAutoPicks;
                    if (nextIndex >= _heldInputExpectedLimit || !IsPicked(_heldInputItems[nextIndex]))
                        throw new InvalidOperationException("Held range pickup did not choose the nearest candidate at the current aim point; expected item index " + nextIndex);
                    _heldInputObservedAutoPicks++;
                    _heldInputPickupTimes.Add(now);
                    _heldInputLastCount = count;
                    var followingIndex = 1 + _heldInputObservedAutoPicks;
                    if (followingIndex <= _heldInputExpectedLimit)
                        PrepareHeldInputRangeTarget(_heldInputInteractor!, _heldInputItems[followingIndex]);
                }

                if (count >= _heldInputBaseline + _heldInputExpectedLimit)
                {
                    VerifyHeldRangeCadence(_heldInputTestGrade);
                    _heldInputCapReachedAt = Time.realtimeSinceStartup;
                    _heldInputTestStage = 3;
                }
                else if (Time.realtimeSinceStartup - _heldInputStartedAt > 20f)
                    throw new TimeoutException("Held range pickup did not reach the requested per-press cap");
                return;
            }

            if (_heldInputTestStage == 3)
            {
                if (Time.realtimeSinceStartup - _heldInputCapReachedAt < 1.0f) return;
                var count = ReadInt(_heldInputHands!, "Count", "_count");
                if (count != _heldInputBaseline + _heldInputExpectedLimit || _rangePressCount != _heldInputExpectedLimit)
                    throw new InvalidOperationException("Held pickup exceeded or reset its per-press cap while the input stayed held");
                var extra = _heldInputItems[_heldInputExpectedLimit];
                PrepareHeldInputRangeTarget(_heldInputInteractor!, extra);
                if (IsPicked(extra) || InvokeOriginalItemInteraction(extra, _heldInputInteractor!))
                    throw new InvalidOperationException("The original click path bypassed the held pickup cap");
                _testHeldAction = null;
                _testAimPoint = null;
                _heldInputReleaseAt = Time.realtimeSinceStartup;
                _heldInputLastCount = count;
                _heldInputTestStage = 4;
                return;
            }

            if (_heldInputTestStage == 4)
            {
                if (Time.realtimeSinceStartup - _heldInputReleaseAt < 0.6f) return;
                if (ReadInt(_heldInputHands!, "Count", "_count") != _heldInputLastCount || IsPicked(_heldInputItems[_heldInputExpectedLimit]))
                    throw new InvalidOperationException("Held range pickup continued after input release");
                _testRangeCandidateIds = null;
                if (_heldInputSessionIndex < 5)
                {
                    _heldInputSessionIndex++;
                    StartHeldInputRangeSession();
                    return;
                }
                BeginHeldInputVPathCheck();
                return;
            }

            if (_heldInputTestStage == 5)
            {
                if (Time.realtimeSinceStartup - _heldInputReleaseAt < 0.6f) return;
                if (ReadInt(_heldInputHands!, "Count", "_count") != _heldInputLastCount || IsPicked(_heldInputItems[1]))
                    throw new InvalidOperationException("Direct V-style PlayerInteractor.TryPick triggered a neighboring range pickup");
                WriteAutomatedSettings(maxAll: false, expansions: false, revision: _automatedTestRevisionBase + 1, holdToDrop: true);
                _heldInputTestStage = 6;
                return;
            }

            if (_heldInputTestStage == 6)
            {
                if (TrainerPlugin.AppliedRevision < _automatedTestRevisionBase + 1) return;
                if (GetCurrentLevel(_heldInputCarry!) != 7 || ReadInt(_heldInputHands!, "MaxCount", "_maxCount") != 75)
                    throw new InvalidOperationException("Disabling Carry expansion did not return the bought grade 20 to original grade 7 and capacity 75");
                BeginHeldInputDropTest();
                return;
            }

            if (_heldInputTestStage == 7)
            {
                _heldInputDropMaxFrameDelta = Math.Max(_heldInputDropMaxFrameDelta, Math.Min(Time.unscaledDeltaTime, 0.08f));
                var count = ReadInt(_heldInputHands!, "Count", "_count");
                if (count < _heldInputDropLastCount)
                {
                    if (_heldInputDropLastCount - count != 1)
                        throw new InvalidOperationException("A held drop interval removed more than one item");
                    var now = Time.realtimeSinceStartup;
                    var elapsed = now - _heldInputDropTimes[_heldInputDropTimes.Count - 1];
                    if (elapsed < DropHeldActionInterval - 0.01f)
                        throw new InvalidOperationException("Held drop was faster than its 8/s target: elapsed=" + elapsed.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture));
                    _heldInputDropTimes.Add(now);
                    _heldInputDropCount++;
                    _heldInputDropLastCount = count;
                }
                if (_heldInputDropCount >= 5)
                {
                    var dropAverage = (_heldInputDropTimes[_heldInputDropTimes.Count - 1] - _heldInputDropTimes[0]) / (_heldInputDropTimes.Count - 1);
                    var dropMax = DropHeldActionInterval + Math.Max(0.025f, _heldInputDropMaxFrameDelta * 1.25f);
                    if (dropAverage < DropHeldActionInterval - 0.01f || dropAverage > dropMax)
                        throw new InvalidOperationException("Continuous-drop cadence did not match 8/s within frame tolerance: average=" + dropAverage.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) +
                            "s, accepted=" + (DropHeldActionInterval - 0.01f).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + ".." +
                            dropMax.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, frameDelta=" +
                            _heldInputDropMaxFrameDelta.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s");
                    TrainerPlugin.Log.LogInfo("Verified continuous-drop cadence: average=" + dropAverage.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) +
                        "s, observed=" + (1f / dropAverage).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/s, scheduled=" +
                        DropHeldActionInterval.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, frameDelta=" +
                        _heldInputDropMaxFrameDelta.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s over four repeated drops.");
                    _testHeldAction = null;
                    _heldInputReleaseAt = Time.realtimeSinceStartup;
                    _heldInputTestStage = 8;
                }
                else if (Time.realtimeSinceStartup - _heldInputDropStartedAt > 5f)
                    throw new TimeoutException("Continuous drop did not execute four timed drops after its original click");
                return;
            }

            if (_heldInputTestStage == 8)
            {
                if (Time.realtimeSinceStartup - _heldInputReleaseAt < 0.6f) return;
                if (ReadInt(_heldInputHands!, "Count", "_count") != _heldInputDropBaseline - 5)
                    throw new InvalidOperationException("Continuous drop did not stop after the drop input was released");
                _testAimPoint = null;
                _testRangeCandidateIds = null;
                _testHeldAction = null;
                WriteAutomatedGameTestReport(true, "Focused phase 4 passed: the isolated new-game tutorial was closed through the game's Menu.Deactivate API and real focus/input-lock/cursor-lock gates were awaited; TestMode simulated held actions and changing aim points while invoking original TryInteract/TryPick/TryDrop methods, so physical key rebinding was not tested. The game's ShelfScoring APIs roundtripped 68 real ItemData IDs with count 68 and next reward 21. Carry 10/16/17/18/19/20 held-range caps 5/10/15/20/25/30 include the original click; scheduled intervals are 0.2500/0.2000/0.1667/0.1429/0.1250/0.1000 seconds, and the frame-limited observed mean for each grade is recorded in BepInEx LogOutput.log. Candidate selection follows the changing aim point, reaching a cap does not restart while held, release stops pickup, a direct PlayerInteractor.TryPick does not trigger range pickup, Carry 20 provides 270 capacity and expansion-off restores grade 7/75. Original drop click plus four held drops use a scheduled 0.1250-second interval; the observed mean is recorded in BepInEx LogOutput.log and release stops further drops.");
            }
        }

        private static void StartHeldInputRangeSession()
        {
            var grades = new[] { 10, 16, 17, 18, 19, 20 };
            var grade = grades[_heldInputSessionIndex];
            _heldInputTestGrade = grade;
            lock (StateGate) RealUpgrades["HandCapacityUpgrade"] = grade;
            SetLevel(_heldInputCarry!, grade);
            if (GetCurrentLevel(_heldInputCarry!) != grade) throw new InvalidOperationException("Could not set isolated Carry grade " + grade);
            _heldInputExpectedLimit = GetRangeHoldLimit(grade);
            if (_heldInputExpectedLimit != (grade <= 15 ? 5 : (grade - 14) * 5))
                throw new InvalidOperationException("Range-hold cap mapping changed for Carry grade " + grade);
            _heldInputItems = ArrangeHeldInputTestItems(_heldInputInteractor!, _heldInputExpectedLimit + 2);
            _heldInputBaseline = ReadInt(_heldInputHands!, "Count", "_count");
            _heldInputLastCount = _heldInputBaseline;
            _heldInputObservedAutoPicks = 0;
            _heldInputPickupTimes = new List<float>();
            _heldInputMaxFrameDelta = 0f;
            _testAimPoint = GetTransform(_heldInputItems[0])!.position;
            _testHeldAction = "InteractAction";
            var level = FindActiveGameComponent("Level");
            TrainerPlugin.Log.LogInfo("Held-input test context: focus=" + Application.isFocused + ", timeScale=" + Time.timeScale + ", InputProvider(IsLocked/IsActionsLocked/IsCursorLocked)=" +
                ReadBool(_heldInputInputProvider!, "IsLocked") + "/" + ReadBool(_heldInputInputProvider!, "IsActionsLocked") + "/" + ReadBool(_heldInputInputProvider!, "IsCursorLocked") +
                ", shopOpen=" + ReadBool(_heldInputInteractor!, "_isShopOpen") + ", levelLoading/exiting=" + (level != null && ReadBool(level, "IsLoading", "_isLoading")) + "/" +
                (level != null && ReadBool(level, "IsExiting", "_isExiting")) + ", rangeSuppressed=" + _rangePressSuppressedUntilRelease + ", carry=" + FindCurrentCarryLevel() +
                ", hands=" + ReadInt(_heldInputHands!, "Count", "_count") + "/" + ReadInt(_heldInputHands!, "MaxCount", "_maxCount") + ".");
            if (!InvokeOriginalItemInteraction(_heldInputItems[0], _heldInputInteractor!) || !IsPicked(_heldInputItems[0]))
                throw new InvalidOperationException("The original click did not pick the first item in the held range session");
            _heldInputLastCount = ReadInt(_heldInputHands!, "Count", "_count");
            if (_heldInputLastCount != _heldInputBaseline + 1 || _rangePressCount != 1)
                throw new InvalidOperationException("The original click was not counted as the first item in the per-press limit: hands=" + _heldInputLastCount + "/" + (_heldInputBaseline + 1) +
                    ", rangePressCount=" + _rangePressCount + ", inputContextAllowed=" + IsHeldInputContextAllowed(_heldInputInputProvider!, _heldInputInteractor!) +
                    ", rangeSuppressed=" + _rangePressSuppressedUntilRelease + ", instanceId=" + InstanceId(_heldInputItems[0]) + ".");
            _heldInputPickupTimes.Add(Time.realtimeSinceStartup);
            _heldInputStartedAt = Time.realtimeSinceStartup;
            if (_heldInputExpectedLimit > 1) PrepareHeldInputRangeTarget(_heldInputInteractor!, _heldInputItems[1]);
            _heldInputTestStage = 2;
            TrainerPlugin.Log.LogInfo("Started held range session at Carry " + grade + ": cap=" + _heldInputExpectedLimit +
                ", target=" + GetRangePickupRate(grade) + "/s (" + GetRangePickupInterval(grade).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) +
                "s), effective=" + GetCurrentLevel(_heldInputCarry!) + ", recorded test purchase grade=" + RealUpgrades["HandCapacityUpgrade"] + ", including the original click.");
        }

        private static void VerifyHeldRangeCadence(int carryLevel)
        {
            if (_heldInputPickupTimes.Count != _heldInputExpectedLimit)
                throw new InvalidOperationException("Held range cadence sample count did not include the original click and every capped pickup");

            var average = (_heldInputPickupTimes[_heldInputPickupTimes.Count - 1] - _heldInputPickupTimes[0]) / (_heldInputPickupTimes.Count - 1);
            var expected = GetRangePickupInterval(carryLevel);
            var minimum = expected - 0.02f;
            var frameTolerance = Math.Max(0.025f, _heldInputMaxFrameDelta * 1.25f);
            var maximum = expected + frameTolerance;
            if (Math.Abs(_lastRangePickupInterval - expected) > 0.001f)
                throw new InvalidOperationException("The range scheduler did not use the configured Carry " + carryLevel + " interval: scheduled=" +
                    _lastRangePickupInterval.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, expected=" +
                    expected.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s.");
            if (average < minimum || average > maximum)
                throw new InvalidOperationException("Held range cadence missed Carry " + carryLevel + " target " + GetRangePickupRate(carryLevel) +
                    "/s: average=" + average.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, expected=" +
                    expected.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, accepted=" +
                    minimum.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + ".." +
                    maximum.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, frameDelta=" +
                    _heldInputMaxFrameDelta.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s.");

            if (carryLevel == 16 && HeldInputCadenceByGrade.TryGetValue(10, out var grade10Average) && average >= grade10Average - 0.01f)
                throw new InvalidOperationException("Carry 16 did not accelerate the observed pickup cadence relative to Carry 10: grade10=" +
                    grade10Average.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, grade16=" + average.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s.");
            if (carryLevel == 20 && HeldInputCadenceByGrade.TryGetValue(16, out var grade16Average) && average >= grade16Average - 0.01f)
                throw new InvalidOperationException("Carry 20 did not accelerate the observed pickup cadence relative to Carry 16: grade16=" +
                    grade16Average.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, grade20=" + average.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s.");
            HeldInputCadenceByGrade[carryLevel] = average;

            TrainerPlugin.Log.LogInfo("Verified held range cadence at Carry " + carryLevel + ": cap=" + _heldInputExpectedLimit +
                ", average=" + average.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, rate=" +
                (1f / average).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/s (target=" + GetRangePickupRate(carryLevel) + "/s, scheduled=" +
                expected.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s, frameDelta=" +
                _heldInputMaxFrameDelta.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "s).");
        }

        private static int GetRangePickupRate(int carryLevel)
        {
            if (carryLevel <= 15) return 4;
            if (carryLevel <= 19) return carryLevel - 11;
            return 10;
        }

        private static float GetRangePickupInterval(int carryLevel)
        {
            var rate = GetRangePickupRate(carryLevel);
            return rate == 4 ? DefaultRangePickupInterval : 1f / rate;
        }

        private static bool PrepareHeldInputTestContext(object level)
        {
            var tutorial = ReadMember(level, "_tutorial", "Tutorial");
            if (tutorial != null && ReadBool(tutorial, "_isActive", "IsActive"))
            {
                var deactivate = SafeMethods(tutorial.GetType()).FirstOrDefault(method => method.Name == "Deactivate" && method.GetParameters().Length == 0)
                    ?? throw new MissingMethodException(tutorial.GetType().FullName, "Deactivate()");
                deactivate.Invoke(tutorial, null);
                if (!_heldInputTutorialCloseLogged)
                {
                    TrainerPlugin.Log.LogInfo("Closed the isolated game's tutorial through its original Menu.Deactivate() API before testing held input.");
                    _heldInputTutorialCloseLogged = true;
                }
                if (ReadBool(tutorial, "_isActive", "IsActive")) return WaitForHeldInputUnlock();
            }
            return WaitForHeldInputUnlock();
        }

        private static bool WaitForHeldInputUnlock()
        {
            var input = FindActiveGameComponent("InputProvider");
            if (input == null) return false;
            if (!ReadBool(input, "IsLocked") && !ReadBool(input, "IsActionsLocked") && ReadBool(input, "IsCursorLocked"))
            {
                _heldInputContextWaitStartedAt = 0f;
                TrainerPlugin.Log.LogInfo("Held-input test entered normal gameplay context with focus=" + Application.isFocused + ", InputProvider unlocked, cursor locked.");
                return true;
            }
            if (_heldInputContextWaitStartedAt <= 0f) _heldInputContextWaitStartedAt = Time.realtimeSinceStartup;
            if (!_heldInputContextWaitLogged)
            {
                TrainerPlugin.Log.LogInfo("Waiting for the original tutorial/UI to release input: focus=" + Application.isFocused + ", IsLocked=" + ReadBool(input, "IsLocked") +
                    ", IsActionsLocked=" + ReadBool(input, "IsActionsLocked") + ", IsCursorLocked=" + ReadBool(input, "IsCursorLocked") + ".");
                _heldInputContextWaitLogged = true;
            }
            if (Time.realtimeSinceStartup - _heldInputContextWaitStartedAt > 15f)
                throw new TimeoutException("The game's tutorial/menu did not release its original input locks for the held-input test");
            return false;
        }

        private static List<object> ArrangeHeldInputTestItems(object interactor, int required)
        {
            var itemType = FindGameType("PickableItem") ?? throw new MissingMemberException("Bunker.Items.PickableItem");
            var playerTransform = GetTransform(interactor) ?? throw new InvalidOperationException("PlayerInteractor transform unavailable");
            var movement = ReadMember(interactor, "_movement", "Movement");
            if (movement == null || !(ReadMember(movement, "LookingRay", "_lookingRay") is Ray ray))
                throw new InvalidOperationException("PlayerInteractor.LookingRay unavailable for held-input validation");
            var forward = Vector3.ProjectOnPlane(ray.direction, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) throw new InvalidOperationException("Player facing direction was not usable for held-input ground-item placement");
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var interactDistance = GetInteractDistance(interactor);
            var layerMask = GetInteractableMask(interactor);
            var scene = (interactor as Component)?.gameObject.scene ?? SceneManager.GetActiveScene();
            var candidates = UnityEngine.Object.FindObjectsByType(itemType, FindObjectsSortMode.None)
                .Where(candidate => IsCandidateAlive(candidate) && candidate is Component component && component.gameObject.scene == scene)
                .Where(candidate => !IsPicked(candidate) && !IsInsideShelf(GetTransform(candidate)!))
                .Where(candidate => !GetTransform(candidate)!.IsChildOf(playerTransform) && IsLayerInteractable(GetTransform(candidate)!, layerMask))
                .GroupBy(candidate => ReadMember(candidate, "_data", "Data") ?? candidate.GetType(), ReferenceComparer.Instance)
                .Where(group => group.Count() >= required)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Take(required).ToList())
                .FirstOrDefault() ?? throw new InvalidOperationException("The isolated level did not have " + required + " same-type ground items for the held-input test");

            var anchor = playerTransform.position + forward * 1.35f;
            var foundGround = Physics.Raycast(anchor + Vector3.up * 3f, Vector3.down, out var ground, 8f, ~0, QueryTriggerInteraction.Ignore);
            if (foundGround && ground.normal.y >= 0.55f)
            {
                _heldInputTestFloorY = ground.point.y;
                _heldInputTestFloorKnown = true;
            }
            else if (!_heldInputTestFloorKnown || Math.Abs(playerTransform.position.y - _heldInputTestFloorY) > 3f)
            {
                var rayOrigin = anchor + Vector3.up * 3f;
                throw new InvalidOperationException("No walkable ground surface found for held-input test items: player=" + playerTransform.position + ", anchor=" + anchor + ", rayOrigin=" + rayOrigin + ", priorFloorKnown=" + _heldInputTestFloorKnown + ", priorFloorY=" + _heldInputTestFloorY.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
            _heldInputTestAnchor = anchor;
            _heldInputTestRight = right;
            _heldInputTestDecoy = required > 2 ? candidates[required - 1] : null;
            TrainerPlugin.Log.LogInfo("Arranging held-input targets: required=" + required + ", player=" + playerTransform.position + ", anchor=" + anchor + ", floorY=" + _heldInputTestFloorY.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ", freshFloorRay=" + foundGround + ".");
            PositionHeldInputTestItem(candidates[0], anchor);
            if (required > 1) PositionHeldInputTestItem(candidates[1], anchor + right * 0.45f);
            Physics.SyncTransforms();
            ValidateHeldInputTestTarget(candidates[0], interactor, interactDistance, layerMask);
            if (required > 1) ValidateHeldInputTestTarget(candidates[1], interactor, interactDistance, layerMask);
            return candidates.Cast<object>().ToList();
        }

        private static void PrepareHeldInputRangeTarget(object interactor, object target)
        {
            if (_heldInputTestDecoy == null) throw new InvalidOperationException("The held range test has no nearest-target decoy");
            PositionHeldInputTestItem(target, _heldInputTestAnchor);
            PositionHeldInputTestItem(_heldInputTestDecoy, _heldInputTestAnchor + _heldInputTestRight * 0.45f);
            Physics.SyncTransforms();
            var ids = new[] { InstanceId(target), InstanceId(_heldInputTestDecoy) };
            if (ids.Any(id => id == 0) || ids[0] == ids[1]) throw new InvalidOperationException("Held range test targets did not have distinct real InstanceIDs");
            _testRangeCandidateIds = new HashSet<int>(ids);
            _testAimPoint = GetTransform(target)!.position;
            var distance = GetInteractDistance(interactor);
            var mask = GetInteractableMask(interactor);
            ValidateHeldInputTestTarget(target, interactor, distance, mask);
            ValidateHeldInputTestTarget(_heldInputTestDecoy, interactor, distance, mask);
        }

        private static void PositionHeldInputTestItem(object item, Vector3 horizontalPosition)
        {
            var transform = GetTransform(item) ?? throw new InvalidOperationException("Held-input test PickableItem transform unavailable");
            transform.position = new Vector3(horizontalPosition.x, _heldInputTestFloorY + GetGroundPivotOffset(item), horizontalPosition.z);
        }

        private static void ValidateHeldInputTestTarget(object candidate, object interactor, float interactDistance, int layerMask)
        {
            var transform = GetTransform(candidate) ?? throw new InvalidOperationException("Held-input test PickableItem transform unavailable");
            var playerTransform = GetTransform(interactor) ?? throw new InvalidOperationException("PlayerInteractor transform unavailable while validating held targets");
            var origin = GetViewOrigin(interactor, playerTransform);
            var distance = Vector3.Distance(origin, transform.position);
            var correctLayer = IsLayerInteractable(transform, layerMask);
            var visible = distance <= interactDistance && HasLineOfSight(origin, transform, distance, layerMask, GetTriggerInteraction(interactor));
            var capacity = CanInteract(candidate, interactor, transform.position);
            if (distance > interactDistance || !correctLayer || !visible || !capacity)
                throw new InvalidOperationException("Placed held-input target failed live legality checks: id=" + InstanceId(candidate) + ", distance=" + distance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "/" + interactDistance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ", layer=" + correctLayer + ", lineOfSight=" + visible + ", canInteract=" + capacity + ", picked=" + IsPicked(candidate) + ".");
        }

        private static void VerifyProgressiveScoringSaveRoundTrip()
        {
            var scoring = FindActiveGameComponent("ShelfScoring") ?? throw new InvalidOperationException("ShelfScoring unavailable for progressive reward save validation");
            var getSaveData = SafeMethods(scoring.GetType()).FirstOrDefault(method => method.Name == "GetSaveData" && method.GetParameters().Length == 0)
                ?? throw new MissingMethodException(scoring.GetType().FullName, "GetSaveData()");
            var loadFromSaveData = SafeMethods(scoring.GetType()).FirstOrDefault(method => method.Name == "LoadFromSaveData" && method.GetParameters().Length == 1)
                ?? throw new MissingMethodException(scoring.GetType().FullName, "LoadFromSaveData(List<String>)");
            var original = getSaveData.Invoke(scoring, null) as IEnumerable<string>
                ?? throw new InvalidDataException("ShelfScoring.GetSaveData did not return scored type IDs");
            var originalIds = original.ToList();
            try
            {
                var pickableType = FindGameType("PickableItem") ?? throw new MissingMemberException("Bunker.Items.PickableItem");
                var allTypes = ReadStaticMember(pickableType, "Types") as IEnumerable
                    ?? throw new InvalidDataException("PickableItem.Types was unavailable for progressive reward persistence validation");
                var ids = allTypes.Cast<object>()
                    .Select(item => ReadMember(item, "InternalID") as string)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .Take(68)
                    .ToList();
                if (ids.Count != 68) throw new InvalidDataException("Expected 68 distinct PickableItem.Types IDs, found " + ids.Count);

                loadFromSaveData.Invoke(scoring, new object[] { ids });
                if (ReadInt(scoring, "ScoredTypesCount") != 68 || ReadInt(scoring, "_pointsPerType") != 21)
                    throw new InvalidDataException("Loading 68 real scored item IDs did not set ScoredTypesCount=68 and the next award to 21");
                var roundTrip = getSaveData.Invoke(scoring, null) as ICollection<string>
                    ?? throw new InvalidDataException("ShelfScoring.GetSaveData did not return its 68-ID roundtrip");
                if (roundTrip.Count != 68 || roundTrip.Distinct(StringComparer.Ordinal).Count() != 68)
                    throw new InvalidDataException("ShelfScoring.GetSaveData did not preserve all 68 scored type IDs");
                loadFromSaveData.Invoke(scoring, new object[] { roundTrip.ToList() });
                if (ReadInt(scoring, "ScoredTypesCount") != 68 || ReadInt(scoring, "_pointsPerType") != 21)
                    throw new InvalidDataException("ShelfScoring save/load roundtrip reset the progressive reward counter");
                TrainerPlugin.Log.LogInfo("Verified original ShelfScoring load/save roundtrip with 68 live ItemData IDs: persisted count=68, next reward=21.");
            }
            finally
            {
                loadFromSaveData.Invoke(scoring, new object[] { originalIds });
                if (ReadInt(scoring, "ScoredTypesCount") != originalIds.Count)
                    throw new InvalidDataException("Could not restore the isolated test slot's original scored type state");
            }
        }

        private static void BeginHeldInputVPathCheck()
        {
            _testHeldAction = null;
            _testAimPoint = null;
            _testRangeCandidateIds = null;
            var candidates = ArrangeHeldInputTestItems(_heldInputInteractor!, 2);
            _heldInputItems = candidates;
            _heldInputBaseline = ReadInt(_heldInputHands!, "Count", "_count");
            var tryPick = SafeMethods(_heldInputInteractor!.GetType()).FirstOrDefault(method => method.Name == "TryPick" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType.IsInstanceOfType(candidates[0]))
                ?? throw new MissingMethodException(_heldInputInteractor.GetType().FullName, "TryPick(PickableItem)");
            if (!(tryPick.Invoke(_heldInputInteractor, new[] { candidates[0] }) is bool accepted) || !accepted || !IsPicked(candidates[0]))
                throw new InvalidOperationException("Direct V-style PlayerInteractor.TryPick failed its original target");
            _heldInputLastCount = ReadInt(_heldInputHands!, "Count", "_count");
            if (_heldInputLastCount != _heldInputBaseline + 1 || IsPicked(candidates[1]))
                throw new InvalidOperationException("V-style TryPick triggered a range pickup or changed more than one Hands item");
            _heldInputReleaseAt = Time.realtimeSinceStartup;
            _heldInputTestStage = 5;
            TrainerPlugin.Log.LogInfo("V-style direct TryPick collected only its requested item; no adjacent range pickup was triggered.");
        }

        private static void BeginHeldInputDropTest()
        {
            _heldInputInputProvider = ReadMember(_heldInputInteractor!, "_input", "Input") ?? FindActiveGameComponent("InputProvider")
                ?? throw new InvalidOperationException("InputProvider unavailable for continuous-drop validation");
            _heldInputDropBaseline = ReadInt(_heldInputHands!, "Count", "_count");
            if (_heldInputDropBaseline < 5) throw new InvalidOperationException("Not enough held items to verify the continuous-drop cadence");
            _heldInputDropLastCount = _heldInputDropBaseline;
            _heldInputDropCount = 0;
            _heldInputDropTimes = new List<float>();
            _heldInputDropMaxFrameDelta = 0f;
            _testHeldAction = "DropAction";
            var onDrop = SafeMethods(_heldInputInteractor!.GetType()).FirstOrDefault(method => method.Name == "OnDropKeyPressed" && method.GetParameters().Length == 0)
                ?? throw new MissingMethodException(_heldInputInteractor.GetType().FullName, "OnDropKeyPressed()");
            onDrop.Invoke(_heldInputInteractor, null);
            var afterOriginal = ReadInt(_heldInputHands!, "Count", "_count");
            if (afterOriginal != _heldInputDropBaseline - 1)
                throw new InvalidOperationException("Original drop-key callback did not drop exactly one item");
            _heldInputDropLastCount = afterOriginal;
            _heldInputDropCount = 1;
            _heldInputDropTimes.Add(Time.realtimeSinceStartup);
            _heldInputDropStartedAt = Time.realtimeSinceStartup;
            _heldInputTestStage = 7;
            TrainerPlugin.Log.LogInfo("Original drop-key callback performed the first item; continuous-drop timing is now under test.");
        }

        private static void ProcessAutomatedNewGameTest()
        {
            if (_automatedTestStage == 0)
            {
                var menu = FindActiveGameComponent("MainMenu");
                if (menu == null) return;
                ClearAutomatedTestSlot();
                InvokeMenuStart(menu, "OnPlayClicked");
                _automatedTestStage = 1;
                TrainerPlugin.Log.LogInfo("Starting the isolated new-game slot through Bunker.Core.MainMenu.OnPlayClicked.");
                return;
            }

            if (_automatedTestStage == 1)
            {
                var level = FindActiveGameComponent("Level");
                if (level == null || GetSaveSlot() != AutomatedTestSlot) return;
                _automatedTestLevel = level;
                if (_automatedTestLevelLoadedAt <= 0f) _automatedTestLevelLoadedAt = Time.realtimeSinceStartup;
                if (!_automatedInitializeSeen || IsLevelLoading(level) || Time.realtimeSinceStartup - _automatedInitializeObservedAt < 2f) return;
                if (GetPlayerWalletInstance() == null)
                {
                    if (!_automatedWalletWaitLogged && Time.realtimeSinceStartup - _automatedTestLevelLoadedAt > 4f)
                    {
                        _automatedWalletWaitLogged = true;
                        var diagnosticType = FindGameType("PlayerWallet");
                        var found = diagnosticType == null ? Array.Empty<UnityEngine.Object>() : UnityEngine.Resources.FindObjectsOfTypeAll(diagnosticType);
                        var scene = SceneManager.GetActiveScene();
                        var diagnostics = new List<string>();
                        foreach (var name in new[] { "PlayerWallet", "PlayerInteractor", "Hands", "AbilityController", "UpgradeController", "Level", "MainMenu" })
                        {
                            var type = FindGameType(name);
                            var instances = type == null ? Array.Empty<UnityEngine.Object>() : UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None);
                            diagnostics.Add(name + "=" + instances.Length + (instances.Length > 0 ? "[" + string.Join(",", instances.Take(3).Select(item => item.name + "@" + (item is Component component ? component.gameObject.scene.name : "static"))) + "]" : string.Empty));
                        }
                        var levelDescription = _automatedTestLevel == null ? "none" : _automatedTestLevel.GetType().FullName + ":" + (_automatedTestLevel is Component levelComponent ? levelComponent.gameObject.name + "@" + levelComponent.gameObject.scene.name : "non-component");
                        TrainerPlugin.Log.LogWarning("Waiting for PlayerWallet in isolated level; typeFound=" + (diagnosticType != null) + ", resources=" + found.Length + ", activeScene=" + scene.name + "#" + scene.buildIndex + ", slot=" + GetSaveSlot() + ", level=" + levelDescription + ", objects=" + string.Join("; ", diagnostics));
                    }
                    return;
                }
                var objects = FindLevelObjects().ToDictionary(item => item.Owner.GetType().Name, item => item.Owner, StringComparer.Ordinal);
                VerifyExpandedTables(objects);
                VerifyApprovedPriceAndRewardPlan(objects);
                PurchaseToLevel(objects, "ItemPullAbility", 8, true);
                PurchaseToLevel(objects, "ShelfScatterAbility", 6, true);
                PurchaseToLevel(objects, "HandCapacityUpgrade", 16, false);
                PurchaseToLevel(objects, "RepeatInteractUpgrade", 7, false);
                VerifyCurrentGrades(objects, 8, 6, 16, 7);
                var walletType = FindGameType("PlayerWallet") ?? throw new InvalidOperationException("PlayerWallet was not found");
                var wallet = GetPlayerWalletInstance() ?? throw new InvalidOperationException("PlayerWallet.Instance is not ready");
                walletType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public)?.Invoke(wallet, new object[] { 1000000 });
                WriteAutomatedSettings(maxAll: true, expansions: true, revision: _automatedTestRevisionBase + 1);
                _automatedTestStage = 2;
                return;
            }

            if (_automatedTestStage == 2)
            {
                if (TrainerPlugin.AppliedRevision < _automatedTestRevisionBase + 1) return;
                var objects = FindLevelObjects().ToDictionary(item => item.Owner.GetType().Name, item => item.Owner, StringComparer.Ordinal);
                VerifyExpandedTables(objects);
                VerifyCurrentGrades(objects, 10, 10, 20, 10);
                var autoSave = SafeMethods(_automatedTestLevel!.GetType()).FirstOrDefault(method => method.Name == "AutoSave" && method.GetParameters().Length == 0);
                if (autoSave == null) throw new MissingMethodException("Level.AutoSave()");
                _automatedSaveSequenceBaseline = CompletedSaveSequence.TryGetValue(AutomatedTestSlot, out var sequence) ? sequence : 0;
                autoSave.Invoke(_automatedTestLevel, null);
                _automatedTestStage = 3;
                TrainerPlugin.Log.LogInfo("Running the game's own AutoSave with MaxAll enabled after genuine extension purchases.");
                return;
            }

            if (_automatedTestStage == 3)
            {
                if (!CompletedSaveSequence.TryGetValue(AutomatedTestSlot, out var sequence) || sequence <= _automatedSaveSequenceBaseline) return;
                var progress = JsonFile.Read<SlotProgress>(ProgressPath(AutomatedTestSlot));
                if (progress == null || !string.Equals(progress.SaveFingerprint, GetSaveFingerprint(AutomatedTestSlot), StringComparison.OrdinalIgnoreCase)) return;
                VerifyCommittedProgressAndVanillaSave();
                StartReadingSavedModel();
                _automatedTestStage = 4;
                return;
            }

            if (_automatedTestStage == 4)
            {
                if (_automatedSaveLoadTask == null) return;
                if (!_automatedSaveLoadTask.IsCompleted) return;
                if (_automatedSaveLoadTask.IsFaulted) throw new IOException("The game's own SaveSystem.Load failed", _automatedSaveLoadTask.Exception);
                _automatedSaveModel = ReadTaskResult(_automatedSaveLoadTask);
                VerifyVanillaSaveGrades(_automatedSaveModel!);
                VerifyPersistedScoredTypeCount(_automatedSaveModel!);
                WriteAutomatedSettings(maxAll: false, expansions: false, revision: _automatedTestRevisionBase + 2);
                _automatedTestStage = 5;
                return;
            }

            if (_automatedTestStage == 5)
            {
                if (TrainerPlugin.AppliedRevision < _automatedTestRevisionBase + 2) return;
                var objects = FindLevelObjects().ToDictionary(item => item.Owner.GetType().Name, item => item.Owner, StringComparer.Ordinal);
                VerifyVanillaTables(objects);
                VerifyCurrentGrades(objects, 6, 3, 7, 5);
                WriteAutomatedSettings(maxAll: false, expansions: true, revision: _automatedTestRevisionBase + 3);
                _automatedTestStage = 6;
                return;
            }

            if (_automatedTestStage == 6)
            {
                if (TrainerPlugin.AppliedRevision < _automatedTestRevisionBase + 3) return;
                var objects = FindLevelObjects().ToDictionary(item => item.Owner.GetType().Name, item => item.Owner, StringComparer.Ordinal);
                VerifyExpandedTables(objects);
                VerifyCurrentGrades(objects, 8, 6, 16, 7);
                WriteAutomatedGameTestReport(true, "new game, all expanded tables, genuine purchases, temporary max-all save clamp, real save reread, and expansion off/on purchase retention passed");
                _automatedTestStage = 7;
            }
        }

        private static void ProcessAutomatedContinueTest()
        {
            if (_automatedTestStage == 0)
            {
                var menu = FindActiveGameComponent("MainMenu");
                if (menu == null) return;
                InvokeMenuStart(menu, "OnContinueClicked");
                _automatedTestStage = 1;
                TrainerPlugin.Log.LogInfo("Loading the isolated save through Bunker.Core.MainMenu.OnContinueClicked.");
                return;
            }

            if (_automatedTestStage == 1)
            {
                var level = FindActiveGameComponent("Level");
                if (level == null || GetSaveSlot() != AutomatedTestSlot) return;
                _automatedTestLevel = level;
                _automatedTestLevelLoadedAt = Time.realtimeSinceStartup;
                _automatedTestStage = 2;
                return;
            }

            if (_automatedTestStage == 2 && Time.realtimeSinceStartup - _automatedTestLevelLoadedAt >= 3f)
            {
                if (TrainerPlugin.Status.State != "extensionRecovered")
                    throw new InvalidDataException("A corrupt extension sidecar was not reported as recovered from a fingerprint-matched backup; state=" + TrainerPlugin.Status.State);
                var objects = FindLevelObjects().ToDictionary(item => item.Owner.GetType().Name, item => item.Owner, StringComparer.Ordinal);
                VerifyExpandedTables(objects);
                VerifyCurrentGrades(objects, 8, 6, 16, 7);
                WriteAutomatedSettings(maxAll: false, expansions: true, revision: _automatedTestRevisionBase + 1, infiniteStars: true, noCooldown: true);
                _automatedTestStage = 3;
                return;
            }

            if (_automatedTestStage == 3 && TrainerPlugin.AppliedRevision >= _automatedTestRevisionBase + 1)
            {
                VerifyInfiniteStarsNoCooldownAndReward();
                BeginAutomatedRangePickupTest();
                _automatedTestStage = 4;
                _rangeTestStartedAt = Time.realtimeSinceStartup;
                return;
            }

            if (_automatedTestStage == 4)
            {
                if (IsPicked(_rangeTestPositiveNeighbor!))
                {
                    var count = ReadInt(_rangeTestHands!, "Count", "_count");
                    if (count < _rangeTestInitialHandsCount + 3)
                        throw new InvalidOperationException("The original click and range pickup did not both increase the real Hands.Count; expected at least " + (_rangeTestInitialHandsCount + 3) + ", got " + count);
                    if (IsRangeBatch)
                    {
                        if (Time.realtimeSinceStartup - _rangeTestStartedAt > 10f)
                            throw new TimeoutException("The real range pickup queue did not drain after collecting the validated neighboring item");
                        return;
                    }
                    TrainerPlugin.Log.LogInfo("Range pickup passed: Carry 9 clicked one valid ground item without collecting its neighbor; Carry 11 clicked the next item and the real range batch collected its nearby neighbor. Hands.Count=" + count + ".");
                    BeginAutomatedOverflowSaveTest();
                    _automatedTestStage = 5;
                    return;
                }
                if (Time.realtimeSinceStartup - _rangeTestStartedAt > 5f)
                    throw new TimeoutException("The original Carry 11 click succeeded but its nearby valid ground item was not picked by the next-frame range batch");
            }

            if (_automatedTestStage == 5 && TrainerPlugin.AppliedRevision >= _automatedTestRevisionBase + 2)
            {
                var interactor = FindActiveGameComponent("PlayerInteractor") ?? throw new InvalidOperationException("PlayerInteractor was lost before overflow save validation");
                var hands = FindNestedByName(interactor, "Hands", 2) ?? throw new InvalidOperationException("Hands was lost before overflow save validation");
                var count = ReadInt(hands, "Count", "_count");
                var capacity = ReadInt(hands, "MaxCount", "_maxCount");
                if (GetCurrentLevel(_rangeTestCarryUpgrade!) != 7 || capacity != 75 || count < 76)
                    throw new InvalidOperationException("Turning Carry expansion off did not retain at least 76 held items while restoring the original level-7 capacity; level=" + GetCurrentLevel(_rangeTestCarryUpgrade!) + ", count=" + count + ", capacity=" + capacity);
                var heldItems = GetHandsItems(hands);
                var heldData = heldItems.Select(item => ReadMember(item, "_data", "Data")).FirstOrDefault(value => value != null);
                if (heldData == null) throw new InvalidDataException("Could not read ItemData from the actual Hands.Items collection");
                if (HandsCanAdd(hands, heldData))
                    throw new InvalidOperationException("The original Hands.CanAdd(ItemData) accepted another matching item while Hands.Count " + count + " exceeded restored capacity 75");
                _overflowSaveSequenceBaseline = CompletedSaveSequence.TryGetValue(AutomatedTestSlot, out var sequence) ? sequence : 0;
                var autoSave = SafeMethods(_overflowTestLevel!.GetType()).FirstOrDefault(method => method.Name == "AutoSave" && method.GetParameters().Length == 0) ?? throw new MissingMethodException("Level.AutoSave()");
                autoSave.Invoke(_overflowTestLevel, null);
                TrainerPlugin.Log.LogInfo("Overflow save started with Carry expansion off; Hands.Count=" + count + ", MaxCount=75, and a same-data ordinary CanAdd probe was rejected.");
                _automatedTestStage = 6;
                return;
            }

            if (_automatedTestStage == 6)
            {
                if (!CompletedSaveSequence.TryGetValue(AutomatedTestSlot, out var sequence) || sequence <= _overflowSaveSequenceBaseline) return;
                var progress = JsonFile.Read<SlotProgress>(ProgressPath(AutomatedTestSlot));
                if (progress == null || !string.Equals(progress.SaveFingerprint, GetSaveFingerprint(AutomatedTestSlot), StringComparison.OrdinalIgnoreCase)) return;
                VerifyCommittedProgressAndVanillaSave();
                StartReadingSavedModel();
                _automatedTestStage = 7;
                return;
            }

            if (_automatedTestStage == 7)
            {
                if (_automatedSaveLoadTask == null || !_automatedSaveLoadTask.IsCompleted) return;
                if (_automatedSaveLoadTask.IsFaulted) throw new IOException("SaveSystem.Load failed after the overflow save", _automatedSaveLoadTask.Exception);
                var model = ReadTaskResult(_automatedSaveLoadTask) ?? throw new InvalidDataException("SaveSystem.Load returned no model after overflow save");
                _overflowSavedHandItems = CountEnumerable(ReadMember(model, "HandItems", "_handItems"));
                var expectedHandsCount = ReadInt(FindNestedByName(FindActiveGameComponent("PlayerInteractor")!, "Hands", 2)!, "Count", "_count");
                _overflowExpectedHandsCount = expectedHandsCount;
                File.WriteAllText(Path.Combine(Paths.GameRootPath, "trainer-overflow.expected.txt"), expectedHandsCount + Environment.NewLine + _overflowSavedHandItems, new System.Text.UTF8Encoding(false));
                if (expectedHandsCount < 76) throw new InvalidDataException("The isolated held count unexpectedly fell below the over-capacity test threshold: " + expectedHandsCount);
                TrainerPlugin.Log.LogInfo("Overflow save verified through SaveSystem.Load: Hands.Count=" + expectedHandsCount + ", serialized HandItems=" + _overflowSavedHandItems + ", vanilla save grade remained within level 7.");
                WriteAutomatedGameTestReport(true, "corrupt-sidecar recovery, expanded grades and purchases, temporary MaxAll save clamp, infinite stars, no cooldown, More Stars, and real Carry 9/11 range pickup passed; " + _overflowSavedHandItems + " serialized HandItems and Hands.Count " + _overflowExpectedHandsCount + " were saved with Carry expansion off at vanilla capacity 75, while ordinary CanAdd rejected a new item");
                return;
            }
        }

        private static void BeginAutomatedOverflowSaveTest()
        {
            const int targetCount = 76;
            var interactor = FindActiveGameComponent("PlayerInteractor") ?? throw new InvalidOperationException("PlayerInteractor is unavailable for overflow test");
            var hands = FindNestedByName(interactor, "Hands", 2) ?? throw new InvalidOperationException("Hands is unavailable for overflow test");
            var carryUpgrade = _rangeTestCarryUpgrade ?? throw new InvalidOperationException("HandCapacityUpgrade was not retained from range test");
            if (GetCurrentLevel(carryUpgrade) < 11) throw new InvalidOperationException("Expanded carry level 11 is required to build the isolated overflow inventory");
            var held = GetHandsItems(hands);
            var heldData = held.Select(item => ReadMember(item, "_data", "Data")).FirstOrDefault(value => value != null)
                ?? ReadMember(_rangeTestControlItem!, "_data", "Data")
                ?? throw new InvalidOperationException("Could not get an ItemData reference for the overflow inventory");
            var addItems = FindItemsMatchingData(heldData, interactor);
            var required = Math.Max(0, targetCount - ReadInt(hands, "Count", "_count"));
            if (addItems.Length < required)
                throw new InvalidOperationException("Not enough same-data world items to build a legal 76-item hand inventory: found " + addItems.Length + ", need " + required);
            var adopt = SafeMethods(hands.GetType()).FirstOrDefault(method => method.Name == "TryAdopt" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType.IsInstanceOfType(addItems[0]))
                ?? throw new MissingMethodException(hands.GetType().FullName, "TryAdopt(PickableItem)");
            var before = ReadInt(hands, "Count", "_count");
            foreach (var item in addItems)
            {
                if (ReadInt(hands, "Count", "_count") >= targetCount) break;
                if (IsPicked(item) || !CanInteract(item, interactor, GetTransform(item)!.position)) continue;
                adopt.Invoke(hands, new[] { item });
            }
            var after = ReadInt(hands, "Count", "_count");
            if (after < targetCount)
                throw new InvalidOperationException("The game's real Hands.TryAdopt could not legally fill 76 same-data items; started " + before + ", ended " + after);
            var capacity = ReadInt(hands, "MaxCount", "_maxCount");
            if (after > capacity) throw new InvalidOperationException("The game's legal Hands.TryAdopt exceeded its live expanded capacity; count=" + after + ", capacity=" + capacity);

            WriteAutomatedSettings(maxAll: false, expansions: false, revision: _automatedTestRevisionBase + 2);
            _overflowTestLevel = _automatedTestLevel;
            TrainerPlugin.Log.LogInfo("Built an isolated legal Hands inventory above vanilla capacity using real Hands.TryAdopt with expanded Carry 11; Hands.Count=" + after + ", MaxCount=" + capacity + ". Now disabling expansion before saving and restarting.");
        }

        private static void ProcessAutomatedOverflowReloadTest()
        {
            if (_automatedTestStage == 0)
            {
                var menu = FindActiveGameComponent("MainMenu");
                if (menu == null) return;
                InvokeMenuStart(menu, "OnContinueClicked");
                _automatedTestStage = 1;
                TrainerPlugin.Log.LogInfo("Restart verification: continuing isolated slot 666 after the over-capacity vanilla-compatible inventory save.");
                return;
            }
            if (_automatedTestStage == 1)
            {
                var level = FindActiveGameComponent("Level");
                if (level == null || GetSaveSlot() != AutomatedTestSlot) return;
                _automatedTestLevel = level;
                _automatedTestLevelLoadedAt = Time.realtimeSinceStartup;
                _automatedTestStage = 2;
                return;
            }
            if (_automatedTestStage == 2 && Time.realtimeSinceStartup - _automatedTestLevelLoadedAt >= 4f)
            {
                var objects = FindLevelObjects().ToDictionary(item => item.Owner.GetType().Name, item => item.Owner, StringComparer.Ordinal);
                VerifyVanillaTables(objects);
                VerifyCurrentGrades(objects, 6, 3, 7, 5);
                var interactor = FindActiveGameComponent("PlayerInteractor") ?? throw new InvalidOperationException("PlayerInteractor was unavailable after continuing overflow save");
                var hands = FindNestedByName(interactor, "Hands", 2) ?? throw new InvalidOperationException("Hands was unavailable after continuing overflow save");
                var count = ReadInt(hands, "Count", "_count");
                var capacity = ReadInt(hands, "MaxCount", "_maxCount");
                var expectedPath = Path.Combine(Paths.GameRootPath, "trainer-overflow.expected.txt");
                if (!File.Exists(expectedPath)) throw new FileNotFoundException("Phase 2 did not write the isolated overflow expectations", expectedPath);
                var expected = File.ReadAllLines(expectedPath);
                if (expected.Length < 2 || !int.TryParse(expected[0], out var expectedCount) || !int.TryParse(expected[1], out var expectedItemRecords))
                    throw new InvalidDataException("Isolated overflow expectation file was invalid");
                var heldItemCount = GetHandsItems(hands).Length;
                if (count != expectedCount || heldItemCount != expectedItemRecords || count < 76 || capacity != 75)
                    throw new InvalidDataException("Restart/Continue did not retain every serialized hand item above original capacity 75; Hands.Count=" + count + "/" + expectedCount + ", item records=" + heldItemCount + "/" + expectedItemRecords + ", MaxCount=" + capacity);
                var heldItems = GetHandsItems(hands);
                var heldData = heldItems.Select(item => ReadMember(item, "_data", "Data")).FirstOrDefault(value => value != null);
                if (heldData == null) throw new InvalidDataException("Could not read ItemData from the loaded Hands.Items collection");
                if (HandsCanAdd(hands, heldData))
                    throw new InvalidOperationException("The original Hands.CanAdd(ItemData) allowed an extra item after restoring Hands.Count " + count + " into original capacity 75");
                TrainerPlugin.Log.LogInfo("Overflow Continue passed: all " + heldItemCount + " serialized HandItems were adopted with Hands.Count=" + count + " while MaxCount remained 75, and ordinary CanInteract rejected a same-data new item.");
                _overflowRestoreRevision = TrainerPlugin.Settings.Revision + 1;
                WriteTestRestoreRequest(_overflowRestoreRevision);
                _automatedTestStage = 3;
                return;
            }
            if (_automatedTestStage == 3 && TrainerPlugin.AppliedRevision >= _overflowRestoreRevision)
            {
                if (TrainerPlugin.Status.RestoreSaved) throw new InvalidOperationException("The test's over-capacity state unexpectedly received a successful restore receipt before saving");
                _overflowSaveSequenceBaseline = CompletedSaveSequence.TryGetValue(AutomatedTestSlot, out var sequence) ? sequence : 0;
                var autoSave = SafeMethods(_automatedTestLevel!.GetType()).FirstOrDefault(method => method.Name == "AutoSave" && method.GetParameters().Length == 0) ?? throw new MissingMethodException("Level.AutoSave()");
                autoSave.Invoke(_automatedTestLevel, null);
                _automatedTestStage = 4;
                TrainerPlugin.Log.LogInfo("Started a restore-request save with Hands.Count above original capacity to verify that it cannot receive an uninstall-ready receipt.");
                return;
            }
            if (_automatedTestStage == 4)
            {
                if (!CompletedSaveSequence.TryGetValue(AutomatedTestSlot, out var sequence) || sequence <= _overflowSaveSequenceBaseline) return;
                if (TrainerPlugin.Status.RestoreSaved || TrainerPlugin.Status.RestoreRevision == TrainerPlugin.Settings.Revision || !TrainerPlugin.Status.Message.Contains("超过原版容量", StringComparison.Ordinal)) return;
                TrainerPlugin.Log.LogInfo("Over-capacity restore receipt correctly refused: " + TrainerPlugin.Status.Message + ".");
                WriteAutomatedGameTestReport(true, "Restart/Continue preserved every saved HandItem above original capacity 75, ordinary CanInteract rejected further pickups, and the current RestoreRequested save was refused an uninstall-ready receipt until Hands.Count is reduced");
            }
        }

        private static void WriteTestRestoreRequest(long revision)
        {
            var settings = new ModSettings
            {
                Schema = 1,
                Revision = revision,
                RestoreRequested = true,
                ExpandCarry = false,
                ExpandAutoPickup = false,
                ExpandAutoPlace = false,
                ExpandContinuousPlace = false,
                MoreStars = false,
                InfiniteStars = false,
                NoCooldown = false,
                MaxAll = false
            };
            JsonFile.WriteAtomic(TrainerPlugin.SettingsPath, settings);
        }

        private static object[] FindItemsMatchingData(object? data, object interactor)
        {
            if (data == null) return Array.Empty<object>();
            var itemType = FindGameType("PickableItem");
            if (itemType == null) return Array.Empty<object>();
            var scene = (interactor as Component)?.gameObject.scene ?? SceneManager.GetActiveScene();
            return UnityEngine.Object.FindObjectsByType(itemType, FindObjectsSortMode.None)
                .Where(candidate => IsCandidateAlive(candidate) && candidate is Component component && component.gameObject.scene == scene)
                .Where(candidate => !IsPicked(candidate) && !IsInsideShelf(GetTransform(candidate)!))
                .Where(candidate => ReferenceEquals(ReadMember(candidate, "_data", "Data"), data))
                .ToArray();
        }

        private static object? FindAvailableItemWithData(object? data, object interactor)
        {
            return FindItemsMatchingData(data, interactor).FirstOrDefault(candidate => CanInteract(candidate, interactor, GetTransform(candidate)!.position));
        }

        private static bool HandsCanAdd(object hands, object data)
        {
            var method = SafeMethods(hands.GetType()).FirstOrDefault(candidate => candidate.Name == "CanAdd" && candidate.GetParameters().Length == 1 && candidate.GetParameters()[0].ParameterType.IsInstanceOfType(data));
            if (method == null) throw new MissingMethodException(hands.GetType().FullName, "CanAdd(ItemData)");
            return method.Invoke(hands, new[] { data }) is bool allowed && allowed;
        }

        private static object[] GetHandsItems(object hands)
        {
            var pickableType = FindGameType("PickableItem");
            if (pickableType == null) return Array.Empty<object>();
            foreach (var field in Fields(hands.GetType()))
            {
                var value = SafeGet(field, hands);
                if (!(value is IEnumerable values) || value is string) continue;
                var candidates = values.Cast<object?>().Where(item => item != null && pickableType.IsInstanceOfType(item)).Cast<object>().ToArray();
                if (candidates.Length > 0) return candidates;
            }
            return Array.Empty<object>();
        }

        private static int CountEnumerable(object? value)
        {
            if (!(value is IEnumerable sequence) || value is string) return 0;
            return sequence.Cast<object?>().Count(item => item != null);
        }

        private static void BeginAutomatedRangePickupTest()
        {
            var interactor = FindActiveGameComponent("PlayerInteractor") ?? throw new InvalidOperationException("PlayerInteractor was not available for range pickup validation");
            var playerTransform = GetTransform(interactor) ?? throw new InvalidOperationException("PlayerInteractor has no transform");
            var hands = FindNestedByName(interactor, "Hands", 2) ?? throw new InvalidOperationException("PlayerInteractor Hands was not available");
            var carryUpgrade = FindLevelObjects().Select(entry => entry.Owner).FirstOrDefault(owner => owner.GetType().Name == "HandCapacityUpgrade") ?? throw new InvalidOperationException("HandCapacityUpgrade was not available for range pickup validation");
            var itemType = FindGameType("PickableItem") ?? throw new MissingMemberException("Bunker.Items.PickableItem");
            var movement = ReadMember(interactor, "_movement", "Movement");
            var lookingRay = movement == null ? null : ReadMember(movement, "LookingRay", "_lookingRay");
            if (!(lookingRay is Ray ray)) throw new InvalidOperationException("PlayerInteractor movement did not expose its current LookingRay");
            var forward = Vector3.ProjectOnPlane(ray.direction, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) throw new InvalidOperationException("Player facing direction was not usable for placing the isolated ground-item pair");
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var interactDistance = GetInteractDistance(interactor);
            var layerMask = GetInteractableMask(interactor);
            var scene = (interactor as Component)?.gameObject.scene ?? SceneManager.GetActiveScene();
            var candidates = UnityEngine.Object.FindObjectsByType(itemType, FindObjectsSortMode.None)
                .Where(candidate => IsCandidateAlive(candidate) && candidate is Component component && component.gameObject.scene == scene)
                .Where(candidate => !IsPicked(candidate) && !IsInsideShelf(GetTransform(candidate)!))
                .Where(candidate => !GetTransform(candidate)!.IsChildOf(playerTransform) && IsLayerInteractable(GetTransform(candidate)!, layerMask))
                .Where(candidate => CanInteract(candidate, interactor, GetTransform(candidate)!.position))
                .GroupBy(candidate => ReadMember(candidate, "_data", "Data") ?? candidate.GetType(), ReferenceComparer.Instance)
                .Select(group => group.Take(3).ToArray())
                .FirstOrDefault(group => group.Length == 3);
            if (candidates == null) throw new InvalidOperationException("The isolated scene had no three unpicked, same-data, non-shelf ground items accepted by Hands.CanAdd on the interactable layer");

            var anchor = playerTransform.position + forward * 1.35f;
            if (!Physics.Raycast(anchor + Vector3.up * 3f, Vector3.down, out var ground, 8f, ~0, QueryTriggerInteraction.Ignore) || ground.normal.y < 0.55f)
                throw new InvalidOperationException("No walkable ground surface was found in front of the isolated player for the range pickup check");
            var floorY = ground.point.y;
            var offsets = candidates.Select(candidate => GetGroundPivotOffset(candidate)).ToArray();
            var positions = new[]
            {
                new Vector3(anchor.x, floorY + offsets[0], anchor.z),
                new Vector3(anchor.x, floorY + offsets[1], anchor.z) + right * 0.45f,
                new Vector3(anchor.x, floorY + offsets[2], anchor.z) + right * 0.90f
            };
            for (var i = 0; i < candidates.Length; i++) GetTransform(candidates[i])!.position = positions[i];
            Physics.SyncTransforms();

            var origin = GetViewOrigin(interactor, playerTransform);
            for (var i = 0; i < candidates.Length; i++)
            {
                var transform = GetTransform(candidates[i])!;
                var distance = Vector3.Distance(origin, transform.position);
                if (distance > interactDistance || !HasLineOfSight(origin, transform, distance, layerMask, GetTriggerInteraction(interactor)) || !CanInteract(candidates[i], interactor, transform.position))
                    throw new InvalidOperationException("A temporarily positioned same-floor test item failed the game's real distance, line-of-sight, layer, or CanInteract checks at index " + i + "; distance=" + distance + ", allowed=" + interactDistance);
            }

            var initialCount = ReadInt(hands, "Count", "_count");
            var maxAtNine = initialCount;
            SetLevel(carryUpgrade, 9);
            maxAtNine = ReadInt(hands, "MaxCount", "_maxCount");
            if (maxAtNine <= initialCount + 2) throw new InvalidOperationException("Carry 9 did not leave enough real Hands capacity to validate an original click and compare its neighbor");
            if (!InvokeOriginalItemInteraction(candidates[0], interactor) || !IsPicked(candidates[0]))
                throw new InvalidOperationException("The original PickableItem.TryInteract click was not accepted at Carry 9");
            if (IsPicked(candidates[1])) throw new InvalidOperationException("Carry 9 incorrectly collected the nearby ground item");

            SetLevel(carryUpgrade, 11);
            if (!InvokeOriginalItemInteraction(candidates[1], interactor) || !IsPicked(candidates[1]))
                throw new InvalidOperationException("The original PickableItem.TryInteract click was not accepted at Carry 11");
            if (IsPicked(candidates[2])) throw new InvalidOperationException("The nearby Carry 11 item was picked synchronously instead of by the normal next-frame range batch");

            _rangeTestInteractor = interactor;
            _rangeTestHands = hands;
            _rangeTestCarryUpgrade = carryUpgrade;
            _rangeTestControlItem = candidates[0];
            _rangeTestNegativeNeighbor = candidates[1];
            _rangeTestPositiveClick = candidates[1];
            _rangeTestPositiveNeighbor = candidates[2];
            _rangeTestInitialHandsCount = initialCount;
            TrainerPlugin.Log.LogInfo("Range pickup probe started with three same-data ground items. Carry 9 original click passed without a neighbor pickup; Carry 11 original click passed and awaited the runtime batch. Player=" + playerTransform.position + ", item0=" + positions[0] + ", item1=" + positions[1] + ", item2=" + positions[2] + ", distance=" + interactDistance + ".");
        }

        private static float GetGroundPivotOffset(object item)
        {
            var transform = GetTransform(item)!;
            var colliders = transform.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0) return 0.05f;
            var lowest = colliders.Where(collider => collider != null).Select(collider => collider.bounds.min.y).DefaultIfEmpty(transform.position.y).Min();
            return Math.Max(0.02f, transform.position.y - lowest + 0.02f);
        }

        private static bool InvokeOriginalItemInteraction(object item, object interactor)
        {
            var method = SafeMethods(item.GetType()).FirstOrDefault(candidate => candidate.Name == "TryInteract" && candidate.GetParameters().Length == 2 && candidate.GetParameters()[0].ParameterType.IsInstanceOfType(interactor) && candidate.GetParameters()[1].ParameterType == typeof(Vector3));
            if (method == null) throw new MissingMethodException(item.GetType().FullName, "TryInteract(IInteractor, Vector3)");
            var transform = GetTransform(item) ?? throw new InvalidOperationException("PickableItem transform was unavailable");
            return method.Invoke(item, new object[] { interactor, transform.position }) is bool accepted && accepted;
        }

        private static void InvokeMenuStart(object menu, string methodName)
        {
            var method = SafeMethods(menu.GetType()).FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == 1 && candidate.GetParameters()[0].ParameterType == typeof(int));
            if (method == null) throw new MissingMethodException(menu.GetType().FullName, methodName + "(Int32)");
            method.Invoke(menu, new object[] { AutomatedTestSlot });
        }

        private static object? FindActiveGameComponent(string typeName)
        {
            var type = FindGameType(typeName);
            if (type == null) return null;
            var objects = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None);
            foreach (var candidate in objects)
                if (candidate is Component component && component != null && component.gameObject.activeInHierarchy) return candidate;
            return null;
        }

        private static void PurchaseToLevel(Dictionary<string, object> objects, string typeName, int target, bool ability)
        {
            if (!objects.TryGetValue(typeName, out var owner)) throw new InvalidOperationException("Controller level object missing: " + typeName);
            var walletType = FindGameType("PlayerWallet") ?? throw new InvalidOperationException("PlayerWallet was not found");
            var wallet = GetPlayerWalletInstance() ?? throw new InvalidOperationException("PlayerWallet.Instance is not ready");
            walletType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public)?.Invoke(wallet, new object[] { 1000000 });
            var methodName = ability ? "TryLevelUp" : "TryBuy";
            var purchase = SafeMethods(owner.GetType()).FirstOrDefault(method => method.Name == methodName && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType.IsInstanceOfType(wallet));
            if (purchase == null) throw new MissingMethodException(owner.GetType().FullName, methodName);
            while (GetCurrentLevel(owner) < target)
            {
                if (!(purchase.Invoke(owner, new[] { wallet }) is bool bought) || !bought)
                    throw new InvalidOperationException("Game purchase was rejected for " + typeName + " at grade " + GetCurrentLevel(owner));
            }
        }

        private static void VerifyExpandedTables(Dictionary<string, object> objects)
        {
            VerifyListCount(objects, "ItemPullAbility", "_levels", 10);
            VerifyListCount(objects, "ShelfScatterAbility", "_levels", 10);
            VerifyListCount(objects, "HandCapacityUpgrade", "_steps", ExpandedCarryMaximum);
            VerifyListCount(objects, "RepeatInteractUpgrade", "_steps", 10);
        }

        private static void VerifyVanillaTables(Dictionary<string, object> objects)
        {
            VerifyListCount(objects, "ItemPullAbility", "_levels", 6);
            VerifyListCount(objects, "ShelfScatterAbility", "_levels", 3);
            VerifyListCount(objects, "HandCapacityUpgrade", "_steps", 7);
            VerifyListCount(objects, "RepeatInteractUpgrade", "_steps", 5);
        }

        private static void VerifyListCount(Dictionary<string, object> objects, string typeName, string member, int expected)
        {
            if (!objects.TryGetValue(typeName, out var owner) || !(ReadMember(owner, member) is IList list) || list.Count != expected)
                throw new InvalidOperationException(typeName + " " + member + " count did not equal " + expected);
        }

        private static void VerifyApprovedPriceAndRewardPlan(Dictionary<string, object> objects)
        {
            VerifyOriginalPrices(objects, "HandCapacityUpgrade", "_steps", CarryOriginalCosts);
            VerifyOriginalPrices(objects, "ItemPullAbility", "_levels", PullOriginalCosts);
            VerifyOriginalPrices(objects, "ShelfScatterAbility", "_levels", ScatterOriginalCosts);
            VerifyOriginalPrices(objects, "RepeatInteractUpgrade", "_steps", RepeatOriginalCosts);
            VerifyOriginalPriceTotal(objects, "ItemFinderAbility", 150);
            VerifyOriginalPriceTotal(objects, "ShelfFinderAbility", 150);
            VerifyOriginalPriceTotal(objects, "HandGroupingUpgrade", 10);
            VerifyOriginalPriceTotal(objects, "MoveSpeedUpgrade", 35);
            VerifyOriginalPriceTotal(objects, "JumpHeightUpgrade", 35);
            VerifyPrices(objects, "HandCapacityUpgrade", "_steps", 7, CarryExpansionCosts);
            VerifyPrices(objects, "ItemPullAbility", "_levels", 6, PullExpansionCosts);
            VerifyPrices(objects, "ShelfScatterAbility", "_levels", 3, ScatterExpansionCosts);
            VerifyPrices(objects, "RepeatInteractUpgrade", "_steps", 5, RepeatExpansionCosts);

            var expandedTotal = CarryExpansionCosts.Sum() + PullExpansionCosts.Sum() + ScatterExpansionCosts.Sum() + RepeatExpansionCosts.Sum();
            var originalTotal = SumOriginalPrices(objects);
            if (originalTotal != 1280 || expandedTotal != 2796 || originalTotal + expandedTotal != 4076)
                throw new InvalidDataException("Expanded purchase budget did not match the approved 2,796 new / 4,076 total star plan");
            var scoringType = FindGameType("ShelfScoring") ?? throw new MissingMemberException("Bunker.Scoring.ShelfScoring");
            var scorers = UnityEngine.Object.FindObjectsByType(scoringType, FindObjectsSortMode.None);
            var typeCounts = scorers.Select(scorer => ReadInt(scorer, "TypesCount", "_typesCount")).Distinct().ToArray();
            if (typeCounts.Length != 1 || typeCounts[0] != 136)
                throw new InvalidDataException("Expected the live ShelfScoring.TypesCount to be 136, found " + (typeCounts.Length == 1 ? typeCounts[0].ToString() : string.Join(",", typeCounts)));
            var rewardBudget = Enumerable.Range(0, typeCounts[0]).Sum(GetProgressiveReward);
            if (rewardBudget != 4076)
                throw new InvalidDataException("The progressive reward schedule for " + typeCounts[0] + " live item types totaled " + rewardBudget + " instead of 4,076");
            VerifyProgressiveRewardBoundaries();
            TrainerPlugin.Log.LogInfo("Approved price/reward plan passed: original total " + originalTotal + ", new levels " + expandedTotal + ", campaign total " + (originalTotal + expandedTotal) + "; live ShelfScoring.TypesCount=" + typeCounts[0] + ", progressive reward total=" + rewardBudget + ", reward steps 20/21/44/44 at scored-type counts 67/68/91/92.");
        }

        private static void VerifyOriginalPrices(Dictionary<string, object> objects, string typeName, string listName, int[] expected)
        {
            if (!objects.TryGetValue(typeName, out var owner) || !(ReadMember(owner, listName) is IList list) || list.Count < expected.Length)
                throw new InvalidDataException("Could not inspect original price table for " + typeName);
            for (var i = 0; i < expected.Length; i++)
                if (ReadInt(list[i]!, "Cost", "_cost") != expected[i])
                    throw new InvalidDataException(typeName + " original grade " + (i + 1) + " cost changed");
        }

        private static void VerifyOriginalPriceTotal(Dictionary<string, object> objects, string typeName, int expected)
        {
            if (!objects.TryGetValue(typeName, out var owner)) throw new InvalidDataException("Missing original upgrade " + typeName);
            var list = ReadMember(owner, "_levels", "_steps") as IList ?? throw new InvalidDataException("Missing original price list for " + typeName);
            if (list.Cast<object>().Sum(level => ReadInt(level, "Cost", "_cost")) != expected)
                throw new InvalidDataException(typeName + " original total price changed");
        }

        private static int SumOriginalPrices(Dictionary<string, object> objects)
        {
            var total = 0;
            foreach (var owner in objects.Values)
            {
                var list = ReadMember(owner, "_levels", "_steps") as IList;
                if (list == null) continue;
                var key = owner.GetType().Name;
                var originals = OriginalLevelLists.TryGetValue(owner, out var originalList) ? originalList : list.Cast<object>().ToList();
                if (originals.Count == 0) continue;
                total += originals.Sum(level => ReadInt(level, "Cost", "_cost"));
            }
            return total;
        }

        private static void VerifyPrices(Dictionary<string, object> objects, string typeName, string listName, int originalCount, int[] expected)
        {
            if (!objects.TryGetValue(typeName, out var owner) || !(ReadMember(owner, listName) is IList list) || list.Count != originalCount + expected.Length)
                throw new InvalidDataException("Price validation found an unexpected table size for " + typeName);
            for (var i = 0; i < expected.Length; i++)
            {
                var level = list[originalCount + i];
                var cost = ReadInt(level!, "Cost", "_cost");
                if (cost != expected[i]) throw new InvalidDataException(typeName + " grade " + (originalCount + i + 1) + " cost expected " + expected[i] + " but was " + cost);
                if (i > 0 && expected[i] <= expected[i - 1]) throw new InvalidDataException(typeName + " expansion costs are not strictly increasing");
            }

            var vanillaLast = ReadInt(list[originalCount - 1]!, "Cost", "_cost");
            if (expected[0] <= vanillaLast) throw new InvalidDataException(typeName + " first expansion cost must exceed the original final grade");
        }

        private static void VerifyProgressiveRewardBoundaries()
        {
            var cases = new[]
            {
                (Completed: 0, Expected: 20),
                (Completed: 67, Expected: 20), // Reward 68
                (Completed: 68, Expected: 21), // Reward 69
                (Completed: 91, Expected: 44), // Reward 92
                (Completed: 92, Expected: 44), // Reward 93
                (Completed: 135, Expected: 44)
            };
            foreach (var item in cases)
                if (GetProgressiveReward(item.Completed) != item.Expected)
                    throw new InvalidDataException("Progressive reward boundary mismatch at completed type count " + item.Completed);
        }

        private static int GetProgressiveReward(int completedTypeCount) => 20 + Math.Min(24, Math.Max(0, completedTypeCount - 67));

        private static object? GetPlayerWalletInstance()
        {
            var type = FindGameType("PlayerWallet");
            if (type == null) return null;
            var wallet = ReadStaticMember(type, "Instance");
            if (wallet is UnityEngine.Object unityWallet && unityWallet == null) wallet = null;
            return wallet ?? FindActiveGameComponent("PlayerWallet");
        }

        private static void VerifyCurrentGrades(Dictionary<string, object> objects, int itemPull, int shelfScatter, int carry, int repeat)
        {
            VerifyGrade(objects, "ItemPullAbility", itemPull);
            VerifyGrade(objects, "ShelfScatterAbility", shelfScatter);
            VerifyGrade(objects, "HandCapacityUpgrade", carry);
            VerifyGrade(objects, "RepeatInteractUpgrade", repeat);
        }

        private static void VerifyGrade(Dictionary<string, object> objects, string typeName, int expected)
        {
            if (!objects.TryGetValue(typeName, out var owner) || GetCurrentLevel(owner) != expected)
                throw new InvalidOperationException(typeName + " current grade expected " + expected + " but was " + (objects.TryGetValue(typeName, out owner) ? GetCurrentLevel(owner) : -1));
        }

        private static void WriteAutomatedSettings(bool maxAll, bool expansions, long revision, bool infiniteStars = false, bool noCooldown = false, bool holdToDrop = false)
        {
            var settings = new ModSettings
            {
                Schema = 1,
                Revision = revision,
                ExpandCarry = expansions,
                ExpandAutoPickup = expansions,
                ExpandAutoPlace = expansions,
                ExpandContinuousPlace = expansions,
                MoreStars = true,
                InfiniteStars = infiniteStars,
                NoCooldown = noCooldown,
                MaxAll = maxAll,
                HoldToDrop = holdToDrop
            };
            JsonFile.WriteAtomic(TrainerPlugin.SettingsPath, settings);
        }

        private static void VerifyInfiniteStarsNoCooldownAndReward()
        {
            var walletType = FindGameType("PlayerWallet") ?? throw new MissingMemberException("Bunker.Scoring.PlayerWallet");
            var wallet = GetPlayerWalletInstance() ?? throw new InvalidOperationException("PlayerWallet.Instance is not ready");
            var balanceProperty = walletType.GetProperty("Balance", BindingFlags.Instance | BindingFlags.Public) ?? throw new MissingMemberException(walletType.FullName, "Balance");
            var balanceBefore = (int)balanceProperty.GetValue(wallet, null)!;
            var allFree = (bool)(walletType.GetProperty("IsAllFree")?.GetValue(wallet, null) ?? false);
            var canSpend = walletType.GetMethod("CanSpend", new[] { typeof(int) })?.Invoke(wallet, new object[] { int.MaxValue }) as bool? ?? false;
            if (!allFree || !canSpend) throw new InvalidOperationException("Infinite stars did not report all-free spending for the isolated wallet");
            var spend = walletType.GetMethod("Spend", new[] { typeof(int) }) ?? throw new MissingMethodException(walletType.FullName, "Spend(Int32)");
            spend.Invoke(wallet, new object[] { 100000000 });
            var balanceAfter = (int)balanceProperty.GetValue(wallet, null)!;
            if (balanceAfter != balanceBefore) throw new InvalidOperationException("Infinite stars changed the real isolated wallet balance");

            var scoringType = FindGameType("ShelfScoring") ?? throw new MissingMemberException("Bunker.Scoring.ShelfScoring");
            var scorers = UnityEngine.Object.FindObjectsByType(scoringType, FindObjectsSortMode.None);
            if (scorers.Length == 0 || scorers.Any(source => ReadInt(source, "_pointsPerType", "pointsPerType") != GetProgressiveReward(ReadInt(source, "ScoredTypesCount", "_scoredTypesCount"))))
                throw new InvalidOperationException("More Stars reward value did not match the game's persisted scored-type count");
            VerifyProgressiveRewardBoundaries();

            var abilities = FindLevelObjects().Where(item => item.Owner.GetType().Name == "ItemPullAbility").ToArray();
            var ability = abilities.Select(item => item.Owner).FirstOrDefault() ?? throw new InvalidOperationException("ItemPullAbility is unavailable for cooldown validation");
            var controller = Controllers.FirstOrDefault(candidate => candidate.GetType().Name == "AbilityController") ?? throw new InvalidOperationException("AbilityController is unavailable for cooldown validation");
            var context = ReadMember(controller, "_context", "Context");
            var tick = SafeMethods(ability.GetType()).FirstOrDefault(method => method.Name == "Tick" && method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType.IsInstanceOfType(context) && method.GetParameters()[1].ParameterType == typeof(float));
            if (tick == null) throw new MissingMethodException(ability.GetType().FullName, "Tick(AbilityContext, Single)");
            WriteMember(ability, false, "_isRunning");
            WriteMember(ability, 60f, "_cooldownLeft");
            tick.Invoke(ability, new[] { context, (object)0.01f });
            if (ReadFloat(ability, "_cooldownLeft") > 0.001f || ReadBool(ability, "_isRunning"))
                throw new InvalidOperationException("No Cooldown failed to clear idle cooldown or altered the ability run state");
            TrainerPlugin.Log.LogInfo("Feature check passed: unlimited spending preserved wallet balance " + balanceBefore + ", More Stars uses the progressive amount derived from the persisted scored-type count, and an idle ability Tick cleared cooldown without starting a release.");
        }

        private static void ClearAutomatedTestSlot()
        {
            var saveRoot = GetSaveRoot();
            if (string.IsNullOrEmpty(saveRoot)) throw new InvalidOperationException("Test SaveSystem path is not initialized");
            var saveDirectory = Path.GetFullPath(saveRoot);
            var cloneRoot = Path.GetFullPath(Paths.GameRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!saveDirectory.StartsWith(cloneRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Automated test cleanup escaped the isolated game directory");
            foreach (var path in Directory.GetFiles(saveDirectory, "Save-" + AutomatedTestSlot + ".sav*")) File.Delete(path);

            var sidecarDirectory = Path.Combine(TrainerPlugin.DataDirectory, "slots");
            if (Directory.Exists(sidecarDirectory))
                foreach (var path in Directory.GetFiles(sidecarDirectory, "Save-" + AutomatedTestSlot + ".sav.extension.json*")) File.Delete(path);
            var backupDirectory = Path.Combine(TrainerPlugin.DataDirectory, "backups", "slots", "Save-" + AutomatedTestSlot);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, true);
            TrainerPlugin.Log.LogInfo("Cleared only stale slot " + AutomatedTestSlot + " data inside the isolated test clone.");
        }

        private static void VerifyCommittedProgressAndVanillaSave()
        {
            var progress = JsonFile.Read<SlotProgress>(ProgressPath(AutomatedTestSlot)) ?? throw new FileNotFoundException("The extension sidecar was not committed after SaveSystem.Save");
            if (!IsValidProgress(progress, AutomatedTestSlot) || !string.Equals(progress.SaveFingerprint, GetSaveFingerprint(AutomatedTestSlot), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The committed sidecar does not match the saved game fingerprint");
            VerifyEntry(progress.Abilities, "ItemPullAbility", 8);
            VerifyEntry(progress.Abilities, "ShelfScatterAbility", 6);
                VerifyEntry(progress.Upgrades, "HandCapacityUpgrade", 16);
            VerifyEntry(progress.Upgrades, "RepeatInteractUpgrade", 7);
        }

        private static void VerifyEntry(List<LevelEntry> entries, string key, int grade)
        {
            if (entries.FirstOrDefault(entry => entry.Key == key)?.Level != grade)
                throw new InvalidDataException("Sidecar grade did not preserve the genuine purchase for " + key);
        }

        private static void StartReadingSavedModel()
        {
            var type = FindGameType("SaveSystem") ?? throw new InvalidOperationException("SaveSystem was not found");
            var locationMethod = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == "GetSaveLocationFor" && candidate.GetParameters().Length == 1 && candidate.GetParameters()[0].ParameterType == typeof(int));
            var location = locationMethod?.Invoke(null, new object[] { AutomatedTestSlot }) as string;
            TrainerPlugin.Log.LogInfo("Test SaveSystem readback path=" + location + ", exists=" + (!string.IsNullOrEmpty(location) && File.Exists(location)) + ", saveRoot=" + GetSaveRoot() + ".");
            var method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == "Load" && candidate.GetParameters().Length == 2 && candidate.GetParameters()[0].ParameterType == typeof(int));
            if (method == null) throw new MissingMethodException(type.FullName, "Load(Int32, SaveMetadata)");
            _automatedSaveLoadTask = method.Invoke(null, new object?[] { AutomatedTestSlot, null }) as Task ?? throw new InvalidOperationException("SaveSystem.Load did not return Task");
            TrainerPlugin.Log.LogInfo("Started SaveSystem.Load with embedded metadata parsing for slot " + AutomatedTestSlot + ".");
        }

        private static void VerifyVanillaSaveGrades(object save)
        {
            var abilities = ReadMember(save, "AbilityLevels", "abilityLevels") as IDictionary ?? throw new InvalidDataException("Loaded save has no AbilityLevels map");
            var upgrades = ReadMember(save, "UpgradeLevels", "upgradeLevels") as IDictionary ?? throw new InvalidDataException("Loaded save has no UpgradeLevels map");
            VerifySavedGrade(abilities, "ItemPullAbility", 6);
            VerifySavedGrade(abilities, "ShelfScatterAbility", 3);
            VerifySavedGrade(upgrades, "HandCapacityUpgrade", 7);
            VerifySavedGrade(upgrades, "RepeatInteractUpgrade", 5);
        }

        private static void VerifySavedGrade(IDictionary levels, string key, int vanillaMaximum)
        {
            if (!TryInt(levels[key], out var grade) || grade > vanillaMaximum)
                throw new InvalidDataException("Vanilla save grade for " + key + " exceeded its original limit");
        }

        private static void VerifyPersistedScoredTypeCount(object save)
        {
            var savedTypes = ReadMember(save, "ScoredTypes", "scoredTypes") as ICollection
                ?? throw new InvalidDataException("The real Save model did not contain its persisted ScoredTypes list");
            var scoringType = FindGameType("ShelfScoring") ?? throw new MissingMemberException("Bunker.Scoring.ShelfScoring");
            var scorers = UnityEngine.Object.FindObjectsByType(scoringType, FindObjectsSortMode.None);
            var liveCount = scorers.Select(source => ReadInt(source, "ScoredTypesCount", "_scoredTypesCount")).DefaultIfEmpty(-1).Max();
            if (liveCount < 0 || savedTypes.Count != liveCount)
                throw new InvalidDataException("Save/load did not preserve the game's genuine scored-type counter: live=" + liveCount + ", serialized=" + savedTypes.Count);
            TrainerPlugin.Log.LogInfo("Verified Save.ScoredTypes roundtrip against ShelfScoring.ScoredTypesCount: " + savedTypes.Count + ".");
        }

        private static object? ReadTaskResult(Task task)
        {
            var result = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
            return result?.GetValue(task, null);
        }

        private static void ProcessSavedModelRead()
        {
            if (_automatedMetadataTask == null) return;
            if (!_automatedMetadataTask.IsCompleted) return;
            if (_automatedMetadataTask.IsFaulted) throw new IOException("SaveSystem.LoadMetadata failed", _automatedMetadataTask.Exception);
            _automatedMetadata = ReadTaskResult(_automatedMetadataTask);
            if (TrainerPlugin.TestMode)
            {
                var metaDescription = _automatedMetadata == null ? "null" : _automatedMetadata.GetType().FullName + "{" + string.Join(",", _automatedMetadata.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => field.Name + "=" + Convert.ToString(field.GetValue(_automatedMetadata)))) + "}";
                TrainerPlugin.Log.LogInfo("Test SaveSystem metadata result=" + metaDescription + ".");
            }
            var type = FindGameType("SaveSystem") ?? throw new InvalidOperationException("SaveSystem was not found");
            var method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == "Load" && candidate.GetParameters().Length == 2 && candidate.GetParameters()[0].ParameterType == typeof(int));
            if (method == null) throw new MissingMethodException(type.FullName, "Load(Int32, SaveMetadata)");
            _automatedSaveLoadTask = method.Invoke(null, new[] { (object)AutomatedTestSlot, _automatedMetadata! }) as Task ?? throw new InvalidOperationException("SaveSystem.Load did not return Task");
            _automatedMetadataTask = null;
        }

        private static void WriteAutomatedGameTestReport(bool passed, string message)
        {
            var phase = _automatedTestPhase;
            var reportPath = Path.Combine(Paths.GameRootPath, "trainer-test-phase" + phase + ".txt");
            File.WriteAllText(reportPath, (passed ? "PASS " : "FAIL ") + message, new System.Text.UTF8Encoding(false));
            if (passed) TrainerPlugin.Log.LogInfo("Automated gameplay test phase " + phase + " passed: " + message);
            else TrainerPlugin.Log.LogError("Automated gameplay test phase " + phase + " failed: " + message);
            _automatedTestStage = -1;
            _automatedTestPhase = 0;
        }

        private static void FailAutomatedGameTest(string message)
        {
            if (_automatedTestStage < 0) return;
            _testHeldAction = null;
            _testAimPoint = null;
            _testRangeCandidateIds = null;
            StopRangePress(true);
            StopDropPress(true);
            _automatedTestStage = -1;
            try { WriteAutomatedGameTestReport(false, message); }
            catch (Exception ex) { TrainerPlugin.Log.LogError("Could not write gameplay test report: " + ex); }
        }

        internal static void ExtendController(object controller)
        {
            if (!Controllers.Contains(controller, ReferenceComparer.Instance)) Controllers.Add(controller);
            var kind = controller.GetType().Name.ToLowerInvariant();
            var isAbilityController = kind.Contains("abilitycontroller");
            var isUpgradeController = kind.Contains("upgradecontroller");
            if (!isAbilityController && !isUpgradeController) return;

            foreach (var field in Fields(controller.GetType()))
            {
                object? child;
                try { child = field.GetValue(controller); }
                catch { continue; }
                if (child == null) continue;
                if (isAbilityController && IsSubclassNamed(child.GetType(), "Ability")) ExtendAbility(child, field.Name);
                if (isUpgradeController && IsSubclassNamed(child.GetType(), "Upgrade")) ExtendUpgrade(child, field.Name);
            }
        }

        private static void ExtendAbility(object ability, string fieldName)
        {
            var typeName = ability.GetType().Name;
            var category = Category(typeName + " " + fieldName);
            if (category != "autoPickup" && category != "autoPlace") return;
            SyncExtensionList(ability, true, category);
        }

        private static void ExtendUpgrade(object upgrade, string fieldName)
        {
            var category = Category(upgrade.GetType().Name + " " + fieldName);
            if (category != "carry" && category != "continuousPlace") return;
            SyncExtensionList(upgrade, false, category);
        }

        private static void SyncExtensionList(object owner, bool isAbility, string category)
        {
            var key = owner.GetType().Name;
            var list = ReadMember(owner, isAbility ? "_levels" : "_steps") as IList;
            if (list == null || list.Count == 0) return;
            if (!OriginalLevelLists.TryGetValue(owner, out var original))
            {
                original = list.Cast<object>().ToList();
                OriginalLevelLists[owner] = original;
                (isAbility ? BaseAbilityLimits : BaseUpgradeLimits)[key] = original.Count;
            }

            var baseLimit = original.Count;
            var featureEnabled = TrainerPlugin.Settings != null && !TrainerPlugin.Settings.RestoreRequested && TrainerPlugin.Settings.IsExpansionEnabled(category);
            if (!featureEnabled)
            {
                // A running ability may still index the current extended grade until its natural release ends.
                if (isAbility && IsRunning(owner) && GetCurrentLevel(owner) > baseLimit) return;
                RestoreList(list, original);
                return;
            }

            var target = GetExpandedLimit(owner, category, isAbility, baseLimit);
            if (list.Count > target) RestoreList(list, original);
            var lastVanilla = original[original.Count - 1];
            while (list.Count < target)
            {
                var grade = list.Count + 1;
                var copy = Clone(lastVanilla);
                if (isAbility && category == "autoPickup")
                {
                    var index = Math.Max(0, Math.Min(3, grade - 7));
                    WriteMember(copy, new[] { 35, 40, 45, 50 }[index], "amount", "_amount");
                    WriteMember(copy, new[] { 22f, 20f, 18f, 15f }[index], "cooldown", "_cooldown");
                    WriteMember(copy, PullExpansionCosts[index], "cost", "_cost");
                }
                else if (isAbility && category == "autoPlace")
                {
                    var index = Math.Max(0, Math.Min(6, grade - 4));
                    WriteMember(copy, new[] { 80f, 70f, 60f, 50f, 45f, 40f, 30f }[index], "cooldown", "_cooldown");
                    WriteMember(copy, ScatterExpansionCosts[index], "cost", "_cost");
                }
                else
                {
                    WriteMember(copy, category == "carry" ? 15 : 5, "value", "_value");
                    var costs = category == "carry" ? CarryExpansionCosts : RepeatExpansionCosts;
                    var originalCount = original.Count;
                    var index = Math.Max(0, Math.Min(costs.Length - 1, grade - originalCount - 1));
                    WriteMember(copy, costs[index], "cost", "_cost");
                }
                list.Add(copy);
            }
        }

        private static void RestoreList(IList list, List<object> original)
        {
            if (list.Count == original.Count && original.Select((item, index) => ReferenceEquals(item, list[index])).All(same => same)) return;
            list.Clear();
            foreach (var item in original) list.Add(item);
        }

        private static object Clone(object source)
        {
            var clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return clone.Invoke(source, null)!;
        }

        private static string Category(string text)
        {
            var key = text.ToLowerInvariant();
            if (key.Contains("handcapacityupgrade") || key.Contains("_handcapacity")) return "carry";
            if (key.Contains("itempullability") || key.Contains("_itempull")) return "autoPickup";
            if (key.Contains("shelfscatterability") || key.Contains("_shelfscatter")) return "autoPlace";
            if (key.Contains("repeatinteractupgrade") || key.Contains("_repeatinteract")) return "continuousPlace";
            return "";
        }

        private static bool IsSubclassNamed(Type type, string baseName)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (current.Name == baseName && current.Namespace == "Bunker." + (baseName == "Ability" ? "Abilities" : "Upgrades")) return true;
            return false;
        }

        internal static void ApplyLevels()
        {
            if (TrainerPlugin.Settings == null) return;
            var all = FindLevelObjects();
            lock (StateGate)
            {
                TrainerPlugin.IsApplyingLevels = true;
                try
                {
                    foreach (var (owner, category, isAbility) in all)
                        if (category.Length != 0) SyncExtensionList(owner, isAbility, category);

                    foreach (var (owner, category, isAbility) in all)
                    {
                        var key = owner.GetType().Name;
                        var current = GetCurrentLevel(owner);
                        var realLevels = isAbility ? RealAbilities : RealUpgrades;
                        var baseLimits = isAbility ? BaseAbilityLimits : BaseUpgradeLimits;
                        if (!realLevels.ContainsKey(key)) realLevels[key] = FreshSlots.Contains(_activeSlot) ? 0 : current;
                        if (!baseLimits.ContainsKey(key))
                        {
                            var list = ReadMember(owner, isAbility ? "_levels" : "_steps", isAbility ? "levels" : "steps") as IList;
                            if (list != null) baseLimits[key] = list.Count;
                        }

                        var real = realLevels[key];
                        var baseLimit = baseLimits.TryGetValue(key, out var limit) ? limit : Math.Max(current, 0);
                        var expansion = !TrainerPlugin.Settings.RestoreRequested && TrainerPlugin.Settings.IsExpansionEnabled(category);
                        var hardLimit = GetExpandedLimit(owner, category, isAbility, baseLimit);
                        var allowedLimit = expansion ? hardLimit : baseLimit;
                        int desired;
                        if (TrainerPlugin.Settings.MaxAll && !TrainerPlugin.Settings.RestoreRequested)
                            desired = allowedLimit;
                        else
                            desired = Math.Min(real, allowedLimit);

                        if (IsRunning(owner) && desired < current)
                            continue;
                        SetLevel(owner, desired);
                        if (category.Length != 0) SyncExtensionList(owner, isAbility, category);
                    }
                }
                finally
                {
                    TrainerPlugin.IsApplyingLevels = false;
                }
            }
        }

        private static int GetExpandedLimit(object owner, string category, bool isAbility, int fallback)
        {
            if (category == "carry") return ExpandedCarryMaximum;
            if (category == "continuousPlace") return 10;
            if (category == "autoPickup") return 10;
            if (category == "autoPlace") return 10;
            return fallback;
        }

        private static IEnumerable<(object Owner, string Category, bool IsAbility)> FindLevelObjects()
        {
            foreach (var controller in Controllers.ToArray())
            {
                if (controller is UnityEngine.Object unityController && unityController == null) continue;
                foreach (var field in Fields(controller.GetType()))
                {
                    object? child;
                    try { child = field.GetValue(controller); }
                    catch { continue; }
                    if (child == null) continue;
                    if (child is UnityEngine.Object unityChild && unityChild == null) continue;
                    var name = child.GetType().Name;
                    var isAbility = IsSubclassNamed(child.GetType(), "Ability");
                    if (!isAbility && !IsSubclassNamed(child.GetType(), "Upgrade")) continue;
                    var category = Category(name + " " + field.Name);
                    if (ReadMember(child, isAbility ? "_levels" : "_steps", isAbility ? "levels" : "steps") is IList)
                        yield return (child, category, isAbility);
                }
            }
        }

        internal static bool TryGetMaxLevel(object owner, out int maximum)
        {
            maximum = 0;
            var type = owner.GetType();
            var isAbility = IsSubclassNamed(type, "Ability");
            var isUpgrade = IsSubclassNamed(type, "Upgrade");
            if (!isAbility && !isUpgrade) return false;
            var key = type.Name;
            var category = Category(key);
            if (category.Length == 0) return false;
            var limits = isAbility ? BaseAbilityLimits : BaseUpgradeLimits;
            var list = ReadMember(owner, isAbility ? "_levels" : "_steps") as IList;
            var actualCount = list?.Count ?? 0;
            if (!limits.TryGetValue(key, out var originalCount)) originalCount = actualCount;
            maximum = !TrainerPlugin.Settings.RestoreRequested && TrainerPlugin.Settings.IsExpansionEnabled(category)
                ? GetExpandedLimit(owner, category, isAbility, originalCount)
                : originalCount;
            return true;
        }

        internal static void RecordPurchase(object owner)
        {
            var type = owner.GetType();
            var isAbility = IsSubclassNamed(type, "Ability");
            var isUpgrade = IsSubclassNamed(type, "Upgrade");
            if (!isAbility && !isUpgrade) return;
            var target = isAbility ? RealAbilities : RealUpgrades;
            var level = GetCurrentLevel(owner);
            lock (StateGate) target[type.Name] = level;
            if (TrainerPlugin.TestMode) TrainerPlugin.Log.LogInfo("Test purchase receipt recorded " + type.FullName + " grade=" + level + ".");
        }

        internal static void RecordSaveData(object controller, object result)
        {
            // Save dictionaries describe the current runtime view and may contain temporary max levels.
            // Durable true levels change only after a successful purchase or an explicit load.
        }

        internal static void PrepareLoad(object controller, object[] args)
        {
            var dictionary = args.FirstOrDefault(a => a is IDictionary) as IDictionary;
            if (dictionary == null) return;
            var slot = GetSaveSlot();
            if (TrainerPlugin.TestMode) TrainerPlugin.Log.LogInfo("Test load-from-save-data hook: controller=" + controller.GetType().FullName + ", slot=" + slot + ", activeSlot=" + _activeSlot + ", pairs=" + dictionary.Count + ".");
            BeginCampaignLoad(slot);
            var progress = _loadedProgress;
            var abilityController = controller.GetType().Name.ToLowerInvariant().Contains("ability");
            var entries = abilityController ? progress?.Abilities : progress?.Upgrades;
            var real = abilityController ? RealAbilities : RealUpgrades;
            var controllerFields = Fields(controller.GetType()).ToArray();
            var pairs = new List<DictionaryEntry>(dictionary.Count);
            var enumerator = dictionary.GetEnumerator();
            while (enumerator.MoveNext()) pairs.Add(new DictionaryEntry(enumerator.Key, enumerator.Value));
            foreach (DictionaryEntry entry in pairs)
            {
                if (!(entry.Key is string key)) continue;
                var saved = TryInt(entry.Value, out var parsed) ? parsed : 0;
                var extension = entries?.FirstOrDefault(e => e.Key == key)?.Level;
                var recorded = extension ?? saved;
                real[key] = recorded;
                if (TrainerPlugin.Settings.RestoreRequested || !TrainerPlugin.Settings.IsExpansionEnabled(CategoryForKey(key, controllerFields)))
                {
                    dictionary[key] = saved;
                }
                else
                {
                    dictionary[key] = recorded;
                }
            }
        }

        private static string CategoryForKey(string key, FieldInfo[] controllerFields)
        {
            foreach (var field in controllerFields)
            {
                if (field.FieldType.Name == key || field.Name == key) return Category(field.Name + " " + field.FieldType.Name);
            }
            return Category(key);
        }

        internal static SaveSnapshot BeginSave(object[] args)
        {
            var save = args.FirstOrDefault(IsSaveModel);
            var slot = args.FirstOrDefault(a => a is int) is int slotArg ? slotArg : GetSaveSlot();
            var hands = ReadHandsInfo();
            var snapshot = new SaveSnapshot
            {
                Slot = slot,
                SettingsRevision = TrainerPlugin.Settings.Revision,
                RestoreRequested = TrainerPlugin.Settings.RestoreRequested,
                HandsCount = hands.Count,
                HandsCapacity = hands.Capacity,
                HasSaveModel = save != null
            };
            if (save == null) return snapshot;
            lock (SlotGates.GetOrAdd(slot, _ => new object()))
            {
                snapshot.Sequence = Interlocked.Increment(ref _nextSaveSequence);
                LatestSaveSequence[slot] = snapshot.Sequence;
            }
            var abilities = ReadLevels(save, "AbilityLevels", "abilityLevels");
            var upgrades = ReadLevels(save, "UpgradeLevels", "upgradeLevels");
            lock (StateGate)
            {
                SeedVanillaProgress(RealAbilities, abilities);
                SeedVanillaProgress(RealUpgrades, upgrades);
                var savedAbilities = CompatibilityLevels(abilities, RealAbilities);
                var savedUpgrades = CompatibilityLevels(upgrades, RealUpgrades);
                WriteLevels(save, savedAbilities, "AbilityLevels", "abilityLevels");
                WriteLevels(save, savedUpgrades, "UpgradeLevels", "upgradeLevels");
                if (TrainerPlugin.TestMode && slot == AutomatedTestSlot)
                {
                    VerifySavedGrade(savedAbilities, "ItemPullAbility", 6);
                    VerifySavedGrade(savedAbilities, "ShelfScatterAbility", 3);
                    VerifySavedGrade(savedUpgrades, "HandCapacityUpgrade", 7);
                    VerifySavedGrade(savedUpgrades, "RepeatInteractUpgrade", 5);
                    TrainerPlugin.Log.LogInfo("Verified AutoSave model retained true extension grades in sidecar and wrote only original-compatible levels to game Save.");
                }
                snapshot.Abilities = ToEntries(RealAbilities);
                snapshot.Upgrades = ToEntries(RealUpgrades);
            }
            snapshot.VanillaHandsCapacity = GetVanillaHandsCapacity(snapshot.Upgrades);
            snapshot.HandInventoryMetadataPresent = snapshot.HandsCount >= 0 && snapshot.VanillaHandsCapacity > 0;
            if (TrainerPlugin.TestMode)
                TrainerPlugin.Log.LogInfo("Test save snapshot for slot " + slot + ": abilities=" + string.Join(",", snapshot.Abilities.Select(entry => entry.Key + "=" + entry.Level)) + "; upgrades=" + string.Join(",", snapshot.Upgrades.Select(entry => entry.Key + "=" + entry.Level)) + ".");
            return snapshot;
        }

        private static bool IsSaveModel(object? obj)
        {
            if (obj == null) return false;
            return FindMember(obj.GetType(), "AbilityLevels", "abilityLevels") != null &&
                   FindMember(obj.GetType(), "UpgradeLevels", "upgradeLevels") != null;
        }

        private static IDictionary? ReadLevels(object save, params string[] names) => ReadMember(save, names) as IDictionary;

        private static void WriteLevels(object save, IDictionary dictionary, params string[] names)
        {
            WriteMember(save, dictionary, names);
        }

        private static IDictionary CompatibilityLevels(IDictionary? source, Dictionary<string, int> real)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (source == null) return result;
            foreach (DictionaryEntry entry in source)
            {
                if (!(entry.Key is string key) || !TryInt(entry.Value, out var grade)) continue;
                if (real.TryGetValue(key, out var saved)) grade = saved;
                var category = Category(key);
                var baseLimits = category == "autoPickup" || category == "autoPlace" ? BaseAbilityLimits :
                    category == "carry" || category == "continuousPlace" ? BaseUpgradeLimits : null;
                if (baseLimits != null && baseLimits.TryGetValue(key, out var baseLimit)) grade = Math.Min(grade, baseLimit);
                result[key] = Math.Max(0, grade);
            }
            return result;
        }

        private static void SeedVanillaProgress(Dictionary<string, int> target, IDictionary? source)
        {
            if (source == null) return;
            foreach (DictionaryEntry entry in source)
                if (entry.Key is string key && TryInt(entry.Value, out var value) && !target.ContainsKey(key)) target[key] = value;
        }

        private static List<LevelEntry> ToEntries(Dictionary<string, int> source) => source.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new LevelEntry { Key = p.Key, Level = p.Value }).ToList();

        internal static Task CompleteSaveAsync(SaveSnapshot snapshot, Task task)
        {
            if (task == null) throw new InvalidOperationException("SaveSystem returned a null Task");
            if (!snapshot.HasSaveModel) return task;
            return AwaitAndCommit(snapshot, task);
        }

        private static async Task AwaitAndCommit(SaveSnapshot snapshot, Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TrainerPlugin.MainThreadActions.Enqueue(() =>
                {
                    if (_activeSlot == snapshot.Slot && GetSaveSlot() == snapshot.Slot && TrainerPlugin.Settings.Revision == snapshot.SettingsRevision)
                    {
                        TrainerPlugin.Status.State = "saveFailed";
                        TrainerPlugin.Status.RestoreSaved = false;
                        TrainerPlugin.Status.Message = "游戏存档未成功写入；扩展记录保持原样";
                    }
                    TrainerPlugin.Log.LogError("Save task did not complete successfully; extension progress was not committed: " + ex.GetBaseException());
                });
                throw;
            }

            lock (SlotGates.GetOrAdd(snapshot.Slot, _ => new object()))
            {
                try
                {
                    if (!LatestSaveSequence.TryGetValue(snapshot.Slot, out var latest) || snapshot.Sequence != latest) return;
                    var fingerprint = GetSaveFingerprint(snapshot.Slot);
                    if (string.IsNullOrEmpty(fingerprint)) throw new IOException("SaveSystem task completed but Save-" + snapshot.Slot + ".sav was not found on disk");
                    if (snapshot.RestoreRequested)
                    {
                        var restoredProgress = new SlotProgress
                        {
                            Slot = snapshot.Slot,
                            SaveFileName = "Save-" + snapshot.Slot + ".sav",
                            SaveFingerprint = fingerprint,
                            SavedUtc = DateTime.UtcNow,
                            Abilities = snapshot.Abilities.Select(CopyEntry).ToList(),
                            Upgrades = snapshot.Upgrades.Select(CopyEntry).ToList(),
                            HandInventoryMetadataPresent = snapshot.HandInventoryMetadataPresent,
                            HandsCount = snapshot.HandsCount,
                            VanillaHandsCapacity = snapshot.VanillaHandsCapacity
                        };
                        CommitProgress(snapshot.Slot, restoredProgress);
                        MarkSaveCommitted(snapshot.Slot, snapshot.Sequence);
                        TrainerPlugin.MainThreadActions.Enqueue(() =>
                        {
                            if (TrainerPlugin.Settings.Revision != snapshot.SettingsRevision || !TrainerPlugin.Settings.RestoreRequested) return;
                            if (_activeSlot == snapshot.Slot && GetSaveSlot() == snapshot.Slot)
                            {
                                lock (StateGate) _loadedProgress = restoredProgress;
                            }
                            TrainerPlugin.Status.HandsCount = snapshot.HandsCount;
                            TrainerPlugin.Status.HandsCapacity = snapshot.VanillaHandsCapacity;
                            if (!snapshot.HandInventoryMetadataPresent)
                            {
                                TrainerPlugin.Status.RestoreSaved = false;
                                TrainerPlugin.Status.RestoreRevision = -1;
                                TrainerPlugin.Status.RestoreSlot = -1;
                                TrainerPlugin.Status.State = "restorePending";
                                TrainerPlugin.Status.Message = "原版兼容存档已保存，但未能核对手持容量；请载入存档后再次保存以确认恢复安全";
                            }
                            else if (snapshot.HandsCount > snapshot.VanillaHandsCapacity)
                            {
                                TrainerPlugin.Status.RestoreSaved = false;
                                TrainerPlugin.Status.RestoreRevision = -1;
                                TrainerPlugin.Status.RestoreSlot = -1;
                                TrainerPlugin.Status.State = "restorePending";
                                TrainerPlugin.Status.Message = "存档已保存，但手持 " + snapshot.HandsCount + " 件，超过原版容量 " + snapshot.VanillaHandsCapacity + "；请整理至容量内再保存";
                            }
                            else
                            {
                                TrainerPlugin.Status.RestoreSaved = true;
                                TrainerPlugin.Status.RestoreRevision = snapshot.SettingsRevision;
                                TrainerPlugin.Status.RestoreSlot = snapshot.Slot;
                                TrainerPlugin.Status.State = "restored";
                                TrainerPlugin.Status.Message = "原版兼容存档已成功保存，手持数量符合原版容量；可以完成卸载";
                            }
                        });
                        return;
                    }

                    var progress = new SlotProgress
                    {
                        Slot = snapshot.Slot,
                        SaveFileName = "Save-" + snapshot.Slot + ".sav",
                        SaveFingerprint = fingerprint,
                        SavedUtc = DateTime.UtcNow,
                        Abilities = snapshot.Abilities.Select(CopyEntry).ToList(),
                        Upgrades = snapshot.Upgrades.Select(CopyEntry).ToList(),
                        HandInventoryMetadataPresent = snapshot.HandInventoryMetadataPresent,
                        HandsCount = snapshot.HandsCount,
                        VanillaHandsCapacity = snapshot.VanillaHandsCapacity
                    };
                    CommitProgress(snapshot.Slot, progress);
                    MarkSaveCommitted(snapshot.Slot, snapshot.Sequence);
                    TrainerPlugin.MainThreadActions.Enqueue(() =>
                    {
                        lock (StateGate)
                        {
                            FreshSlots.Remove(snapshot.Slot);
                            if (_activeSlot == snapshot.Slot && GetSaveSlot() == snapshot.Slot) _loadedProgress = progress;
                        }
                        if (TrainerPlugin.Settings.Revision != snapshot.SettingsRevision || TrainerPlugin.Settings.RestoreRequested) return;
                        TrainerPlugin.Status.State = "active";
                        TrainerPlugin.Status.Message = "游戏存档和对应扩展等级均已成功写入";
                        TrainerPlugin.Status.RestoreSaved = false;
                    });
                }
                catch (Exception ex)
                {
                    TrainerPlugin.MainThreadActions.Enqueue(() =>
                    {
                        TrainerPlugin.Status.State = "saveFailed";
                        TrainerPlugin.Status.Message = "游戏存档已写入，但扩展备份提交失败；旧扩展记录已保留";
                        TrainerPlugin.Status.RestoreSaved = false;
                        TrainerPlugin.Log.LogError("Extension progress commit failed for slot " + snapshot.Slot + ": " + ex);
                    });
                    throw;
                }
            }
        }

        private static LevelEntry CopyEntry(LevelEntry entry) => new LevelEntry { Key = entry.Key, Level = entry.Level };

        private static void CommitProgress(int slot, SlotProgress progress)
        {
            var path = ProgressPath(slot);
            ArchiveProgress(slot);
            JsonFile.WriteAtomic(path, progress);
        }

        private static void MarkSaveCommitted(int slot, long sequence)
        {
            CompletedSaveSequence.AddOrUpdate(slot, sequence, (_, current) => Math.Max(current, sequence));
        }

        private static void ArchiveProgress(int slot)
        {
            var path = ProgressPath(slot);
            if (!File.Exists(path)) return;
            var backupDirectory = Path.Combine(TrainerPlugin.DataDirectory, "backups", "slots", "Save-" + slot);
            Directory.CreateDirectory(backupDirectory);
            var archive = Path.Combine(backupDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json");
            File.Copy(path, archive, false);
            var archives = new DirectoryInfo(backupDirectory).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            foreach (var stale in archives.Skip(20)) stale.Delete();
        }

        private static string ProgressPath(int slot) => Path.Combine(TrainerPlugin.DataDirectory, "slots", "Save-" + slot + ".sav.extension.json");
        private static SlotProgress? ReadProgress(int slot)
        {
            if (FreshSlots.Contains(slot)) return null;
            var currentFingerprint = GetSaveFingerprint(slot);
            var primaryPath = ProgressPath(slot);
            var backupDirectory = Path.Combine(TrainerPlugin.DataDirectory, "backups", "slots", "Save-" + slot);
            return ReadProgressCandidates(slot, currentFingerprint, primaryPath, backupDirectory, true);
        }

        private static SlotProgress? ReadProgressCandidates(int slot, string currentFingerprint, string primaryPath, string backupDirectory, bool updateStatus)
        {
            if (!IsFingerprint(currentFingerprint))
            {
                if (updateStatus && File.Exists(primaryPath))
                {
                    TrainerPlugin.Status.State = "extensionMismatch";
                    TrainerPlugin.Status.Message = "无法核验当前存档指纹，已按原版购买等级读档并保留扩展记录";
                    TrainerPlugin.Status.RestoreSaved = false;
                }
                return null;
            }

            var candidates = new List<string>();
            if (File.Exists(primaryPath)) candidates.Add(primaryPath);
            var previousPath = primaryPath + ".previous";
            if (File.Exists(previousPath)) candidates.Add(previousPath);
            if (Directory.Exists(backupDirectory))
                candidates.AddRange(Directory.GetFiles(backupDirectory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc));

            var corruptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sawMismatch = false;
            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                SlotProgress? progress;
                try
                {
                    var info = new FileInfo(candidate);
                    if (info.Length <= 0 || info.Length > 1024 * 1024) throw new InvalidDataException("sidecar file size is invalid");
                    progress = JsonFile.Read<SlotProgress>(candidate);
                    if (!IsValidProgress(progress, slot)) throw new InvalidDataException("sidecar schema or progress values are invalid");
                }
                catch (Exception ex)
                {
                    corruptFiles.Add(candidate);
                    if (string.Equals(candidate, primaryPath, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate, previousPath, StringComparison.OrdinalIgnoreCase))
                        ArchiveInvalidProgress(candidate, backupDirectory);
                    TrainerPlugin.Log.LogWarning("Ignored invalid extension progress file " + candidate + ": " + ex.GetType().Name);
                    continue;
                }

                if (string.Equals(progress!.SaveFingerprint, currentFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    if (updateStatus && !string.Equals(candidate, primaryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        TrainerPlugin.Status.State = "extensionRecovered";
                        TrainerPlugin.Status.Message = "扩展记录损坏或与存档不同步，已从匹配当前存档的备份恢复；坏文件已归档";
                        TrainerPlugin.Status.RestoreSaved = false;
                    }
                    return progress;
                }
                sawMismatch = true;
            }

            if (updateStatus && corruptFiles.Count > 0)
            {
                TrainerPlugin.Status.State = "extensionCorrupt";
                TrainerPlugin.Status.Message = "扩展记录损坏且没有与当前存档匹配的备份，已归档坏文件并按原版购买等级读档";
                TrainerPlugin.Status.RestoreSaved = false;
            }
            else if (updateStatus && sawMismatch)
            {
                TrainerPlugin.Status.State = "extensionMismatch";
                TrainerPlugin.Status.Message = "存档文件已变化，已忽略不匹配的扩展等级并保留其备份";
                TrainerPlugin.Status.RestoreSaved = false;
            }
            return null;
        }

        private static bool IsValidProgress(SlotProgress? progress, int slot)
        {
            if (progress == null || progress.Schema != 1 || progress.Slot != slot ||
                !string.Equals(progress.SaveFileName, "Save-" + slot + ".sav", StringComparison.Ordinal) ||
                !IsFingerprint(progress.SaveFingerprint) || progress.Abilities == null || progress.Upgrades == null ||
                progress.Abilities.Count > 4 || progress.Upgrades.Count > 5) return false;

            return ValidEntries(progress.Abilities, true) && ValidEntries(progress.Upgrades, false);
        }

        private static bool ValidEntries(List<LevelEntry> entries, bool abilities)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Key) || !seen.Add(entry.Key)) return false;
                int maximum;
                if (abilities)
                {
                    if (entry.Key == "ItemPullAbility" || entry.Key == "ShelfScatterAbility") maximum = 10;
                    else if (entry.Key == "ItemFinderAbility" || entry.Key == "ShelfFinderAbility") maximum = 15;
                    else return false;
                }
                else
                {
                    if (entry.Key == "HandCapacityUpgrade") maximum = ExpandedCarryMaximum;
                    else if (entry.Key == "RepeatInteractUpgrade") maximum = 10;
                    else if (entry.Key == "HandGroupingUpgrade" || entry.Key == "MoveSpeedUpgrade" || entry.Key == "JumpHeightUpgrade") maximum = 15;
                    else return false;
                }
                if (entry.Level < 0 || entry.Level > maximum) return false;
            }
            return true;
        }

        private static bool IsFingerprint(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f') && !(character >= 'A' && character <= 'F')) return false;
            return true;
        }

        private static void ArchiveInvalidProgress(string sourcePath, string backupDirectory)
        {
            try
            {
                if (!File.Exists(sourcePath)) return;
                Directory.CreateDirectory(backupDirectory);
                var archive = Path.Combine(backupDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-corrupt-" + Guid.NewGuid().ToString("N") + ".json");
                File.Copy(sourcePath, archive, false);
            }
            catch (Exception ex)
            {
                TrainerPlugin.Log.LogError("Could not archive invalid extension progress " + sourcePath + ": " + ex);
            }
        }

        internal static void RunProgressCorruptionFallbackSelfTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "BunkerTidyUpTrainer-progress-test-" + Guid.NewGuid().ToString("N"));
            var slot = 987654;
            var primaryPath = Path.Combine(root, "slots", "Save-" + slot + ".sav.extension.json");
            var backupDirectory = Path.Combine(root, "backups", "Save-" + slot);
            var fingerprint = new string('a', 64);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(primaryPath)!);
                File.WriteAllText(primaryPath, "{ broken-json");
                var recovery = new SlotProgress
                {
                    Schema = 1,
                    Slot = slot,
                    SaveFileName = "Save-" + slot + ".sav",
                    SaveFingerprint = fingerprint,
                    SavedUtc = DateTime.UtcNow,
                    Abilities = new List<LevelEntry>
                    {
                        new LevelEntry { Key = "ItemPullAbility", Level = 9 },
                        new LevelEntry { Key = "ItemFinderAbility", Level = 2 },
                        new LevelEntry { Key = "ShelfFinderAbility", Level = 3 },
                        new LevelEntry { Key = "ShelfScatterAbility", Level = 8 }
                    },
                    Upgrades = new List<LevelEntry>
                    {
                        new LevelEntry { Key = "HandCapacityUpgrade", Level = 14 },
                        new LevelEntry { Key = "HandGroupingUpgrade", Level = 2 },
                        new LevelEntry { Key = "MoveSpeedUpgrade", Level = 1 },
                        new LevelEntry { Key = "JumpHeightUpgrade", Level = 1 },
                        new LevelEntry { Key = "RepeatInteractUpgrade", Level = 7 }
                    }
                };
                JsonFile.WriteAtomic(primaryPath + ".previous", recovery);
                var loaded = ReadProgressCandidates(slot, fingerprint, primaryPath, backupDirectory, false);
                if (loaded == null || loaded.Abilities[0].Level != 9 || loaded.Upgrades[0].Level != 14 ||
                    File.ReadAllText(primaryPath) != "{ broken-json" || !Directory.Exists(backupDirectory) || Directory.GetFiles(backupDirectory, "*.json").Length == 0)
                    throw new InvalidOperationException("A corrupt primary sidecar was not safely recovered from a matching backup");

                var noMatch = ReadProgressCandidates(slot, new string('b', 64), primaryPath, backupDirectory, false);
                if (noMatch != null) throw new InvalidOperationException("A sidecar without the current save fingerprint was accepted");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch { }
            }
        }

        private static void BeginCampaignLoad(int slot)
        {
            if (slot < 0 || _activeSlot == slot) return;
            ResetRangeBatch();
            lock (StateGate)
            {
                RealAbilities.Clear();
                RealUpgrades.Clear();
                _activeSlot = slot;
                _loadedProgress = ReadProgress(slot);
                if (_loadedProgress != null)
                {
                    foreach (var item in _loadedProgress.Abilities) RealAbilities[item.Key] = item.Level;
                    foreach (var item in _loadedProgress.Upgrades) RealUpgrades[item.Key] = item.Level;
                }
            }
            if (TrainerPlugin.TestMode) TrainerPlugin.Log.LogInfo("Test campaign load began for slot " + slot + "; recoveredProgress=" + (_loadedProgress != null) + ".");
        }

        internal static void OnLevelInitialize(object[] args)
        {
            var slotValue = args.FirstOrDefault(a => a is int);
            if (!TryInt(slotValue, out var slot) || slot < 0) return;
            var isSavedRun = args.Any(IsSaveModel);
            if (TrainerPlugin.TestMode && slot == AutomatedTestSlot)
            {
                _automatedInitializeSeen = true;
                _automatedInitializeObservedAt = Time.realtimeSinceStartup;
            }
            if (TrainerPlugin.TestMode) TrainerPlugin.Log.LogInfo("Test Level.Initialize hook slot=" + slot + ", hasSaveModel=" + isSavedRun + ", activeBefore=" + _activeSlot + ".");
            if (isSavedRun)
            {
                BeginCampaignLoad(slot);
                return;
            }

            ResetRangeBatch();
            lock (StateGate)
            {
                RealAbilities.Clear();
                RealUpgrades.Clear();
                foreach (var (owner, _, isAbility) in FindLevelObjects())
                {
                    (isAbility ? RealAbilities : RealUpgrades)[owner.GetType().Name] = 0;
                }
                _activeSlot = slot;
                _loadedProgress = null;
                FreshSlots.Add(slot);
            }
            if (TrainerPlugin.TestMode) TrainerPlugin.Log.LogInfo("Test new campaign initialized with zero purchased grades for slot " + slot + ".");
            ApplyLevels();
        }

        internal static void DeleteSlotProgress(int slot)
        {
            lock (SlotGates.GetOrAdd(slot, _ => new object()))
            {
                ArchiveProgress(slot);
                var path = ProgressPath(slot);
                if (File.Exists(path)) File.Delete(path);
                FreshSlots.Remove(slot);
                if (_activeSlot == slot)
                {
                    RealAbilities.Clear();
                    RealUpgrades.Clear();
                    _loadedProgress = null;
                    _activeSlot = -2;
                }
            }
        }

        private static void ResetRangeBatch()
        {
            StopRangePress(true);
            StopDropPress(true);
        }

        internal static void ProcessMainThreadActions()
        {
            while (TrainerPlugin.MainThreadActions.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { TrainerPlugin.Log.LogWarning("存档回执更新失败：" + ex.Message); }
            }
        }

        private static string GetSaveFingerprint(int slot)
        {
            var root = GetSaveRoot();
            if (string.IsNullOrEmpty(root)) return "";
            var path = Path.Combine(root, "Save-" + slot + ".sav");
            if (!File.Exists(path)) return "";
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        private static string GetSaveRoot()
        {
            var type = FindGameType("SaveSystem");
            if (type == null) return "";
            var path = ReadStaticString(type, "_persistentDataPath", "persistentDataPath");
            return string.IsNullOrEmpty(path) ? "" : Path.Combine(path, "Saves");
        }

        internal static int GetSaveSlot()
        {
            var type = FindGameType("Level");
            if (type == null) return -1;
            try
            {
                var property = type.GetProperty("SaveIndex", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && TryInt(property.GetValue(null, null), out var slot)) return slot;
                var value = ReadStaticMember(type, "_saveIndex", "SaveIndex");
                return TryInt(value, out slot) ? slot : -1;
            }
            catch { return -1; }
        }

        private static bool IsLevelLoading(object level)
        {
            var type = FindGameType("Level");
            if (type == null) return false;
            foreach (var propertyName in new[] { "IsLoading", "isLoading" })
            {
                var property = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || property.PropertyType != typeof(bool)) continue;
                try { return (bool)property.GetValue(property.GetMethod?.IsStatic == true ? null : level, null)!; }
                catch { }
            }
            return ReadBool(level, "_isLoading", "IsLoading", "isLoading");
        }

        internal static void CaptureCurrentLevels()
        {
            foreach (var controller in Controllers.ToArray())
            {
                foreach (var field in Fields(controller.GetType()))
                {
                    var child = SafeGet(field, controller);
                    if (child == null) continue;
                    var name = child.GetType().Name;
                    var isAbility = IsSubclassNamed(child.GetType(), "Ability");
                    if (!isAbility && !IsSubclassNamed(child.GetType(), "Upgrade")) continue;
                    var key = child.GetType().Name;
                    var dictionary = isAbility ? RealAbilities : RealUpgrades;
                    if (!dictionary.ContainsKey(key)) dictionary[key] = GetCurrentLevel(child);
                }
            }
        }

        internal static void UpdateHandsStatus()
        {
            var hands = ReadHandsInfo();
            TrainerPlugin.Status.HandsCount = hands.Count;
            TrainerPlugin.Status.HandsCapacity = hands.Capacity;
        }

        private static (int Count, int Capacity) ReadHandsInfo()
        {
            foreach (var controller in Controllers.ToArray())
            {
                if (controller is UnityEngine.Object unityController && unityController == null) continue;
                foreach (var field in Fields(controller.GetType()))
                {
                    if (!string.Equals(field.Name, "_hands", StringComparison.OrdinalIgnoreCase)) continue;
                    var hands = SafeGet(field, controller);
                    if (hands == null) continue;
                    return (ReadInt(hands, "Count", "_count"), ReadInt(hands, "MaxCount", "_maxCount"));
                }
            }
            return (-1, -1);
        }

        private static int GetVanillaHandsCapacity(IReadOnlyList<LevelEntry> upgrades)
        {
            var carry = FindLevelObjectsForRuntime().FirstOrDefault(entry => entry.Category == "carry").Owner;
            if (carry == null || !OriginalLevelLists.TryGetValue(carry, out var originalSteps) || originalSteps.Count == 0)
                return -1;
            if (!TryInt(ReadMember(carry, "BaseCapacity", "_baseCapacity"), out var capacity) || capacity <= 0)
                return -1;

            var carryGrade = upgrades.FirstOrDefault(entry => entry.Key == "HandCapacityUpgrade")?.Level;
            if (!carryGrade.HasValue && RealUpgrades.TryGetValue("HandCapacityUpgrade", out var realGrade)) carryGrade = realGrade;
            if (!carryGrade.HasValue) return -1;

            var vanillaLimit = BaseUpgradeLimits.TryGetValue("HandCapacityUpgrade", out var knownLimit)
                ? Math.Min(knownLimit, originalSteps.Count)
                : originalSteps.Count;
            var grade = Math.Max(0, Math.Min(carryGrade.Value, vanillaLimit));
            try
            {
                for (var i = 0; i < grade; i++)
                {
                    if (!TryInt(ReadMember(originalSteps[i], "Value", "_value"), out var stepValue) || stepValue < 0)
                        return -1;
                    capacity = checked(capacity + stepValue);
                }
            }
            catch (OverflowException) { return -1; }
            return capacity;
        }

        internal static int BeginSavedHandAdoption(object hands)
        {
            if (!IsSavedHandsRestoreCall()) return -1;
            var current = ReadInt(hands, "Count", "_count");
            var originalCapacity = ReadInt(hands, "MaxCount", "_maxCount");
            if (originalCapacity <= 0 || current < originalCapacity) return -1;
            WriteMember(hands, current + 1, "_maxCount", "MaxCount");
            return originalCapacity;
        }

        internal static void EndSavedHandAdoption(object hands, int originalCapacity)
        {
            if (originalCapacity > 0) WriteMember(hands, originalCapacity, "_maxCount", "MaxCount");
        }

        private static bool IsSavedHandsRestoreCall()
        {
            try
            {
                return new StackTrace(false).GetFrames().Any(frame =>
                {
                    var method = frame.GetMethod();
                    var type = method?.DeclaringType;
                    return method?.Name == "MoveNext" && type != null && type.Namespace == "Bunker.Core" && type.Name.Contains("<ApplyState>");
                });
            }
            catch { return false; }
        }

        private static IEnumerable<object> FindControllerObjects()
        {
            var game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "Bunker");
            if (game == null) yield break;
            foreach (var type in SafeTypes(game))
            {
                if (type.Name != "AbilityController" && type.Name != "UpgradeController") continue;
                if (!IsGameNamespace(type.Namespace)) continue;
                UnityEngine.Object[] objects;
                try { objects = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None); }
                catch { continue; }
                foreach (var item in objects) if (item != null) yield return item;
            }
        }

        private static IEnumerable<(object Owner, string Category, bool IsAbility)> FindLevelObjectsForRuntime()
        {
            return FindLevelObjects();
        }

        internal static void ApplyNoCooldown(object ability)
        {
            if (!TrainerPlugin.Settings.NoCooldown || TrainerPlugin.Settings.RestoreRequested) return;
            if (IsRunning(ability)) return;
            WriteMember(ability, 0f, "_cooldownLeft");
        }

        internal static bool IsAbilityReady(object ability)
        {
            if (IsRestoreLevelPending(ability)) return false;
            return !IsRunning(ability) && ((TrainerPlugin.Settings.NoCooldown && !TrainerPlugin.Settings.RestoreRequested) || ReadFloat(ability, "_cooldownLeft") <= 0f);
        }

        internal static bool IsRestoreLevelPending(object ability)
        {
            var current = GetCurrentLevel(ability);
            var key = ability.GetType().Name;
            if (!RealAbilities.TryGetValue(key, out var real)) return false;
            var category = Category(key);
            var baseLimit = BaseAbilityLimits.TryGetValue(key, out var limit) ? limit : current;
            var allowed = TrainerPlugin.Settings.IsExpansionEnabled(category) ? GetExpandedLimit(ability, category, true, baseLimit) : baseLimit;
            var desired = TrainerPlugin.Settings.MaxAll && !TrainerPlugin.Settings.RestoreRequested ? allowed : Math.Min(real, allowed);
            return current > desired;
        }

        internal static void OnApplicationFocusLost()
        {
            StopDropPress(true);
            StopRangePress(true);
        }

        internal static void ProcessHeldInputs()
        {
            var input = FindActiveGameComponent("InputProvider");
            var interactor = FindActiveGameComponent("PlayerInteractor");
            if (input == null)
            {
                StopDropPress(true);
                StopRangePress(true);
                return;
            }

            ProcessContinuousDrop(input, interactor);
            ProcessHeldRangePickup(input, interactor);
        }

        private static void ProcessContinuousDrop(object input, object? interactor)
        {
            if (IsInputActionReleasedThisFrame(input, "DropAction")) StopDropPress(false);
            var pressed = IsInputActionPressed(input, "DropAction");
            if (!pressed)
            {
                StopDropPress(false);
                return;
            }

            if (!TrainerPlugin.Settings.HoldToDrop || TrainerPlugin.Settings.RestoreRequested || interactor == null || !IsHeldInputContextAllowed(input, interactor))
            {
                StopDropPress(true);
                return;
            }
            if (_dropPressSuppressedUntilRelease) return;

            if (!_dropPressActive || !ReferenceEquals(_dropPressInput, input))
            {
                _dropPressActive = true;
                _dropPressInput = input;
                _nextDropAt = Time.realtimeSinceStartup + DropHeldActionInterval;
                return;
            }
            if (Time.realtimeSinceStartup < _nextDropAt) return;

            InvokeOriginalDrop(interactor);
            _nextDropAt = Time.realtimeSinceStartup + DropHeldActionInterval;
        }

        internal static void AfterOriginalDropKeyPressed(object interactor)
        {
            if (!TrainerPlugin.Settings.HoldToDrop || TrainerPlugin.Settings.RestoreRequested || _dropPressSuppressedUntilRelease)
                return;
            var input = ReadMember(interactor, "_input", "Input");
            if (input == null || !IsInputActionPressed(input, "DropAction") || !IsHeldInputContextAllowed(input, interactor)) return;
            _dropPressActive = true;
            _dropPressInput = input;
            _nextDropAt = Time.realtimeSinceStartup + DropHeldActionInterval;
        }

        private static void InvokeOriginalDrop(object interactor)
        {
            var method = SafeMethods(interactor.GetType()).FirstOrDefault(candidate => candidate.Name == "TryDrop" && candidate.GetParameters().Length == 0 && candidate.ReturnType == typeof(bool));
            if (method == null) return;
            try { method.Invoke(interactor, null); }
            catch (TargetInvocationException ex) { TrainerPlugin.Log.LogWarning("连续丢弃被游戏拒绝：" + ex.InnerException?.Message); }
            catch (Exception ex) { TrainerPlugin.Log.LogWarning("连续丢弃失败：" + ex.Message); }
        }

        private static void ProcessHeldRangePickup(object input, object? interactor)
        {
            ConsumeRangeRelease(input);
            var pressed = IsInputActionPressed(input, "InteractAction");
            if (!pressed)
            {
                StopRangePress(false);
                return;
            }

            if (!TrainerPlugin.Settings.ExpandCarry || TrainerPlugin.Settings.RestoreRequested || interactor == null || !IsHeldInputContextAllowed(input, interactor))
            {
                StopRangePress(true);
                return;
            }

            var carryLevel = FindCurrentCarryLevel();
            if (carryLevel < PickupFinalLevel)
            {
                StopRangePress(true);
                return;
            }
            if (_rangePressSuppressedUntilRelease) return;
            if (!EnsureRangePress(input, interactor, carryLevel)) return;
            if (_rangePressCount >= _rangePressLimit || Time.realtimeSinceStartup < _nextRangePickupAt) return;

            _lastRangePickupInterval = GetRangePickupInterval(carryLevel);
            _nextRangePickupAt = Time.realtimeSinceStartup + _lastRangePickupInterval;
            if (!HasCapacity(interactor) || !TryGetCurrentAimPoint(interactor, out var aimPoint)) return;
            if (!TryFindNearestRangeItem(interactor, aimPoint, out var candidate, out var candidateId)) return;
            if (!RangePressAttemptedItems.Add(candidateId)) return;

            IsRangeBatch = true;
            try { TryPick(interactor, candidate); }
            finally { IsRangeBatch = false; }
        }

        internal static ClickContext BeginClick(object item, object[] args)
        {
            var interactor = args.FirstOrDefault(a => a != null && a.GetType().Name.Contains("PlayerInteractor"));
            var input = interactor == null ? null : ReadMember(interactor, "_input", "Input");
            if (input != null) ConsumeRangeRelease(input);
            if (interactor == null || input == null || !IsInputActionPressed(input, "InteractAction") ||
                !TrainerPlugin.Settings.ExpandCarry || TrainerPlugin.Settings.RestoreRequested ||
                FindCurrentCarryLevel() < PickupFinalLevel || !IsHeldInputContextAllowed(input, interactor))
                return new ClickContext();

            var transform = GetTransform(item);
            if (transform == null) return new ClickContext();
            var position = args.OfType<Vector3>().FirstOrDefault();
            if (position == default(Vector3)) position = transform.position;
            if (_rangePressSuppressedUntilRelease || !EnsureRangePress(input, interactor, FindCurrentCarryLevel())) return new ClickContext();
            var context = new ClickContext
            {
                Enabled = true,
                TargetPosition = position,
                Interactor = interactor
            };
            if (_rangePressCount >= _rangePressLimit || (!IsRangeBatch && _rangePressCount > 0 && Time.realtimeSinceStartup < _nextRangePickupAt))
                context.BlockOriginal = true;
            return context;
        }

        private static void ConsumeRangeRelease(object input)
        {
            if (!IsInputActionReleasedThisFrame(input, "InteractAction")) return;
            if (ReferenceEquals(_rangeReleaseConsumedInput, input) && _rangeReleaseConsumedFrame == Time.frameCount) return;
            _rangeReleaseConsumedInput = input;
            _rangeReleaseConsumedFrame = Time.frameCount;
            StopRangePress(false);
        }

        internal static void EndClick(object item, object[] args, bool originalSucceeded, ClickContext context)
        {
            if (!originalSucceeded || !context.Enabled || context.Interactor == null) return;
            var input = ReadMember(context.Interactor, "_input", "Input");
            if (input == null || !IsInputActionPressed(input, "InteractAction") ||
                !TrainerPlugin.Settings.ExpandCarry || TrainerPlugin.Settings.RestoreRequested ||
                !IsHeldInputContextAllowed(input, context.Interactor)) return;

            var carryLevel = FindCurrentCarryLevel();
            if (carryLevel < PickupFinalLevel || _rangePressSuppressedUntilRelease || !EnsureRangePress(input, context.Interactor, carryLevel)) return;
            var id = InstanceId(item);
            if (id != 0 && RangePressCountedItems.Add(id))
            {
                _rangePressCount = Math.Min(_rangePressLimit, _rangePressCount + 1);
                _lastRangePickupInterval = GetRangePickupInterval(carryLevel);
                _nextRangePickupAt = Time.realtimeSinceStartup + _lastRangePickupInterval;
            }
        }

        private static bool EnsureRangePress(object input, object interactor, int carryLevel)
        {
            var slot = GetSaveSlot();
            if (_rangePressActive && ReferenceEquals(_rangePressInput, input) && ReferenceEquals(_rangePressInteractor, interactor) && _rangeSlot == slot)
                return true;

            StopRangePress(false);
            _rangePressActive = true;
            _rangePressInput = input;
            _rangePressInteractor = interactor;
            _rangePressCount = 0;
            _rangePressLimit = GetRangeHoldLimit(carryLevel);
            _lastRangePickupInterval = GetRangePickupInterval(carryLevel);
            _nextRangePickupAt = Time.realtimeSinceStartup + _lastRangePickupInterval;
            _rangeSlot = slot;
            return true;
        }

        private static int GetRangeHoldLimit(int carryLevel)
        {
            if (carryLevel <= 15) return 5;
            return Math.Min(30, (carryLevel - 14) * 5);
        }

        private static bool TryFindNearestRangeItem(object interactor, Vector3 aimPoint, out object item, out int itemId)
        {
            item = null!;
            itemId = 0;
            var itemType = FindGameType("PickableItem");
            var playerTransform = GetTransform(interactor);
            var interactDistance = GetInteractDistance(interactor);
            if (itemType == null || playerTransform == null || interactDistance <= 0f) return false;

            var origin = GetViewOrigin(interactor, playerTransform);
            var layerMask = GetInteractableMask(interactor);
            var triggerInteraction = GetTriggerInteraction(interactor);
            UnityEngine.Object[] candidates;
            try { candidates = UnityEngine.Object.FindObjectsByType(itemType, FindObjectsSortMode.None); }
            catch { return false; }

            var nearestDistance = float.MaxValue;
            foreach (var candidate in candidates)
            {
                if (!IsCandidateAlive(candidate)) continue;
                var candidateTransform = GetTransform(candidate);
                if (candidateTransform == null || candidateTransform.IsChildOf(playerTransform) || IsInsideShelf(candidateTransform) || IsPicked(candidate)) continue;
                var id = InstanceId(candidate);
                if (id == 0 || (TrainerPlugin.TestMode && _testRangeCandidateIds != null && !_testRangeCandidateIds.Contains(id))) continue;

                var position = candidateTransform.position;
                var aimDistance = Vector3.Distance(aimPoint, position);
                if (aimDistance > RangePickupRadius || Math.Abs(position.y - aimPoint.y) > 0.75f) continue;
                var normalDistance = Vector3.Distance(origin, position);
                if (normalDistance > interactDistance || !IsLayerInteractable(candidateTransform, layerMask) ||
                    !HasLineOfSight(origin, candidateTransform, normalDistance, layerMask, triggerInteraction) ||
                    !CanInteract(candidate, interactor, position)) continue;

                if (RangePressAttemptedItems.Contains(id) || aimDistance >= nearestDistance) continue;
                nearestDistance = aimDistance;
                item = candidate;
                itemId = id;
            }
            return itemId != 0;
        }

        private static bool TryGetCurrentAimPoint(object interactor, out Vector3 aimPoint)
        {
            if (TrainerPlugin.TestMode && _testAimPoint.HasValue)
            {
                aimPoint = _testAimPoint.Value;
                return true;
            }
            var state = ReadMember(interactor, "_currentState", "CurrentState");
            var lookingObject = state == null ? null : ReadMember(state, "LookingObject");
            if (lookingObject != null && ReadMember(state!, "_lastHitPoint") is Vector3 lastHitPoint)
            {
                aimPoint = lastHitPoint;
                return true;
            }

            var movement = ReadMember(interactor, "_movement", "Movement");
            if (movement != null && ReadMember(movement, "LookingRay", "_lookingRay") is Ray ray)
            {
                var distance = GetInteractDistance(interactor);
                if (distance > 0f && Physics.Raycast(ray, out var hit, distance, ~0, QueryTriggerInteraction.Ignore))
                {
                    aimPoint = hit.point;
                    return true;
                }
            }

            aimPoint = default;
            return false;
        }

        private static bool IsHeldInputContextAllowed(object input, object interactor)
        {
            if (!Application.isFocused || Time.timeScale <= 0.001f || !IsCandidateAlive(interactor) ||
                ReadBool(input, "IsLocked") || ReadBool(input, "IsActionsLocked") || !ReadBool(input, "IsCursorLocked") ||
                ReadBool(interactor, "_isShopOpen")) return false;

            var level = FindActiveGameComponent("Level");
            if (level == null || ReadBool(level, "IsLoading", "_isLoading") || ReadBool(level, "IsExiting", "_isExiting")) return false;
            var escapeMenu = FindActiveGameComponent("EscapeMenu");
            return escapeMenu == null || !ReadBool(escapeMenu, "IsOpened");
        }

        private static bool IsInputActionPressed(object input, string actionName)
        {
            if (TrainerPlugin.TestMode && string.Equals(_testHeldAction, actionName, StringComparison.Ordinal)) return true;
            var action = ReadMember(input, actionName);
            if (action == null) return false;
            var method = SafeMethods(action.GetType()).FirstOrDefault(candidate => candidate.Name == "IsPressed" && candidate.GetParameters().Length == 0);
            try { return method?.Invoke(action, null) is bool pressed && pressed; }
            catch { return false; }
        }

        private static bool IsInputActionReleasedThisFrame(object input, string actionName)
        {
            var action = ReadMember(input, actionName);
            if (action == null) return false;
            var method = SafeMethods(action.GetType()).FirstOrDefault(candidate => candidate.Name == "WasReleasedThisFrame" && candidate.GetParameters().Length == 0);
            try { return method?.Invoke(action, null) is bool released && released; }
            catch { return false; }
        }

        private static void StopRangePress(bool suppressUntilRelease)
        {
            _rangePressActive = false;
            _rangePressInput = null;
            _rangePressInteractor = null;
            _rangePressCount = 0;
            _rangePressLimit = 0;
            _nextRangePickupAt = 0f;
            _rangeSlot = -1;
            RangePressCountedItems.Clear();
            RangePressAttemptedItems.Clear();
            _rangePressSuppressedUntilRelease = suppressUntilRelease;
            IsRangeBatch = false;
        }

        private static void StopDropPress(bool suppressUntilRelease)
        {
            _dropPressActive = false;
            _dropPressInput = null;
            _nextDropAt = 0f;
            _dropPressSuppressedUntilRelease = suppressUntilRelease;
        }

        private static int FindCurrentCarryLevel()
        {
            var values = FindLevelObjectsForRuntime().Where(x => x.Category == "carry").Select(x => GetCurrentLevel(x.Owner)).ToArray();
            return values.Length == 0 ? 0 : values.Max();
        }

        private static bool HasCapacity(object interactor)
        {
            var hands = FindNestedByName(interactor, "Hands", 2);
            if (hands == null) return false;
            var count = ReadInt(hands, "Count", "_count");
            var max = ReadInt(hands, "MaxCount", "_maxCount");
            return max > 0 && count < max;
        }

        private static bool TryPick(object interactor, object item)
        {
            var method = SafeMethods(item.GetType()).FirstOrDefault(m => m.Name == "TryInteract" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.IsInstanceOfType(interactor) && m.GetParameters()[1].ParameterType == typeof(Vector3));
            if (method == null) return false;
            var transform = GetTransform(item);
            if (transform == null) return false;
            if (transform == null || IsPicked(item) || !CanInteract(item, interactor, transform.position)) return false;
            try { return method.Invoke(item, new object[] { interactor, transform.position }) is bool accepted && accepted; }
            catch (TargetInvocationException ex) { TrainerPlugin.Log.LogWarning("范围拾取被游戏拒绝：" + ex.InnerException?.Message); return false; }
            catch (Exception ex) { TrainerPlugin.Log.LogWarning("范围拾取失败：" + ex.Message); return false; }
        }

        private static bool CanInteract(object item, object interactor, Vector3 position)
        {
            var method = SafeMethods(item.GetType()).FirstOrDefault(m => m.Name == "CanInteract" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.IsInstanceOfType(interactor) && m.GetParameters()[1].ParameterType == typeof(Vector3));
            if (method == null) return false;
            try { return method.Invoke(item, new object[] { interactor, position }) is bool allowed && allowed; }
            catch { return false; }
        }

        private static bool IsCandidateAlive(object? candidate)
        {
            if (candidate == null) return false;
            if (candidate is UnityEngine.Object unityObject && unityObject == null) return false;
            var transform = GetTransform(candidate);
            return transform != null && transform.gameObject.activeInHierarchy;
        }

        private static bool IsPicked(object item) => ReadBool(item, "IsPicked", "_isPicked");

        private static Vector3 GetViewOrigin(object interactor, Transform fallback)
        {
            var movement = ReadMember(interactor, "_movement", "Movement");
            var lookingRay = movement == null ? null : ReadMember(movement, "LookingRay", "_lookingRay");
            if (lookingRay is Ray ray) return ray.origin;
            return fallback.position;
        }

        private static float GetInteractDistance(object interactor)
        {
            var state = ReadMember(interactor, "_currentState", "CurrentState");
            var stateDistance = state == null ? 0f : ReadFloat(state, "InteractDistance", "_interactDistance", "Distance");
            return stateDistance > 0f ? stateDistance : ReadFloat(interactor, "_interactDistance");
        }

        private static int GetInteractableMask(object interactor)
        {
            var state = ReadMember(interactor, "_currentState", "CurrentState");
            var layer = state == null ? null : ReadMember(state, "InteractableLayer", "_interactableLayer", "LayerMask");
            if (layer == null) layer = ReadMember(interactor, "_interactableLayer");
            if (layer is LayerMask mask) return mask.value;
            if (layer != null && TryInt(ReadMember(layer, "value", "Value"), out var value)) return value;
            return ~0;
        }

        private static QueryTriggerInteraction GetTriggerInteraction(object interactor)
        {
            var state = ReadMember(interactor, "_currentState", "CurrentState");
            var trigger = state == null ? null : ReadMember(state, "TriggerInteraction", "_triggerInteraction");
            if (trigger is QueryTriggerInteraction query) return query;
            try { if (trigger != null) return (QueryTriggerInteraction)Convert.ToInt32(trigger); }
            catch { }
            return QueryTriggerInteraction.Ignore;
        }

        private static bool IsLayerInteractable(Transform target, int mask) => (mask & (1 << target.gameObject.layer)) != 0;

        private static bool HasLineOfSight(Vector3 origin, Transform target, float distance, int layerMask, QueryTriggerInteraction triggerInteraction)
        {
            if (distance <= 0.01f) return true;
            var direction = (target.position - origin).normalized;
            if (!Physics.Raycast(origin, direction, out var hit, distance + 0.05f, layerMask, triggerInteraction)) return true;
            return hit.transform == target || hit.transform.IsChildOf(target) || target.IsChildOf(hit.transform);
        }

        private static Transform? GetTransform(object obj)
        {
            if (obj is Component component) return component.transform;
            var value = ReadMember(obj, "transform", "Transform");
            return value as Transform;
        }

        private static int InstanceId(object obj)
        {
            try { return obj is UnityEngine.Object unityObject ? unityObject.GetInstanceID() : 0; }
            catch { return 0; }
        }

        internal static void RegisterRewardSource(object source)
        {
            var amount = ReadInt(source, "_pointsPerType", "pointsPerType");
            if (amount <= 0) amount = 10;
            RewardSources[source] = amount;
            ApplyRewardValue(source);
        }

        internal static void ApplyRewardValue()
        {
            foreach (var source in RewardSources.Keys.ToArray()) ApplyRewardValue(source);
        }

        internal static void ApplyRewardValue(object source)
        {
            if (!RewardSources.TryGetValue(source, out var original))
            {
                original = ReadInt(source, "_pointsPerType", "pointsPerType");
                if (original <= 0) original = 10;
                RewardSources[source] = original;
            }
            var reward = original;
            if (TrainerPlugin.Settings.MoreStars && !TrainerPlugin.Settings.RestoreRequested)
            {
                if (TryGetScoredTypesCount(source, out var completedTypeCount)) reward = GetProgressiveReward(completedTypeCount);
                else TrainerPlugin.Log.LogWarning("无法读取游戏已持久化的 ScoredTypesCount，暂时使用原版奖励，避免重复发放累计奖励。");
            }
            WriteMember(source, reward, "_pointsPerType", "pointsPerType");
        }

        private static bool TryGetScoredTypesCount(object source, out int count)
        {
            var value = ReadMember(source, "ScoredTypesCount", "_scoredTypesCount");
            if (TryInt(value, out count) && count >= 0) return true;
            var scored = ReadMember(source, "_scoredTypes");
            if (scored is ICollection collection)
            {
                count = collection.Count;
                return count >= 0;
            }
            count = 0;
            return false;
        }

        internal static List<string> GetActiveFeatures(ModSettings settings)
        {
            var active = new List<string>();
            if (settings.RestoreRequested) return active;
            if (settings.ExpandCarry) active.Add("carry");
            if (settings.ExpandAutoPickup) active.Add("autoPickup");
            if (settings.ExpandAutoPlace) active.Add("autoPlace");
            if (settings.ExpandContinuousPlace) active.Add("continuousPlace");
            if (settings.MoreStars) active.Add("moreStars");
            if (settings.InfiniteStars) active.Add("infiniteStars");
            if (settings.NoCooldown) active.Add("noCooldown");
            if (settings.MaxAll) active.Add("maxAll");
            if (settings.HoldToDrop) active.Add("holdToDrop");
            return active;
        }

        private static void SetLevel(object obj, int value)
        {
            var method = SafeMethods(obj.GetType()).FirstOrDefault(m => m.Name == "SetLevel" && m.GetParameters().Length == 1);
            if (method == null) return;
            var parameter = method.GetParameters()[0].ParameterType;
            var old = GetCurrentLevel(obj);
            try
            {
                method.Invoke(obj, new[] { Convert.ChangeType(value, parameter) });
                if (old != value && IsSubclassNamed(obj.GetType(), "Upgrade")) ApplyUpgrade(obj);
            }
            catch (Exception ex) { TrainerPlugin.Log.LogWarning("设置等级失败 " + obj.GetType().Name + ": " + ex.GetBaseException().Message); }
        }

        private static void ApplyUpgrade(object upgrade)
        {
            foreach (var controller in Controllers)
            {
                if (!Fields(controller.GetType()).Any(field => ReferenceEquals(SafeGet(field, controller), upgrade))) continue;
                var context = ReadMember(controller, "_context", "context");
                if (context == null) return;
                var apply = SafeMethods(upgrade.GetType()).FirstOrDefault(method => method.Name == "Apply" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType.IsInstanceOfType(context));
                if (apply == null) return;
                try { apply.Invoke(upgrade, new[] { context }); }
                catch (Exception ex) { TrainerPlugin.Log.LogWarning("刷新升级效果失败 " + upgrade.GetType().Name + ": " + ex.GetBaseException().Message); }
                return;
            }
        }

        private static int GetCurrentLevel(object obj) => ReadInt(obj, "_currentLevel", "CurrentLevel", "currentLevel");
        private static bool IsRunning(object obj) => ReadBool(obj, "_isRunning", "IsRunning", "isRunning");

        private static int ReadInt(object obj, params string[] names)
        {
            var value = ReadMember(obj, names);
            return TryInt(value, out var parsed) ? parsed : 0;
        }

        private static float ReadFloat(object obj, params string[] names)
        {
            var value = ReadMember(obj, names);
            try { return value == null ? 0f : Convert.ToSingle(value); }
            catch { return 0f; }
        }

        private static bool ReadBool(object obj, params string[] names)
        {
            var value = ReadMember(obj, names);
            return value is bool flag && flag;
        }

        private static bool TryInt(object? value, out int result)
        {
            try { result = value == null ? 0 : Convert.ToInt32(value); return value != null; }
            catch { result = 0; return false; }
        }

        private static object? ReadMember(object obj, params string[] names)
        {
            var member = FindMember(obj.GetType(), names);
            try
            {
                if (member is FieldInfo field) return field.GetValue(obj);
                if (member is PropertyInfo property && property.GetIndexParameters().Length == 0) return property.GetValue(obj, null);
            }
            catch { }
            return null;
        }

        private static void WriteMember(object obj, object value, params string[] names)
        {
            var member = FindMember(obj.GetType(), names);
            try
            {
                if (member is FieldInfo field)
                {
                    field.SetValue(obj, ConvertFor(value, field.FieldType));
                    return;
                }
                if (member is PropertyInfo property && property.CanWrite)
                    property.SetValue(obj, ConvertFor(value, property.PropertyType), null);
            }
            catch (Exception ex) { TrainerPlugin.Log?.LogWarning("无法设置字段 " + string.Join("/", names) + ": " + ex.Message); }
        }

        private static object ConvertFor(object value, Type type)
        {
            if (type.IsInstanceOfType(value)) return value;
            return Convert.ChangeType(value, Nullable.GetUnderlyingType(type) ?? type);
        }

        private static MemberInfo? FindMember(Type type, params string[] names)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            for (var cursor = type; cursor != null; cursor = cursor.BaseType)
            {
                foreach (var name in names)
                {
                    var field = cursor.GetField(name, flags | BindingFlags.DeclaredOnly);
                    if (field != null) return field;
                    var property = cursor.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                    if (property != null) return property;
                    field = cursor.GetFields(flags | BindingFlags.DeclaredOnly).FirstOrDefault(f => string.Equals(f.Name.TrimStart('_'), name.TrimStart('_'), StringComparison.OrdinalIgnoreCase));
                    if (field != null) return field;
                    property = cursor.GetProperties(flags | BindingFlags.DeclaredOnly).FirstOrDefault(p => string.Equals(p.Name, name.TrimStart('_'), StringComparison.OrdinalIgnoreCase));
                    if (property != null) return property;
                }
            }
            return null;
        }

        private static IEnumerable<FieldInfo> Fields(Type type)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var cursor = type; cursor != null; cursor = cursor.BaseType)
                foreach (var field in cursor.GetFields(flags | BindingFlags.DeclaredOnly)) yield return field;
        }

        private static object? SafeGet(FieldInfo field, object target)
        {
            try { return field.GetValue(target); }
            catch { return null; }
        }

        private static object? FindNestedByName(object root, string name, int depth)
        {
            if (root.GetType().Name == name) return root;
            if (depth <= 0) return null;
            foreach (var field in Fields(root.GetType()))
            {
                var child = SafeGet(field, root);
                if (child == null || child is string || child.GetType().IsPrimitive) continue;
                if (child.GetType().Name == name) return child;
                var nested = FindNestedByName(child, name, depth - 1);
                if (nested != null) return nested;
            }
            return null;
        }

        private static object? ReadStaticMember(Type type, params string[] names)
        {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in names)
            {
                try
                {
                    var field = type.GetField(name, flags);
                    if (field != null) return field.GetValue(null);
                    var property = type.GetProperty(name, flags);
                    if (property != null) return property.GetValue(null, null);
                }
                catch { }
            }
            return null;
        }

        private static string ReadStaticString(Type type, params string[] names) => ReadStaticMember(type, names) as string ?? "";
        private static Type? FindGameType(string name)
        {
            var game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "Bunker");
            return game == null ? null : SafeTypes(game).FirstOrDefault(type => type.Name == name && IsGameNamespace(type.Namespace));
        }

        private static bool IsGameNamespace(string? value) => value == "Bunker" || (value != null && value.StartsWith("Bunker.", StringComparison.Ordinal));
        private static IEnumerable<Type> SafeTypes(Assembly assembly) { try { return assembly.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>(); } }
        private static IEnumerable<MethodInfo> SafeMethods(Type type) { try { return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); } catch { return Array.Empty<MethodInfo>(); } }

        private static bool IsInsideShelf(Transform transform)
        {
            for (var cursor = transform; cursor != null; cursor = cursor.parent)
                if (cursor.name.IndexOf("shelf", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static object? FindMemberValue(object obj, string name) => ReadMember(obj, name, "_" + char.ToLowerInvariant(name[0]) + name.Substring(1));

        private static void WriteLevels(object save, Dictionary<string, int> values, string name)
        {
            var member = FindMember(save.GetType(), name, char.ToLowerInvariant(name[0]) + name.Substring(1));
            if (member is FieldInfo field)
            {
                try { field.SetValue(save, values); } catch { }
            }
            else if (member is PropertyInfo property && property.CanWrite)
            {
                try { property.SetValue(save, values, null); } catch { }
            }
            else if (member != null)
            {
                var current = member is FieldInfo fi ? fi.GetValue(save) : ((PropertyInfo)member).GetValue(save, null);
                if (current is IDictionary dict)
                {
                    dict.Clear();
                    foreach (var pair in values) dict[pair.Key] = pair.Value;
                }
            }
        }

        private static List<LevelEntry> CopyEntries(IEnumerable<LevelEntry> entries) => entries.Select(CopyEntry).ToList();

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}


