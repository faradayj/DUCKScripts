/*
name: UltraEzrajalv4
description: Ultra Ezrajal v4 — handles Counter Attack windows with army sync. Uses synced class equip and no-taunt fight flow.
tags: null
*/
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraEnhancementsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraPotionsv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraGeneralv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs

using System;
using System.IO;
using System.Linq;
using Skua.Core.Interfaces;

public class UltraEzrajalv4
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

    private const string Dps1 = "Verus DoomKnight";
    private const string Dps2 = "StoneCrusher";
    private const string Dps3 = "Lord of Order";
    private const string Dps4 = "King's Echo";

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

        Bot.UltraBossHelper.EnableCounterAttack();

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

        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("ezrajal", UltraClassesByRole);

        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "ultra_ezrajal_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        // Clear stale class registrations from previous runs/other accounts
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("ultra_ezrajal_class-v4.sync"));

        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        Enh.ApplyContext("ezrajal");
    }

    private void Fight()
    {
        const string map = "ultraezrajal";
        const string boss = "Ultra Ezrajal";
        const string bossDefeatedTemp = "Ultra Ezrajal Defeated";

        const string waitSyncFile = "ultra_ezrajal.sync";
        const string completionSyncFile = "UltraEzrajalCompletion.sync";
        int armySize = 4;

        
        const int questId = 8152;

        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        Pots.EnsureRecommendedPotions(skipThird: false);

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: false);

        Pots.UseRecommendedPotions(skipThird: false, ensureStock: false);

        Engine.Join(map);
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: true);

        Engine.ChooseBestCell(boss);
        Bot.Player.SetSpawnPoint();
        Bot.Sleep(2000);

        // Pre-seed completion sync file so all 4 entries exist before the loop starts.
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
                C.Logger("Ultra Ezrajal defeated. Finishing quest.");
                Engine.DisableSkills();
                Engine.Join(map);
                Bot.UltraBossHelper.DisableCounterAttack();
                Ultra.PersistentJoinHouse();
                UltraGeneralv4.CompleteQuest(Bot, questId);
                Bot.Sleep(3000);
                break;
            }

            // Counter Attack handler — stop attacking when Ezrajal reflects
            if (Bot.Player.HasTarget
                && Bot.Target?.Auras?.Any(a => a != null && a.Name == "Counter Attack") == true)
            {
                Bot.Combat.CancelAutoAttack();
                Bot.Sleep(6300);
            }
            else
            {
                Bot.Combat.Attack(boss);
            }

            Pots.ActivateEquippedPotion();
            Bot.Sleep(500);
        }
    }
}


