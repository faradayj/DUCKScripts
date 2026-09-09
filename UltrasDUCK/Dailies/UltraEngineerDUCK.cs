/*
name: Ultra Engineer DUCK
description: Four-player CoreDUCK Army script for Ultra Engineer.
tags: ultra, engineer, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraEngineerDUCK
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

    private const string LogPrefix = "Ultra Engineer DUCK";
    private const string SyncFileName = "UltraEngineerDUCK.sync";
    private const string MapName = "ultraengineer";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const int UltraQuestId = 8154;
    private const int PrerequisiteQuestId = 8151;
    private const string PrerequisiteQuestName = "The Engineer";
    private const int MinimumLevel = 61;
    private const int MainBossMapId = 3;
    private const int FirstTargetMapId = 2;
    private const int SecondTargetMapId = 1;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DefaultPrivateRoomNumber = 1245;

    private string playerAlias = string.Empty;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraEngineerDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] EngineerSlots = new[]
    {
        // Slot 1 (Primary DPS): Verus DoomKnight (Optimized) -> King's Echo (Stable) -> Void Highlord -> Legion Revenant -> Chaos Avenger
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight", "King's Echo", "Void Highlord", "Legion Revenant", "Chaos Avenger" } },
        // Slot 2 (Support DPS): StoneCrusher
        new DuckSlotRequirement { CandidateClasses = new[] { "StoneCrusher" } },
        // Slot 3 (Defense/Mitigation): ArchPaladin
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchPaladin" } },
        // Slot 4 (Core Buffer): Lord of Order
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

        // Auto-assign roles based on owned classes and Forge enhancements
        DuckAssignmentResult? assignResult = Duck.AutoAssignAndEquip(
            SyncFileName,
            EngineerSlots,
            armySize: 4
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

        Core.Jump(SafeCell, SafePad);

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
            Duck.JoinRoom(MapName, privateRoomNumber, SafeCell, SafePad);

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            Core.Jump(BossCell, BossPad);

            if (Bot.ShouldExit || !Sync("START_FIGHT"))
                return false;

            FightResult result = Fight(preset, fightAttempt);
            if (result == FightResult.Defeated)
            {
                bool CreditCheck() =>
                    Bot.Quests.CanComplete(UltraQuestId) || Bot.Quests.IsDailyComplete(UltraQuestId);

                if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, 4, LogPrefix))
                    return true;

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

    private FightResult Fight(ClassPreset preset, int fightAttempt)
    {
        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            false,
            LogPrefix,
            preset.SkillMode
        );
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            if (!Duck.IsMonsterAlive(MainBossMapId))
                break;

            if (Duck.ShouldResetFight(fightAttempt))
            {
                Duck.StopSkillEngine();
                return FightResult.Reset;
            }

            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died.");

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
                    break;

                if (!Duck.IsMonsterAlive(MainBossMapId))
                    break;

                if (Duck.ShouldResetFight(fightAttempt))
                {
                    Duck.StopSkillEngine();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (
                    Duck.IsMonsterAlive(MainBossMapId)
                    && (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                )
                    Core.Jump(BossCell, BossPad);

                continue;
            }

            int targetMapId = Duck.IsMonsterAlive(FirstTargetMapId)
                ? FirstTargetMapId
                : Duck.IsMonsterAlive(SecondTargetMapId)
                    ? SecondTargetMapId
                    : MainBossMapId;

            if (targetMapId != MainBossMapId)
            {
                Duck.SetOrdinarySkillsSuppressed(
                    Duck.IsMonsterAlive(FirstTargetMapId) && Duck.IsMonsterAlive(SecondTargetMapId)
                );
            }
            else
            {
                Duck.SetOrdinarySkillsSuppressed(false);
            }

            Duck.MaintainTarget(targetMapId);
            Bot.Sleep(FightPollDelay);
        }

        Duck.StopSkillEngine();

        if (Duck.ShouldResetFight(fightAttempt))
            return FightResult.Reset;

        return !Duck.IsMonsterAlive(MainBossMapId)
            ? FightResult.Defeated
            : FightResult.Stopped;
    }

    private bool HandleFightReset(int fightAttempt)
    {
        Core.Logger($"{LogPrefix} {playerAlias} retreating to safe cell after attempt {fightAttempt}.");
        Core.Jump(SafeCell, SafePad);
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
        Duck.StopArmySync("ATTEMPTS_EXHAUSTED");
    }

    private void StopArmy()
    {
        if (Duck.IsArmyPlayer(1))
        {
            Bot.Sleep(1000);
            if (Duck.StopArmySync("COMPLETE"))
                Core.Logger($"{LogPrefix} playerOne published COMPLETE.");
            else
                Core.Logger($"{LogPrefix} playerOne could not publish COMPLETE.");
        }
        else
        {
            Duck.SyncArmy("STOP_CHECK");
        }
    }

    private bool Sync(string stepName) => Duck.SyncArmy(stepName);
}
