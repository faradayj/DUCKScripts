/*
name: Test Class DUCK
description: Auto-enhances currently equipped class and tests its custom CoreDUCK skillset in selected testing environment (classhall, classhall2, celestialarenad, icestormunder, necrodungeon, ultradrakath, bosschallenge, or sevencircleswar).
tags: test, class, skillset, coreduck, dummy, boss dummy, aranx, frost spirit, doom overlord, champion of chaos, ancient horror, wrath guard
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Options;
#nullable enable

public class TestClassDUCK
{
    public enum TestMode
    {
        Classhall,
        Classhall2,
        Celestialarenad,
        Icestormunder,
        Necrodungeon,
        Ultradrakath,
        Bosschallenge,
        Sevencircleswar
    }

    public enum ArachnomancerMode
    {
        Solo_Dauntless,
        Default_Ravenous
    }

    public enum LightCasterMode
    {
        Default,
        Healing
    }

    public enum ChronoShadowHunterMode
    {
        Stable,
        Gunslinger
    }

    public enum ChaosSlayerMode
    {
        Solo,
        Farm
    }

    public enum ChaosAvengerMode
    {
        Optimized,
        Default
    }

    public enum ShamanMode
    {
        Solo_Boss,
        Farm
    }

    public enum AbyssalAngelMode
    {
        Default_Survival,
        Farm
    }

    public enum FrostvalBarbarianMode
    {
        Default,
        Farm
    }

    public enum LegionSwordMasterAssassinMode
    {
        Solo,
        Farm
    }

    public enum QuantumChronomancerMode
    {
        Lament_Cape,
        Penitence_Cape
    }

    public enum ArchMageMode
    {
        Corporeal_Farming,
        Astral_Solo
    }

    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    public bool DontPreconfigure = true;
    public string OptionsStorage = "TestClassDUCK";
    public List<IOption> Options = new()
    {
        new Option<TestMode>(
            "TestMode",
            "Test Mode",
            "Select combat testing map and target monster.",
            TestMode.Classhall
        ),
        new Option<ArchMageMode>(
            "ArchMageMode",
            "ArchMage Mode",
            "Corporeal (Farming) or Astral (Solo).",
            ArchMageMode.Corporeal_Farming
        ),
        new Option<ArachnomancerMode>(
            "ArachnomancerMode",
            "Arachnomancer Mode",
            "Solo (Dauntless/Anima/Lament + Timed Auras) or Default (Ravenous/Forge/Vainglory/Simple).",
            ArachnomancerMode.Solo_Dauntless
        ),
        new Option<LightCasterMode>(
            "LightCasterMode",
            "LightCaster Mode",
            "Default (Simple DPS: 2, 1, 3, 4) or Healing (Party Healer: 2, 1, 4).",
            LightCasterMode.Default
        ),
        new Option<ChronoShadowHunterMode>(
            "ChronoShadowHunterMode",
            "Chrono ShadowHunter Mode",
            "Stable (Safe Rotation) or Gunslinger (High-Risk Burst).",
            ChronoShadowHunterMode.Stable
        ),
        new Option<ChaosSlayerMode>(
            "ChaosSlayerMode",
            "Chaos Slayer Mode",
            "Solo (3, 2, 4, 1) or Farm (2, 4 with safe skill 3).",
            ChaosSlayerMode.Solo
        ),
        new Option<ChaosAvengerMode>(
            "ChaosAvengerMode",
            "Chaos Avenger Mode",
            "Optimized (Fury/Taunt Engine) or Default (Simple Spam).",
            ChaosAvengerMode.Optimized
        ),
        new Option<ShamanMode>(
            "ShamanMode",
            "Shaman Mode",
            "Solo_Boss (Hydrophobic Flow Engine) or Farm (Simple Spam).",
            ShamanMode.Solo_Boss
        ),
        new Option<AbyssalAngelMode>(
            "AbyssalAngelMode",
            "Abyssal Angel Mode",
            "Default_Survival (Skill 3 Safe Heal) or Farm (Pure 4 & 2 Spam).",
            AbyssalAngelMode.Default_Survival
        ),
        new Option<FrostvalBarbarianMode>(
            "FrostvalBarbarianMode",
            "Frostval Barbarian Mode",
            "Default (1, 2, 3) or Farm (4, 1, 2, 3).",
            FrostvalBarbarianMode.Default
        ),
        new Option<LegionSwordMasterAssassinMode>(
            "LegionSwordMasterAssassinMode",
            "Legion SwordMaster Assassin Mode",
            "Solo (Skill 3 when Burning Sensation missing) or Farm (Skill 3 when Blood Arts != 1).",
            LegionSwordMasterAssassinMode.Solo
        ),
        new Option<QuantumChronomancerMode>(
            "QuantumChronomancerMode",
            "Quantum Chrono Mode",
            "Lament_Cape (Max Crit) or Penitence_Cape (Defense).",
            QuantumChronomancerMode.Lament_Cape
        ),
        new Option<bool>(
            "AutoEnhance",
            "Auto Enhance Gear",
            "Automatically apply optimal Forge enhancements before testing.",
            true
        ),
        new Option<bool>(
            "LagKiller",
            "Lag Killer",
            "Enable Lag Killer during testing (default false).",
            false
        ),
        new Option<string>(
            "CustomSkills",
            "Custom Skills Override",
            "Leave blank to use mode default, or enter comma-separated numbers (e.g. 1, 2).",
            ""
        ),
        CoreBots.Instance.SkipOptions,
    };

    private const string LogPrefix = "TestClassDUCK";
    private const int PrivateRoomNumber = 1245;

    private static (string Map, string Cell, string Pad, string MonsterName) GetModeConfig(TestMode mode) => mode switch
    {
        TestMode.Classhall => ("classhall", "r4", "Right", "Testing Dummy"),
        TestMode.Classhall2 => ("classhall", "r4c", "Right", "Boss Dummy"),
        TestMode.Celestialarenad => ("celestialarenad", "r10", "Left", "Aranx"),
        TestMode.Icestormunder => ("icestormunder", "r2", "Top", "Frost Spirit"),
        TestMode.Necrodungeon => ("necrodungeon", "r4", "Left", "Doom Overlord"),
        TestMode.Ultradrakath => ("ultradrakath", "r1", "Left", "Champion of Chaos"),
        TestMode.Bosschallenge => ("bosschallenge", "r16", "Left", "Ancient Horror"),
        TestMode.Sevencircleswar => ("sevencircleswar", "Enter", "Right", "Wrath Guard"),
        _ => ("classhall", "r4", "Right", "Testing Dummy")
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;
        Bot.Options.LagKiller = false;

        try
        {
            Run();
        }
        finally
        {
            Duck.StopSkillEngine();
        }
    }

    private void Run()
    {
        string? currentClass = Bot.Player.CurrentClass?.Name;
        if (string.IsNullOrWhiteSpace(currentClass))
        {
            Core.Logger("No class equipped.", LogPrefix, messageBox: true, stopBot: true);
            return;
        }

        TestMode testMode = Bot.Config?.Get<TestMode>("TestMode") ?? TestMode.Classhall;
        var (mapName, cellName, padName, targetMonsterName) = GetModeConfig(testMode);

        bool autoEnhance = Bot.Config?.Get<bool>("AutoEnhance") ?? true;
        bool lagKiller = Bot.Config?.Get<bool>("LagKiller") ?? false;
        Bot.Options.LagKiller = lagKiller;
        if (!lagKiller && Bot.Flash.GetGameObject<bool>("ui.monsterIcon.redX.visible"))
            Bot.Flash.CallGameFunction("world.toggleMonsters");
        string customSkillsStr = Bot.Config?.Get<string>("CustomSkills") ?? "";

        ClassPreset preset = ResolvePreset(currentClass);

        if (!string.IsNullOrWhiteSpace(customSkillsStr))
        {
            int[] parsedSkills = customSkillsStr
                .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out int val) ? val : 0)
                .Where(v => v >= 1 && v <= 4)
                .ToArray();
            if (parsedSkills.Length > 0)
                preset.Skills = parsedSkills;
        }

        Duck.FileLog($"Testing class: {preset.ClassName} (Skill Mode: {preset.SkillMode})", LogPrefix);
        Core.Logger($"Testing class: {preset.ClassName} (Skill Mode: {preset.SkillMode})", LogPrefix);

        // 1. Prepare Enhancements
        if (autoEnhance)
        {
            Duck.PrepareEnhancements(
                preset.BaseEnhancement,
                preset.CapeEnhancement,
                preset.HelmEnhancement,
                preset.WeaponEnhancement,
                weaponFallbacks: preset.WeaponEnhancementFallbacks
            );
        }

        // 2. Join room
        Duck.JoinRoom(mapName, PrivateRoomNumber, cellName, padName);
        Core.Jump(cellName, padName);

        // 3. Start CoreDUCK Skill Engine
        Duck.StartSkillEngine(
            preset.Skills,
            "Tester",
            false,
            LogPrefix,
            preset.SkillMode,
            survivalSkill: preset.SurvivalSkill,
            survivalHealthThreshold: preset.SurvivalHealthThreshold
        );

        Duck.FileLog($"Started combat testing in [{mapName}-{PrivateRoomNumber}] ({cellName}, {padName}) on {targetMonsterName}. Stop script when done.", LogPrefix);
        Core.Logger($"Started combat testing in [{mapName}-{PrivateRoomNumber}] ({cellName}, {padName}) on {targetMonsterName}. Stop script when done.", LogPrefix);
        Bot.Combat.Attack(targetMonsterName);

        DateTime lastDiag = DateTime.MinValue;

        // 4. Combat Loop
        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                Bot.Wait.ForTrue(() => Bot.Player.Alive, 20);
                Bot.Sleep(1000);
            }

            if (Bot.Player.Cell != cellName)
                Core.Jump(cellName, padName);

            MaintainTarget(targetMonsterName);

            if ((DateTime.Now - lastDiag).TotalSeconds >= 1)
            {
                lastDiag = DateTime.Now;
                var selfAuras = Bot.Self.Auras != null 
                    ? string.Join(", ", Bot.Self.Auras.Select(a => $"{a.Name}({a.Value})"))
                    : "none";
                var targetAuras = Bot.Target?.Auras != null 
                    ? string.Join(", ", Bot.Target.Auras.Select(a => $"{a.Name}({a.Value})")) 
                    : "none";
                
                string diagMsg = $"[Diag] HP:{Bot.Player.Health}/{Bot.Player.MaxHealth} MP:{Bot.Player.Mana}/{Bot.Player.MaxMana} | Skills:[1:{Bot.Skills.CanUseSkill(1)}, 2:{Bot.Skills.CanUseSkill(2)}, 3:{Bot.Skills.CanUseSkill(3)}, 4:{Bot.Skills.CanUseSkill(4)}] | Self: [{selfAuras}] | Target: [{targetAuras}]";
                Duck.FileLog(diagMsg, LogPrefix);
                Core.Logger(diagMsg, LogPrefix);
            }

            Bot.Sleep(100);
        }
    }

    private void MaintainTarget(string monsterName)
    {
        if (!Bot.Player.Alive || string.IsNullOrEmpty(monsterName))
            return;

        var target = Bot.Player.Target;
        if (!Bot.Player.HasTarget || target == null || target.HP <= 0 || !string.Equals(target.Name, monsterName, StringComparison.OrdinalIgnoreCase))
        {
            var mon = Bot.Monsters?.CurrentAvailableMonsters?.FirstOrDefault(m =>
                m != null && m.HP > 0 && string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase));
            if (mon != null && mon.MapID > 0)
                Bot.Combat.Attack(mon.MapID);
            else if (mon != null)
                Bot.Combat.Attack(monsterName);
        }
    }

    private ClassPreset ResolvePreset(string currentClass)
    {
        string norm = currentClass.Trim();

        if (norm.Equals("Arachnomancer", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<ArachnomancerMode>("ArachnomancerMode") ?? ArachnomancerMode.Solo_Dauntless;
            return Duck.Arachnomancer(soloMode: mode == ArachnomancerMode.Solo_Dauntless);
        }

        if (norm.Equals("LightCaster", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<LightCasterMode>("LightCasterMode") ?? LightCasterMode.Default;
            return Duck.LightCaster(healingMode: mode == LightCasterMode.Healing);
        }

        if (norm.StartsWith("Chrono Shadow", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<ChronoShadowHunterMode>("ChronoShadowHunterMode") ?? ChronoShadowHunterMode.Stable;
            return Duck.ChronoShadowHunter(gunslingerMode: mode == ChronoShadowHunterMode.Gunslinger);
        }

        if (norm.StartsWith("Chaos Slayer", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<ChaosSlayerMode>("ChaosSlayerMode") ?? ChaosSlayerMode.Solo;
            return Duck.ChaosSlayer(farmMode: mode == ChaosSlayerMode.Farm);
        }

        if (norm.Equals("Chaos Avenger", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<ChaosAvengerMode>("ChaosAvengerMode") ?? ChaosAvengerMode.Optimized;
            return Duck.ChaosAvenger(optimizedMode: mode == ChaosAvengerMode.Optimized);
        }

        if (norm.Equals("Shaman", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<ShamanMode>("ShamanMode") ?? ShamanMode.Solo_Boss;
            return Duck.Shaman(farmMode: mode == ShamanMode.Farm);
        }

        if (norm.StartsWith("Abyssal Angel", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<AbyssalAngelMode>("AbyssalAngelMode") ?? AbyssalAngelMode.Default_Survival;
            return Duck.AbyssalAngelShadow(farmMode: mode == AbyssalAngelMode.Farm);
        }

        if (norm.Equals("Frostval Barbarian", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<FrostvalBarbarianMode>("FrostvalBarbarianMode") ?? FrostvalBarbarianMode.Default;
            return Duck.FrostvalBarbarian(farmMode: mode == FrostvalBarbarianMode.Farm);
        }

        if (norm.Equals("Legion SwordMaster Assassin", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Legion BladeMaster Assassin", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<LegionSwordMasterAssassinMode>("LegionSwordMasterAssassinMode") ?? LegionSwordMasterAssassinMode.Solo;
            return Duck.LegionSwordMasterAssassin(farmMode: mode == LegionSwordMasterAssassinMode.Farm);
        }

        if (norm.StartsWith("Quantum Chrono", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<QuantumChronomancerMode>("QuantumChronomancerMode") ?? QuantumChronomancerMode.Lament_Cape;
            return Duck.QuantumChronomancer(usePenitenceCape: mode == QuantumChronomancerMode.Penitence_Cape);
        }

        if (norm.Equals("ArchMage", StringComparison.OrdinalIgnoreCase))
        {
            var mode = Bot.Config?.Get<ArchMageMode>("ArchMageMode") ?? ArchMageMode.Corporeal_Farming;
            return Duck.ArchMage(astralMode: mode == ArchMageMode.Astral_Solo);
        }

        return Duck.GetClassPreset(currentClass);
    }
}
