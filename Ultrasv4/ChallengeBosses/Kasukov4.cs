/*
name: Kasukov4
description: Kasuko challenge boss v4 — 2 taunters taunt Kasuko and attack Whirlpool. Army sync with taunt loop via UltraAsyncv4.
tags: kasuko, challenge boss
*/
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraAsyncv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/GetScrollsv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class Kasukov4
{
    private static IScriptInterface Bot => IScriptInterface.Instance;
    private static CoreBots C => CoreBots.Instance;
    private static CoreEnginev4 Engine => CoreEnginev4.Instance;
    private static CoreUltrav4 Ultra => _Ultra ??= new CoreUltrav4();
    private static CoreUltrav4 _Ultra;
    private static UltraEnhancementsv4 Enh => _Enh ??= new UltraEnhancementsv4();
    private static UltraEnhancementsv4 _Enh;
    private static UltraPotionsv4 Pots => _Pots ??= new UltraPotionsv4();
    private static UltraPotionsv4 _Pots;
    private static GetScrollsv4 Scrolls => _Scrolls ??= new GetScrollsv4();
    private static GetScrollsv4 _Scrolls;
    private static string _fbsMuteFile = "";

    // ── Boss & Map Data ──────────────────────────────────────────
    // Monsters on lavarockshore (2 total):
    // "Whirlpool" [MapID: 1, HP: 1,000,000] — add / priority target for taunters
    // "Kasuko"    [MapID: 2, HP: 5,000,000] — main boss
    private const int WhirlpoolMapID = 1;
    private const int KasukoMapID = 2;

    // ── Taunter Role Names ──────────────────────────────────────
    private const string Taunter1AttackWhirlpool1 = "Taunter1AttackWhirlpool1";
    private const string Taunter2AttackWhirlpool2 = "Taunter2AttackWhirlpool2";

    bool usePotions;
    private CancellationTokenSource _tauntCts = new();
    private DateTime fightStartTime = DateTime.MinValue;
    private string _role = "";

    public bool DontPreconfigure = true;
    public string OptionsStorage = "Kasukov4";
    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself).", 7),
        new Option<bool>("UsePotions", "Use Potions", "Enable buying and consuming recommended potions.", true),
        new Option<int>("PotionQuantity", "Potion Quantity", "How many potions to keep stocked.", 10),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { "Verus DoomKnight" },
        new[] { "Lord of Order" },
        new[] { "Legion Revenant" },
        new[] { "StoneCrusher" },
        new[] { "ArchFiend" },
        new[] { "King's Echo" },
        new[] { "King's Echo" }
    };

    public void ScriptMain(IScriptInterface bot)
    {
        RunBoss();
        Bot.StopSync();
    }

    public void RunBoss()
    {
        C.SetOptions(true);
        _fbsMuteFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Skua", "fbs_mute.sync"
        );
        try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }
        Engine.Boot();
        _tauntCts = new();
        Bot.Events.ScriptStopping -= StopTauntEvent;
        Bot.Events.ScriptStopping += StopTauntEvent;

        try
        {
            Prep();
            Fight();
        }
        finally
        {
            Bot.Events.ScriptStopping -= StopTauntEvent;
            _tauntCts.Cancel();
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
        }
    }

    private bool StopTauntEvent(Exception? e)
    {
        _tauntCts.Cancel();
        return true;
    }

    private bool IsTaunter() => _role == Taunter1AttackWhirlpool1 || _role == Taunter2AttackWhirlpool2;

    private int MyTaunterIndex() => _role == Taunter1AttackWhirlpool1 ? 0 : 1;

    private void EquipPresetClasses()
    {
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
        bool allowDuplicates = armySize > UltraClassesByRole.Length || armySize >= 7;
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("kasuko", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "kasukov4_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("kasukov4_class-v4.sync"));
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        usePotions = Bot.Config!.Get<bool>("UsePotions");

        // Determine role based on equipped class
        string? className = Bot.Player.CurrentClass?.Name;
        if (className == "Verus DoomKnight") _role = Taunter1AttackWhirlpool1;
        else if (className == "Lord of Order") _role = Taunter2AttackWhirlpool2;
        else _role = "Dps";
        C.Logger($"[Kasukov4] Role: {_role} ({className})");

        Enh.ApplyContext("kasuko");

        bool skipThird = IsTaunter();
        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
        if (usePotions)
        {
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: skipThird);
            Pots.UseRecommendedPotions(potionQuant, skipThird: skipThird, ensureStock: false);
        }

        Bot.Sleep(2500);
    }

    private void Fight()
    {
        const string map = "lavarockshore";
        const string boss = "Kasuko";
        const string bossDefeatedTemp = "Molten Heart";

        const string whitemapSync = "kasukov4_Whitemap.sync";
        const string mapSync = "kasukov4_Map.sync";
        const string completionSyncFile = "Kasukov4Completion.sync";
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));

        const int questId = 9254;

        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        bool skipThird = IsTaunter();
        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
        if (usePotions)
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: skipThird);
        Scrolls.GetScrollOfEnrage();

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, whitemapSync, staleThresholdSec: 15, useSkill: false);

        if (usePotions)
            Pots.UseRecommendedPotions(potionQuant, skipThird: skipThird, ensureStock: false);

        if (skipThird)
        {
            C.Logger("[Kasukov4] Taunter detected, equipping Scroll of Enrage.");
            Engine.EquipEnrage();
        }

        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, mapSync, staleThresholdSec: 15, useSkill: true);

        Engine.ChooseBestCell(boss);
        Bot.Player.SetSpawnPoint();

        // ── Taunt loop setup ────────────────────────────────────
        string fightTimeSyncPath = Ultra.ResolveSyncPath("Kasukov4FightTime.sync");
        if (_role == Taunter1AttackWhirlpool1)
        {
            C.Logger("[Kasukov4] Taunter1 (Primary) — setting fight start time.");
            fightStartTime = UltraAsyncv4.SetFightTime(C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 0, 2, cancellationToken: _tauntCts.Token);
        }
        else if (_role == Taunter2AttackWhirlpool2)
        {
            C.Logger("[Kasukov4] Taunter2 — reading fight start time.");
            fightStartTime = UltraAsyncv4.GetFightTime(Ultra, C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 1, 2, cancellationToken: _tauntCts.Token);
        }

        Bot.Sleep(2000);

        // Pre-seed completion sync file so all entries exist before the loop starts.
        string? _username = Bot.Player.Username;
        string? _className = Bot.Player.CurrentClass?.Name;
        if (!string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(_className))
        {
            string _myKey = $"{_username}|{_className}".Replace(":", "-");
            Ultra.UpdateEntry(Ultra.ResolveSyncPath(completionSyncFile), _myKey, "0");
        }

        while (!Bot.ShouldExit)
        {
            // Refresh mute file so FBS plugin stays muted during the fight
            try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }

            if (!Bot.Player.Alive)
            {
                Bot.Wait.ForTrue(() => Bot.Player.Alive, 20);
                continue;
            }

            if (Ultra.CheckArmyProgressBool(() => Bot.TempInv.Contains(bossDefeatedTemp, 1), completionSyncFile, armySize))
            {
                C.Logger("Kasuko defeated. Finishing quest.");
                Bot.Events.ScriptStopping -= StopTauntEvent;
                _tauntCts.Cancel();
                Engine.DisableSkills();
                Engine.Join(map);
                Ultra.PersistentJoinHouse();
                UltraGeneralv4.CompleteQuest(Bot, questId);
                Bot.Sleep(3000);
                break;
            }

            // ── Dynamic targeting ─────────────────────────────────
            // Taunters: attack Whirlpool normally, switch to Kasuko during
            //           taunt pulse window (~1s before to ~3s after) to land the taunt.
            //           Pattern from UltraNulgathv4 Taunter3AttackBlade.
            // Non-taunters: Whirlpool (MapID 1) → Kasuko (MapID 2)
            if (IsTaunter())
            {
                double elapsed = (DateTime.UtcNow - fightStartTime).TotalSeconds;
                int pulseInterval = 5;
                int cycleLength = 2 * pulseInterval; // 10s cycle for 2 taunters
                double timeInCycle = elapsed % cycleLength;
                int myFireTime = MyTaunterIndex() * pulseInterval;
                double startWindow = (myFireTime - 1 + cycleLength) % cycleLength;
                double endWindow = (myFireTime + 3) % cycleLength;

                bool inTauntWindow;
                if (startWindow < endWindow)
                    inTauntWindow = timeInCycle >= startWindow && timeInCycle <= endWindow;
                else
                    inTauntWindow = timeInCycle >= startWindow || timeInCycle <= endWindow;

                if (inTauntWindow)
                {
                    // During taunt window — target Kasuko to land the taunt
                    if (Bot.Player.Target?.Name != boss)
                        Bot.Combat.Attack(boss);
                }
                else
                {
                    // Outside taunt window — attack Whirlpool if alive, else Kasuko
                    if (Bot.Monsters.CurrentAvailableMonsters.Any(x => x != null && x.MapID == WhirlpoolMapID && x.HP > 0))
                    {
                        if (Bot.Player.Target?.MapID != WhirlpoolMapID)
                            Bot.Combat.Attack(WhirlpoolMapID);
                    }
                    else if (Bot.Player.Target?.Name != boss)
                    {
                        Bot.Combat.Attack(boss);
                    }
                }
            }
            else
            {
                // Non-taunter — Whirlpool first, then Kasuko
                if (Bot.Monsters.CurrentAvailableMonsters.Any(x => x != null && x.MapID == WhirlpoolMapID && x.HP > 0))
                {
                    if (Bot.Player.Target?.MapID != WhirlpoolMapID)
                        Bot.Combat.Attack(WhirlpoolMapID);
                }
                else if (Bot.Player.Target?.Name != boss)
                {
                    Bot.Combat.Attack(boss);
                }
            }

            if (usePotions)
                Pots.ActivateEquippedPotion();
            Bot.Sleep(500);
        }
    }
}
