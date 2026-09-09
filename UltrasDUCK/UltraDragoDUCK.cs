/*
name: Ultra Drago LW
description: Four-player CoreDUCK Army script for Ultra Drago.
tags: ultra, drago, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraDragoDUCK
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Reliable,
        Test,
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

    private const string LogPrefix = "UltraDragoDUCK";
    private const string SyncFileName = "UltraDragoDUCK.sync";
    private const string MapName = "ultradrago";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string FocusAura = "Focus";
    private const string EntryTimestampName = "DRAGO_ENTRY_TIME";
    private const string AlgieTauntSignalPrefix = "ALGIE_TAUNT_";
    private const string DeneTauntSignalPrefix = "DENIE_TAUNT_";
    private const int UltraQuestId = 8397;
    private const int PrerequisiteQuestId = 8395;
    private const string PrerequisiteQuestName = "Mahapadma";
    private const int MinimumLevel = 80;
    private const int DeneMapId = 1;
    private const int DragoMapId = 2;
    private const int AlgieMapId = 3;
    private const int EntryLeadMilliseconds = 2000;
    private const int FocusRefreshMilliseconds = 1500;
    private const int TargetSettleDelay = 50;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int EntryTimeoutMilliseconds = 5000;
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
    private bool isTaunter = true;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private ArmyComposition armyComposition = ArmyComposition.Default;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraDragoDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] DragoSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight", "King's Echo", "Void Highlord", "Legion Revenant" } },
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
            DragoSlots,
            armySize: 4
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        currentAssignResult = assignResult;
        ClassPreset preset = assignResult.Preset;
        ApplyDragoOverrides(preset, assignResult.RoleName);
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";

        isArchPaladin = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        isLordOfOrder = string.Equals(preset.ClassName, "Lord of Order", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(preset.ClassName, "LordOfOrder", StringComparison.OrdinalIgnoreCase);
        isStoneCrusher = string.Equals(preset.ClassName, "StoneCrusher", StringComparison.OrdinalIgnoreCase);
        isDPS = !isArchPaladin && !isLordOfOrder && !isStoneCrusher;

        ResolveRolePlayerNumbers(assignResult);

        isTaunter = true;
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

    private void ApplyDragoOverrides(ClassPreset preset, string roleName)
    {
        if (string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset.ClassName, "StoneCrusher", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset.ClassName, "Legion Revenant", StringComparison.OrdinalIgnoreCase))
        {
            preset.CapeEnhancement = CapeSpecial.Penitence;
        }

        if (string.Equals(preset.ClassName, "Legion Revenant", StringComparison.OrdinalIgnoreCase))
        {
            preset.HelmEnhancement = HelmSpecial.None;
        }
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
                  || string.Equals(kvp.Value, "LordOfOrder", StringComparison.OrdinalIgnoreCase))
                looPlayerNumber = kvp.Key;
            else if (string.Equals(kvp.Value, "StoneCrusher", StringComparison.OrdinalIgnoreCase))
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

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            Duck.GenericPrebuff();
            if (Bot.ShouldExit)
                return false;

            long entryTimestamp = GetEntryTimestamp(fightAttempt);
            if (entryTimestamp <= 0)
                return false;

            int initialTargetMapId = GetInitialTarget();
            Duck.MaintainTarget(initialTargetMapId);
            StartSkillEngine(preset);

            if (
                !WaitForEntryTimestamp(entryTimestamp)
                || !AggressiveMoveToBossRoom(initialTargetMapId)
            )
                return false;

            FightResult result = Fight(fightAttempt);
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

    private bool ValidateOptions()
    {
        armyComposition = ArmyComposition.Default;
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
        if (true)
            Duck.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );

        if (isTaunter)
            Duck.EquipScroll(EnrageScroll);
        return !Bot.ShouldExit;
    }

    private long GetEntryTimestamp(int fightAttempt)
    {
        string timestampName = $"{EntryTimestampName}_{fightAttempt}";
        if (Duck.IsArmyPlayer(1))
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                + EntryLeadMilliseconds;

            if (!Duck.SendArmyTimestamp(timestampName, timestamp))
                return 0;

            Core.Logger($"{LogPrefix} playerOne published the entry timestamp.");
            return timestamp;
        }

        long entryTimestamp = 0;
        while (!Bot.ShouldExit && entryTimestamp <= 0)
        {
            entryTimestamp = Duck.GetArmyTimestamp(timestampName, 1);
            if (entryTimestamp <= 0)
                Bot.Sleep(FightPollDelay);
        }

        if (entryTimestamp > 0)
            Core.Logger($"{LogPrefix} {playerAlias} received the entry timestamp.");

        return entryTimestamp;
    }

    private bool WaitForEntryTimestamp(long entryTimestamp)
    {
        while (!Bot.ShouldExit)
        {
            long remaining = entryTimestamp
                - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (remaining <= 0)
                return true;

            Bot.Sleep((int)Math.Min(remaining, TargetSettleDelay));
        }

        return false;
    }

    private bool AggressiveMoveToBossRoom(int targetMapId)
    {
        if (IsInBossRoom())
            return true;

        Bot.Combat.Attack(targetMapId);
        Bot.Flash.Call("jumpCorrectRoom", BossCell, BossPad, false, false);

        if (WaitForBossRoom(targetMapId))
            return true;

        Bot.Map.Jump(BossCell, BossPad, autoCorrect: false);
        if (WaitForBossRoom(targetMapId))
            return true;

        Core.Logger(
            $"{LogPrefix} {playerAlias} could not enter the boss room.",
            "AggressiveMoveToBossRoom",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private bool WaitForBossRoom(int targetMapId)
    {
        DateTimeOffset timeout = DateTimeOffset.Now.AddMilliseconds(
            EntryTimeoutMilliseconds
        );

        while (!Bot.ShouldExit && DateTimeOffset.Now < timeout)
        {
            if (IsInBossRoom())
                return true;

            Bot.Combat.Attack(targetMapId);
            Bot.Sleep(FightPollDelay);
        }

        return IsInBossRoom();
    }

    private FightResult Fight(int fightAttempt)
    {
        if (armyComposition != ArmyComposition.Stable)
            return FightGuardPair(fightAttempt);

        if (Duck.IsArmyPlayer(1))
            return FightDamageDealer(fightAttempt);

        if (Duck.IsArmyPlayer(4))
            return FightImmediateAlgieTaunter(fightAttempt);

        return FightGuardPair(fightAttempt);
    }

    private FightResult FightDamageDealer(int fightAttempt)
    {
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt);
            if (result != FightResult.Continue)
                return FinishFight(result);

            result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            Duck.MaintainTarget(GetNormalTarget());
            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightImmediateAlgieTaunter(int fightAttempt)
    {
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt);
            if (result != FightResult.Continue)
                return FinishFight(result);

            result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            bool immediateTauntAccepted = Duck.IsMonsterAlive(AlgieMapId)
                && Duck.RequestImmediateTaunt(AlgieMapId);

            if (!immediateTauntAccepted)
                Duck.MaintainTarget(GetNormalTarget());

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightGuardPair(int fightAttempt)
    {
        int assignedGuardMapId = GetAssignedGuardMapId();
        int partnerPlayerNumber = GetPartnerPlayerNumber();
        bool openingOwner = IsOpeningOwner();
        string signalPrefix = GetTauntSignalPrefix();
        string partnerName = GetPartnerPlayerName(partnerPlayerNumber);
        string partnerAlias = $"Player {partnerPlayerNumber}";

        bool pairClosed = false;
        bool ownsFocusCycle = false;
        bool waitingForOwnFocus = openingOwner;
        bool partnerDeathObserved = false;
        int nextSignalNumber = 1;
        DateTimeOffset focusBaseline = DateTimeOffset.MinValue;
        DateTimeOffset nextFocusInspection = DateTimeOffset.MinValue;

        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        if (openingOwner && Duck.IsMonsterAlive(assignedGuardMapId))
        {
            focusBaseline = BeginTauntAcquisition(assignedGuardMapId);
            Core.Logger($"{LogPrefix} {playerAlias} requested the first guard taunt.");
        }

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt);
            if (result != FightResult.Continue)
                return FinishFight(result);

            result = RecoverFromDeath(fightAttempt, out bool recovered);
            if (result != FightResult.Continue)
                return FinishFight(result);

            if (!pairClosed && !Duck.IsMonsterAlive(assignedGuardMapId))
            {
                pairClosed = true;
                ownsFocusCycle = false;
                waitingForOwnFocus = false;
                Core.Logger($"{LogPrefix} {playerAlias} closed its guard taunt loop.");
            }

            if (pairClosed)
            {
                Duck.MaintainTarget(GetNormalTarget());
                Bot.Sleep(FightPollDelay);
                continue;
            }

            if (recovered)
            {
                nextSignalNumber = AlignSignalNumber(
                    nextSignalNumber,
                    openingOwner
                );
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = BeginTauntAcquisition(assignedGuardMapId);
                Core.Logger($"{LogPrefix} {playerAlias} owns the next guard taunt after returning.");
            }

            if (!waitingForOwnFocus)
                Duck.MaintainTarget(GetNormalTarget());

            bool partnerFound = TryGetPartnerState(
                partnerName,
                out bool partnerDead,
                out bool partnerInBossRoom
            );

            if (partnerFound && partnerDead && !partnerDeathObserved)
            {
                string expectedSignal = GetTauntSignalName(
                    signalPrefix,
                    fightAttempt,
                    nextSignalNumber
                );
                if (
                    !ownsFocusCycle
                    && !waitingForOwnFocus
                    && Duck.HasArmySignal(expectedSignal, partnerPlayerNumber)
                )
                    nextSignalNumber++;

                partnerDeathObserved = true;
                ownsFocusCycle = false;
                waitingForOwnFocus = false;
                Core.Logger($"{LogPrefix} {playerAlias} detected its taunt partner died.");
            }

            if (partnerDeathObserved)
            {
                if (partnerFound && !partnerDead && partnerInBossRoom)
                {
                    partnerDeathObserved = false;
                    nextSignalNumber = AlignSignalNumber(
                        nextSignalNumber,
                        !openingOwner
                    );
                    ownsFocusCycle = false;
                    waitingForOwnFocus = false;
                    Core.Logger($"{LogPrefix} {playerAlias} restored alternating guard taunts.");
                }
                else
                {
                    RequestImmediateGuardTaunt(assignedGuardMapId);
                    Bot.Sleep(FightPollDelay);
                    continue;
                }
            }

            if (waitingForOwnFocus)
            {
                SelectGuard(assignedGuardMapId);
                var focus = Bot.Target.GetAura(FocusAura);

                if (focus != null && focus.ExpiresAt > focusBaseline)
                {
                    focusBaseline = focus.ExpiresAt;
                    nextFocusInspection = focus.ExpiresAt
                        - TimeSpan.FromMilliseconds(FocusRefreshMilliseconds);
                    waitingForOwnFocus = false;
                    ownsFocusCycle = true;
                    Duck.MaintainTarget(GetNormalTarget());
                    Core.Logger($"{LogPrefix} {playerAlias} confirmed its Focus and owns the taunt cycle.");
                }
                else if (focus == null)
                {
                    RequestImmediateGuardTaunt(assignedGuardMapId);
                }
            }
            else if (
                ownsFocusCycle
                && DateTimeOffset.Now >= nextFocusInspection
            )
            {
                SelectGuard(assignedGuardMapId);
                var focus = Bot.Target.GetAura(FocusAura);

                if (focus == null)
                {
                    ownsFocusCycle = false;
                    waitingForOwnFocus = true;
                    focusBaseline = DateTimeOffset.MinValue;
                    RequestImmediateGuardTaunt(assignedGuardMapId);
                }
                else if (
                    focus.ExpiresAt - DateTimeOffset.Now
                    <= TimeSpan.FromMilliseconds(FocusRefreshMilliseconds)
                )
                {
                    string signal = GetTauntSignalName(
                        signalPrefix,
                        fightAttempt,
                        nextSignalNumber
                    );
                    if (Duck.SendArmySignal(signal))
                    {
                        Core.Logger($"{LogPrefix} {playerAlias} sent {signal} to {partnerAlias}.");
                        nextSignalNumber++;
                        ownsFocusCycle = false;
                        Duck.MaintainTarget(GetNormalTarget());
                    }
                }
                else
                {
                    nextFocusInspection = focus.ExpiresAt
                        - TimeSpan.FromMilliseconds(FocusRefreshMilliseconds);
                    Duck.MaintainTarget(GetNormalTarget());
                }
            }
            else if (!ownsFocusCycle && !waitingForOwnFocus)
            {
                string signal = GetTauntSignalName(
                    signalPrefix,
                    fightAttempt,
                    nextSignalNumber
                );
                if (Duck.HasArmySignal(signal, partnerPlayerNumber))
                {
                    nextSignalNumber++;
                    focusBaseline = BeginTauntAcquisition(assignedGuardMapId);
                    waitingForOwnFocus = true;
                    Core.Logger($"{LogPrefix} {playerAlias} received {signal} and requested its scheduled guard taunt.");
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FinishFight(FightResult result)
    {
        Duck.StopSkillEngine();

        if (Bot.ShouldExit || result == FightResult.Stopped)
            return FightResult.Stopped;

        if (result == FightResult.Defeated)
            Core.Logger($"{LogPrefix} {playerAlias} confirmed King Drago defeated.");
        else if (result == FightResult.Reset)
            Core.Logger($"{LogPrefix} {playerAlias} received the coordinated fight reset.");

        return result;
    }

    private DateTimeOffset BeginTauntAcquisition(int guardMapId)
    {
        SelectGuard(guardMapId);
        DateTimeOffset baseline = GetFocusExpiry();
        if (armyComposition == ArmyComposition.Test)
            Duck.RequestAbsolutePriorityTaunt(guardMapId);
        else
            Duck.RequestTaunt(guardMapId);
        return baseline;
    }

    private void RequestImmediateGuardTaunt(int guardMapId)
    {
        if (armyComposition == ArmyComposition.Test)
            Duck.RequestAbsolutePriorityTaunt(guardMapId);
        else
            Duck.RequestImmediateTaunt(guardMapId);
    }

    private void SelectGuard(int guardMapId)
    {
        var target = Bot.Player.Target;
        bool changed = !Bot.Player.HasTarget
            || target?.MapID != guardMapId
            || target?.HP <= 0;

        Duck.MaintainTarget(guardMapId);
        if (changed)
            Bot.Sleep(TargetSettleDelay);
    }

    private int GetInitialTarget() =>
        isArchPaladin || isStoneCrusher
            ? DeneMapId
            : AlgieMapId;

    private int GetNormalTarget()
    {
        bool algieAlive = Duck.IsMonsterAlive(AlgieMapId);
        bool deneAlive = Duck.IsMonsterAlive(DeneMapId);

        if (algieAlive && deneAlive)
            return isArchPaladin || isStoneCrusher ? DeneMapId : AlgieMapId;

        if (algieAlive)
            return AlgieMapId;

        if (deneAlive)
            return DeneMapId;

        return DragoMapId;
    }

    private int GetAssignedGuardMapId() =>
        isDPS || isLordOfOrder
            ? AlgieMapId
            : DeneMapId;

    private int GetPartnerPlayerNumber()
    {
        if (isDPS)
            return looPlayerNumber;
        if (isLordOfOrder)
            return dpsPlayerNumber;
        if (isArchPaladin)
            return scPlayerNumber;
        return apPlayerNumber;
    }

    private string GetPartnerPlayerName(int partnerPlayerNumber)
    {
        if (currentAssignResult?.DiscoveredPlayers != null
            && partnerPlayerNumber >= 1
            && partnerPlayerNumber <= currentAssignResult.DiscoveredPlayers.Length)
        {
            return currentAssignResult.DiscoveredPlayers[partnerPlayerNumber - 1];
        }
        return string.Empty;
    }

    private bool IsOpeningOwner() =>
        isDPS || isArchPaladin;

    private string GetTauntSignalPrefix() =>
        isDPS || isLordOfOrder
            ? AlgieTauntSignalPrefix
            : DeneTauntSignalPrefix;

    private static string GetTauntSignalName(
        string signalPrefix,
        int fightAttempt,
        int signalNumber
    ) => $"{signalPrefix}{fightAttempt}_{signalNumber}";

    private static int AlignSignalNumber(
        int signalNumber,
        bool senderIsOpeningOwner
    )
    {
        bool signalIsOpeningOwner = signalNumber % 2 != 0;
        return signalIsOpeningOwner == senderIsOpeningOwner
            ? signalNumber
            : signalNumber + 1;
    }

    private void StartSkillEngine(ClassPreset preset)
    {
        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            isTaunter,
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: !isTaunter
                && true
                    ? preset.CombatPotion
                    : null
        );
    }

    private FightResult GetFightResult(int fightAttempt)
    {
        if (!Duck.IsMonsterAlive(DragoMapId))
            return FightResult.Defeated;

        return Duck.ShouldResetFight(fightAttempt)
            ? FightResult.Reset
            : FightResult.Continue;
    }

    private FightResult RecoverFromDeath(int fightAttempt, out bool recovered)
    {
        recovered = false;

        if (Bot.Player.Alive)
            return FightResult.Continue;

        Core.Logger($"{LogPrefix} {playerAlias} died.");

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            if (Duck.ShouldResetFight(fightAttempt))
                return FightResult.Reset;

            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        FightResult result = GetFightResult(fightAttempt);
        if (result != FightResult.Continue)
            return result;

        Core.Logger($"{LogPrefix} {playerAlias} respawned.");

        if (!AggressiveMoveToBossRoom(GetNormalTarget()))
            return FightResult.Stopped;

        recovered = true;
        return FightResult.Continue;
    }

    private bool HandleFightReset(int fightAttempt)
    {
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

    private bool TryGetPartnerState(
        string partnerName,
        out bool dead,
        out bool inBossRoom
    )
    {
        dead = false;
        inBossRoom = false;

        var players = Bot.Map.Players;
        if (players == null)
            return false;

        foreach (var player in players)
        {
            if (!string.Equals(player.Name, partnerName, StringComparison.OrdinalIgnoreCase))
                continue;

            dead = player.State == 0;
            inBossRoom = !dead
                && string.Equals(player.Cell, BossCell, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        return false;
    }

    private DateTimeOffset GetFocusExpiry() =>
        Bot.Target.GetAura(FocusAura)?.ExpiresAt ?? DateTimeOffset.MinValue;

    private bool IsInBossRoom() =>
        Bot.Player.Cell == BossCell && Bot.Player.Pad == BossPad;

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

    private bool Sync(string step)
    {
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");

        if (!Duck.SyncArmy(step))
            return false;

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
