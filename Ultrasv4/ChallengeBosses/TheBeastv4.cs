/*
name: TheBeastv4
description: The Beast challenge boss v4 — 7 player army sync with no special mechanics.
tags: the beast, challenge boss, sevencircleswar
*/
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class TheBeastv4
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

    public bool DontPreconfigure = true;
    public string OptionsStorage = "TheBeastv4";
    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself).", 7),
        new Option<bool>("UsePotions", "Use Potions", "Enable buying and consuming recommended potions.", true),
        new Option<int>("PotionQuantity", "Potion Quantity", "How many potions to keep stocked.", 10),
        new Option<int>("FarmBeastSoulAmount", "Farm Beast Soul Amount", "Amount of Beast Souls to farm. If 0, completes the quest once and exits.", 0),
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

        try
        {
            Prep();
            Fight();
        }
        finally
        {
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
        }
    }

    private void EquipPresetClasses()
    {
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
        bool allowDuplicates = armySize > UltraClassesByRole.Length || armySize >= 7;
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("the beast", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "thebeastv4_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        C.Unbank("Legion Promotion");
        Bot.Drops.Add("Lineage of Devastation");
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("thebeastv4_class-v4.sync"));
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        usePotions = Bot.Config!.Get<bool>("UsePotions");

        Enh.ApplyContext("the beast");

        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
        if (usePotions)
        {
            Pots.EnsureRecommendedPotions(potionQuant);
            Pots.UseRecommendedPotions(potionQuant, ensureStock: false);
        }

        Bot.Sleep(2500);
    }

    private void Fight()
    {
        const string map = "sevencircleswar";
        const string boss = "The Beast";

        const string whitemapSync = "thebeastv4_Whitemap.sync";
        const string mapSync = "thebeastv4_Map.sync";
        const string completionSyncFile = "TheBeastv4Completion.sync";
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));

        const int questId = 1675;
        int targetAmount = Bot.Config!.Get<int>("FarmBeastSoulAmount");

        if (targetAmount > 0)
            Bot.Drops.Add("Beast Soul");

        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
        if (usePotions)
            Pots.EnsureRecommendedPotions(potionQuant);

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, whitemapSync, staleThresholdSec: 15, useSkill: false);

        if (usePotions)
            Pots.UseRecommendedPotions(potionQuant, ensureStock: false);

        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, mapSync, staleThresholdSec: 15, useSkill: true);

        C.Jump("r17", "Left");
        Bot.Player.SetSpawnPoint();
        Bot.Sleep(2000);

        string? _username = Bot.Player.Username;
        string? _className = Bot.Player.CurrentClass?.Name;
        string _myKey = "";
        if (!string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(_className))
            _myKey = $"{_username}|{_className}".Replace(":", "-");

        while (!Bot.ShouldExit)
        {
            if (targetAmount > 0 && C.CheckInventory("Beast Soul", targetAmount))
            {
                C.Logger($"Reached target amount of Beast Soul: {targetAmount}.");
                break;
            }

            if (!UltraGeneralv4.IsQuestGreen(Bot, questId) && !Bot.Quests.IsInProgress(questId))
                UltraGeneralv4.EnsureAcceptOnce(Bot, questId);

            Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));
            if (!string.IsNullOrEmpty(_myKey))
                Ultra.UpdateEntry(Ultra.ResolveSyncPath(completionSyncFile), _myKey, "0");

            while (!Bot.ShouldExit)
            {
                try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }

                if (!Bot.Player.Alive)
                {
                    Bot.Wait.ForTrue(() => Bot.Player.Alive, 20);
                    continue;
                }

                if (Ultra.CheckArmyProgressBool(() => UltraGeneralv4.IsQuestGreen(Bot, questId), completionSyncFile, armySize))
                {
                    C.Logger("The Beast defeated. Finishing quest.");
                    Bot.Combat.CancelTarget();
                    Bot.Sleep(1000);
                    UltraGeneralv4.CompleteQuest(Bot, questId);
                    Bot.Sleep(3000);

                    if (targetAmount <= 0)
                    {
                        Engine.DisableSkills();
                        Engine.Join(map);
                        Ultra.PersistentJoinHouse();
                        return;
                    }
                    break;
                }

                Bot.Combat.Attack(boss);
                if (usePotions)
                    Pots.ActivateEquippedPotion();
                Bot.Sleep(500);
            }
        }

        if (targetAmount > 0)
        {
            Engine.DisableSkills();
            Engine.Join(map);
            Ultra.PersistentJoinHouse();
        }
    }
}
