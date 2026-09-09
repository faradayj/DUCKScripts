/*
name: Nightmare Carnax v4
description: Nightmare Carnax helper for army farming Calamitous Ruin using V4 Orchestration.
tags: Ultra, Carnax, Nightmare Carnax, v4
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreStory.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs

using System;
using System.IO;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class NightmareCarnax
{
    private static IScriptInterface Bot => IScriptInterface.Instance;
    private static CoreBots C => CoreBots.Instance;
    private static CoreAdvanced Adv => _Adv ??= new CoreAdvanced();
    private static CoreAdvanced _Adv;
    private static CoreEnginev4 Engine => CoreEnginev4.Instance;
    private static CoreUltrav4 Ultra => _Ultra ??= new CoreUltrav4();
    private static CoreUltrav4 _Ultra;
    private static UltraEnhancementsv4 Enh => _Enh ??= new UltraEnhancementsv4();
    private static UltraEnhancementsv4 _Enh;
    private static UltraPotionsv4 Pots => _Pots ??= new UltraPotionsv4();
    private static UltraPotionsv4 _Pots;

    private static string _fbsMuteFile = "";
    
    public bool DontPreconfigure = true;
    public string OptionsStorage = "NightmareCarnax-v4";

    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself).", 5),
        new Option<int>("Quant", "Synthetic Viscera Quantity", "How many Synthetic Viscera to farm.", 350),
        new Option<bool>("FarmCalamitous", "Farm Calamitous Ruin", "Also farm Calamitous Ruin drops.", false),
        new Option<bool>("UsePotions", "Use Potions", "Enable buying and consuming recommended potions.", true),
        new Option<int>("PotionQuantity", "Potion Quantity", "How many potions to keep stocked.", 10),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { "StoneCrusher" },
        new[] { "Dragon of Time" },
        new[] { "ArchPaladin" },
        new[] { "Lord of Order" },
        new[] { "Dragon of Time" } // 5th Player
    };

    public void ScriptMain(IScriptInterface bot)
    {
        C.SetOptions(true);
        _fbsMuteFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Skua", "fbs_mute.sync"
        );
        try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }
        
        Engine.Boot();

        Bot.Events.ExtensionPacketReceived += DarkCarnaxListener;
        try
        {
            Prep();
            Fight();
        }
        finally
        {
            Bot.Events.ExtensionPacketReceived -= DarkCarnaxListener;
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
            Bot.StopSync();
        }
    }

    private void EquipPresetClasses()
    {
        int armySize = Bot.Config!.Get<int>("ArmySize");
        bool allowDuplicates = armySize > UltraClassesByRole.Length || armySize >= 5; // DoT is duplicated by default in 5-man
        
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("nightmare carnax", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "nightmarecarnax_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("nightmarecarnax_class-v4.sync"));
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        Enh.ApplyContext("nightmare carnax");

        if (Bot.Config!.Get<bool>("UsePotions"))
        {
            int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: false);
            Pots.UseRecommendedPotions(potionQuant, skipThird: false, ensureStock: false);
        }

        Bot.Sleep(2500);
    }

    private bool HasBossTarget(string boss) =>
        Bot.Player.HasTarget &&
        string.Equals(Bot.Player.Target?.Name, boss, StringComparison.OrdinalIgnoreCase);

    private void Fight()
    {
        const string map = "darkcarnax";
        const string boss = "Nightmare Carnax";

        string syncPath = Ultra.ResolveSyncPath("UltraItemCheck.sync");
        string wipeDeadSyncPath = Ultra.ResolveSyncPath("NightmareCarnaxWipeDead.sync");
        string wipeAliveSyncPath = Ultra.ResolveSyncPath("NightmareCarnaxWipeAlive.sync");
        
        Ultra.ClearSyncFile(syncPath);
        Ultra.ClearSyncFile(wipeDeadSyncPath);
        Ultra.ClearSyncFile(wipeAliveSyncPath);
        Bot.Sleep(2500);

        bool farmCalamitous = Bot.Config!.Get<bool>("FarmCalamitous");
        if (farmCalamitous)
            C.AddDrop("Calamitous Ruin");
        C.AddDrop("Synthetic Viscera");
        C.RegisterQuests(8872);

        int armySize = Math.Max(1, Bot.Config.Get<int>("ArmySize"));
        int quant = Math.Max(1, Bot.Config.Get<int>("Quant"));
        
        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, "carnax_wait.sync", useSkill: false);
        
        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, "carnax_wait.sync", useSkill: true);

        var (bestCell, _) = Engine.ChooseBestCell(boss);
        Bot.Player.SetSpawnPoint();

        C.Logger("Fight start synced.");

        bool armyWipeDetected = false;

        while (!Bot.ShouldExit)
        {
            try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }

            // Utilizing the V4 framework's built-in wipe detection for non-taunter scripts
            if (UltraGeneralv4.ArmyWipeHelperWithNoTaunters(Ultra, Bot, wipeDeadSyncPath, wipeAliveSyncPath, ref armyWipeDetected))
            {
                continue;
            }

            if (!Bot.Player.Alive)
            {
                Bot.Wait.ForTrue(() => Bot.Player.Alive, 20);
                continue;
            }

            bool isDone = false;
            if (farmCalamitous)
                isDone = C.CheckInventory("Synthetic Viscera", quant) && C.CheckInventory("Calamitous Ruin", quant);
            else
                isDone = C.CheckInventory("Synthetic Viscera", quant);

            if (Ultra.CheckArmyProgressBool(() => isDone, syncPath, armySize))
            {
                C.Logger("All players finished farm.");
                Ultra.PersistentJoinHouse();
                C.CancelRegisteredQuests();
                Adv.GearStore(true, true);
                break;
            }

            if (!HasBossTarget(boss))
                Bot.Combat.Attack(boss);

            if (Bot.Config!.Get<bool>("UsePotions"))
                Pots.ActivateEquippedPotion();
                
            Bot.Sleep(100);
        }
    }

    private void DarkCarnaxListener(dynamic packet)
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

        int y = Bot.Random.Next(380, 475);
        int x = 0;

        switch (zoneSet.ToUpper())
        {
            case "A":
                x = Bot.Random.Next(600, 931);
                break;
            case "B":
                x = Bot.Random.Next(25, 326);
                break;
            default:
                x = Bot.Random.Next(325, 601);
                break;
        }

        _ = System.Threading.Tasks.Task.Run(() => Bot.Player.WalkTo(x, y));
    }
}
