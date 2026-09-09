/*
name: Ultra Tyndarius LW
description: Four-player CoreDUCK Army script for Ultra Tyndarius.
tags: ultra, tyndarius, army, coreduck
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
    public enum ArmyComposition
    {
        Default,
        Stable,
        Reliable,
        Fast,
        Test,
        Test2,
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
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

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
        // Bot.Config?.Configure();

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
        isTaunter = Duck.IsArmyPlayer(1) || Duck.IsArmyPlayer(2);

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
        Core.Logger($"{LogPrefix} playerThree starting Righteous Seal preparation.");

        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                Core.Logger($"{LogPrefix} playerThree died during Righteous Seal preparation.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                    Bot.Sleep(RespawnPollDelay);

                if (Bot.ShouldExit)
                    return false;

                Core.Logger($"{LogPrefix} playerThree respawned and is retrying preparation.");
            }

            if (!MoveToBossRoom(useDirectFlash: true))
                return false;

            if (!Duck.IsMonsterAlive(MainBossMapId))
                return true;

            Duck.MaintainTarget(MainBossMapId);

            if (Bot.Target.GetAura(RighteousSealAura) != null)
            {
                Core.Logger($"{LogPrefix} playerThree confirmed Righteous Seal.");
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

        Core.Logger($"{LogPrefix} playerThree sent {signal}.");
        return true;
    }

    private bool WaitForRighteousSealSignal(int fightAttempt)
    {
        string signal = GetRighteousSealSignal(fightAttempt);
        Core.Logger($"{LogPrefix} {playerAlias} waiting for {signal}.");

        while (!Bot.ShouldExit)
        {
            for (int p = 1; p <= 4; p++)
            {
                if (Duck.HasArmySignal(signal, p))
                {
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
        if (Bot.Player.Cell == BossCell && Bot.Player.Pad == BossPad)
            return true;

        if (useDirectFlash)
        {
            Bot.Flash.Call("jumpCorrectRoom", BossCell, BossPad, false, false);

            while (
                !Bot.ShouldExit
                && (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
            )
                Bot.Sleep(FightPollDelay);
        }
        else
        {
            Core.Jump(BossCell, BossPad);
        }

        return !Bot.ShouldExit
            && Bot.Player.Cell == BossCell
            && Bot.Player.Pad == BossPad;
    }

    private DuckAssignmentResult? currentAssignResult;

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
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

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

    private FightResult FightRightAddThenBoss(
        ClassPreset preset,
        int fightAttempt,
        bool tauntLeftAdd
    )
    {
        StartSkillEngine(preset);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");
        bool bossLocked = false;

        while (!Bot.ShouldExit)
        {
            FightResult result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            bool immediateTauntAccepted = tauntLeftAdd
                && Duck.IsMonsterAlive(FirstAddMapId)
                && Duck.RequestImmediateTaunt(FirstAddMapId);

            if (!immediateTauntAccepted)
            {
                int targetMapId = bossLocked
                    ? MainBossMapId
                    : Duck.IsMonsterAlive(SecondAddMapId)
                        ? SecondAddMapId
                        : MainBossMapId;

                if (targetMapId == MainBossMapId)
                    bossLocked = true;

                Duck.MaintainTarget(targetMapId);
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private FightResult FightDamageDealer(ClassPreset preset, int fightAttempt)
    {
        StartSkillEngine(preset);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");
        bool bossLocked = false;

        while (!Bot.ShouldExit)
        {
            FightResult result = RecoverFromDeath(fightAttempt, out _);
            if (result != FightResult.Continue)
                return FinishFight(result);

            int targetMapId = bossLocked
                ? MainBossMapId
                : GetPriorityTarget();

            if (targetMapId == MainBossMapId)
                bossLocked = true;

            Duck.MaintainTarget(targetMapId);
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
        string partnerAlias = isArchPaladin ? "playerFour" : "playerThree";

        if (isArchPaladin)
        {
            RequestBossTaunt(immediate: false);
            Core.Logger($"{LogPrefix} playerThree requested the first boss taunt.");
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
                    Core.Logger($"{LogPrefix} {playerAlias} restored alternating boss taunts.");
                }
                else
                {
                    RequestBossTaunt(immediate: true);
                    Bot.Sleep(FightPollDelay);
                    continue;
                }
            }

            var focus = Bot.Target.GetAura(FocusAura);
            if (
                waitingForOwnFocus
                && focus != null
                && focus.ExpiresAt > focusBaseline
            )
            {
                focusBaseline = focus.ExpiresAt;
                waitingForOwnFocus = false;
                ownsFocusCycle = true;
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
                    Core.Logger($"{LogPrefix} {playerAlias} received {signal} and requested its scheduled boss taunt.");
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishFight(FightResult.Stopped);
    }

    private void RequestBossTaunt(bool immediate)
    {
        if (armyComposition == ArmyComposition.Test2)
            Duck.RequestAbsolutePriorityTaunt(MainBossMapId);
        else if (immediate)
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
            maintainedPotion: !isTaunter
                
                    ? preset.CombatPotion
                    : null
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

    private FightResult FinishFight(FightResult result)
    {
        Duck.StopSkillEngine();

        if (result != FightResult.Defeated)
            return result;

        Core.Jump(SafeCell, SafePad);
        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Tyndarius defeated.");
        return FightResult.Defeated;
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

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

    private ClassPreset GetClassPreset()
    {
        if (Duck.IsArmyPlayer(1))
            return armyComposition switch
            {
                ArmyComposition.Stable => Duck.KingsEcho(),
                ArmyComposition.Reliable => Duck.VerusDoomKnight(),
                ArmyComposition.Fast => Duck.ArcanaInvoker(),
                _ => Duck.LegionRevenant(),
            };

        if (Duck.IsArmyPlayer(2))
            return Duck.StoneCrusher();

        if (Duck.IsArmyPlayer(3))
            return Duck.ArchPaladin();

        return Duck.LordOfOrder();
    }

    private bool UsesDefaultFightRoles() =>
        armyComposition == ArmyComposition.Default
        || armyComposition == ArmyComposition.Reliable
        || armyComposition == ArmyComposition.Test2;

    private bool IsTaunterRole() => true;

    private string GetPlayerAlias()
    {
        if (Duck.IsArmyPlayer(1))
            return "playerOne";

        if (Duck.IsArmyPlayer(2))
            return "playerTwo";

        if (Duck.IsArmyPlayer(3))
            return "playerThree";

        return "playerFour";
    }

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
