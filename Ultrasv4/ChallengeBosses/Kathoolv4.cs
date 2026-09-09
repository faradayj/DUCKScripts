/*
name: Kathoolv4
description: Kill God of the Depths for Sacrosanct Morsel (Frenzy Feast daily).
tags: null
*/
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/GetScrollsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class Kathoolv4
{
    private static IScriptInterface Bot => IScriptInterface.Instance;
    private static CoreBots C => CoreBots.Instance;
    private static CoreEnginev4 Engine => CoreEnginev4.Instance;
    private static CoreUltrav4 Ultra => _Ultra ??= new CoreUltrav4();
    private static CoreUltrav4 _Ultra;
    private static UltraEnhancementsv4 Enh => _Enh ??= new UltraEnhancementsv4();
    private static UltraEnhancementsv4 _Enh;
    private static UltraPotionsv4 Pots => _Pots ??= new UltraPotionsv4();
    private static UltraPotionsv4 _Pots;
    private static GetScrollsv4 Scrolls => _Scrolls ??= new GetScrollsv4();
    private static GetScrollsv4 _Scrolls;
    private static string _fbsMuteFile = "";

    bool usePotions;
    public bool DontPreconfigure = true;
    public string OptionsStorage = "Kathoolv4";
    public List<IOption> Options = new()
    {
        new Option<int>("ArmySize", "Army Size", "How many players are in your army (including yourself).", 7),
        new Option<bool>("UsePotions", "Use Potions", "Enable buying and consuming recommended potions.", true),
        new Option<int>("PotionQuantity", "Potion Quantity", "How many potions to keep stocked.", 10),
        CoreBots.Instance.SkipOptions,
    };

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { "Verus DoomKnight" },
        new[] { "Lord of Order" },
        new[] { "Legion Revenant" },
        new[] { "StoneCrusher" },
        new[] { "ArchFiend" },
        new[] { "King's Echo" },
        new[] { "Quantum Chronomancer" }
    };

    public void ScriptMain(IScriptInterface bot)
    {
        RunBoss();
        Bot.StopSync();
    }

    public void RunBoss()
    {
        C.SetOptions(true);
        _fbsMuteFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Skua", "fbs_mute.sync"
        );
        try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }
        Engine.Boot();

        Bot.Flash.FlashCall += KathoolFlashListener;
        try
        {
            Prep();
            Fight();
        }
        finally
        {
            Bot.Flash.FlashCall -= KathoolFlashListener;
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
        }
    }

    private void EquipPresetClasses()
    {
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));
        bool allowDuplicates = armySize > UltraClassesByRole.Length || armySize >= 7;
        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("kathool", UltraClassesByRole);
        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "kathoolv4_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        C.Unbank("Legion Promotion");
        Bot.Drops.Add("Lineage of Devastation", "Soul Sand");
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("kathoolv4_class-v4.sync"));
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        usePotions = Bot.Config!.Get<bool>("UsePotions");

        Enh.ApplyContext("kathool");
        Bot.Sleep(2500);
    }

    private async Task UseVigilAsync()
    {
        for (int i = 0; i < 60; i++)
        {
            Engine.Cast(5);
            await Task.Delay(50);
        }
    }

    private void KathoolFlashListener(string name, object[] args)
    {
        try
        {
            if (name != "packetFromServer")
                return;

            dynamic? data = null;
            var packet = JsonConvert.DeserializeObject<dynamic>((string)args[0])!;
            data = packet?["b"]?["o"];

            if (data == null || data["cmd"]?.ToString() != "ct")
                return;

            if (data["anims"] != null)
            {
                foreach (var anim in data["anims"])
                {
                    if (anim?.msg != null)
                    {
                        string msg = (string)anim.msg;
                        if (msg.Contains("You cannot resist.", StringComparison.OrdinalIgnoreCase))
                        {
                            C.Logger("[Kathool/Flash] Detected 'You cannot resist.' — using Vigil.");
                            _ = UseVigilAsync();
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void Fight()
    {
        const string map = "kathooldepths";
        const string boss = "God of the Depths";
        const string bossDefeatedTemp = "Sacrosanct Morsel";

        const string waitSyncFile = "kathoolv4.sync";
        const string completionSyncFile = "KathoolCompletion.sync";
        int armySize = Math.Max(1, Bot.Config!.Get<int>("ArmySize"));

        
        const int questId = 9350;
        const int extraQuestId = 1677;

        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);
            
        if (!UltraGeneralv4.IsQuestGreen(Bot, extraQuestId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, extraQuestId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        int potionQuant = Bot.Config!.Get<int>("PotionQuantity");
        if (usePotions)
            Pots.EnsureRecommendedPotions(potionQuant, skipThird: true);

        Scrolls.BuyVigil();

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: false);

        if (usePotions)
            Pots.UseRecommendedPotions(potionQuant, skipThird: true, ensureStock: false);

        Scrolls.EquipVigil();

        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: true);

        Engine.ChooseBestCell(boss);
        Bot.Player.SetSpawnPoint();
        Bot.Sleep(2000);

        // Pre-seed completion sync file so all entries exist before the loop starts.
        string? _username = Bot.Player.Username;
        string? _className = Bot.Player.CurrentClass?.Name;
        if (!string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(_className))
        {
            string _myKey = $"{_username}|{_className}".Replace(":", "-");
            Ultra.UpdateEntry(Ultra.ResolveSyncPath(completionSyncFile), _myKey, "0");
        }

        while (!Bot.ShouldExit)
        {
            // Refresh mute file so FBS plugin stays muted during the fight
            try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }

            if (!Bot.Player.Alive)
            {
                Bot.Wait.ForTrue(() => Bot.Player.Alive, 20);
                continue;
            }

            if (Ultra.CheckArmyProgressBool(() => Bot.TempInv.Contains(bossDefeatedTemp, 1), completionSyncFile, armySize))
            {
                C.Logger("Boss defeated. Finishing quests.");
                Engine.DisableSkills();
                Engine.Join(map);
                Ultra.PersistentJoinHouse();
                UltraGeneralv4.CompleteQuest(Bot, questId);
                UltraGeneralv4.CompleteQuest(Bot, extraQuestId);
                Bot.Sleep(3000);
                break;
            }

            // Target priority: Tendril (MapID 3) > Tendril (MapID 1) > God of the Depths (MapID 2)
            int[] priority = { 3, 1, 2 };
            int targetMapId = 0;
            foreach (int mapId in priority)
            {
                var mon = Bot.Monsters.MapMonsters.FirstOrDefault(m => m != null && m.MapID == mapId && m.Alive);
                if (mon != null)
                {
                    targetMapId = mapId;
                    break;
                }
            }
            if (targetMapId > 0 && (Bot.Player.Target?.MapID != targetMapId || !Bot.Player.HasTarget))
                Bot.Combat.Attack(targetMapId);

            if (usePotions)
                Pots.ActivateEquippedPotion();
            Bot.Sleep(500);
        }
    }
}
