/*
name: IceWing DUCK
description: Seven-player DUCK script for Warlord Icewing with Yulgar staging prebuff.
tags: ultra, icewing, warlord icewing, seven-player, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include DUCKScripts\UltrasDUCK\CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class IceWingDUCK
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

    private const string LogPrefix = "IceWing DUCK";
    private const string SyncFileName = "IceWingDUCK.sync";
    private const string StagingMap = "yulgar";
    private const string StagingCell = "Enter";
    private const string StagingPad = "Spawn";
    private const string MapName = "icewing";
    private const string FightCell = "Enter";
    private const string FightPad = "Spawn";
    private const string GlacialPinion = "Glacial Pinion";
    private const int GlacialPinionId = 70052;
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
    private DuckAssignmentResult? currentAssignResult;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "IceWingDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        new Option<int>("PrivateRoomNumber", "Private Room Number", "Private room number to use for the army.", DefaultPrivateRoomNumber),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly DuckSlotRequirement[] SevenPlayerSlotRequirements = new DuckSlotRequirement[]
    {
        new() { CandidateClasses = new[] { "Legion Revenant", "King's Echo" } },
        new() { CandidateClasses = new[] { "ArchPaladin" } },
        new() { CandidateClasses = new[] { "Lord of Order" } },
        new() { CandidateClasses = new[] { "LightCaster" } },
        new() { CandidateClasses = new[] { "StoneCrusher" } },
        new() { CandidateClasses = new[] { "Bard" } },
        new() { CandidateClasses = new[] { "Imperial Chunin", "Chunin" } },
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

        currentAssignResult = assignResult;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        ClassPreset preset = assignResult.Preset;
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";

        if (
            !Duck.ValidateUltraAccess(
                EncroachingShadowsQuestId,
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

        if (!Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId) && !Bot.Quests.IsInProgress(EncroachingShadowsQuestId))
            Core.EnsureAccept(EncroachingShadowsQuestId);

        Core.AddDrop(GlacialPinion);
        Core.AddDrop(GlacialPinionId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        Duck.EnsureAlive(15);
        Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);

        if (Bot.Quests.CanComplete(EncroachingShadowsQuestId))
            Duck.CompleteUltraQuest(EncroachingShadowsQuestId);

        Duck.EnsureAlive(15);

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

        return !Bot.ShouldExit;
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
            // 1. Gather in Yulgar-1245 for safe potion and party prebuffing
            Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);

            if (!PrepareSafeRoom(preset))
                return false;

            FightResult result;
            try
            {
                // 2. Synchronize ready status in Yulgar
                if (!Sync("FIGHT_READY"))
                    return false;

                // 3. Simultaneous jump into icewing-1245 with full buffs already active
                Duck.JoinRoom(MapName, privateRoomNumber, FightCell, FightPad);

                if (Bot.ShouldExit || !Sync("START_FIGHT"))
                    return false;

                bool CreditCheck() =>
                    Duck.HasItemAnywhere(GlacialPinion, GlacialPinionId)
                    || Bot.Quests.CanComplete(EncroachingShadowsQuestId)
                    || Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId);

                // 4. All 7 accounts fight IceWing together
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
                    Duck.HasItemAnywhere(GlacialPinion, GlacialPinionId)
                    || Bot.Quests.CanComplete(EncroachingShadowsQuestId)
                    || Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId);

                if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, TargetArmySize, LogPrefix, timeoutMs: 8000))
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

    private FightResult Fight(ClassPreset preset, int fightAttempt, Func<bool> creditCondition)
    {
        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            false,
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: preset.CombatPotion
        );

        bool bossObservedAlive = false;

        while (!Bot.ShouldExit)
        {
            if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
            {
                StopFightCombat();
                return FightResult.Reset;
            }

            if (creditCondition())
                break;

            if (!Bot.Player.Alive)
            {
                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                    {
                        StopFightCombat();
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
                    StopFightCombat();
                    return FightResult.Reset;
                }

                if (Bot.Player.Cell != FightCell || Bot.Player.Pad != FightPad)
                    Core.Jump(FightCell, FightPad);

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

        StopFightCombat();
        return (Bot.ShouldExit) ? FightResult.Stopped : FightResult.Defeated;
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
        Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);

        return Sync($"FIGHT_RESET_{fightAttempt}_SAFE");
    }

    private void StopFightCombat()
    {
        Duck.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private bool Sync(string step) => Duck.SyncArmy(step);
}

