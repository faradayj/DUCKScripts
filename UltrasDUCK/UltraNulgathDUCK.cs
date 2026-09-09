/*
name: Ultra Nulgath LW
description: Four-player CoreDUCK Army script for Ultra Nulgath.
tags: ultra, nulgath, weekly, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/Customv2/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraNulgathDUCK
{
    public enum ArmyComposition
    {
        Default,
        Stable,
        Reliable,
        Optimized,
        Pay2Win,
        Fast,
        Test,
    }

    private enum FightResult
    {
        Defeated,
        Reset,
        Stopped,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "UltraNulgathDUCK";
    private const string SyncFileName = "UltraNulgathDUCK.sync";
    private const string MapName = "ultranulgath";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Boss";
    private const string BossPad = "Right";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string PacketCommand = "ct";
    private const string AbyssPacketText = "Abyss!";
    private const int UltraQuestId = 8692;
    private const int PrerequisiteQuestId = 0;
    private const string PrerequisiteQuestName = "";
    private const int MinimumLevel = 80;
    private const int BladeMapId = 1;
    private const int NulgathMapId = 2;
    private const int BladeHealthThreshold = 750_000;
    private const int TaunterOneTauntDelay = 5000;
    private const int PlayerFourTauntDelay = 2000;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DefaultPrivateRoomNumber = 1245;

    private string playerAlias = string.Empty;
    private ArmyComposition armyComposition;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;
    private bool isTaunter;
    private bool isTaunterOne;
    private bool isAbyssDetector;
    private bool isStoneCrusher;
    private bool isArchPaladin;
    private bool isLordOfOrder;
    private bool isVDK;
    private bool isKingsEcho;
    private int privateRoomNumber = DefaultPrivateRoomNumber;

    public string OptionsStorage = "UltraNulgathDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] NulgathSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight", "King's Echo", "Legion Revenant", "Arcana Invoker", "Void Highlord" } },
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
            NulgathSlots,
            armySize: 4
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        ClassPreset preset = assignResult.Preset;
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";

        isArchPaladin = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        isLordOfOrder = string.Equals(preset.ClassName, "Lord of Order", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(preset.ClassName, "Lord Of Order", StringComparison.OrdinalIgnoreCase);
        isStoneCrusher = string.Equals(preset.ClassName, "StoneCrusher", StringComparison.OrdinalIgnoreCase);
        isVDK = string.Equals(preset.ClassName, "Verus DoomKnight", StringComparison.OrdinalIgnoreCase);
        isKingsEcho = string.Equals(preset.ClassName, "King's Echo", StringComparison.OrdinalIgnoreCase);

        // ArchPaladin is Taunter 1 (opening taunt @ T+5s and Abyss response taunt @ T+5s)
        // Lord of Order runs packet detection and handles the first Abyss taunt (@ T+2s)
        isTaunterOne = isArchPaladin || (assignResult.PlayerNumber == 1 && !isLordOfOrder && !isStoneCrusher);
        isAbyssDetector = isLordOfOrder;
        isTaunter = isTaunterOne || isAbyssDetector;

        if (isTaunter)
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

    private bool RunFightAttempts(ClassPreset preset)
    {
        for (
            int fightAttempt = 1;
            fightAttempt <= MaxFightAttempts && !Bot.ShouldExit;
            fightAttempt++
        )
        {
            Duck.JoinRoom(MapName, privateRoomNumber, SafeCell, SafePad);

            if (!PrepareSafeRoom(preset))
                return false;

            if (
                isAbyssDetector
                && !Duck.StartPacketDetector(PacketCommand, AbyssPacketText)
            )
            {
                Core.Logger(
                    "The Abyss packet detector could not be started.",
                    "RunFightAttempts",
                    messageBox: true,
                    stopBot: true
                );
                return false;
            }

            if (!Sync("FIGHT_READY"))
                return false;

            Core.Jump(BossCell, BossPad);

            if (Bot.ShouldExit || !Sync("START_FIGHT"))
                return false;

            FightResult result = Fight(preset, fightAttempt);
            Duck.StopPacketDetector();

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
        if (true)
            Duck.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (isTaunter)
            Duck.EquipScroll(EnrageScroll);

        Duck.GenericPrebuff();
        return !Bot.ShouldExit;
    }

    private FightResult Fight(ClassPreset preset, int fightAttempt)
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
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        bool tauntScheduled = isTaunterOne;
        bool openingTaunt = isTaunterOne;
        int abyssCycle = 1;
        DateTimeOffset tauntAt = isTaunterOne
            ? DateTimeOffset.Now.AddMilliseconds(TaunterOneTauntDelay)
            : DateTimeOffset.MinValue;

        while (!Bot.ShouldExit)
        {
            if (!Duck.IsMonsterAlive(NulgathMapId))
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

                if (!Duck.IsMonsterAlive(NulgathMapId))
                    break;

                if (Duck.ShouldResetFight(fightAttempt))
                {
                    Duck.StopSkillEngine();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                    Core.Jump(BossCell, BossPad);

                continue;
            }

            Duck.MaintainTarget(GetTargetMapId());
            DateTimeOffset now = DateTimeOffset.Now;

            if (isTaunterOne)
                RunTaunterOneTaunts(fightAttempt, ref abyssCycle, ref tauntScheduled, ref openingTaunt, ref tauntAt, now);
            else if (isAbyssDetector && !RunAbyssDetectorTaunts(fightAttempt, ref abyssCycle, ref tauntScheduled, ref tauntAt, now))
            {
                Duck.StopSkillEngine();
                return FightResult.Stopped;
            }

            Bot.Sleep(FightPollDelay);
        }

        Duck.StopSkillEngine();

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Nulgath defeated.");
        return FightResult.Defeated;
    }

    private void RunTaunterOneTaunts(
        int fightAttempt,
        ref int abyssCycle,
        ref bool tauntScheduled,
        ref bool openingTaunt,
        ref DateTimeOffset tauntAt,
        DateTimeOffset now
    )
    {
        if (tauntScheduled)
        {
            if (now < tauntAt)
                return;

            RequestNulgathTaunt();

            if (openingTaunt)
            {
                openingTaunt = false;
                Core.Logger($"{LogPrefix} {playerAlias} requested the opening Nulgath taunt.");
            }
            else
            {
                Core.Logger($"{LogPrefix} {playerAlias} requested Abyss response taunt {abyssCycle}.");
                abyssCycle++;
            }

            tauntScheduled = false;
            return;
        }

        string signal = GetAbyssTauntSignalName(fightAttempt, abyssCycle);
        if (!Duck.HasArmySignal(signal, 4))
            return;

        tauntAt = now.AddMilliseconds(TaunterOneTauntDelay);
        tauntScheduled = true;
        Core.Logger($"{LogPrefix} {playerAlias} received {signal}.");
    }

    private bool RunAbyssDetectorTaunts(
        int fightAttempt,
        ref int abyssCycle,
        ref bool tauntScheduled,
        ref DateTimeOffset tauntAt,
        DateTimeOffset now
    )
    {
        if (!tauntScheduled)
        {
            if (!Duck.HasPacketDetection(abyssCycle))
                return true;

            tauntAt = now.AddMilliseconds(PlayerFourTauntDelay);
            tauntScheduled = true;
            Core.Logger($"{LogPrefix} {playerAlias} detected Abyss cycle {abyssCycle}.");
            return true;
        }

        if (now < tauntAt)
            return true;

        RequestNulgathTaunt();
        string signal = GetAbyssTauntSignalName(fightAttempt, abyssCycle);

        if (!Duck.SendArmySignal(signal))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} requested Abyss taunt and sent {signal}.");
        abyssCycle++;
        tauntScheduled = false;
        return true;
    }

    private int GetTargetMapId()
    {
        if (isStoneCrusher)
        {
            int bladeHealth = Duck.GetMonsterHP(BladeMapId);
            return Duck.IsMonsterAlive(BladeMapId)
                && bladeHealth > BladeHealthThreshold
                    ? BladeMapId
                    : NulgathMapId;
        }

        if (isVDK)
        {
            return Duck.IsMonsterAlive(BladeMapId)
                ? BladeMapId
                : NulgathMapId;
        }

        if (isKingsEcho)
        {
            return Duck.GetMonsterHP(BladeMapId) > 500_000
                ? BladeMapId
                : NulgathMapId;
        }

        return NulgathMapId;
    }

    private void RequestNulgathTaunt()
    {
        if (armyComposition == ArmyComposition.Test)
            Duck.RequestAbsolutePriorityTaunt(NulgathMapId);
        else
            Duck.RequestTaunt(NulgathMapId);
    }

    private static string GetAbyssTauntSignalName(
        int fightAttempt,
        int abyssCycle
    ) =>
        $"ABYSS_TAUNT_DONE_{fightAttempt}_{abyssCycle}";

    private bool HandleFightReset(int fightAttempt)
    {
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
