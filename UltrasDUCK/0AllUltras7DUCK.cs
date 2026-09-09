/*
name: All Ultras 7 DUCK
description: Zero-config auto-assigned runner for 7-player DUCK Ultra scripts, Legion Dailies, and Void Dailies.
tags: ultra, army, coreduck, master, 7man, legion dailies
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/Customv2/UltrasDUCK/CoreDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/SevenPlayerUltras/AstralEmpyreanDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/SevenPlayerUltras/KathoolDepthsDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/SevenPlayerUltras/DeimosDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/SevenPlayerUltras/LegionLichLordDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/SevenPlayerUltras/TheBeastDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/SevenPlayerUltras/KasukoDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/SevenPlayerUltras/IceWingDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/VoidBosses/VoidXyfragDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/VoidBosses/VoidNightbaneDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/VoidBosses/VoidFlibbitiestgibbetDUCK.cs
//cs_include Scripts/Customv2/UltrasDUCK/VoidBosses/VoidDailiesDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class AllUltras7DUCK
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "All Ultras 7 DUCK";
    private const string RosterSyncFileName = "0AllUltras7DUCK_Roster.sync";
    private const string CompletionSyncFileName = "0AllUltras7DUCK_Completion.sync";
    private const int DefaultPrivateRoomNumber = 1245;
    private const int TargetArmySize = 7;

    // Quest IDs
    private const int AstralQuestId = 9803;
    private const int KathoolUltraQuestId = 9350;
    private const int KathoolLegionQuestId = 1677;
    private const int DeimosQuestId = 1676;
    private const int LichLordQuestId = 1674;
    private const int TheBeastQuestId = 1675;
    private const int KasukoQuestId = 9254;
    private const int WrongTurnQuestId = 9091;
    private const int EncroachingShadowsQuestId = 8653;

    // Bit flags for 7-man bosses
    private const int AstralBit = 1 << 0;
    private const int KathoolBit = 1 << 1;
    private const int DeimosBit = 1 << 2;
    private const int LichLordBit = 1 << 3;
    private const int TheBeastBit = 1 << 4;
    private const int KasukoBit = 1 << 5;
    private const int VoidDailiesBit = 1 << 6;

    private static readonly int[] UltraBits = new[]
    {
        AstralBit,
        KathoolBit,
        DeimosBit,
        LichLordBit,
        TheBeastBit,
        KasukoBit,
        VoidDailiesBit,
    };

    private static readonly AstralEmpyreanDUCK Astral = new();
    private static readonly KathoolDepthsDUCK Kathool = new();
    private static readonly DeimosDUCK Deimos = new();
    private static readonly LegionLichLordDUCK LichLord = new();
    private static readonly TheBeastDUCK TheBeast = new();
    private static readonly KasukoDUCK Kasuko = new();
    private static readonly VoidDailiesDUCK VoidDailies = new();

    private int partyRequiredMask;
    private int actualRunMask;
    private int remainingUltras;

    public string OptionsStorage = "0AllUltras7DUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;
        Bot.Options.AcceptACDrops = true;
        Bot.Options.RejectAllDrops = false;

        Run();
    }

    private void Run()
    {
        Bot.Options.InfiniteRange = true;
        Bot.Options.AcceptACDrops = true;
        Bot.Options.RejectAllDrops = false;

        int privateRoomNumber = DefaultPrivateRoomNumber;
        if (!Duck.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        int selectedMask = BuildSelectedMask();
        if (selectedMask == 0)
        {
            Core.Logger("No 7-man Ultras are currently selected.", LogPrefix);
            return;
        }

        if (!BuildPartyRequiredMask())
            return;

        actualRunMask = selectedMask & partyRequiredMask;
        remainingUltras = CountUltras(actualRunMask);

        Duck.FileLog($"Starting All Ultras 7 DUCK with {remainingUltras} needed Ultras across {TargetArmySize} accounts. actualRunMask={actualRunMask}", LogPrefix);
        Core.Logger($"Starting All Ultras 7 DUCK with {remainingUltras} needed Ultras across {TargetArmySize} accounts.", LogPrefix);

        if (remainingUltras > 1 && !PrepareOracle())
            return;

        // 1. Astral Empyrean
        if (!RunSelectedUltra("Astral Empyrean", AstralBit, () => Astral.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // 2. Kathool Depths (9350 & 1677)
        if (!RunSelectedUltra("Kathool Depths", KathoolBit, () => Kathool.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // 3. Deimos (1676)
        if (!RunSelectedUltra("Devastator Deimos", DeimosBit, () => Deimos.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // 4. Legion Lich Lord (1674)
        if (!RunSelectedUltra("Legion Lich Lord", LichLordBit, () => LichLord.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // 5. The Beast (1675)
        if (!RunSelectedUltra("The Beast", TheBeastBit, () => TheBeast.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // 6. Kasuko (9254)
        if (!RunSelectedUltra("Kasuko", KasukoBit, () => Kasuko.Run(isMasterMode: true, privateRoomNumber)))
            return;

        // 7. Void Dailies (Xyfrag -> Nightbane -> Flibbi -> Icewing -> Hydra Solo)
        if ((actualRunMask & VoidDailiesBit) != 0)
        {
            Duck.FileLog($"Beginning Void Dailies Gauntlet (9091 & 8653). {remainingUltras} Ultras remaining.", LogPrefix);
            Core.Logger($"Beginning Void Dailies Gauntlet (9091 & 8653). {remainingUltras} Ultras remaining.", LogPrefix);
            VoidDailies.Run();
            actualRunMask &= ~VoidDailiesBit;
            remainingUltras--;
            if (remainingUltras > 0 && !ResetBetweenUltras())
                return;
        }

        Duck.FileLog("All Ultras 7 DUCK run completed.", LogPrefix);
        Core.Logger("All Ultras 7 DUCK run completed.", LogPrefix);
    }

    private bool RunSelectedUltra(
        string ultraName,
        int ultraBit,
        Func<UltraRunResult> runUltra
    )
    {
        if ((actualRunMask & ultraBit) == 0)
            return true;

        Duck.StopSkillEngine();
        Duck.FileLog($"Beginning {ultraName}. {remainingUltras} Ultras remaining.", LogPrefix);
        Core.Logger($"Beginning {ultraName}. {remainingUltras} Ultras remaining.", LogPrefix);

        UltraRunResult result = runUltra();
        if (result == UltraRunResult.Failed)
        {
            Core.Logger($"{ultraName} failed. Stopping Army.", LogPrefix);
            return false;
        }

        if (result == UltraRunResult.AttemptsExhausted)
        {
            Core.Logger($"{ultraName} attempts were exhausted. Skipping to the next Ultra.", LogPrefix);
            actualRunMask &= ~ultraBit;
            remainingUltras--;
            return ResetBetweenUltras();
        }

        actualRunMask &= ~ultraBit;
        remainingUltras--;
        Core.Logger($"{ultraName} completed. {remainingUltras} Ultras remaining.", LogPrefix);

        if (remainingUltras > 0 && !ResetBetweenUltras())
            return false;

        return true;
    }

    private bool ResetBetweenUltras()
    {
        Duck.EnsureAlive(15);
        Duck.StopSkillEngine();
        Core.Logger("Cleansing state and resetting between Ultras...", LogPrefix);
        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");
        if (Bot.ShouldExit)
            return false;

        Bot.Sleep(1000);
        Duck.EquipClass(Duck.Oracle());
        return true;
    }

    private bool PrepareOracle()
    {
        Duck.EquipClass(Duck.Oracle());
        return true;
    }

    private int BuildSelectedMask()
    {
        int mask = 0;
        foreach (int bit in UltraBits)
            mask |= bit;
        return mask;
    }

    private bool BuildPartyRequiredMask()
    {
        Duck.FileLog($"[PartySync] Discovering {TargetArmySize} active accounts for 7-man gauntlet...", LogPrefix);
        Core.Logger($"[PartySync] Discovering {TargetArmySize} active accounts for 7-man gauntlet...", LogPrefix);
        string[]? discovered = Duck.DiscoverParty(RosterSyncFileName, armySize: TargetArmySize);
        if (discovered == null)
            return false;

        Duck.FileLog($"[PartySync] Discovered party: [{string.Join(", ", discovered)}]. Starting 7-man master lockstep sync...", LogPrefix);
        Core.Logger($"[PartySync] Discovered party: [{string.Join(", ", discovered)}]. Starting 7-man master lockstep sync...", LogPrefix);
        if (!Duck.StartArmySyncDynamic(CompletionSyncFileName, discovered))
            return false;

        int localRequiredMask = BuildLocalRequiredMask();
        foreach (int ultraBit in UltraBits)
        {
            if ((localRequiredMask & ultraBit) != 0)
            {
                if (!Duck.SendArmySignal($"ULTRA_REQUIRED_{ultraBit}"))
                    return false;
            }
        }

        if (!Duck.SyncArmy("COMPLETION_MASK_READY"))
            return false;

        partyRequiredMask = 0;
        foreach (int ultraBit in UltraBits)
        {
            string signal = $"ULTRA_REQUIRED_{ultraBit}";
            for (int playerNumber = 1; playerNumber <= TargetArmySize; playerNumber++)
            {
                if (Duck.HasArmySignal(signal, playerNumber))
                {
                    partyRequiredMask |= ultraBit;
                    break;
                }
            }
        }

        Duck.ClearAssignSync(RosterSyncFileName);
        Duck.ClearAssignSync(CompletionSyncFileName);

        Duck.FileLog($"[PartySync] Party required mask resolved: {partyRequiredMask}.", LogPrefix);
        Core.Logger($"[PartySync] Party required mask resolved: {partyRequiredMask}.", LogPrefix);
        return true;
    }

    private int BuildLocalRequiredMask()
    {
        int myRequiredMask = 0;
        if (!Bot.Quests.IsDailyComplete(AstralQuestId))
            myRequiredMask |= AstralBit;
        if (!Bot.Quests.IsDailyComplete(KathoolUltraQuestId) || !Bot.Quests.IsDailyComplete(KathoolLegionQuestId))
            myRequiredMask |= KathoolBit;
        if (!Bot.Quests.IsDailyComplete(DeimosQuestId))
            myRequiredMask |= DeimosBit;
        if (!Bot.Quests.IsDailyComplete(LichLordQuestId))
            myRequiredMask |= LichLordBit;
        if (!Bot.Quests.IsDailyComplete(TheBeastQuestId))
            myRequiredMask |= TheBeastBit;
        if (!Bot.Quests.IsDailyComplete(KasukoQuestId))
            myRequiredMask |= KasukoBit;
        if (!Bot.Quests.IsDailyComplete(WrongTurnQuestId) || !Bot.Quests.IsDailyComplete(EncroachingShadowsQuestId))
            myRequiredMask |= VoidDailiesBit;

        return myRequiredMask;
    }

    private static int CountUltras(int mask)
    {
        int count = 0;
        foreach (int bit in UltraBits)
        {
            if ((mask & bit) != 0)
                count++;
        }
        return count;
    }
}
