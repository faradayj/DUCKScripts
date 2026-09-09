/*
name: UltraNulgathv4
description: Ultra Nulgath v4 — 3 taunters + 1 DPSAttackBlade with pulse-driven taunt and army sync.
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
using Skua.Core.Interfaces;

public class UltraNulgathv4
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
    private const string Taunter1 = "Lord of Order";
    private const string Taunter2 = "StoneCrusher";
    private const string Taunter3AttackBlade = "Verus DoomKnight";
    private const string DPSAttackBlade = "King's Echo";

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { Taunter1 },
        new[] { Taunter2 },
        new[] { Taunter3AttackBlade },
        new[] { DPSAttackBlade }
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

    private bool IsTaunter() => _role != "DPSAttackBlade";

    private void EquipPresetClasses()
    {
        int armySize = 4;
        bool allowDuplicates = armySize > UltraClassesByRole.Length;

        SlotRequirement[] slotRequirements = UltraEnhancementsv4.GetSlotRequirements("nulgath", UltraClassesByRole);

        UltraCustomClassSyncv4.CustomClassSync(Ultra, Bot, slotRequirements, armySize, "ultra_nulgath_class-v4.sync", allowDuplicates);
    }

    private void Prep()
    {
        // Clear stale class registrations from previous runs/other accounts
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath("ultra_nulgath_class-v4.sync"));

        UltraGeneralv4.EquipWarriorClass();
        Bot.Sleep(2000);
        EquipPresetClasses();
        Bot.Sleep(2000);

        // Determine role based on equipped class
        string? className = Bot.Player.CurrentClass?.Name;
        if (className == Taunter1) _role = "Taunter1";
        else if (className == Taunter2) _role = "Taunter2";
        else if (className == Taunter3AttackBlade) _role = "Taunter3AttackBlade";
        else _role = "DPSAttackBlade";

        Enh.ApplyContext("nulgath");

        C.Logger($"[UltraNulgath-v4] Role: {_role} ({className})");
    }

    private void Fight()
    {
        const string map = "ultranulgath";
        const string boss = "Nulgath the Archfiend";
        const string bossDefeatedTemp = "Nulgath the Archfiend Defeated?";

        const string waitSyncFile = "ultra_nulgath.sync";
        const string fightTimeSyncFile = "UltraNulgathFightTime.sync";
        const string completionSyncFile = "UltraNulgathCompletion.sync";
        int armySize = 4;

        const int questId = 8692;

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
            C.Logger("[UltraNulgath-v4] Taunter detected, equipping Scroll of Enrage.");
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

        Func<bool> shouldSkipTaunt = () => Engine.HasAura("Contract of Despair", true);

        // Set or retrieve fight start time, then launch the appropriate taunter loop
        if (_role == "Taunter1")
        {
            C.Logger("[UltraNulgath-v4] Taunter1 (Primary) — setting fight start time.");
            fightStartTime = UltraAsyncv4.SetFightTime(C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 0, 3, shouldSkipTaunt, cancellationToken: _tauntCts.Token);
        }
        else if (_role == "Taunter2")
        {
            C.Logger("[UltraNulgath-v4] Taunter2 — reading fight start time.");
            fightStartTime = UltraAsyncv4.GetFightTime(Ultra, C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 1, 3, shouldSkipTaunt, cancellationToken: _tauntCts.Token);
        }
        else if (_role == "Taunter3AttackBlade")
        {
            C.Logger("[UltraNulgath-v4] Taunter3AttackBlade — reading fight start time.");
            fightStartTime = UltraAsyncv4.GetFightTime(Ultra, C, fightTimeSyncPath);
            UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 2, 3, shouldSkipTaunt, cancellationToken: _tauntCts.Token);
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
                C.Logger("Nulgath the Archfiend defeated. Finishing quest.");
                Bot.Events.ScriptStopping -= StopTauntEvent;
                _tauntCts.Cancel();
                Engine.DisableSkills();
                Engine.Join(map);
                Ultra.PersistentJoinHouse();
                UltraGeneralv4.CompleteQuest(Bot, questId);
                Bot.Sleep(3000);
                break;
            }

            // ── Dynamic targeting ──
            // Taunter1/Taunter2: always attack Nulgath; DPSAttackBlade: attack Blade
            // Taunter3: attack Blade normally, switch to Nulgath during taunt pulse;
            //           if Blade is dead, fall back to Nulgath
            if (_role == "Taunter3AttackBlade" && !shouldSkipTaunt())
            {
                double elapsed = (DateTime.UtcNow - fightStartTime).TotalSeconds;
                double timeInCycle = elapsed % 15;
                // Taunter3 fires at t=10,25,40... (index 2, 5s interval, 15s cycle)
                // Pre-switch to Nulgath at 9s (1s early), stay until 13s (taunt ~3s)
                bool targetNulgath = timeInCycle >= 9 && timeInCycle <= 13;

                if (targetNulgath)
                {
                    if (Bot.Player.Target?.MapID != 2)
                        Bot.Combat.Attack(2);
                }
                else
                {
                    // Attack Blade (MapID 1) if alive, else fall back to Nulgath (MapID 2)
                    if (Bot.Monsters.CurrentAvailableMonsters.Any(x => x != null && x.MapID == 1 && x.HP > 0))
                    {
                        if (Bot.Player.Target?.MapID != 1)
                            Bot.Combat.Attack(1);
                    }
                    else if (Bot.Player.Target?.MapID != 2)
                    {
                        Bot.Combat.Attack(2);
                    }
                }
            }
            else if (_role == "DPSAttackBlade")
            {
                // DPSAttackBlade — attack Blade (MapID 1) if alive, else Nulgath (MapID 2)
                if (Bot.Monsters.CurrentAvailableMonsters.Any(x => x != null && x.MapID == 1 && x.HP > 0))
                {
                    if (Bot.Player.Target?.MapID != 1)
                        Bot.Combat.Attack(1);
                }
                else if (Bot.Player.Target?.MapID != 2)
                {
                    Bot.Combat.Attack(2);
                }
            }
            else
            {
                // Taunter1, Taunter2 — always on Nulgath
                if (Bot.Player.Target?.Name != boss)
                    Bot.Combat.Attack(boss);
            }

            Pots.ActivateEquippedPotion();

            Bot.Sleep(500);
        }
    }
}


