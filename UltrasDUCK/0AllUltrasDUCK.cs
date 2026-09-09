/*
name: All Ultras DUCK
description: Zero-config auto-assigned runner for all 12 DUCK Ultra scripts.
tags: ultra, army, coreduck, master
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include DUCKScripts\UltrasDUCK\CoreDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\Dailies\UltraEzrajalDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\Dailies\UltraWardenDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\Dailies\UltraEngineerDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\Dailies\UltraTyndariusDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\UltraDrakathDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\UltraDragoDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\UltraNulgathDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\UltraDageDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\UltraDarkonDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\UltraGramielDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\UltraSpeakerDUCK.cs
//cs_include DUCKScripts\UltrasDUCK\Dailies\UltraBataraKalaDUCK.cs
using System;
using System.Collections.Generic;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class AllUltrasDUCK
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "All Ultras DUCK";
    private const string RosterSyncFileName = "0AllUltrasDUCK_Roster.sync";
    private const string CompletionSyncFileName = "0AllUltrasDUCK_Completion.sync";
    private const int DefaultPrivateRoomNumber = 1245;

    private const int EzrajalBit = 1 << 0;
    private const int WardenBit = 1 << 1;
    private const int EngineerBit = 1 << 2;
    private const int TyndariusBit = 1 << 3;
    private const int DrakathBit = 1 << 4;
    private const int DragoBit = 1 << 5;
    private const int NulgathBit = 1 << 6;
    private const int DageBit = 1 << 7;
    private const int DarkonBit = 1 << 8;
    private const int GramielBit = 1 << 9;
    private const int SpeakerBit = 1 << 10;
    private const int KalaBit = 1 << 11;

    private static readonly int[] UltraBits =
    {
        EzrajalBit,
        WardenBit,
        EngineerBit,
        TyndariusBit,
        DrakathBit,
        DragoBit,
        NulgathBit,
        DageBit,
        DarkonBit,
        GramielBit,
        SpeakerBit,
        KalaBit,
    };

    private int partyRequiredMask;
    private int actualRunMask;
    private int remainingUltras;

    public string OptionsStorage = "0AllUltrasDUCK";
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
        int privateRoomNumber = DefaultPrivateRoomNumber;
        if (!Duck.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        int selectedMask = BuildSelectedMask();
        if (selectedMask == 0)
        {
            Core.Logger("No Ultras were selected.", LogPrefix);
            return;
        }

        if (!BuildPartyRequiredMask())
            return;

        actualRunMask = selectedMask & partyRequiredMask;
        remainingUltras = CountUltras(actualRunMask);

        Duck.FileLog($"Starting All Ultras DUCK with {remainingUltras} needed Ultras. actualRunMask={actualRunMask}", LogPrefix);
        Core.Logger($"Starting All Ultras DUCK with {remainingUltras} needed Ultras.", LogPrefix);

        if (remainingUltras > 1 && !PrepareOracle())
            return;

        // Dailies
        if (
            !RunSelectedUltra(
                "Ultra Ezrajal",
                EzrajalBit,
                () => new UltraEzrajalDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Warden",
                WardenBit,
                () => new UltraWardenDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Engineer",
                EngineerBit,
                () => new UltraEngineerDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Tyndarius",
                TyndariusBit,
                () => new UltraTyndariusDUCK().RunFromMaster(privateRoomNumber)
            )
            // Weeklies
            || !RunSelectedUltra(
                "Champion Drakath",
                DrakathBit,
                () => new UltraDrakathDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Drago",
                DragoBit,
                () => new UltraDragoDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Nulgath",
                NulgathBit,
                () => new UltraNulgathDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Dage",
                DageBit,
                () => new UltraDageDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Darkon",
                DarkonBit,
                () => new UltraDarkonDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Gramiel",
                GramielBit,
                () => new UltraGramielDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Speaker",
                SpeakerBit,
                () => new UltraSpeakerDUCK().RunFromMaster(privateRoomNumber)
            )
            || !RunSelectedUltra(
                "Ultra Batara Kala",
                KalaBit,
                () => new UltraBataraKalaDUCK().RunFromMaster(privateRoomNumber)
            )
        )
            return;

        Duck.FileLog("All Ultras DUCK run completed.", LogPrefix);
        Core.Logger("All Ultras DUCK run completed.", LogPrefix);
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
        Duck.FileLog("[PartySync] Discovering 4 active accounts for gauntlet...", LogPrefix);
        Core.Logger("[PartySync] Discovering 4 active accounts for gauntlet...", LogPrefix);
        string[]? discovered = Duck.DiscoverParty(RosterSyncFileName, armySize: 4);
        if (discovered == null)
            return false;

        Duck.FileLog($"[PartySync] Discovered party: [{string.Join(", ", discovered)}]. Starting master lockstep sync...", LogPrefix);
        Core.Logger($"[PartySync] Discovered party: [{string.Join(", ", discovered)}]. Starting master lockstep sync...", LogPrefix);
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
            for (int playerNumber = 1; playerNumber <= 4; playerNumber++)
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
        if (!Bot.Quests.IsDailyComplete(8152)) myRequiredMask |= EzrajalBit;
        if (!Bot.Quests.IsDailyComplete(8153)) myRequiredMask |= WardenBit;
        if (!Bot.Quests.IsDailyComplete(8154)) myRequiredMask |= EngineerBit;
        if (!Bot.Quests.IsDailyComplete(8245)) myRequiredMask |= TyndariusBit;
        if (!Bot.Quests.IsDailyComplete(8300)) myRequiredMask |= DrakathBit;
        if (!Bot.Quests.IsDailyComplete(8397)) myRequiredMask |= DragoBit;
        if (!Bot.Quests.IsDailyComplete(8692)) myRequiredMask |= NulgathBit;
        if (!Bot.Quests.IsDailyComplete(8547) || !Bot.Quests.IsDailyComplete(1678)) myRequiredMask |= DageBit;
        if (!Bot.Quests.IsDailyComplete(8746)) myRequiredMask |= DarkonBit;
        if (!Bot.Quests.IsDailyComplete(10301)) myRequiredMask |= GramielBit;
        if (!Bot.Quests.IsDailyComplete(9173)) myRequiredMask |= SpeakerBit;
        if (!Bot.Quests.IsDailyComplete(8216)) myRequiredMask |= KalaBit;
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

