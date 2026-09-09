/*
name: VoidDailiesv4
description: Master combined daily runner for Quest 9091 (Wrong Turn at Voidbuquerque) and Quest 8653 (The Encroaching Shadows) using Ultrasv4 framework (7P matrix: VDK, LR, LoO, AF, LH, DoT x2) with strict 7-player army sync gates and bank-inclusive drop checks.
tags: custom, dailies, voidbuquerque, encroaching, shadow, ultrasv4
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreStory.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UnifiedQueuev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/Customv2/Ultrasv4/ChallengeBosses/Xyfragv4.cs
//cs_include Scripts/Customv2/Ultrasv4/ChallengeBosses/Flibbiv4.cs
//cs_include Scripts/Customv2/Ultrasv4/ChallengeBosses/NightBanev4.cs
//cs_include Scripts/Customv2/Ultrasv4/ChallengeBosses/IceWingv4.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class VoidDailiesv4
{
    private static CoreAdvanced Adv => _Adv ??= new CoreAdvanced();
    private static CoreAdvanced _Adv;
    private static UltraPotionsv4 Pots => _Pots ??= new UltraPotionsv4();
    private static UltraPotionsv4 _Pots;
    private static UltraEnhancementsv4 Enh => _Enh ??= new UltraEnhancementsv4();
    private static UltraEnhancementsv4 _Enh;

    private CoreBots C => CoreBots.Instance;
    public IScriptInterface Bot => IScriptInterface.Instance;
    public CoreEnginev4 Core = new();
    public CoreUltrav4 Ultra = new();

    public enum VoidbuquerqueReward
    {
        Tainted_Gem = 4769,
        Dark_Crystal_Shard = 4770,
        Diamond_of_Nulgath = 4771,
        Totem_of_Nulgath = 5357,
        Gem_of_Nulgath = 6136,
        Blood_Gem_of_the_Archfiend = 22332
    }

    public bool DontPreconfigure = true;
    public string OptionsStorage = "VoidDailies-v4";
    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army.", 7),
        new Option<VoidbuquerqueReward>("Reward", "Reward for Quest 9091", "Select which reward to turn in Quest 9091 for.", VoidbuquerqueReward.Blood_Gem_of_the_Archfiend),
        CoreBots.Instance.SkipOptions,
    };

    public void ScriptMain(IScriptInterface bot)
    {
        RunCombinedDailies();
        Bot.StopSync();
    }

    public void RunCombinedDailies()
    {
        try
        {
            C.SetOptions(true);
            Bot.Options.RejectAllDrops = false;
            Bot.Drops.RejectElse = false;
            Core.Boot();
            int configSize = Bot.Config!.Get<int>("ArmySize");

            int armySize = configSize > 0 ? configSize : 7;
            C.Logger($"[VoidDailiesv4] Script Initialized — Target Army Size: {armySize} players.");



            string rawReward = Bot.Config!.Get<string>("Reward") ?? "Blood_Gem_of_the_Archfiend";
            rawReward = rawReward.Trim().Replace(" ", "_");
            if (!Enum.TryParse(rawReward, true, out VoidbuquerqueReward selectedReward))
                selectedReward = VoidbuquerqueReward.Blood_Gem_of_the_Archfiend;

            string rewardName = selectedReward switch
            {
                VoidbuquerqueReward.Tainted_Gem => "Tainted Gem",
                VoidbuquerqueReward.Dark_Crystal_Shard => "Dark Crystal Shard",
                VoidbuquerqueReward.Diamond_of_Nulgath => "Diamond of Nulgath",
                VoidbuquerqueReward.Totem_of_Nulgath => "Totem of Nulgath",
                VoidbuquerqueReward.Gem_of_Nulgath => "Gem of Nulgath",
                VoidbuquerqueReward.Blood_Gem_of_the_Archfiend => "Blood Gem of the Archfiend",
                _ => "Blood Gem of the Archfiend"
            };

            C.BankingBlackList.AddRange(new[]
            {
                "Flibbitiestgibbet's ??? Essence", "Flibbitiestgibbet’s ??? Essence",
                "Nightbane's ??? Essence", "Nightbane’s ??? Essence",
                "Xyfrag's ??? Essence", "Xyfrag’s ??? Essence",
                "Flibbitigiblets", "Insatiable Hunger", "Chest Plate", "Glacial Pinion", "Hydra Eyeball",
                rewardName
            });

            C.AddDrop(
                "Flibbitiestgibbet's ??? Essence", "Flibbitiestgibbet’s ??? Essence",
                "Nightbane's ??? Essence", "Nightbane’s ??? Essence",
                "Xyfrag's ??? Essence", "Xyfrag’s ??? Essence",
                "Flibbitigiblets", "Insatiable Hunger", "Chest Plate", "Glacial Pinion", "Hydra Eyeball",
                rewardName
            );
            C.AddDrop(70052, 70053, 70054, 73862);

            C.Unbank(
                "Flibbitiestgibbet's ??? Essence", "Flibbitiestgibbet’s ??? Essence",
                "Nightbane's ??? Essence", "Nightbane’s ??? Essence",
                "Xyfrag's ??? Essence", "Xyfrag’s ??? Essence",
                "Flibbitigiblets", "Glacial Pinion", "Hydra Eyeball"
            );

            // Accept 9091
            if (!Bot.Quests.IsDailyComplete(9091) && !Bot.Quests.IsInProgress(9091))
                Bot.Quests.EnsureAccept(9091);

            // Accept 8653
            if (!Bot.Quests.IsDailyComplete(8653) && !Bot.Quests.IsInProgress(8653))
                Bot.Quests.EnsureAccept(8653);


            C.Logger($"[VoidDailiesv4] Starting Dailies — Selected Reward for Quest 9091: {rewardName} (ID: {(int)selectedReward})");

            // Setup the bosses list
            var allBosses = new[] { "Xyfrag", "Flibbi", "NightBane", "IceWing" };

            // Startup Gate
            C.Join("Whitemap");
            C.Logger($"[VoidDailiesv4] Staging army: Checking inventory and computing shared boss queue...");

            var pending = UnifiedQueuev4.GetSharedBossQueue(Ultra, Bot, C, allBosses, "Dailies_Bosses_v4.sync", "Dailies_Startup_Gate_v4.sync", IsBossComplete, armySize).ToList();

            if (pending.Any())
            {
                C.Logger($"[VoidDailiesv4] Executing shared boss queue: {string.Join(", ", pending)}");
                RunBossQueue(pending, armySize);
            }
            else
            {
                C.Logger("[VoidDailiesv4] All shared ultras already complete for army.");
            }

            // Independent End Phase: No sync files, no gates, no restarts.
            C.Logger("[VoidDailiesv4] Shared ultras finished. Proceeding to independent Hydra farm and quest turn-ins.");

            if (!IsBossComplete("Hydra"))
            {
                FarmHydraChallenge();
            }

            // Turn in completed quests independently
            Bot.Sleep(1000);
            if (Bot.Quests.CanComplete(9091))
            {
                C.EnsureComplete(9091, (int)selectedReward);
                Bot.Sleep(1000);
                if (UltraGeneralv4.IsQuestComplete(Bot, 9091))
                    C.Logger($"[Quest 9091 Complete] Granted reward: {rewardName}");
            }

            if (Bot.Quests.CanComplete(8653))
            {
                C.EnsureComplete(8653);
                Bot.Sleep(1000);
                if (UltraGeneralv4.IsQuestComplete(Bot, 8653))
                    C.Logger("[Quest 8653 Complete] The Encroaching Shadows finished.");
            }

            Ultra.PersistentJoinHouse();
            C.Logger("[VoidDailiesv4] ALL DAILIES COMPLETED SUCCESSFULLY!");
        }
        finally
        {
            Core.DisableSkills();
            C.SetOptions(false);
        }
    }

    private void RunBossQueue(IEnumerable<string> bosses, int armySize)
    {
        foreach (string boss in bosses)
        {
            switch (boss)
            {
                case "Xyfrag":
                    C.Logger("[Sequence] Fighting Ultra Xyfrag with full army...");
                    new Xyfragv4().RunBoss();
                    break;
                case "Flibbi":
                    C.Logger("[Sequence] Fighting Ultra Flibbitiestgibbet with full army...");
                    new Flibbiv4().RunBoss();
                    break;
                case "NightBane":
                    C.Logger("[Sequence] Fighting Ultra NightBane with full army...");
                    new NightBanev4().RunBoss();
                    break;
                case "IceWing":
                    C.Logger("[Sequence] Farming Warlord Icewing for Glacial Pinion with full army...");
                    new IceWingv4().RunBoss();
                    break;
            }
            RestoreDailiesState();
        }
    }

    private void RestoreDailiesState()
    {
        C.SetOptions(true);
        Bot.Options.RejectAllDrops = false;
        Bot.Drops.RejectElse = false;
        string rawReward = Bot.Config!.Get<string>("Reward") ?? "Blood_Gem_of_the_Archfiend";
        rawReward = rawReward.Trim().Replace(" ", "_");
        if (!Enum.TryParse(rawReward, true, out VoidbuquerqueReward selectedReward))
            selectedReward = VoidbuquerqueReward.Blood_Gem_of_the_Archfiend;

        string rewardName = selectedReward switch
        {
            VoidbuquerqueReward.Tainted_Gem => "Tainted Gem",
            VoidbuquerqueReward.Dark_Crystal_Shard => "Dark Crystal Shard",
            VoidbuquerqueReward.Diamond_of_Nulgath => "Diamond of Nulgath",
            VoidbuquerqueReward.Totem_of_Nulgath => "Totem of Nulgath",
            VoidbuquerqueReward.Gem_of_Nulgath => "Gem of Nulgath",
            VoidbuquerqueReward.Blood_Gem_of_the_Archfiend => "Blood Gem of the Archfiend",
            _ => "Blood Gem of the Archfiend"
        };

        C.AddDrop(
            "Flibbitiestgibbet's ??? Essence", "Flibbitiestgibbet’s ??? Essence",
            "Nightbane's ??? Essence", "Nightbane’s ??? Essence",
            "Xyfrag's ??? Essence", "Xyfrag’s ??? Essence",
            "Flibbitigiblets", "Insatiable Hunger", "Chest Plate", "Glacial Pinion", "Hydra Eyeball",
            rewardName
        );
        C.AddDrop(70052, 70053, 70054, 73862);
    }

    private bool IsBossComplete(string boss)
    {
        bool is9091Done = Bot.Quests.IsDailyComplete(9091);
        bool is8653Done = Bot.Quests.IsDailyComplete(8653);

        return boss switch
        {
            "Xyfrag" => is9091Done || C.CheckInventory("Xyfrag's ??? Essence", toInv: false) || C.CheckInventory("Xyfrag’s ??? Essence", toInv: false),
            "Flibbi" => (is9091Done || C.CheckInventory("Flibbitiestgibbet's ??? Essence", toInv: false) || C.CheckInventory("Flibbitiestgibbet’s ??? Essence", toInv: false))
                         && (is8653Done || C.CheckInventory("Flibbitigiblets", toInv: false) || C.CheckInventory(70054, toInv: false)),
            "NightBane" => is9091Done || C.CheckInventory("Nightbane's ??? Essence", toInv: false) || C.CheckInventory("Nightbane’s ??? Essence", toInv: false),
            "IceWing" => is8653Done || C.CheckInventory("Glacial Pinion", toInv: false) || C.CheckInventory(70052, toInv: false),
            "Hydra" => is8653Done || C.CheckInventory("Hydra Eyeball", 3, toInv: false) || C.CheckInventory(70053, 3, toInv: false),
            _ => false
        };
    }



    private void FarmHydraChallenge()
    {
        const string boss = "Hydra Head 90";

        // Equip Yami no Ronin
        C.Logger("[FarmHydraChallenge] Equipping Yami no Ronin for independent Hydra takedown.");
        try
        {
            if (C.CheckInventory("Yami no Ronin"))
            {
                Bot.Inventory.EquipItem("Yami no Ronin");
                Enh.ApplyContext("voiddailies");
            }
            else
            {
                C.Logger("[FarmHydraChallenge] Fallback: Yami no Ronin not found, equipping Solo class.");
                C.EquipClass(ClassType.Solo);
            }
        }
        catch { }
        Bot.Sleep(2000);

        C.AddDrop("Hydra Eyeball");
        C.AddDrop(70053);
        Bot.Drops.Add("Hydra Eyeball");
        Bot.Drops.Add(70053);
        Bot.Options.RejectAllDrops = false;
        Bot.Drops.RejectElse = false;

        if (!Bot.Quests.IsDailyComplete(8653) && !Bot.Quests.IsInProgress(8653))
            Bot.Quests.EnsureAccept(8653);

        // Join native AQW private room instance
        Core.Join("hydrachallenge-9999999");
        Core.ChooseBestCell(boss);
        Bot.Player.SetSpawnPoint();

        while (!Bot.ShouldExit)
        {
            while (!Bot.Player.Alive && !Bot.ShouldExit)
            {
                Bot.Sleep(500);
            }
            Core.ChooseBestCell(boss);
            Bot.Player.SetSpawnPoint();


            if (C.CheckInventory("Hydra Eyeball", 3, toInv: false) || C.CheckInventory(70053, 3, toInv: false))
            {
                C.Logger("[FarmHydraChallenge] Finished Hydra farm.");
                break;
            }

            Bot.Combat.Attack(boss);
            Bot.Sleep(250);
        }
        Ultra.PersistentJoinHouse();
    }
}
