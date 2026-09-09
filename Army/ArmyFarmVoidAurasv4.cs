/*
name: Army Farm Void Auras v4
description: Solo/Army farms Void Auras using the V4 Orchestration Framework.
tags: void aura, nsoD, farm, solo, army, v4
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
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UnifiedQueuev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class ArmyFarmVoidAurasv4
{
    private static IScriptInterface Bot => IScriptInterface.Instance;
    private static CoreBots Core => CoreBots.Instance;
    private static CoreFarms Farm => _Farm ??= new CoreFarms();
    private static CoreFarms _Farm;
    private static CoreEnginev4 Engine => CoreEnginev4.Instance;
    private static CoreUltrav4 Ultra => _Ultra ??= new CoreUltrav4();
    private static CoreUltrav4 _Ultra;
    private static UltraEnhancementsv4 Enh => _Enh ??= new UltraEnhancementsv4();
    private static UltraEnhancementsv4 _Enh;

    public bool DontPreconfigure = true;
    public string OptionsStorage = "ArmyFarmVoidAurasv4";

    public List<IOption> Options = new()
    {
        new Option<int>("Quantity", "Void Aura Quantity", "Number of Void Auras to farm.", 7600),
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself). Set to 1 for solo.", 4),
        new Option<string>("FarmClass", "Farming Class", "The class to equip during the farm.", "King's Echo"),
        CoreBots.Instance.SkipOptions,
    };

    private readonly (string map, int? monsterID, string? monsterName, string essence)[] EssenceStages =
    {
        ("timespace", null, "Astral Ephemerite", "Astral Ephemerite Essence"),
        ("citadel", 21, "Belrot the Fiend", "Belrot the Fiend Essence"),
        ("greenguardwest", 22, "Black Knight", "Black Knight Essence"),
        ("mudluk", 18, "Tiger Leech", "Tiger Leech Essence"),
        ("aqlesson", 17, "Carnax", "Carnax Essence"),
        ("necrocavern", 5, "Chaos Vordred", "Chaos Vordred Essence"),
        ("hachiko", 10, "Dai Tengu", "Dai Tengu Essence"),
        ("timevoid", 12, "Unending Avatar", "Unending Avatar Essence"),
        ("dragonchallenge", 4, "Void Dragon", "Void Dragon Essence"),
        ("maul", 17, "Creature Creation", "Creature Creation Essence"),
    };

    public void ScriptMain(IScriptInterface bot)
    {
        Core.SetOptions(true);
        Engine.Boot();

        try
        {
            FarmVoidAuras();
        }
        finally
        {
            Engine.DisableSkills();
            Core.SetOptions(false);
        }
    }

    public void FarmVoidAuras()
    {
        int targetAuras = Bot.Config!.Get<int>("Quantity");
        int armySize = Bot.Config.Get<int>("ArmySize");
        string farmClass = Bot.Config.Get<string>("FarmClass");

        if (Core.CheckInventory("Void Aura", targetAuras))
        {
            Core.Logger($"Target of {targetAuras} Void Auras reached.");
            return;
        }

        Core.AddDrop("Void Aura");
        Core.AddDrop(EssenceStages.Select(s => s.essence).ToArray());
        if (!Core.CheckInventory("Necromancer", toInv: false))
            Bot.Drops.Add("Creature Shard");

        Farm.EvilREP();
        
        string bossSyncFile = "ArmyFarmVoidAuras_BossQueue.sync";
        string participantSyncFile = "ArmyFarmVoidAuras_UnifiedParticipants_v4.sync";

        Core.Equip(farmClass);
        Enh.ApplyContext("default");

        while (!Bot.ShouldExit)
        {
            if (Ultra.CheckArmyProgressBool(() => Core.CheckInventory("Void Aura", targetAuras), "ArmyFarmVoidAuras_Done.sync", armySize))
            {
                Core.Logger("All players have reached the target Void Auras.");
                break;
            }

            // 1. Jump to Whitemap for safe assessment
            Core.Join("Whitemap");
            
            // 2. Assess: Unified Upfront Ballot
            var allEssences = EssenceStages.Select(x => x.essence).ToList();
            var pendingQueue = UnifiedQueuev4.GetSharedBossQueue(
                Ultra, Bot, Core, allEssences, bossSyncFile, participantSyncFile, IsEssenceComplete, armySize
            ).ToList();

            if (!pendingQueue.Any())
            {
                // The master queue is empty. Everyone has 100 of all 10 essences!
                int turnIns = GetMaxVoidAuraTurnIns();
                if (turnIns > 0)
                {
                    Core.Logger($"Turning in quest 4432 {turnIns} time(s).");
                    Core.EnsureCompleteMulti(4432, turnIns);
                    Bot.Wait.ForPickup("Void Aura");
                }
                
                Core.FarmingLogger("Void Aura", targetAuras);
                continue; // Restart the master loop and jump back to whitemap
            }

            // 3. Route & Execute
            foreach (string essence in pendingQueue)
            {
                if (Bot.ShouldExit) break;

                var stage = EssenceStages.First(s => s.essence == essence);
                RunCombatSandbox(stage.map, stage.monsterID, stage.monsterName, stage.essence, armySize);
            }
        }
    }

    private void RunCombatSandbox(string map, int? monsterID, string? monsterName, string essence, int armySize)
    {
        string safeName = essence.Replace(" ", "");
        string whitemapSync = $"VoidAuras_{safeName}_Whitemap.sync";
        string mapSync = $"VoidAuras_{safeName}_Map.sync";
        string completionSyncFile = $"VoidAuras_{safeName}_Done.sync";

        Core.Logger($"[Orchestrator] Routing to farm {essence}");
        
        Core.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, whitemapSync, staleThresholdSec: 15, useSkill: false);

        Core.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, mapSync, staleThresholdSec: 15, useSkill: true);

        if (!string.IsNullOrEmpty(monsterName))
        {
            Engine.ChooseBestCell(monsterName!);
        }
        
        // Execute Combat Loop
        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                Bot.Wait.ForTrue(() => Bot.Player.Alive, 20);
                continue;
            }

            if (Ultra.CheckArmyProgressBool(() => Core.CheckInventory(essence, 100), completionSyncFile, armySize))
            {
                Core.Logger($"[Combat Sandbox] Finished farming {essence}. Returning to Orchestrator.");
                break;
            }

            if (!string.IsNullOrEmpty(monsterName))
            {
                Bot.Combat.Attack(monsterName!);
            }
            else if (monsterID.HasValue)
            {
                Bot.Combat.Attack(monsterID.Value);
            }
            
            Bot.Sleep(250);
        }
    }

    private bool IsEssenceComplete(string essenceName)
    {
        return Core.CheckInventory(essenceName, 100);
    }

    private int GetMaxVoidAuraTurnIns()
    {
        int minEssenceStacks = EssenceStages.Min(stage => Bot.Inventory.GetQuantity(stage.essence));
        return minEssenceStacks / 20;
    }
}
