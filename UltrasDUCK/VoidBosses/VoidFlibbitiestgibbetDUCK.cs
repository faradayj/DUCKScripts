/*
name: Void Flibbitiestgibbet DUCK
description: Seven-player DUCK script for Void Flibbitiestgibbet.
tags: void, flibbi, challenge boss, seven-player, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class VoidFlibbitiestgibbetDUCK
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

    private const string LogPrefix = "Void Flibbitiestgibbet DUCK";
    private const string SyncFileName = "VoidFlibbitiestgibbetDUCK.sync";
    private const string StagingMap = "yulgar";
    private const string StagingCell = "Enter";
    private const string StagingPad = "Spawn";
    private const string MapName = "voidflibbi";
    private const string FightCell = "Enter";
    private const string FightPad = "Spawn";
    private const string BossName = "Void Flibbitiestgibbet";
    private const string FlibbiEssence = "Flibbitiestgibbet's ??? Essence";
    private const string Flibbitigiblets = "Flibbitigiblets";
    private const int FlibbiEssenceId = 73865;
    private const int FlibbitigibletsId = 70054;
    private const int WrongTurnQuestId = 9091;
    private const int EncroachingShadowsQuestId = 8653;
    private const int MinimumLevel = 80;
    private const int TargetArmySize = 7;
    private const int BossMapId = 1;
    private const int DefaultPrivateRoomNumber = 1245;
    private const int MaxFightAttempts = 10;
    private const int DeathResetThreshold = 3;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;

    private string playerAlias = string.Empty;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "VoidFlibbitiestgibbetDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        new Option<int>("PrivateRoomNumber", "Private Room Number", "Private room number to use for the army.", DefaultPrivateRoomNumber),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly DuckSlotRequirement[] SevenPlayerSlotRequirements = new DuckSlotRequirement[]
    {
        new() { CandidateClasses = new[] { "Legion Revenant", "King's Echo" } },
        new() { CandidateClasses = new[] { "StoneCrusher" } },
        new() { CandidateClasses = new[] { "ArchPaladin" } },
        new() { CandidateClasses = new[] { "Lord of Order" } },
        new() { CandidateClasses = new[] { "Verus DoomKnight" } },
        new() { CandidateClasses = new[] { "Bard" } },
        new() { CandidateClasses = new[] { "ArchFiend", "Shaman", "Chaos Avenger", "Void Highlord", "Dragon of Time" } },
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        try
        {
            Run(isMasterMode: false, privateRoomNumber: DefaultPrivateRoomNumber);
        }
        finally
        {
            Duck.StopSkillEngine();
        }
    }

    public UltraRunResult RunFromMaster(int roomNumber = DefaultPrivateRoomNumber) =>
        Run(isMasterMode: true, roomNumber);

    public UltraRunResult Run(bool isMasterMode, int privateRoomNumber)
    {
        masterMode = isMasterMode;
        this.privateRoomNumber = privateRoomNumber;
        runResult = UltraRunResult.Failed;

        try
        {
            ExecuteScript();
            return runResult;
        }
        catch (Exception ex)
        {
            Duck.FileLog($"Execution error: {ex.Message}", LogPrefix);
            Core.Logger($"Execution error: {ex.Message}", LogPrefix);
            return UltraRunResult.Failed;
        }
        finally
        {
            Duck.StopSkillEngine();
        }
    }

    private void ExecuteScript()
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;
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
            SevenPlayerSlotRequirements,
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
                WrongTurnQuestId,
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

        if (!Bot.Quests.IsDailyComplete(WrongTurnQuestId) && !Bot.Quests.IsInProgress(WrongTurnQuestId))
            Core.EnsureAccept(WrongTurnQuestId);

        if (!Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId) && !Bot.Quests.IsInProgress(EncroachingShadowsQuestId))
            Core.EnsureAccept(EncroachingShadowsQuestId);

        Core.AddDrop(FlibbiEssence, Flibbitigiblets);
        Core.AddDrop(FlibbiEssenceId, FlibbitigibletsId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        Duck.EnsureAlive(15);
        Duck.FileLog($"{playerAlias} retreating to {StagingMap} after defeat.", LogPrefix);
        Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);

        if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        runResult = UltraRunResult.Completed;

        if (Duck.IsArmyPlayer(1))
        {
            Bot.Sleep(1000);
            Duck.ClearAllSyncFiles(SyncFileName);
        }
    }

    private bool Prepare(ClassPreset preset)
    {
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

        if (Bot.ShouldExit)
            return false;

        Duck.FileLog($"{playerAlias} finished setup.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        Duck.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);
        Duck.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool RunFightAttempts(ClassPreset preset)
    {
        for (int fightAttempt = 1; fightAttempt <= MaxFightAttempts && !Bot.ShouldExit; fightAttempt++)
        {
            // 1. Gather in Yulgar for safe potion application and party prebuffing
            Duck.FileLog($"{playerAlias} gathering in {StagingMap}-{privateRoomNumber} for attempt {fightAttempt}.", LogPrefix);
            Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            FightResult result;
            try
            {
                // 2. Simultaneous jump into voidflibbi with full buffs active
                Duck.FileLog($"{playerAlias} joining {MapName}-{privateRoomNumber} for attempt {fightAttempt}.", LogPrefix);
                Duck.JoinRoom(MapName, privateRoomNumber, FightCell, FightPad);
                Duck.FileLog($"{playerAlias} entered {MapName}-{privateRoomNumber}.", LogPrefix);

                bool CreditCheck() =>
                    (Duck.HasItemAnywhere(FlibbiEssence, FlibbiEssenceId) || Bot.Quests.CanComplete(WrongTurnQuestId) || Bot.Quests.IsDailyComplete(WrongTurnQuestId))
                    && (Duck.HasItemAnywhere(Flibbitigiblets, FlibbitigibletsId) || Bot.Quests.CanComplete(EncroachingShadowsQuestId) || Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId));

                Duck.FileLog($"{playerAlias} started fight attempt {fightAttempt}.", LogPrefix);
                result = Fight(preset, fightAttempt, CreditCheck);
            }
            finally
            {
                Duck.StopSkillEngine();
            }

            if (result == FightResult.Defeated)
            {
                Duck.EnsureAlive(15);
                bool CreditCheck() =>
                    (Duck.HasItemAnywhere(FlibbiEssence, FlibbiEssenceId) || Bot.Quests.CanComplete(WrongTurnQuestId) || Bot.Quests.IsDailyComplete(WrongTurnQuestId))
                    && (Duck.HasItemAnywhere(Flibbitigiblets, FlibbitigibletsId) || Bot.Quests.CanComplete(EncroachingShadowsQuestId) || Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId));

                if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, TargetArmySize, LogPrefix, timeoutMs: 8000))
                {
                    Duck.FileLog($"{playerAlias} confirmed {BossName} defeated on attempt {fightAttempt}.", LogPrefix);
                    return true;
                }

                Core.Logger($"{LogPrefix} One or more players missed kill credit on attempt {fightAttempt}. Retrying fight with entire army...");
                Duck.FileLog($"{playerAlias} missed kill credit on attempt {fightAttempt}. Resetting fight...", LogPrefix);
                if (!HandleFightReset(fightAttempt))
                    return false;

                continue;
            }

            if (result != FightResult.Reset || !HandleFightReset(fightAttempt))
                return false;

            if (fightAttempt >= MaxFightAttempts)
            {
                StopArmyAfterFailedAttempts();
                runResult = UltraRunResult.AttemptsExhausted;
                return false;
            }
        }

        return false;
    }

    private FightResult Fight(ClassPreset preset, int fightAttempt, Func<bool> creditCondition)
    {
        try
        {
            Duck.StartSkillEngine(
                preset.Skills,
                playerAlias,
                false,
                LogPrefix,
                preset.SkillMode,
                maintainedPotion: preset.CombatPotion
            );
            Core.Logger($"{LogPrefix} {playerAlias} started fighting {BossName}.");

            bool bossObservedAlive = false;

            while (!Bot.ShouldExit)
            {
                if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                {
                    return FightResult.Reset;
                }

                if (creditCondition())
                    break;

                if (!Bot.Player.Alive)
                {
                    Core.Logger($"{LogPrefix} {playerAlias} died.");
                    Duck.FileLog($"{playerAlias} died during fight attempt {fightAttempt}.", LogPrefix);

                    while (!Bot.ShouldExit && !Bot.Player.Alive)
                    {
                        if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                        {
                            return FightResult.Reset;
                        }

                        Bot.Sleep(RespawnPollDelay);
                    }

                    if (Bot.ShouldExit)
                        break;

                    if (creditCondition())
                        break;

                    if (!Duck.IsMonsterAlive(BossMapId) && bossObservedAlive)
                        break;

                    if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                    {
                        return FightResult.Reset;
                    }

                    Core.Logger($"{LogPrefix} {playerAlias} respawned.");
                    Duck.FileLog($"{playerAlias} respawned.", LogPrefix);

                    if (!IsInFightCell())
                    {
                        Core.Jump(FightCell, FightPad);
                        Duck.FileLog($"{playerAlias} jumped back to {FightCell} after respawn.", LogPrefix);
                    }

                    continue;
                }

                bool bossAlive = Duck.IsMonsterAlive(BossMapId);
                if (bossAlive)
                    bossObservedAlive = true;
                else if (bossObservedAlive)
                    break;

                Duck.MaintainTarget(BossMapId);
                Bot.Sleep(FightPollDelay);
            }

            return (Bot.ShouldExit) ? FightResult.Stopped : FightResult.Defeated;
        }
        finally
        {
            StopFightCombat();
        }
    }

    private bool HandleFightReset(int fightAttempt)
    {
        Duck.FileLog($"{playerAlias} executing fight reset for attempt {fightAttempt}.", LogPrefix);
        StopFightCombat();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            Duck.ShouldResetFight(fightAttempt, DeathResetThreshold);
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        Duck.ShouldResetFight(fightAttempt, DeathResetThreshold);
        Core.Logger($"{LogPrefix} {playerAlias} retreating to Yulgar staging room after attempt {fightAttempt}.");
        Duck.FileLog($"{playerAlias} retreating to Yulgar staging room after attempt {fightAttempt}.", LogPrefix);

        Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);

        if (!IsInStagingRoom())
        {
            Core.Logger(
                $"{LogPrefix} {playerAlias} could not reach the Yulgar staging room after reset.",
                "HandleFightReset",
                messageBox: !masterMode,
                stopBot: !masterMode
            );
            return false;
        }

        return Sync($"FIGHT_RESET_{fightAttempt}_SAFE");
    }

    private void StopFightCombat()
    {
        Duck.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private void StopArmyAfterFailedAttempts()
    {
        Duck.FileLog($"{playerAlias} failed after {MaxFightAttempts} fight attempts.", LogPrefix);
        if (Duck.IsArmyPlayer(1))
            Duck.StopArmySync("ATTEMPTS_EXHAUSTED");
        else
            Duck.SyncArmy("STOP_CHECK");

        Core.Logger(
            $"{LogPrefix} failed after {MaxFightAttempts} fight attempts.",
            "RunFightAttempts",
            messageBox: !masterMode,
            stopBot: !masterMode
        );
    }

    private bool IsInStagingRoom() =>
        string.Equals(Bot.Map.Name, StagingMap, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Bot.Player.Cell, StagingCell, StringComparison.OrdinalIgnoreCase);

    private bool IsInFightCell() =>
        string.Equals(Bot.Map.Name, MapName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Bot.Player.Cell, FightCell, StringComparison.OrdinalIgnoreCase);

    private bool Sync(string step)
    {
        Duck.FileLog($"{playerAlias} syncing on {step}...", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");

        bool success = Duck.SyncArmy(step);
        Duck.FileLog($"{playerAlias} sync {step} => {(success ? "SUCCESS" : "TIMEOUT/FAILED")}", LogPrefix);
        if (!success)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
    }
}
