/*
name: Turret DUCK
description: Map sentry and auto-attack turret using CoreDUCK asynchronous skill engine.
tags: turret, sentry, autoattack, farm, kill, coreduck, duck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreFarms.cs
//cs_include DUCKScripts\UltrasDUCK\CoreDUCK.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class TurretDUCK
{
    private static IScriptInterface Bot => IScriptInterface.Instance;
    private static CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "[Turret DUCK]";

    public bool DontPreconfigure = true;
    public string OptionsStorage = "TurretDUCK";

    public List<IOption> Options = new()
    {
        new Option<bool>("AutoEnhance", "Auto Enhance", "Automatically enhance equipped class on startup using CoreDUCK.", true),
        new Option<bool>("LockToStartingCell", "Lock to Starting Cell", "Automatically return to the starting map/room/cell/pad if killed or displaced.", true),
        new Option<bool>("AggroMonsters", "Aggro Cell Monsters", "Enable cell-wide aggro to pull all monsters in the current cell.", false),
        new Option<string>("Monsters", "Monsters to Attack", "Monster name(s) separated by commas (leave blank to attack everything in cell).", ""),
        new Option<string>("DropsToPickup", "Drops to Pickup", "Items to auto-whitelist and pick up (separated by commas).", ""),
        new Option<string>("CustomSkillOrder", "Custom Skill Order", "Optional custom skill indices separated by commas (e.g. 1,2,3,4). Leave blank for class preset.", ""),
        CoreBots.Instance.SkipOptions,
    };

    private string currentClassName = string.Empty;
    private ClassPreset? currentPreset;
    private bool skillEngineRunning = false;
    private string startingMap = string.Empty;
    private int startingRoomNumber = 0;
    private string startingCell = string.Empty;
    private string startingPad = string.Empty;

    public void ScriptMain(IScriptInterface bot)
    {
        Core.SetOptions(disableClassSwap: true);

        Bot.Skills.Stop();
        Bot.Options.AttackWithoutTarget = false;

        bool aggro = Bot.Config?.Get<bool>("AggroMonsters") ?? false;
        Bot.Options.AggroMonsters = aggro;
        Bot.Options.AggroAllMonsters = aggro;

        string drops = Bot.Config?.Get<string>("DropsToPickup") ?? "";
        if (!string.IsNullOrWhiteSpace(drops))
        {
            string[] dropList = drops.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (dropList.Length > 0)
                Core.AddDrop(dropList);
        }

        startingMap = Bot.Map?.Name ?? "";
        startingRoomNumber = Bot.Map?.RoomID ?? 0;
        startingCell = Bot.Player?.Cell ?? "Enter";
        startingPad = Bot.Player?.Pad ?? "Spawn";

        currentPreset = ResolveCurrentClassPreset();
        currentClassName = currentPreset.ClassName;

        if (Bot.Config?.Get<bool>("AutoEnhance") ?? true)
        {
            ApplyEnhancements(currentPreset);
            ReturnToTurretStation();
        }

        Core.Logger($"{LogPrefix} Activated in map '{startingMap}' (Room {startingRoomNumber}) cell '{startingCell}' [{startingPad}] using class '{currentClassName}'.");

        try
        {
            RunTurret();
        }
        finally
        {
            StopTurretSkillEngine();
            Bot.Options.AggroMonsters = false;
            Bot.Options.AggroAllMonsters = false;
            Core.SetOptions(false);
        }
    }

    private void RunTurret()
    {
        bool lockCell = Bot.Config?.Get<bool>("LockToStartingCell") ?? true;
        string monsterOption = Bot.Config?.Get<string>("Monsters") ?? "";
        string[] monsterList = string.IsNullOrWhiteSpace(monsterOption)
            ? Array.Empty<string>()
            : monsterOption.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        StartTurretSkillEngine();

        while (!Bot.ShouldExit)
        {
            if (Bot.Player?.Alive != true)
            {
                StopTurretSkillEngine();
                while (!Bot.ShouldExit && Bot.Player?.Alive != true)
                    Core.Sleep(250);

                if (Bot.ShouldExit)
                    return;

                if (lockCell)
                    ReturnToTurretStation();

                StartTurretSkillEngine();
            }

            if (lockCell && (!string.Equals(Bot.Map?.Name, startingMap, StringComparison.OrdinalIgnoreCase) || !string.Equals(Bot.Player?.Cell, startingCell, StringComparison.OrdinalIgnoreCase)))
            {
                StopTurretSkillEngine();
                ReturnToTurretStation();
                StartTurretSkillEngine();
            }

            string activeClass = Bot.Player?.CurrentClass?.Name ?? "";
            if (!string.IsNullOrWhiteSpace(activeClass) && !string.Equals(activeClass, currentClassName, StringComparison.OrdinalIgnoreCase))
            {
                Core.Logger($"{LogPrefix} Class change detected: '{currentClassName}' -> '{activeClass}'. Re-tuning CoreDUCK skill engine.");
                StopTurretSkillEngine();
                currentPreset = ResolveCurrentClassPreset();
                currentClassName = currentPreset.ClassName;

                if (Bot.Config?.Get<bool>("AutoEnhance") ?? true)
                {
                    ApplyEnhancements(currentPreset);
                    ReturnToTurretStation();
                }

                StartTurretSkillEngine();
            }

            if (monsterList.Length > 0)
            {
                bool attackedAny = false;
                foreach (string mob in monsterList)
                {
                    if (Bot.Monsters.MapMonsters.Any(m => m.Alive && string.Equals(m.Name, mob, StringComparison.OrdinalIgnoreCase)))
                    {
                        Bot.Combat.Attack(mob);
                        attackedAny = true;
                        break;
                    }
                }

                if (!attackedAny)
                    Bot.Combat.Attack("*");
            }
            else
            {
                Bot.Combat.Attack("*");
            }

            Core.Sleep(200);
        }
    }

    private void ReturnToTurretStation()
    {
        if (string.IsNullOrEmpty(startingMap))
            return;

        bool wrongMap = !string.Equals(Bot.Map?.Name, startingMap, StringComparison.OrdinalIgnoreCase);
        bool wrongCell = !string.IsNullOrEmpty(startingCell) && !string.Equals(Bot.Player?.Cell, startingCell, StringComparison.OrdinalIgnoreCase);

        if (wrongMap)
        {
            Core.Logger($"{LogPrefix} Returning to turret map '{startingMap}' (Room {startingRoomNumber}) cell '{startingCell}' [{startingPad}].");
            if (startingRoomNumber > 0)
                Core.Join($"{startingMap}-{startingRoomNumber}", startingCell, startingPad);
            else
                Core.Join(startingMap, startingCell, startingPad);

            Bot.Wait.ForCellChange(startingCell);
        }
        else if (wrongCell)
        {
            Core.Logger($"{LogPrefix} Returning to turret cell '{startingCell}' [{startingPad}].");
            Core.Jump(startingCell, startingPad);
        }
    }

    private void StartTurretSkillEngine()
    {
        if (skillEngineRunning)
            return;

        currentPreset ??= ResolveCurrentClassPreset();

        Duck.StartSkillEngine(
            currentPreset.Skills,
            roleName: currentPreset.ClassName,
            taunter: false,
            logPrefix: LogPrefix,
            mode: currentPreset.SkillMode,
            survivalSkill: currentPreset.SurvivalSkill,
            survivalHealthThreshold: currentPreset.SurvivalHealthThreshold
        );

        skillEngineRunning = true;
    }

    private void StopTurretSkillEngine()
    {
        if (!skillEngineRunning)
            return;

        Duck.StopSkillEngine();
        skillEngineRunning = false;
    }

    private void ApplyEnhancements(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} Auto-enhancing class '{preset.ClassName}' via CoreDUCK.");
        Duck.PrepareEnhancements(
            preset.BaseEnhancement,
            preset.CapeEnhancement,
            preset.HelmEnhancement,
            preset.WeaponEnhancement,
            weaponFallbacks: preset.WeaponEnhancementFallbacks
        );
    }

    private ClassPreset ResolveCurrentClassPreset()
    {
        string className = Bot.Player?.CurrentClass?.Name ?? "";
        string normalized = className.Trim().ToLowerInvariant();

        string customSkillsStr = Bot.Config?.Get<string>("CustomSkillOrder") ?? "";
        int[]? customSkills = null;
        if (!string.IsNullOrWhiteSpace(customSkillsStr))
        {
            var parsed = customSkillsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out int val) ? val : -1)
                .Where(v => v is >= 1 and <= 4)
                .ToArray();
            if (parsed.Length > 0)
                customSkills = parsed;
        }

        ClassPreset preset = normalized switch
        {
            "legion revenant" => Duck.LegionRevenant(),
            "archpaladin" => Duck.ArchPaladin(),
            "lord of order" or "lordoforder" => Duck.LordOfOrder(),
            "stonecrusher" or "infinity titan" or "infinitytitan" => Duck.StoneCrusher(),
            "verus doomknight" => Duck.VerusDoomKnight(),
            "void highlord" => Duck.VoidHighlord(),
            "chaos avenger" => Duck.ChaosAvenger(),
            "chaos slayer" or "chaos slayer berserker" or "chaos slayer mystic" or "chaos slayer cleric" or "chaos slayer thief" => Duck.ChaosSlayer(),
            "dragon of time" => Duck.DragonOfTime(),
            "archfiend" => Duck.ArchFiend(),
            "arachnomancer" => Duck.Arachnomancer(),
            "hollowborn vindicator" => Duck.HollowbornVindicator(),
            "shaman" => Duck.Shaman(),
            "lightcaster" => Duck.LightCaster(),
            "quantum chronomancer" => Duck.QuantumChronomancer(),
            "chrono shadowhunter" or "chrono shadowslayer" => Duck.ChronoShadowHunter(),
            "arcana invoker" => Duck.ArcanaInvoker(),
            "scion of flames" => Duck.ScionOfFlames(),
            "guardian" => Duck.Guardian(),
            "oracle" => Duck.Oracle(),
            "bard" => Duck.Bard(),
            "king's echo" or "kings echo" => Duck.KingsEcho(),
            "yami no ronin" => Duck.YamiNoRonin(),
            "abyssal angel's shadow" or "abyssal angel" or "aas" => Duck.AbyssalAngelShadow(),
            "healer" or "healer (rare)" or "acolyte" => Duck.Healer(),
            "imperial chunin" or "chunin" => Duck.ImperialChunin(),
            "alpha doommega" or "alpha omega" or "alphadoommega" or "alphaomega" or "ao" => Duck.AlphaOmega(),
            _ => new ClassPreset
            {
                ClassName = string.IsNullOrWhiteSpace(className) ? "Generic" : className,
                Skills = new[] { 1, 2, 3, 4 },
                SkillMode = SkillEngineMode.Simple
            }
        };

        if (customSkills != null && customSkills.Length > 0)
        {
            preset.Skills = customSkills;
            preset.SkillMode = SkillEngineMode.Simple;
        }

        return preset;
    }
}

