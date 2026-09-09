/*
name: Deimosv4
description: Deimos challenge boss v4 — 7 player army sync with VDK as the sole taunter.
tags: deimos, challenge boss
*/
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraAsyncv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/GetScrollsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class Deimosv4
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

    bool usePotions;
    private CancellationTokenSource _tauntCts = new();
    private DateTime fightStartTime = DateTime.MinValue;

    public bool DontPreconfigure = true;
    public string OptionsStorage = "Deimosv4";
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
        new[] { "Quantum Chronomancer" }
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

    private bool IsTaunter()
    {
        return Bot.Player.CurrentClass?.Name == "Verus DoomKnight";
    }

    private void EquipPresetClasses()
    {
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
        bool allowDuplicates = armySize > UltraClassesByRole.Length || armySize >= 7;
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("deimos", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "deimosv4_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        C.Unbank("Legion Promotion");
        Bot.Drops.Add("Lineage of Devastation");
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("deimosv4_class-v4.sync"));
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        usePotions = Bot.Config!.Get<bool>("UsePotions");

        Enh.ApplyContext("deimos");

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
        const string map = "deimos";
        const string boss = "Devastator Deimos";

        const string whitemapSync = "deimosv4_Whitemap.sync";
        const string mapSync = "deimosv4_Map.sync";
        const string completionSyncFile = "Deimosv4Completion.sync";
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));

        const int questId = 1676;

        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        bool skipThird = IsTaunter();
        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
        if (usePotions)
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: skipThird);
        
        if (skipThird)
            Scrolls.GetScrollOfEnrage();

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, whitemapSync, staleThresholdSec: 15, useSkill: false);

        if (usePotions)
            Pots.UseRecommendedPotions(potionQuant, skipThird: skipThird, ensureStock: false);

        if (skipThird)
        {
            C.Logger("[Deimosv4] Taunter detected, equipping Scroll of Enrage.");
            Engine.EquipEnrage();
        }

        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, mapSync, staleThresholdSec: 15, useSkill: true);

        Engine.ChooseBestCell(boss);
        Bot.Player.SetSpawnPoint();

        // ── Taunt loop setup ────────────────────────────────────
        string fightTimeSyncPath = Ultra.ResolveSyncPath("Deimosv4FightTime.sync");
        if (skipThird)
        {
            C.Logger("[Deimosv4] Taunter (VDK) — setting fight start time and starting taunt loop.");
            fightStartTime = UltraAsyncv4.SetFightTime(C, fightTimeSyncPath);
            // offset 0, 1 taunter, so it taunts exactly on cooldown every 15s.
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 0, 1, cancellationToken: _tauntCts.Token);
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

            if (Ultra.CheckArmyProgressBool(() => UltraGeneralv4.IsQuestGreen(Bot, questId), completionSyncFile, armySize))
            {
                C.Logger("Deimos defeated. Finishing quest.");
                Bot.Events.ScriptStopping -= StopTauntEvent;
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
