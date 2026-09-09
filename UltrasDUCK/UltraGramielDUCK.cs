/*
name: Ultra Gramiel DUCK
description: Four-player CoreDUCK Ultra Gramiel script.
tags: ultra, gramiel, weekly, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include DUCKScripts\UltrasDUCK\CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraGramielDUCK
{

    private enum PhaseResult
    {
        Completed,
        Reset,
        Stopped,
    }

    private enum PhaseTwoDetectorState
    {
        Attacks,
        Liberator,
        ChargeTwo,
        ChargeAttackTwo,
        WaitingForInvulnerableEnd,
        Finished,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "UltraGramielDUCK";
    private const string SyncFileName = "UltraGramielDUCK.sync";
    private const string MapName = "ultragramiel";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string FightCell = "r2";
    private const string FightPad = "Down";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string PacketCommand = "ct";
    private const string CrystalChargeMessage =
        "The Grace Crystal prepares a defense shattering attack!";
    private const string ChargeAnimationMarker = "\"animStr\":\"Charge\"";
    private const string CrystalACasterMarker = "\"cInf\":\"m:2\"";
    private const string SafeguardAuraMarker = "\"nam\":\"Safeguard\"";
    private const string GraceGivenAuraMarker = "\"nam\":\"Grace Given\"";
    private const string GramielCasterMarker = "\"cInf\":\"m:1\"";
    private const string AttackAnimationMarker = "\"animStr\":\"Attack";
    private const string ChargeTwoAnimationMarker = "\"animStr\":\"Charge2\"";
    private const string ChargeAttackTwoAnimationMarker =
        "\"animStr\":\"ChargeAttack2\"";
    private const string LiberatorMarker = "Liberator";
    private const string InvulnerableAura = "Invulnerable";
    private const int UltraQuestId = 10301;
    private const int PrerequisiteQuestId = 9986;
    private const string PrerequisiteQuestName = "Isa, Reversed - Realized";
    private const int MinimumLevel = 80;
    private const int GramielMapId = 1;
    private const int CrystalAMapId = 2;
    private const int CrystalBMapId = 3;
    private const int BalanceStartDifference = 20;
    private const int BalanceStopDifference = 2;
    private const int TauntTargetHold = 1500;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DefaultPrivateRoomNumber = 1245;
    private const int GramielNukeCount = 3;

    private int dpsPlayerNumber = 1;
    private int scPlayerNumber = 2;
    private int apPlayerNumber = 3;
    private int looPlayerNumber = 4;
    private bool isArchPaladin;
    private bool isLordOfOrder;
    private bool isStoneCrusher;
    private bool isDPS;
    private bool isShaman;
    private bool isVDK;
    private DuckAssignmentResult? currentAssignResult;

    private string playerAlias = string.Empty;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraGramielDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] GramielSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight", "Legion Revenant", "Shaman" } },
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
            Duck.StopPacketDetector();
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
            Duck.StopPacketDetector();
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
            GramielSlots,
            armySize: 4
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        currentAssignResult = assignResult;
        ClassPreset preset = assignResult.Preset;
        ApplyGramielOverrides(preset, assignResult.RoleName);
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";

        isArchPaladin = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        isLordOfOrder = string.Equals(preset.ClassName, "Lord of Order", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(preset.ClassName, "LordOfOrder", StringComparison.OrdinalIgnoreCase);
        isStoneCrusher = string.Equals(preset.ClassName, "StoneCrusher", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(preset.ClassName, "Infinity Titan", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(preset.ClassName, "InfinityTitan", StringComparison.OrdinalIgnoreCase);
        isShaman = string.Equals(preset.ClassName, "Shaman", StringComparison.OrdinalIgnoreCase);
        isVDK = string.Equals(preset.ClassName, "Verus DoomKnight", StringComparison.OrdinalIgnoreCase);
        isDPS = !isArchPaladin && !isLordOfOrder && !isStoneCrusher;

        ResolveRolePlayerNumbers(assignResult);

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

    private void ApplyGramielOverrides(ClassPreset preset, string roleName)
    {
        if (string.Equals(roleName, "Shaman", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset.ClassName, "Shaman", StringComparison.OrdinalIgnoreCase))
        {
            preset.BaseEnhancement = EnhancementType.Wizard;
            preset.WeaponEnhancement = WeaponSpecial.Elysium;
            preset.HelmEnhancement = HelmSpecial.None;
            preset.CapeEnhancement = CapeSpecial.Absolution;
            preset.Tonic = "Sage Tonic";
            preset.Elixir = "Potent Malevolence Elixir";
        }
        else if (string.Equals(roleName, "Legion Revenant", StringComparison.OrdinalIgnoreCase)
              || string.Equals(preset.ClassName, "Legion Revenant", StringComparison.OrdinalIgnoreCase))
        {
            preset.Skills = new[] { 3, 4, 2 };
            preset.CapeEnhancement = CapeSpecial.Penitence;
            preset.HelmEnhancement = HelmSpecial.None;
        }
        else if (string.Equals(roleName, "ArchPaladin", StringComparison.OrdinalIgnoreCase)
              || string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase))
        {
            preset.CapeEnhancement = CapeSpecial.Penitence;
        }
        else if (string.Equals(roleName, "Lord of Order", StringComparison.OrdinalIgnoreCase)
              || string.Equals(roleName, "LordOfOrder", StringComparison.OrdinalIgnoreCase)
              || string.Equals(preset.ClassName, "Lord of Order", StringComparison.OrdinalIgnoreCase)
              || string.Equals(preset.ClassName, "LordOfOrder", StringComparison.OrdinalIgnoreCase))
        {
            preset.WeaponEnhancement = WeaponSpecial.Valiance;
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

            if (
                !PrepareSafeRoom(preset)
                || !StartCrystalPacketDetector()
            )
                return false;

            bool usePhaseTwoSlowdown = ShouldUsePhaseTwoSlowdown();
            if (!usePhaseTwoSlowdown && IsPhaseTwoSlowdownOwner())
                Core.Logger(
                    $"{LogPrefix} {playerAlias} disabled Phase 2 slowdown because the equipped weapon is below 40% damage boost."
                );

            if (!Sync("FIGHT_READY"))
                return false;

            Core.Jump(FightCell, FightPad);
            Duck.FileLog($"{playerAlias} jumped to {FightCell} for attempt {fightAttempt}.", LogPrefix);

            PhaseResult result = FightPhaseOne(preset, fightAttempt);
            if (result == PhaseResult.Completed)
            {
                result = FightPhaseTwo(
                    preset,
                    fightAttempt,
                    usePhaseTwoSlowdown
                );
                if (result == PhaseResult.Completed)
                {
                    Core.Jump(SafeCell, SafePad);
                    bool CreditCheck() =>
                        Bot.Quests.CanComplete(UltraQuestId) || Bot.Quests.IsDailyComplete(UltraQuestId);

                    if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, 4, LogPrefix))
                    {
                        Duck.FileLog($"{playerAlias} confirmed Ultra Gramiel defeated on attempt {fightAttempt}.", LogPrefix);
                        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Gramiel defeated.");
                        return true;
                    }

                    Core.Logger($"{LogPrefix} One or more players missed kill credit on attempt {fightAttempt}. Retrying fight with entire army...");
                    result = PhaseResult.Reset;
                }
            }

            if (
                result != PhaseResult.Reset
                || !HandleFightReset(fightAttempt)
            )
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
            warnForElysiumUnlock: isShaman,
            weaponFallbacks: preset.WeaponEnhancementFallbacks
        );

        Duck.PreparePotions(
            preset.Tonic,
            preset.Elixir,
            preset.CombatPotion
        );

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

        Duck.EquipScroll(EnrageScroll);
        Duck.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private bool StartCrystalPacketDetector()
    {
        if (
            Duck.StartPacketDetector(
                PacketCommand,
                new[]
                {
                    CrystalChargeMessage,
                    ChargeAnimationMarker,
                    CrystalACasterMarker,
                }
            )
        )
            return true;

        Core.Logger(
            "The crystal packet detector could not be started.",
            "RunFightAttempts",
            messageBox: !masterMode,
            stopBot: !masterMode
        );
        return false;
    }

    private PhaseResult FightPhaseOne(ClassPreset preset, int fightAttempt)
    {
        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            true,
            LogPrefix,
            isShaman
                ? SkillEngineMode.Simple
                : preset.SkillMode,
            blockedStrictSkill: isVDK ? 4 : 0,
            blockedStrictSkillTargetAura: isVDK
                ? "Safeguard"
                : string.Empty
        );
        Duck.FileLog($"{playerAlias} started Phase 1.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} started Phase 1.");

        int nextDetection = 1;
        int emergencyTarget = 0;
        bool crystalBalancing = false;
        bool crystalsObserved = false;
        bool safeguardActive = false;
        DateTimeOffset tauntTargetUntil = DateTimeOffset.MinValue;
        bool shamanOpeningSkillFourQueued = false;
        bool shamanSafeguardSkillFourQueued = false;

        while (!Bot.ShouldExit)
        {
            int crystalAHealth = Duck.GetMonsterHP(CrystalAMapId);
            int crystalBHealth = Duck.GetMonsterHP(CrystalBMapId);

            if (Duck.ShouldResetFight(fightAttempt))
                return FinishPhaseOne(PhaseResult.Reset);

            if (!Bot.Player.Alive)
            {
                Duck.FileLog($"{playerAlias} died in Phase 1 (CrystalA HP: {crystalAHealth}, CrystalB HP: {crystalBHealth}).", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} died.");
                crystalBalancing = false;
                safeguardActive = false;
                tauntTargetUntil = DateTimeOffset.MinValue;

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    if (Duck.ShouldResetFight(fightAttempt))
                        return FinishPhaseOne(PhaseResult.Reset);

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                crystalAHealth = Duck.GetMonsterHP(CrystalAMapId);
                crystalBHealth = Duck.GetMonsterHP(CrystalBMapId);

                if (Duck.ShouldResetFight(fightAttempt))
                    return FinishPhaseOne(PhaseResult.Reset);

                Duck.FileLog($"{playerAlias} respawned in Phase 1.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (!IsInFightRoom())
                    Core.Jump(FightCell, FightPad);

                AdvanceDetections(ref nextDetection);
                continue;
            }

            if (!crystalsObserved)
            {
                if (crystalAHealth > 0 && crystalBHealth > 0)
                {
                    crystalsObserved = true;
                    Core.Logger($"{LogPrefix} {playerAlias} observed both Grace Crystals.");
                }
                else
                {
                    Bot.Sleep(FightPollDelay);
                    continue;
                }
            }

            if (crystalAHealth <= 0 && crystalBHealth <= 0)
                break;

            bool bothCrystalsAlive = crystalAHealth > 0
                && crystalBHealth > 0;

            HandleCrystalDetections(
                bothCrystalsAlive,
                ref nextDetection,
                ref tauntTargetUntil
            );

            bool shamanSkillFourPending = isShaman
                && Duck.HasPendingTargetedPrioritySkill();
            if (!shamanSkillFourPending)
            {
                MaintainPhaseOneTarget(
                    crystalAHealth,
                    crystalBHealth,
                    ref crystalBalancing,
                    ref safeguardActive,
                    tauntTargetUntil,
                    ref emergencyTarget
                );
            }

            MaintainPhaseOneShamanSkillFour(
                bothCrystalsAlive,
                safeguardActive,
                tauntTargetUntil,
                ref shamanOpeningSkillFourQueued,
                ref shamanSafeguardSkillFourQueued
            );

            Bot.Sleep(FightPollDelay);
        }

        if (Bot.ShouldExit)
            return FinishPhaseOne(PhaseResult.Stopped);

        Duck.FileLog($"{playerAlias} confirmed both Grace Crystals defeated. Transitioning to Phase 2.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} confirmed both Grace Crystals defeated.");
        return FinishPhaseOne(PhaseResult.Completed);
    }

    private PhaseResult FightPhaseTwo(
        ClassPreset preset,
        int fightAttempt,
        bool usePhaseTwoSlowdown
    )
    {
        int[] normalSkills = GetPhaseTwoSkills(preset);
        Duck.StartSkillEngine(
            normalSkills,
            playerAlias,
            true,
            LogPrefix,
            preset.SkillMode
        );

        Duck.FileLog($"{playerAlias} started Phase 2.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} started Phase 2.");

        bool graceGivenObserved = false;
        bool gramielObserved = false;
        int nukeCycle = 1;
        int nextAttack = 1;
        int ownedAttack = GetPhaseTwoTauntAttack();
        PhaseTwoDetectorState detectorState = PhaseTwoDetectorState.Attacks;
        bool rotationRestricted = false;
        bool damageHold = false;
        bool shamanInvulnerableSkillThreeBlocked = false;

        while (!Bot.ShouldExit)
        {
            if (Duck.ShouldResetFight(fightAttempt))
                return FinishPhaseTwo(PhaseResult.Reset);

            if (!Bot.Player.Alive)
            {
                Duck.FileLog($"{playerAlias} died during Phase 2 (Gramiel HP: {Duck.GetMonsterHP(GramielMapId)}).", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} died during Phase 2.");

                while (!Bot.ShouldExit && !Bot.Player.Alive)
                {
                    if (Duck.ShouldResetFight(fightAttempt))
                        return FinishPhaseTwo(PhaseResult.Reset);

                    Bot.Sleep(RespawnPollDelay);
                }

                if (Bot.ShouldExit)
                    break;

                if (Duck.ShouldResetFight(fightAttempt))
                    return FinishPhaseTwo(PhaseResult.Reset);

                Duck.FileLog($"{playerAlias} respawned during Phase 2.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} respawned during Phase 2.");

                if (!IsInFightRoom())
                    Core.Jump(FightCell, FightPad);
            }

            int gramielHealth = Duck.GetMonsterHP(GramielMapId);
            if (gramielHealth > 0)
                gramielObserved = true;
            else if (gramielObserved)
            {
                Duck.FileLog($"{playerAlias} confirmed Gramiel defeated.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} confirmed Gramiel defeated.");
                return FinishPhaseTwo(PhaseResult.Completed);
            }

            if (damageHold)
            {
                if (
                    !Bot.Player.HasTarget
                    || Bot.Player.Target?.MapID != GramielMapId
                    || Bot.Player.Target?.HP <= 0
                )
                    Duck.MaintainTarget(GramielMapId);

                Bot.Combat.CancelAutoAttack();
            }
            else
                Duck.MaintainTarget(GramielMapId);

            if (isShaman)
            {
                bool gramielInvulnerable = Bot.Target
                    .GetMonsterAura(GramielMapId)
                    .Contains(
                        $"\"nam\":\"{InvulnerableAura}\"",
                        StringComparison.Ordinal
                    );

                if (
                    gramielInvulnerable
                    && !shamanInvulnerableSkillThreeBlocked
                )
                {
                    Duck.SetShamanSkillThreeEnabled(false);
                    shamanInvulnerableSkillThreeBlocked = true;
                    Core.Logger($"{LogPrefix} {playerAlias} disabled skill 3 while Gramiel has Invulnerable.");
                }
                else if (
                    !gramielInvulnerable
                    && shamanInvulnerableSkillThreeBlocked
                )
                {
                    Duck.SetShamanSkillThreeEnabled(true);
                    shamanInvulnerableSkillThreeBlocked = false;
                    Core.Logger($"{LogPrefix} {playerAlias} restored skill 3 after Gramiel's Invulnerable ended.");
                }
            }

            if (!graceGivenObserved)
            {
                if (
                    !Bot.Target
                        .GetMonsterAura(GramielMapId)
                        .Contains(
                            GraceGivenAuraMarker,
                            StringComparison.Ordinal
                        )
                )
                {
                    Bot.Sleep(FightPollDelay);
                    continue;
                }

                graceGivenObserved = true;
                if (!StartPhaseTwoAttackDetector())
                    return FinishPhaseTwo(PhaseResult.Stopped);

                Core.Logger($"{LogPrefix} {playerAlias} detected Grace Given and armed nuke cycle 1.");
            }

            switch (detectorState)
            {
                case PhaseTwoDetectorState.Attacks:
                    while (Duck.HasPacketDetection(nextAttack))
                    {
                        int attack = nextAttack++;

                        if (
                            usePhaseTwoSlowdown
                            && IsLegionRevenantComp()
                            && isArchPaladin
                            && attack == 1
                        )
                        {
                            Duck.SetSkillEngineSkills(new[] { 2 });
                            rotationRestricted = true;
                            Core.Logger($"{LogPrefix} ArchPaladin restricted its rotation to {{2}} on Legion Revenant taunt attack.");
                        }

                        if (attack == ownedAttack)
                        {
                            Duck.MaintainTarget(GramielMapId);
                            RequestGramielTaunt(GramielMapId);
                            Core.Logger($"{LogPrefix} {playerAlias} requested Gramiel taunt on nuke cycle {nukeCycle}, attack {attack}.");

                            if (
                                isDPS
                                && IsLegionRevenantComp()
                                && usePhaseTwoSlowdown
                            )
                            {
                                Duck.SetSkillEngineSkills(new[] { 3 });
                                rotationRestricted = true;
                            }
                        }

                        if (attack != 7)
                            continue;

                        if (rotationRestricted)
                        {
                            Duck.SetSkillEngineSkills(normalSkills);
                            rotationRestricted = false;
                            Core.Logger($"{LogPrefix} {playerAlias} restored its normal rotation on nuke cycle {nukeCycle}, attack 7.");
                        }

                        if (!StartLiberatorPacketDetector())
                            return FinishPhaseTwo(PhaseResult.Stopped);

                        detectorState = PhaseTwoDetectorState.Liberator;
                        Core.Logger($"{LogPrefix} {playerAlias} reached attack 7 and started waiting for Liberator.");

                        break;
                    }
                    break;

                case PhaseTwoDetectorState.Liberator:
                    if (!Duck.HasPacketDetection(1))
                    {
                        if (
                            !damageHold
                            && GetGramielHealthPercentage()
                                <= GetHealingHoldThreshold(nukeCycle)
                        )
                        {
                            if (SuppressPlayerOneDuringHealingHold())
                                Duck.SetOrdinarySkillsSuppressed(true);
                            else
                                Duck.SetSkillEngineSkills(GetHealingHoldSkills());

                            Bot.Combat.CancelAutoAttack();
                            damageHold = true;
                            Core.Logger($"{LogPrefix} {playerAlias} started the nuke cycle {nukeCycle} healing hold at {GetHealingHoldThreshold(nukeCycle)}% Gramiel HP.");
                        }

                        if (damageHold)
                            Bot.Combat.CancelAutoAttack();

                        break;
                    }

                    if (SuppressPlayerOneDuringHealingHold())
                        Duck.SetOrdinarySkillsSuppressed(false);
                    else
                        Duck.SetSkillEngineSkills(normalSkills);

                    damageHold = false;
                    rotationRestricted = false;
                    Duck.MaintainTarget(GramielMapId);
                    Core.Logger($"{LogPrefix} {playerAlias} detected Liberator in a {Duck.GetPacketDetectorCommand()} packet and restored its normal rotation.");

                    if (!StartChargeTwoPacketDetector())
                        return FinishPhaseTwo(PhaseResult.Stopped);

                    detectorState = PhaseTwoDetectorState.ChargeTwo;
                    break;

                case PhaseTwoDetectorState.ChargeTwo:
                    if (!Duck.HasPacketDetection(1))
                        break;

                    LogGramielProtection(nukeCycle);

                    if (!StartChargeAttackTwoPacketDetector())
                        return FinishPhaseTwo(PhaseResult.Stopped);

                    detectorState = PhaseTwoDetectorState.ChargeAttackTwo;
                    break;

                case PhaseTwoDetectorState.ChargeAttackTwo:
                    if (!Duck.HasPacketDetection(1))
                        break;

                    Bot.Sleep(FightPollDelay);
                    Duck.StopPacketDetector();
                    Core.Logger($"{LogPrefix} {playerAlias} handled ChargeAttack2 for nuke cycle {nukeCycle}.");
                    detectorState = PhaseTwoDetectorState.WaitingForInvulnerableEnd;
                    break;

                case PhaseTwoDetectorState.WaitingForInvulnerableEnd:
                    if (Bot.Self.HasActiveAura(InvulnerableAura))
                        break;

                    Core.Logger($"{LogPrefix} {playerAlias} detected Invulnerable ended after nuke cycle {nukeCycle}.");

                    if (nukeCycle >= GramielNukeCount)
                    {
                        Duck.SetSkillEngineSkills(normalSkills);
                        damageHold = false;
                        detectorState = PhaseTwoDetectorState.Finished;
                        Core.Logger($"{LogPrefix} {playerAlias} completed all three Gramiel nuke cycles.");
                        break;
                    }

                    nukeCycle++;
                    nextAttack = 1;
                    Duck.SetSkillEngineSkills(normalSkills);
                    rotationRestricted = false;
                    damageHold = false;

                    if (!StartPhaseTwoAttackDetector())
                        return FinishPhaseTwo(PhaseResult.Stopped);

                    detectorState = PhaseTwoDetectorState.Attacks;
                    Core.Logger($"{LogPrefix} {playerAlias} armed Gramiel nuke cycle {nukeCycle}.");
                    break;

                case PhaseTwoDetectorState.Finished:
                    break;
            }

            Bot.Sleep(FightPollDelay);
        }

        return FinishPhaseTwo(PhaseResult.Stopped);
    }

    private bool StartPhaseTwoAttackDetector() =>
        StartPhaseTwoPacketDetector(
            new[] { PacketCommand },
            new[] { GramielCasterMarker, AttackAnimationMarker },
            "Attack2/Attack3"
        );

    private bool StartLiberatorPacketDetector() =>
        StartPhaseTwoPacketDetector(
            new[] { PacketCommand },
            new[] { LiberatorMarker },
            "Liberator"
        );

    private bool StartChargeTwoPacketDetector() =>
        StartPhaseTwoPacketDetector(
            new[] { PacketCommand },
            new[] { GramielCasterMarker, ChargeTwoAnimationMarker },
            "Charge2"
        );

    private bool StartChargeAttackTwoPacketDetector() =>
        StartPhaseTwoPacketDetector(
            new[] { PacketCommand },
            new[] { GramielCasterMarker, ChargeAttackTwoAnimationMarker },
            "ChargeAttack2"
        );

    private bool StartPhaseTwoPacketDetector(
        string[] commands,
        string[] markers,
        string detectorName
    )
    {
        if (Duck.StartPacketDetector(commands, markers))
            return true;

        Core.Logger(
            $"The Phase 2 {detectorName} packet detector could not be started.",
            "FightPhaseTwo",
            messageBox: !masterMode,
            stopBot: !masterMode
        );
        return false;
    }

    private void LogGramielProtection(int nukeCycle)
    {
        Core.Logger(
            Bot.Self.HasActiveAura(InvulnerableAura)
                ? $"{LogPrefix} {playerAlias} entered nuke cycle {nukeCycle} protected by Invulnerable."
                : $"{LogPrefix} {playerAlias} entered nuke cycle {nukeCycle} without Invulnerable."
        );
    }

    private int[] GetPhaseTwoSkills(ClassPreset preset) =>
        isDPS && string.Equals(preset.ClassName, "Legion Revenant", StringComparison.OrdinalIgnoreCase)
            ? new[] { 3, 4, 2, 1 }
            : preset.Skills;

    private int[] GetHealingHoldSkills() =>
        isDPS || isStoneCrusher
            ? new[] { 3 }
            : new[] { 2 };

    private float GetGramielHealthPercentage()
    {
        var monsters = Bot.Monsters?.MapMonsters;
        if (monsters == null)
            return 100f;

        foreach (var monster in monsters)
        {
            if (
                monster != null
                && monster.MapID == GramielMapId
                && monster.MaxHP > 0
            )
                return monster.HP * 100f / monster.MaxHP;
        }

        return 100f;
    }

    private int GetHealingHoldThreshold(int nukeCycle) =>
        nukeCycle switch
        {
            1 => 75,
            2 => 45,
            _ => 15,
        };

    private bool ShouldUsePhaseTwoSlowdown()
    {
        foreach (var item in Bot.Inventory.Items)
        {
            if (
                item.Equipped
                && !Core.NoneEnhancableFilter(item)
            )
                return Core.GetBoostFloat(item, "dmgAll") >= 1.40f;
        }

        return false;
    }

    private bool IsLegionRevenantComp() =>
        currentAssignResult?.PlayerNumberToClass != null
        && currentAssignResult.PlayerNumberToClass.Values.Any(c => string.Equals(c, "Legion Revenant", StringComparison.OrdinalIgnoreCase));

    private bool IsVerusDoomKnightComp() =>
        currentAssignResult?.PlayerNumberToClass != null
        && currentAssignResult.PlayerNumberToClass.Values.Any(c => string.Equals(c, "Verus DoomKnight", StringComparison.OrdinalIgnoreCase));

    private bool IsPhaseTwoSlowdownOwner() =>
        IsLegionRevenantComp() && (isDPS || isArchPaladin);

    private int GetPhaseTwoTauntAttack()
    {
        if (isDPS)
            return 1;

        if (isStoneCrusher)
            return 3;

        if (isArchPaladin)
            return 5;

        return 7;
    }

    private PhaseResult FinishPhaseTwo(PhaseResult result)
    {
        Duck.StopPacketDetector();
        Duck.StopSkillEngine();
        return Bot.ShouldExit ? PhaseResult.Stopped : result;
    }

    private void HandleCrystalDetections(
        bool bothCrystalsAlive,
        ref int nextDetection,
        ref DateTimeOffset tauntTargetUntil
    )
    {
        while (Duck.HasPacketDetection(nextDetection))
        {
            int cycle = nextDetection++;
            if (!bothCrystalsAlive || !OwnsTauntCycle(cycle))
                continue;

            int crystalMapId = GetAssignedCrystalMapId();
            Duck.MaintainTarget(crystalMapId);
            RequestGramielTaunt(crystalMapId);

            if (
                isStoneCrusher
                || isArchPaladin
                || isLordOfOrder
                || isShaman
                || isVDK
            )
                tauntTargetUntil = DateTimeOffset.Now.AddMilliseconds(
                    TauntTargetHold
                );

            Duck.FileLog($"{playerAlias} requested crystal taunt cycle {cycle} on MapID {crystalMapId}.", LogPrefix);
            Core.Logger($"{LogPrefix} {playerAlias} requested crystal taunt cycle {cycle} on MapID {crystalMapId}.");
        }
    }

    private void MaintainPhaseOneShamanSkillFour(
        bool bothCrystalsAlive,
        bool safeguardActive,
        DateTimeOffset tauntTargetUntil,
        ref bool openingSkillFourQueued,
        ref bool safeguardSkillFourQueued
    )
    {
        if (!isShaman)
            return;

        if (!safeguardActive)
            safeguardSkillFourQueued = false;

        if (
            !bothCrystalsAlive
            || DateTimeOffset.Now < tauntTargetUntil
            || Duck.HasPendingTargetedPrioritySkill()
            || !Bot.Skills.CanUseSkill(4)
        )
            return;

        bool openingCast = !openingSkillFourQueued;
        bool safeguardCast = openingSkillFourQueued
            && safeguardActive
            && !safeguardSkillFourQueued;
        if (!openingCast && !safeguardCast)
            return;

        if (
            !Duck.RequestTargetedPrioritySkill(
                4,
                GramielMapId,
                GramielMapId
            )
        )
            return;

        if (openingCast)
            openingSkillFourQueued = true;
        else
            safeguardSkillFourQueued = true;

        Core.Logger(
            openingCast
                ? $"{LogPrefix} playerOne queued opening Phase 1 skill 4 on Gramiel."
                : $"{LogPrefix} playerOne queued Safeguard Phase 1 skill 4 on Gramiel."
        );
    }

    private void MaintainPhaseOneTarget(
        int crystalAHealth,
        int crystalBHealth,
        ref bool crystalBalancing,
        ref bool safeguardActive,
        DateTimeOffset tauntTargetUntil,
        ref int emergencyTarget
    )
    {
        if (crystalAHealth <= 0 || crystalBHealth <= 0)
        {
            int survivingCrystal = crystalAHealth > 0
                ? CrystalAMapId
                : CrystalBMapId;

            if (emergencyTarget != survivingCrystal)
            {
                emergencyTarget = survivingCrystal;
                Core.Logger($"{LogPrefix} {playerAlias} focusing surviving Crystal MapID {survivingCrystal}.");
            }

            crystalBalancing = false;
            safeguardActive = false;
            Duck.MaintainTarget(survivingCrystal);
            return;
        }

        emergencyTarget = 0;
        int assignedCrystal = GetAssignedCrystalMapId();

        bool isLR = IsLegionRevenantComp();
        bool safeguardAttacker = isLR
            ? isStoneCrusher || isArchPaladin
            : true;

        if (safeguardAttacker || isShaman)
        {
            bool currentSafeguard = Bot.Target
                .GetMonsterAura(GramielMapId)
                .Contains(
                    SafeguardAuraMarker,
                    StringComparison.Ordinal
                );

            if (currentSafeguard != safeguardActive)
            {
                safeguardActive = currentSafeguard;
                Core.Logger(
                    currentSafeguard
                        ? $"{LogPrefix} {playerAlias} detected Safeguard on Gramiel."
                        : $"{LogPrefix} {playerAlias} detected Safeguard ended."
                );
            }

            if (DateTimeOffset.Now < tauntTargetUntil || (safeguardAttacker && currentSafeguard))
            {
                Duck.MaintainTarget(
                    DateTimeOffset.Now < tauntTargetUntil
                        ? assignedCrystal
                        : GramielMapId
                );
                return;
            }
        }

        bool crystalBalancer = IsVerusDoomKnightComp()
            ? isStoneCrusher || isLordOfOrder
            : isLR
                ? isLordOfOrder
                : isShaman || isStoneCrusher;

        if (!crystalBalancer)
        {
            Duck.MaintainTarget(assignedCrystal);
            return;
        }

        if (DateTimeOffset.Now < tauntTargetUntil)
        {
            Duck.MaintainTarget(assignedCrystal);
            return;
        }

        int difference = Math.Abs(crystalAHealth - crystalBHealth);
        if (!crystalBalancing && difference > BalanceStartDifference)
        {
            crystalBalancing = true;
            Duck.FileLog($"{playerAlias} started crystal balancing at {crystalAHealth}/{crystalBHealth} HP.", LogPrefix);
            Core.Logger($"{LogPrefix} {playerAlias} started crystal balancing at {crystalAHealth}/{crystalBHealth} HP.");
        }

        if (!crystalBalancing)
        {
            Duck.MaintainTarget(assignedCrystal);
            return;
        }

        if (difference <= BalanceStopDifference)
        {
            crystalBalancing = false;
            Duck.FileLog($"{playerAlias} finished crystal balancing at {crystalAHealth}/{crystalBHealth} HP.", LogPrefix);
            Core.Logger($"{LogPrefix} {playerAlias} finished crystal balancing at {crystalAHealth}/{crystalBHealth} HP.");
            Duck.MaintainTarget(assignedCrystal);
            return;
        }

        Duck.MaintainTarget(
            crystalAHealth >= crystalBHealth
                ? CrystalAMapId
                : CrystalBMapId
        );
    }

    private bool OwnsTauntCycle(int cycle)
    {
        bool openingOwner = isDPS || isArchPaladin;
        return openingOwner ? cycle % 2 == 1 : cycle % 2 == 0;
    }

    private int GetAssignedCrystalMapId() =>
        isDPS || isStoneCrusher
            ? CrystalAMapId
            : CrystalBMapId;

    private void AdvanceDetections(ref int nextDetection)
    {
        while (Duck.HasPacketDetection(nextDetection))
            nextDetection++;
    }

    private PhaseResult FinishPhaseOne(PhaseResult result)
    {
        Duck.StopPacketDetector();
        Duck.StopSkillEngine();
        return Bot.ShouldExit ? PhaseResult.Stopped : result;
    }

    private bool HandleFightReset(int fightAttempt)
    {
        Duck.FileLog($"{playerAlias} executing fight reset for attempt {fightAttempt}.", LogPrefix);
        Duck.StopPacketDetector();
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
            $"{LogPrefix} fight failed after {MaxFightAttempts} attempts.",
            "RunFightAttempts",
            messageBox: !masterMode,
            stopBot: !masterMode
        );
    }

    private bool IsInSafeRoom() =>
        string.Equals(Bot.Player.Cell, SafeCell, StringComparison.OrdinalIgnoreCase);

    private bool IsInFightRoom() =>
        string.Equals(Bot.Player.Cell, FightCell, StringComparison.OrdinalIgnoreCase);

    private bool SuppressPlayerOneDuringHealingHold() =>
        isShaman || isVDK;

    private void RequestGramielTaunt(int mapId) =>
        Duck.RequestAbsolutePriorityTaunt(mapId);

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

