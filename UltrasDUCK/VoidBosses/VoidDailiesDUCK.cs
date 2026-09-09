/*
name: Void Dailies DUCK
description: Master combined daily runner for Quest 9091 (Wrong Turn at Voidbuquerque) and Quest 8653 (The Encroaching Shadows) using CoreDUCK.
tags: void, dailies, 9091, 8653, xyfrag, nightbane, flibbi, icewing, hydra, coreduck, 7man
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include DUCKScripts\UltrasDUCK\CoreDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\VoidBosses\VoidXyfragDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\VoidBosses\VoidNightbaneDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\VoidBosses\VoidFlibbitiestgibbetDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\SevenPlayerUltras\IceWingDUCK.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class VoidDailiesDUCK
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "Void Dailies DUCK";
    private const string RosterSyncFileName = "VoidDailiesDUCK_Roster.sync";
    private const string CompletionSyncFileName = "VoidDailiesDUCK_Completion.sync";
    private const int DefaultPrivateRoomNumber = 1245;
    private const int TargetArmySize = 7;

    // Quest IDs
    private const int WrongTurnQuestId = 9091;
    private const int EncroachingShadowsQuestId = 8653;

    // Item IDs
    private const int XyfragEssenceId = 73863;
    private const int NightbaneEssenceId = 73862;
    private const int FlibbiEssenceId = 73865;
    private const int FlibbitigibletsId = 70054;
    private const int GlacialPinionId = 70052;
    private const int HydraEyeballId = 70053;

    // Item Names
    private const string XyfragEssence = "Xyfrag's ??? Essence";
    private const string NightbaneEssence = "Nightbane's ??? Essence";
    private const string FlibbiEssence = "Flibbitiestgibbet's ??? Essence";
    private const string Flibbitigiblets = "Flibbitigiblets";
    private const string GlacialPinion = "Glacial Pinion";
    private const string HydraEyeball = "Hydra Eyeball";

    // Item-Drop centered bitmask flags
    private const int XyfragBit = 1 << 0;
    private const int NightbaneBit = 1 << 1;
    private const int FlibbiBit = 1 << 2;
    private const int IceWingBit = 1 << 3;

    private static readonly int[] BossBits = new[]
    {
        XyfragBit,
        NightbaneBit,
        FlibbiBit,
        IceWingBit,
    };

    public enum VoidbuquerqueReward
    {
        Blood_Gem_of_the_Archfiend = 22332,
        Totem_of_Nulgath = 5357,
        Gem_of_Nulgath = 6136,
        Dark_Crystal_Shard = 4770,
        Diamond_of_Nulgath = 4771,
        Tainted_Gem = 4769,
    }

    private static readonly VoidXyfragDUCK Xyfrag = new();
    private static readonly VoidNightbaneDUCK Nightbane = new();
    private static readonly VoidFlibbitiestgibbetDUCK Flibbi = new();
    private static readonly IceWingDUCK IceWing = new();

    private int partyRequiredMask;
    private int actualRunMask;
    private int remainingBosses;

    public string OptionsStorage = "VoidDailiesDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new()
    {
        new Option<VoidbuquerqueReward>("Reward", "Reward for Quest 9091", "Select which reward to turn in Quest 9091 for.", VoidbuquerqueReward.Blood_Gem_of_the_Archfiend),
        CoreBots.Instance.SkipOptions,
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;
        Bot.Options.AcceptACDrops = true;
        Bot.Options.RejectAllDrops = false;

        Run();
    }

    public void Run()
    {
        Bot.Options.InfiniteRange = true;
        Bot.Options.AcceptACDrops = true;
        Bot.Options.RejectAllDrops = false;

        int privateRoomNumber = DefaultPrivateRoomNumber;
        if (!Duck.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        VoidbuquerqueReward selectedReward = Bot.Config?.Get<VoidbuquerqueReward>("Reward") ?? VoidbuquerqueReward.Blood_Gem_of_the_Archfiend;
        string rewardName = selectedReward.ToString().Replace("_", " ");

        // Setup drop listeners, unbank items, and accept active daily quests
        PrepareDropsAndQuests(selectedReward, rewardName);

        // Pre-check turn-ins (in case drops are already held from previous sessions)
        CheckEarlyTurnIns(selectedReward, rewardName);

        // Compute party item-drop requirements
        if (!BuildPartyRequiredMask())
            return;

        actualRunMask = partyRequiredMask;
        remainingBosses = CountBosses(actualRunMask);

        Duck.FileLog($"Starting Void Dailies DUCK with {remainingBosses} required bosses across {TargetArmySize} accounts. partyRequiredMask={partyRequiredMask}", LogPrefix);
        Core.Logger($"Starting Void Dailies DUCK with {remainingBosses} required bosses across {TargetArmySize} accounts.", LogPrefix);

        if (remainingBosses > 0 && !PrepareOracle())
            return;

        // Stage 1: Synchronized 7-Player Army Gauntlet (Xyfrag -> Nightbane -> Flibbi -> Icewing)
        // 1. Void Xyfrag (Xyfrag's ??? Essence)
        if (!RunSelectedBoss("Void Xyfrag", XyfragBit, () => Xyfrag.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // 2. Void Nightbane (Nightbane's ??? Essence)
        if (!RunSelectedBoss("Void Nightbane", NightbaneBit, () => Nightbane.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // 3. Void Flibbitiestgibbet (Flibbi Essence + Flibbitigiblets)
        if (!RunSelectedBoss("Void Flibbitiestgibbet", FlibbiBit, () => Flibbi.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // 4. Warlord Icewing (Glacial Pinion)
        if (!RunSelectedBoss("Warlord Icewing", IceWingBit, () => IceWing.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // Cleanse army sync files - no more lockstep sync needed!
        Duck.FileLog("Shared 7-player boss gauntlet complete. Closing army sync and transitioning to independent solo Hydra farm.", LogPrefix);
        Core.Logger("Shared 7-player boss gauntlet complete. Closing army sync and transitioning to independent solo Hydra farm.", LogPrefix);
        Duck.StopSkillEngine();
        Duck.ClearAssignSync(RosterSyncFileName);
        Duck.ClearAssignSync(CompletionSyncFileName);

        // Stage 2: Independent Solo Phase - Hydra Challenge (Hydra Head 90)
        FarmHydraSolo();

        // Stage 3: Quest Turn-Ins
        TurnInDailies(selectedReward, rewardName);

        Duck.FileLog("Void Dailies DUCK completed successfully!", LogPrefix);
        Core.Logger("Void Dailies DUCK completed successfully!", LogPrefix);
    }

    private bool RunSelectedBoss(
        string bossName,
        int bossBit,
        Func<UltraRunResult> runBoss
    )
    {
        if ((actualRunMask & bossBit) == 0)
            return true;

        Duck.FileLog($"Beginning {bossName}. {remainingBosses} bosses remaining in gauntlet.", LogPrefix);
        Core.Logger($"Beginning {bossName}. {remainingBosses} bosses remaining in gauntlet.", LogPrefix);

        UltraRunResult result = runBoss();
        if (result == UltraRunResult.Failed)
        {
            Core.Logger($"{bossName} failed. Stopping Army.", LogPrefix);
            return false;
        }

        if (result == UltraRunResult.AttemptsExhausted)
        {
            Core.Logger($"{bossName} attempts were exhausted. Skipping to next boss.", LogPrefix);
            actualRunMask &= ~bossBit;
            remainingBosses--;
            return ResetBetweenBosses();
        }

        actualRunMask &= ~bossBit;
        remainingBosses--;
        Core.Logger($"{bossName} completed. {remainingBosses} bosses remaining.", LogPrefix);

        if (remainingBosses > 0 && !ResetBetweenBosses())
            return false;

        return true;
    }

    private void FarmHydraSolo()
    {
        if (IsBossComplete("Hydra"))
        {
            Duck.FileLog("Hydra Eyeball requirement already fulfilled or Quest 8653 completed. Skipping Hydra farm.", LogPrefix);
            Core.Logger("Hydra Eyeball requirement already fulfilled or Quest 8653 completed. Skipping Hydra farm.", LogPrefix);
            return;
        }

        Duck.FileLog("Setting up Yami no Ronin for independent Hydra Head 90 solo takedown.", LogPrefix);
        Core.Logger("Setting up Yami no Ronin for independent Hydra Head 90 solo takedown.", LogPrefix);

        ClassPreset preset = Core.CheckInventory("Yami no Ronin")
            ? Duck.YamiNoRonin()
            : Duck.GetClassPreset(Bot.Player.CurrentClass?.Name ?? "Solo");

        Duck.EquipClass(preset);
        Duck.PrepareEnhancements(
            preset.BaseEnhancement,
            preset.CapeEnhancement,
            preset.HelmEnhancement,
            preset.WeaponEnhancement,
            weaponFallbacks: preset.WeaponEnhancementFallbacks
        );
        Duck.PreparePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);
        Duck.UsePotions(preset.Tonic, preset.Elixir, preset.CombatPotion);
        Duck.GenericPrebuff();

        if (!Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId) && !Bot.Quests.IsInProgress(EncroachingShadowsQuestId))
            Core.EnsureAccept(EncroachingShadowsQuestId);

        Core.AddDrop(HydraEyeball);
        Core.AddDrop(HydraEyeballId);

        // Join native private instance at room h90, Left
        Core.Join("hydrachallenge-9999999", "h90", "Left");
        Bot.Sleep(1000);

        const string monsterName = "Hydra Head 90";

        try
        {
            Duck.StartSkillEngine(
                preset.Skills,
                "Solo",
                false,
                LogPrefix,
                preset.SkillMode,
                maintainedPotion: preset.CombatPotion
            );

            while (!Bot.ShouldExit)
            {
                if (IsBossComplete("Hydra"))
                {
                    Duck.FileLog("Collected 3x Hydra Eyeball.", LogPrefix);
                    Core.Logger("Collected 3x Hydra Eyeball.", LogPrefix);
                    break;
                }

                if (!Bot.Player.Alive)
                {
                    while (!Bot.ShouldExit && !Bot.Player.Alive)
                        Bot.Sleep(500);

                    if (Bot.ShouldExit)
                        break;

                    if (Bot.Player.Cell != "h90" || Bot.Player.Pad != "Left")
                        Core.Jump("h90", "Left");

                    continue;
                }

                if (Bot.Player.Cell != "h90")
                    Core.Jump("h90", "Left");

                Duck.MaintainTarget(monsterName);
                Bot.Sleep(150);
            }
        }
        finally
        {
            Duck.StopSkillEngine();
            Bot.Combat.CancelTarget();
        }
    }

    private void CheckEarlyTurnIns(VoidbuquerqueReward selectedReward, string rewardName)
    {
        if (!Bot.Quests.IsDailyComplete(WrongTurnQuestId) && Bot.Quests.CanComplete(WrongTurnQuestId))
        {
            Core.EnsureComplete(WrongTurnQuestId, (int)selectedReward);
            Bot.Sleep(1000);
            if (Bot.Quests.IsDailyComplete(WrongTurnQuestId))
            {
                Duck.FileLog($"[Early Turn-In] Quest 9091 completed with reward: {rewardName}.", LogPrefix);
                Core.Logger($"[Early Turn-In] Quest 9091 completed with reward: {rewardName}.", LogPrefix);
            }
        }

        if (!Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId) && Bot.Quests.CanComplete(EncroachingShadowsQuestId))
        {
            Core.EnsureComplete(EncroachingShadowsQuestId);
            Bot.Sleep(1000);
            if (Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId))
            {
                Duck.FileLog("[Early Turn-In] Quest 8653 completed.", LogPrefix);
                Core.Logger("[Early Turn-In] Quest 8653 completed.", LogPrefix);
            }
        }
    }

    private void TurnInDailies(VoidbuquerqueReward selectedReward, string rewardName)
    {
        Bot.Sleep(1000);

        // Turn in Quest 9091: Wrong Turn at Voidbuquerque
        if (!Bot.Quests.IsDailyComplete(WrongTurnQuestId) && Bot.Quests.CanComplete(WrongTurnQuestId))
        {
            Core.EnsureComplete(WrongTurnQuestId, (int)selectedReward);
            Bot.Sleep(1000);
            if (Bot.Quests.IsDailyComplete(WrongTurnQuestId))
            {
                Duck.FileLog($"[Quest 9091 Complete] Claimed reward: {rewardName}.", LogPrefix);
                Core.Logger($"[Quest 9091 Complete] Claimed reward: {rewardName}.", LogPrefix);
            }
        }

        // Turn in Quest 8653: The Encroaching Shadows
        if (!Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId) && Bot.Quests.CanComplete(EncroachingShadowsQuestId))
        {
            Core.EnsureComplete(EncroachingShadowsQuestId);
            Bot.Sleep(1000);
            if (Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId))
            {
                Duck.FileLog("[Quest 8653 Complete] The Encroaching Shadows turned in successfully.", LogPrefix);
                Core.Logger("[Quest 8653 Complete] The Encroaching Shadows turned in successfully.", LogPrefix);
            }
        }

        ResetToHouse();
    }

    private void PrepareDropsAndQuests(VoidbuquerqueReward selectedReward, string rewardName)
    {
        Core.BankingBlackList.AddRange(new[]
        {
            XyfragEssence,
            NightbaneEssence,
            FlibbiEssence,
            Flibbitigiblets,
            GlacialPinion,
            HydraEyeball,
            rewardName,
        });

        Core.AddDrop(
            XyfragEssence,
            NightbaneEssence,
            FlibbiEssence,
            Flibbitigiblets,
            GlacialPinion,
            HydraEyeball,
            rewardName
        );
        Core.AddDrop(XyfragEssenceId, NightbaneEssenceId, FlibbiEssenceId, FlibbitigibletsId, GlacialPinionId, HydraEyeballId, (int)selectedReward);

        Core.Unbank(
            XyfragEssence,
            NightbaneEssence,
            FlibbiEssence,
            Flibbitigiblets,
            GlacialPinion,
            HydraEyeball
        );

        if (!Bot.Quests.IsDailyComplete(WrongTurnQuestId) && !Bot.Quests.IsInProgress(WrongTurnQuestId))
            Core.EnsureAccept(WrongTurnQuestId);

        if (!Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId) && !Bot.Quests.IsInProgress(EncroachingShadowsQuestId))
            Core.EnsureAccept(EncroachingShadowsQuestId);
    }

    private bool BuildPartyRequiredMask()
    {
        Duck.FileLog($"[PartySync] Discovering {TargetArmySize} active accounts for Void Dailies gauntlet...", LogPrefix);
        Core.Logger($"[PartySync] Discovering {TargetArmySize} active accounts for Void Dailies gauntlet...", LogPrefix);
        string[]? discovered = Duck.DiscoverParty(RosterSyncFileName, armySize: TargetArmySize);
        if (discovered == null)
            return false;

        Duck.FileLog($"[PartySync] Discovered party: [{string.Join(", ", discovered)}]. Starting lockstep sync...", LogPrefix);
        Core.Logger($"[PartySync] Discovered party: [{string.Join(", ", discovered)}]. Starting lockstep sync...", LogPrefix);
        if (!Duck.StartArmySyncDynamic(CompletionSyncFileName, discovered))
            return false;

        int localRequiredMask = BuildLocalRequiredMask();
        foreach (int bossBit in BossBits)
        {
            if ((localRequiredMask & bossBit) != 0)
            {
                if (!Duck.SendArmySignal($"BOSS_REQUIRED_{bossBit}"))
                    return false;
            }
        }

        if (!Duck.SyncArmy("COMPLETION_MASK_READY"))
            return false;

        partyRequiredMask = 0;
        foreach (int bossBit in BossBits)
        {
            string signal = $"BOSS_REQUIRED_{bossBit}";
            for (int playerNumber = 1; playerNumber <= TargetArmySize; playerNumber++)
            {
                if (Duck.HasArmySignal(signal, playerNumber))
                {
                    partyRequiredMask |= bossBit;
                    break;
                }
            }
        }

        Duck.ClearAssignSync(RosterSyncFileName);
        Duck.ClearAssignSync(CompletionSyncFileName);

        Duck.FileLog($"[PartySync] Item-drop requirements mask resolved: {partyRequiredMask}.", LogPrefix);
        Core.Logger($"[PartySync] Item-drop requirements mask resolved: {partyRequiredMask}.", LogPrefix);
        return true;
    }

    private bool HasItem(string name, int id = 0, int quantity = 1) =>
        Duck.HasItemAnywhere(name, id, quantity);

    private bool IsBossComplete(string boss)
    {
        bool is9091Done = Bot.Quests.IsDailyComplete(WrongTurnQuestId);
        bool is8653Done = Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId);

        return boss switch
        {
            "Xyfrag" => is9091Done || HasItem(XyfragEssence, XyfragEssenceId),
            "Nightbane" => is9091Done || HasItem(NightbaneEssence, NightbaneEssenceId),
            "Flibbi" => (is9091Done || HasItem(FlibbiEssence, FlibbiEssenceId))
                         && (is8653Done || HasItem(Flibbitigiblets, FlibbitigibletsId)),
            "IceWing" => is8653Done || HasItem(GlacialPinion, GlacialPinionId),
            "Hydra" => is8653Done || HasItem(HydraEyeball, HydraEyeballId, 3),
            _ => false
        };
    }

    private int BuildLocalRequiredMask()
    {
        int myMask = 0;

        // 1. Void Xyfrag: (Wrong Turn 9091 || Xyfrag's ??? Essence)
        if (!IsBossComplete("Xyfrag"))
            myMask |= XyfragBit;

        // 2. Void Nightbane: (Wrong Turn 9091 || Nightbane's ??? Essence)
        if (!IsBossComplete("Nightbane"))
            myMask |= NightbaneBit;

        // 3. Void Flibbi: (9091 || Flibbi Essence) && (8653 || Flibbitigiblets)
        if (!IsBossComplete("Flibbi"))
            myMask |= FlibbiBit;

        // 4. Warlord Icewing: (Encroaching Shadows 8653 || Glacial Pinion)
        if (!IsBossComplete("IceWing"))
            myMask |= IceWingBit;

        return myMask;
    }

    private bool ResetBetweenBosses()
    {
        Duck.EnsureAlive(15);
        Duck.StopSkillEngine();
        Core.Logger("Cleansing state and resetting between bosses...", LogPrefix);
        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");
        if (Bot.ShouldExit)
            return false;

        Bot.Sleep(1000);
        Duck.EquipClass(Duck.Oracle());
        return true;
    }

    private void ResetToHouse()
    {
        Duck.EnsureAlive(15);
        Duck.StopSkillEngine();
        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");
        Bot.Sleep(500);
    }

    private bool PrepareOracle()
    {
        Duck.EquipClass(Duck.Oracle());
        return true;
    }

    private static int CountBosses(int mask)
    {
        int count = 0;
        foreach (int bit in BossBits)
        {
            if ((mask & bit) != 0)
                count++;
        }
        return count;
    }
}

