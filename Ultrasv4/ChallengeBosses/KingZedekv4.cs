/*
name: King Zedek v4
description: King Zedek helper for army farming Atlas Regalia (Quest 10137: Oh Anima Effimera) using V4 Orchestration.
tags: Ultra, Zedek, King Zedek, v4, Atlas Regalia
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreStory.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs

using System;
using System.IO;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class KingZedekv4
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
    public string OptionsStorage = "KingZedek-v4";

    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself).", 3),
        new Option<int>("Quant", "Atlas Regalia Quantity", "How many Atlas Regalia to farm.", 100),
        new Option<bool>("UsePotions", "Use Potions", "Enable buying and consuming recommended potions.", true),
        new Option<int>("PotionQuantity", "Potion Quantity", "How many potions to keep stocked.", 10),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { "StoneCrusher" },
        new[] { "Chaos Avenger" },
        new[] { "Verus DoomKnight" }
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
        
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("king zedek", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "kingzedek_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("kingzedek_class-v4.sync"));
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        Enh.ApplyContext("king zedek");

        if (Bot.Config!.Get<bool>("UsePotions"))
        {
            int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: false, context: "king zedek");
            Pots.UseRecommendedPotions(potionQuant, skipThird: false, ensureStock: false, context: "king zedek");
        }

        Bot.Sleep(2500);
    }

    private bool HasBossTarget(string boss) =>
        Bot.Player.HasTarget &&
        string.Equals(Bot.Player.Target?.Name, boss, StringComparison.OrdinalIgnoreCase);

    private void Fight()
    {
        const string map = "atlasfalls";
        const string boss = "King Zedek";

        string syncPath = Ultra.ResolveSyncPath("UltraKingZedekItemCheck.sync");
        string wipeDeadSyncPath = Ultra.ResolveSyncPath("KingZedekWipeDead.sync");
        string wipeAliveSyncPath = Ultra.ResolveSyncPath("KingZedekWipeAlive.sync");
        
        Ultra.ClearSyncFile(syncPath);
        Ultra.ClearSyncFile(wipeDeadSyncPath);
        Ultra.ClearSyncFile(wipeAliveSyncPath);
        Bot.Sleep(2500);

        C.AddDrop("Atlas Regalia");
        C.RegisterQuests(10137);

        int armySize = Math.Max(1, Bot.Config.Get<int>("ArmySize"));
        int quant = Math.Max(1, Bot.Config.Get<int>("Quant"));
        
        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, "zedek_wait.sync", useSkill: false);
        
        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, "zedek_wait.sync", useSkill: true);

        C.Jump("r12", "Left");
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
                    C.Jump("r12", "Left");
                    Bot.Player.SetSpawnPoint();
                }
                continue;
            }

            // Explicit drop collection on loop tick


            bool isDone = C.CheckInventory("Atlas Regalia", quant, toInv: false);

            if (Ultra.CheckArmyProgressBool(() => isDone, syncPath, armySize))
            {
                C.Logger("All players finished King Zedek farm.");
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
