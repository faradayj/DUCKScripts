/*
name: Cold Thunder Avatar v4
description: Cold Thunder Avatar helper for army farming Skye's Lightning (Quest 9834) using V4 Orchestration.
tags: Ultra, Cold Thunder Avatar, Cold Thunder, v4, Skye's Lightning
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
using Skua.Core.Models.Skills;

public class ColdThunderAvatarv4
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
    
    private bool needsPotion = false;
    private bool potionApplied = false;

    public bool DontPreconfigure = true;
    public string OptionsStorage = "ColdThunderAvatar-v4";

    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself).", 3),
        new Option<int>("Quant", "Skye's Lightning Quantity", "How many Skye's Lightning to farm.", 50),
        new Option<bool>("UsePotions", "Use Potions", "Enable buying and consuming recommended potions during prep.", true),
        new Option<int>("PotionQuantity", "Potion Quantity", "How many potions to keep stocked.", 10),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { "King's Echo" },
        new[] { "Lord of Order" },
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

        Bot.Events.ExtensionPacketReceived += TelegraphListener;
        try
        {
            Prep();
            Fight();
        }
        finally
        {
            Bot.Events.ExtensionPacketReceived -= TelegraphListener;
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
            Bot.StopSync();
        }
    }

    private void TelegraphListener(dynamic packet)
    {
        string type = packet?["params"]?.type;
        dynamic data = packet?["params"]?.dataObj;
        if (type != null && type == "json")
        {
            string cmd = data?.cmd?.ToString();
            if (cmd == "ct" && data?.anims != null)
            {
                foreach (var a in data.anims)
                {
                    if (a == null) continue;
                    if (a.msg != null && (string)a.msg == "The skies rumble. Prepare yourself!")
                    {
                        needsPotion = true;
                        potionApplied = false;
                        C.Logger("Event detected: The skies rumble. Prepare yourself! - Potion needed.");
                    }
                }
            }
        }
    }

    private void EquipPresetClasses()
    {
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
        bool allowDuplicates = armySize > UltraClassesByRole.Length;
        
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("cold thunder avatar", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "coldthunder_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("coldthunder_class-v4.sync"));
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        Enh.ApplyContext("cold thunder avatar");

        if (Bot.Config!.Get<bool>("UsePotions"))
        {
            int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: false, context: "cold thunder avatar");
            Pots.UseRecommendedPotions(potionQuant, skipThird: false, ensureStock: false, context: "cold thunder avatar");
        }

        // Must buy and equip Bananach's Last Will, overwriting any previous potions
        C.BuyItem("coldthunder", 2467, "Bananach's Last Will", 1000);
        C.Equip("Bananach's Last Will");
        Bot.Wait.ForItemEquip("Bananach's Last Will");
        Bot.Sleep(1500);
        C.Sleep();

        Bot.Sleep(2500);
    }

    private bool HasBossTarget(string boss) =>
        Bot.Player.HasTarget &&
        string.Equals(Bot.Player.Target?.Name, boss, StringComparison.OrdinalIgnoreCase);

    private void Fight()
    {
        const string map = "coldthunder";
        const string boss = "Cold Thunder Avatar";

        string syncPath = Ultra.ResolveSyncPath("UltraColdThunderItemCheck.sync");
        string wipeDeadSyncPath = Ultra.ResolveSyncPath("ColdThunderWipeDead.sync");
        string wipeAliveSyncPath = Ultra.ResolveSyncPath("ColdThunderWipeAlive.sync");
        
        Ultra.ClearSyncFile(syncPath);
        Ultra.ClearSyncFile(wipeDeadSyncPath);
        Ultra.ClearSyncFile(wipeAliveSyncPath);
        Bot.Sleep(2500);

        C.AddDrop("Frozen Bolt", "Skye's Lightning");
        C.RegisterQuests(9834);

        int armySize = Math.Max(1, Bot.Config.Get<int>("ArmySize"));
        int quant = Math.Max(1, Bot.Config.Get<int>("Quant"));
        
        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, "coldthunder_wait.sync", useSkill: false);
        
        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, "coldthunder_wait.sync", useSkill: true);

        C.Jump("r3", "Left");
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
                needsPotion = false;
                potionApplied = false;

                while (!Bot.Player.Alive && !Bot.ShouldExit)
                {
                    try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }
                    Bot.Sleep(500);
                }

                if (!Bot.ShouldExit)
                {
                    C.Jump("r3", "Left");
                    Bot.Player.SetSpawnPoint();
                }
                continue;
            }

            if (needsPotion && !potionApplied)
            {
                C.Logger("Detected 'The skies rumble. Prepare yourself!' - applying Bananach's Last Will potion...");
                while (!Bot.ShouldExit && needsPotion && !potionApplied)
                {
                    Bot.Combat.CancelAutoAttack();
                    Bot.Combat.CancelTarget();
                    C.UsePotion();
                    C.Sleep(200);

                    if (Bot.Self.HasActiveAura("Bananach's Last Will"))
                    {
                        potionApplied = true;
                        needsPotion = false;
                        C.Logger("Bananach's Last Will potion successfully applied!");
                    }
                    else
                    {
                        C.Sleep(100);
                    }
                }

                // If we lose target due to cancel, we should continue to let the loop logic acquire it again
                continue;
            }

            // Explicit drop collection on loop tick


            bool isDone = C.CheckInventory("Skye's Lightning", quant, toInv: false);

            if (Ultra.CheckArmyProgressBool(() => isDone, syncPath, armySize))
            {
                C.Logger("All players finished Cold Thunder Avatar farm.");
                Ultra.PersistentJoinHouse();
                C.CancelRegisteredQuests();
                Adv.GearStore(true, true);
                break;
            }

            if (!HasBossTarget(boss))
                Bot.Combat.Attack(boss);

            Bot.Sleep(100);
        }
    }
}
