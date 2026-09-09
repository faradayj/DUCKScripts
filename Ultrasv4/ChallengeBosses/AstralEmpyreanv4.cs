/*
name: AstralEmpyreanv4
description: Two-taunter strategy for Astral Empyrean with aura-based taunting and army synchronization.
tags: null
*/
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraAsyncv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/GetScrollsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraDeathv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class AstralEmpyreanv4
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
    private static UltraDeathv4 Death => _Death ??= new UltraDeathv4();
    private static UltraDeathv4 _Death;
    private static string _fbsMuteFile = "";

    bool usePotions;
    private CancellationTokenSource _tauntCts = new();
    private DateTime fightStartTime = DateTime.MinValue;
    private string _role = "";
    private CancellationTokenSource _wipeCts = new();
    private System.Threading.ManualResetEvent _retreatComplete = new(false);
    private UltraDeathv4.RetryCounter _deathRetryCounter = new();
    private const int MaxDeathRetries = 3;
    public bool DontPreconfigure = true;
    public string OptionsStorage = "AstralEmpyreanv4";
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
        new[] { "ArchPaladin" },
        new[] { "Lord of Order" },
        new[] { "Quantum Chronomancer", "Continuum Chronomancer" },
        new[] { "StoneCrusher" },
        new[] { "Legion Revenant" },
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

        if (!Bot.Quests.IsUnlocked(9803))
        {
            C.Logger("Quest not unlocked: Asterism's Toll, we'll continue anyway");
            Bot.Quests.UpdateQuest(9803);
        }

        try
        {
            // Daily mode: existing death-retry loop with quest completion
            while (_deathRetryCounter.Value < MaxDeathRetries)
            {
                Engine.Boot();
                _tauntCts?.Cancel();
                _tauntCts = new();
                _wipeCts = new();
                _retreatComplete.Reset();

                Bot.Events.ScriptStopping -= StopTauntEvent;
                Bot.Events.ScriptStopping += StopTauntEvent;

                int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
                UltraDeathv4.StartWipeMonitor(
                    C, armySize, _wipeCts, _retreatComplete,
                    () => UltraDeathv4.PerformRetreat(C, armySize, MaxDeathRetries, _deathRetryCounter, "AstralEmpyreanRetreat.sync")
                );

                Prep();
                Fight();

                _retreatComplete.WaitOne();
            }
        }
        finally
        {
            Bot.Events.ScriptStopping -= StopTauntEvent;
            _tauntCts.Cancel();
            _wipeCts.Cancel();
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
        }
    }

    private void EquipPresetClasses()
    {
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
        bool allowDuplicates = armySize > UltraClassesByRole.Length || armySize >= 7;
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("astral empyrean", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "astralempyreanv4_class-v4.sync", allowDuplicates);
    }

    private bool StopTauntEvent(Exception? e)
    {
        _tauntCts.Cancel();
        return true;
    }

    private bool IsTaunter() => _role == "Taunter1" || _role == "Taunter2" || _role == "Taunter3";

    private void Prep()
    {
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("astralempyreanv4_class-v4.sync"));
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        usePotions = Bot.Config!.Get<bool>("UsePotions");

        string? className = Bot.Player.CurrentClass?.Name;
        if (className == "Verus DoomKnight") _role = "Taunter1";
        else if (className == "Lord of Order") _role = "Taunter2";
        else if (className == "Legion Revenant") _role = "Taunter3";
        else _role = "Dps";
        C.Logger($"[AstralEmpyreanv4] Role: {_role} ({className})");

        Enh.ApplyContext("astral empyrean");



        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
        if (usePotions)
        {
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: IsTaunter(), context: "AstralEmpyrean");
            Pots.UseRecommendedPotions(potionQuant, skipThird: IsTaunter(), ensureStock: false, context: "AstralEmpyrean");
        }

        Bot.Quests.UpdateQuest(9802);
        Bot.Sleep(2500);
    }



    private void Fight()
    {
        const string map = "astralshrine";
        const string boss = "Astral Empyrean";
        const string bossDefeatedTemp = "Astral's Supernova";

        const string whitemapSync = "AstralEmpyreanv4_Whitemap.sync";
        const string mapSync = "AstralEmpyreanv4_Map.sync";
        const string completionSyncFile = "AstralEmpyreanv4Completion.sync";
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));

        const int questId = 9803;

        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        bool skipThird = IsTaunter();
        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
        if (usePotions)
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: skipThird, context: "AstralEmpyrean");
        Scrolls.GetScrollOfEnrage();

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, whitemapSync, staleThresholdSec: 15, useSkill: false);

        if (usePotions)
            Pots.UseRecommendedPotions(potionQuant, skipThird: skipThird, ensureStock: false, context: "AstralEmpyrean");

        if (skipThird)
        {
            C.Logger("[AstralEmpyreanv4] Taunter detected, equipping Scroll of Enrage.");
            Engine.EquipEnrage();
        }

        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, mapSync, staleThresholdSec: 15, useSkill: true);

        Engine.ChooseBestCell(boss);
        Bot.Player.SetSpawnPoint();

        string fightTimeSyncPath = Ultra.ResolveSyncPath("AstralEmpyreanFightTime.sync");
        if (_role == "Taunter1")
        {
            C.Logger("[AstralEmpyreanv4] Taunter1 (Primary) — setting fight start time.");
            fightStartTime = UltraAsyncv4.SetFightTime(C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 0, 3, cancellationToken: _tauntCts.Token);
        }
        else if (_role == "Taunter2")
        {
            C.Logger("[AstralEmpyreanv4] Taunter2 — reading fight start time.");
            fightStartTime = UltraAsyncv4.GetFightTime(Ultra, C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 1, 3, cancellationToken: _tauntCts.Token);
        }
        else if (_role == "Taunter3")
        {
            C.Logger("[AstralEmpyreanv4] Taunter3 — reading fight start time.");
            fightStartTime = UltraAsyncv4.GetFightTime(Ultra, C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 2, 3, cancellationToken: _tauntCts.Token);
        }

        // Zone listener must be active in BOTH modes — the zone mechanic (A/B safe boxes)
        // applies whether we're doing the daily or farming drops. Dying to the zone
        // mechanic fails the fight either way.
        Bot.Events.ExtensionPacketReceived += AstralZoneListener;
        Bot.Sleep(2000);

        // Pre-seed completion sync file so all 4 entries exist before the loop starts.
        string? _username = Bot.Player.Username;
        string? _className = Bot.Player.CurrentClass?.Name;
        if (!string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(_className))
        {
            string _myKey = $"{_username}|{_className}".Replace(":", "-");
            Ultra.UpdateEntry(Ultra.ResolveSyncPath(completionSyncFile), _myKey, "0");
        }

        while (!Bot.ShouldExit && !_wipeCts.IsCancellationRequested)
        {
            // Refresh mute file so FBS plugin stays muted during the fight
            try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }

            if (!Bot.Player.Alive)
            {
                while (!Bot.Player.Alive && !Bot.ShouldExit)
                {
                    try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }
                    Bot.Sleep(500);
                }

                if (!Bot.ShouldExit)
                {
                    Engine.ChooseBestCell(boss);
                    Bot.Player.SetSpawnPoint();
                }
                continue;
            }

            // Daily: check boss temp item → complete quest
            if (Ultra.CheckArmyProgressBool(() => Bot.TempInv.Contains(bossDefeatedTemp, 1), completionSyncFile, armySize))
            {
                C.Logger("Boss defeated. Finishing quest.");
                _wipeCts.Cancel();
                Bot.Events.ScriptStopping -= StopTauntEvent;
                _tauntCts.Cancel();
                Engine.DisableSkills();
                Engine.Join(map);
                Ultra.PersistentJoinHouse();
                Bot.Events.ExtensionPacketReceived -= AstralZoneListener;
                UltraGeneralv4.CompleteQuest(Bot, questId);
                Bot.Sleep(3000);
                _deathRetryCounter.Value = MaxDeathRetries;
                _retreatComplete.Set();
                break;
            }

            // Default — attack the boss
            if (Bot.Player.Target?.Name != boss)
                Bot.Combat.Attack(boss);

            if (usePotions)
                Pots.ActivateEquippedPotion();

            Bot.Sleep(500);
        }
    }

    public async void AstralZoneListener(dynamic packet)
    {
        if (packet?["params"]?.type?.ToString() != "json")
            return;
        if (!Bot.Player.Alive)
            return;
        dynamic data = packet["params"].dataObj;
        if (data?.cmd?.ToString() != "event")
            return;
        string? zoneSet = data?.args?.zoneSet?.ToString();
        if (string.IsNullOrEmpty(zoneSet))
            return;

        int x = 0, y = 0;

        switch (zoneSet.ToUpper())
        {
            case "B": // Red on bottom - GO UP — center of safe box
                x = 240;
                y = 200;
                break;
            case "A": // Red on top - GO DOWN — center of safe box
                x = 600;
                y = 429;
                break;
            default:
                return;
        }

        _ = Task.Run(() => Bot.Player.WalkTo(x, y));
    }
}
