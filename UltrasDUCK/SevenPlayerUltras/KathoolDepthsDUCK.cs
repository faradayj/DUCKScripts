/*
name: Kathool Depths DUCK
description: Seven-player CoreDUCK Army script for God of the Depths (including Legion Daily Quest 1677).
tags: ultra, kathool depths, god of the depths, seven-player, army, coreduck, legion daily
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include DUCKScripts\UltrasDUCK\CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class KathoolDepthsDUCK
{
    private enum FightResult
    {
        Defeated,
        Reset,
        Stopped,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "KathoolDepthsDUCK";
    private const string SyncFileName = "KathoolDepthsDUCK.sync";
    private const string MapName = "kathooldepths";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string Vigil = "Vigil";
    private const string LegionPromotion = "Legion Promotion";
    private const string PacketCommand = "ct";
    private const string ResistPacketText = "cannot resist";
    private const int UltraQuestId = 9350;
    private const int LegionQuestId = 1677;
    private const int MinimumLevel = 80;
    private const int VigilShopId = 2322;
    private const int VigilRestockThreshold = 100;
    private const int VigilMaxStack = 1000;
    private const int FirstTargetMapId = 3;
    private const int SecondTargetMapId = 1;
    private const int BossMapId = 2;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 5;
    private const int DeathResetThreshold = 3;
    private const int DefaultPrivateRoomNumber = 1245;
    private const int TargetArmySize = 7;

    private string playerAlias = string.Empty;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "KathoolDepthsDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        new Option<int>("PrivateRoomNumber", "Private Room Number", "Private room number to use for the army.", DefaultPrivateRoomNumber),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly DuckSlotRequirement[] KathoolSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Legion Revenant", "King's Echo" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "StoneCrusher" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchPaladin" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Lord of Order" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Bard" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchFiend", "Shaman", "Chaos Avenger", "Void Highlord", "Dragon of Time" } }
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;
        Bot.Options.AcceptACDrops = true;
        Bot.Options.RejectAllDrops = false;

        try
        {
            Run();
        }
        finally
        {
            Duck.StopPacketDetector();
            Duck.StopSkillEngine();
            masterMode = false;
        }
    }

    public UltraRunResult Run(bool isMasterMode = false, int masterRoomNumber = DefaultPrivateRoomNumber)
    {
        masterMode = isMasterMode;
        privateRoomNumber = masterRoomNumber;
        runResult = UltraRunResult.Failed;

        try
        {
            ExecuteScript();
        }
        finally
        {
            Duck.StopPacketDetector();
            Duck.StopSkillEngine();
            masterMode = false;
        }

        return runResult;
    }

    private void ExecuteScript()
    {
        Bot.Options.AcceptACDrops = true;
        Bot.Options.RejectAllDrops = false;

        if (!masterMode)
        {
            int configuredRoom = Bot.Config?.Get<int>("PrivateRoomNumber") ?? DefaultPrivateRoomNumber;
            if (configuredRoom > 0)
                privateRoomNumber = configuredRoom;
        }

        if (!Duck.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        DuckAssignmentResult? assignResult = Duck.AutoAssignAndEquip(
            SyncFileName,
            KathoolSlots,
            armySize: TargetArmySize,
            timeoutSeconds: 120,
            allowDuplicates: false
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        ClassPreset preset = assignResult.Preset;
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";

        if (
            !Duck.ValidateUltraAccess(
                UltraQuestId,
                0,
                string.Empty,
                MinimumLevel,
                LogPrefix,
                preset.ClassName
            )
        )
            return;

        Duck.FileLog($"Started as {playerAlias} in room {privateRoomNumber}.", LogPrefix);
        Core.Logger($"{LogPrefix} started as {playerAlias} in room {privateRoomNumber}.");

        Core.Unbank(LegionPromotion);
        Bot.Drops.Add("Lineage of Devastation", "Soul Sand");

        Duck.AcceptUltraQuest(UltraQuestId);
        if (!Bot.Quests.IsDailyComplete(LegionQuestId) && !Bot.Quests.IsInProgress(LegionQuestId))
            Duck.AcceptUltraQuest(LegionQuestId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        Core.Jump(SafeCell, SafePad);
        Duck.CompleteUltraQuest(UltraQuestId);
        if (Bot.Quests.CanComplete(LegionQuestId))
            Duck.CompleteUltraQuest(LegionQuestId);

        if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        runResult = UltraRunResult.Completed;

        if (assignResult.IsLeader)
        {
            Bot.Sleep(1000);
            Duck.ClearAllSyncFiles(SyncFileName);
        }
    }

    private bool Prepare(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting setup.");

        Duck.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

        Duck.PrepareEnhancements(
            preset.BaseEnhancement,
            preset.CapeEnhancement,
            preset.HelmEnhancement,
            preset.WeaponEnhancement,
            weaponFallbacks: preset.WeaponEnhancementFallbacks
        );

        Duck.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);
        if (!PrepareVigil())
            return false;

        Duck.FileLog($"{playerAlias} finished setup.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return !Bot.ShouldExit;
    }

    private bool PrepareVigil()
    {
        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load(waitForLoad: false);
        Bot.Wait.ForTrue(() => Bot.Bank.Contains(Vigil), 20);

        if (Bot.Bank.Contains(Vigil))
        {
            if (!Bot.Inventory.Contains(Vigil) && !Core.HasSpace)
            {
                Core.Logger($"{LogPrefix} Vigil is banked but no free inventory slot is available.", LogPrefix, messageBox: true, stopBot: true);
                return false;
            }

            int quantityBefore = Bot.Inventory.GetQuantity(Vigil);
            Bot.Bank.EnsureToInventory(Vigil);
            Bot.Wait.ForTrue(() => Bot.Inventory.GetQuantity(Vigil) > quantityBefore, 14);

            if (!Bot.Inventory.Contains(Vigil))
            {
                Core.Logger($"{LogPrefix} Vigil could not be moved from bank.", LogPrefix, messageBox: true, stopBot: true);
                return false;
            }

            Core.Logger($"{LogPrefix} Vigil moved from bank.");
        }

        Core.Join($"{MapName}-{privateRoomNumber}", SafeCell, SafePad);

        if (Bot.Inventory.GetQuantity(Vigil) < VigilRestockThreshold)
        {
            Core.Logger($"{LogPrefix} Restocking {Vigil} potions.");
            Core.BuyItem(MapName, VigilShopId, Vigil, VigilMaxStack);
        }

        int quantity = Bot.Inventory.GetQuantity(Vigil);
        if (quantity <= 0)
        {
            Core.Logger($"{LogPrefix} Vigil could not be obtained. Fight cannot start.", LogPrefix, messageBox: true, stopBot: true);
            return false;
        }

        if (!Bot.Inventory.IsEquipped(Vigil))
            Core.Equip(Vigil);

        return true;
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        Duck.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);
        if (!Bot.Inventory.IsEquipped(Vigil))
            Core.Equip(Vigil);

        Duck.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool RunFightAttempts(ClassPreset preset)
    {
        for (int fightAttempt = 1; fightAttempt <= MaxFightAttempts && !Bot.ShouldExit; fightAttempt++)
        {
            Duck.JoinRoom(MapName, privateRoomNumber, SafeCell, SafePad);

            if (!PrepareSafeRoom(preset))
                return false;

            if (!Duck.StartPacketDetector(PacketCommand, ResistPacketText))
                return false;

            FightResult result;
            try
            {
                if (!Sync("FIGHT_READY"))
                    return false;

                Core.Jump(BossCell, BossPad);

                if (Bot.ShouldExit || !Sync("START_FIGHT"))
                    return false;

                result = Fight(preset, fightAttempt);
            }
            finally
            {
                Duck.StopPacketDetector();
            }

            if (result == FightResult.Defeated)
            {
                Duck.EnsureAlive(15);
                bool CreditCheck() =>
                    Bot.Quests.CanComplete(UltraQuestId)
                    || Bot.Quests.CanComplete(LegionQuestId)
                    || (Bot.Quests.IsDailyComplete(UltraQuestId) && Bot.Quests.IsDailyComplete(LegionQuestId));

                if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, 7, LogPrefix, timeoutMs: 8000))
                    return true;

                Core.Logger($"{LogPrefix} One or more players missed kill credit on attempt {fightAttempt}. Retrying fight with entire army...");
                result = FightResult.Reset;
            }

            if (result != FightResult.Reset || !HandleFightReset(fightAttempt))
                return false;

            if (fightAttempt >= MaxFightAttempts)
            {
                runResult = UltraRunResult.AttemptsExhausted;
                return false;
            }
        }

        return false;
    }

    private FightResult Fight(ClassPreset preset, int fightAttempt)
    {
        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            false,
            LogPrefix,
            preset.SkillMode
        );

        bool bossObservedAlive = false;
        int nextResistDetection = 1;

        while (!Bot.ShouldExit)
        {
            if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
            {
                StopFightCombat();
                return FightResult.Reset;
            }

            if (!Bot.Player.Alive)
            {
                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    ProcessResistDetections(ref nextResistDetection, requestVigil: false);

                    if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                    {
                        StopFightCombat();
                        return FightResult.Reset;
                    }

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                if (!Duck.IsMonsterAlive(BossMapId) && bossObservedAlive)
                    break;

                if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                {
                    StopFightCombat();
                    return FightResult.Reset;
                }

                if (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                    Core.Jump(BossCell, BossPad);

                continue;
            }

            bool bossAlive = Duck.IsMonsterAlive(BossMapId);
            if (bossAlive)
                bossObservedAlive = true;
            else if (bossObservedAlive)
                break;

            // Target Priority: Add 3 -> Add 1 -> Boss 2
            int targetMapId = Duck.IsMonsterAlive(FirstTargetMapId)
                ? FirstTargetMapId
                : Duck.IsMonsterAlive(SecondTargetMapId)
                    ? SecondTargetMapId
                    : BossMapId;

            Duck.MaintainTarget(targetMapId);

            ProcessResistDetections(ref nextResistDetection, requestVigil: true);
            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();
        return (Bot.ShouldExit || !bossObservedAlive) ? FightResult.Stopped : FightResult.Defeated;
    }

    private void ProcessResistDetections(
        ref int nextResistDetection,
        bool requestVigil
    )
    {
        while (Duck.HasPacketDetection(nextResistDetection))
        {
            if (requestVigil)
            {
                Duck.RequestAbsolutePrioritySkill(5);
                Core.Logger(
                    $"{LogPrefix} {playerAlias} requested Vigil for resist detection {nextResistDetection}."
                );
            }

            nextResistDetection++;
        }
    }

    private bool HandleFightReset(int fightAttempt)
    {
        StopFightCombat();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            Duck.ShouldResetFight(fightAttempt, DeathResetThreshold);
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        Duck.ShouldResetFight(fightAttempt, DeathResetThreshold);
        Core.Jump(SafeCell, SafePad);

        return Sync($"FIGHT_RESET_{fightAttempt}_SAFE");
    }

    private void StopFightCombat()
    {
        Duck.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private bool Sync(string step) => Duck.SyncArmy(step);
}

