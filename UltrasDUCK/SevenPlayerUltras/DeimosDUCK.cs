/*
name: Deimos DUCK
description: Seven-player CoreDUCK Army script for Devastator Deimos (Legion Daily Quest 1676).
tags: ultra, deimos, devastator deimos, seven-player, army, coreduck, legion daily
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include DUCKScripts\UltrasDUCK\CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class DeimosDUCK
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

    private const string LogPrefix = "DeimosDUCK";
    private const string SyncFileName = "DeimosDUCK.sync";
    private const string MapName = "deimos";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Enter";
    private const string BossPad = "Spawn";
    private const string LegionPromotion = "Legion Promotion";
    private const int UltraQuestId = 1676;
    private const int MinimumLevel = 80;
    private const int BossMapId = 1;
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

    public string OptionsStorage = "DeimosDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        new Option<int>("PrivateRoomNumber", "Private Room Number", "Private room number to use for the army.", DefaultPrivateRoomNumber),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly DuckSlotRequirement[] DeimosSlots = new[]
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
            DeimosSlots,
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
        Bot.Drops.Add("Lineage of Devastation");

        if (!Bot.Quests.IsDailyComplete(UltraQuestId) && !Bot.Quests.IsInProgress(UltraQuestId))
            Duck.AcceptUltraQuest(UltraQuestId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        Core.Jump(SafeCell, SafePad);
        if (Bot.Quests.CanComplete(UltraQuestId))
            Duck.CompleteUltraQuest(UltraQuestId);

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

        Duck.FileLog($"{playerAlias} finished setup.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
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
            Duck.JoinRoom(MapName, privateRoomNumber, SafeCell, SafePad);

            if (!PrepareSafeRoom(preset))
                return false;

            FightResult result;
            if (!Sync("FIGHT_READY"))
                return false;

            Core.Jump(BossCell, BossPad);

            if (Bot.ShouldExit || !Sync("START_FIGHT"))
                return false;

            result = Fight(preset, fightAttempt);

            if (result == FightResult.Defeated)
            {
                Duck.EnsureAlive(15);
                bool CreditCheck() =>
                    Bot.Quests.CanComplete(UltraQuestId) || Bot.Quests.IsDailyComplete(UltraQuestId);

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

            Duck.MaintainTarget(BossMapId);
            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();
        return (Bot.ShouldExit || !bossObservedAlive) ? FightResult.Stopped : FightResult.Defeated;
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

