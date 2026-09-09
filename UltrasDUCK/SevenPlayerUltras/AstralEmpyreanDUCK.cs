/*
name: Astral Empyrean DUCK
description: Seven-player CoreDUCK Army script for Astral Empyrean.
tags: ultra, astral empyrean, seven-player, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include DUCKScripts\UltrasDUCK\CoreDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class AstralEmpyreanDUCK
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

    private const string LogPrefix = "AstralEmpyreanDUCK";
    private const string SyncFileName = "AstralEmpyreanDUCK.sync";
    private const string MapName = "astralshrine";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const string PacketCommand = "ct";
    private const string StarfirePacketText = "starfire";
    private const string BossPacketText = "\"cInf\":\"m:1\"";
    private const string StardustBreathPacketText = "\"animStr\":\"Attack2\"";
    private const int UltraQuestId = 9803;
    private const int PrerequisiteQuestId = 9802;
    private const string PrerequisiteQuestName = "Hoshiyoru";
    private const int MinimumLevel = 95;
    private const int BossMapId = 1;
    private const int FightPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int MaxFightAttempts = 3;
    private const int DeathResetThreshold = 3;
    private const int ZoneBTargetX = 240;
    private const int ZoneBTargetY = 200;
    private const int ZoneATargetX = 600;
    private const int ZoneATargetY = 430;
    private const int DefaultPrivateRoomNumber = 1245;
    private const int TargetArmySize = 7;

    private readonly Queue<string> pendingZones = new();
    private readonly object zoneLock = new();

    private string playerAlias = string.Empty;
    private bool isTaunter;
    private bool isTaunterOne;
    private bool isTaunterTwo;
    private bool isTimedHealer;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;

    public string OptionsStorage = "AstralEmpyreanDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] AstralSlots = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "Legion Revenant", "King's Echo" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "StoneCrusher" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchPaladin" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Lord of Order" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Bard" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Quantum Chronomancer", "Shaman", "ArchFiend", "Arcana Invoker" } }
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
            StopZoneListener();
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
            Duck.StopPacketDetector();
            StopZoneListener();
            Duck.StopSkillEngine();
            masterMode = false;
        }

        return runResult;
    }

    public UltraRunResult RunFromMaster(int roomNumber = DefaultPrivateRoomNumber) =>
        Run(isMasterMode: true, masterRoomNumber: roomNumber);

    private void ExecuteScript()
    {
        if (!masterMode)
            privateRoomNumber = DefaultPrivateRoomNumber;

        if (!Duck.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        DuckAssignmentResult? assignResult = Duck.AutoAssignAndEquip(
            SyncFileName,
            AstralSlots,
            armySize: TargetArmySize
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        ClassPreset preset = assignResult.Preset;
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";

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

        isTimedHealer = assignResult.RoleName.Equals("Lord of Order", StringComparison.OrdinalIgnoreCase);
        isTaunterOne = assignResult.RoleName.Equals("Legion Revenant", StringComparison.OrdinalIgnoreCase) ||
                       assignResult.RoleName.Equals("King's Echo", StringComparison.OrdinalIgnoreCase);
        isTaunterTwo = assignResult.RoleName.Equals("Verus DoomKnight", StringComparison.OrdinalIgnoreCase) ||
                       assignResult.RoleName.Equals("ArchPaladin", StringComparison.OrdinalIgnoreCase);
        isTaunter = isTaunterOne || isTaunterTwo;

        ApplyAstralOverrides(preset, assignResult.RoleName);

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

        if (assignResult.IsLeader)
        {
            Bot.Sleep(1000);
            Duck.ClearAllSyncFiles(SyncFileName);
        }
    }

    private void ApplyAstralOverrides(ClassPreset preset, string roleName)
    {
        if (roleName.Equals("Quantum Chronomancer", StringComparison.OrdinalIgnoreCase))
        {
            preset.CapeEnhancement = CapeSpecial.Penitence;
        }
        else if (preset.CapeEnhancement == CapeSpecial.Vainglory)
        {
            preset.CapeEnhancement = CapeSpecial.Lament;
        }

        if (isTimedHealer)
        {
            preset.CapeEnhancement = CapeSpecial.Absolution;
            preset.Skills = new[] { 3, 1, 4 };
        }

        if (isTaunter)
            preset.CombatPotion = null;
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

        WarnIfVaingloryCapeIsEquipped();
        Duck.FileLog($"{playerAlias} finished setup.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private void WarnIfVaingloryCapeIsEquipped()
    {
        foreach (var item in Bot.Inventory.Items)
        {
            if (
                !item.Equipped
                || !string.Equals(item.CategoryString, "Cape", StringComparison.OrdinalIgnoreCase)
                || item.EnhancementPatternID != (int)CapeSpecial.Vainglory
            )
                continue;

            Core.Logger("Vainglory is enhanced on the cape. Astral Empyrean will fail.", "Prepare", messageBox: !masterMode);
            return;
        }
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
            Duck.FileLog($"{playerAlias} joined {MapName}-{privateRoomNumber} for attempt {fightAttempt}.", LogPrefix);

            if (!PrepareSafeRoom(preset))
                return false;

            StartZoneListener();
            if (!StartFightPacketDetector())
            {
                StopZoneListener();
                return false;
            }

            FightResult result;
            try
            {
                if (!Sync("FIGHT_READY"))
                    return false;

                Core.Jump(BossCell, BossPad);
                Duck.FileLog($"{playerAlias} jumped to {BossCell} for attempt {fightAttempt}.", LogPrefix);

                result = Fight(preset, fightAttempt);
            }
            finally
            {
                Duck.StopPacketDetector();
                StopZoneListener();
            }

            if (result == FightResult.Defeated)
            {
                Core.Jump(SafeCell, SafePad);
                Duck.EnsureAlive(15);
                bool CreditCheck() =>
                    Bot.Quests.CanComplete(UltraQuestId) || Bot.Quests.IsDailyComplete(UltraQuestId);

                if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, 7, LogPrefix, timeoutMs: 8000))
                {
                    Duck.FileLog($"{playerAlias} confirmed Astral Empyrean defeated on attempt {fightAttempt}.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} confirmed Astral Empyrean defeated.");
                    return true;
                }

                Core.Logger($"{LogPrefix} One or more players missed kill credit on attempt {fightAttempt}. Retrying fight with entire army...");
                result = FightResult.Reset;
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
        int nextStarfireCycle = 1;
        int nextStardustBreath = 1;
        DateTime openingTauntAt = DateTime.UtcNow.AddMilliseconds(1500);
        bool openingTauntRequested = !isTaunterTwo;

        try
        {
            while (!Bot.ShouldExit)
            {
                DrainZoneEvents(move: Bot.Player.Alive);

                if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                    return FightResult.Reset;

                if (!Bot.Player.Alive)
                {
                    Duck.FileLog($"{playerAlias} died.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} died.");

                    while (!Bot.ShouldExit && !Bot.Player.Alive)
                    {
                        DrainZoneEvents(move: false);
                        ProcessStarfireDetections(ref nextStarfireCycle, requestTaunt: false);
                        ProcessStardustBreathDetections(ref nextStardustBreath, requestHeal: false);

                        if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                            return FightResult.Reset;

                        Bot.Sleep(RespawnPollDelay);
                    }

                    if (Bot.ShouldExit)
                        break;

                    if (!Duck.IsMonsterAlive(BossMapId) && bossObservedAlive)
                        break;

                    if (Duck.ShouldResetFight(fightAttempt, DeathResetThreshold))
                        return FightResult.Reset;

                    Duck.FileLog($"{playerAlias} respawned.", LogPrefix);
                    Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                    if (Duck.IsMonsterAlive(BossMapId) && !IsInBossRoom())
                        Core.Jump(BossCell, BossPad);

                    continue;
                }

                bool bossAlive = Duck.IsMonsterAlive(BossMapId);
                if (bossAlive)
                    bossObservedAlive = true;
                else if (bossObservedAlive)
                    break;

                if (!openingTauntRequested && bossAlive && DateTime.UtcNow >= openingTauntAt)
                {
                    openingTauntRequested = true;
                    Duck.RequestAbsolutePriorityTaunt(BossMapId);
                    Duck.FileLog($"{playerAlias} requested opening taunt.", LogPrefix);
                }

                Duck.MaintainTarget(BossMapId);
                ProcessStarfireDetections(ref nextStarfireCycle, requestTaunt: true);
                ProcessStardustBreathDetections(ref nextStardustBreath, requestHeal: true);
                Bot.Sleep(FightPollDelay);
            }

            return (Bot.ShouldExit || !bossObservedAlive) ? FightResult.Stopped : FightResult.Defeated;
        }
        finally
        {
            StopFightCombat();
        }
    }

    private void ProcessStarfireDetections(ref int nextStarfireCycle, bool requestTaunt)
    {
        if (!isTaunter)
            return;

        while (Duck.HasPacketDetection(nextStarfireCycle))
        {
            bool ownsCycle = (nextStarfireCycle % 2 == 1)
                ? isTaunterOne
                : isTaunterTwo;

            if (ownsCycle && requestTaunt)
            {
                Duck.RequestAbsolutePriorityTaunt(BossMapId);
                Duck.FileLog($"[Astral] {playerAlias} requested Starfire taunt {nextStarfireCycle}.", LogPrefix);
            }

            nextStarfireCycle++;
        }
    }

    private bool StartFightPacketDetector()
    {
        if (isTaunter)
            return Duck.StartPacketDetector(PacketCommand, StarfirePacketText);

        if (isTimedHealer)
            return Duck.StartPacketDetector(PacketCommand, new[] { BossPacketText, StardustBreathPacketText });

        return true;
    }

    private void ProcessStardustBreathDetections(ref int nextStardustBreath, bool requestHeal)
    {
        if (!isTimedHealer)
            return;

        while (Duck.HasPacketDetection(nextStardustBreath))
        {
            if (requestHeal)
            {
                Duck.RequestAbsolutePrioritySkill(2);
                Duck.FileLog($"[Astral] {playerAlias} requested timed Stardust Breath heal {nextStardustBreath}.", LogPrefix);
            }

            nextStardustBreath++;
        }
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
        if (zone != "A" && zone != "B")
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

            if (!move)
                continue;

            if (zone == "B")
                Bot.Player.WalkTo(ZoneBTargetX, ZoneBTargetY);
            else
                Bot.Player.WalkTo(ZoneATargetX, ZoneATargetY);
        }
    }

    private bool HandleFightReset(int fightAttempt)
    {
        Duck.FileLog($"{playerAlias} executing fight reset for attempt {fightAttempt}.", LogPrefix);
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

    private void StopFightCombat()
    {
        Duck.StopSkillEngine();
        Bot.Combat.CancelTarget();
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

