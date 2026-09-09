/*
name: Ultra Warden LW
description: Four-player CoreDUCK Army script for Ultra Warden.
tags: ultra, warden, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/Customv2/UltrasDUCK/CoreDUCK.cs
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class UltraWardenDUCK
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
        Defeated,
        Reset,
        Stopped,
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "Ultra Warden DUCK";
    private const string SyncFileName = "UltraWardenDUCK.sync";
    private const string MapName = "ultrawarden";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string BossCell = "r2";
    private const string BossPad = "Left";
    private const string EnrageScroll = "Scroll of Enrage";
    private const int UltraQuestId = 8153;
    private const int PrerequisiteQuestId = 8151;
    private const string PrerequisiteQuestName = "The Engineer";
    private const int MinimumLevel = 61;
    private const int MonsterMapId = 1;
    private const int TauntHealth = 500_000;
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

    public string OptionsStorage = "UltraWardenDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] WardenSlots = new[]
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
            WardenSlots,
            armySize: 4
        );

        if (assignResult == null)
            return;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        ClassPreset preset = assignResult.Preset;
        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";
        isTaunter = Duck.IsArmyPlayer(1);
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

            if (!PrepareSafeRoom(preset) || !Sync("FIGHT_READY"))
                return false;

            Core.Jump(BossCell, BossPad);

            if (Bot.ShouldExit || !Sync("START_FIGHT"))
                return false;

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

    private FightResult Fight(ClassPreset preset, int fightAttempt)
    {
        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            isTaunter,
            LogPrefix,
            preset.SkillMode,
            useSurvivalSkill: armyComposition != ArmyComposition.Stable
        );
        Core.Logger($"{LogPrefix} {playerAlias} started fighting.");

        bool tauntRequested = false;

        while (!Bot.ShouldExit)
        {
            if (!Duck.IsMonsterAlive(MonsterMapId))
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

                if (!Duck.IsMonsterAlive(MonsterMapId))
                    break;

                if (Duck.ShouldResetFight(fightAttempt))
                {
                    Duck.StopSkillEngine();
                    return FightResult.Reset;
                }

                Core.Logger($"{LogPrefix} {playerAlias} respawned.");

                if (
                    Duck.IsMonsterAlive(MonsterMapId)
                    && (Bot.Player.Cell != BossCell || Bot.Player.Pad != BossPad)
                )
                    Core.Jump(BossCell, BossPad);

                continue;
            }

            int monsterHealth = Duck.GetMonsterHP(MonsterMapId);
            Duck.MaintainTarget(MonsterMapId);

            if (
                isTaunter
                && !tauntRequested
                && monsterHealth > 0
                && monsterHealth <= TauntHealth
            )
            {
                Duck.RequestAbsolutePriorityTaunt(MonsterMapId);
                tauntRequested = true;
                Duck.FileLog($"{playerAlias} requested Warden absolute priority taunt at {monsterHealth} HP.", LogPrefix);
                Core.Logger($"{LogPrefix} {playerAlias} requested the Warden taunt.");
            }

            Bot.Sleep(FightPollDelay);
        }

        Duck.StopSkillEngine();

        if (Bot.ShouldExit)
            return FightResult.Stopped;

        Core.Logger($"{LogPrefix} {playerAlias} confirmed Ultra Warden defeated.");
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
                _ => Duck.LegionRevenant(),
            };

        if (Duck.IsArmyPlayer(2))
            return Duck.StoneCrusher();

        if (Duck.IsArmyPlayer(3))
            return Duck.ArchPaladin();

        return Duck.LordOfOrder();
    }

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
