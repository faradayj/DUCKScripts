/*
name: Xyfragv4
description: Xyfrag helper for army farming Xyfrag with double taunter synchronization.
tags: Ultra, Xyfrag, voidxyfrag
*/

//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/GetScrollsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraAsyncv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreStory.cs

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Skua.Core.Interfaces;

public class Xyfragv4
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

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { "Lord of Order" },
        new[] { "Legion Revenant" },
        new[] { "Verus DoomKnight" },
        new[] { "Legendary Hero" },
        new[] { "ArchFiend" },
        new[] { "Dragon of Time" },
        new[] { "Dragon of Time" }
    };

    private CancellationTokenSource _tauntCts = new();
    private DateTime fightStartTime = DateTime.MinValue;
    private string _role = "";

    public void ScriptMain(IScriptInterface bot)
    {
        RunBoss();
        Bot.StopSync();
    }

    public void RunBoss()
    {
        C.SetOptions(true);
        Bot.Options.RejectAllDrops = false;
        Bot.Drops.RejectElse = false;
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

    private void EquipPresetClasses()
    {
        int armySize = 7;
        bool allowDuplicates = true;

        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("xyfrag", UltraClassesByRole);

        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "ultra_xyfrag_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        // Clear stale class registrations from previous runs/other accounts
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("ultra_xyfrag_class-v4.sync"));

        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        string? className = Bot.Player.CurrentClass?.Name;
        if (className == "Verus DoomKnight") _role = "Taunter1";
        else if (className == "Legion Revenant") _role = "Taunter2";
        else _role = "Dps";

        Enh.ApplyContext("xyfrag");

        C.Logger($"[Xyfragv4] Role: {_role} ({className})");
    }

    private void Fight()
    {
        const string map = "voidxyfrag";
        const string boss = "Xyfrag";

        const string waitSyncFile = "ultra_xyfrag.sync";
        const string fightTimeSyncFile = "UltraXyfragFightTime.sync";
        const string completionSyncFile = "UltraXyfragCompletion.sync";
        int armySize = 7;

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(fightTimeSyncFile));
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        C.AddDrop("Xyfrag's ??? Essence", "Xyfrag’s ??? Essence");

        C.EnsureAcceptmultiple(9091, 8653);

        bool skipThird = _role == "Taunter1" || _role == "Taunter2";
        Pots.EnsureRecommendedPotions(skipThird: skipThird, context: "xyfrag");

        if (skipThird)
        {
            Scrolls.GetScrollOfEnrage();
        }

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: false);

        Pots.UseRecommendedPotions(skipThird: skipThird, ensureStock: false, context: "xyfrag");

        if (skipThird)
        {
            C.Logger("[Xyfragv4] Taunter detected, equipping Scroll of Enrage.");
            Engine.EquipEnrage();
        }

        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: true);

        Engine.ChooseBestCell(boss);
         
        Bot.Player.SetSpawnPoint();
        Bot.Sleep(2000);

        // Pre-seed completion sync file so all 7 entries exist before the loop starts.
        string? _username = Bot.Player.Username;
        string? _className = Bot.Player.CurrentClass?.Name;
        if (!string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(_className))
        {
            string _myKey = $"{_username}|{_className}".Replace(":", "-");
            Ultra.UpdateEntry(Ultra.ResolveSyncPath(completionSyncFile), _myKey, "0");
        }

        string fightTimeSyncPath = Ultra.ResolveSyncPath(fightTimeSyncFile);

        if (_role == "Taunter1")
        {
            C.Logger("[Xyfragv4] Taunter1 (Primary) — setting fight start time.");
            fightStartTime = UltraAsyncv4.SetFightTime(C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 0, 2, cancellationToken: _tauntCts.Token);
        }
        else if (_role == "Taunter2")
        {
            C.Logger("[Xyfragv4] Taunter2 (Secondary) — reading fight start time.");
            fightStartTime = UltraAsyncv4.GetFightTime(Ultra, C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 1, 2, cancellationToken: _tauntCts.Token);
        }

        while (!Bot.ShouldExit)
        {
            // Refresh mute file so FBS plugin stays muted during the fight
            try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }

            while (!Bot.Player.Alive && !Bot.ShouldExit)
            {
                try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }
                Bot.Sleep(500);
            }
            Engine.ChooseBestCell(boss);
            Bot.Player.SetSpawnPoint();


            if (Ultra.CheckArmyProgressBool(() => InventoryHasItems(), completionSyncFile, armySize))
            {
                C.Logger("All players acquired Xyfrag's ??? Essence. Farm complete.");
                Bot.Events.ScriptStopping -= StopTauntEvent;
                _tauntCts.Cancel();
                Engine.DisableSkills();
                Engine.Join(map);
                Ultra.PersistentJoinHouse();
                break;
            }

            Bot.Combat.Attack(boss);
            Pots.ActivateEquippedPotion();

            Bot.Sleep(500);
        }
    }

    bool InventoryHasItems()
    {
        return C.CheckInventory("Xyfrag's ??? Essence", quant: 1, toInv: false) ||
               C.CheckInventory("Xyfrag’s ??? Essence", quant: 1, toInv: false);
    }
}
