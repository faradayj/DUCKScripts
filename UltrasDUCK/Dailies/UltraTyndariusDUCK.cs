/*
name: Ultra Tyndarius DUCK
description: Four-player CoreDUCK Army script for Ultra Tyndarius.
tags: ultra, tyndarius, army, coreduck, daily
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraTyndariusDUCK
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

    private const string LogPrefix = "Ultra Tyndarius DUCK";
    private const string SyncFileName = "UltraTyndariusDUCK.sync";
    private const string MapName = "ultratyndarius";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string RighteousSealAura = "Righteous Seal";
    private const string FocusAura = "Focus";
    private const string RighteousSealSignalPrefix = "RIGHTEOUS_SEAL_READY_";
    private const string TauntSignalPrefix = "TYNDARIUS_TAUNT_";
    private const int UltraQuestId = 8245;
    private const int PrerequisiteQuestId = 8243;
    private const string PrerequisiteQuestName = "Avatar of Fire";
    private const int MinimumLevel = 61;
    private const int FirstAddMapId = 1;
    private const int MainBossMapId = 2;
    private const int SecondAddMapId = 3;
    private const int FocusRefreshMilliseconds = 1500;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DefaultPrivateRoomNumber = 1245;

    private string playerAlias = string.Empty;
    private bool isTaunter;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;
    private DuckAssignmentResult? currentAssignResult;

    public string OptionsStorage = "UltraTyndariusDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] TyndariusSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight", "Arcana Invoker", "King's Echo", "Void Highlord", "Legion Revenant" } },
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
            TyndariusSlots,
            armySize: 4
        );

        if (assignResult == null)
            return;

        currentAssignResult = assignResult;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        ClassPreset preset = assignResult.Preset;
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";
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
        Duck.FileLog($"{playerAlias} completing Ultra Tyndarius quest ({UltraQuestId}).", LogPrefix);
        Duck.CompleteUltraQuest(UltraQuestId);
        Duck.FileLog($"{playerAlias} completed Ultra Tyndarius quest ({UltraQuestId}).", LogPrefix);

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

            FightResult result;
            try
            {
                bool isAP = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
                if (isAP)
                {
                    if (
                        !PrepareRighteousSeal()
                        || !SendRighteousSealSignal(fightAttempt)
                    )
                        return false;
                }
                else
                {
                    if (
                        !WaitForRighteousSealSignal(fightAttempt)
                        || !MoveToBossRoom(useDirectFlash: true)
                    )
                        return false;
                }

                Duck.FileLog($"{playerAlias} jumped to {BossCell} for attempt {fightAttempt}.", LogPrefix);
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
                    Duck.FileLog($"{playerAlias} confirmed Ultra Tyndarius defeated on attempt {fightAttempt}.", LogPrefix);
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

        Duck.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

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
        Duck.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (isTaunter)
            Duck.EquipScroll(EnrageScroll);

        Duck.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool PrepareRighteousSeal()
    {
        Duck.FileLog($"{playerAlias} starting Righteous Seal preparation.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} starting Righteous Seal preparation.");

        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                Duck.FileLog($"{playerAlias} died during Righteous Seal preparation.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} died during Righteous Seal preparation.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                    Bot.Sleep(RespawnPollDelay);

                if (Bot.ShouldExit)
                    return false;

                Duck.FileLog($"{playerAlias} respawned and retrying Righteous Seal preparation.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} respawned and is retrying preparation.");
            }

            if (!MoveToBossRoom(useDirectFlash: true))
                return false;

            if (!Duck.IsMonsterAlive(MainBossMapId))
                return true;

            Duck.MaintainTarget(MainBossMapId);

            if (Bot.Target?.GetAura(RighteousSealAura) != null)
            {
                Duck.FileLog($"{playerAlias} confirmed Righteous Seal active.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} confirmed Righteous Seal.");
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
        string signal = GetRighteousSealSignal(fightAttempt);
        if (!Duck.SendArmySignal(signal))
            return false;

        Duck.FileLog($"{playerAlias} sent {signal}.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} sent {signal}.");
        return true;
    }

    private bool WaitForRighteousSealSignal(int fightAttempt)
    {
        string signal = GetRighteousSealSignal(fightAttempt);
        Duck.FileLog($"{playerAlias} waiting for {signal}...", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} waiting for {signal}.");

        while (!Bot.ShouldExit)
        {
            for (int p = 1; p <= 4; p++)
            {
                if (Duck.HasArmySignal(signal, p))
                {
                    Duck.FileLog($"{playerAlias} received {signal} from Player {p}.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} received {signal} from Player {p}.");
                    return true;
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return false;
    }

    private static string GetRighteousSealSignal(int fightAttempt) =>
        $"{RighteousSealSignalPrefix}{fightAttempt}";

    private bool MoveToBossRoom(bool useDirectFlash = false)
    {
        if (IsInBossRoom())
            return true;

        if (useDirectFlash)
        {
            Bot.Flash.Call("jumpCorrectRoom", BossCell, BossPad, false, false);

            while (!Bot.ShouldExit && !IsInBossRoom())
                Bot.Sleep(FightPollDelay);
        }
        else
        {
            Core.Jump(BossCell, BossPad);
        }

        return !Bot.ShouldExit && IsInBossRoom();
    }

    private FightResult Fight(ClassPreset preset, int fightAttempt)
    {
        bool isAP = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        bool isLOO = string.Equals(preset.ClassName, "Lord of Order", StringComparison.OrdinalIgnoreCase);

        int apPlayer = currentAssignResult?.GetPlayerNumberForClass("ArchPaladin") ?? 3;
        int looPlayer = currentAssignResult?.GetPlayerNumberForClass("Lord of Order") ?? 4;

        if (isAP)
        {
            return FightBossTaunter(
                preset,
                fightAttempt,
                isArchPaladin: true,
                partnerPlayerNumber: looPlayer
            );
        }

        if (isLOO)
        {
            return FightBossTaunter(
                preset,
                fightAttempt,
                isArchPaladin: false,
                partnerPlayerNumber: apPlayer
            );
        }

        // For non-boss taunters: one taunts FirstAdd (Left), one taunts SecondAdd (Right)
        List<int> otherPlayers = new();
        for (int p = 1; p <= 4; p++)
        {
            if (p != apPlayer && p != looPlayer)
                otherPlayers.Add(p);
        }

        int myPlayerNum = currentAssignResult?.PlayerNumber ?? 1;
        int tauntMapId = (otherPlayers.Count > 0 && myPlayerNum == otherPlayers[0])
            ? FirstAddMapId
            : SecondAddMapId;

        return FightAddTaunter(preset, fightAttempt, tauntMapId);
    }

    private FightResult FightAddTaunter(
        ClassPreset preset,
        int fightAttempt,
        int tauntMapId
    )
    {
        StartSkillEngine(preset);
        string addName = tauntMapId == FirstAddMapId ? "Left Orb" : "Right Orb";
        Duck.FileLog($"{playerAlias} started fighting and assigned to taunt {addName} (MapId {tauntMapId}).", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting (taunting {addName}).");

        while (!Bot.ShouldExit)
        {
            FightResult result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            bool immediateTauntAccepted = Duck.IsMonsterAlive(tauntMapId)
                && Duck.RequestImmediateTaunt(tauntMapId);

            if (!immediateTauntAccepted)
                Duck.MaintainTarget(GetPriorityTarget());

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightBossTaunter(
        ClassPreset preset,
        int fightAttempt,
        bool isArchPaladin,
        int partnerPlayerNumber
    )
    {
        StartSkillEngine(preset);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        bool ownsFocusCycle = false;
        bool waitingForOwnFocus = isArchPaladin;
        bool partnerDeathObserved = false;
        int nextSignalNumber = 1;
        DateTimeOffset focusBaseline = GetFocusExpiry();
        string partnerName = (
            Duck.GetArmyPlayerName(partnerPlayerNumber)
        ).Trim();
        string partnerAlias = $"Player {partnerPlayerNumber}";

        if (isArchPaladin)
        {
            RequestBossTaunt(immediate: false);
            Duck.FileLog($"{playerAlias} requested the opening boss taunt.", LogPrefix);
            Core.Logger($"{LogPrefix} {playerAlias} requested the first boss taunt.");
        }

        while (!Bot.ShouldExit)
        {
            FightResult result = RecoverFromDeath(
                fightAttempt,
                out bool recovered
            );
            if (result != FightResult.Continue)
                return FinishFight(result);

            Duck.MaintainTarget(MainBossMapId);

            if (recovered)
            {
                nextSignalNumber = AlignSignalNumber(nextSignalNumber, isArchPaladin);
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = GetFocusExpiry();
                RequestBossTaunt(immediate: false);
                Duck.FileLog($"{playerAlias} owns the next boss taunt after returning from respawn.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} owns the next boss taunt after returning.");
            }

            bool partnerFound = TryGetPartnerState(
                partnerName,
                out bool partnerDead,
                out bool partnerInBossRoom
            );

            if (partnerFound && partnerDead && !partnerDeathObserved)
            {
                string expectedSignal = GetTauntSignalName(
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
                Duck.FileLog($"{playerAlias} detected its taunt partner ({partnerAlias}) died.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} detected its taunt partner died.");
            }

            if (partnerDeathObserved)
            {
                if (partnerFound && !partnerDead && partnerInBossRoom)
                {
                    partnerDeathObserved = false;
                    nextSignalNumber = AlignSignalNumber(
                        nextSignalNumber,
                        !isArchPaladin
                    );
                    ownsFocusCycle = false;
                    waitingForOwnFocus = false;
                    Duck.FileLog($"{playerAlias} restored alternating boss taunts with {partnerAlias}.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} restored alternating boss taunts.");
                }
                else
                {
                    RequestBossTaunt(immediate: true);
                    Bot.Sleep(FightPollDelay);
                    continue;
                }
            }

            var focus = Bot.Target?.GetAura(FocusAura);
            if (
                waitingForOwnFocus
                && focus != null
                && focus.ExpiresAt > focusBaseline
            )
            {
                focusBaseline = focus.ExpiresAt;
                waitingForOwnFocus = false;
                ownsFocusCycle = true;
                Duck.FileLog($"{playerAlias} confirmed its Focus and owns the taunt cycle.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} confirmed its Focus and owns the taunt cycle.");
            }

            if (waitingForOwnFocus)
            {
                if (focus == null)
                    RequestBossTaunt(immediate: true);
            }
            else if (
                ownsFocusCycle
                && focus == null
            )
            {
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = DateTimeOffset.MinValue;
                RequestBossTaunt(immediate: true);
            }
            else if (
                ownsFocusCycle
                && focus != null
                && focus.ExpiresAt - DateTimeOffset.Now
                    <= TimeSpan.FromMilliseconds(FocusRefreshMilliseconds)
            )
            {
                string signal = GetTauntSignalName(
                    fightAttempt,
                    nextSignalNumber
                );
                if (Duck.SendArmySignal(signal))
                {
                    Duck.FileLog($"{playerAlias} sent {signal} to {partnerAlias}.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} sent {signal} to {partnerAlias}.");
                    nextSignalNumber++;
                    ownsFocusCycle = false;
                }
            }
            else if (!ownsFocusCycle && !waitingForOwnFocus)
            {
                string signal = GetTauntSignalName(
                    fightAttempt,
                    nextSignalNumber
                );
                if (Duck.HasArmySignal(signal, partnerPlayerNumber))
                {
                    nextSignalNumber++;
                    focusBaseline = GetFocusExpiry();
                    waitingForOwnFocus = true;
                    RequestBossTaunt(immediate: false);
                    Duck.FileLog($"{playerAlias} received {signal} and requested its scheduled boss taunt.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} received {signal} and requested its scheduled boss taunt.");
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private void RequestBossTaunt(bool immediate)
    {
        if (immediate)
            Duck.RequestImmediateTaunt(MainBossMapId);
        else
            Duck.RequestTaunt(MainBossMapId);
    }

    private static string GetTauntSignalName(
        int fightAttempt,
        int signalNumber
    ) =>
        $"{TauntSignalPrefix}{fightAttempt}_{signalNumber}";

    private static int AlignSignalNumber(
        int signalNumber,
        bool senderIsArchPaladin
    )
    {
        bool signalIsArchPaladin = signalNumber % 2 != 0;
        return signalIsArchPaladin == senderIsArchPaladin
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
            maintainedPotion: null
        );
    }

    private int GetPriorityTarget()
    {
        if (Duck.IsMonsterAlive(SecondAddMapId))
            return SecondAddMapId;

        if (Duck.IsMonsterAlive(FirstAddMapId))
            return FirstAddMapId;

        return MainBossMapId;
    }

    private FightResult GetFightResult(int fightAttempt)
    {
        if (!Duck.IsMonsterAlive(MainBossMapId))
            return FightResult.Defeated;

        return Duck.ShouldResetFight(fightAttempt)
            ? FightResult.Reset
            : FightResult.Continue;
    }

    private FightResult RecoverFromDeath(
        int fightAttempt,
        out bool recovered
    )
    {
        recovered = false;

        if (Bot.Player.Alive)
            return GetFightResult(fightAttempt);

        Duck.FileLog($"{playerAlias} died during fight attempt {fightAttempt}.", LogPrefix);
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

        Duck.FileLog($"{playerAlias} respawned.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} respawned.");

        if (!MoveToBossRoom())
            return FightResult.Stopped;

        recovered = true;
        return FightResult.Continue;
    }

    private bool TryGetPartnerState(
        string partnerName,
        out bool dead,
        out bool inBossRoom
    )
    {
        dead = false;
        inBossRoom = false;

        if (string.IsNullOrWhiteSpace(partnerName))
            return false;

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
        Bot.Target?.GetAura(FocusAura)?.ExpiresAt ?? DateTimeOffset.MinValue;

    private FightResult FinishFight(FightResult result)
    {
        Duck.StopSkillEngine();

        if (result != FightResult.Defeated)
            return result;

        Core.Jump(SafeCell, SafePad);
        Duck.FileLog($"{playerAlias} confirmed Ultra Tyndarius defeated.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Tyndarius defeated.");
        return FightResult.Defeated;
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

    private bool IsInSafeRoom() =>
        string.Equals(Bot.Player.Cell, SafeCell, StringComparison.OrdinalIgnoreCase);

    private bool IsInBossRoom() =>
        string.Equals(Bot.Player.Cell, BossCell, StringComparison.OrdinalIgnoreCase);

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
