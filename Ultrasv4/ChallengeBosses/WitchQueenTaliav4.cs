/*
name: Witch Queen Talia v4
description: Witch Queen Talia helper for army farming Witch Princess' Hat (Quest 10824) using V4 Orchestration.
tags: Ultra, Talia, Witch Queen Talia, v4, Witch Princess' Hat
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

public class WitchQueenTaliav4
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
    public string OptionsStorage = "WitchQueenTalia-v4";

    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself).", 5),
        new Option<int>("Quant", "Witch Princess' Hat Quantity", "How many Witch Princess' Hat to farm.", 50),
        new Option<bool>("UsePotions", "Use Potions", "Enable buying and consuming recommended potions.", true),
        new Option<int>("PotionQuantity", "Potion Quantity", "How many potions to keep stocked.", 10),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { "Verus DoomKnight" },
        new[] { "Chaos Avenger" },
        new[] { "StoneCrusher" },
        new[] { "Lord of Order" },
        new[] { "Shaman" }
    };

    public void ScriptMain(IScriptInterface bot)
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
            Bot.StopSync();
        }
    }

    private void EquipPresetClasses()
    {
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
        bool allowDuplicates = armySize > UltraClassesByRole.Length;
        
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("talia", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "talia_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("talia_class-v4.sync"));
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        Enh.ApplyContext("talia");

        if (Bot.Config!.Get<bool>("UsePotions"))
        {
            int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: false, context: "talia");
            Pots.UseRecommendedPotions(potionQuant, skipThird: false, ensureStock: false, context: "talia");
        }

        Bot.Sleep(2500);
    }

    private bool HasBossTarget(string boss) =>
        Bot.Player.HasTarget &&
        string.Equals(Bot.Player.Target?.Name, boss, StringComparison.OrdinalIgnoreCase);

    private void Fight()
    {
        const string map = "birgittaspire";
        const string boss = "Witch Queen Talia";

        string syncPath = Ultra.ResolveSyncPath("UltraTaliaItemCheck.sync");
        string wipeDeadSyncPath = Ultra.ResolveSyncPath("TaliaWipeDead.sync");
        string wipeAliveSyncPath = Ultra.ResolveSyncPath("TaliaWipeAlive.sync");
        
        Ultra.ClearSyncFile(syncPath);
        Ultra.ClearSyncFile(wipeDeadSyncPath);
        Ultra.ClearSyncFile(wipeAliveSyncPath);
        Bot.Sleep(2500);

        C.AddDrop("Witch Princess' Hat", "Witch Princess’ Hat");
        C.RegisterQuests(10824);

        int armySize = Math.Max(1, Bot.Config.Get<int>("ArmySize"));
        int quant = Math.Max(1, Bot.Config.Get<int>("Quant"));
        
        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, "talia_wait.sync", useSkill: false);
        
        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, "talia_wait.sync", useSkill: true);

        C.Jump("r2", "Bottom");
        Bot.Player.SetSpawnPoint();

        C.Logger("Fight start synced.");

        bool armyWipeDetected = false;

        while (!Bot.ShouldExit)
        {
            try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }

            // Non-taunter army wipe detection & recovery
            if (UltraGeneralv4.ArmyWipeHelperWithNoTaunters(Ultra, Bot, wipeDeadSyncPath, wipeAliveSyncPath, ref armyWipeDetected))
            {
                continue;
            }

            if (!Bot.Player.Alive)
            {
                while (!Bot.Player.Alive && !Bot.ShouldExit)
                {
                    try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }
                    Bot.Sleep(500);
                }

                if (!Bot.ShouldExit)
                {
                    C.Jump("r2", "Bottom");
                    Bot.Player.SetSpawnPoint();
                }
                continue;
            }

            // Explicit drop collection on loop tick


            bool isDone = C.CheckInventory("Witch Princess' Hat", quant, toInv: false) ||
                          C.CheckInventory("Witch Princess’ Hat", quant, toInv: false);

            if (Ultra.CheckArmyProgressBool(() => isDone, syncPath, armySize))
            {
                C.Logger("All players finished Witch Queen Talia farm.");
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
}
