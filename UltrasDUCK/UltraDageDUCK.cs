/*
name: Ultra Dage LW
description: Four-player CoreDUCK Army script for Ultra Dage.
tags: ultra, dage, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/Customv2/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraDageDUCK
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

    private const string LogPrefix = "UltraDageDUCK";
    private const string SyncFileName = "UltraDageDUCK.sync";
    private const string MapName = "ultradage";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Right";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string DecayScroll = "Scroll of Decay";
    private const string MystifyScroll = "Scroll of Mystify";
    private const string NoxiousDecayAura = "Noxious Decay";
    private const string UnleashedDoomAura = "Unleashed Doom";
    private const string FocusAura = "Focus";
    private const string DecayMessage =
        "I possess the full power of the Legion at my disposal.";
    private const string LegionPromotion = "Legion Promotion";
    private const int UltraQuestId = 8547;
    private const int WeeklyQuestId = 8547;
    private const int LegionDailyQuestId = 1678;
    private const int PrerequisiteQuestId = 793;
    private const string PrerequisiteQuestName = "Fail to the King";
    private const int MinimumLevel = 80;
    private const int DageMapId = 1;
    private const int FocusRefreshMilliseconds = 1500;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DefaultPrivateRoomNumber = 1245;

    private readonly Queue<string> pendingZones = new();
    private readonly object zoneLock = new();
    private int dpsPlayerNumber = 1;
    private int scPlayerNumber = 2;
    private int apPlayerNumber = 3;
    private int looPlayerNumber = 4;
    private string playerAlias = string.Empty;
    private bool isTaunter;
    private bool isArchPaladin;
    private bool isLordOfOrder;
    private bool isStoneCrusher;
    private bool isDPS;
    private bool isOpeningTauntOwner;
    private DuckAssignmentResult? currentAssignResult;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraDageDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] DageSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight", "King's Echo", "Legion Revenant" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "StoneCrusher" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchPaladin" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Lord of Order" } }
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
            StopAttemptSystems();
        }
    }

    public UltraRunResult RunFromMaster(int roomNumber = DefaultPrivateRoomNumber)
    {
        masterMode = true;
        privateRoomNumber = roomNumber;
        runResult = UltraRunResult.Failed;
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
            StopAttemptSystems();
            masterMode = false;
        }

        return runResult;
    }

    private void Run()
    {
        Bot.Options.AcceptACDrops = true;
        Bot.Options.RejectAllDrops = false;

        if (!masterMode)
            privateRoomNumber = DefaultPrivateRoomNumber;

        if (!Duck.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        DuckAssignmentResult? assignResult = Duck.AutoAssignAndEquip(
            SyncFileName,
            DageSlots,
            armySize: 4
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        currentAssignResult = assignResult;
        ClassPreset preset = assignResult.Preset;
        ApplyDageOverrides(preset, assignResult.RoleName);
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";

        isArchPaladin = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        isLordOfOrder = string.Equals(preset.ClassName, "Lord of Order", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(preset.ClassName, "Lord Of Order", StringComparison.OrdinalIgnoreCase);
        isStoneCrusher = string.Equals(preset.ClassName, "StoneCrusher", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(preset.ClassName, "Infinity Titan", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(preset.ClassName, "InfinityTitan", StringComparison.OrdinalIgnoreCase);
        isDPS = !isArchPaladin && !isLordOfOrder && !isStoneCrusher;

        ResolveRolePlayerNumbers(assignResult);

        // In standard comp: DPS is opening taunter, ArchPaladin is partner taunter
        isTaunter = isDPS || isArchPaladin;
        isOpeningTauntOwner = isDPS;
        armyComposition = ArmyComposition.Default;

        // Reserve Slot 5 for scrolls on all players
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

        Core.Unbank(LegionPromotion);
        Bot.Drops.Add("Legion Shard", "Lineage of Devastation", "Soul Sand");

        if (!Bot.Quests.IsDailyComplete(WeeklyQuestId) && !Bot.Quests.IsInProgress(WeeklyQuestId))
            Duck.AcceptUltraQuest(WeeklyQuestId);
        if (!Bot.Quests.IsDailyComplete(LegionDailyQuestId) && !Bot.Quests.IsInProgress(LegionDailyQuestId))
            Duck.AcceptUltraQuest(LegionDailyQuestId);

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        if (!RunFightAttempts(preset) || !Sync("BOSS_DEFEATED"))
            return;

        if (Bot.Quests.CanComplete(WeeklyQuestId))
            Duck.CompleteUltraQuest(WeeklyQuestId);
        if (Bot.Quests.CanComplete(LegionDailyQuestId))
            Duck.CompleteUltraQuest(LegionDailyQuestId);

                if (Bot.ShouldExit || !Sync("FINISH"))
            return;

        runResult = UltraRunResult.Completed;

        if (Duck.IsArmyPlayer(1))
        {
            Bot.Sleep(1000);
            Duck.ClearAllSyncFiles(SyncFileName);
        }
    }

    private void ApplyDageOverrides(ClassPreset preset, string roleName)
    {
        preset.WeaponEnhancement = WeaponSpecial.Health_Vamp;
        preset.WeaponEnhancementFallbacks = Array.Empty<WeaponSpecial>();
        preset.CapeEnhancement = CapeSpecial.Vainglory;

        if (roleName.Equals("ArchPaladin", StringComparison.OrdinalIgnoreCase))
            preset.Skills = new[] { 3, 1, 4 };
        else if (roleName.Equals("StoneCrusher", StringComparison.OrdinalIgnoreCase) || roleName.Equals("Infinity Titan", StringComparison.OrdinalIgnoreCase))
            preset.Skills = new[] { 2, 4, 1 };
        else if (roleName.Equals("LordOfOrder", StringComparison.OrdinalIgnoreCase) || roleName.Equals("Lord of Order", StringComparison.OrdinalIgnoreCase))
            preset.Skills = new[] { 3, 1, 4 };
        else if (roleName.Equals("Legion Revenant", StringComparison.OrdinalIgnoreCase))
            preset.Skills = new[] { 3, 4, 2, 1 };
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

            if (!PrepareSafeRoom(preset, fightAttempt, out bool mystifyMode))
                return false;

            StartZoneListener();

            try
            {
                if (!StartDecayDetector(mystifyMode) || !Sync("FIGHT_READY"))
                    return false;

                StartSkillEngine(preset);
                Core.Jump(BossCell, BossPad);
                Duck.FileLog($"{playerAlias} jumped to boss room for attempt {fightAttempt}.", LogPrefix);

                FightResult result = Fight(fightAttempt, mystifyMode);
                if (result == FightResult.Defeated)
                {
                    Core.Jump(SafeCell, SafePad);
                    bool CreditCheck() =>
                        Bot.Quests.CanComplete(WeeklyQuestId)
                        || Bot.Quests.CanComplete(LegionDailyQuestId)
                        || (Bot.Quests.IsDailyComplete(WeeklyQuestId) && Bot.Quests.IsDailyComplete(LegionDailyQuestId));

                    if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, 4, LogPrefix))
                    {
                        Duck.FileLog($"{playerAlias} confirmed Ultra Dage defeated on attempt {fightAttempt}.", LogPrefix);
                        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Dage defeated.");
                        return true;
                    }

                    Core.Logger($"{LogPrefix} One or more players missed kill credit on attempt {fightAttempt}. Retrying fight with entire army...");
                    result = FightResult.Reset;
                }

                if (result != FightResult.Reset)
                    return false;
            }
            finally
            {
                StopAttemptSystems();
            }

            if (!HandleFightReset(fightAttempt))
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

        bool stableKingsEcho = armyComposition == ArmyComposition.Stable
            && Duck.IsArmyPlayer(1);

        if (true)
            Duck.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                stableKingsEcho ? preset.CombatPotion : null
            );

        if (isTaunter)
            Duck.PrepareScrolls(EnrageScroll);
        else if (isStoneCrusher)
            Duck.PrepareScrolls(DecayScroll);
        else if (isLordOfOrder)
        {
            Duck.PrepareScrolls(MystifyScroll);
            Duck.PrepareScrolls(DecayScroll);
        }

        if (Bot.ShouldExit)
            return false;

        WarnIfHealthVampWeaponIsMissing();
        Duck.FileLog($"{playerAlias} finished setup.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private void WarnIfHealthVampWeaponIsMissing()
    {
        foreach (var item in Bot.Inventory.Items)
        {
            if (
                !item.Equipped
                || !string.Equals(
                    item.CategoryString,
                    "Weapon",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                continue;

            if (
                item.EnhancementPatternID
                != (int)WeaponSpecial.Health_Vamp
            )
                Core.Logger(
                    "Health Vamp is not enhanced on the weapon. Ultra Dage will fail.",
                    "Prepare",
                    messageBox: true
                );

            return;
        }
    }

    private bool PrepareSafeRoom(
        ClassPreset preset,
        int fightAttempt,
        out bool mystifyMode
    )
    {
        mystifyMode = false;

        bool stableKingsEcho = false;

        if (true)
            Duck.UsePotions(
                preset.Tonic,
                preset.Elixir,
                stableKingsEcho ? preset.CombatPotion : null
            );

        if (isTaunter)
            Duck.EquipScroll(EnrageScroll);
        else if (isStoneCrusher)
            Duck.EquipScroll(DecayScroll);
        else if (isLordOfOrder)
        {
            Duck.EquipScroll(MystifyScroll);
            mystifyMode = Bot.Inventory.IsEquipped(MystifyScroll);

            if (!mystifyMode)
                Duck.EquipScroll(DecayScroll);

            string modeSignal = mystifyMode
                ? GetMystifySignal(fightAttempt)
                : GetAlternatingDecaySignal(fightAttempt);

            if (!Duck.SendArmySignal(modeSignal))
            {
                Core.Logger(
                    $"{LogPrefix} {playerAlias} could not publish the scroll mode.",
                    "PrepareSafeRoom",
                    messageBox: true,
                    stopBot: true
                );
                return false;
            }

            Core.Logger($"{LogPrefix} {playerAlias} selected {(mystifyMode ? "Mystify" : "alternating Decay")} mode.");
        }

        if (Bot.ShouldExit || !Sync($"SCROLL_MODE_{fightAttempt}"))
            return false;

        if (!isLordOfOrder)
            mystifyMode = Duck.HasArmySignal(
                GetMystifySignal(fightAttempt),
                looPlayerNumber
            );

        return !Bot.ShouldExit;
    }

    private bool StartDecayDetector(bool mystifyMode)
    {
        bool detectsDecay = isStoneCrusher
            || (
                isLordOfOrder
                && !mystifyMode
            );

        return !detectsDecay
            || Duck.StartPacketDetector("ct", DecayMessage);
    }

    private FightResult Fight(int fightAttempt, bool mystifyMode)
    {
        if (isTaunter)
            return FightEnrageTaunter(fightAttempt);

        if (
            armyComposition == ArmyComposition.Stable
            && Duck.IsArmyPlayer(1)
        )
            return FightDamageDealer(fightAttempt);

        return FightScrollHolder(fightAttempt, mystifyMode);
    }

    private FightResult FightDamageDealer(int fightAttempt)
    {
        int nextDecayDetection = 1;
        bool bossObserved = Duck.IsMonsterAlive(DageMapId);

        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt, ref bossObserved);
            if (result != FightResult.Continue)
                return result;

            result = RecoverFromDeath(
                fightAttempt,
                ref bossObserved,
                ref nextDecayDetection,
                out _
            );
            if (result != FightResult.Continue)
                return result;

            DrainZoneEvents(move: true);
            Duck.MaintainTarget(DageMapId);
            Bot.Sleep(FightPollDelay);
        }

        return FightResult.Stopped;
    }

    private int ResolveTauntPartnerPlayerNumber() =>
        isDPS ? apPlayerNumber : dpsPlayerNumber;

    private FightResult FightEnrageTaunter(int fightAttempt)
    {
        bool openingOwner = isOpeningTauntOwner;
        int partnerPlayerNumber = ResolveTauntPartnerPlayerNumber();
        string partnerName = (
            Duck.GetArmyPlayerName(partnerPlayerNumber)
        ).Trim();
        string partnerAlias = $"Player {partnerPlayerNumber}";
        bool ownsFocusCycle = false;
        bool waitingForOwnFocus = openingOwner;
        bool partnerDeathObserved = false;
        bool safeHealUsedThisWindow = false;
        int nextSignalNumber = 1;
        int nextDecayDetection = 1;
        DateTimeOffset focusBaseline = GetFocusExpiry();
        bool bossObserved = Duck.IsMonsterAlive(DageMapId);

        if (openingOwner)
        {
            RequestEnrageTaunt();
            Core.Logger($"{LogPrefix} {playerAlias} requested the first Dage taunt.");
        }

        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt, ref bossObserved);
            if (result != FightResult.Continue)
                return result;

            result = RecoverFromDeath(
                fightAttempt,
                ref bossObserved,
                ref nextDecayDetection,
                out bool recovered
            );
            if (result != FightResult.Continue)
                return result;

            DrainZoneEvents(move: true);
            Duck.MaintainTarget(DageMapId);

            if (isArchPaladin)
                HandleSafeHeal(ref safeHealUsedThisWindow);

            if (recovered)
            {
                safeHealUsedThisWindow = false;
                nextSignalNumber = AlignSignalNumber(
                    nextSignalNumber,
                    openingOwner
                );
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = GetFocusExpiry();
                RequestEnrageTaunt();
                Core.Logger($"{LogPrefix} {playerAlias} owns the next Dage taunt after returning.");
            }

            bool partnerFound = TryGetPartnerState(
                partnerName,
                out bool partnerDead,
                out bool partnerInBossRoom
            );

            if (partnerFound && partnerDead && !partnerDeathObserved)
            {
                string expectedSignal = GetTauntSignal(
                    fightAttempt,
                    nextSignalNumber
                );
                if (
                    !ownsFocusCycle
                    && !waitingForOwnFocus
                    && Duck.HasArmySignal(
                        expectedSignal,
                        partnerPlayerNumber
                    )
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
                    Core.Logger($"{LogPrefix} {playerAlias} restored alternating Dage taunts.");
                }
                else
                {
                    RequestImmediateEnrageTaunt();
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
                    RequestImmediateEnrageTaunt();
            }
            else if (ownsFocusCycle && focus == null)
            {
                ownsFocusCycle = false;
                waitingForOwnFocus = true;
                focusBaseline = DateTimeOffset.MinValue;
                RequestImmediateEnrageTaunt();
            }
            else if (
                ownsFocusCycle
                && focus != null
                && focus.ExpiresAt - DateTimeOffset.Now
                    <= TimeSpan.FromMilliseconds(FocusRefreshMilliseconds)
            )
            {
                string signal = GetTauntSignal(fightAttempt, nextSignalNumber);
                if (Duck.SendArmySignal(signal))
                {
                    Core.Logger($"{LogPrefix} {playerAlias} sent {signal} to {partnerAlias}.");
                    nextSignalNumber++;
                    ownsFocusCycle = false;
                }
            }
            else if (!ownsFocusCycle && !waitingForOwnFocus)
            {
                string signal = GetTauntSignal(fightAttempt, nextSignalNumber);
                if (Duck.HasArmySignal(signal, partnerPlayerNumber))
                {
                    nextSignalNumber++;
                    focusBaseline = GetFocusExpiry();
                    waitingForOwnFocus = true;
                    RequestEnrageTaunt();
                    Core.Logger($"{LogPrefix} {playerAlias} received {signal} and requested its scheduled Dage taunt.");
                }
            }

            Bot.Sleep(FightPollDelay);
        }

        return FightResult.Stopped;
    }

    private void RequestEnrageTaunt()
    {
        Duck.RequestTaunt(DageMapId);
    }

    private void RequestImmediateEnrageTaunt()
    {
        Duck.RequestImmediateTaunt(DageMapId);
    }

    private FightResult FightScrollHolder(int fightAttempt, bool mystifyMode)
    {
        bool safeHealUsedThisWindow = false;
        bool packetCommandLogged = false;
        int nextDecayDetection = 1;
        bool bossObserved = Duck.IsMonsterAlive(DageMapId);

        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        while (!Bot.ShouldExit)
        {
            FightResult result = GetFightResult(fightAttempt, ref bossObserved);
            if (result != FightResult.Continue)
                return result;

            result = RecoverFromDeath(
                fightAttempt,
                ref bossObserved,
                ref nextDecayDetection,
                out bool recovered
            );
            if (result != FightResult.Continue)
                return result;

            if (recovered)
                safeHealUsedThisWindow = false;

            DrainZoneEvents(move: true);
            Duck.MaintainTarget(DageMapId);

            if (isLordOfOrder)
            {
                HandleSafeHeal(ref safeHealUsedThisWindow);

                if (mystifyMode)
                    Duck.RequestImmediateSkillFive(DageMapId);
            }

            if (isStoneCrusher || (isLordOfOrder && !mystifyMode))
            {
                HandleDecayDetections(
                    mystifyMode,
                    ref nextDecayDetection,
                    ref packetCommandLogged
                );
            }

            Bot.Sleep(FightPollDelay);
        }

        return FightResult.Stopped;
    }

    private void HandleDecayDetections(
        bool mystifyMode,
        ref int nextDetection,
        ref bool packetCommandLogged
    )
    {
        while (Duck.HasPacketDetection(nextDetection))
        {
            if (!packetCommandLogged)
            {
                string command = Duck.GetPacketDetectorCommand();
                if (command.Length > 0)
                {
                    Core.Logger($"{LogPrefix} {playerAlias} detected Decay in {command} packets.");
                    packetCommandLogged = true;
                }
            }

            int cycle = nextDetection++;
            bool ownsCycle = isStoneCrusher
                && (
                    mystifyMode
                    || cycle % 2 != 0
                )
                || isLordOfOrder
                    && !mystifyMode
                    && cycle % 2 == 0;

            if (!ownsCycle)
                continue;

            if (!Bot.Inventory.IsEquipped(DecayScroll))
            {
                if (Bot.Inventory.Contains(DecayScroll))
                    Duck.EquipScroll(DecayScroll);

                if (!Bot.Inventory.IsEquipped(DecayScroll))
                {
                    Core.Logger($"{LogPrefix} {playerAlias} consumed Decay cycle {cycle} without a cast because {DecayScroll} is not equipped.");
                    continue;
                }
            }

            Duck.RequestSkillFive(DageMapId);
            Core.Logger($"{LogPrefix} {playerAlias} requested Decay for cycle {cycle}.");
        }
    }

    private void HandleSafeHeal(ref bool safeHealUsedThisWindow)
    {
        var selfAuras = Bot.Self.Auras;
        if (selfAuras == null || selfAuras.Count == 0)
            return;

        if (Bot.Self.GetAura(NoxiousDecayAura) != null)
        {
            safeHealUsedThisWindow = false;
            return;
        }

        if (
            safeHealUsedThisWindow
            || !Bot.Player.Alive
            || !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != DageMapId
            || Bot.Player.Target?.HP <= 0
            || !Bot.Skills.CanUseSkill(2)
        )
            return;

        Bot.Skills.UseSkill(2);
        safeHealUsedThisWindow = true;
        Core.Logger($"{LogPrefix} {playerAlias} used its safe-window heal.");
    }

    private FightResult RecoverFromDeath(
        int fightAttempt,
        ref bool bossObserved,
        ref int nextDecayDetection,
        out bool recovered
    )
    {
        recovered = false;

        if (Bot.Player.Alive)
            return FightResult.Continue;

        Core.Logger($"{LogPrefix} {playerAlias} died.");

        while (!Bot.ShouldExit && !Bot.Player.Alive)
        {
            DrainZoneEvents(move: false);
            AdvanceDecayDetections(ref nextDecayDetection);

            if (Duck.ShouldResetFight(fightAttempt))
                return FightResult.Reset;

            Bot.Sleep(RespawnPollDelay);
        }

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        FightResult result = GetFightResult(fightAttempt, ref bossObserved);
        if (result != FightResult.Continue)
            return result;

        Core.Logger($"{LogPrefix} {playerAlias} respawned.");

        if (!IsInBossRoom())
            Core.Jump(BossCell, BossPad);

        Duck.MaintainTarget(DageMapId);
        recovered = true;
        return FightResult.Continue;
    }

    private void AdvanceDecayDetections(ref int nextDetection)
    {
        while (Duck.HasPacketDetection(nextDetection))
            nextDetection++;
    }

    private FightResult GetFightResult(
        int fightAttempt,
        ref bool bossObserved
    )
    {
        if (Duck.IsMonsterAlive(DageMapId))
            bossObserved = true;
        else if (bossObserved)
            return FightResult.Defeated;

        return Duck.ShouldResetFight(fightAttempt)
            ? FightResult.Reset
            : FightResult.Continue;
    }

    private void StartSkillEngine(ClassPreset preset)
    {
        bool isVDK = string.Equals(preset.ClassName, "Verus DoomKnight", StringComparison.OrdinalIgnoreCase);

        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            isTaunter,
            LogPrefix,
            preset.SkillMode,
            useSurvivalSkill: true,
            maintainedPotion: null,
            blockedStrictSkill: isVDK ? 2 : 0,
            blockedStrictSkillSelfAura: isVDK ? UnleashedDoomAura : string.Empty
        );
    }

    private void StartZoneListener()
    {
        lock (zoneLock)
            pendingZones.Clear();

        Bot.Events.RunToArea -= OnRunToArea;
        Bot.Events.RunToArea += OnRunToArea;
    }

    private void StopZoneListener()
    {
        Bot.Events.RunToArea -= OnRunToArea;

        lock (zoneLock)
            pendingZones.Clear();
    }

    private void OnRunToArea(string zone)
    {
        if (zone != "A" && zone != "B" && zone.Length != 0)
            return;

        lock (zoneLock)
            pendingZones.Enqueue(zone);
    }

    private void DrainZoneEvents(bool move)
    {
        while (true)
        {
            string zone;
            lock (zoneLock)
            {
                if (pendingZones.Count == 0)
                    return;

                zone = pendingZones.Dequeue();
            }

            if (!move || !Bot.Player.Alive)
                continue;

            if (zone == "A")
                Bot.Player.WalkTo(122, 420);
            else if (zone == "B")
                Bot.Player.WalkTo(856, 420);
            else
                Bot.Player.WalkTo(500, 420);
        }
    }

    private void StopAttemptSystems()
    {
        Duck.StopSkillEngine();
        Duck.StopPacketDetector();
        StopZoneListener();
    }

    private bool HandleFightReset(int fightAttempt)
    {
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
                && string.Equals(
                    player.Cell,
                    BossCell,
                    StringComparison.OrdinalIgnoreCase
                );
            return true;
        }

        return false;
    }

    private DateTimeOffset GetFocusExpiry() =>
        Bot.Target.GetAura(FocusAura)?.ExpiresAt ?? DateTimeOffset.MinValue;

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

    private static string GetTauntSignal(int attempt, int signalNumber) =>
        $"DAGE_TAUNT_{attempt}_{signalNumber}";

    private static string GetMystifySignal(int attempt) =>
        $"DAGE_MYSTIFY_{attempt}";

    private static string GetAlternatingDecaySignal(int attempt) =>
        $"DAGE_ALTERNATING_DECAY_{attempt}";

    private bool IsInBossRoom() =>
        Bot.Player.Cell == BossCell && Bot.Player.Pad == BossPad;

    private bool IsInSafeRoom() =>
        Bot.Player.Cell == SafeCell && Bot.Player.Pad == SafePad;

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
