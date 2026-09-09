/*
name: Ultra Drakath DUCK
description: Four-player CoreDUCK Army script for Champion Drakath.
tags: ultra, champion drakath, weekly, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include DUCKScripts\UltrasDUCK\CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraDrakathDUCK
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

    private static readonly int[] StoneCrusherTauntThresholds =
    {
        16_500_000,
        12_500_000,
        6_500_000,
    };

    private static readonly int[] ArchPaladinTauntThresholds =
    {
        18_500_000,
        14_500_000,
        8_500_000,
        4_500_000,
    };

    private const string LogPrefix = "UltraDrakathDUCK";
    private const string SyncFileName = "UltraDrakathDUCK.sync";
    private const string MapName = "championdrakath";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const int UltraQuestId = 8300;
    private const int PrerequisiteQuestId = 3881;
    private const string PrerequisiteQuestName = "The Final Showdown!";
    private const int MinimumLevel = 80;
    private const int BossMapId = 1;
    private const int FightPollDelay = 150;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DefaultPrivateRoomNumber = 1245;

    private string playerAlias = string.Empty;
    private bool isTaunter;
    private bool isStoneCrusher;
    private bool isArchPaladin;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "UltraDrakathDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] DrakathSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Chaos Slayer", "King's Echo", "Verus DoomKnight", "Void Highlord", "Legion Revenant", "Chaos Avenger" } },
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
            DrakathSlots,
            armySize: 4
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        ClassPreset preset = assignResult.Preset;
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";
        isStoneCrusher = string.Equals(preset.ClassName, "StoneCrusher", StringComparison.OrdinalIgnoreCase);
        isArchPaladin = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        isTaunter = isStoneCrusher || isArchPaladin;

        // Drakath defensive enhancement overrides (eliminate lethal Vainglory, enforce Penitence DR)
        if (isArchPaladin || preset.ClassName.StartsWith("Chaos Slayer", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(preset.ClassName, "Verus DoomKnight", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(preset.ClassName, "Legion Revenant", StringComparison.OrdinalIgnoreCase))
        {
            preset.CapeEnhancement = CapeSpecial.Penitence;
        }
        else if (preset.CapeEnhancement == CapeSpecial.Vainglory)
        {
            preset.CapeEnhancement = CapeSpecial.Lament;
        }

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
            Duck.FileLog($"{playerAlias} joined {MapName}-{privateRoomNumber} for attempt {fightAttempt}.", LogPrefix);

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            StartSkillEngine(preset);
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
                    Duck.FileLog($"{playerAlias} confirmed Champion Drakath defeated on attempt {fightAttempt}.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} confirmed Champion Drakath defeated.");
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
        Duck.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (isTaunter)
            Duck.EquipScroll(EnrageScroll);

        Duck.GenericPrebuff();
        return !Bot.ShouldExit;
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
        Duck.FileLog($"{playerAlias} started skill engine.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");
    }

    private FightResult Fight(int fightAttempt)
    {
        try
        {
            int[] tauntThresholds = GetTauntThresholds();
            int tauntIndex = 0;

            while (!Bot.ShouldExit)
            {
                if (!Duck.IsMonsterAlive(BossMapId))
                    break;

                if (Duck.ShouldResetFight(fightAttempt))
                    return FightResult.Reset;

                if (!Bot.Player.Alive)
                {
                    Duck.FileLog($"{playerAlias} died.", LogPrefix);
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

                    Duck.FileLog($"{playerAlias} respawned.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                    if (Duck.IsMonsterAlive(BossMapId) && !IsInBossRoom())
                        Core.Jump(BossCell, BossPad);

                    continue;
                }

                int bossHealth = Duck.GetMonsterHP(BossMapId);
                Duck.MaintainTarget(BossMapId);

                if (
                    tauntIndex < tauntThresholds.Length
                    && bossHealth > 0
                    && bossHealth <= tauntThresholds[tauntIndex]
                )
                {
                    int threshold = tauntThresholds[tauntIndex];
                    Duck.RequestTaunt(BossMapId);
                    tauntIndex++;
                    Duck.FileLog($"{playerAlias} requested taunt at {threshold} HP.", LogPrefix);
                    Core.Logger(
                        $"{LogPrefix} {playerAlias} requested taunt at {threshold} HP."
                    );
                }

                Bot.Sleep(FightPollDelay);
            }

            if (Bot.ShouldExit)
                return FightResult.Stopped;

            return FightResult.Defeated;
        }
        finally
        {
            Duck.StopSkillEngine();
        }
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

    private int[] GetTauntThresholds()
    {
        if (isStoneCrusher)
            return StoneCrusherTauntThresholds;

        if (isArchPaladin)
            return ArchPaladinTauntThresholds;

        return Array.Empty<int>();
    }

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

