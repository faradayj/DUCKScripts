/*
name: Kolrv4  No Op
description: Farm Kolr, Usurper of Flames for Choronzonite. No config needed. It needs 4 clients, it will go to the map 2/2 2/2.
tags: null
*/
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
using System;
using System.IO;
using System.Linq;
using Skua.Core.Interfaces;

public class KolrNoOpv4
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
    private static string _fbsMuteFile = "";

    private const string Group1Dps1 = "King's Echo";
    private const string Group1Dps2 = "Lord of Order";
    private const string Group2Dps1 = "King's Echo";
    private const string Group2Dps2 = "Lord of Order";

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { Group1Dps1 },
        new[] { Group1Dps2 },
        new[] { Group2Dps1 },
        new[] { Group2Dps2 }
    };

    private static int _roomOffset = 0;

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

        try
        {
            Prep();
            Fight();
        }
        finally
        {
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
        }
    }

        private void EquipPresetClasses()
    {
        int armySize = 4;
        bool allowDuplicates = armySize > UltraClassesByRole.Length;

        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("kolr", UltraClassesByRole);

        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "kolrv4_class.sync", allowDuplicates);
    }

    private void AssignGroup(int armySize)
    {
        string username = Bot.Player.Username;
        string groupSync = Ultra.ResolveSyncPath("kolrv4_group.sync");

        Ultra.UpdateEntry(groupSync, username, "1");
        Bot.Sleep(500);

        while (!Bot.ShouldExit)
        {
            string[] lines = Ultra.ReadLines(groupSync);
            var valid = lines
                .Select(l => l.Split(':')[0])
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (valid.Count >= armySize)
                break;
            Ultra.UpdateEntry(groupSync, username, "1");
            Bot.Sleep(500);
        }

        var accounts = Ultra.ReadLines(groupSync)
            .Select(l => l.Split(':')[0])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int myIndex = accounts.FindIndex(a => string.Equals(a, username, StringComparison.OrdinalIgnoreCase));
        int half = armySize / 2;
        _roomOffset = (myIndex / half) * 10;

        C.Logger($"[Kolrv4] Assigned to Group{(myIndex / half) + 1} (room offset: +{_roomOffset})");
    }

    private void Prep()
    {
        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        Enh.ApplyContext("kolr");

        Bot.Sleep(2500);
    }

    private void Fight()
    {
        const string boss = "Kolr, Usurper of Flames";
        const string bossDefeatedTemp = "Choronzonite";

        const string waitSyncFile = "kolrv4.sync";
        const string completionSyncFile = "Kolrv4Completion.sync";
        const int armySize = 4;

        const int questId = 10715;

        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        const int potionQuant = 10;
        Pots.EnsureRecommendedPotions(potionQuant, skipThird: false, context: "Kolr");

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: false);

        Pots.UseRecommendedPotions(potionQuant, skipThird: false, context: "Kolr", ensureStock: false);

        C.Join($"flameusurper-{C.PrivateRoomNumber + _roomOffset}");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: true);

        Engine.ChooseBestCellOneMonster(boss);
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
                C.Join($"flameusurper-{C.PrivateRoomNumber + _roomOffset}");
                Ultra.PersistentJoinHouse();
                UltraGeneralv4.CompleteQuest(Bot, questId);
                Bot.Sleep(3000);
                break;
            }

            // Default — attack the boss
            if (Bot.Player.Target?.Name != boss)
                Bot.Combat.Attack(boss);

            Pots.ActivateEquippedPotion();
            Bot.Sleep(500);
        }
    }
}

