/*
name: DoAll7Ultrasv4
description: Master combined runner for 7-player Ultras (Astral Empyrean, Deimos, Legion Lich Lord, The Beast, Kathool, and Kasuko).
tags: ultras, 7-player, astral empyrean, deimos, legion lich lord, the beast, kathool, kasuko
*/
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UnifiedQueuev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreStory.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraAsyncv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/GetScrollsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraDeathv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/ChallengeBosses/AstralEmpyreanv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/ChallengeBosses/Deimosv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/ChallengeBosses/LegionLichLordv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/ChallengeBosses/TheBeastv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/ChallengeBosses/Kathoolv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/ChallengeBosses/Kasukov4.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Models.Quests;
using Skua.Core.Options;

public class DoAll7Ultrasv4
{
    private static CoreEnginev4 Core => CoreEnginev4.Instance;
    private static CoreUltrav4 Ultra => _Ultra ??= new CoreUltrav4();
    private static CoreUltrav4 _Ultra;
    private CoreBots C => CoreBots.Instance;
    public IScriptInterface Bot => IScriptInterface.Instance;

    public bool DontPreconfigure = true;
    public string OptionsStorage = "DoAll7Ultrasv4";
    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself).", 7),
        new Option<bool>("UsePotions", "Use Potions", "Enable buying and consuming recommended potions.", true),
        new Option<int>("PotionQuantity", "Potion Quantity", "How many potions to keep stocked.", 10),
        CoreBots.Instance.SkipOptions,
    };

    private const string BossParticipantSyncFile = "ultrasv4_7_participants.sync";
    private const string BossSyncFile = "ultrasv4_7_bosses.sync";

    public void ScriptMain(IScriptInterface bot)
    {
        C.SetOptions(true);
        Core.Boot();

        RunAll();

        Ultra.PersistentJoinHouse();
        Core.DisableSkills();
        C.SetOptions(false);
        Bot.StopSync();
    }

    public void RunAll()
    {
        var allBosses = new[]
        {
            "AstralEmpyrean",
            "Deimos",
            "LegionLichLord",
            "TheBeast",
            "Kathool",
            "Kasuko"
        };
        int armySize = 7;

        int pass = 1;
        while (true)
        {
            var pending = GetSharedBossQueue(allBosses, armySize).ToList();
            if (!pending.Any())
                break;

            if (pass > 1)
                C.Logger($"[DoAll7Ultras-v4] Re-run pass #{pass}: {pending.Count} boss(es) still pending for some accounts.", "Info");

            if (pass > 3)
            {
                C.Logger($"[DoAll7Ultras-v4] Safety abort: Script looped {pass} times without finishing. Check if your inventory is full (max stack for quest rewards) or if a quest is failing to turn in!", "Error");
                break;
            }

            RunBossQueue(pending);
            pass++;
        }

        C.Logger("[DoAll7Ultras-v4] All 7-Player Bosses Complete.");
    }

    private void RunBossQueue(IEnumerable<string> bosses)
    {
        foreach (string boss in bosses)
        {
            switch (boss)
            {
                case "AstralEmpyrean":
                    C.Logger("[Sequence] Starting Astral Empyrean...");
                    new AstralEmpyreanv4().RunBoss();
                    break;
                case "Deimos":
                    C.Logger("[Sequence] Starting Deimos...");
                    new Deimosv4().RunBoss();
                    break;
                case "LegionLichLord":
                    C.Logger("[Sequence] Starting Legion Lich Lord...");
                    new LegionLichLordv4().RunBoss();
                    break;
                case "TheBeast":
                    C.Logger("[Sequence] Starting The Beast...");
                    new TheBeastv4().RunBoss();
                    break;
                case "Kathool":
                    C.Logger("[Sequence] Starting Kathool...");
                    new Kathoolv4().RunBoss();
                    break;
                case "Kasuko":
                    C.Logger("[Sequence] Starting Kasuko...");
                    new Kasukov4().RunBoss();
                    break;
                default:
                    C.Logger($"Unknown Ultra boss in queue: {boss}", "Error", true, true);
                    break;
            }
        }
    }

    private IEnumerable<string> GetSharedBossQueue(IEnumerable<string> bosses, int armySize)
        => UnifiedQueuev4.GetSharedBossQueue(Ultra, Bot, C, bosses, BossSyncFile, BossParticipantSyncFile, IsBossComplete, armySize);

    private bool IsBossComplete(string boss)
    {
        (int id, string name) = boss switch
        {
            "AstralEmpyrean" => (9803, "Astral's Supernova"),
            "Deimos" => (1676, "Deimos (Lineage of Devastation)"),
            "LegionLichLord" => (1674, "Legion Lich Lord"),
            "TheBeast" => (1675, "The Beast"),
            "Kathool" => (9350, "Kathool (Frenzy Feast)"),
            "Kasuko" => (9254, "Molten Heart"),
            _ => (0, string.Empty),
        };

        if (id == 0)
            return false;

        bool complete = Bot.Quests.IsDailyComplete(id);
        LogBossQuestStatus(name, id, complete);
        return complete;
    }

    private void LogBossQuestStatus(string name, int questId, bool complete)
    {
        Quest? quest = Bot.Quests.EnsureLoad(questId);
        int slot = quest?.Slot ?? 0;
        int value = quest?.Value ?? 0;
        bool active = quest?.Active ?? false;
        int questValue = slot > 0 ? Bot.Flash.CallGameFunction<int>("world.getQuestValue", slot) : 0;

        C.Logger(
            $"{name} [{questId}] complete={complete} " +
            $"daily={Bot.Quests.IsDailyComplete(questId)} " +
            $"progress={Bot.Quests.IsInProgress(questId)} " +
            $"active={active} " +
            $"everCompleted={Bot.Quests.HasBeenCompleted(questId)} " +
            $"slot={slot} value={value} questValue={questValue}",
            "Info"
        );
    }
}
