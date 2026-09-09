/*
name: UltraDagev4
description: Ultra Dage v4 — pulse-driven dual taunter with zone movement handler and army sync.
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
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraAsyncv4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Skua.Core.Interfaces;

public class UltraDagev4
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

    private const string Taunter1 = "Verus DoomKnight";
    private const string Taunter2 = "ArchPaladin";
    private const string Decayer = "Lord of Order";
    // DPS classes
    private const string Dps1 = "King's Echo";

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { Taunter1 },
        new[] { Taunter2 },
        new[] { Decayer },
        new[] { Dps1 }
    };

    private CancellationTokenSource _tauntCts = new();
    private DateTime fightStartTime = DateTime.MinValue;

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
        _tauntCts = new();
        Bot.Events.ScriptStopping -= StopTauntEvent;
        Bot.Events.ScriptStopping += StopTauntEvent;

        try
        {
            Bot.Events.ExtensionPacketReceived += UltraDageListener;
            try
            {
                Prep();
                Fight();
            }
            finally
            {
                Bot.Events.ExtensionPacketReceived -= UltraDageListener;
            }
        }
        finally
        {
            Bot.Events.ScriptStopping -= StopTauntEvent;
            _tauntCts.Cancel();
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
        }
    }

    private bool StopTauntEvent(Exception? e)
    {
        _tauntCts.Cancel();
        return true;
    }

    private bool IsTaunter()
    {
        string? className = Bot.Player.CurrentClass?.Name;
        return className == Taunter1 || className == Taunter2;
    }

    private bool IsDecayer()
    {
        string? className = Bot.Player.CurrentClass?.Name;
        return className == Decayer;
    }

        private void EquipPresetClasses()
    {
        int armySize = 4;
        bool allowDuplicates = armySize > UltraClassesByRole.Length;

        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("dage", UltraClassesByRole);

        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "ultra_dage_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        C.Unbank("Legion Promotion");
        Bot.Drops.Add("Lineage of Devastation", "Soul Sand");
        // Clear stale class registrations from previous runs/other accounts
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("ultra_dage_class-v4.sync"));

        if (!C.isCompletedBefore(793))

        Bot.Quests.UpdateQuest(793);

        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        Enh.ApplyContext("dage");

        string? className = Bot.Player.CurrentClass?.Name;
        C.Logger($"[UltraDage-v4] Role: {className}");
    }

    private void Fight()
    {
        const string map = "ultradage";
        const string boss = "Dage the Dark Lord";
        const string bossDefeatedTemp = "Dage the Dark Lord Defeated";

        const string waitSyncFile = "ultra_dage.sync";
        const string fightTimeSyncFile = "UltraDageFightTime.sync";
        const string completionSyncFile = "UltraDageCompletion.sync";
        int armySize = 4;

        const int questId = 8547;
        const int extraQuestId = 1678;
        
        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);
            
        if (!UltraGeneralv4.IsQuestGreen(Bot, extraQuestId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, extraQuestId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(fightTimeSyncFile));
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        bool skipThird = IsTaunter();
        Pots.EnsureRecommendedPotions(skipThird: skipThird, context: "Dage");
        Scrolls.GetScrollOfEnrage();
        Scrolls.GetScrollOfDecay();

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: false);

        Pots.UseRecommendedPotions(skipThird: skipThird, ensureStock: false, context: "Dage");

        if (skipThird)
        {
            C.Logger("[UltraDage-v4] Taunter detected, equipping Scroll of Enrage.");
            Engine.EquipEnrage();
        }

        if (IsDecayer())
        {
            C.Logger("[UltraDage-v4] Decayer detected, equipping Scroll of Decay.");

            Engine.EquipDecay();
        }

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

        string fightTimeSyncPath = Ultra.ResolveSyncPath(fightTimeSyncFile);

        // Set or retrieve fight start time, then launch the appropriate taunter loop
        string? className = Bot.Player.CurrentClass?.Name;
        if (className == Taunter1)
        {
            C.Logger("[UltraDage-v4] Taunter1 (Primary) — setting fight start time.");
            fightStartTime = UltraAsyncv4.SetFightTime(C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 0, cancellationToken: _tauntCts.Token);
        }
        else if (className == Taunter2)
        {
            C.Logger("[UltraDage-v4] Taunter2 (Secondary) — reading fight start time.");
            fightStartTime = UltraAsyncv4.GetFightTime(Ultra, C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 1, cancellationToken: _tauntCts.Token);
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
                C.Logger("Dage the Dark Lord defeated. Finishing quests.");
                Bot.Events.ScriptStopping -= StopTauntEvent;
                _tauntCts.Cancel();
                Engine.DisableSkills();
                Engine.Join(map);
                Ultra.PersistentJoinHouse();
                UltraGeneralv4.CompleteQuest(Bot, questId);
                UltraGeneralv4.CompleteQuest(Bot, extraQuestId);
                Bot.Sleep(3000);
                break;
            }

            Bot.Combat.Attack(boss);
            Pots.ActivateEquippedPotion();

            // Decayer logic — Verus DoomKnight spams skill 5 when Legionnaire aura is active
            if (IsDecayer() && Engine.HasAura("Legionnaire") && Engine.Left("Legionnaire", 12))
                _ = DecayAsync(boss);

            Bot.Sleep(500);
        }
    }

    public async void UltraDageListener(dynamic packet)
    {
        if (packet?["params"]?.type?.ToString() != "json")
            return;

        if (!Bot.Player.Alive)
            return;

        dynamic data = packet["params"].dataObj;

        if (data?.cmd?.ToString() != "event")
            return;

        string? zoneSet = data?.args?.zoneSet?.ToString();

        if (!string.IsNullOrEmpty(zoneSet))
        {
            int targetX =
                zoneSet.Equals("A", StringComparison.OrdinalIgnoreCase)
                    ? 122
                    : zoneSet.Equals("B", StringComparison.OrdinalIgnoreCase)
                        ? 856
                        : 0;

            if (targetX != 0)
            {
                _ = Task.Run(() =>
                {
                    Bot.Player.WalkTo(targetX, 420);

                    Bot.Wait.ForTrue(
                        () => Math.Abs(Bot.Player.X - targetX) < 40,
                        10
                    );

                    Bot.Sleep(2000);
                });

                return;
            }
        }

        if (string.IsNullOrEmpty(zoneSet))
        {
            _ = Task.Run(() =>
            {
                Bot.Sleep(5000);

                int middleX = 500;

                Bot.Player.WalkTo(middleX, 420);

                Bot.Wait.ForTrue(
                    () => Math.Abs(Bot.Player.X - middleX) < 40,
                    10
                );
            });

            return;
        }
    }

    private async Task DecayAsync(string bossName)
    {
        for (int i = 0; i < 60 && !Bot.ShouldExit; i++)
        {
            if (!Bot.Player.Alive)
                break;

            if (!Bot.Player.HasTarget)
                Bot.Combat.Attack(bossName);

            Engine.Cast(5);
            await Task.Delay(50);
        }
    }
}


