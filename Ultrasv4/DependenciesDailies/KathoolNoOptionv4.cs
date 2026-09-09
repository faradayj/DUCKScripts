/*
name: Kathoolv4 No Op
description: Kill God of the Depths for Sacrosanct Morsel (Frenzy Feast daily). No config needed.
tags: null
*/
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/GetScrollsv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Skua.Core.Interfaces;

public class KathoolNoOpv4
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

    private const string Dps1 = "Verus DoomKnight";
    private const string Dps2 = "King's Echo";
    private const string Dps3 = "Lord of Order";
    private const string Dps4 = "StoneCrusher";

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { Dps1 },
        new[] { Dps2 },
        new[] { Dps3 },
        new[] { Dps4 }
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
        int armySize = 4;
        bool allowDuplicates = armySize > UltraClassesByRole.Length;

        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("default", UltraClassesByRole);

        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "kathoolv4_class.sync", allowDuplicates);
    }

    private void Prep()
    {
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        Enh.ApplyContext("kathool");

        Scrolls.BuyVigil();
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
                            C.Logger("[Kathool] Detected 'You cannot resist.' — using Vigil.");
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
        const string completionSyncFile = "Kathoolv4Completion.sync";
        int armySize = 4;

        const int questId = 9350;

        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        const int potionQuant = 10;
        Pots.EnsureRecommendedPotions(potionQuant, skipThird: true);

        Scrolls.BuyVigil();

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: false);

        Pots.UseRecommendedPotions(potionQuant, skipThird: true, ensureStock: false);
        Scrolls.EquipVigil();

        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: true);

        Engine.ChooseBestCell(boss);
        Bot.Player.SetSpawnPoint();
        Bot.Sleep(2000);

        // Pre-seed completion sync file
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

            if (Ultra.CheckArmyProgressBool(() => Bot.TempInv.Contains(bossDefeatedTemp, 1), completionSyncFile))
            {
                C.Logger("Boss defeated. Finishing quest.");
                Engine.DisableSkills();
                Engine.Join(map);
                Ultra.PersistentJoinHouse();
                UltraGeneralv4.CompleteQuest(Bot, questId);
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

            Pots.ActivateEquippedPotion();
            Bot.Sleep(500);
        }
    }
}

