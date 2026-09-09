/*
name: Army Prismatas
description: Farms gold using the Prismatas in /archmage and selling the elemental bindings
tags: Prismatas, elemental binding, gold, farm
*/

//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreStory.cs
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class ArmyPristmasv4
{
    private static CoreAdvanced Adv
    {
        get => _Adv ??= new CoreAdvanced();
        set => _Adv = value;
    }
    private CoreBots C => CoreBots.Instance;
    private static CoreAdvanced _Adv;
    private static CoreBots _sCore;
    public IScriptInterface Bot => IScriptInterface.Instance;
    public CoreEnginev4 Core = new();
    public CoreUltrav4 Ultra = new();
    private static CoreBots sCore
    {
        get => _sCore ??= new CoreBots();
        set => _sCore = value;
    }

    private static UltraEnhancementsv4 Enh
    {
        get => _Enh ??= new UltraEnhancementsv4();
        set => _Enh = value;
    }
    private static UltraEnhancementsv4 _Enh;

    public string OptionsStorage = "ArmyPristmas-v2";
    public bool DontPreconfigure = true;

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { "Verus DoomKnight" },
        new[] { "Lord of Order" },
        new[] { "Legion Revenant" },
        new[] { "Quantum Chronomancer" },
        new[] { "StoneCrusher" },
        new[] { "ArchFiend" },
        new[] { "King's Echo" }
    };

    public List<IOption> Options = new()
    {
        new Option<int>(
            "ArmySize",
            "Army Size",
            "How many players are in your army (including yourself).",
            4
        ),

        new Option<bool>(
            "DoEnh",
            "Do Enhancements",
            "Auto-enhance gear properly for the fight before killing Prismatas.",
            true
        ),

        new Option<bool>(
            "SellEvery100",
            "Sell Every 100",
            "Enable to sell Elemental Binding every 100. Disable to keep them.",
            true
        ),
        new Option<bool>(
            "StopAtMaxGold",
            "Stop at Max Gold",
            "Enable to stop when reaching 100M gold. Disable to continue farming.",
            true
        ),
        CoreBots.Instance.SkipOptions,
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        C.BankingBlackList.AddRange(new[] { "Elemental Binding" });
        C.SetOptions(true);
        C.Logger("Elemental Bindings will be sold every 100\nClass presets will be applied if configured.");
        Core.Boot();
        try
        {
            Prep();
            KillPrismatas();
        }
        finally
        {
            C.SetOptions(false);
            Bot.StopSync();
        }
    }

    private void Prep()
    {
        C.Logger("Preparing Army Prismatas...");
        EquipPresetClasses();

        if (Bot.Config!.Get<bool>("DoEnh"))
            DoEnhs();
    }

    void KillPrismatas()
    {
        const string map = "archmage";
        const string boss = "Prismata";
        string syncPath = Ultra.ResolveSyncPath("ArmyBool.sync");
        Ultra.ClearSyncFile(syncPath);
        Bot.Sleep(2500);

        // Wait for the army in a safe map before entering the boss area.
        Core.Join("whitemap");
        Bot.Wait.ForMapLoad("whitemap");

        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
        if (armySize > 1)
            Ultra.WaitForArmy(armySize - 1, "ArmyPrismatas.sync", 3000, 500, 10000);
        Core.Join(map);
        C.AddDrop("Elemental Binding");
        var (bestCell, bestPad) = Core.ChooseBestCell(boss);

        Bot.Player.SetSpawnPoint();
        Bot.Sleep(1500);

        bool sellEvery100 = Bot.Config!.Get<bool>("SellEvery100");
        bool stopAtMaxGold = Bot.Config!.Get<bool>("StopAtMaxGold");
        while (!Bot.ShouldExit)
        {
            if (stopAtMaxGold && Ultra.CheckArmyProgressBool(() => Bot.Player.Gold >= 100000000, syncPath))
            {
                Bot.Options.AggroMonsters = false;
                C.Jump("Enter", "Spawn");
                C.Logger("All players finished farm.");
                break;
            }
            // Dead → wait for respawn
            if (!Bot.Player.Alive)
            {
                Bot.Wait.ForTrue(() => Bot.Player.Alive, 20);
                continue;
            }

            if (Bot.Player.Cell != "r2")
            {
                Bot.Map.Jump("r2", "Left");
                Bot.Wait.ForCellChange("r2");
            }

            Bot.Combat.Attack(GetPrismataTargetMapID());

            if (!C.GoldMaxed && sellEvery100 && C.CheckInventory("Elemental Binding", 100))
            {
                C.SellItem("Elemental Binding", all: true);
            }

            Bot.Sleep(500);
        }
    }

    private void EquipPresetClasses()
    {
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
        bool allowDuplicates = armySize > UltraClassesByRole.Length || armySize >= 7;
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("prismatas", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "army_prismatas_class-v4.sync", allowDuplicates);
    }

    private int GetPrismataTargetMapID()
    {
        var currentTarget = Bot.Player.Target;
        if (currentTarget != null && currentTarget.Alive && currentTarget.HP > 0)
            return currentTarget.MapID;

        var nextTarget = Bot.Monsters.CurrentAvailableMonsters
            .Where(m => m != null && m.Alive && m.HP > 0)
            .OrderBy(m => m.MapID)
            .FirstOrDefault();

        return nextTarget?.MapID ?? 1;
    }

    private void DoEnhs()
    {
        Enh.ApplyContext("prismatas");
    }
}
