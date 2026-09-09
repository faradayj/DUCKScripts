/*
name: UltraDragov4
description: Ultra King Drago v4 — Taunter1AttackRightSummon1 + Taunter2AttackRightSummon2 + DPSAttackRightSummon1 + DPSAttackRightSummon2.
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

public class UltraDragov4
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

    private const string Taunter1AttackRightSummon1 = "Lord of Order";
    private const string Taunter2AttackRightSummon2 = "Verus DoomKnight";
    private const string DPSAttackRightSummon1 = "StoneCrusher";
    private const string DPSAttackRightSummon2 = "King's Echo";

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { Taunter1AttackRightSummon1 },
        new[] { Taunter2AttackRightSummon2 },
        new[] { DPSAttackRightSummon1 },
        new[] { DPSAttackRightSummon2 }
    };

    private CancellationTokenSource _tauntCts = new();
    private DateTime fightStartTime = DateTime.MinValue;
    private string _role = "";

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
            Prep();
            Fight();
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
        return className == Taunter1AttackRightSummon1 || className == Taunter2AttackRightSummon2;
    }

        private void EquipPresetClasses()
    {
        int armySize = 4;
        bool allowDuplicates = armySize > UltraClassesByRole.Length;

        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("drago", UltraClassesByRole);

        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "ultra_drago_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        // Clear stale class registrations from previous runs/other accounts
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("ultra_drago_class-v4.sync"));

        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        string? className = Bot.Player.CurrentClass?.Name;
        if (className == Taunter1AttackRightSummon1) _role = "Taunter1AttackRightSummon1";
        else if (className == Taunter2AttackRightSummon2) _role = "Taunter2AttackRightSummon2";
        else if (className == DPSAttackRightSummon1) _role = "DPSAttackRightSummon1";
        else _role = "DPSAttackRightSummon2";

        Enh.ApplyContext("drago");

        C.Logger($"[UltraDrago-v4] Role: {_role} ({className})");
    }

    private void Fight()
    {
        const string map = "ultradrago";
        const string boss = "King Drago";
        const string bossDefeatedTemp = "Drago Dethroned";
        const string leftSummon = "Executioner Dene";
        const string rightSummon = "Bowmaster Algie";

        const string waitSyncFile = "ultra_drago.sync";
        const string fightTimeSyncFile = "UltraDragoFightTime.sync";
        const string completionSyncFile = "UltraDragoCompletion.sync";
        int armySize = 4;

        const int questId = 8397;
        

        if (!Bot.Quests.IsUnlocked(questId))
            Bot.Quests.UpdateQuest(8395);

        if (!UltraGeneralv4.IsQuestGreen(Bot, questId))
            UltraGeneralv4.EnsureAcceptOnce(Bot, questId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(fightTimeSyncFile));
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));

        bool skipThird = IsTaunter();
        Pots.EnsureRecommendedPotions(skipThird: skipThird);
        Scrolls.GetScrollOfEnrage();

        C.Join("Whitemap");
        UltraWaitForArmyv4.Instance.NewWaitForArmy(armySize - 1, waitSyncFile, useSkill: false);

        Pots.UseRecommendedPotions(skipThird: skipThird, ensureStock: false);

        if (skipThird)
        {
            C.Logger("[UltraDrago-v4] Taunter detected, equipping Scroll of Enrage.");
            Engine.EquipEnrage();
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
        if (_role == "Taunter1AttackRightSummon1")
        {
            C.Logger("[UltraDrago-v4] Taunter1AttackRightSummon1 (Primary) — setting fight start time.");
            fightStartTime = UltraAsyncv4.SetFightTime(C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 0, cancellationToken: _tauntCts.Token);
        }
        else if (_role == "Taunter2AttackRightSummon2")
        {
            C.Logger("[UltraDrago-v4] Taunter2AttackRightSummon2 — reading fight start time.");
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
                C.Logger("King Drago defeated. Finishing quest.");
                Bot.Events.ScriptStopping -= StopTauntEvent;
                _tauntCts.Cancel();
                Engine.DisableSkills();
                Engine.Join(map);
                Ultra.PersistentJoinHouse();
                UltraGeneralv4.CompleteQuest(Bot, questId);
                Bot.Sleep(3000);
                break;
            }

            if (IsTaunter())
            {
                // Taunters — attack rightSummon normally;
                // 1s before taunt pulse, switch to leftSummon to taunt it (like Taunter3AttackBlade)
                double elapsed = (DateTime.UtcNow - fightStartTime).TotalSeconds;
                double timeInCycle = elapsed % 10;

                bool targetLeftSummon;
                if (_role == "Taunter1AttackRightSummon1")
                    // index 0: taunts at t=0,10,20... pre-switch at 9s, stay until 3s
                    targetLeftSummon = timeInCycle >= 9 || timeInCycle <= 3;
                else
                    // index 1: taunts at t=5,15,25... pre-switch at 4s, stay until 8s
                    targetLeftSummon = timeInCycle >= 4 && timeInCycle <= 8;

                if (targetLeftSummon)
                {
                    // Target leftSummon for taunt
                    if (Ultra.MonsterAlive(leftSummon))
                    {
                        if (Bot.Player.Target?.Name != leftSummon)
                            Bot.Combat.Attack(leftSummon);
                    }
                    else if (Ultra.MonsterAlive(rightSummon))
                    {
                        if (Bot.Player.Target?.Name != rightSummon)
                            Bot.Combat.Attack(rightSummon);
                    }
                    else
                    {
                        if (Bot.Player.Target?.Name != boss)
                            Bot.Combat.Attack(boss);
                    }
                }
                else
                {
                    // Attack rightSummon primarily
                    if (Ultra.MonsterAlive(rightSummon))
                    {
                        if (Bot.Player.Target?.Name != rightSummon)
                            Bot.Combat.Attack(rightSummon);
                    }
                    else if (Ultra.MonsterAlive(leftSummon))
                    {
                        if (Bot.Player.Target?.Name != leftSummon)
                            Bot.Combat.Attack(leftSummon);
                    }
                    else
                    {
                        if (Bot.Player.Target?.Name != boss)
                            Bot.Combat.Attack(boss);
                    }
                }
            }
            else
            {
                if (Ultra.MonsterAlive(rightSummon))
                {
                    if (Bot.Player.Target?.Name != rightSummon)
                        Bot.Combat.Attack(rightSummon);
                }
                else if (Ultra.MonsterAlive(leftSummon))
                {
                    if (Bot.Player.Target?.Name != leftSummon)
                        Bot.Combat.Attack(leftSummon);
                }
                else
                {
                    if (Bot.Player.Target?.Name != boss)
                        Bot.Combat.Attack(boss);
                }
            }
            Pots.ActivateEquippedPotion();

            Bot.Sleep(500);
        }
    }

}


