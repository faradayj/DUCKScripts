/*
name: Kasuko DUCK
description: Seven-player CoreDUCK Army script for Kasuko.
tags: ultra, kasuko, seven-player, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class KasukoDUCK
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

    private const string LogPrefix = "KasukoDUCK";
    private const string SyncFileName = "KasukoDUCK.sync";
    private const string MapName = "lavarockshore";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "Enter";
    private const string BossPad = "Spawn";
    private const string EnrageScroll = "Scroll of Enrage";
    private const int UltraQuestId = 9254;
    private const int MinimumLevel = 75;
    private const int WhirlpoolMapId = 1;
    private const int KasukoMapId = 2;
    private const string BossName = "Kasuko";
    private const string WhirlpoolName = "Whirlpool";
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 5;
    private const int DeathResetThreshold = 3;
    private const int DefaultPrivateRoomNumber = 1245;
    private const int TargetArmySize = 7;
    private const int TauntIntervalSeconds = 5;

    private string playerAlias = string.Empty;
    private bool isTaunter;
    private bool isTaunterOne;
    private bool isTaunterTwo;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private DuckAssignmentResult? currentAssignResult;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "KasukoDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        new Option<int>("PrivateRoomNumber", "Private Room Number", "Private room number to use for the army.", DefaultPrivateRoomNumber),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly DuckSlotRequirement[] KasukoSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Legion Revenant", "King's Echo" } }, // Slot 1: Taunter 1
        new DuckSlotRequirement { CandidateClasses = new[] { "StoneCrusher" } },                     // Slot 2: Buffer
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchPaladin" } },                      // Slot 3: Debuffer / Defense
        new DuckSlotRequirement { CandidateClasses = new[] { "Lord of Order" } },                   // Slot 4: Buffer / Healer
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight" } },                 // Slot 5: Taunter 2
        new DuckSlotRequirement { CandidateClasses = new[] { "Bard" } },                             // Slot 6: Buffer
        new DuckSlotRequirement { CandidateClasses = new[] { "Quantum Chronomancer", "Shaman", "ArchFiend", "Arcana Invoker" } } // Slot 7: DPS
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
            KasukoSlots,
            armySize: TargetArmySize
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

        isTaunterOne = assignResult.RoleName.Equals("Legion Revenant", StringComparison.OrdinalIgnoreCase) ||
                       assignResult.RoleName.Equals("King's Echo", StringComparison.OrdinalIgnoreCase);
        isTaunterTwo = assignResult.RoleName.Equals("Verus DoomKnight", StringComparison.OrdinalIgnoreCase);
        isTaunter = isTaunterOne || isTaunterTwo;

        if (isTaunter)
            preset.CombatPotion = null;

        Duck.FileLog($"Started as {playerAlias} in room {privateRoomNumber}.", LogPrefix);
        Core.Logger($"{LogPrefix} started as {playerAlias} in room {privateRoomNumber}.");

        Bot.Drops.Add("Volcanic Essence");

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

        if (isTaunter)
            Duck.PrepareScrolls(EnrageScroll);

        if (Bot.ShouldExit)
            return false;

        Duck.FileLog($"{playerAlias} finished setup.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return !Bot.ShouldExit;
    }

    private bool PrepareSafeRoom(ClassPreset preset)
    {
        Duck.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);

        if (isTaunter)
            Duck.EquipScroll(EnrageScroll);

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
            try
            {
                if (!Sync("FIGHT_READY"))
                    return false;

                Duck.JumpToMonsterCell(BossName);

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
                    Bot.Quests.CanComplete(UltraQuestId) || Bot.Quests.IsDailyComplete(UltraQuestId);

                if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, TargetArmySize, LogPrefix, timeoutMs: 8000))
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
            isTaunter,
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: !isTaunter ? preset.CombatPotion : null
        );

        bool bossObservedAlive = false;
        DateTime fightStart = DateTime.UtcNow;
        int cycleLength = TauntIntervalSeconds * 2; // 10s cycle
        int lastTauntCycle = -1;

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

                if (!Duck.IsMonsterAlive(KasukoMapId) && bossObservedAlive)
                    break;

                if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                {
                    StopFightCombat();
                    return FightResult.Reset;
                }

                Duck.JumpToMonsterCell(BossName);
                continue;
            }

            bool bossAlive = Duck.IsMonsterAlive(KasukoMapId);
            if (bossAlive)
                bossObservedAlive = true;
            else if (bossObservedAlive)
                break;

            if (isTaunter)
            {
                // Taunters strictly focus and maintain target on Kasuko (MapID: 2)
                Duck.MaintainTarget(KasukoMapId);

                if (bossAlive)
                {
                    double elapsed = (DateTime.UtcNow - fightStart).TotalSeconds;
                    int currentCycle = (int)Math.Floor(elapsed / cycleLength);
                    double timeInCycle = elapsed % cycleLength;

                    if (currentCycle != lastTauntCycle)
                    {
                        if (
                            isTaunterOne
                            && timeInCycle >= 0
                            && timeInCycle < TauntIntervalSeconds
                        )
                        {
                            Duck.RequestTaunt(KasukoMapId);
                            lastTauntCycle = currentCycle;
                        }
                        else if (
                            !isTaunterOne
                            && timeInCycle >= TauntIntervalSeconds
                            && timeInCycle < cycleLength
                        )
                        {
                            Duck.RequestTaunt(KasukoMapId);
                            lastTauntCycle = currentCycle;
                        }
                    }
                }
            }
            else
            {
                // Non-taunters focus Whirlpool (MapID: 1) if alive to prevent 25-30s wipe timer, otherwise Kasuko
                int targetMapId = Duck.IsMonsterAlive(WhirlpoolMapId) ? WhirlpoolMapId : KasukoMapId;
                Duck.MaintainTarget(targetMapId);
            }

            Bot.Sleep(FightPollDelay);
        }

        StopFightCombat();
        return (Bot.ShouldExit || !bossObservedAlive) ? FightResult.Stopped : FightResult.Defeated;
    }

    private void StopFightCombat()
    {
        Duck.StopSkillEngine();
        Bot.Combat.CancelTarget();
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

    private bool Sync(string step) => Duck.SyncArmy(step);
}
