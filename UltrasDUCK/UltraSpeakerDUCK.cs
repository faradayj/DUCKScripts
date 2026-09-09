/*
name: Ultra Speaker LW
description: Four-player Ultra Speaker Army script using CoreDUCK.
tags: ultra, speaker, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/Customv2/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using System.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraSpeakerDUCK
{
    private enum FightResult
    {
        Defeated,
        Reset,
        Stopped,
    }

    private sealed class SpeakerStep
    {
        public string Warning { get; }
        public int Owner { get; }
        public bool Fresh { get; }
        public int SkillOwner { get; }
        public int Skill { get; }

        public SpeakerStep(
            string warning,
            int owner,
            bool fresh,
            int skillOwner = 0,
            int skill = 0
        )
        {
            Warning = warning;
            Owner = owner;
            Fresh = fresh;
            SkillOwner = skillOwner;
            Skill = skill;
        }
    }

    private sealed class ZoneState
    {
        public bool Moving;
        public int TargetX;
        public int TargetY;
        public string ArrivalSignal = string.Empty;
        public DateTimeOffset ConfirmMovementAt;
        public int WaitingForSanctityCycle;
        public int WaitingForClearCycle;
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "UltraSpeakerDUCK";
    private const string SyncFileName = "UltraSpeakerDUCK.sync";
    private const string MapName = "ultraspeaker";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string SanctityAura = "Sanctity";
    private const string RighteousSealAura = "Righteous Seal";
    private const string TruthMessage = "I will make you see the truth.";
    private const string ListenMessage = "You shall listen.";
    private const int UltraQuestId = 9173;
    private const int PrerequisiteQuestId = 9125;
    private const string PrerequisiteQuestName = "Your Hero";
    private const int MinimumLevel = 90;
    private const int SpeakerMapId = 1;
    private const int SafeX = 890;
    private const int SafeY = 395;
    private const int RedX = 731;
    private const int RedY = 380;
    private const int CoordinateTolerance = 20;
    private const int WalkSpeed = 8;
    private const int MovementRetryDelay = 500;
    private const int RighteousSealSkillFourWindow = 1000;
    private const int FightPollDelay = 100;
    private const int MaxFightAttempts = 3;
    private const int DefaultPrivateRoomNumber = 1245;

    private int dpsPlayerNumber = 1;
    private int scPlayerNumber = 2;
    private int apPlayerNumber = 3;
    private int looPlayerNumber = 4;
    private bool isArchPaladin;
    private bool isLordOfOrder;
    private bool isStoneCrusher;
    private bool isDPS;
    private DuckAssignmentResult? currentAssignResult;

    private string playerAlias = string.Empty;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;
    private bool bruteForceMethod;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private int equalizeDetectionCount;

    public string OptionsStorage = "UltraSpeakerDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] SpeakerSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight", "Legion Revenant" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "StoneCrusher", "Infinity Titan" } },
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
            StopFightSystems();
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
            StopFightSystems();
            masterMode = false;
        }

        return runResult;
    }

    private void Run()
    {
        if (!ValidateOptions())
            return;

        DuckAssignmentResult? assignResult = Duck.AutoAssignAndEquip(
            SyncFileName,
            SpeakerSlots,
            armySize: 4
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        currentAssignResult = assignResult;
        ClassPreset preset = assignResult.Preset;
        ApplySpeakerOverrides(preset, assignResult.RoleName);
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";

        isArchPaladin = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        isLordOfOrder = string.Equals(preset.ClassName, "Lord of Order", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(preset.ClassName, "Lord Of Order", StringComparison.OrdinalIgnoreCase);
        isStoneCrusher = string.Equals(preset.ClassName, "StoneCrusher", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(preset.ClassName, "Infinity Titan", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(preset.ClassName, "InfinityTitan", StringComparison.OrdinalIgnoreCase);
        isDPS = !isArchPaladin && !isLordOfOrder && !isStoneCrusher;

        ResolveRolePlayerNumbers(assignResult);

        if (IsTaunter())
            preset.CombatPotion = null;

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

    private void ApplySpeakerOverrides(ClassPreset preset, string roleName)
    {
        preset.CapeEnhancement = CapeSpecial.Penitence;

        if (string.Equals(preset.ClassName, "Legion Revenant", StringComparison.OrdinalIgnoreCase))
            preset.HelmEnhancement = HelmSpecial.None;

        if (string.Equals(preset.ClassName, "StoneCrusher", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset.ClassName, "Infinity Titan", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset.ClassName, "InfinityTitan", StringComparison.OrdinalIgnoreCase))
            preset.Elixir = "Divine Elixir";
        else if (string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase))
            preset.Skills = new[] { 2, 1 };
        else if (string.Equals(preset.ClassName, "Lord of Order", StringComparison.OrdinalIgnoreCase)
              || string.Equals(preset.ClassName, "LordOfOrder", StringComparison.OrdinalIgnoreCase)
              || string.Equals(preset.ClassName, "Lord Of Order", StringComparison.OrdinalIgnoreCase))
            preset.Skills = new[] { 2, 3, 1 };
    }

    private void ResolveRolePlayerNumbers(DuckAssignmentResult assignResult)
    {
        if (assignResult.PlayerNumberToClass == null)
            return;

        foreach (var kvp in assignResult.PlayerNumberToClass)
        {
            if (string.Equals(kvp.Value, "ArchPaladin", StringComparison.OrdinalIgnoreCase))
                apPlayerNumber = kvp.Key;
            else if (string.Equals(kvp.Value, "Lord of Order", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(kvp.Value, "LordOfOrder", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(kvp.Value, "Lord Of Order", StringComparison.OrdinalIgnoreCase))
                looPlayerNumber = kvp.Key;
            else if (string.Equals(kvp.Value, "StoneCrusher", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(kvp.Value, "Infinity Titan", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(kvp.Value, "InfinityTitan", StringComparison.OrdinalIgnoreCase))
                scPlayerNumber = kvp.Key;
            else
                dpsPlayerNumber = kvp.Key;
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

            if (!StartFightSystems(preset))
                return false;

            FightResult result;
            try
            {
                Core.Jump(BossCell, BossPad);
                Duck.FileLog($"{playerAlias} jumped to boss room for attempt {fightAttempt}.", LogPrefix);

                result = Fight(fightAttempt);
            }
            finally
            {
                StopFightSystems();
            }

            if (result == FightResult.Defeated)
            {
                Duck.FileLog($"{playerAlias} confirmed Ultra Speaker defeated on attempt {fightAttempt}.", LogPrefix);
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

    private bool ValidateOptions()
    {
        bruteForceMethod = false;
        if (!masterMode) privateRoomNumber = DefaultPrivateRoomNumber;
        return Duck.ValidatePrivateRoomNumber(privateRoomNumber);
    }

    private bool Prepare(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting setup.");

        Duck.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

        if (true)
            Duck.PrepareEnhancements(
                preset.BaseEnhancement,
                preset.CapeEnhancement,
                preset.HelmEnhancement,
                preset.WeaponEnhancement,
                weaponFallbacks: preset.WeaponEnhancementFallbacks
            );

        if (true)
        {
            if (isStoneCrusher)
                preset.Elixir = Duck.GetDivineElixir(
                    preset,
                    playerAlias,
                    LogPrefix
                );

            Duck.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );
        }

        if (IsTaunter())
            Duck.PrepareScrolls(EnrageScroll);

        if (Bot.ShouldExit)
            return false;

        Duck.FileLog($"{playerAlias} finished setup.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        if (true)
            Duck.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        if (IsTaunter())
            Duck.EquipScroll(EnrageScroll);

        Duck.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool StartFightSystems(ClassPreset preset)
    {
        if (
            !Duck.StartPacketChoiceDetector(
                "ct",
                new[] { TruthMessage, ListenMessage },
                pauseSkillChoice: ListenMessage,
                pauseEveryChoice: true
            )
        )
        {
            Core.Logger(
                "Speaker packet detector could not be started.",
                "StartFightSystems",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        Interlocked.Exchange(ref equalizeDetectionCount, 0);
        Bot.Events.RunToArea -= OnRunToArea;
        Bot.Events.RunToArea += OnRunToArea;

        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            IsTaunter(),
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: true
                ? preset.CombatPotion
                : null
        );
        return true;
    }

    private void StopFightSystems()
    {
        Bot.Events.RunToArea -= OnRunToArea;
        Interlocked.Exchange(ref equalizeDetectionCount, 0);
        Duck.StopPacketDetector();
        Duck.StopSkillEngine();
    }

    private void OnRunToArea(string zone)
    {
        if (string.Equals(zone, "A", StringComparison.Ordinal))
            Interlocked.Increment(ref equalizeDetectionCount);
    }

    private FightResult Fight(int fightAttempt)
    {
        int currentSection = 0;
        int currentStep = 0;
        int nextWarningDetection = 1;
        int nextEqualizeDetection = 1;
        int equalizeCycle = 0;
        bool bossObserved = false;
        bool playerWasDead = false;
        bool localChartInvalid = false;
        bool chartFailureSent = false;
        bool chartResetSent = false;
        int nextChartFailureSender = 1;
        bool righteousSealSkillFourQueued = false;
        ZoneState zoneState = new();

        StartInitialMovement(zoneState);

        Duck.FileLog($"{playerAlias} started fighting attempt {fightAttempt}.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting attempt {fightAttempt}.");

        while (!Bot.ShouldExit)
        {
            if (IsInBossRoom())
            {
                if (Duck.IsMonsterAlive(SpeakerMapId))
                    bossObserved = true;
                else if (bossObserved)
                    return FightResult.Defeated;
            }

            if (
                !bruteForceMethod
                && Duck.ShouldResetFight(fightAttempt)
            )
                return FightResult.Reset;

            if (!localChartInvalid)
            {
                bool warningChartValid = ProcessWarningDetections(
                    ref currentSection,
                    ref currentStep,
                    ref nextWarningDetection,
                    ref righteousSealSkillFourQueued,
                    out string warningMismatch
                );

                if (
                    !warningChartValid
                    && !ReportChartFailure(
                        fightAttempt,
                        warningMismatch,
                        ref localChartInvalid,
                        ref chartFailureSent
                    )
                )
                    return FightResult.Stopped;

                if (
                    !localChartInvalid
                    && !ProcessEqualizeDetections(
                        fightAttempt,
                        zoneState,
                        ref currentSection,
                        ref currentStep,
                        ref nextEqualizeDetection,
                        ref equalizeCycle,
                        out string equalizeMismatch
                    )
                    && !ReportChartFailure(
                        fightAttempt,
                        equalizeMismatch,
                        ref localChartInvalid,
                        ref chartFailureSent
                    )
                )
                    return FightResult.Stopped;
            }

            if (
                !PublishChartResetIfRequested(
                    fightAttempt,
                    ref chartResetSent,
                    ref nextChartFailureSender
                )
            )
                return FightResult.Stopped;

            if (HasChartResetSignal(fightAttempt))
                return FightResult.Reset;

            if (!Bot.Player.Alive)
            {
                if (!playerWasDead)
                {
                    playerWasDead = true;
                    Core.Logger($"{LogPrefix} {playerAlias} died.");
                }

                Bot.Sleep(FightPollDelay);
                continue;
            }

            if (playerWasDead)
            {
                playerWasDead = false;
                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (!IsInBossRoom())
                    Core.Jump(BossCell, BossPad);

                if (Bot.ShouldExit)
                    break;

                RestoreZonePositionAfterDeath(
                    zoneState,
                    fightAttempt,
                    equalizeCycle
                );
            }

            if (!ProcessZoneState(zoneState, fightAttempt))
                return FightResult.Stopped;

            if (IsInBossRoom())
            {
                Duck.MaintainTarget(SpeakerMapId);

                if (!bruteForceMethod)
                {
                    MaintainArchPaladinRighteousSeal(
                        ref righteousSealSkillFourQueued
                    );
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return FightResult.Stopped;
    }

    private bool ProcessWarningDetections(
        ref int currentSection,
        ref int currentStep,
        ref int nextDetection,
        ref bool righteousSealSkillFourQueued,
        out string mismatch
    )
    {
        mismatch = string.Empty;

        while (Duck.HasPacketDetection(nextDetection))
        {
            string detectedWarning = Duck.GetPacketDetectorChoice();
            SpeakerStep[] steps = GetSectionSteps(currentSection);

            if (currentStep >= steps.Length)
            {
                mismatch = $"Unexpected {GetWarningName(detectedWarning)} before the next Equalize.";
                return false;
            }

            SpeakerStep expected = steps[currentStep];
            if (!string.Equals(
                detectedWarning,
                expected.Warning,
                StringComparison.Ordinal
            ))
            {
                mismatch = $"Expected {GetWarningName(expected.Warning)} but detected {GetWarningName(detectedWarning)}.";
                return false;
            }

            nextDetection++;
            currentStep++;

            bool localFreshOwner = expected.Fresh
                && Duck.IsArmyPlayer(expected.Owner);

            if (localFreshOwner)
            {
                Duck.RequestAbsolutePriorityTaunt(SpeakerMapId);

                Core.Logger(
                    $"{LogPrefix} {playerAlias} requested Fresh {GetWarningName(expected.Warning)} taunt."
                );
            }
            else if (!expected.Fresh)
            {
                Core.Logger(
                    $"{LogPrefix} {playerAlias} processed Covered {GetWarningName(expected.Warning)}."
                );
            }

            if (!localFreshOwner)
                Duck.ResumeSkillEngine();

            if (
                !bruteForceMethod
                && expected.Skill > 0
                && Duck.IsArmyPlayer(expected.SkillOwner)
            )
            {
                Duck.RequestPrioritySkill(expected.Skill);

                if (
                    expected.SkillOwner == GetArchPaladinPlayer()
                    && expected.Skill == 3
                )
                    righteousSealSkillFourQueued = false;

                Core.Logger(
                    $"{LogPrefix} {playerAlias} queued chart skill {expected.Skill}."
                );
            }
        }

        return true;
    }

    private bool ProcessEqualizeDetections(
        int fightAttempt,
        ZoneState zoneState,
        ref int currentSection,
        ref int currentStep,
        ref int nextDetection,
        ref int equalizeCycle,
        out string mismatch
    )
    {
        mismatch = string.Empty;

        while (Volatile.Read(ref equalizeDetectionCount) >= nextDetection)
        {
            if (currentStep != GetSectionStepCount(currentSection))
            {
                mismatch = $"Equalize arrived before {GetPortionName(currentSection)} completed.";
                return false;
            }

            int cycle = nextDetection++;
            int owner = GetZoneOwner(cycle);
            if (!bruteForceMethod)
            {
                if (
                    Duck.IsArmyPlayer(owner)
                    && !IsAtCoordinate(RedX, RedY)
                )
                {
                    mismatch = $"Equalize cycle {cycle} owner was not in the red zone.";
                    return false;
                }
            }

            equalizeCycle = cycle;
            currentSection = ((cycle - 1) % 4) + 1;
            currentStep = 0;

            if (!bruteForceMethod)
            {
                if (Duck.IsArmyPlayer(owner))
                    zoneState.WaitingForSanctityCycle = cycle;

                int nextOwner = GetZoneOwner(cycle + 1);
                if (Duck.IsArmyPlayer(nextOwner))
                    zoneState.WaitingForClearCycle = cycle;
            }

            Core.Logger(
                $"{LogPrefix} {playerAlias} entered Equalize cycle {cycle} section {currentSection}."
            );
        }

        return true;
    }

    private bool ProcessZoneState(ZoneState state, int fightAttempt)
    {
        if (!Bot.Player.Alive || !IsInBossRoom())
            return true;

        if (state.Moving)
        {
            if (DateTimeOffset.Now < state.ConfirmMovementAt)
                return true;

            if (!IsAtCoordinate(state.TargetX, state.TargetY))
            {
                IssueMovement(state);
                return true;
            }

            state.Moving = false;
            if (state.ArrivalSignal.Length == 0)
                return true;

            string signal = state.ArrivalSignal;
            state.ArrivalSignal = string.Empty;
            if (!Duck.SendArmySignal(signal))
            {
                Core.Logger(
                    $"{LogPrefix} {playerAlias} could not send {signal}.",
                    "ProcessZoneState",
                    messageBox: true,
                    stopBot: true
                );
                return false;
            }

            Core.Logger($"{LogPrefix} {playerAlias} sent {signal}.");
            return true;
        }

        if (bruteForceMethod)
            return true;

        if (state.WaitingForSanctityCycle > 0)
        {
            if (Bot.Self.GetAura(SanctityAura) == null)
                return true;

            int cycle = state.WaitingForSanctityCycle;
            state.WaitingForSanctityCycle = 0;
            StartMovement(
                state,
                SafeX,
                SafeY,
                GetZoneClearSignal(fightAttempt, cycle)
            );
            Core.Logger(
                $"{LogPrefix} {playerAlias} resolved Sanctity cycle {cycle} and moved safe."
            );
            return true;
        }

        if (state.WaitingForClearCycle > 0)
        {
            int cycle = state.WaitingForClearCycle;
            int owner = GetZoneOwner(cycle);
            string clearSignal = GetZoneClearSignal(fightAttempt, cycle);
            if (!Duck.HasArmySignal(clearSignal, owner))
                return true;

            state.WaitingForClearCycle = 0;
            StartMovement(
                state,
                RedX,
                RedY,
                string.Empty
            );
            Core.Logger(
                $"{LogPrefix} {playerAlias} received {clearSignal} and moved into the red zone."
            );
        }

        return true;
    }

    private void StartInitialMovement(ZoneState state)
    {
        if (bruteForceMethod)
            StartMovement(state, SafeX, SafeY, string.Empty);
        else if (Duck.IsArmyPlayer(GetZoneOwner(1)))
            StartMovement(state, RedX, RedY, string.Empty);
        else
            StartMovement(state, SafeX, SafeY, string.Empty);
    }

    private void RestoreZonePositionAfterDeath(
        ZoneState state,
        int fightAttempt,
        int equalizeCycle
    )
    {
        state.Moving = false;
        state.ArrivalSignal = string.Empty;
        state.ConfirmMovementAt = DateTimeOffset.MinValue;
        state.WaitingForSanctityCycle = 0;
        state.WaitingForClearCycle = 0;

        if (bruteForceMethod)
        {
            StartMovement(state, SafeX, SafeY, string.Empty);
            return;
        }

        int nextCycle = equalizeCycle + 1;
        if (!Duck.IsArmyPlayer(GetZoneOwner(nextCycle)))
        {
            StartMovement(state, SafeX, SafeY, string.Empty);
            return;
        }

        if (equalizeCycle == 0)
        {
            StartMovement(
                state,
                RedX,
                RedY,
                string.Empty
            );
            return;
        }

        string clearSignal = GetZoneClearSignal(fightAttempt, equalizeCycle);
        int clearOwner = GetZoneOwner(equalizeCycle);
        if (Duck.HasArmySignal(clearSignal, clearOwner))
            StartMovement(
                state,
                RedX,
                RedY,
                string.Empty
            );
        else
        {
            StartMovement(state, SafeX, SafeY, string.Empty);
            state.WaitingForClearCycle = equalizeCycle;
        }
    }

    private void StartMovement(
        ZoneState state,
        int x,
        int y,
        string arrivalSignal
    )
    {
        state.Moving = true;
        state.TargetX = x;
        state.TargetY = y;
        state.ArrivalSignal = arrivalSignal;
        IssueMovement(state);
    }

    private void IssueMovement(ZoneState state)
    {
        Bot.Flash.Call(
            "walkTo",
            state.TargetX,
            state.TargetY,
            WalkSpeed
        );
        state.ConfirmMovementAt = DateTimeOffset.Now.AddMilliseconds(
            MovementRetryDelay
        );
    }

    private bool ReportChartFailure(
        int fightAttempt,
        string reason,
        ref bool localChartInvalid,
        ref bool chartFailureSent
    )
    {
        localChartInvalid = true;
        Core.Logger($"{LogPrefix} {playerAlias} chart mismatch: {reason}");

        if (chartFailureSent)
            return true;

        string signal = GetChartFailureSignal(fightAttempt);
        if (!Duck.SendArmySignal(signal))
        {
            Core.Logger(
                $"{LogPrefix} {playerAlias} could not send {signal}.",
                "ReportChartFailure",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        chartFailureSent = true;
        Core.Logger($"{LogPrefix} {playerAlias} sent {signal}.");
        return true;
    }

    private bool PublishChartResetIfRequested(
        int fightAttempt,
        ref bool chartResetSent,
        ref int nextFailureSender
    )
    {
        if (!Duck.IsArmyPlayer(1) || chartResetSent)
            return true;

        string failureSignal = GetChartFailureSignal(fightAttempt);
        bool failureReported = Duck.HasArmySignal(
            failureSignal,
            nextFailureSender
        );
        nextFailureSender = nextFailureSender % 4 + 1;
        if (!failureReported)
            return true;

        string signal = GetChartResetSignal(fightAttempt);
        if (!Duck.SendArmySignal(signal))
        {
            Core.Logger(
                $"{LogPrefix} playerOne could not send {signal}.",
                "PublishChartResetIfRequested",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        chartResetSent = true;
        Core.Logger($"{LogPrefix} playerOne sent {signal}.");
        return true;
    }

    private bool HasChartResetSignal(int fightAttempt) =>
        Duck.HasArmySignal(GetChartResetSignal(fightAttempt), 1);

    private void MaintainArchPaladinRighteousSeal(
        ref bool skillFourQueued
    )
    {
        if (!isArchPaladin)
            return;

        var righteousSeal = Bot.Target.GetAura(RighteousSealAura);
        if (righteousSeal == null)
        {
            skillFourQueued = false;
            return;
        }

        if (skillFourQueued || Duck.HasPendingPrioritySkill())
            return;

        TimeSpan remaining = righteousSeal.ExpiresAt - DateTimeOffset.Now;
        if (
            remaining <= TimeSpan.Zero
            || remaining
                > TimeSpan.FromMilliseconds(RighteousSealSkillFourWindow)
        )
            return;

        Duck.RequestPrioritySkill(4);
        skillFourQueued = true;
        Core.Logger(
            $"{LogPrefix} {playerAlias} queued skill 4 for Righteous Seal."
        );
    }

    private bool HandleFightReset(int fightAttempt)
    {
        StopFightSystems();
        Bot.Combat.CancelTarget();

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            Duck.ShouldResetFight(fightAttempt);
            Bot.Sleep(FightPollDelay);
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
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        return Sync($"FIGHT_RESET_{fightAttempt}_SAFE");
    }

    private void StopArmyAfterFailedAttempts()
    {
        if (masterMode)
        {
            if (Duck.IsArmyPlayer(1))
                Duck.StopArmySync("ATTEMPTS_EXHAUSTED");
            else
                Duck.SyncArmy("STOP_CHECK");

            Core.Logger(
                $"{LogPrefix} failed after {MaxFightAttempts} fight attempts.",
                "RunFightAttempts"
            );
            return;
        }

        if (Duck.IsArmyPlayer(1))
            Duck.StopArmySync("ATTEMPTS_EXHAUSTED");
        else
            Duck.SyncArmy("STOP_CHECK");

        Core.Logger(
            $"{LogPrefix} failed after {MaxFightAttempts} fight attempts.",
            "RunFightAttempts",
            messageBox: true,
            stopBot: true
        );
    }

    private SpeakerStep[] GetOpeningSteps() => new SpeakerStep[]
    {
        new(TruthMessage, dpsPlayerNumber, fresh: true),
        new(ListenMessage, looPlayerNumber, fresh: true),
    };

    private SpeakerStep[] GetSectionOneSteps() => new SpeakerStep[]
    {
        new(TruthMessage, apPlayerNumber, fresh: true, skillOwner: apPlayerNumber, skill: 3),
        new(ListenMessage, scPlayerNumber, fresh: true),
        new(TruthMessage, scPlayerNumber, fresh: false, skillOwner: looPlayerNumber, skill: 4),
    };

    private SpeakerStep[] GetSectionTwoSteps() => new SpeakerStep[]
    {
        new(ListenMessage, dpsPlayerNumber, fresh: true),
        new(TruthMessage, dpsPlayerNumber, fresh: false, skillOwner: apPlayerNumber, skill: 3),
        new(TruthMessage, looPlayerNumber, fresh: true),
        new(ListenMessage, looPlayerNumber, fresh: false),
    };

    private SpeakerStep[] GetSectionSteps(int section) => section switch
    {
        0 => GetOpeningSteps(),
        1 or 3 => GetSectionOneSteps(),
        2 or 4 => GetSectionTwoSteps(),
        _ => Array.Empty<SpeakerStep>(),
    };

    private int GetSectionStepCount(int section) =>
        GetSectionSteps(section).Length;

    private int GetZoneOwner(int cycle)
    {
        int[] zoneOwners = { scPlayerNumber, dpsPlayerNumber, apPlayerNumber, looPlayerNumber };
        return zoneOwners[(cycle - 1) % zoneOwners.Length];
    }

    private int GetArchPaladinPlayer() => apPlayerNumber;

    private bool IsTaunter() => true;

    private bool IsAtCoordinate(int x, int y) =>
        Math.Abs(Bot.Player.X - x) <= CoordinateTolerance
        && Math.Abs(Bot.Player.Y - y) <= CoordinateTolerance;

    private bool IsInBossRoom() =>
        string.Equals(Bot.Player.Cell, BossCell, StringComparison.OrdinalIgnoreCase);

    private bool IsInSafeRoom() =>
        string.Equals(Bot.Player.Cell, SafeCell, StringComparison.OrdinalIgnoreCase);

    private string GetWarningName(string warning) =>
        string.Equals(warning, TruthMessage, StringComparison.Ordinal)
            ? "Truth"
            : string.Equals(warning, ListenMessage, StringComparison.Ordinal)
                ? "Listen"
                : "Unknown";

    private string GetPortionName(int section) =>
        section == 0 ? "Opening" : $"Section {section}";

    private string GetZoneClearSignal(int fightAttempt, int cycle) =>
        $"SPEAKER_ZONE_{fightAttempt}_{cycle}_CLEAR";

    private string GetChartFailureSignal(int fightAttempt) =>
        $"SPEAKER_CHART_FAILURE_{fightAttempt}";

    private string GetChartResetSignal(int fightAttempt) =>
        $"SPEAKER_CHART_RESET_{fightAttempt}";

    private bool Sync(string step)
    {
        Duck.FileLog($"{playerAlias} entering sync step: {step}", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");

        if (!Duck.SyncArmy(step))
        {
            Duck.FileLog($"{playerAlias} failed sync step: {step}", LogPrefix);
            return false;
        }

        Duck.FileLog($"{playerAlias} continued from sync step: {step}", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
    }

    private void StopArmy()
    {
        if (Duck.IsArmyPlayer(1))
        {
            Bot.Sleep(2000);

            if (Bot.ShouldExit)
                return;

            if (Duck.StopArmySync("COMPLETE"))
                Core.Logger($"{LogPrefix} playerOne published COMPLETE.");
            else
                Core.Logger($"{LogPrefix} playerOne could not publish COMPLETE.");

            return;
        }

        if (Duck.SyncArmy("STOP_CHECK"))
            Core.Logger($"{LogPrefix} {playerAlias} unexpectedly passed STOP_CHECK.");
        else
            Core.Logger($"{LogPrefix} {playerAlias} detected COMPLETE.");
    }
}
