/*
name: Ultra Batara Kala DUCK
description: Four-player CoreDUCK Army script for Ultra Batara Kala (Kala Insignia).
tags: ultra, kala, batara kala, army, coreduck, daily
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/Customv2/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraBataraKalaDUCK
{
    public enum ArmyComposition
    {
        Default,
    }

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
    private ArmyComposition armyComposition = ArmyComposition.Default;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;
    private DuckAssignmentResult? currentAssignResult;

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

        currentAssignResult = assignResult;

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
        Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);
        Duck.FileLog($"{playerAlias} turned in Ultra Kala quest ({UltraQuestId}).", LogPrefix);
        Duck.CompleteUltraQuest(UltraQuestId);

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
            Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            FightResult result;
            try
            {
                // 2. Simultaneous jump into ultrakala with full buffs already active
                Duck.JoinRoom(MapName, privateRoomNumber, FightCell, FightPad);
                Duck.JumpToMonsterCell(BossName);

                if (Bot.ShouldExit || !Sync("START_FIGHT"))
                    return false;

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
                    Duck.FileLog($"{playerAlias} defeated {BossName} on attempt {fightAttempt}.", LogPrefix);
                    return true;
                }

                Core.Logger($"{LogPrefix} One or more players missed kill credit on attempt {fightAttempt}. Retrying fight with entire army...");
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
        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            false,
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
                Duck.StopSkillEngine();
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
                        Duck.StopSkillEngine();
                        return FightResult.Reset;
                    }
                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                {
                    Duck.StopSkillEngine();
                    return FightResult.Stopped;
                }

                if (Bot.TempInv.Contains(BossDefeatedTemp) || (!Duck.IsMonsterAlive(BossName) && Duck.GetMonsterHP(BossName) <= 0))
                    break;

                if (Duck.ShouldResetFight(fightAttempt))
                {
                    Duck.StopSkillEngine();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                Duck.JumpToMonsterCell(BossName);

                continue;
            }

            Duck.MaintainTarget(BossName);
            Bot.Sleep(FightPollDelay);
        }

        Duck.StopSkillEngine();

        if (Duck.ShouldResetFight(fightAttempt))
            return FightResult.Reset;

        return (Bot.TempInv.Contains(BossDefeatedTemp) || !Duck.IsMonsterAlive(BossName))
            ? FightResult.Defeated
            : FightResult.Stopped;
    }

    private bool HandleFightReset(int fightAttempt)
    {
        Duck.StopSkillEngine();
        Bot.Combat.CancelTarget();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} retreating to Yulgar staging room after attempt {fightAttempt}.");
        Duck.FileLog($"{playerAlias} retreating to Yulgar staging room after attempt {fightAttempt}.", LogPrefix);

        Duck.JoinRoom(StagingMap, privateRoomNumber, StagingCell, StagingPad);
        return Sync($"FIGHT_RESET_{fightAttempt}_SAFE");
    }

    private void StopArmyAfterFailedAttempts()
    {
        Core.Logger(
            $"{LogPrefix} failed after {MaxFightAttempts} attempts.",
            LogPrefix,
            messageBox: true,
            stopBot: true
        );
        Duck.FileLog($"{playerAlias} exhausted all {MaxFightAttempts} attempts.", LogPrefix);
        Duck.StopArmySync("ATTEMPTS_EXHAUSTED");
    }

    private bool Sync(string stepName) => Duck.SyncArmy(stepName);
}
