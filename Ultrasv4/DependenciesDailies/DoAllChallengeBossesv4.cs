/*
name: DoAllChallengeBossesv4
description: Runs all challenge boss dailies with shared queue — Queen Iona > Kolr > Kathool > Astral Empyrean.
tags: all, challenge, bosses, dailies, queeniona, kolr, kathool, astralempyrean
*/
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraQueuev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/PrerequisitesCheckerv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesDailies/QueenIonaNoOptionv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesDailies/KolrNoOptionv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesDailies/KathoolNoOptionv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesDailies/AstralEmpyreanNoOptionv4.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

public class DoAllChallengeBossesv4
{
    private static CoreEnginev4 Core => CoreEnginev4.Instance;
    private static CoreUltrav4 Ultra => _Ultra ??= new CoreUltrav4();
    private static CoreUltrav4 _Ultra;
    private CoreBots C => CoreBots.Instance;
    public IScriptInterface Bot => IScriptInterface.Instance;

    private const string BossParticipantSyncFile = "challengeboss_participants.sync";
    private const string BossSyncFile = "challengeboss_bosses.sync";

    public void ScriptMain(IScriptInterface bot)
    {
        C.SetOptions(true);
        Core.Boot();

        RunAll();

        Core.DisableSkills();
        C.SetOptions(false);
        Bot.StopSync();
    }

    public void RunAll()
    {
        if (!new PrerequisitesCheckerv4().PrerequisiteSyncGate(4))
            return;

        var allBosses = new[] { "QueenIonav4", "Kolr", "AstralEmpyrean", "Kathool" };

        int pass = 1;
        while (true)
        {
            var pending = GetSharedBossQueue(allBosses).ToList();
            if (!pending.Any())
                break;

            if (pass > 1)
                C.Logger($"[DoAllChallengeBossesv4] Re-run pass #{pass}: {pending.Count} boss(es) still pending.", "Info");

            RunBossQueue(pending);
            pass++;
        }

        C.Logger("[DoAllChallengeBossesv4] All Challenge Bosses Complete.");
    }

    private void RunBossQueue(IEnumerable<string> bosses)
    {
        foreach (string boss in bosses)
        {
            switch (boss)
            {
                case "QueenIonav4":
                    new QueenIonaNoOpv4().RunBoss();
                    break;
                case "Kolr":
                    new KolrNoOpv4().RunBoss();
                    break;
                case "Kathool":
                    new KathoolNoOpv4().RunBoss();
                    break;
                case "AstralEmpyrean":
                    new AstralEmpyreanNoOpv4().RunBoss();
                    break;
                default:
                    C.Logger($"Unknown challenge boss in queue: {boss}", "Error", true, true);
                    break;
            }
        }
    }

    private IEnumerable<string> GetSharedBossQueue(IEnumerable<string> bosses)
        => UltraQueuev4.GetSharedBossQueue(Ultra, Bot, C, bosses, BossSyncFile, BossParticipantSyncFile, IsBossComplete);

    private bool IsBossComplete(string boss)
    {
        (int id, string name) = boss switch
        {
            "QueenIonav4" => (9852, "Queen Iona"),
            "Kolr" => (10715, "Kolr, Usurper of Flames"),
            "Kathool" => (9350, "God of the Depths"),
            "AstralEmpyrean" => (9803, "Astral Empyrean"),
            _ => (0, string.Empty),
        };

        if (id == 0)
            return false;

        bool complete = Bot.Quests.IsDailyComplete(id);
        C.Logger($"{name} [{id}] dailyComplete={complete}", "Info");
        return complete;
    }
}
