/*
name: NightBane
description: NightBane helper for army farming NightBane.
tags: Ultra, NightBane, voidNightBane
*/

//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreStory.cs

using System;
using System.IO;
using System.Linq;
using Skua.Core.Interfaces;

public class NightBanev4
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
        int armySize = 7;
        bool allowDuplicates = true;

        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("nightbane", UltraClassesByRole);

        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "ultra_nightbane_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        // Clear stale class registrations from previous runs/other accounts
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("ultra_nightbane_class-v4.sync"));

        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        Enh.ApplyContext("nightbane");
    }

    private void Fight()
    {
        const string map = "voidNightBane";
        const string boss = "NightBane";

        const string waitSyncFile = "ultra_nightbane.sync";
        const string completionSyncFile = "UltraNightBaneCompletion.sync";
        int armySize = 7;

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        C.EnsureAcceptmultiple(9091, 8653);
        C.AddDrop("Nightbane's ??? Essence", "Nightbane’s ??? Essence", "Insatiable Hunger", "Chest Plate", "Starlit Journal Page 3 Scraps");

        Pots.EnsureRecommendedPotions(skipThird: false, context: "NightBane");

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: false);

        Pots.UseRecommendedPotions(skipThird: false, ensureStock: false, context: "NightBane");

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
                C.Logger("All players acquired Nightbane's ??? Essence. Farm complete.");
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
        // Checks if you own 1 copy of the item across Inventory OR Bank
        // Automatically unbanks it to Inventory if found in Bank
        return C.CheckInventory("Nightbane's ??? Essence", quant: 1, toInv: false) ||
               C.CheckInventory("Nightbane’s ??? Essence", quant: 1, toInv: false);
    }
}
