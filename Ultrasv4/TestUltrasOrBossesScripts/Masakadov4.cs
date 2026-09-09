/*
name: Masakadov4
description: Challenge Boss Template v4 — single-taunter fight flow with synced class equip. Copy and adapt for specific bosses.
tags: null
*/
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraAsyncv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraCounterAttackv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/GetScrollsv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
using System;
using System.IO;
using System.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class Masakadov4
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
    private static string _fbsMuteFile = "";

    bool usePotions;
    private CancellationTokenSource _tauntCts = new();
    private string _role = "";
    public bool DontPreconfigure = true;
    public string OptionsStorage = "Masakadov4";
    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself).", 4),
        new Option<string>("TauntClass1", "Taunt Class 1", "First taunter class. Leave empty for no taunter.", "ArchPaladin"),
        new Option<string>("TauntClass2", "Taunt Class 2", "Second taunter class. Leave empty for single taunter.", "Lord of Order"),
        new Option<string>("Class1", "Class 1", "Preset class 1 to auto-equip before the fight.\nUse format: ClassName,Username.\nOnly type ClassName if you want it to be random.", "King's Echo"),
        new Option<string>("Class2", "Class 2", "Preset class 2 to auto-equip before the fight.\nUse format: ClassName,Username.\nOnly type ClassName if you want it to be random.", "StoneCrusher"),
        new Option<string>("Class3", "Class 3", "Preset class 3 to auto-equip before the fight.\nUse format: ClassName,Username.\nOnly type ClassName if you want it to be random.", "ArchPaladin"),
        new Option<string>("Class4", "Class 4", "Preset class 4 to auto-equip before the fight.\nUse format: ClassName,Username.\nOnly type ClassName if you want it to be random.", "Lord of Order"),
        new Option<bool>("DoEnh", "Do Enhancements",  "Auto-Enhance Gear properly for the fight", true),
        new Option<bool>("UsePotions", "Use Potions", "Enable buying and consuming recommended potions.", true),
        new Option<int>("PotionQuantity", "Potion Quantity", "How many potions to keep stocked.", 10),
        CoreBots.Instance.SkipOptions,
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

        UltraCounterAttackv4.Enable();

        try
        {
            _tauntCts?.Cancel();
            _tauntCts = new();
            Bot.Events.ScriptStopping -= StopTauntEvent;
            Bot.Events.ScriptStopping += StopTauntEvent;

            Prep();
            Fight();
        }
        finally
        {
            Bot.Events.ScriptStopping -= StopTauntEvent;
            UltraCounterAttackv4.Disable();
            _tauntCts.Cancel();
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
        }
    }

    private void EquipPresetClasses()
    {
        UltraGeneralv4.EquipPresetClasses(Ultra, Bot, "masakadov4_class.sync");
    }

    private void Prep()
    {
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        usePotions = Bot.Config!.Get<bool>("UsePotions");

        string? className = Bot.Player.CurrentClass?.Name;
        string? tc1 = Bot.Config!.Get<string>("TauntClass1");
        string? tc2 = Bot.Config!.Get<string>("TauntClass2");
        bool hasTwoTaunters = !string.IsNullOrWhiteSpace(tc1) && !string.IsNullOrWhiteSpace(tc2);

        if (hasTwoTaunters)
        {
            if (className == tc1) _role = "Taunter1";
            else if (className == tc2) _role = "Taunter2";
            else _role = "Dps";
        }
        else
        {
            if (className == tc1) _role = "Taunter";
            else _role = "Dps";
        }
        C.Logger($"[Masakadov4] Role: {_role} ({className})");

        if (Bot.Config!.Get<bool>("DoEnh"))
            DoEnhs();

        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");

        Bot.Sleep(2500);
    }

    void DoEnhs() => Enh.Apply();

    private bool IsTaunter()
    {
        return _role is "Taunter" or "Taunter1" or "Taunter2";
    }

    private bool StopTauntEvent(Exception? e)
    {
        _tauntCts.Cancel();
        return true;
    }

    private void Fight()
    {
        const string map = "victormatsuri";
        const string boss = "Masakadov4";
        const string bossDefeatedTemp = "Agehachou Crest";

        const string waitSyncFile = "masakadov4_ready.sync";
        const string fightTimeSyncFile = "masakadov4_fighttime.sync";
        const string completionSyncFile = "masakadov4_completion.sync";
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));

        
        const int questId = 10295;

        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(fightTimeSyncFile));
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        bool skipThird = IsTaunter();
        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
        if (usePotions)
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: skipThird);

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: false);

        if (usePotions)
            Pots.UseRecommendedPotions(potionQuant, skipThird: skipThird, ensureStock: false);

        if (skipThird)
        {
            C.Logger("[Masakadov4] Taunter detected, equipping Scroll of Enrage.");
            Engine.EquipEnrage();
        }

        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: true);

        Engine.ChooseBestCell(boss);
        Bot.Player.SetSpawnPoint();

        string fightTimeSyncPath = Ultra.ResolveSyncPath(fightTimeSyncFile);
        bool hasTwoTaunters = !string.IsNullOrWhiteSpace(Bot.Config!.Get<string>("TauntClass2"));

        if (hasTwoTaunters)
        {
            if (_role == "Taunter1")
            {
                C.Logger("[Masakadov4] Taunter1 (Primary) — setting fight start time.");
                DateTime fightStart = UltraAsyncv4.SetFightTime(C, fightTimeSyncPath);
                UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStart, 0, 2, cancellationToken: _tauntCts.Token);
            }
            else if (_role == "Taunter2")
            {
                C.Logger("[Masakadov4] Taunter2 (Secondary) — reading fight start time.");
                DateTime fightStart = UltraAsyncv4.GetFightTime(Ultra, C, fightTimeSyncPath);
                UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStart, 1, 2, cancellationToken: _tauntCts.Token);
            }
        }
        else if (_role == "Taunter")
        {
            C.Logger("[Masakadov4] Taunter detected — starting taunt loop.");
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, DateTime.UtcNow, 0, 1, cancellationToken: _tauntCts.Token);
        }

        Bot.Sleep(2000);

        // Pre-seed completion sync file so all 4 entries exist before the loop starts.
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

            if (Ultra.CheckArmyProgressBool(() => Bot.TempInv.Contains(bossDefeatedTemp, 1), completionSyncFile))
            {
                C.Logger("Boss defeated. Finishing quest.");
                Bot.Events.ScriptStopping -= StopTauntEvent;
                UltraCounterAttackv4.Disable();
                _tauntCts.Cancel();
                Engine.DisableSkills();
                Engine.Join(map);
                Ultra.PersistentJoinHouse();
                UltraGeneralv4.CompleteQuest(Bot, questId);
                Bot.Sleep(3000);
                break;
            }

            Bot.Combat.Attack(boss);

            if (usePotions)
                Pots.ActivateEquippedPotion();
            Bot.Sleep(500);
        }
    }
}
