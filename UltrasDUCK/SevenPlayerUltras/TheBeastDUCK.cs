/*
name: The Beast DUCK
description: Seven-player CoreDUCK Army script for The Beast (Legion Daily Quest 1675).
tags: ultra, the beast, sevencircleswar, seven-player, army, coreduck, legion daily
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/Customv2/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class TheBeastDUCK
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

    private const string LogPrefix = "TheBeastDUCK";
    private const string SyncFileName = "TheBeastDUCK.sync";
    private const string MapName = "sevencircleswar";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r17";
    private const string BossPad = "Left";
    private const string LegionPromotion = "Legion Promotion";
    private const string BossName = "The Beast";
    private const int UltraQuestId = 1675;
    private const int MinimumLevel = 80;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 5;
    private const int DeathResetThreshold = 3;
    private const int DefaultPrivateRoomNumber = 1245;
    private const int TargetArmySize = 7;

    private string playerAlias = string.Empty;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private bool originalLagKiller;
    private DuckAssignmentResult? currentAssignResult;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "TheBeastDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        new Option<int>(
            "BeastSoulQuantity",
            "Beast Soul Quantity",
            "0 = Daily Quest 1675 only (Single Kill). 1-600 = Continuous Farm until Beast Souls reach this quantity.",
            0
        ),
        new Option<int>("PrivateRoomNumber", "Private Room Number", "Private room number to use for the army.", DefaultPrivateRoomNumber),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly DuckSlotRequirement[] BeastSlots = new[]
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
        originalLagKiller = Bot.Options.LagKiller;

        try
        {
            ExecuteScript();
        }
        finally
        {
            Duck.StopSkillEngine();
            Bot.Options.LagKiller = originalLagKiller;
            masterMode = false;
        }

        return runResult;
    }

    private void ExecuteScript()
    {
        Bot.Options.AcceptACDrops = true;
        Bot.Options.RejectAllDrops = false;

        int targetQuantity = masterMode ? 0 : Bot.Config?.Get<int>("BeastSoulQuantity") ?? 0;
        bool isDailyMode = targetQuantity <= 0;

        if (!isDailyMode)
            Bot.Options.LagKiller = true;

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
            BeastSlots,
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
                UltraQuestId,
                0,
                string.Empty,
                MinimumLevel,
                LogPrefix,
                preset.ClassName
            )
        )
            return;

        Duck.FileLog($"Started as {playerAlias} in room {privateRoomNumber} (Mode: {(isDailyMode ? "Daily Quest 1675" : $"Farm {targetQuantity} Beast Souls")}).", LogPrefix);
        Core.Logger($"{LogPrefix} started as {playerAlias} in room {privateRoomNumber} (Mode: {(isDailyMode ? "Daily Quest 1675" : $"Farm {targetQuantity} Beast Souls")}).");

        Core.Unbank(LegionPromotion);
        Bot.Drops.Add("Lineage of Devastation", "Beast Soul");

        if (isDailyMode)
        {
            if (!Bot.Quests.IsDailyComplete(UltraQuestId) && !Bot.Quests.IsInProgress(UltraQuestId))
                Duck.AcceptUltraQuest(UltraQuestId);
        }

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (isDailyMode)
        {
            if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
                return;

            Core.Jump(SafeCell, SafePad);
            if (Bot.Quests.CanComplete(UltraQuestId))
                Duck.CompleteUltraQuest(UltraQuestId);
        }
        else
        {
            if (!RunContinuousDropFarm(preset, targetQuantity) || !Sync("BOSS_DEFEATED"))
                return;

            Core.Jump(SafeCell, SafePad);
        }

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
        return !Bot.ShouldExit;
    }

    private bool RunFightAttempts(ClassPreset preset)
    {
        bool isAP = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        int apPlayerNumber = currentAssignResult?.GetPlayerNumberForClass("ArchPaladin") ?? -1;

        for (int fightAttempt = 1; fightAttempt <= MaxFightAttempts && !Bot.ShouldExit; fightAttempt++)
        {
            Duck.JoinRoom(MapName, privateRoomNumber, SafeCell, SafePad);

            if (!PrepareSafeRoom(preset))
                return false;

            FightResult result;
            try
            {
                if (!Sync("FIGHT_READY"))
                    return false;

                if (isAP)
                {
                    if (!PrepareRighteousSeal(fightAttempt) || !SendRighteousSealSignal(fightAttempt))
                        return false;
                }
                else
                {
                    if (apPlayerNumber > 0)
                        WaitForRighteousSealSignal(fightAttempt, apPlayerNumber);

                    Core.Jump(BossCell, BossPad);
                }

                if (Bot.ShouldExit || !Sync("START_FIGHT"))
                    return false;

                result = Fight(preset, fightAttempt);
            }
            finally
            {
                Duck.StopSkillEngine();
            }

            if (result == FightResult.Defeated)
            {
                Duck.EnsureAlive(15);
                bool CreditCheck() =>
                    Bot.Quests.CanComplete(UltraQuestId)
                    || Bot.Quests.IsDailyComplete(UltraQuestId);

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

    private bool PrepareRighteousSeal(int fightAttempt)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting Righteous Seal preparation on The Beast.");

        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} {playerAlias} died during Righteous Seal preparation.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                    Bot.Sleep(RespawnPollDelay);

                if (Bot.ShouldExit)
                    return false;

                Core.Logger($"{LogPrefix} {playerAlias} respawned and is retrying preparation.");
            }

            if (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                Core.Jump(BossCell, BossPad);

            if (!Duck.IsMonsterAlive(BossName))
                return true;

            Duck.MaintainTarget(BossName);

            if (Bot.Target.GetAura("Righteous Seal") != null)
            {
                Core.Logger($"{LogPrefix} {playerAlias} confirmed Righteous Seal on The Beast.");
                return true;
            }

            if (Bot.Skills.CanUseSkill(3))
                Bot.Skills.UseSkill(3);

            Bot.Sleep(FightPollDelay);
        }

        return !Bot.ShouldExit;
    }

    private bool SendRighteousSealSignal(int fightAttempt)
    {
        string signal = $"BEAST_SEAL_READY_{fightAttempt}";
        if (!Duck.SendArmySignal(signal))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} sent {signal}.");
        return true;
    }

    private bool WaitForRighteousSealSignal(int fightAttempt, int apPlayerNumber)
    {
        string signal = $"BEAST_SEAL_READY_{fightAttempt}";
        Core.Logger($"{LogPrefix} {playerAlias} waiting for {signal} from ArchPaladin (Player {apPlayerNumber}).");
        DateTime timeout = DateTime.UtcNow.AddSeconds(15);

        while (!Bot.ShouldExit && DateTime.UtcNow < timeout)
        {
            if (Duck.HasArmySignal(signal, apPlayerNumber))
            {
                Core.Logger($"{LogPrefix} {playerAlias} received {signal} from Player {apPlayerNumber}.");
                return true;
            }

            Bot.Sleep(FightPollDelay);
        }

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

                if (!Duck.IsMonsterAlive(BossName) && bossObservedAlive)
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

            bool bossAlive = Duck.IsMonsterAlive(BossName);
            if (bossAlive)
                bossObservedAlive = true;
            else if (bossObservedAlive)
                break;

            Duck.MaintainTarget(BossName);
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

    private bool RunContinuousDropFarm(ClassPreset preset, int targetQuantity)
    {
        bool isAP = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        int apPlayerNumber = currentAssignResult?.GetPlayerNumberForClass("ArchPaladin") ?? -1;

        Duck.JoinRoom(MapName, privateRoomNumber, SafeCell, SafePad);
        if (!PrepareSafeRoom(preset))
            return false;

        if (!Sync("FIGHT_READY"))
            return false;

        if (isAP)
        {
            if (!PrepareRighteousSeal(1) || !SendRighteousSealSignal(1))
                return false;
        }
        else
        {
            if (apPlayerNumber > 0)
                WaitForRighteousSealSignal(1, apPlayerNumber);

            Core.Jump(BossCell, BossPad);
        }

        if (Bot.ShouldExit || !Sync("START_FIGHT"))
            return false;

        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            false,
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: preset.CombatPotion
        );

        string readySignal = "BEAST_SOUL_GOAL_REACHED";
        bool readySignalSent = false;
        bool deathLogged = false;

        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                if (!deathLogged)
                {
                    Core.Logger($"{LogPrefix} {playerAlias} died during Beast Soul farm.");
                    deathLogged = true;
                }

                Bot.Sleep(RespawnPollDelay);
                continue;
            }

            if (deathLogged)
            {
                Core.Logger($"{LogPrefix} {playerAlias} respawned and returned to combat.");
                deathLogged = false;
                Core.Jump(BossCell, BossPad);
            }

            if (!readySignalSent && Duck.CheckItemQuantity("Beast Soul", targetQuantity))
            {
                if (!Duck.SendArmySignal(readySignal))
                    return false;

                readySignalSent = true;
                Core.Logger(
                    $"{LogPrefix} {playerAlias} reached {targetQuantity} Beast Souls and continues fighting to assist teammates."
                );
            }

            if (readySignalSent && AllPlayersSignaled(readySignal))
                break;

            if (Duck.IsMonsterAlive(BossName))
                Duck.MaintainTarget(BossName);

            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();
        return !Bot.ShouldExit;
    }

    private bool AllPlayersSignaled(string signal)
    {
        for (int playerNumber = 1; playerNumber <= TargetArmySize; playerNumber++)
        {
            if (!Duck.HasArmySignal(signal, playerNumber))
                return false;
        }

        return true;
    }

    private void StopFightCombat()
    {
        Duck.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private bool Sync(string step) => Duck.SyncArmy(step);
}
