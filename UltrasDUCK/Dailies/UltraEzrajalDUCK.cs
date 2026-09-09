/*
name: Ultra Ezrajal DUCK
description: Four-player CoreDUCK Army script for Ultra Ezrajal.
tags: ultra, ezrajal, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraEzrajalDUCK
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

    private const string LogPrefix = "Ultra Ezrajal DUCK";
    private const string SyncFileName = "UltraEzrajalDUCK.sync";
    private const string MapName = "ultraezrajal";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string CounterAttackAura = "Counter Attack";
    private const int UltraQuestId = 8152;
    private const int PrerequisiteQuestId = 8151;
    private const string PrerequisiteQuestName = "The Engineer";
    private const int MinimumLevel = 61;
    private const int BossMapId = 1;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DefaultPrivateRoomNumber = 1245;

    private string playerAlias = string.Empty;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraEzrajalDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] EzrajalSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight", "King's Echo", "Void Highlord", "Legion Revenant", "Chaos Avenger" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "StoneCrusher" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchPaladin" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Lord of Order" } }
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.UltraBossHelper.DisableCounterAttack();
        Bot.Options.InfiniteRange = true;
        // Bot.Config?.Configure();

        try
        {
            Run();
        }
        finally
        {
            Bot.Combat.StopAttacking = false;
            Duck.StopSkillEngine();
        }
    }

    public UltraRunResult RunFromMaster(int roomNumber = DefaultPrivateRoomNumber)
    {
        masterMode = true;
        privateRoomNumber = roomNumber;
        runResult = UltraRunResult.Failed;
        Bot.Skills.Stop();
        Bot.UltraBossHelper.DisableCounterAttack();
        Bot.Options.InfiniteRange = true;

        try
        {
            Run();
        }
        finally
        {
            Bot.Combat.StopAttacking = false;
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
            EzrajalSlots,
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
            Duck.FileLog($"{playerAlias} joined {MapName}-{privateRoomNumber} for attempt {fightAttempt}.", LogPrefix);

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            // Pre-combat barrier principle: start skill engine, jump, and fight immediately without post-jump sync
            Duck.StartSkillEngine(
                preset.Skills,
                playerAlias,
                false,
                LogPrefix,
                preset.SkillMode
            );

            Core.Jump(BossCell, BossPad);
            Duck.FileLog($"{playerAlias} jumped to {BossCell} for attempt {fightAttempt}.", LogPrefix);

            FightResult result = Fight(fightAttempt);
            if (result == FightResult.Defeated)
            {
                Core.Jump(SafeCell, SafePad);
                bool CreditCheck() =>
                    Bot.Quests.CanComplete(UltraQuestId) || Bot.Quests.IsDailyComplete(UltraQuestId);

                if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, 4, LogPrefix))
                {
                    Duck.FileLog($"{playerAlias} confirmed Ultra Ezrajal defeated on attempt {fightAttempt}.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Ezrajal defeated.");
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

        Duck.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

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

    private FightResult Fight(int fightAttempt)
    {
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        try
        {
            while (!Bot.ShouldExit)
            {
                if (!Duck.IsMonsterAlive(BossMapId))
                    break;

                if (Duck.ShouldResetFight(fightAttempt))
                    return FightResult.Reset;

                if (!Bot.Player.Alive)
                {
                    Bot.Combat.StopAttacking = false;
                    Core.Logger($"{LogPrefix} {playerAlias} died.");

                    while (!Bot.ShouldExit && !Bot.Player.Alive)
                    {
                        if (Duck.ShouldResetFight(fightAttempt))
                            return FightResult.Reset;

                        Bot.Sleep(RespawnPollDelay);
                    }

                    if (Bot.ShouldExit)
                        break;

                    if (!Duck.IsMonsterAlive(BossMapId))
                        break;

                    if (Duck.ShouldResetFight(fightAttempt))
                        return FightResult.Reset;

                    Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                    if (Duck.IsMonsterAlive(BossMapId) && !IsInBossRoom())
                        Core.Jump(BossCell, BossPad);

                    continue;
                }

                Duck.MaintainTarget(BossMapId);

                if (HandleCounterAttack(fightAttempt))
                    continue;

                Bot.Sleep(FightPollDelay);
            }
        }
        finally
        {
            Bot.Combat.StopAttacking = false;
            Duck.StopSkillEngine();
        }

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        return FightResult.Defeated;
    }

    private bool HandleCounterAttack(int fightAttempt)
    {
        if (
            !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != BossMapId
            || !Bot.Target.HasActiveAura(CounterAttackAura)
        )
            return false;

        Bot.Combat.StopAttacking = true;
        Bot.Combat.CancelAutoAttack();
        Duck.FileLog($"{playerAlias} paused for {CounterAttackAura}.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} paused for {CounterAttackAura}.");
        bool resetRequested = false;

        while (
            !Bot.ShouldExit
            && Bot.Player.Alive
            && Duck.IsMonsterAlive(BossMapId)
            && Bot.Player.HasTarget
            && Bot.Player.Target?.MapID == BossMapId
            && Bot.Target.HasActiveAura(CounterAttackAura)
        )
        {
            if (Duck.ShouldResetFight(fightAttempt))
            {
                resetRequested = true;
                Duck.StopSkillEngine();
                break;
            }

            Bot.Sleep(FightPollDelay);
        }

        Bot.Combat.StopAttacking = false;

        if (
            !resetRequested
            && !Bot.ShouldExit
            && Bot.Player.Alive
            && Duck.IsMonsterAlive(BossMapId)
        )
        {
            Bot.Combat.Attack(BossMapId);
            Duck.FileLog($"{playerAlias} resumed after {CounterAttackAura}.", LogPrefix);
            Core.Logger($"{LogPrefix} {playerAlias} resumed after {CounterAttackAura}.");
        }

        return true;
    }

    private bool HandleFightReset(int fightAttempt)
    {
        Duck.FileLog($"{playerAlias} executing fight reset for attempt {fightAttempt}.", LogPrefix);
        Bot.Combat.StopAttacking = false;
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
        Core.Jump(SafeCell, SafePad);

        if (!IsInSafeRoom())
        {
            Core.Logger(
                $"{LogPrefix} {playerAlias} could not reach the safe room after reset.",
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

    private bool IsInSafeRoom() =>
        string.Equals(Bot.Player.Cell, SafeCell, StringComparison.OrdinalIgnoreCase);

    private bool IsInBossRoom() =>
        string.Equals(Bot.Player.Cell, BossCell, StringComparison.OrdinalIgnoreCase);

    private bool Sync(string step)
    {
        Duck.FileLog($"{playerAlias} syncing on {step}...", LogPrefix);
        bool success = Duck.SyncArmy(step);
        Duck.FileLog($"{playerAlias} sync {step} => {(success ? "SUCCESS" : "TIMEOUT/FAILED")}", LogPrefix);
        return success;
    }
}
