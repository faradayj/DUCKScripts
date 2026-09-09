/*
name: Ultra Batara Kala DUCK
description: Four-player CoreDUCK Army script for Ultra Batara Kala (Kala Insignia).
tags: ultra, kala, batara kala, army, coreduck, daily
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraBataraKalaDUCK
{
    private enum FightResult
    {
        Continue,
        Defeated,
        Reset,
        Stopped,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    // Configuration Constants
    private const string LogPrefix = "UltraBataraKalaDUCK";
    private const string SyncFileName = "UltraBataraKalaDUCK.sync";
    private const string StagingMap = "yulgar";
    private const string StagingCell = "Enter";
    private const string StagingPad = "Spawn";
    private const string MapName = "ultrakala";
    private const string FightCell = "Enter";
    private const string FightPad = "Spawn";
    private const string BossName = "Batara Kala";
    private const string BossDefeatedTemp = "Kala Batara Defeated";
    private const string EnrageScroll = "Scroll of Enrage";
    private const int UltraQuestId = 8216;
    private const int PrerequisiteQuestId = 0;
    private const string PrerequisiteQuestName = "";
    private const int MinimumLevel = 80;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DefaultPrivateRoomNumber = 1245;

    // Runtime Fields
    private string playerAlias = string.Empty;
    private bool isTaunter;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraBataraKalaDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] KalaSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Legion Revenant", "Verus DoomKnight", "King's Echo", "Void Highlord" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "StoneCrusher" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchPaladin" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Lord of Order" } }
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;

        try
        {
            Run();
        }
        finally
        {
            Duck.StopSkillEngine();
        }
    }

    public UltraRunResult RunFromMaster(int roomNumber = DefaultPrivateRoomNumber)
    {
        masterMode = true;
        privateRoomNumber = roomNumber;
        runResult = UltraRunResult.Failed;
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;

        try
        {
            Run();
        }
        finally
        {
            Duck.StopSkillEngine();
            masterMode = false;
        }

        return runResult;
    }

    private void Run()
    {
        if (!masterMode)
            privateRoomNumber = DefaultPrivateRoomNumber;

        if (!Duck.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        DuckAssignmentResult? assignResult = Duck.AutoAssignAndEquip(
            SyncFileName,
            KalaSlots,
            armySize: 4
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        ClassPreset preset = assignResult.Preset;
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";
        isTaunter = string.Equals(preset.ClassName, "Lord of Order", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);

        if (
            !Duck.ValidateUltraAccess(
                UltraQuestId,
                PrerequisiteQuestId,
                PrerequisiteQuestName,
                MinimumLevel,
                LogPrefix,
                preset.ClassName
            )
        )
            return;

        Duck.FileLog($"Started as {playerAlias} in room {privateRoomNumber}.", LogPrefix);
        Core.Logger($"{LogPrefix} started as {playerAlias} in room {privateRoomNumber}.");

        Duck.AcceptUltraQuest(UltraQuestId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        Duck.EnsureAlive(15);
        Duck.FileLog($"{playerAlias} joining {StagingMap}-{privateRoomNumber} after defeat.", LogPrefix);
        Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);
        Duck.FileLog($"{playerAlias} completing Ultra Kala quest ({UltraQuestId}).", LogPrefix);
        Duck.CompleteUltraQuest(UltraQuestId);
        Duck.FileLog($"{playerAlias} completed Ultra Kala quest ({UltraQuestId}).", LogPrefix);

        if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        runResult = UltraRunResult.Completed;

        if (Duck.IsArmyPlayer(1))
        {
            Bot.Sleep(1000);
            Duck.ClearAllSyncFiles(SyncFileName);
        }
    }

    private bool RunFightAttempts(ClassPreset preset)
    {
        for (
            int fightAttempt = 1;
            fightAttempt <= MaxFightAttempts && !Bot.ShouldExit;
            fightAttempt++
        )
        {
            // 1. Gather in Yulgar for safe potion, scroll equipping, and party prebuffing
            Duck.FileLog($"{playerAlias} gathering in {StagingMap}-{privateRoomNumber} for attempt {fightAttempt}.", LogPrefix);
            Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            FightResult result;
            try
            {
                // 2. Simultaneous jump into ultrakala with full buffs already active
                Duck.FileLog($"{playerAlias} joining {MapName}-{privateRoomNumber} for attempt {fightAttempt}.", LogPrefix);
                Duck.JoinRoom(MapName, privateRoomNumber, FightCell, FightPad);
                Duck.FileLog($"{playerAlias} entered {MapName}-{privateRoomNumber}.", LogPrefix);

                Duck.JumpToMonsterCell(BossName);
                Duck.FileLog($"{playerAlias} jumped to {BossName} cell for attempt {fightAttempt}.", LogPrefix);

                Duck.FileLog($"{playerAlias} started fight attempt {fightAttempt}.", LogPrefix);
                result = Fight(preset, fightAttempt);
            }
            finally
            {
                Duck.StopSkillEngine();
            }

            if (result == FightResult.Defeated)
            {
                bool CreditCheck() =>
                    Bot.Quests.CanComplete(UltraQuestId) || Bot.Quests.IsDailyComplete(UltraQuestId);

                if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, 4, LogPrefix))
                {
                    Duck.FileLog($"{playerAlias} confirmed kill credit for {BossName} on attempt {fightAttempt}.", LogPrefix);
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

        Duck.PreparePotions(
            preset.Tonic,
            preset.Elixir,
            preset.CombatPotion
        );

        if (isTaunter)
            Duck.PrepareScrolls(EnrageScroll);

        if (Bot.ShouldExit)
            return false;

        Duck.FileLog($"{playerAlias} finished setup.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        Duck.UsePotions(
            preset.Tonic,
            preset.Elixir,
            preset.CombatPotion
        );

        if (isTaunter)
            Duck.EquipScroll(EnrageScroll);

        Duck.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private FightResult Fight(ClassPreset preset, int fightAttempt)
    {
        try
        {
            Duck.StartSkillEngine(
                preset.Skills,
                playerAlias,
                isTaunter,
                LogPrefix,
                preset.SkillMode
            );
            Core.Logger($"{LogPrefix} {playerAlias} started fighting {BossName}.");

            // Give initial attack command to acquire target
            Bot.Combat.Attack(BossName);
            Bot.Sleep(300);

            while (!Bot.ShouldExit)
            {
                if (Bot.TempInv.Contains(BossDefeatedTemp) || (!Duck.IsMonsterAlive(BossName) && Duck.GetMonsterHP(BossName) <= 0))
                {
                    // Verify monster is actually gone
                    Bot.Sleep(300);
                    if (Bot.TempInv.Contains(BossDefeatedTemp) || !Duck.IsMonsterAlive(BossName))
                        break;
                }

                if (Duck.ShouldResetFight(fightAttempt))
                {
                    return FightResult.Reset;
                }

                if (!Bot.Player.Alive)
                {
                    Core.Logger($"{LogPrefix} {playerAlias} died.");
                    Duck.FileLog($"{playerAlias} died during fight attempt {fightAttempt}.", LogPrefix);

                    while (!Bot.ShouldExit && !Bot.Player.Alive)
                    {
                        if (Duck.ShouldResetFight(fightAttempt))
                        {
                            return FightResult.Reset;
                        }
                        Bot.Sleep(RespawnPollDelay);
                    }

                    if (Bot.ShouldExit)
                    {
                        return FightResult.Stopped;
                    }

                    if (Bot.TempInv.Contains(BossDefeatedTemp) || (!Duck.IsMonsterAlive(BossName) && Duck.GetMonsterHP(BossName) <= 0))
                        break;

                    if (Duck.ShouldResetFight(fightAttempt))
                    {
                        return FightResult.Reset;
                    }

                    Core.Logger($"{LogPrefix} {playerAlias} respawned.");
                    Duck.FileLog($"{playerAlias} respawned.", LogPrefix);

                    if (!IsInFightCell())
                    {
                        Duck.JumpToMonsterCell(BossName);
                        Duck.FileLog($"{playerAlias} jumped to {BossName} cell after respawn.", LogPrefix);
                    }

                    continue;
                }

                Duck.MaintainTarget(BossName);
                Bot.Sleep(FightPollDelay);
            }

            if (Duck.ShouldResetFight(fightAttempt))
                return FightResult.Reset;

            return (Bot.TempInv.Contains(BossDefeatedTemp) || !Duck.IsMonsterAlive(BossName))
                ? FightResult.Defeated
                : FightResult.Stopped;
        }
        finally
        {
            Duck.StopSkillEngine();
        }
    }

    private bool HandleFightReset(int fightAttempt)
    {
        Duck.FileLog($"{playerAlias} executing fight reset for attempt {fightAttempt}.", LogPrefix);
        Duck.StopSkillEngine();
        Bot.Combat.CancelTarget();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            Duck.ShouldResetFight(fightAttempt);
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        Duck.ShouldResetFight(fightAttempt);
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
