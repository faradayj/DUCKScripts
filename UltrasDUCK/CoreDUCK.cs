/*
name: Core DUCK
description: Shared combat mechanics for LoneWolf Ultra scripts.
tags: core, duck, ultra
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Quests;
using Skua.Core.Models.Shops;
using Skua.Core.Options;
using Skua.Core.Models.Auras;

#nullable enable

public class CoreDUCK
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;

    private const int SkillPollDelay = 100;
    private const int CSSGunslingerFirePollDelay = 50;
    private const string ArmyProtocolVersion = "1";
    private const int ArmyPollDelay = 500;
    private const int ArmyFileRetryDelay = 100;
    private const string PotionShopMap = "alchemyacademy";
    private const int PotionShopId = 2036;
    private const int PotionAuraCheckDelay = 1500;
    private const int PotionSuccessDelay = 2000;
    private const string EnrageScroll = "Scroll of Enrage";
    private const string DecayScroll = "Scroll of Decay";
    private const string MystifyScroll = "Scroll of Mystify";
    private const string SpellcraftMap = "spellcraft";
    private const int ArcaneQuillShopId = 693;
    private const int ArcaneQuillShopItemId = 7686;
    private const int SpellInkShopId = 549;
    private const int EnrageQuestId = 2330;
    private const int EnrageThreshold = 80;
    private const int EnrageMaxStack = 1000;
    private const int EnrageRewardQuantity = 40;
    private const int DecayQuestId = 2331;
    private const int MystifyQuestId = 2344;
    private const int OptionalScrollThreshold = 10;
    private const string ArcanaInvokerFoolAura = "0 - The Fool";
    private const string ArcanaInvokerJudgementAura = "Judgement Day";
    private const string ArcanaInvokerWorldAura = "XXI - The World";
    private const string ShamanElementalEmbraceAura = "Elemental Embrace";
    private const string ChronoShadowHunterRoundsEmptyAura = "Rounds Empty";
    private const string ChronoShadowHunterGunslingerAura = "Gunslinger Stance";
    private const string GuardianSpiritAura = "Guardian Spirit";

    private enum PotionCategory
    {
        Tonic,
        Elixir,
        CombatPotion,
    }

    private enum EnhancementSlot
    {
        Class,
        Cape,
        Helm,
        Weapon,
    }

    private sealed class TargetedPrioritySkillRequest
    {
        public TargetedPrioritySkillRequest(
            int skill,
            int targetMapId,
            int returnMapId
        )
        {
            Skill = skill;
            TargetMapId = targetMapId;
            ReturnMapId = returnMapId;
        }

        public int Skill { get; }
        public int TargetMapId { get; }
        public int ReturnMapId { get; }
    }

    private sealed class LimitedPrioritySkillRequest
    {
        public LimitedPrioritySkillRequest(int skill, int remainingUses)
        {
            Skill = skill;
            RemainingUses = remainingUses;
        }

        public int Skill { get; }
        public int RemainingUses { get; }
    }

    private static readonly string[] ArmyAliases =
    {
        "playerOne",
        "playerTwo",
        "playerThree",
        "playerFour",
        "playerFive",
        "playerSix",
        "playerSeven",
    };

    private Thread? skillThread;
    private volatile bool skillEngineRunning;
    private int skillEnginePaused;
    private int ordinarySkillsSuppressed;
    private int pendingAbsolutePriorityTauntMapId;
    private long pendingAbsolutePriorityTauntTimestamp;
    private int pendingTauntMapId;
    private int pendingImmediateTauntMapId;
    private int pendingSkillFiveMapId;
    private int pendingImmediateSkillFiveMapId;
    private int pendingAbsolutePrioritySkill;
    private int pendingPrioritySkill;
    private TargetedPrioritySkillRequest? pendingTargetedPrioritySkill;
    private LimitedPrioritySkillRequest? pendingLimitedPrioritySkill;
    private int skillIndex;
    private int[] skillList = Array.Empty<int>();
    private string role = string.Empty;
    private bool isTaunter;
    private string LogPrefix = string.Empty;
    private SkillEngineMode skillEngineMode;
    private bool useSurvivalSkill = true;
    private string? maintainedPotion;
    private int kingsEchoManaThreshold = 12;
    private int blockedStrictSkill;
    private string blockedStrictSkillSelfAura = string.Empty;
    private string blockedStrictSkillTargetAura = string.Empty;
    private int blockedSimpleSkill;
    private string blockedSimpleSkillTargetAura = string.Empty;
    private int survivalSkill;
    private int survivalHealthThreshold;
    private bool shamanSkillThreeEnabled = true;
    private bool cssNormalInitialManaCheck;
    private bool cssNormalNeedsRegeneration;
    private bool cssGunslingerInitialManaCheck;
    private bool cssGunslingerWaitingForStance;
    private bool cssGunslingerFiring;
    private bool cssGunslingerNeedsRegeneration;
    private volatile bool packetDetectorRunning;
    private string[] packetCommands = Array.Empty<string>();
    private string packetSelectedCommand = string.Empty;
    private string[] packetTexts = Array.Empty<string>();
    private string packetSelectedPacket = string.Empty;
    private bool packetChoiceMode;
    private string packetSelectedChoice = string.Empty;
    private string packetSkillPauseChoice = string.Empty;
    private bool packetPauseEveryChoice;
    private int packetDetectionCount;
    private int packetDebounceMs;
    private long lastPacketDetectionTimestamp;
    private int bypassedUltraQuestID;

    private string[] armyPlayers = Array.Empty<string>();
    private string armyUsername = string.Empty;
    private string armySyncPath = string.Empty;
    private string armyLaunchToken = string.Empty;
    private string armySessionId = string.Empty;
    private int armyPlayerIndex = -1;
    private long nextArmyStepId = 1;
    private long lastArmyStepId;
    private bool armyInitialized;
    private bool armySessionStarted;
    private bool armySessionFailureLogged;
    private bool armyStopLogged;
    private bool armyTransportFailed;
    private bool armyTransportFailureLogged;
    private int reportedFightAttempt;
    private bool? reportedFightAlive;

    public Option<string> player1 = new(
        "player1",
        "Player 1",
        "Player 1 is the sync boss.",
        string.Empty
    );

    public Option<string> player2 = new(
        "player2",
        "Player 2",
        "Player 2 account name.",
        string.Empty
    );

    public Option<string> player3 = new(
        "player3",
        "Player 3",
        "Player 3 account name.",
        string.Empty
    );

    public Option<string> player4 = new(
        "player4",
        "Player 4",
        "Player 4 account name.",
        string.Empty
    );

    public Option<string> player5 = new(
        "player5",
        "Player 5",
        "Player 5 account name.",
        string.Empty
    );

    public Option<string> player6 = new(
        "player6",
        "Player 6",
        "Player 6 account name.",
        string.Empty
    );

    public Option<string> player7 = new(
        "player7",
        "Player 7",
        "Player 7 account name.",
        string.Empty
    );

    public void ScriptMain(IScriptInterface Bot)
    {
        Core.RunCore();
    }

    public ClassPreset LegionRevenant() =>
        new()
        {
            ClassName = "Legion Revenant",
            AlternateClassNames = new[] { "Legion Revenant (IoDA)" },
            Skills = new[] { 3, 4, 2, 1 },
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Arcanas_Concerto,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Ravenous,
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
            Tonic = "Sage Tonic",
            Elixir = "Potent Malevolence Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset LightCaster(bool healingMode = false) =>
        new()
        {
            ClassName = "LightCaster",
            Skills = healingMode
                ? new[] { 2, 1, 4 }
                : new[] { 2, 1, 3, 4 },
            SkillMode = healingMode
                ? SkillEngineMode.LightCasterHealing
                : SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ArchFiend() =>
        new()
        {
            ClassName = "ArchFiend",
            Skills = new[] { 3, 4, 1, 2 },
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset Arachnomancer(bool soloMode = false) =>
        new()
        {
            ClassName = "Arachnomancer",
            Skills = soloMode ? new[] { 1, 2 } : new[] { 1, 3, 2, 4 },
            SkillMode = soloMode
                ? SkillEngineMode.ArachnomancerSolo
                : SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = soloMode ? CapeSpecial.Lament : CapeSpecial.Vainglory,
            HelmEnhancement = soloMode ? HelmSpecial.Anima : HelmSpecial.Forge,
            WeaponEnhancement = soloMode ? WeaponSpecial.Dauntless : WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = soloMode
                ? new[]
                {
                    WeaponSpecial.Valiance,
                    WeaponSpecial.Ravenous,
                    WeaponSpecial.Health_Vamp,
                }
                : new[]
                {
                    WeaponSpecial.Praxis,
                    WeaponSpecial.Lacerate,
                    WeaponSpecial.Health_Vamp,
                },
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset VoidHighlord() =>
        new()
        {
            ClassName = "Void Highlord",
            AlternateClassNames = new[] { "Void Highlord (IoDA)" },
            Skills = new[] { 1, 2, 4 },
            SkillMode = SkillEngineMode.VoidHighlord,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset HollowbornVindicator() =>
        new()
        {
            ClassName = "Hollowborn Vindicator",
            Skills = new[] { 3, 4, 1, 4, 1, 3, 1, 3, 1, 2 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Penitence,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset VerusDoomKnight() =>
        new()
        {
            ClassName = "Verus DoomKnight",
            Skills = new[]
            {
                1, 2, 3, 4,
                1, 2, 3,
                1, 2, 3,
                4, 1, 2, 3,
                1, 2, 4, 3,
                1, 2, 3,
            },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset DragonOfTime() =>
        new()
        {
            ClassName = "Dragon of Time",
            Skills = new[] { 3, 2, 1, 2, 4, 2 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
            Tonic = "Sage Tonic",
            Elixir = "Potent Malevolence Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset Bard() =>
        new()
        {
            ClassName = "Bard",
            Skills = new[] { 1, 4, 2, 3, 1, 2, 3, 4, 1, 3, 4, 2 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Absolution,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Valiance,
            Tonic = "Sage Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset KingsEcho() =>
        new()
        {
            ClassName = "King's Echo",
            AlternateClassNames = new[] { "Kings Echo" },
            Skills = new[] { 1, 2 },
            SkillMode = SkillEngineMode.KingsEcho,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Elysium,
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ArcanaInvoker() =>
        new()
        {
            ClassName = "Arcana Invoker",
            Skills = new[] { 2, 3, 4 },
            SkillMode = SkillEngineMode.ArcanaInvoker,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ChronoShadowHunter(bool gunslingerMode = false) =>
        new()
        {
            ClassName = "Chrono ShadowHunter",
            AlternateClassNames = new[] { "Chrono ShadowSlayer" },
            SkillMode = gunslingerMode
                ? SkillEngineMode.ChronoShadowHunterGunslinger
                : SkillEngineMode.ChronoShadowHunterStable,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Arcanas_Concerto,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset QuantumChronomancer(bool usePenitenceCape = false) =>
        new()
        {
            ClassName = "Quantum Chronomancer",
            Skills = new[] { 1, 2 },
            SkillMode = SkillEngineMode.QuantumChronomancer,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = usePenitenceCape ? CapeSpecial.Penitence : CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Smite,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Ravenous,
                WeaponSpecial.Dauntless,
                WeaponSpecial.Spiral_Carve,
            },
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ChaosSlayer(bool farmMode = false) =>
        new()
        {
            ClassName = "Chaos Slayer Berserker",
            AlternateClassNames = new[]
            {
                "Chaos Slayer Mystic",
                "Chaos Slayer Cleric",
                "Chaos Slayer Thief",
            },
            Skills = farmMode ? new[] { 2, 4 } : new[] { 3, 2, 4, 1 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = farmMode ? 3 : 0,
            SurvivalHealthThreshold = farmMode ? 60 : 0,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ChaosAvenger(bool optimizedMode = false) =>
        new()
        {
            ClassName = "Chaos Avenger",
            Skills = new[] { 3, 4, 1, 2 },
            SkillMode = optimizedMode
                ? SkillEngineMode.ChaosAvengerOptimized
                : SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ScionOfFlames() =>
        new()
        {
            ClassName = "Scion of Flames",
            Skills = new[] { 0, 3, 2, 1 },
            SkillMode = SkillEngineMode.ScionOfFlames,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
            Tonic = "Sage Tonic",
            Elixir = "Potent Malevolence Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset Shaman(bool farmMode = false) =>
        new()
        {
            ClassName = "Shaman",
            Skills = new[] { 1, 2 },
            SkillMode = farmMode ? SkillEngineMode.Simple : SkillEngineMode.Shaman,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Elysium,
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
            Tonic = "Fate Tonic",
            Elixir = "Potent Malevolence Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset StoneCrusher() =>
        new()
        {
            ClassName = "StoneCrusher",
            Skills = new[] { 3, 2, 4, 1 },
            BaseEnhancement = EnhancementType.Fighter,
            CapeEnhancement = CapeSpecial.Absolution,
            HelmEnhancement = HelmSpecial.None,
            WeaponEnhancement = WeaponSpecial.Valiance,
            Tonic = "Might Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset ArchPaladin() =>
        new()
        {
            ClassName = "ArchPaladin",
            Skills = new[] { 3, 2, 1, 4 },
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset LordOfOrder() =>
        new()
        {
            ClassName = "Lord of Order",
            AlternateClassNames = new[] { "Lord Of Order" },
            Skills = new[] { 2, 3, 1, 4 },
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Penitence,
            HelmEnhancement = HelmSpecial.None,
            WeaponEnhancement = WeaponSpecial.Awe_Blast,
            Tonic = "Fate Tonic",
            Elixir = "Potent Destruction Elixir",
            CombatPotion = "Potent Honor Potion",
        };

    public ClassPreset Oracle() =>
        new()
        {
            ClassName = "Oracle",
            Skills = new[] { 4, 3, 1, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Absolution,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Arcanas_Concerto,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset Guardian() =>
        new()
        {
            ClassName = "Guardian",
            Skills = new[] { 1, 2 },
            SkillMode = SkillEngineMode.Guardian,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = new[] { WeaponSpecial.Spiral_Carve },
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset YamiNoRonin() =>
        new()
        {
            ClassName = "Yami no Ronin",
            Skills = new[] { 1, 4, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Valiance,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Lacerate,
                WeaponSpecial.Ravenous,
                WeaponSpecial.Health_Vamp,
            },
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset AbyssalAngelShadow(bool farmMode = false) =>
        new()
        {
            ClassName = "Abyssal Angel's Shadow",
            AlternateClassNames = new[] { "Abyssal Angel" },
            Skills = farmMode ? new[] { 4, 2 } : new[] { 1, 4, 2 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 3,
            SurvivalHealthThreshold = 60,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset Healer() =>
        new()
        {
            ClassName = "Healer",
            AlternateClassNames = new[] { "Healer (Rare)", "Acolyte" },
            Skills = new[] { 1, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 2,
            SurvivalHealthThreshold = 80,
            BaseEnhancement = EnhancementType.Healer,
            CapeEnhancement = CapeSpecial.None,
            HelmEnhancement = HelmSpecial.Hearty,
            WeaponEnhancement = WeaponSpecial.Valiance,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Health_Vamp,
                WeaponSpecial.Awe_Blast,
            },
            Tonic = "Fate Tonic",
            Elixir = "Potent Malevolence Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset ImperialChunin() =>
        new()
        {
            ClassName = "Imperial Chunin",
            AlternateClassNames = new[] { "Chunin" },
            Skills = new[] { 2, 3, 1, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Valiance,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Ravenous,
                WeaponSpecial.Health_Vamp,
            },
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset AlphaOmega() =>
        new()
        {
            ClassName = "Alpha Omega",
            AlternateClassNames = new[] { "Alpha DOOMmega" },
            Skills = new[] { 1, 3, 4, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Smite,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Ravenous,
                WeaponSpecial.Health_Vamp,
            },
            Tonic = "Fate Tonic",
            Elixir = "Potent Battle Elixir",
            CombatPotion = "Felicitous Philtre",
        };

    public ClassPreset AlphaPirate() =>
        new()
        {
            ClassName = "Alpha Pirate",
            AlternateClassNames = new[] { "Pirate" },
            Skills = new[] { 1, 2, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset AntiqueHunter() =>
        new()
        {
            ClassName = "Antique Hunter",
            AlternateClassNames = new[] { "Artifact Hunter" },
            Skills = new[] { 1, 2, 4 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 3,
            SurvivalHealthThreshold = 60,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset ArcaneDarkCaster() =>
        new()
        {
            ClassName = "Arcane Dark Caster",
            AlternateClassNames = new[] { "Mystical Dark Caster", "Timeless Dark Caster" },
            Skills = new[] { 1, 3, 2, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset ArchMage(bool astralMode = false) =>
        new()
        {
            ClassName = "ArchMage",
            Skills = astralMode ? new[] { 2, 1, 3 } : new[] { 1, 3 },
            SkillMode = astralMode ? SkillEngineMode.ArchMageAstral : SkillEngineMode.ArchMageCorporeal,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
        };

    public ClassPreset Assassin() =>
        new()
        {
            ClassName = "Assassin",
            AlternateClassNames = new[] { "Ninja Warrior", "Ninja" },
            Skills = new[] { 4, 1 },
            SkillMode = SkillEngineMode.Assassin,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset Barber() =>
        new()
        {
            ClassName = "Barber",
            Skills = new[] { 1, 2, 4, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset BattleMage() =>
        new()
        {
            ClassName = "BattleMage",
            AlternateClassNames = new[] { "Royal BattleMage" },
            Skills = new[] { 4, 1, 2 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 3,
            SurvivalHealthThreshold = 50,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset Warlord() =>
        new()
        {
            ClassName = "Warlord",
            AlternateClassNames = new[] { "Warrior", "Warrior (Rare)" },
            Skills = new[] { 4, 1, 3, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset BeastMaster() =>
        new()
        {
            ClassName = "BeastMaster",
            Skills = new[] { 2, 3, 1, 4 },
            SkillMode = SkillEngineMode.BeastMaster,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset Berserker() =>
        new()
        {
            ClassName = "Berserker",
            Skills = new[] { 1, 3 },
            SkillMode = SkillEngineMode.Berserker,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset BladeMasterAssassin() =>
        new()
        {
            ClassName = "BladeMaster Assassin",
            AlternateClassNames = new[] { "SwordMaster Assassin" },
            Skills = new[] { 1, 2, 4, 3, 3, 3, 3 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset BladeMaster() =>
        new()
        {
            ClassName = "BladeMaster",
            AlternateClassNames = new[] { "SwordMaster" },
            Skills = new[] { 2, 1, 4, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset BlazeBinder() =>
        new()
        {
            ClassName = "Blaze Binder",
            Skills = new[] { 1, 4, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
        };

    public ClassPreset BloodAncient() =>
        new()
        {
            ClassName = "Blood Ancient",
            Skills = new[] { 3, 1, 2, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset BloodSorceress() =>
        new()
        {
            ClassName = "Blood Sorceress",
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
        };

    public ClassPreset BloodTitan() =>
        new()
        {
            ClassName = "Blood Titan",
            Skills = new[] { 4, 2, 1, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Fighter,
            CapeEnhancement = CapeSpecial.None,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset ChronoAssassin() =>
        new()
        {
            ClassName = "Chrono Assassin",
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset ClassicDoomKnight() =>
        new()
        {
            ClassName = "Classic DoomKnight",
            Skills = new[] { 1, 4, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset ClassicSoulCleaver() =>
        new()
        {
            ClassName = "Classic Exalted Soul Cleaver",
            AlternateClassNames = new[] { "Classic Soul Cleaver" },
            Skills = new[] { 1, 3 },
            SkillMode = SkillEngineMode.ClassicSoulCleaver,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset ClassicLegionDoomKnight() =>
        new()
        {
            ClassName = "Classic Legion DoomKnight",
            Skills = new[] { 1, 4, 3, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset ClassicNinja() =>
        new()
        {
            ClassName = "Classic Ninja",
            Skills = new[] { 4, 2, 1, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset ClassicPaladin() =>
        new()
        {
            ClassName = "Classic Paladin",
            Skills = new[] { 1, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 2,
            SurvivalHealthThreshold = 60,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset ClassicPirate() =>
        new()
        {
            ClassName = "Classic Pirate",
            AlternateClassNames = new[]
            {
                "Classic Alpha Pirate",
                "Rogue",
                "Rogue (Rare)",
                "Renegade",
            },
            Skills = new[] { 2, 3 },
            SkillMode = SkillEngineMode.ClassicPirate,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset Cryomancer() =>
        new()
        {
            ClassName = "Cryomancer",
            AlternateClassNames = new[]
            {
                "Dark Cryomancer",
                "Sakura Cryomancer",
            },
            Skills = new[] { 1, 4, 2 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 3,
            SurvivalHealthThreshold = 60,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset Daimon() =>
        new()
        {
            ClassName = "Daimon",
            Skills = new[] { 4, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 1,
            SurvivalHealthThreshold = 70,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset DarkCaster() =>
        new()
        {
            ClassName = "Dark Caster",
            AlternateClassNames = new[] { "Immortal Dark Caster" },
            Skills = new[] { 1, 3, 2, 2, 4, 2, 2, 3, 1, 4, 1 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Avarice,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset DarkHarbinger() =>
        new()
        {
            ClassName = "Dark Harbinger",
            AlternateClassNames = new[]
            {
                "Exalted Harbinger",
                "Exalted Soul Cleaver",
                "Soul Cleaver",
            },
            Skills = new[] { 1, 3 },
            SkillMode = SkillEngineMode.DarkHarbinger,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset DarkLegendaryHero() =>
        new()
        {
            ClassName = "Dark Legendary Hero",
            AlternateClassNames = new[] { "Legendary Hero" },
            Skills = new[] { 2, 1, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset DarkLord() =>
        new()
        {
            ClassName = "Dark Lord",
            AlternateClassNames = new[] { "Darkside" },
            Skills = new[] { 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 4,
            SurvivalHealthThreshold = 60,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Avarice,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
        };

    public ClassPreset DarkMetalNecro() =>
        new()
        {
            ClassName = "Dark Metal Necro",
            AlternateClassNames = new[]
            {
                "Doom Metal Necro",
                "Heavy Metal Necro",
                "Heavy Metal Rockstar",
                "Shadow Ripper",
                "Unchained Rockstar",
            },
            Skills = new[] { 1, 2, 3 },
            SkillMode = SkillEngineMode.DarkMetalNecro,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Penitence,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset DarkUltraOmniNight() =>
        new()
        {
            ClassName = "Dark Ultra OmniNight",
            AlternateClassNames = new[] { "Ultra OmniKnight" },
            Skills = new[] { 2, 3, 4, 1 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset DarkbloodStormKing() =>
        new()
        {
            ClassName = "Darkblood StormKing",
            Skills = new[] { 3, 1, 2, 1 },
            SkillMode = SkillEngineMode.DarkbloodStormKing,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
        };

    public ClassPreset DeathKnight() =>
        new()
        {
            ClassName = "DeathKnight",
            Skills = new[] { 3, 1, 2, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Fighter,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset DeathKnightLord() =>
        new()
        {
            ClassName = "DeathKnight Lord",
            Skills = new[] { 1, 2, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset DoomKnight() =>
        new()
        {
            ClassName = "DoomKnight",
            Skills = new[] { 1, 4, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset DracoKnight() =>
        new()
        {
            ClassName = "Draco Knight",
            AlternateClassNames = new[]
            {
                "Dragon Knight",
                "Drakkar Knight",
            },
            Skills = new[] { 2, 1 },
            SkillMode = SkillEngineMode.DracoKnight,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset DragonShinobi() =>
        new()
        {
            ClassName = "Dragon Shinobi",
            AlternateClassNames = new[]
            {
                "DragonSoul Shinobi",
                "Shadow Dragon Shinobi",
            },
            Skills = new[] { 1, 4 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 2,
            SurvivalHealthThreshold = 20,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset Dragonslayer() =>
        new()
        {
            ClassName = "Dragonslayer",
            Skills = new[] { 1, 2, 3 },
            SkillMode = SkillEngineMode.Dragonslayer,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset DragonslayerGeneral() =>
        new()
        {
            ClassName = "Dragonslayer General",
            AlternateClassNames = new[] { "ShadowFlame DragonLord" },
            Skills = new[] { 2, 1, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset DrakelWarlord() =>
        new()
        {
            ClassName = "Drakel Warlord",
            Skills = new[] { 4, 3, 1, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset ElementalDracomancer() =>
        new()
        {
            ClassName = "Elemental Dracomancer",
            AlternateClassNames = new[] { "Love Caster" },
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset EnchantedVampireLord() =>
        new()
        {
            ClassName = "Enchanted Vampire Lord",
            AlternateClassNames = new[]
            {
                "Royal Vampire Lord",
                "Vampire",
                "Vampire Lord",
            },
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset Enforcer() =>
        new()
        {
            ClassName = "Enforcer",
            AlternateClassNames = new[] { "ProtoSartorium" },
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset EternalInversionist() =>
        new()
        {
            ClassName = "Eternal Inversionist",
            Skills = new[] { 2, 4, 1, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset EvolvedClawSuit() =>
        new()
        {
            ClassName = "Evolved ClawSuit",
            AlternateClassNames = new[] { "Prismatic ClawSuit" },
            Skills = new[] { 2, 3, 4 },
            SkillMode = SkillEngineMode.EvolvedClawSuit,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset EvolvedDarkCaster() =>
        new()
        {
            ClassName = "Evolved Dark Caster",
            AlternateClassNames = new[]
            {
                "Infinite Dark Caster",
                "Infinite Legion Dark Caster",
                "Legion Evolved Dark Caster",
            },
            Skills = new[] { 1, 2, 4, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Avarice,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset EvolvedLeprechaun() =>
        new()
        {
            ClassName = "Evolved Leprechaun",
            Skills = new[] { 3, 4 },
            SkillMode = SkillEngineMode.EvolvedLeprechaun,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset EvolvedPumpkinLord() =>
        new()
        {
            ClassName = "Evolved Pumpkin Lord",
            Skills = new[] { 1, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset EvolvedShaman() =>
        new()
        {
            ClassName = "Evolved Shaman",
            Skills = new[] { 1, 2 },
            SkillMode = SkillEngineMode.EvolvedShaman,
            SurvivalSkill = 3,
            SurvivalHealthThreshold = 60,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset FrostSpiritReaver() =>
        new()
        {
            ClassName = "Frost SpiritReaver",
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset FrostvalBarbarian(bool farmMode = false) =>
        new()
        {
            ClassName = "Frostval Barbarian",
            Skills = farmMode ? new[] { 4, 1, 2, 3 } : new[] { 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Fighter,
            CapeEnhancement = CapeSpecial.Absolution,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset GlacialBerserker() =>
        new()
        {
            ClassName = "Glacial Berserker",
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset GrungeRocker() =>
        new()
        {
            ClassName = "Grunge Rocker",
            AlternateClassNames = new[]
            {
                "Neo Metal Necro",
                "Nu Metal Necro",
                "Shadow Rocker",
                "Unchained Rocker",
            },
            Skills = new[] { 1, 2, 3 },
            SkillMode = SkillEngineMode.GrungeRocker,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset HeroicNavalCommander() =>
        new()
        {
            ClassName = "Heroic Naval Commander",
            AlternateClassNames = new[]
            {
                "Legendary Naval Commander",
                "Naval Commander",
            },
            Skills = new[] { 3, 1, 2, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset HighSeasCommander() =>
        new()
        {
            ClassName = "HighSeas Commander",
            Skills = new[] { 1, 2, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Valiance,
        };

    public ClassPreset HoboHighlord() =>
        new()
        {
            ClassName = "Hobo Highlord",
            AlternateClassNames = new[]
            {
                "Hollowborn No-Class",
                "No Class",
                "Obsidian No Class",
                "Simple Class",
            },
            Skills = new[] { 1 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset HorcEvader() =>
        new()
        {
            ClassName = "Horc Evader",
            Skills = new[] { 1, 3, 4 },
            SkillMode = SkillEngineMode.HorcEvader,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset LegendaryElementalWarrior() =>
        new()
        {
            ClassName = "Legendary Elemental Warrior",
            AlternateClassNames = new[]
            {
                "Mythic Elemental Warrior",
                "Ultra Elemental Warrior",
            },
            Skills = new[] { 1, 5, 3, 1, 3, 5, 1, 2, 3 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset LegionDoomKnight() =>
        new()
        {
            ClassName = "Legion DoomKnight",
            Skills = new[] { 1, 2, 4, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
        };

    public ClassPreset LegionSwordMasterAssassin(bool farmMode = false) =>
        new()
        {
            ClassName = "Legion SwordMaster Assassin",
            AlternateClassNames = new[] { "Legion BladeMaster Assassin" },
            Skills = new[] { 1, 2, 4 },
            SkillMode = farmMode
                ? SkillEngineMode.LegionSwordMasterAssassinFarm
                : SkillEngineMode.LegionSwordMasterAssassinSolo,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Dauntless,
        };

    public ClassPreset Lich() =>
        new()
        {
            ClassName = "Lich",
            Skills = new[] { 4, 3, 4, 2, 4, 1, 3, 2, 4, 1 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset LightMage() =>
        new()
        {
            ClassName = "LightMage",
            Skills = new[] { 3, 1, 2, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset Lycan() =>
        new()
        {
            ClassName = "Lycan",
            Skills = new[] { 4, 3, 1, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset Mage() =>
        new()
        {
            ClassName = "Mage",
            AlternateClassNames = new[] { "Mage (Rare)", "Sorcerer" },
            Skills = new[] { 4, 2, 1, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset MartialArtist() =>
        new()
        {
            ClassName = "Martial Artist",
            AlternateClassNames = new[] { "Master Martial Artist" },
            Skills = new[] { 1, 2, 4, 2, 3, 2, 1, 2, 1, 2, 3, 1, 2, 2, 2, 2, 2 },
            SkillMode = SkillEngineMode.Strict,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset MasterRanger() =>
        new()
        {
            ClassName = "Master Ranger",
            AlternateClassNames = new[] { "Ranger" },
            Skills = new[] { 1, 2, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Avarice,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Elysium,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset MechaJouster() =>
        new()
        {
            ClassName = "MechaJouster",
            Skills = new[] { 4, 1, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset Necromancer() =>
        new()
        {
            ClassName = "Necromancer",
            AlternateClassNames = new[] { "Pinkomancer" },
            Skills = new[] { 4, 2, 1, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset NorthlandsMonk() =>
        new()
        {
            ClassName = "Northlands Monk",
            Skills = new[] { 4, 1, 3, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Examen,
            WeaponEnhancement = WeaponSpecial.Valiance,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Ravenous,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset Paladin() =>
        new()
        {
            ClassName = "Paladin",
            AlternateClassNames = new[] { "Silver Paladin" },
            Skills = new[] { 1, 4, 3 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 2,
            SurvivalHealthThreshold = 60,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Ravenous,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset Pyromancer() =>
        new()
        {
            ClassName = "Pyromancer",
            AlternateClassNames = new[] { "Pink Romancer" },
            Skills = new[] { 1, 2, 4 },
            SkillMode = SkillEngineMode.Pyromancer,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset Rustbucket() =>
        new()
        {
            ClassName = "Rustbucket",
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Ravenous,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset ScarletSorceress() =>
        new()
        {
            ClassName = "Scarlet Sorceress",
            Skills = new[] { 1, 2, 3 },
            SkillMode = SkillEngineMode.ScarletSorceress,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset ShadowScytheGeneral() =>
        new()
        {
            ClassName = "ShadowScythe General",
            Skills = new[] { 1, 2, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset SkyGuardGrenadier() =>
        new()
        {
            ClassName = "SkyGuard Grenadier",
            Skills = new[] { 1, 2, 4, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset SovereignOfStorms() =>
        new()
        {
            ClassName = "Sovereign of Storms",
            Skills = new[] { 3, 1 },
            SkillMode = SkillEngineMode.SovereignOfStorms,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Valiance,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset TheCollector() =>
        new()
        {
            ClassName = "The Collector",
            Skills = new[] { 1, 3, 2, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Ravenous,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset ThiefOfHours() =>
        new()
        {
            ClassName = "Thief of Hours",
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset TrollSpellsmith() =>
        new()
        {
            ClassName = "Troll Spellsmith",
            Skills = new[] { 4, 1, 2, 3 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Wizard,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Pneuma,
            WeaponEnhancement = WeaponSpecial.Elysium,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset UndeadGoat() =>
        new()
        {
            ClassName = "Undead Goat",
            AlternateClassNames = new[] { "Unundead Goat" },
            Skills = new[] { 4, 1, 2 },
            SkillMode = SkillEngineMode.Simple,
            SurvivalSkill = 3,
            SurvivalHealthThreshold = 30,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset UndeadLeperchaun() =>
        new()
        {
            ClassName = "Undead Leperchaun",
            AlternateClassNames = new[] { "Unlucky Leperchaun" },
            Skills = new[] { 4, 3, 1, 2 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Anima,
            WeaponEnhancement = WeaponSpecial.Dauntless,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public ClassPreset UndeadSlayer() =>
        new()
        {
            ClassName = "UndeadSlayer",
            Skills = new[] { 1, 3, 2 },
            SkillMode = SkillEngineMode.UndeadSlayer,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Lament,
            HelmEnhancement = HelmSpecial.Vim,
            WeaponEnhancement = WeaponSpecial.Dauntless,
            WeaponEnhancementFallbacks = new[]
            {
                WeaponSpecial.Valiance,
                WeaponSpecial.Health_Vamp,
            },
        };

    public void EquipClass(ClassPreset preset)
    {
        if (Bot.ShouldExit)
            return;

        if (preset == null || string.IsNullOrWhiteSpace(preset.ClassName))
        {
            Core.Logger(
                "Class preset is invalid.",
                "EquipClass",
                messageBox: true,
                stopBot: true
            );
            return;
        }

        InventoryItem? classItem = ResolveClassItem(preset);
        if (classItem == null)
        {
            Core.Logger(
                $"{string.Join(" or ", new[] { preset.ClassName }.Concat(preset.AlternateClassNames))} (Class) is not in inventory or bank.",
                "EquipClass",
                messageBox: true,
                stopBot: true
            );
            return;
        }

        if (
            Bot.Player.CurrentClass?.ID == classItem.ID
            || string.Equals(
                Bot.Player.CurrentClass?.Name,
                classItem.Name,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            Core.Logger($"{classItem.Name} already equipped.", "EquipClass");
            return;
        }

        if (Bot.Bank.Items.Any(x => x.ID == classItem.ID))
        {
            if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
                Bot.Bank.Open();

            Bot.Bank.Load(waitForLoad: false);
            Bot.Wait.ForTrue(() => Bot.Bank.Items.Any(x => x.ID == classItem.ID), 20);

            if (!Core.HasSpace)
            {
                Core.Logger(
                    $"{classItem.Name} could not be moved from bank because no inventory slot is available.",
                    "EquipClass",
                    messageBox: true,
                    stopBot: true
                );
                return;
            }

            Bot.Bank.ToInventory(classItem.ID);
            Bot.Wait.ForTrue(() => Bot.Inventory.Items.Any(x => x.ID == classItem.ID), 14);

            if (!Bot.Inventory.Items.Any(x => x.ID == classItem.ID))
            {
                Core.Logger(
                    $"{classItem.Name} could not be moved from bank.",
                    "EquipClass",
                    messageBox: true,
                    stopBot: true
                );
                return;
            }
        }

        Core.Equip(classItem.ID);
        Bot.Wait.ForTrue(
            () =>
                Bot.Player.CurrentClass?.ID == classItem.ID
                || string.Equals(
                    Bot.Player.CurrentClass?.Name,
                    classItem.Name,
                    StringComparison.OrdinalIgnoreCase
                ),
            10
        );

        if (
            Bot.Player.CurrentClass?.ID == classItem.ID
            || string.Equals(
                Bot.Player.CurrentClass?.Name,
                classItem.Name,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            Core.Logger($"{classItem.Name} equipped.", "EquipClass");
            return;
        }

        Core.Logger(
            $"{classItem.Name} could not be equipped.",
            "EquipClass",
            messageBox: true,
            stopBot: true
        );
    }

    public InventoryItem? ResolveClassItem(ClassPreset preset)
    {
        if (preset == null)
            return null;

        List<string> searchNames = new();
        if (!string.IsNullOrWhiteSpace(preset.ClassName))
            searchNames.Add(preset.ClassName);

        if (preset.AlternateClassNames != null && preset.AlternateClassNames.Length > 0)
        {
            foreach (string alt in preset.AlternateClassNames)
            {
                if (!string.IsNullOrWhiteSpace(alt) && !searchNames.Contains(alt, StringComparer.OrdinalIgnoreCase))
                    searchNames.Add(alt);
            }
        }

        if (!Bot.Bank.Loaded)
        {
            if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
                Bot.Bank.Open();

            Bot.Bank.Load(waitForLoad: false);
            Bot.Wait.ForBankLoad(20);
        }

        var allClassItems = Bot.Inventory.Items.Concat(Bot.Bank.Items)
            .Where(x => x.Category == ItemCategory.Class)
            .ToList();

        // 1. Exact match against search names
        foreach (string name in searchNames)
        {
            var item = allClassItems.FirstOrDefault(x =>
                x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (item != null)
                return item;
        }

        // 2. StartsWith prefix match fallback (e.g. "Chaos Slayer")
        foreach (string name in searchNames)
        {
            var item = allClassItems.FirstOrDefault(x =>
                x.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            if (item != null)
                return item;
        }

        return null;
    }

    public InventoryItem? ResolveClassItem(string className)
    {
        ClassPreset preset = GetClassPreset(className);
        return ResolveClassItem(preset);
    }

    public void StartSkillEngine(
        int[] skills,
        string roleName,
        bool taunter,
        string logPrefix,
        SkillEngineMode mode,
        bool useSurvivalSkill,
        string? maintainedPotion,
        int kingsEchoManaThreshold,
        int blockedStrictSkill,
        string blockedStrictSkillSelfAura,
        int blockedSimpleSkill,
        string blockedSimpleSkillTargetAura,
        string blockedStrictSkillTargetAura
    )
    {
        StartSkillEngine(
            skills,
            roleName,
            taunter,
            logPrefix,
            mode,
            useSurvivalSkill,
            maintainedPotion,
            kingsEchoManaThreshold,
            blockedStrictSkill,
            blockedStrictSkillSelfAura,
            blockedSimpleSkill,
            blockedSimpleSkillTargetAura,
            blockedStrictSkillTargetAura,
            survivalSkill: 0,
            survivalHealthThreshold: 0
        );
    }

    public void StartSkillEngine(
        int[] skills,
        string roleName,
        bool taunter,
        string logPrefix,
        SkillEngineMode mode = SkillEngineMode.Simple
    )
    {
        StartSkillEngine(
            skills,
            roleName,
            taunter,
            logPrefix,
            mode,
            useSurvivalSkill: true,
            maintainedPotion: null,
            kingsEchoManaThreshold: 12,
            blockedStrictSkill: 0,
            blockedStrictSkillSelfAura: "",
            blockedSimpleSkill: 0,
            blockedSimpleSkillTargetAura: "",
            blockedStrictSkillTargetAura: "",
            survivalSkill: 0,
            survivalHealthThreshold: 0
        );
    }

    public void StartSkillEngine(
        int[] skills,
        string roleName,
        bool taunter,
        string logPrefix,
        SkillEngineMode mode = SkillEngineMode.Simple,
        bool useSurvivalSkill = true,
        string? maintainedPotion = null,
        int kingsEchoManaThreshold = 12,
        int blockedStrictSkill = 0,
        string blockedStrictSkillSelfAura = "",
        int blockedSimpleSkill = 0,
        string blockedSimpleSkillTargetAura = "",
        string blockedStrictSkillTargetAura = "",
        int survivalSkill = 0,
        int survivalHealthThreshold = 0
    )
    {
        if (!ValidateFunctionBasedSkillsDisabled())
            return;

        StopSkillEngine();

        skillList = skills;
        role = roleName;
        isTaunter = taunter;
        LogPrefix = logPrefix;
        skillEngineMode = mode;
        this.useSurvivalSkill = useSurvivalSkill;
        this.maintainedPotion = maintainedPotion;
        this.kingsEchoManaThreshold = kingsEchoManaThreshold;
        this.blockedStrictSkill = blockedStrictSkill;
        this.blockedStrictSkillSelfAura = blockedStrictSkillSelfAura;
        this.blockedStrictSkillTargetAura = blockedStrictSkillTargetAura;
        this.blockedSimpleSkill = blockedSimpleSkill;
        this.blockedSimpleSkillTargetAura = blockedSimpleSkillTargetAura;
        this.survivalSkill = survivalSkill;
        this.survivalHealthThreshold = survivalHealthThreshold;
        skillIndex = 0;
        if (mode == SkillEngineMode.ChronoShadowHunterStable)
            ResetCSSNormalMode();
        if (mode == SkillEngineMode.ChronoShadowHunterGunslinger)
            ResetCSSGunslingerMode();
        skillEnginePaused = 0;
        ordinarySkillsSuppressed = 0;
        pendingAbsolutePriorityTauntMapId = 0;
        pendingAbsolutePriorityTauntTimestamp = 0;
        pendingTauntMapId = 0;
        pendingImmediateTauntMapId = 0;
        pendingSkillFiveMapId = 0;
        pendingImmediateSkillFiveMapId = 0;
        pendingAbsolutePrioritySkill = 0;
        pendingPrioritySkill = 0;
        pendingTargetedPrioritySkill = null;
        pendingLimitedPrioritySkill = null;
        shamanSkillThreeEnabled = true;
        skillEngineRunning = true;

        Bot.Events.ScriptStopping -= OnScriptStopping;
        Bot.Events.ScriptStopping += OnScriptStopping;

        skillThread = new Thread(SkillEngineLoop)
        {
            Name = "LoneWolf Skill Engine",
            IsBackground = true,
        };
        skillThread.Start();
    }

    public void StopSkillEngine()
    {
        skillEngineRunning = false;
        Bot.Events.ScriptStopping -= OnScriptStopping;

        Thread? thread = skillThread;
        if (thread != null && thread.IsAlive && Thread.CurrentThread != thread)
            thread.Join(2000);

        skillThread = null;
        skillEnginePaused = 0;
        ordinarySkillsSuppressed = 0;
        pendingAbsolutePriorityTauntMapId = 0;
        pendingAbsolutePriorityTauntTimestamp = 0;
        pendingTauntMapId = 0;
        pendingImmediateTauntMapId = 0;
        pendingSkillFiveMapId = 0;
        pendingImmediateSkillFiveMapId = 0;
        pendingAbsolutePrioritySkill = 0;
        pendingPrioritySkill = 0;
        pendingTargetedPrioritySkill = null;
        pendingLimitedPrioritySkill = null;
        shamanSkillThreeEnabled = true;
        maintainedPotion = null;
        blockedStrictSkill = 0;
        blockedStrictSkillSelfAura = string.Empty;
        blockedStrictSkillTargetAura = string.Empty;
        blockedSimpleSkill = 0;
        blockedSimpleSkillTargetAura = string.Empty;
        survivalSkill = 0;
        survivalHealthThreshold = 0;
    }

    public void SetSkillEngineSkills(int[] skills)
    {
        if (skills.Length == 0)
            return;

        skillList = skills;
        skillIndex = 0;
    }

    public bool StartPacketDetector(string command, string text, int debounceMs = 0)
        => StartPacketDetector(new[] { command }, new[] { text }, choiceMode: false, debounceMs: debounceMs);

    public bool StartPacketDetector(string[] commands, string text, int debounceMs = 0)
        => StartPacketDetector(commands, new[] { text }, choiceMode: false, debounceMs: debounceMs);

    public bool StartPacketDetector(string command, string[] texts, int debounceMs = 0)
        => StartPacketDetector(new[] { command }, texts, choiceMode: false, debounceMs: debounceMs);

    public bool StartPacketDetector(string[] commands, string[] texts, int debounceMs = 0)
        => StartPacketDetector(commands, texts, choiceMode: false, debounceMs: debounceMs);

    public bool StartPacketChoiceDetector(
        string command,
        string[] choices,
        string pauseSkillChoice = "",
        bool pauseEveryChoice = false,
        int debounceMs = 0
    ) => StartPacketDetector(
        new[] { command },
        choices,
        choiceMode: true,
        pauseSkillChoice: pauseSkillChoice,
        pauseEveryChoice: pauseEveryChoice,
        debounceMs: debounceMs
    );

    private bool StartPacketDetector(
        string[] commands,
        string[] texts,
        bool choiceMode,
        string pauseSkillChoice = "",
        bool pauseEveryChoice = false,
        int debounceMs = 0
    )
    {
        StopPacketDetector();

        string[] validCommands = commands?
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Select(command => command.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        string[] validTexts = texts?
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(command => command.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        if (validCommands.Length == 0 || validTexts.Length == 0)
        {
            Core.Logger(
                "Packet detector command and text are required.",
                "CoreDUCK"
            );
            return false;
        }

        packetDebounceMs = debounceMs;
        Volatile.Write(ref lastPacketDetectionTimestamp, 0);
        packetCommands = validCommands;
        packetSelectedCommand = string.Empty;
        packetTexts = validTexts;
        Volatile.Write(ref packetSelectedPacket, string.Empty);
        packetChoiceMode = choiceMode;
        packetSelectedChoice = string.Empty;
        packetSkillPauseChoice = choiceMode
            && validTexts.Contains(pauseSkillChoice, StringComparer.Ordinal)
                ? pauseSkillChoice
                : string.Empty;
        packetPauseEveryChoice = choiceMode && pauseEveryChoice;
        Interlocked.Exchange(ref packetDetectionCount, 0);
        packetDetectorRunning = true;

        Bot.Flash.FlashCall += PacketDetectorFlashCall;
        Bot.Events.ScriptStopping -= OnPacketDetectorStopping;
        Bot.Events.ScriptStopping += OnPacketDetectorStopping;
        return true;
    }

    public bool HasPacketDetection(int detectionNumber) =>
        packetDetectorRunning
        && detectionNumber > 0
        && Volatile.Read(ref packetDetectionCount) >= detectionNumber;

    public string GetPacketDetectorCommand() =>
        Volatile.Read(ref packetSelectedCommand);

    public string GetPacketDetectorPacket() =>
        Volatile.Read(ref packetSelectedPacket);

    public string GetPacketDetectorChoice() =>
        packetChoiceMode
            ? Volatile.Read(ref packetSelectedChoice)
            : string.Empty;

    public void StopPacketDetector()
    {
        packetDetectorRunning = false;
        Bot.Flash.FlashCall -= PacketDetectorFlashCall;
        Bot.Events.ScriptStopping -= OnPacketDetectorStopping;
        packetCommands = Array.Empty<string>();
        packetSelectedCommand = string.Empty;
        packetTexts = Array.Empty<string>();
        Volatile.Write(ref packetSelectedPacket, string.Empty);
        packetChoiceMode = false;
        packetSelectedChoice = string.Empty;
        packetSkillPauseChoice = string.Empty;
        packetPauseEveryChoice = false;
        packetDebounceMs = 0;
        Volatile.Write(ref lastPacketDetectionTimestamp, 0);
        Volatile.Write(ref skillEnginePaused, 0);
        Interlocked.Exchange(ref packetDetectionCount, 0);
    }

    public void ResumeSkillEngine() =>
        Volatile.Write(ref skillEnginePaused, 0);

    public void SetOrdinarySkillsSuppressed(bool suppressed) =>
        Volatile.Write(ref ordinarySkillsSuppressed, suppressed ? 1 : 0);

    public void RequestTaunt(int mapId)
    {
        if (mapId > 0)
            Volatile.Write(ref pendingTauntMapId, mapId);
    }

    public void RequestAbsolutePriorityTaunt(int mapId)
    {
        if (mapId > 0)
        {
            Volatile.Write(ref pendingAbsolutePriorityTauntTimestamp, Environment.TickCount64);
            Volatile.Write(ref pendingAbsolutePriorityTauntMapId, mapId);
        }
    }

    public bool HasPendingAbsolutePriorityTaunt() =>
        Volatile.Read(ref pendingAbsolutePriorityTauntMapId) > 0;

    public bool RequestImmediateTaunt(int mapId)
    {
        if (
            !Bot.Player.Alive
            || !IsTaunterRole()
            || mapId <= 0
            || GetMonsterHP(mapId) <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        Volatile.Write(ref pendingImmediateTauntMapId, mapId);
        return true;
    }

    public void RequestSkillFive(int mapId)
    {
        if (mapId > 0)
            Volatile.Write(ref pendingSkillFiveMapId, mapId);
    }

    public void RequestAbsolutePrioritySkill(int skill)
    {
        if (skill is >= 1 and <= 5)
            Volatile.Write(ref pendingAbsolutePrioritySkill, skill);
    }

    public bool HasPendingAbsolutePrioritySkill() =>
        Volatile.Read(ref pendingAbsolutePrioritySkill) > 0;

    public void RequestPrioritySkill(int skill)
    {
        if (skill is >= 1 and <= 4)
            Volatile.Write(ref pendingPrioritySkill, skill);
    }

    public bool HasPendingPrioritySkill() =>
        Volatile.Read(ref pendingPrioritySkill) > 0;

    public void RequestLimitedPrioritySkill(int skill, int uses)
    {
        if (skill is < 1 or > 4 || uses <= 0)
            return;

        Volatile.Write(
            ref pendingLimitedPrioritySkill,
            new LimitedPrioritySkillRequest(skill, uses)
        );
    }

    public bool HasPendingLimitedPrioritySkill() =>
        Volatile.Read(ref pendingLimitedPrioritySkill) != null;

    public bool RequestTargetedPrioritySkill(
        int skill,
        int targetMapId,
        int returnMapId
    )
    {
        if (
            skill is < 1 or > 4
            || targetMapId <= 0
            || returnMapId <= 0
        )
            return false;

        TargetedPrioritySkillRequest request = new(
            skill,
            targetMapId,
            returnMapId
        );
        return Interlocked.CompareExchange(
            ref pendingTargetedPrioritySkill,
            request,
            null
        ) == null;
    }

    public bool HasPendingTargetedPrioritySkill() =>
        Volatile.Read(ref pendingTargetedPrioritySkill) != null;

    public void SetShamanSkillThreeEnabled(bool enabled) =>
        Volatile.Write(ref shamanSkillThreeEnabled, enabled);

    public bool RequestImmediateSkillFive(int mapId)
    {
        if (
            !Bot.Player.Alive
            || mapId <= 0
            || GetMonsterHP(mapId) <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        Volatile.Write(ref pendingImmediateSkillFiveMapId, mapId);
        return true;
    }

    private bool OnScriptStopping(Exception? exception)
    {
        StopSkillEngine();
        return true;
    }

    private bool OnPacketDetectorStopping(Exception? exception)
    {
        StopPacketDetector();
        return true;
    }

    private void PacketDetectorFlashCall(string function, object[] args)
    {
        if (
            !packetDetectorRunning
            || !string.Equals(function, "pext", StringComparison.Ordinal)
            || args.Length == 0
            || args[0] is not string packet
        )
            return;

        string[] texts = packetTexts;
        bool choiceMode = packetChoiceMode;
        string selectedChoice = string.Empty;

        if (texts.Length == 0)
            return;

        if (choiceMode)
        {
            foreach (string text in texts)
            {
                if (!packet.Contains(text, StringComparison.Ordinal))
                    continue;

                selectedChoice = text;
                break;
            }

            if (selectedChoice.Length == 0)
                return;
        }
        else if (
            texts.Any(text =>
                !packet.Contains(text, StringComparison.Ordinal)
            )
        )
            return;

        if (!packetDetectorRunning)
            return;

        string selectedCommand = Volatile.Read(ref packetSelectedCommand);
        if (selectedCommand.Length == 0)
        {
            foreach (string command in packetCommands)
            {
                string commandMarker = $"\"cmd\":\"{command}\"";
                if (!packet.Contains(commandMarker, StringComparison.Ordinal))
                    continue;

                Interlocked.CompareExchange(
                    ref packetSelectedCommand,
                    command,
                    string.Empty
                );
                selectedCommand = Volatile.Read(ref packetSelectedCommand);
                break;
            }
        }

        if (
            selectedCommand.Length == 0
            || !packet.Contains(
                $"\"cmd\":\"{selectedCommand}\"",
                StringComparison.Ordinal
            )
            || !packetDetectorRunning
        )
            return;

        if (packetDebounceMs > 0)
        {
            long now = Environment.TickCount64;
            long last = Volatile.Read(ref lastPacketDetectionTimestamp);
            if (last > 0 && (now - last) < packetDebounceMs)
                return;

            Volatile.Write(ref lastPacketDetectionTimestamp, now);
        }

        if (choiceMode)
        {
            if (
                packetPauseEveryChoice
                || string.Equals(
                    selectedChoice,
                    Volatile.Read(ref packetSkillPauseChoice),
                    StringComparison.Ordinal
                )
            )
                Volatile.Write(ref skillEnginePaused, 1);

            Volatile.Write(ref packetSelectedChoice, selectedChoice);
        }

        Volatile.Write(ref packetSelectedPacket, packet);
        Interlocked.Increment(ref packetDetectionCount);
    }

    private void SkillEngineLoop()
    {
        try
        {
            while (skillEngineRunning && !Bot.ShouldExit)
            {
                if (!Bot.Player.Alive)
                {
                    if (
                        skillEngineMode is SkillEngineMode.Simple
                            or SkillEngineMode.LightCasterHealing
                            or SkillEngineMode.VoidHighlord
                            or SkillEngineMode.ChaosAvengerOptimized
                            or SkillEngineMode.ScionOfFlames
                    )
                        skillIndex = 0;

                    Volatile.Write(ref pendingAbsolutePrioritySkill, 0);
                    Volatile.Write(ref pendingPrioritySkill, 0);
                    Volatile.Write(ref pendingLimitedPrioritySkill, null);
                }

                int absolutePriorityTauntMapId = Volatile.Read(
                    ref pendingAbsolutePriorityTauntMapId
                );
                if (absolutePriorityTauntMapId > 0)
                {
                    long pendingSince = Volatile.Read(ref pendingAbsolutePriorityTauntTimestamp);
                    bool timedOut = pendingSince > 0 && (Environment.TickCount64 - pendingSince) > 3500;

                    if (
                        !Bot.Player.Alive
                        || !IsTaunterRole()
                        || GetMonsterHP(absolutePriorityTauntMapId) <= 0
                        || timedOut
                    )
                    {
                        Interlocked.CompareExchange(
                            ref pendingAbsolutePriorityTauntMapId,
                            0,
                            absolutePriorityTauntMapId
                        );
                        Volatile.Write(ref pendingAbsolutePriorityTauntTimestamp, 0);
                        Volatile.Write(ref skillEnginePaused, 0);
                        if (timedOut)
                        {
                            Core.Logger(
                                $"{LogPrefix} {role} absolute priority taunt on MapID {absolutePriorityTauntMapId} timed out after 3.5s (skill 5 unavailable). Resuming normal skills."
                            );
                        }
                    }
                    else
                    {
                        var target = Bot.Player.Target;
                        if (
                            !Bot.Player.HasTarget
                            || target?.MapID != absolutePriorityTauntMapId
                            || target?.HP <= 0
                        )
                            Bot.Combat.Attack(absolutePriorityTauntMapId);
                        else if (
                            Bot.Skills.CanUseSkill(5)
                            && Bot.Skills.UseSkill(5)
                        )
                        {
                            Interlocked.CompareExchange(
                                ref pendingAbsolutePriorityTauntMapId,
                                0,
                                absolutePriorityTauntMapId
                            );
                            Volatile.Write(ref pendingAbsolutePriorityTauntTimestamp, 0);
                            Volatile.Write(ref skillEnginePaused, 0);
                            Core.Logger(
                                $"{LogPrefix} {role} used absolute priority taunt."
                            );
                        }
                    }

                    Bot.Sleep(SkillPollDelay);
                    continue;
                }

                if (Volatile.Read(ref skillEnginePaused) > 0)
                {
                    Bot.Sleep(SkillPollDelay);
                    continue;
                }

                if (Bot.Combat.StopAttacking)
                {
                    Bot.Sleep(SkillPollDelay);
                    continue;
                }

                int absolutePrioritySkill = Volatile.Read(
                    ref pendingAbsolutePrioritySkill
                );
                bool absolutePrioritySkillPending = absolutePrioritySkill > 0;
                bool absolutePrioritySkillUsed = absolutePrioritySkillPending
                    && Bot.Skills.CanUseSkill(absolutePrioritySkill)
                    && Bot.Skills.UseSkill(absolutePrioritySkill);

                if (absolutePrioritySkillUsed)
                {
                    Interlocked.CompareExchange(
                        ref pendingAbsolutePrioritySkill,
                        0,
                        absolutePrioritySkill
                    );
                    Core.Logger($"{LogPrefix} {role} used absolute priority skill {absolutePrioritySkill}.");
                }

                if (absolutePrioritySkillPending)
                {
                    Bot.Sleep(SkillPollDelay);
                    continue;
                }

                int tauntMapId = Interlocked.Exchange(ref pendingTauntMapId, 0);

                if (tauntMapId > 0)
                    TauntMonster(tauntMapId);
                else
                {
                    int immediateTauntMapId = Interlocked.Exchange(
                        ref pendingImmediateTauntMapId,
                        0
                    );
                    bool immediateTauntUsed = immediateTauntMapId > 0
                        && ImmediateTaunt(immediateTauntMapId);

                    if (Volatile.Read(ref ordinarySkillsSuppressed) > 0)
                    {
                        Bot.Sleep(SkillPollDelay);
                        continue;
                    }

                    int skillFiveMapId = immediateTauntUsed
                        ? 0
                        : Interlocked.Exchange(ref pendingSkillFiveMapId, 0);
                    bool skillFiveUsed = !immediateTauntUsed
                        && skillFiveMapId > 0
                        && UseSkillFive(skillFiveMapId);

                    int immediateSkillFiveMapId = immediateTauntUsed || skillFiveUsed
                        ? 0
                        : Interlocked.Exchange(
                            ref pendingImmediateSkillFiveMapId,
                            0
                        );
                    bool immediateSkillFiveUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && immediateSkillFiveMapId > 0
                        && ImmediateSkillFive(immediateSkillFiveMapId);

                    TargetedPrioritySkillRequest? targetedPrioritySkill =
                        Volatile.Read(ref pendingTargetedPrioritySkill);
                    bool targetedPrioritySkillPending =
                        targetedPrioritySkill != null;
                    bool targetedPrioritySkillUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && targetedPrioritySkill != null
                        && UseTargetedPrioritySkill(targetedPrioritySkill);

                    if (targetedPrioritySkillUsed)
                    {
                        Interlocked.CompareExchange(
                            ref pendingTargetedPrioritySkill,
                            null,
                            targetedPrioritySkill
                        );
                    }

                    int prioritySkill = Volatile.Read(ref pendingPrioritySkill);
                    bool prioritySkillPending = prioritySkill > 0;
                    bool prioritySkillUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && !targetedPrioritySkillPending
                        && prioritySkillPending
                        && Bot.Skills.CanUseSkill(prioritySkill)
                        && Bot.Skills.UseSkill(prioritySkill);

                    if (prioritySkillUsed)
                    {
                        Interlocked.CompareExchange(
                            ref pendingPrioritySkill,
                            0,
                            prioritySkill
                        );
                        Core.Logger($"{LogPrefix} {role} used priority skill {prioritySkill}.");
                    }

                    LimitedPrioritySkillRequest? limitedPrioritySkill =
                        Volatile.Read(ref pendingLimitedPrioritySkill);
                    bool limitedPrioritySkillUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && !targetedPrioritySkillPending
                        && !prioritySkillPending
                        && limitedPrioritySkill != null
                        && Bot.Skills.CanUseSkill(limitedPrioritySkill.Skill)
                        && Bot.Skills.UseSkill(limitedPrioritySkill.Skill);

                    if (limitedPrioritySkillUsed && limitedPrioritySkill != null)
                    {
                        LimitedPrioritySkillRequest? replacement =
                            limitedPrioritySkill.RemainingUses > 1
                                ? new LimitedPrioritySkillRequest(
                                    limitedPrioritySkill.Skill,
                                    limitedPrioritySkill.RemainingUses - 1
                                )
                                : null;
                        LimitedPrioritySkillRequest? observed =
                            Interlocked.CompareExchange(
                                ref pendingLimitedPrioritySkill,
                                replacement,
                                limitedPrioritySkill
                            );
                        int remainingUses = observed == limitedPrioritySkill
                            ? replacement?.RemainingUses ?? 0
                            : Volatile.Read(ref pendingLimitedPrioritySkill)
                                ?.RemainingUses ?? 0;
                        Core.Logger(
                            $"{LogPrefix} {role} used limited priority skill {limitedPrioritySkill.Skill}. {remainingUses} uses remain."
                        );
                    }

                    bool potionUsed = !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && !targetedPrioritySkillPending
                        && !prioritySkillPending
                        && !limitedPrioritySkillUsed
                        && !string.IsNullOrWhiteSpace(maintainedPotion)
                        && MaintainPotion(maintainedPotion);

                    bool survivalSkillUsed = false;
                    if (
                        !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && !targetedPrioritySkillPending
                        && !prioritySkillPending
                        && !limitedPrioritySkillUsed
                        && !potionUsed
                        && survivalSkill is >= 1 and <= 4
                        && survivalHealthThreshold > 0
                        && Bot.Player.Alive
                        && (Bot.Player.MaxHealth > 0 && (double)Bot.Player.Health / Bot.Player.MaxHealth * 100 < survivalHealthThreshold)
                    )
                    {
                        if (Bot.Skills.CanUseSkill(survivalSkill) && Bot.Skills.UseSkill(survivalSkill))
                            survivalSkillUsed = true;
                    }

                    if (
                        !immediateTauntUsed
                        && !skillFiveUsed
                        && !immediateSkillFiveUsed
                        && !targetedPrioritySkillPending
                        && !prioritySkillPending
                        && !limitedPrioritySkillUsed
                        && !potionUsed
                        && !survivalSkillUsed
                    )
                    {
                        if (skillEngineMode == SkillEngineMode.Strict)
                            StrictSkillEngine(skillList, ref skillIndex);
                        else if (skillEngineMode == SkillEngineMode.KingsEcho)
                            KingsEchoSkillEngine(useSurvivalSkill);
                        else if (skillEngineMode == SkillEngineMode.ArcanaInvoker)
                            ArcanaInvokerSkillEngine();
                        else if (skillEngineMode == SkillEngineMode.ChronoShadowHunterStable)
                            CSSNormalMode();
                        else if (
                            skillEngineMode == SkillEngineMode.ChronoShadowHunterGunslinger
                        )
                            CSSGunslingerMode();
                        else if (skillEngineMode == SkillEngineMode.Shaman)
                            ShamanSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.LightCasterHealing
                        )
                            LightCasterHealingSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.VoidHighlord
                        )
                            VoidHighlordSkillEngine();
                        else if (
                            skillEngineMode
                            == SkillEngineMode.ChaosAvengerOptimized
                        )
                            ChaosAvengerOptimizedSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.ScionOfFlames
                        )
                            ScionOfFlamesSkillEngine();
                        else if (skillEngineMode == SkillEngineMode.Guardian)
                            GuardianSkillEngine();
                        else if (
                            skillEngineMode
                            == SkillEngineMode.QuantumChronomancer
                        )
                            QuantumChronomancerSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.ArachnomancerSolo
                        )
                            ArachnomancerSoloSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.ArchMageCorporeal
                        )
                            ArchMageCorporealSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.ArchMageAstral
                        )
                            ArchMageAstralSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.BeastMaster
                        )
                            BeastMasterSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.Berserker
                        )
                            BerserkerSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.ClassicSoulCleaver
                        )
                            ClassicSoulCleaverSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.ClassicPirate
                        )
                            ClassicPirateSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.DarkHarbinger
                        )
                            DarkHarbingerSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.DarkMetalNecro
                        )
                            DarkMetalNecroSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.DarkbloodStormKing
                        )
                            DarkbloodStormKingSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.DracoKnight
                        )
                            DracoKnightSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.EvolvedClawSuit
                        )
                            EvolvedClawSuitSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.EvolvedLeprechaun
                        )
                            EvolvedLeprechaunSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.EvolvedShaman
                        )
                            EvolvedShamanSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.GrungeRocker
                        )
                            GrungeRockerSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.HorcEvader
                        )
                            HorcEvaderSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.LegionSwordMasterAssassinSolo
                        )
                            LegionSwordMasterAssassinSoloSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.LegionSwordMasterAssassinFarm
                        )
                            LegionSwordMasterAssassinFarmSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.Pyromancer
                        )
                            PyromancerSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.ScarletSorceress
                        )
                            ScarletSorceressSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.SovereignOfStorms
                        )
                            SovereignOfStormsSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.UndeadSlayer
                        )
                            UndeadSlayerSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.Dragonslayer
                        )
                            DragonslayerSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.Assassin
                        )
                            AssassinSkillEngine();
                        else if (
                            skillEngineMode == SkillEngineMode.Simple
                            && blockedSimpleSkill is >= 1 and <= 4
                            && !string.IsNullOrWhiteSpace(
                                blockedSimpleSkillTargetAura
                            )
                        )
                            AuraBlockedSimpleSkillEngine();
                        else
                            CustomSkillEngine();
                    }
                }

                Bot.Sleep(
                    skillEngineMode == SkillEngineMode.ChronoShadowHunterGunslinger
                        && cssGunslingerFiring
                        ? CSSGunslingerFirePollDelay
                        : SkillPollDelay
                );
            }
        }
        catch (Exception ex)
        {
            Core.Logger($"{LogPrefix} skill engine stopped: {ex.Message}");
        }
        finally
        {
            skillEngineRunning = false;
            skillEnginePaused = 0;
            ordinarySkillsSuppressed = 0;
            pendingAbsolutePriorityTauntMapId = 0;
            pendingTauntMapId = 0;
            pendingImmediateTauntMapId = 0;
            pendingSkillFiveMapId = 0;
            pendingImmediateSkillFiveMapId = 0;
            pendingAbsolutePrioritySkill = 0;
            pendingPrioritySkill = 0;
            pendingTargetedPrioritySkill = null;
            pendingLimitedPrioritySkill = null;
            shamanSkillThreeEnabled = true;
            maintainedPotion = null;
            survivalSkill = 0;
            survivalHealthThreshold = 0;
        }
    }

    private bool UseTargetedPrioritySkill(
        TargetedPrioritySkillRequest request
    )
    {
        if (
            !Bot.Player.Alive
            || request.TargetMapId <= 0
            || GetMonsterHP(request.TargetMapId) <= 0
        )
            return false;

        Bot.Combat.Attack(request.TargetMapId);
        Bot.Sleep(100);

        if (
            !Bot.Player.Alive
            || !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != request.TargetMapId
            || Bot.Player.Target?.HP <= 0
            || !Bot.Skills.CanUseSkill(request.Skill)
            || !Bot.Skills.UseSkill(request.Skill)
        )
            return false;

        Core.Logger(
            $"{LogPrefix} {role} used targeted priority skill {request.Skill} on MapID {request.TargetMapId}."
        );

        if (GetMonsterHP(request.ReturnMapId) > 0)
            Bot.Combat.Attack(request.ReturnMapId);

        return true;
    }

    private bool ImmediateTaunt(int mapId)
    {
        if (
            !Bot.Player.Alive
            || !IsTaunterRole()
            || mapId <= 0
            || GetMonsterHP(mapId) <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (
            !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != mapId
            || Bot.Player.Target?.HP <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        if (!Bot.Skills.UseSkill(5))
            return false;

        Core.Logger($"{LogPrefix} {role} used immediate taunt.");
        return true;
    }

    private bool UseSkillFive(int mapId)
    {
        if (!Bot.Player.Alive || mapId <= 0 || GetMonsterHP(mapId) <= 0)
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return false;

        if (Bot.Skills.CanUseSkill(5))
        {
            Bot.Skills.UseSkill(5);
            Core.Logger($"{LogPrefix} {role} used skill 5 immediately.");
            Bot.Sleep(150);
            Bot.Combat.Attack(mapId);
            return true;
        }

        Core.Logger($"{LogPrefix} {role} waiting 750ms for skill 5.");
        Bot.Sleep(750);

        if (!Bot.Player.Alive || GetMonsterHP(mapId) <= 0)
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return false;

        if (Bot.Skills.CanUseSkill(5))
        {
            Bot.Skills.UseSkill(5);
            Core.Logger($"{LogPrefix} {role} used skill 5 after 750ms.");
            Bot.Sleep(150);
            Bot.Combat.Attack(mapId);
            return true;
        }

        Core.Logger($"{LogPrefix} {role} could not use skill 5 after 750ms.");
        Bot.Combat.Attack(mapId);
        return false;
    }

    private bool ImmediateSkillFive(int mapId)
    {
        if (
            !Bot.Player.Alive
            || mapId <= 0
            || GetMonsterHP(mapId) <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (
            !Bot.Player.HasTarget
            || Bot.Player.Target?.MapID != mapId
            || Bot.Player.Target?.HP <= 0
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        if (!Bot.Skills.UseSkill(5))
            return false;

        Core.Logger($"{LogPrefix} {role} used immediate skill 5.");
        return true;
    }

    public bool TauntMonster(int mapId)
    {
        if (!Bot.Player.Alive || !IsTaunterRole() || mapId <= 0 || GetMonsterHP(mapId) <= 0)
        {
            return false;
        }

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return false;

        if (Bot.Skills.CanUseSkill(5))
        {
            Bot.Skills.UseSkill(5);
            Core.Logger($"{LogPrefix} {role} used taunt immediately.");
            Bot.Sleep(150);
            Bot.Combat.Attack(mapId);
            return true;
        }

        Core.Logger($"{LogPrefix} {role} waiting 750ms for skill 5.");
        Bot.Sleep(750);

        if (!Bot.Player.Alive || GetMonsterHP(mapId) <= 0)
            return false;

        Bot.Combat.Attack(mapId);
        Bot.Sleep(100);

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return false;

        if (Bot.Skills.CanUseSkill(5))
        {
            Bot.Skills.UseSkill(5);
            Core.Logger($"{LogPrefix} {role} used taunt after 750ms.");
            Bot.Sleep(150);
            Bot.Combat.Attack(mapId);
            return true;
        }

        Core.Logger($"{LogPrefix} {role} could not use skill 5 after 750ms.");
        Bot.Combat.Attack(mapId);
        return false;
    }

    public void CustomSkillEngine()
    {
        if (!Bot.Player.Alive)
            return;

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        int[] skillList = GetSkillList();

        if (skillList.Length == 0)
            return;

        for (int offset = 0; offset < skillList.Length; offset++)
        {
            int index = (skillIndex + offset) % skillList.Length;
            int skill = skillList[index];

            if (!Bot.Skills.CanUseSkill(skill))
                continue;

            Bot.Skills.UseSkill(skill);
            skillIndex = (index + 1) % skillList.Length;
            return;
        }
    }

    private void AuraBlockedSimpleSkillEngine()
    {
        if (!Bot.Player.Alive)
            return;

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        int[] skills = GetSkillList();

        if (skills.Length == 0)
            return;

        bool blockSkill = Bot.Target.GetAura(blockedSimpleSkillTargetAura) != null;

        for (int offset = 0; offset < skills.Length; offset++)
        {
            int index = (skillIndex + offset) % skills.Length;
            int skill = skills[index];

            if (blockSkill && skill == blockedSimpleSkill)
                continue;

            if (!Bot.Skills.CanUseSkill(skill))
                continue;

            Bot.Skills.UseSkill(skill);
            skillIndex = (index + 1) % skills.Length;
            return;
        }
    }

    private void GuardianSkillEngine()
    {
        if (!Bot.Player.Alive)
            return;

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        float guardianSpirit = Bot.Self.Auras
            .Where(aura =>
                aura.Name.Equals(
                    GuardianSpiritAura,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(aura => aura.Value)
            .DefaultIfEmpty(0)
            .Max();
        bool skillFourBlocked =
            blockedSimpleSkill == 4
            && !string.IsNullOrWhiteSpace(blockedSimpleSkillTargetAura)
            && Bot.Target.GetAura(blockedSimpleSkillTargetAura) != null;

        if (guardianSpirit >= 15 && !skillFourBlocked)
        {
            if (Bot.Skills.CanUseSkill(4))
                Bot.Skills.UseSkill(4);

            return;
        }

        CustomSkillEngine();
    }

    private void ChaosAvengerOptimizedSkillEngine()
    {
        if (!Bot.Player.Alive)
            return;

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        if (Bot.Target.GetAura("Branded") != null)
            return;

        CustomSkillEngine();
    }

    private void ScionOfFlamesSkillEngine()
    {
        if (!Bot.Player.Alive)
            return;

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        if (Bot.Self.HasActiveAura("Fuel The Flame"))
        {
            if (Bot.Skills.CanUseSkill(4))
                Bot.Skills.UseSkill(4);

            return;
        }

        CustomSkillEngine();
    }

    private void LightCasterHealingSkillEngine()
    {
        if (Bot.Skills.CanUseSkill(3))
        {
            Bot.Skills.UseSkill(3);
            return;
        }

        CustomSkillEngine();
    }

    private void VoidHighlordSkillEngine()
    {
        double healthPercentage =
            Bot.Player.MaxHealth > 0
                ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
                : 0;

        if (
            healthPercentage > 90
            && Bot.Skills.CanUseSkill(3)
            && Bot.Skills.UseSkill(3)
        )
            return;

        CustomSkillEngine();
    }

    public void DirectSkillEngine()
    {
        if (!Bot.Player.Alive)
            return;

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        int[] skillList = GetSkillList();

        if (skillList.Length == 0)
            return;

        for (int offset = 0; offset < skillList.Length; offset++)
        {
            int index = (skillIndex + offset) % skillList.Length;

            if (!Bot.Skills.UseSkill(skillList[index]))
                continue;

            skillIndex = (index + 1) % skillList.Length;
            return;
        }
    }

    public void StrictSkillEngine(int[] skills, ref int index)
    {
        if (!Bot.Player.Alive)
        {
            index = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        if (skills.Length == 0)
            return;

        int skill = skills[index];

        if (
            skill == blockedStrictSkill
            && (
                (
                    !string.IsNullOrWhiteSpace(blockedStrictSkillSelfAura)
                    && Bot.Self.HasActiveAura(blockedStrictSkillSelfAura)
                )
                || (
                    !string.IsNullOrWhiteSpace(blockedStrictSkillTargetAura)
                    && Bot.Target.GetAura(blockedStrictSkillTargetAura) != null
                )
            )
        )
        {
            index = (index + 1) % skills.Length;
            return;
        }

        if (!Bot.Skills.CanUseSkill(skill))
            return;

        if (!Bot.Skills.UseSkill(skill))
            return;

        index = (index + 1) % skills.Length;
    }

    public void QuantumChronomancerSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        float temporalRift = Bot.Self.GetAura("Temporal Rift")?.Value ?? 0;

        // When Temporal Rift is at 4 stacks or more, execute 3 -> 1 -> 4
        if (temporalRift >= 4)
        {
            if (Bot.Skills.CanUseSkill(3) && Bot.Skills.UseSkill(3))
            {
                FileLog("[QCM] Used Skill 3 at 4+ Temporal Rift stacks.");
                return;
            }

            if (Bot.Skills.CanUseSkill(1) && Bot.Skills.UseSkill(1))
            {
                FileLog("[QCM] Used Skill 1 in burst sequence.");
                return;
            }

            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[QCM] Fired Nuke (Skill 4) at {temporalRift} Temporal Rift stacks.");
                return;
            }
        }

        // When under 4 stacks (or waiting for cooldowns): spam skills 1 and 2
        CustomSkillEngine();
    }

    private static double GetAuraSecondsRemaining(Aura? aura)
    {
        if (aura == null || aura.UnixTimeStamp <= 0 || aura.Duration <= 0)
            return 0;

        double remaining = (DateTimeOffset.FromUnixTimeMilliseconds(aura.UnixTimeStamp)
            .AddSeconds(aura.Duration) - DateTimeOffset.UtcNow).TotalSeconds;
        return Math.Max(0, remaining);
    }

    public string GetTargetRace()
    {
        var target = Bot.Player.Target;
        if (target == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(target.Race))
            return target.Race;

        var currentMon = Bot.Monsters.CurrentMonsters?.FirstOrDefault(m => m.MapID == target.MapID);
        if (!string.IsNullOrWhiteSpace(currentMon?.Race))
            return currentMon.Race;

        var mapMon = Bot.Monsters.MapMonsters?.FirstOrDefault(m => m.MapID == target.MapID || m.ID == target.ID);
        if (!string.IsNullOrWhiteSpace(mapMon?.Race))
            return mapMon.Race;

        return string.Empty;
    }

    public bool IsTargetRace(string raceName)
    {
        if (string.IsNullOrWhiteSpace(raceName))
            return false;

        return GetTargetRace().Contains(raceName, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsTargetDragon() => IsTargetRace("Dragon");

    public bool IsTargetUndead() => IsTargetRace("Undead");

    public bool IsTargetChaos() => IsTargetRace("Chaos");

    public void ArachnomancerSoloSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // 1. Skill 4: Slingshot Web (Cocooned debuff on target < 3.0s or missing)
        var cocoonedAura = Bot.Target.GetAura("Cocooned");
        double cocoonedTime = GetAuraSecondsRemaining(cocoonedAura);
        double healthPct = Bot.Player.MaxHealth > 0
            ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
            : 0;
        if ((cocoonedAura == null || cocoonedTime < 3.0) & healthPct > 50)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog("[ArachnoSolo] Cast Skill 4 (Cocooned refreshed).");
                return;
            }
        }

        // 2. Skill 3: Toxic Mist (Panic debuff on target < 3.0s or missing)
        var panicAura = Bot.Target.GetAura("Panic");
        double panicTime = GetAuraSecondsRemaining(panicAura);
        if ((panicAura == null || panicTime < 3.0) & healthPct > 50)
        {
            if (Bot.Skills.CanUseSkill(3) && Bot.Skills.UseSkill(3))
            {
                FileLog("[ArachnoSolo] Cast Skill 3 (Panic refreshed).");
                return;
            }
        }

        // 3. Fall through to spamming skills 1 & 2
        CustomSkillEngine();
    }

    public void ArchMageCorporealSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var corporeal = Bot.Self.GetAura("Corporeal Ascension");
        if (corporeal == null)
        {
            var arcaneFlux = Bot.Self.GetAura("Arcane Flux");
            if (arcaneFlux == null)
            {
                if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
                {
                    FileLog("[ArchMageCorporeal] Cast Skill 2 to apply Arcane Flux.");
                    return;
                }
            }
            else
            {
                if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
                {
                    FileLog("[ArchMageCorporeal] Cast Skill 4 to enter Corporeal Ascension.");
                    return;
                }
            }
            return;
        }

        double manaPct = Bot.Player.MaxMana > 0
            ? (double)Bot.Player.Mana / Bot.Player.MaxMana * 100
            : Bot.Player.Mana;

        if (manaPct < 25)
        {
            if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
            {
                FileLog($"[ArchMageCorporeal] Cast Skill 2 for mana recovery (Mana: {manaPct:F1}%).");
                return;
            }
        }

        // Fall through to default skill engine for baseline skills 1 and 3
        CustomSkillEngine();
    }

    public void ArchMageAstralSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var astral = Bot.Self.GetAura("Astral Ascension");
        if (astral == null)
        {
            var corporeal = Bot.Self.GetAura("Corporeal Ascension");
            if (corporeal != null)
            {
                if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
                {
                    FileLog("[ArchMageAstral] Cast Skill 4 to shift from Corporeal to Astral Ascension.");
                    return;
                }
                return;
            }

            var arcaneFlux = Bot.Self.GetAura("Arcane Flux");
            if (arcaneFlux == null)
            {
                if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
                {
                    FileLog("[ArchMageAstral] Cast Skill 2 to apply Arcane Flux.");
                    return;
                }
            }
            else
            {
                if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
                {
                    FileLog("[ArchMageAstral] Cast Skill 4 to activate Corporeal Ascension.");
                    return;
                }
            }
            return;
        }

        double healthPct = Bot.Player.MaxHealth > 0
            ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
            : 0;

        var arcaneSigilAura = Bot.Self.GetAura("Arcane Sigil");
        double arcaneSigilTime = GetAuraSecondsRemaining(arcaneSigilAura);

        if (healthPct > 80 && (arcaneSigilAura == null || arcaneSigilTime < 2.0))
        {
            var arcaneFlux = Bot.Self.GetAura("Arcane Flux");
            if (arcaneFlux != null)
            {
                // Stop casting attack skills to allow Arcane Flux to expire
                return;
            }

            // During Arcane Flux gap, immediately cast Skill 4 to sacrifice 40% HP & MP
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[ArchMageAstral] Cast Skill 4 in Flux gap to sacrifice HP/MP (HP: {healthPct:F1}%, Sigil: {arcaneSigilTime:F1}s).");
                return;
            }
            return;
        }

        // Fall through to default skill engine for baseline skills 2, 1, 3
        CustomSkillEngine();
    }

    public void BeastMasterSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        if (Bot.Self.GetAura("Follow Up") == null)
            return;

        // Fall through to default skill engine for baseline skills 2, 3, 1, 4
        CustomSkillEngine();
    }

    public void BerserkerSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var berserkAura = Bot.Self.GetAura("Berserk");
        double healthPct = Bot.Player.MaxHealth > 0
            ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
            : 0;

        // 1. Always start with 4 and use 4 whenever aura Berserk is not applied
        if (berserkAura == null)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog("[Berserker] Cast Skill 4 (Apply Berserk).");
                return;
            }
        }
        // 2. If Berserk is applied and health < 20%, press 4
        else if (healthPct < 20)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[Berserker] Cast Skill 4 (Emergency heal/reset, HP: {healthPct:F1}%).");
                return;
            }
        }

        // 3. Use 2 only if health > 60%
        if (healthPct > 60)
        {
            if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
            {
                FileLog($"[Berserker] Cast Skill 2 (HP: {healthPct:F1}%).");
                return;
            }
        }

        // 4. Fall through to default skill engine for baseline skills 1 and 3
        CustomSkillEngine();
    }

    public void ClassicSoulCleaverSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var bloodPrice = Bot.Self.GetAura("Blood Price");
        double healthPct = Bot.Player.MaxHealth > 0
            ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
            : 0;

        // 1. 4 only if aura Blood Price is not applied and health > 50%
        if (bloodPrice == null && healthPct > 50)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[ClassicSoulCleaver] Cast Skill 4 (Apply Blood Price, HP: {healthPct:F1}%).");
                return;
            }
        }

        // 2. 2 only if health < 15%
        if (healthPct < 15)
        {
            if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
            {
                FileLog($"[ClassicSoulCleaver] Cast Skill 2 (Emergency heal, HP: {healthPct:F1}%).");
                return;
            }
        }

        // 3. Fall through to default skill engine for baseline spam skills 1 and 3
        CustomSkillEngine();
    }

    public void DarkHarbingerSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var bloodPrice = Bot.Self.GetAura("Blood Price");
        double healthPct = Bot.Player.MaxHealth > 0
            ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
            : 0;

        // 1. 4 only if aura Blood Price is not applied and health > 30%
        if (bloodPrice == null && healthPct > 30)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[DarkHarbinger] Cast Skill 4 (Apply Blood Price, HP: {healthPct:F1}%).");
                return;
            }
        }

        // 2. 2 only if health < 15%
        if (healthPct < 15)
        {
            if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
            {
                FileLog($"[DarkHarbinger] Cast Skill 2 (Emergency heal, HP: {healthPct:F1}%).");
                return;
            }
        }

        // 3. Fall through to default skill engine for baseline spam skills 1 and 3
        CustomSkillEngine();
    }

    public void DarkMetalNecroSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var rev = Bot.Self.GetAura("Reverberating") ?? Bot.Target.GetAura("Reverberating");
        var darkPact = Bot.Self.GetAura("Dark Pact");
        float darkPactStacks = darkPact?.Value ?? 0;
        double darkPactTime = GetAuraSecondsRemaining(darkPact);
        double healthPct = Bot.Player.MaxHealth > 0
            ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
            : 0;

        // 1. Always start with 1 if no combat auras are active yet
        if (rev == null && darkPact == null)
        {
            if (Bot.Skills.CanUseSkill(1) && Bot.Skills.UseSkill(1))
            {
                FileLog("[DarkMetalNecro] Cast Skill 1 (Opener).");
                return;
            }
        }

        // 2. If Reverberating aura is applied and Dark Pact < 3 stacks or <= 1.0s left, and health > 40%, use 4
        if (rev != null && (darkPact == null || darkPactStacks < 3 || darkPactTime <= 1.0) && healthPct > 40)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[DarkMetalNecro] Cast Skill 4 (Dark Pact, Stacks: {darkPactStacks}, Time: {darkPactTime:F1}s, HP: {healthPct:F1}%).");
                return;
            }
        }

        // 3. Otherwise spam 1, 2, 3
        CustomSkillEngine();
    }

    public void DarkbloodStormKingSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // 1. Use 4 when Conductive >= 50
        float conductive = Bot.Self.GetAura("Conductive")?.Value ?? Bot.Target.GetAura("Conductive")?.Value ?? 0;
        if (conductive >= 50)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[DarkbloodStormKing] Cast Skill 4 (Storm Bolt, Conductive: {conductive}).");
                return;
            }
        }

        // 2. Otherwise spam 3, 1, 2, 1 in strict mode
        StrictSkillEngine(skillList, ref skillIndex);
    }

    public void DracoKnightSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var dumbfounded = Bot.Target.GetAura("Dumbfounded");
        var flammable = Bot.Target.GetAura("Flammable");

        // 1. Use 3 only if Dumbfounded aura is existing
        if (dumbfounded != null)
        {
            if (Bot.Skills.CanUseSkill(3) && Bot.Skills.UseSkill(3))
            {
                FileLog("[DracoKnight] Cast Skill 3 (Swordplay with Dumbfounded).");
                return;
            }
        }

        // 2. Use 4 only if Flammable aura is existing
        if (flammable != null)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog("[DracoKnight] Cast Skill 4 (Dragon Breath with Flammable).");
                return;
            }
        }

        // Otherwise spam 2, 1
        CustomSkillEngine();
    }

    public void EvolvedClawSuitSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // Health below 60% use 1
        if (Bot.Player.Health < Bot.Player.MaxHealth * 0.60f)
        {
            if (Bot.Skills.CanUseSkill(1) && Bot.Skills.UseSkill(1))
            {
                FileLog("[EvolvedClawSuit] Cast Skill 1 (Health < 60%).");
                return;
            }
        }

        // Simple spam 2, 3, 4
        CustomSkillEngine();
    }

    public void EvolvedLeprechaunSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var keen = Bot.Self.GetAura("Keen");

        // 1 if aura Keen is not applied (avoid refreshing early)
        if (keen == null)
        {
            if (Bot.Skills.CanUseSkill(1) && Bot.Skills.UseSkill(1))
            {
                FileLog("[EvolvedLeprechaun] Cast Skill 1 (Apply Keen).");
                return;
            }
        }

        // 2 if aura Keen < 1 sec remaining (maximize damage bonus)
        if (keen != null && GetAuraSecondsRemaining(keen) < 1.0)
        {
            if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
            {
                FileLog("[EvolvedLeprechaun] Cast Skill 2 (Keen aura < 1 sec remaining).");
                return;
            }
        }

        // Simple skills 3, 4
        CustomSkillEngine();
    }

    public void EvolvedShamanSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // Emergency heal / Safe Skill 3 when HP < 60%
        if (
            Bot.Player.MaxHealth > 0
            && (double)Bot.Player.Health / Bot.Player.MaxHealth * 100 < 60
        )
        {
            if (Bot.Skills.CanUseSkill(3) && Bot.Skills.UseSkill(3))
            {
                FileLog("[EvolvedShaman] Cast Skill 3 (Health < 60%).");
                return;
            }
        }

        // Refresh skill 4 when Elemental Grasp < 2 sec time left or missing
        var elementalGrasp = Bot.Target.GetAura("Elemental Grasp");
        double graspTime = GetAuraSecondsRemaining(elementalGrasp);
        if (elementalGrasp == null || graspTime < 2.0)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog("[EvolvedShaman] Cast Skill 4 (Elemental Grasp < 2s).");
                return;
            }
        }

        // Simple skills 1, 2
        CustomSkillEngine();
    }

    public void GrungeRockerSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // Enraged Power time left < 1 or not on, use 4
        var enragedPower = Bot.Self.GetAura("Enraged Power") ?? Bot.Target.GetAura("Enraged Power");
        double enragedPowerTime = GetAuraSecondsRemaining(enragedPower);
        if (enragedPower == null || enragedPowerTime < 1.0)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog("[GrungeRocker] Cast Skill 4 (Enraged Power < 1s or missing).");
                return;
            }
        }

        // Simple skills 1, 2, 3
        CustomSkillEngine();
    }

    public void HorcEvaderSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // Use 2 if Take Cover aura is not on or if has < 1 sec
        var takeCover = Bot.Self.GetAura("Take Cover") ?? Bot.Target.GetAura("Take Cover");
        double takeCoverTime = GetAuraSecondsRemaining(takeCover);
        if (takeCover == null || takeCoverTime < 1.0)
        {
            if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
            {
                FileLog("[HorcEvader] Cast Skill 2 (Take Cover < 1s or missing).");
                return;
            }
        }

        // Simple skills 1, 3, 4
        CustomSkillEngine();
    }

    public void LegionSwordMasterAssassinSoloSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // If Burning Sensation aura is missing, press 3
        var burningSensation = Bot.Target.GetAura("Burning Sensation") ?? Bot.Self.GetAura("Burning Sensation");
        if (burningSensation == null)
        {
            if (Bot.Skills.CanUseSkill(3) && Bot.Skills.UseSkill(3))
            {
                FileLog("[LSMA-Solo] Cast Skill 3 (Burning Sensation missing).");
                return;
            }
        }

        // Simple skills 1, 2, 4
        CustomSkillEngine();
    }

    public void LegionSwordMasterAssassinFarmSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // If Blood Arts != 1 or is missing, press 3
        var bloodArts = Bot.Self.GetAura("Blood Arts") ?? Bot.Target.GetAura("Blood Arts");
        float bloodArtsStacks = bloodArts?.Value ?? 0;
        if (bloodArts == null || bloodArtsStacks != 1)
        {
            if (Bot.Skills.CanUseSkill(3) && Bot.Skills.UseSkill(3))
            {
                FileLog($"[LSMA-Farm] Cast Skill 3 (Blood Arts {(bloodArts == null ? "missing" : $"stack {bloodArtsStacks} != 1")}).");
                return;
            }
        }

        // Simple skills 1, 2, 4
        CustomSkillEngine();
    }

    public void PyromancerSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var scorch = Bot.Target.GetAura("Scorch") ?? Bot.Self.GetAura("Scorch");
        if (scorch == null)
        {
            // Only skill 1 applies Scorch. Fire skill 1 to apply Scorch before any other skill is used.
            if (Bot.Skills.CanUseSkill(1) && Bot.Skills.UseSkill(1))
            {
                FileLog("[Pyromancer] Cast Skill 1 (Apply Scorch).");
                return;
            }

            return;
        }

        // Scorch is active: check safe skill 3 when HP < 60%
        double healthPct = Bot.Player.MaxHealth > 0
            ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
            : 100;
        if (healthPct < 60 && Bot.Skills.CanUseSkill(3) && Bot.Skills.UseSkill(3))
        {
            FileLog($"[Pyromancer] Cast Skill 3 (Safe Skill HP {healthPct:F1}% < 60%).");
            return;
        }

        // Fall through to simple engine for rotation 1, 2, 4
        CustomSkillEngine();
    }

    public void ScarletSorceressSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var vermilionPact = Bot.Self.GetAura("Vermilion Pact") ?? Bot.Target.GetAura("Vermilion Pact");
        float stacks = vermilionPact?.Value ?? 0;
        double timeLeft = GetAuraSecondsRemaining(vermilionPact);

        // If Vermilion Pact < 5, press 4. Else if Vermilion Pact aura time left < 2 sec press 4
        if (stacks < 5 || (vermilionPact != null && timeLeft < 2.0))
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[ScarletSorceress] Cast Skill 4 (Vermilion Pact stacks={stacks}, timeLeft={timeLeft:F1}s).");
                return;
            }
        }

        // Spam 1, 2, 3
        CustomSkillEngine();
    }

    public void SovereignOfStormsSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // If health under 60%, press 2, and then 1 (similar model to quantum chronomancer)
        double hpPercent = Bot.Player.MaxHealth > 0
            ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
            : 100;

        if (hpPercent < 60)
        {
            if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
            {
                FileLog("[Sovereign of Storms] Used Skill 2 (Health < 60%).");
                return;
            }

            if (Bot.Skills.CanUseSkill(1) && Bot.Skills.UseSkill(1))
            {
                FileLog("[Sovereign of Storms] Used Skill 1 (Health < 60%).");
                return;
            }
        }

        // If aura Superconductive >= 20, press 4
        float superconductive = Bot.Self.GetAura("Superconductive")?.Value ?? Bot.Target.GetAura("Superconductive")?.Value ?? 0;
        if (superconductive >= 20)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[Sovereign of Storms] Cast Skill 4 (Superconductive: {superconductive}).");
                return;
            }
        }

        // Base spam loop / Fallthrough (simple 3, 1)
        CustomSkillEngine();
    }

    public void UndeadSlayerSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // 4 if Spirit Power >= 200
        float spiritPower = Bot.Self.GetAura("Spirit Power")?.Value ?? Bot.Target.GetAura("Spirit Power")?.Value ?? 0;
        if (spiritPower >= 200)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog($"[UndeadSlayer] Fired Skill 4 (Dragon Lance, Spirit Power: {spiritPower}).");
                return;
            }
        }

        // Base spam loop / Fallthrough (simple 1, 3, 2)
        CustomSkillEngine();
    }

    public void DragonslayerSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        // Skill 4: Dragon's Bane (50 Mana, applies Dragonbane only if target is Dragonkin)
        if (IsTargetDragon() && Bot.Player.Mana >= 50)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog("[Dragonslayer] Cast Skill 4 (Dragon's Bane on Dragonkin).");
                return;
            }
        }

        // Base spam loop / Fallthrough (simple 1, 2, 3)
        CustomSkillEngine();
    }

    public void AssassinSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var vanish = Bot.Self.GetAura("Vanish") ?? Bot.Target.GetAura("Vanish");

        // Use 2 if aura Vanish is not active
        if (vanish == null)
        {
            if (Bot.Skills.CanUseSkill(2) && Bot.Skills.UseSkill(2))
            {
                FileLog("[Assassin] Cast Skill 2 (Vanish not active).");
                return;
            }
        }

        // Use 3 if aura Vanish < 1 sec time remaining
        if (vanish != null && GetAuraSecondsRemaining(vanish) < 1.0)
        {
            if (Bot.Skills.CanUseSkill(3) && Bot.Skills.UseSkill(3))
            {
                FileLog("[Assassin] Cast Skill 3 (Vanish < 1s remaining).");
                return;
            }
        }

        // Simple skills 4, 1
        CustomSkillEngine();
    }

    public void ClassicPirateSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var vipersKiss = Bot.Target.GetAura("Viper's Kiss");

        // 1 if aura Viper's Kiss is not applied to enemy (avoid refreshing early)
        if (vipersKiss == null)
        {
            if (Bot.Skills.CanUseSkill(1) && Bot.Skills.UseSkill(1))
            {
                FileLog("[ClassicPirate] Cast Skill 1 (Apply Viper's Kiss).");
                return;
            }
        }

        // Skill 4 (Opportunity's Strike) deals more damage the closer Viper's Kiss is to expiring (<= 1.0s remaining)
        if (vipersKiss != null && GetAuraSecondsRemaining(vipersKiss) <= 1.0)
        {
            if (Bot.Skills.CanUseSkill(4) && Bot.Skills.UseSkill(4))
            {
                FileLog("[ClassicPirate] Cast Skill 4 (Opportunity's Strike on expiring Viper's Kiss).");
                return;
            }
        }

        // Simple skills 2, 3
        CustomSkillEngine();
    }

    public void KingsEchoSkillEngine(bool useSurvivalSkill)
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        float residualEnergy = Bot.Self.GetAura("Residual Energy")?.Value ?? 0;

        if (
            (residualEnergy >= 8 || Bot.Player.Mana < kingsEchoManaThreshold)
            && Bot.Skills.CanUseSkill(4)
            && Bot.Skills.UseSkill(4)
        )
        {
            FileLog($"[KingsEcho] Cast Skill 4 (Residual Energy: {residualEnergy}, Mana: {Bot.Player.Mana}).");
            return;
        }

        double healthPercentage =
            Bot.Player.MaxHealth > 0
                ? (double)Bot.Player.Health / Bot.Player.MaxHealth * 100
                : 0;
        double manaPercentage =
            Bot.Player.MaxMana > 0 ? (double)Bot.Player.Mana / Bot.Player.MaxMana * 100 : 0;

        if (
            useSurvivalSkill
            && healthPercentage < 50
            && manaPercentage > 24
            && Bot.Skills.CanUseSkill(3)
            && Bot.Skills.UseSkill(3)
        )
            return;

        CustomSkillEngine();
    }

    private void ArcanaInvokerSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var worldAura = Bot.Self.GetAura(ArcanaInvokerWorldAura);
        if (worldAura != null)
        {
            if (worldAura.RemainingTime > 1)
                DirectSkillEngine();

            else
            {
                skillIndex = 0;
                if (Bot.Skills.CanUseSkill(1))
                    Bot.Skills.UseSkill(1);
            }

            return;
        }

        if (
            Bot.Self.HasActiveAura(ArcanaInvokerFoolAura)
            && !Bot.Self.HasActiveAura(ArcanaInvokerJudgementAura)
        )
        {
            DirectSkillEngine();
            return;
        }

        skillIndex = 0;
        if (Bot.Skills.CanUseSkill(1))
            Bot.Skills.UseSkill(1);
    }

    private void ShamanSkillEngine()
    {
        if (!Bot.Player.Alive)
        {
            skillIndex = 0;
            return;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        var elementalEmbrace = Bot.Target.GetAura(ShamanElementalEmbraceAura);
        if (
            (elementalEmbrace == null || elementalEmbrace.RemainingTime <= 30)
            && Bot.Skills.CanUseSkill(4)
            && Bot.Skills.UseSkill(4)
        )
            return;

        if (
            Volatile.Read(ref shamanSkillThreeEnabled)
            && Bot.Player.Mana > 70
            && Bot.Skills.CanUseSkill(3)
            && Bot.Skills.UseSkill(3)
        )
            return;

        CustomSkillEngine();
    }

    private void CSSNormalMode()
    {
        if (!Bot.Player.Alive)
        {
            ResetCSSNormalMode();
            return;
        }

        if (cssNormalInitialManaCheck)
        {
            if (Bot.Player.Mana < 100)
            {
                if (Bot.Skills.UseSkill(1))
                    cssNormalInitialManaCheck = false;
                return;
            }

            cssNormalInitialManaCheck = false;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        if (cssNormalNeedsRegeneration)
        {
            if (Bot.Skills.UseSkill(1))
                cssNormalNeedsRegeneration = false;
            return;
        }

        if (
            Bot.Player.Mana < 5
            || Bot.Self.HasActiveAura(ChronoShadowHunterRoundsEmptyAura)
        )
        {
            if (Bot.Skills.UseSkill(4))
                cssNormalNeedsRegeneration = true;
            return;
        }

        Bot.Skills.UseSkill(3);
    }

    private void ResetCSSNormalMode()
    {
        cssNormalInitialManaCheck = true;
        cssNormalNeedsRegeneration = false;
    }

    private void CSSGunslingerMode()
    {
        if (!Bot.Player.Alive)
        {
            ResetCSSGunslingerMode();
            return;
        }

        if (cssGunslingerInitialManaCheck)
        {
            if (Bot.Player.Mana < 100)
            {
                if (Bot.Skills.UseSkill(1))
                    cssGunslingerInitialManaCheck = false;
                return;
            }

            cssGunslingerInitialManaCheck = false;
        }

        if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
            return;

        if (cssGunslingerNeedsRegeneration)
        {
            if (Bot.Skills.UseSkill(1))
                cssGunslingerNeedsRegeneration = false;
            return;
        }

        if (cssGunslingerWaitingForStance)
        {
            if (!Bot.Self.HasActiveAura(ChronoShadowHunterGunslingerAura))
                return;

            cssGunslingerWaitingForStance = false;
            cssGunslingerFiring = true;
        }

        if (cssGunslingerFiring)
        {
            if (Bot.Player.Mana < 10)
            {
                if (Bot.Skills.UseSkill(4))
                {
                    cssGunslingerFiring = false;
                    cssGunslingerNeedsRegeneration = true;
                }
                return;
            }

            Bot.Skills.UseSkill(0);
            return;
        }

        if (
            Bot.Player.Mana < 5
            || Bot.Self.HasActiveAura(ChronoShadowHunterRoundsEmptyAura)
        )
        {
            if (Bot.Skills.UseSkill(1))
                cssGunslingerWaitingForStance = true;
            return;
        }

        Bot.Skills.UseSkill(3);
    }

    private void ResetCSSGunslingerMode()
    {
        cssGunslingerInitialManaCheck = true;
        cssGunslingerWaitingForStance = false;
        cssGunslingerFiring = false;
        cssGunslingerNeedsRegeneration = false;
    }

    private bool IsTaunterRole() => isTaunter;

    private int[] GetSkillList() => skillList;

    public int GetMonsterHP(string monsterName)
    {
        var monsters = Bot.Monsters?.CurrentAvailableMonsters;
        if (monsters == null || string.IsNullOrEmpty(monsterName))
            return 0;

        foreach (var monster in monsters)
        {
            if (monster != null && string.Equals(monster.Name, monsterName, StringComparison.OrdinalIgnoreCase))
                return monster.HP;
        }

        return 0;
    }

    public bool IsMonsterAlive(string monsterName)
    {
        var monsters = Bot.Monsters?.CurrentAvailableMonsters;
        if (monsters == null) return false;
        return monsters.Any(m => string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase) && m.HP > 0);
    }

    public void JumpToMonsterCell(string monsterName)
    {
        var mon = Bot.Monsters?.MapMonsters?.FirstOrDefault(m =>
            string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase));
        if (mon != null && !string.IsNullOrEmpty(mon.Cell))
        {
            Core.Jump(mon.Cell, "Left");
            Bot.Wait.ForCellChange(mon.Cell);
        }
    }

    public void MaintainTarget(string monsterName)
    {
        if (!Bot.Player.Alive || string.IsNullOrEmpty(monsterName) || !IsMonsterAlive(monsterName))
            return;

        var target = Bot.Player.Target;
        if (!Bot.Player.HasTarget || !string.Equals(target?.Name, monsterName, StringComparison.OrdinalIgnoreCase) || target?.HP <= 0)
        {
            var mon = Bot.Monsters?.CurrentAvailableMonsters?.FirstOrDefault(m =>
                string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase) && m.HP > 0);
            if (mon != null && mon.MapID > 0)
                Bot.Combat.Attack(mon.MapID);
            else
                Bot.Combat.Attack(monsterName);
        }
    }

    public int GetMonsterHP(int mapId)
    {
        var monsters = Bot.Monsters?.MapMonsters;
        if (mapId <= 0 || monsters == null)
            return 0;

        foreach (var monster in monsters)
        {
            if (monster != null && monster.MapID == mapId)
                return monster.HP;
        }

        return 0;
    }

    public bool IsMonsterAlive(int mapId) => GetMonsterHP(mapId) > 0;

    public void MaintainTarget(int mapId)
    {
        if (!Bot.Player.Alive || mapId <= 0 || !IsMonsterAlive(mapId))
            return;

        var target = Bot.Player.Target;
        if (
            !Bot.Player.HasTarget
            || target?.MapID != mapId
            || target?.HP <= 0
        )
            Bot.Combat.Attack(mapId);
    }

    public void GenericPrebuff()
    {
        if (
            string.Equals(
                Bot.Player.CurrentClass?.Name,
                "Guardian",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return;

        Bot.Skills.UseSkill(3);
        Bot.Sleep(750);
        Bot.Skills.UseSkill(2);
        Bot.Sleep(750);
        Bot.Skills.UseSkill(1);
        Bot.Sleep(750);
    }

    public string GetDivineElixir(
        ClassPreset preset,
        string roleName,
        string logPrefix
    )
    {
        const string divineElixir = "Divine Elixir";
        const string unstableDivineElixir = "Unstable Divine Elixir";

        if (!Bot.Inventory.Contains(unstableDivineElixir))
        {
            if (!Bot.Bank.Loaded)
            {
                if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
                    Bot.Bank.Open();

                Bot.Bank.Load(waitForLoad: false);
                Bot.Wait.ForBankLoad(20);
            }

            if (Bot.Bank.Contains(unstableDivineElixir))
            {
                if (MoveBankItemToInventory(unstableDivineElixir))
                    Core.Logger(
                        $"{unstableDivineElixir} moved from bank.",
                        "GetDivineElixir"
                    );
                else
                    Core.Logger(
                        $"{unstableDivineElixir} could not be moved from bank.",
                        "GetDivineElixir"
                    );
            }
        }

        if (Bot.Inventory.Contains(unstableDivineElixir))
        {
            Core.Logger(
                $"{unstableDivineElixir} already available.",
                "GetDivineElixir"
            );
            return unstableDivineElixir;
        }

        if (
            Bot.Inventory.Contains(divineElixir)
            || Bot.Bank.Contains(divineElixir)
        )
            return divineElixir;

        if (!Core.HasSpace)
        {
            Core.Logger(
                $"{divineElixir} skipped because no free inventory slot is available.",
                "GetDivineElixir"
            );
            return divineElixir;
        }

        bool dropGrabberWasEnabled = Bot.Drops.Enabled;

        try
        {
            if (!dropGrabberWasEnabled)
                Bot.Drops.Start();

            StartSkillEngine(
                preset.Skills,
                roleName,
                taunter: false,
                logPrefix,
                preset.SkillMode
            );

            Core.KillMonster(
                "poisonforest",
                "r15",
                "Left",
                41,
                divineElixir,
                10,
                isTemp: false
            );
        }
        finally
        {
            StopSkillEngine();

            if (!dropGrabberWasEnabled)
                Bot.Drops.Stop();
        }

        return divineElixir;
    }

    public void PreparePotions(
        string tonicName,
        string elixirName,
        string? potionName = null
    )
    {
        PreparePotion(tonicName, PotionCategory.Tonic);
        PreparePotion(elixirName, PotionCategory.Elixir);

        if (!string.IsNullOrWhiteSpace(potionName))
            PreparePotion(potionName, PotionCategory.CombatPotion);
    }

    public void UsePotions(
        string tonicName,
        string elixirName,
        string? potionName = null
    )
    {
        UsePotion(tonicName, PotionCategory.Tonic);
        UsePotion(elixirName, PotionCategory.Elixir);

        if (!string.IsNullOrWhiteSpace(potionName))
            UsePotion(potionName, PotionCategory.CombatPotion);
    }

    public bool MaintainPotion(string potionName)
    {
        if (
            !Bot.Player.Alive
            || !IsSupportedPotion(potionName, PotionCategory.CombatPotion)
            || !Bot.Inventory.IsEquipped(potionName)
            || !Bot.Skills.CanUseSkill(5)
        )
            return false;

        var aura = Bot.Self.GetAura(GetPotionAuraName(potionName));
        if (
            aura != null
            && aura.ExpiresAt - DateTimeOffset.Now > TimeSpan.FromSeconds(5)
        )
            return false;

        return Bot.Skills.UseSkill(5);
    }

    public void EquipScroll(string scrollName)
    {
        if (Bot.ShouldExit)
            return;

        if (string.IsNullOrWhiteSpace(scrollName))
        {
            Core.Logger(
                "Scroll equipping skipped because no scroll was specified.",
                "EquipScroll"
            );
            return;
        }

        bool required = string.Equals(
            scrollName,
            EnrageScroll,
            StringComparison.OrdinalIgnoreCase
        );

        try
        {
            if (!Bot.Inventory.Contains(scrollName))
            {
                Core.Logger(
                    $"{scrollName} is not in inventory.",
                    "EquipScroll",
                    messageBox: required,
                    stopBot: required
                );
                return;
            }

            if (Bot.Inventory.IsEquipped(scrollName))
            {
                Core.Logger($"{scrollName} already equipped.", "EquipScroll");
                return;
            }

            Bot.Inventory.EquipUsableItem(scrollName);
            Bot.Wait.ForItemEquip(scrollName);

            if (!Bot.Inventory.IsEquipped(scrollName))
            {
                Core.Logger(
                    $"{scrollName} could not be equipped.",
                    "EquipScroll",
                    messageBox: required,
                    stopBot: required
                );
                return;
            }

            Bot.Sleep(2000);
            Core.Logger($"{scrollName} equipped.", "EquipScroll");
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreDUCK scroll equip failed for {scrollName}: {ex}");
            Core.Logger(
                $"{scrollName} could not be equipped.",
                "EquipScroll",
                messageBox: required,
                stopBot: required
            );
        }
    }

    private void PreparePotion(string itemName, PotionCategory category)
    {
        if (Bot.ShouldExit)
            return;

        if (!IsSupportedPotion(itemName, category))
        {
            Core.Logger(
                $"{itemName} is not supported as a {GetPotionCategoryName(category)}.",
                "PreparePotions"
            );
            return;
        }

        try
        {
            if (Bot.Inventory.Contains(itemName))
            {
                Core.Logger($"{itemName} already available.", "PreparePotions");
                return;
            }

            if (Bot.Bank.Contains(itemName))
            {
                if (!MoveBankItemToInventory(itemName))
                    Core.Logger($"{itemName} could not be moved from bank.", "PreparePotions");
                else
                    Core.Logger($"{itemName} moved from bank.", "PreparePotions");
                return;
            }

            if (
                !TryGetPotionRecipe(
                    itemName,
                    out string voucherName,
                    out int voucherQuantity,
                    out int voucherCost,
                    out int potionQuantity,
                    out string? factionName,
                    out int factionRank
                )
            )
            {
                Core.Logger(
                    $"{itemName} skipped because no purchase recipe is configured.",
                    "PreparePotions"
                );
                return;
            }

            if (
                factionName != null
                && !Bot.Reputation.HasRank(factionName, factionRank)
            )
            {
                Core.Logger(
                    $"{itemName} skipped because {factionName} rank {factionRank} is required.",
                    "PreparePotions"
                );
                return;
            }

            int requiredSlots = Bot.Inventory.Contains(voucherName) ? 1 : 2;
            if (Bot.Inventory.FreeSlots < requiredSlots)
            {
                Core.Logger(
                    $"{itemName} skipped because {requiredSlots} free inventory slots are required.",
                    "PreparePotions"
                );
                return;
            }

            if (Bot.Bank.Contains(voucherName) && !MoveBankItemToInventory(voucherName))
            {
                Core.Logger(
                    $"{itemName} skipped because {voucherName} could not be moved from bank.",
                    "PreparePotions"
                );
                return;
            }

            int missingVouchers = Math.Max(
                0,
                voucherQuantity - Bot.Inventory.GetQuantity(voucherName)
            );
            int requiredGold = missingVouchers * voucherCost;

            if (Bot.Player.Gold < requiredGold)
            {
                Core.Logger(
                    $"{itemName} skipped because {requiredGold} gold is required.",
                    "PreparePotions"
                );
                return;
            }

            if (missingVouchers > 0)
            {
                if (!Core.HasSpace)
                {
                    Core.Logger(
                        $"{itemName} skipped because no free inventory slot is available.",
                        "PreparePotions"
                    );
                    return;
                }

                Core.BuyItem(
                    PotionShopMap,
                    PotionShopId,
                    voucherName,
                    voucherQuantity
                );

                if (Bot.Inventory.GetQuantity(voucherName) < voucherQuantity)
                {
                    Core.Logger(
                        $"{itemName} skipped because the required vouchers could not be obtained.",
                        "PreparePotions"
                    );
                    return;
                }
            }

            if (!Core.HasSpace)
            {
                Core.Logger(
                    $"{itemName} skipped because no free inventory slot is available.",
                    "PreparePotions"
                );
                return;
            }

            Core.BuyItem(
                PotionShopMap,
                PotionShopId,
                itemName,
                potionQuantity
            );

            if (!Bot.Inventory.Contains(itemName))
            {
                Core.Logger($"{itemName} could not be purchased.", "PreparePotions");
                return;
            }

            Core.Logger($"{itemName} purchased.", "PreparePotions");
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreDUCK potion preparation failed for {itemName}: {ex}");
            Core.Logger($"{itemName} preparation failed.", "PreparePotions");
        }
    }

    private void UsePotion(string itemName, PotionCategory category)
    {
        if (Bot.ShouldExit)
            return;

        if (!IsSupportedPotion(itemName, category))
        {
            Core.Logger(
                $"{itemName} is not supported as a {GetPotionCategoryName(category)}.",
                "UsePotions"
            );
            return;
        }

        try
        {
            if (!Bot.Inventory.Contains(itemName))
            {
                Core.Logger(
                    $"{itemName} skipped because it is not in inventory.",
                    "UsePotions"
                );
                return;
            }

            string auraName = GetPotionAuraName(itemName);
            bool auraActive = Bot.Self.HasActiveAura(auraName);

            if (auraActive && category != PotionCategory.CombatPotion)
            {
                Core.Logger($"{itemName} already active.", "UsePotions");
                return;
            }

            if (!EquipPotion(itemName))
                return;

            Bot.Sleep(PotionSuccessDelay);

            if (auraActive)
            {
                Core.Logger(
                    $"{itemName} already active and equipped.",
                    "UsePotions"
                );
                return;
            }

            Bot.Skills.UseSkill(5);
            Bot.Sleep(PotionAuraCheckDelay);

            if (!Bot.Self.HasActiveAura(auraName))
            {
                Core.Logger(
                    $"{itemName} was not verified after use.",
                    "UsePotions"
                );
                return;
            }

            Core.Logger($"{itemName} applied.", "UsePotions");
            Bot.Sleep(PotionSuccessDelay);
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreDUCK potion use failed for {itemName}: {ex}");
            Core.Logger($"{itemName} use failed.", "UsePotions");
        }
    }

    private bool EquipPotion(string itemName)
    {
        if (Bot.Inventory.IsEquipped(itemName))
            return true;

        Bot.Inventory.EquipUsableItem(itemName);
        Bot.Wait.ForItemEquip(itemName);

        if (Bot.Inventory.IsEquipped(itemName))
            return true;

        Core.Logger($"{itemName} could not be equipped.", "UsePotions");
        return false;
    }

    private bool MoveBankItemToInventory(string itemName)
    {
        if (!Bot.Bank.Contains(itemName))
            return false;

        if (!Bot.Inventory.Contains(itemName) && !Core.HasSpace)
            return false;

        int inventoryQuantity = Bot.Inventory.GetQuantity(itemName);
        Bot.Bank.EnsureToInventory(itemName);
        Bot.Wait.ForTrue(
            () => Bot.Inventory.GetQuantity(itemName) > inventoryQuantity,
            14
        );
        return Bot.Inventory.GetQuantity(itemName) > inventoryQuantity;
    }

    private static bool IsSupportedPotion(
        string itemName,
        PotionCategory category
    ) =>
        category switch
        {
            PotionCategory.Tonic => itemName is "Might Tonic" or "Sage Tonic" or "Fate Tonic",
            PotionCategory.Elixir =>
                itemName
                    is "Potent Battle Elixir"
                    or "Potent Malevolence Elixir"
                    or "Potent Destruction Elixir"
                    or "Divine Elixir"
                    or "Unstable Divine Elixir",
            PotionCategory.CombatPotion =>
                itemName is "Potent Honor Potion" or "Felicitous Philtre",
            _ => false,
        };

    private static string GetPotionCategoryName(PotionCategory category) =>
        category switch
        {
            PotionCategory.Tonic => "tonic",
            PotionCategory.Elixir => "elixir",
            PotionCategory.CombatPotion => "combat potion",
            _ => "potion",
        };

    private static string GetPotionAuraName(string itemName) =>
        itemName switch
        {
            "Sage Tonic" => "Sage",
            "Might Tonic" => "Might",
            "Fate Tonic" => "Fate",
            "Potent Honor Potion" => "Potent Honor Malice",
            "Felicitous Philtre" => "Felicitous Philtre",
            _ => itemName,
        };

    private static bool TryGetPotionRecipe(
        string itemName,
        out string voucherName,
        out int voucherQuantity,
        out int voucherCost,
        out int potionQuantity,
        out string? factionName,
        out int factionRank
    )
    {
        voucherName = "Gold Voucher 500k";
        voucherQuantity = 0;
        voucherCost = 500_000;
        potionQuantity = 0;
        factionName = null;
        factionRank = 0;

        switch (itemName)
        {
            case "Sage Tonic":
            case "Might Tonic":
                voucherQuantity = 2;
                potionQuantity = 10;
                factionName = "Alchemy";
                factionRank = 8;
                return true;

            case "Fate Tonic":
                voucherQuantity = 4;
                potionQuantity = 10;
                factionName = "Alchemy";
                factionRank = 8;
                return true;

            case "Potent Battle Elixir":
            case "Potent Malevolence Elixir":
                voucherQuantity = 4;
                potionQuantity = 8;
                return true;

            case "Potent Destruction Elixir":
                voucherQuantity = 2;
                potionQuantity = 8;
                return true;

            case "Potent Honor Potion":
                voucherQuantity = 1;
                potionQuantity = 5;
                factionName = "Good";
                factionRank = 10;
                return true;

            case "Felicitous Philtre":
                voucherName = "Gold Voucher 100k";
                voucherQuantity = 2;
                voucherCost = 100_000;
                potionQuantity = 25;
                return true;

            default:
                return false;
        }
    }

    public void PrepareScrolls(string scrollName)
    {
        if (Bot.ShouldExit)
            return;

        if (string.IsNullOrWhiteSpace(scrollName))
        {
            Core.Logger(
                "Scroll preparation skipped because no scroll was specified.",
                "PrepareScrolls"
            );
            return;
        }

        if (
            string.Equals(scrollName, DecayScroll, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scrollName, MystifyScroll, StringComparison.OrdinalIgnoreCase)
        )
        {
            PrepareOptionalScroll(
                string.Equals(scrollName, DecayScroll, StringComparison.OrdinalIgnoreCase)
                    ? DecayScroll
                    : MystifyScroll
            );
            return;
        }

        if (!string.Equals(scrollName, EnrageScroll, StringComparison.OrdinalIgnoreCase))
        {
            Core.Logger(
                $"{scrollName} preparation skipped because it is not supported.",
                "PrepareScrolls"
            );
            return;
        }

        scrollName = EnrageScroll;

        try
        {
            if (!Bot.Reputation.HasRank("SpellCrafting", 5))
            {
                ScrollPreparationFailed(
                    scrollName,
                    "SpellCrafting rank 5 is required"
                );
                return;
            }

            if (Bot.Inventory.GetQuantity(scrollName) >= EnrageThreshold)
            {
                Core.Logger($"{scrollName} already available.", "PrepareScrolls");
                return;
            }

            if (!Bot.Bank.Loaded)
            {
                if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
                    Bot.Bank.Open();

                Bot.Bank.Load(waitForLoad: false);
                Bot.Wait.ForBankLoad(20);
            }

            bool scrollWasBanked = Bot.Bank.Contains(scrollName);
            if (
                scrollWasBanked
                && !MoveBankedScrollItem(
                    scrollName,
                    scrollName,
                    EnrageThreshold
                )
            )
                return;

            if (Bot.Inventory.GetQuantity(scrollName) >= EnrageThreshold)
            {
                Core.Logger($"{scrollName} moved from bank.", "PrepareScrolls");
                return;
            }

            bool useGold = Bot.Player.Gold >= 1_000_000;
            bool prepared = useGold
                ? PrepareEnrageWithGold()
                : PrepareEnrageByFarming();

            if (!prepared || Bot.ShouldExit)
                return;

            Core.Logger(
                $"{scrollName} prepared {(useGold ? "with gold" : "by farming")}.",
                "PrepareScrolls"
            );
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreDUCK scroll preparation failed for {scrollName}: {ex}");
            ScrollPreparationFailed(scrollName, "an unexpected error occurred");
        }
    }

    private bool PrepareEnrageWithGold()
    {
        const string voucher = "Gold Voucher 500k";
        const string quill = "Arcane Quill";
        const string ink = "Zealous Ink";

        int requiredTurnIns =
            (
                EnrageMaxStack
                - Bot.Inventory.GetQuantity(EnrageScroll)
                + EnrageRewardQuantity
                - 1
            ) / EnrageRewardQuantity;

        Core.Join(SpellcraftMap);

        if (Bot.ShouldExit)
            return false;

        if (
            !MoveBankedScrollItem(EnrageScroll, voucher, 2)
            || !MoveBankedScrollItem(EnrageScroll, quill, 10)
            || !MoveBankedScrollItem(EnrageScroll, ink, requiredTurnIns)
        )
            return false;

        if (
            !PrepareGoldScrollMaterial(voucher, 2)
            || !PrepareGoldScrollMaterial(
                quill,
                10,
                ArcaneQuillShopItemId,
                index: 1
            )
            || !PrepareGoldScrollMaterial(ink, requiredTurnIns)
        )
            return false;

        if (!CompleteEnrageQuest(requiredTurnIns))
            return false;

        return !Bot.ShouldExit
            && Bot.Inventory.GetQuantity(EnrageScroll) >= EnrageMaxStack;
    }

    private bool PrepareEnrageByFarming()
    {
        const string parchment = "Mystic Parchment";
        const string ink = "Zealous Ink";

        Core.Join(SpellcraftMap);

        if (Bot.ShouldExit || !MoveBankedScrollItem(EnrageScroll, ink, 1))
            return false;

        if (Bot.Inventory.GetQuantity(ink) < 1)
        {
            if (!MoveBankedScrollItem(EnrageScroll, parchment, 2))
                return false;

            if (Bot.Inventory.GetQuantity(parchment) < 2)
            {
                if (!EnsureScrollOutputSpace(EnrageScroll, parchment))
                    return false;

                Core.AddDrop(parchment);
                Core.Join("underworld");
                Core.Jump("r2", "Up");
                Bot.Kill.ForItem("*", parchment, 2, false);

                if (Bot.ShouldExit)
                    return false;

                if (Bot.Inventory.GetQuantity(parchment) < 2)
                    return ScrollPreparationFailed(
                        EnrageScroll,
                        $"2 {parchment} could not be obtained"
                    );
            }

            Core.Join(SpellcraftMap);

            if (!EnsureScrollOutputSpace(EnrageScroll, ink))
                return false;

            Core.BuyItem(SpellcraftMap, SpellInkShopId, ink, 5);

            if (Bot.ShouldExit)
                return false;

            if (Bot.Inventory.GetQuantity(ink) < 5)
                return ScrollPreparationFailed(
                    EnrageScroll,
                    $"{ink} could not be crafted"
                );
        }

        return CompleteEnrageQuest(1);
    }

    private bool PrepareGoldScrollMaterial(
        string itemName,
        int quantity,
        int shopItemID = 0,
        int index = 0
    )
    {
        if (Bot.Inventory.GetQuantity(itemName) >= quantity)
            return true;

        if (!EnsureScrollOutputSpace(EnrageScroll, itemName))
            return false;

        Core.BuyItem(
            SpellcraftMap,
            ArcaneQuillShopId,
            itemName,
            quantity,
            shopItemID: shopItemID,
            index: index
        );

        if (Bot.ShouldExit)
            return false;

        if (Bot.Inventory.GetQuantity(itemName) < quantity)
            return ScrollPreparationFailed(
                EnrageScroll,
                $"{itemName} could not be obtained"
            );

        return true;
    }

    private bool CompleteEnrageQuest(int amount)
    {
        if (Bot.ShouldExit)
            return false;

        if (!EnsureScrollOutputSpace(EnrageScroll, EnrageScroll))
            return false;

        int previousQuantity = Bot.Inventory.GetQuantity(EnrageScroll);
        int expectedQuantity = Math.Min(
            EnrageMaxStack,
            previousQuantity + amount * EnrageRewardQuantity
        );

        if (previousQuantity == 0)
            Core.AddDrop(EnrageScroll);

        int completed = Core.EnsureCompleteMulti(EnrageQuestId, amount);

        if (Bot.ShouldExit)
            return false;

        if (completed < amount)
            return ScrollPreparationFailed(
                EnrageScroll,
                $"the quest completed {completed} of {amount} requested turn-ins"
            );

        if (previousQuantity == 0)
        {
            Bot.Wait.ForDrop(EnrageScroll, 10);
            Bot.Drops.Pickup(EnrageScroll);
            Bot.Wait.ForPickup(EnrageScroll, 10);
        }

        Bot.Wait.ForTrue(
            () => Bot.Inventory.GetQuantity(EnrageScroll) >= expectedQuantity,
            10
        );

        if (Bot.ShouldExit)
            return false;

        if (Bot.Inventory.GetQuantity(EnrageScroll) < expectedQuantity)
            return ScrollPreparationFailed(
                EnrageScroll,
                $"the quest produced fewer than {expectedQuantity} total scrolls"
            );

        return true;
    }

    private void PrepareOptionalScroll(string scrollName)
    {
        int questId = string.Equals(
            scrollName,
            DecayScroll,
            StringComparison.OrdinalIgnoreCase
        )
            ? DecayQuestId
            : MystifyQuestId;
        int requiredRank = questId == DecayQuestId ? 5 : 8;
        int startingQuantity = Bot.Inventory.GetQuantity(scrollName);

        try
        {
            if (startingQuantity >= OptionalScrollThreshold)
            {
                Core.Logger($"{scrollName} already available.", "PrepareScrolls");
                return;
            }

            bool scrollWasBanked = Bot.Bank.Contains(scrollName);
            if (
                scrollWasBanked
                && !MoveBankedScrollItem(
                    scrollName,
                    scrollName,
                    OptionalScrollThreshold
                )
            )
                return;

            if (Bot.Inventory.GetQuantity(scrollName) >= OptionalScrollThreshold)
            {
                Core.Logger($"{scrollName} moved from bank.", "PrepareScrolls");
                return;
            }

            if (!Bot.Reputation.HasRank("SpellCrafting", requiredRank))
            {
                ScrollPreparationFailed(
                    scrollName,
                    $"SpellCrafting rank {requiredRank} is required"
                );
                return;
            }

            bool useGold = Bot.Player.Gold >= 1_000_000;
            if (!useGold && questId == MystifyQuestId)
            {
                ScrollPreparationFailed(
                    scrollName,
                    "the gold route requires at least 1,000,000 gold"
                );
                return;
            }

            Core.Join(SpellcraftMap);
            if (
                Bot.ShouldExit
                || !string.Equals(
                    Bot.Map.Name,
                    SpellcraftMap,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ScrollPreparationFailed(scrollName, $"{SpellcraftMap} could not be joined");
                return;
            }

            Quest? quest = Core.InitializeWithRetries(() => Core.EnsureLoad(questId));
            ItemBase? requirement = quest?.Requirements.FirstOrDefault();
            ItemBase? reward = quest?.Rewards.FirstOrDefault();

            if (
                quest == null
                || requirement == null
                || reward == null
                || requirement.Quantity <= 0
                || reward.Quantity <= 0
                || reward.MaxStack <= 0
                || !string.Equals(
                    reward.Name,
                    scrollName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ScrollPreparationFailed(
                    scrollName,
                    $"quest {questId} did not provide valid recipe data"
                );
                return;
            }

            int targetQuantity = useGold
                ? reward.MaxStack
                : OptionalScrollThreshold;
            int turnIns = (
                targetQuantity
                - Bot.Inventory.GetQuantity(scrollName)
                + reward.Quantity
                - 1
            ) / reward.Quantity;

            bool prepared = useGold
                ? PrepareOptionalScrollWithGold(
                    scrollName,
                    quest,
                    requirement,
                    reward,
                    turnIns
                )
                : PrepareDecayByFarming(
                    scrollName,
                    quest,
                    requirement,
                    reward,
                    turnIns
                );

            if (!prepared || Bot.ShouldExit)
                return;

            Core.Logger(
                $"{scrollName} prepared {(useGold ? "with gold" : "by farming")} ({startingQuantity} -> {Bot.Inventory.GetQuantity(scrollName)}).",
                "PrepareScrolls"
            );
        }
        catch (Exception ex)
        {
            Bot.Log($"CoreDUCK scroll preparation failed for {scrollName}: {ex}");
            ScrollPreparationFailed(scrollName, "an unexpected error occurred");
        }
    }

    private bool PrepareOptionalScrollWithGold(
        string scrollName,
        Quest quest,
        ItemBase requirement,
        ItemBase reward,
        int turnIns
    )
    {
        const string voucher = "Gold Voucher 500k";
        const string quill = "Arcane Quill";
        int requiredInk = turnIns * requirement.Quantity;

        if (
            !MoveBankedScrollItem(scrollName, voucher, 2)
            || !MoveBankedScrollItem(scrollName, quill, 10)
            || !MoveBankedScrollItem(scrollName, requirement.Name, requiredInk)
        )
            return false;

        if (
            !PrepareOptionalGoldScrollMaterial(scrollName, voucher, 2)
            || !PrepareOptionalGoldScrollMaterial(
                scrollName,
                quill,
                10,
                ArcaneQuillShopItemId,
                index: 1
            )
            || !PrepareOptionalGoldScrollMaterial(
                scrollName,
                requirement.Name,
                requiredInk
            )
        )
            return false;

        return CompleteOptionalScrollQuest(
            scrollName,
            quest,
            reward,
            turnIns,
            reward.MaxStack
        );
    }

    private bool PrepareDecayByFarming(
        string scrollName,
        Quest quest,
        ItemBase requirement,
        ItemBase reward,
        int turnIns
    )
    {
        const string parchment = "Mystic Parchment";
        int requiredInk = turnIns * requirement.Quantity;

        if (!MoveBankedScrollItem(scrollName, requirement.Name, requiredInk))
            return false;

        if (Bot.Inventory.GetQuantity(requirement.Name) < requiredInk)
        {
            if (!MoveBankedScrollItem(scrollName, parchment, 2))
                return false;

            if (Bot.Inventory.GetQuantity(parchment) < 2)
            {
                if (!EnsureScrollOutputSpace(scrollName, parchment))
                    return false;

                Core.AddDrop(parchment);
                Core.Join("underworld");
                Core.Jump("r2", "Up");
                Bot.Kill.ForItem("*", parchment, 2, false);

                if (Bot.ShouldExit)
                    return false;

                if (Bot.Inventory.GetQuantity(parchment) < 2)
                    return ScrollPreparationFailed(
                        scrollName,
                        $"2 {parchment} could not be obtained"
                    );
            }

            Core.Join(SpellcraftMap);
            if (
                Bot.ShouldExit
                || !string.Equals(
                    Bot.Map.Name,
                    SpellcraftMap,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return ScrollPreparationFailed(
                    scrollName,
                    $"{SpellcraftMap} could not be joined"
                );

            if (!EnsureScrollOutputSpace(scrollName, requirement.Name))
                return false;

            Core.BuyItem(SpellcraftMap, SpellInkShopId, requirement.Name, 5);

            if (Bot.ShouldExit)
                return false;

            if (Bot.Inventory.GetQuantity(requirement.Name) < requiredInk)
                return ScrollPreparationFailed(
                    scrollName,
                    $"{requirement.Name} could not be crafted"
                );
        }

        return CompleteOptionalScrollQuest(
            scrollName,
            quest,
            reward,
            turnIns,
            OptionalScrollThreshold
        );
    }

    private bool PrepareOptionalGoldScrollMaterial(
        string scrollName,
        string itemName,
        int quantity,
        int shopItemID = 0,
        int index = 0
    )
    {
        if (Bot.Inventory.GetQuantity(itemName) >= quantity)
            return true;

        if (!EnsureScrollOutputSpace(scrollName, itemName))
            return false;

        Core.BuyItem(
            SpellcraftMap,
            ArcaneQuillShopId,
            itemName,
            quantity,
            shopItemID: shopItemID,
            index: index
        );

        if (Bot.ShouldExit)
            return false;

        if (Bot.Inventory.GetQuantity(itemName) < quantity)
            return ScrollPreparationFailed(
                scrollName,
                $"{itemName} could not be obtained"
            );

        return true;
    }

    private bool CompleteOptionalScrollQuest(
        string scrollName,
        Quest quest,
        ItemBase reward,
        int amount,
        int targetQuantity
    )
    {
        if (Bot.ShouldExit || amount <= 0)
            return false;

        Core.Join(SpellcraftMap);
        if (
            Bot.ShouldExit
            || !string.Equals(
                Bot.Map.Name,
                SpellcraftMap,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return ScrollPreparationFailed(
                scrollName,
                $"{SpellcraftMap} could not be joined"
            );

        if (!EnsureScrollOutputSpace(scrollName, scrollName))
            return false;

        int previousQuantity = Bot.Inventory.GetQuantity(scrollName);
        int expectedQuantity = Math.Min(
            reward.MaxStack,
            previousQuantity + amount * reward.Quantity
        );

        if (previousQuantity == 0)
            Core.AddDrop(scrollName);

        int completed = Core.EnsureCompleteMulti(quest.ID, amount);
        if (Bot.ShouldExit)
            return false;

        if (completed < amount)
            return ScrollPreparationFailed(
                scrollName,
                $"the quest completed {completed} of {amount} requested turn-ins"
            );

        if (previousQuantity == 0)
        {
            Bot.Wait.ForDrop(scrollName, 10);
            Bot.Drops.Pickup(scrollName);
            Bot.Wait.ForPickup(scrollName, 10);
        }

        Bot.Wait.ForTrue(
            () => Bot.Inventory.GetQuantity(scrollName) >= expectedQuantity,
            10
        );

        if (Bot.ShouldExit)
            return false;

        if (Bot.Inventory.GetQuantity(scrollName) < targetQuantity)
            return ScrollPreparationFailed(
                scrollName,
                $"the quest produced fewer than {targetQuantity} total scrolls"
            );

        return true;
    }

    private bool MoveBankedScrollItem(
        string scrollName,
        string itemName,
        int quantity
    )
    {
        if (Bot.Inventory.GetQuantity(itemName) >= quantity)
            return true;

        if (!Bot.Bank.Contains(itemName))
            return true;

        if (!Bot.Inventory.Contains(itemName) && !Core.HasSpace)
            return ScrollPreparationFailed(
                scrollName,
                $"no free inventory slot is available for {itemName}"
            );

        if (MoveBankItemToInventory(itemName))
            return true;

        if (Bot.ShouldExit)
            return false;

        return ScrollPreparationFailed(
            scrollName,
            $"{itemName} could not be moved from bank"
        );
    }

    private bool EnsureScrollOutputSpace(string scrollName, string itemName)
    {
        if (Bot.Inventory.Contains(itemName) || Core.HasSpace)
            return true;

        return ScrollPreparationFailed(
            scrollName,
            $"no free inventory slot is available for {itemName}"
        );
    }

    private bool ScrollPreparationFailed(string scrollName, string reason)
    {
        bool required = string.Equals(
            scrollName,
            EnrageScroll,
            StringComparison.OrdinalIgnoreCase
        );

        Core.Logger(
            $"{scrollName} preparation failed: {reason}.",
            "PrepareScrolls",
            messageBox: required,
            stopBot: required
        );
        return false;
    }

    public void PrepareEnhancements(
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial,
        bool warnForElysiumUnlock = false,
        WeaponSpecial[]? weaponFallbacks = null
    )
    {
        if (Bot.ShouldExit)
            return;

        int targetLevel = Bot.Player.Level > 0 ? Bot.Player.Level : 100;
        bool classPrepared = IsEquippedEnhancementPrepared(EnhancementSlot.Class, type, targetLevel: targetLevel);
        bool capePrepared = IsEquippedEnhancementPrepared(
            EnhancementSlot.Cape,
            type,
            capeSpecial: capeSpecial,
            targetLevel: targetLevel
        );
        bool helmPrepared = IsEquippedEnhancementPrepared(
            EnhancementSlot.Helm,
            type,
            helmSpecial: helmSpecial,
            targetLevel: targetLevel
        );
        bool weaponPrepared = IsEquippedEnhancementPrepared(
            EnhancementSlot.Weapon,
            type,
            weaponSpecial: weaponSpecial,
            targetLevel: targetLevel
        );

        if (classPrepared && capePrepared && helmPrepared && weaponPrepared)
        {
            Core.Logger(
                "All enhancements already prepared.",
                "PrepareEnhancements"
            );
            return;
        }

        if (classPrepared)
            LogEnhancementResult(
                EnhancementSlot.Class,
                GetRequestedEnhancementName(
                    EnhancementSlot.Class,
                    type,
                    CapeSpecial.None,
                    HelmSpecial.None,
                    WeaponSpecial.None
                ),
                "already prepared"
            );
        else
            PrepareEnhancementSlot(EnhancementSlot.Class, type);

        if (Bot.ShouldExit)
            return;

        if (capePrepared)
            LogEnhancementResult(
                EnhancementSlot.Cape,
                GetRequestedEnhancementName(
                    EnhancementSlot.Cape,
                    type,
                    capeSpecial,
                    HelmSpecial.None,
                    WeaponSpecial.None
                ),
                "already prepared"
            );
        else
            PrepareEnhancementSlot(
                EnhancementSlot.Cape,
                type,
                capeSpecial: capeSpecial
            );

        if (Bot.ShouldExit)
            return;

        if (helmPrepared)
            LogEnhancementResult(
                EnhancementSlot.Helm,
                GetRequestedEnhancementName(
                    EnhancementSlot.Helm,
                    type,
                    CapeSpecial.None,
                    helmSpecial,
                    WeaponSpecial.None
                ),
                "already prepared"
            );
        else
            PrepareEnhancementSlot(
                EnhancementSlot.Helm,
                type,
                helmSpecial: helmSpecial
            );

        if (Bot.ShouldExit)
            return;

        if (weaponPrepared)
            LogEnhancementResult(
                EnhancementSlot.Weapon,
                GetRequestedEnhancementName(
                    EnhancementSlot.Weapon,
                    type,
                    CapeSpecial.None,
                    HelmSpecial.None,
                    weaponSpecial
                ),
                "already prepared"
            );
        else
            PrepareEnhancementSlot(
                EnhancementSlot.Weapon,
                type,
                weaponSpecial: weaponSpecial,
                warnForElysiumUnlock: warnForElysiumUnlock,
                weaponFallbacks: weaponFallbacks
            );
    }

    private void PrepareEnhancementSlot(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial = CapeSpecial.None,
        HelmSpecial helmSpecial = HelmSpecial.None,
        WeaponSpecial weaponSpecial = WeaponSpecial.None,
        bool warnForElysiumUnlock = false,
        WeaponSpecial[]? weaponFallbacks = null
    )
    {
        if (Bot.ShouldExit)
            return;

        string requestedEnhancement = GetRequestedEnhancementName(
            slot,
            type,
            capeSpecial,
            helmSpecial,
            weaponSpecial
        );

        try
        {
            if (
                !TryGetEnhancementShop(
                    slot,
                    type,
                    capeSpecial,
                    helmSpecial,
                    weaponSpecial,
                    out int shopId,
                    out string enhancementName,
                    out bool forgeShop
                )
            )
            {
                Core.Logger(
                    $"{slot}: requested enhancement is invalid.",
                    "PrepareEnhancements"
                );
                return;
            }

            if (forgeShop)
            {
                Core.Join("forge");
                if (Bot.ShouldExit)
                    return;
            }

            string mapName = Bot.Map?.Name ?? "whitemap";
            ShopItem? enhancement = Core.GetShopItems(mapName, shopId)
                .Where(item =>
                    item.Category == ItemCategory.Enhancement
                    && item.Level <= Bot.Player.Level
                    && (!item.Upgrade || Bot.Player.IsMember)
                    && NormalizeEnhancementName(item.Name).Contains(enhancementName)
                )
                .OrderByDescending(item => item.Level)
                .ThenByDescending(item => item.Upgrade ? 1 : 0)
                .FirstOrDefault();

            if (enhancement == null)
            {
                if (
                    slot == EnhancementSlot.Weapon
                    && !IsEnhancementUnlocked(
                        slot,
                        capeSpecial,
                        helmSpecial,
                        weaponSpecial
                    )
                    && TryPrepareWeaponFallback(
                        type,
                        weaponSpecial,
                        weaponFallbacks,
                        warnForElysiumUnlock
                    )
                )
                    return;

                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    "skipped because no usable enhancement was found"
                );
                return;
            }

            InventoryItem? candidate = FindEnhancedCandidate(
                slot,
                type,
                capeSpecial,
                helmSpecial,
                weaponSpecial,
                enhancement.Level
            );

            if (candidate != null)
            {
                if (Bot.Inventory.IsEquipped(candidate.ID))
                {
                    LogEnhancementResult(
                        slot,
                        requestedEnhancement,
                        "already prepared"
                    );
                    return;
                }

                InventoryItem? equippedWeapon = slot == EnhancementSlot.Weapon
                    ? GetEquippedEnhancementItem(EnhancementSlot.Weapon)
                    : null;
                bool candidateIsWeaker = equippedWeapon != null
                    && Core.GetBoostFloat(candidate, "dmgAll")
                    < Core.GetBoostFloat(equippedWeapon, "dmgAll");

                if (candidateIsWeaker)
                {
                    LogEnhancementResult(
                        slot,
                        requestedEnhancement,
                        "will be applied to the stronger equipped weapon"
                    );
                }
                else
                {
                    Core.Equip(candidate.ID);
                    if (Bot.Inventory.IsEquipped(candidate.ID))
                    {
                        LogEnhancementResult(
                            slot,
                            requestedEnhancement,
                            "equipped from inventory"
                        );
                        return;
                    }

                    LogEnhancementResult(
                        slot,
                        requestedEnhancement,
                        $"could not be equipped from inventory. Using the current {GetEnhancementSlotName(slot)}"
                    );
                }
            }

            InventoryItem? equippedItem = GetEquippedEnhancementItem(slot);
            if (equippedItem == null)
            {
                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    $"skipped because no {GetEnhancementSlotName(slot)} is equipped"
                );
                return;
            }

            if (
                IsRequestedEnhancement(
                    equippedItem,
                    slot,
                    type,
                    capeSpecial,
                    helmSpecial,
                    weaponSpecial,
                    enhancement.Level
                )
            )
            {
                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    "already prepared"
                );
                return;
            }

            if (!IsEnhancementUnlocked(slot, capeSpecial, helmSpecial, weaponSpecial))
            {
                if (
                    slot == EnhancementSlot.Weapon
                    && TryPrepareWeaponFallback(
                        type,
                        weaponSpecial,
                        weaponFallbacks,
                        warnForElysiumUnlock
                    )
                )
                    return;

                if (
                    IsMandatoryEnhancementUnlock(
                        slot,
                        capeSpecial,
                        helmSpecial,
                        weaponSpecial
                    )
                )
                {
                    Core.Logger(
                        $"{slot}: {requestedEnhancement} is not unlocked and is required.",
                        "PrepareEnhancements",
                        messageBox: true,
                        stopBot: true
                    );
                    return;
                }

                if (
                    warnForElysiumUnlock
                    && slot == EnhancementSlot.Weapon
                    && weaponSpecial == WeaponSpecial.Elysium
                )
                {
                    Core.Logger(
                        "Weapon: Elysium is not unlocked on Shaman. Ultra Gramiel may fail.",
                        "PrepareEnhancements",
                        messageBox: true
                    );
                    return;
                }

                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    "skipped because it is not unlocked"
                );
                return;
            }

            int roomId = Bot.Map?.RoomID ?? 1;
            Bot.Send.Packet(
                $"%xt%zm%enhanceItemShop%{roomId}%{equippedItem.ID}%{enhancement.ID}%{shopId}%"
            );
            Core.Sleep();

            InventoryItem? updatedItem = Bot.Inventory.Items.FirstOrDefault(item =>
                item.ID == equippedItem.ID && item.Equipped
            );

            if (
                updatedItem == null
                || !IsRequestedEnhancement(
                    updatedItem,
                    slot,
                    type,
                    capeSpecial,
                    helmSpecial,
                    weaponSpecial,
                    enhancement.Level
                )
            )
            {
                LogEnhancementResult(
                    slot,
                    requestedEnhancement,
                    "was not verified after application"
                );
                return;
            }

            LogEnhancementResult(slot, requestedEnhancement, "applied");
        }
        catch (Exception ex)
        {
            Bot.Log(
                $"CoreDUCK enhancement preparation failed for {GetEnhancementSlotName(slot)}: {ex}"
            );
            LogEnhancementResult(slot, requestedEnhancement, "preparation failed");
        }
    }

    private bool TryPrepareWeaponFallback(
        EnhancementType type,
        WeaponSpecial requestedWeapon,
        WeaponSpecial[]? weaponFallbacks,
        bool warnForElysiumUnlock
    )
    {
        if (weaponFallbacks == null || weaponFallbacks.Length == 0)
            return false;

        HashSet<WeaponSpecial> checkedWeapons = new() { requestedWeapon };

        foreach (WeaponSpecial fallback in weaponFallbacks)
        {
            if (
                fallback == WeaponSpecial.None
                || !checkedWeapons.Add(fallback)
                || !IsEnhancementUnlocked(
                    EnhancementSlot.Weapon,
                    CapeSpecial.None,
                    HelmSpecial.None,
                    fallback
                )
                || !TryGetEnhancementShop(
                    EnhancementSlot.Weapon,
                    type,
                    CapeSpecial.None,
                    HelmSpecial.None,
                    fallback,
                    out int shopId,
                    out string enhancementName,
                    out bool forgeShop
                )
            )
                continue;

            if (forgeShop)
            {
                Core.Join("forge");
                if (Bot.ShouldExit)
                    return true;
            }

            string mapName = Bot.Map?.Name ?? "whitemap";
            bool usable = Core.GetShopItems(mapName, shopId).Any(item =>
                item.Category == ItemCategory.Enhancement
                && item.Level <= Bot.Player.Level
                && (!item.Upgrade || Bot.Player.IsMember)
                && NormalizeEnhancementName(item.Name).Contains(enhancementName)
            );

            if (!usable)
                continue;

            string requestedName = GetRequestedEnhancementName(
                EnhancementSlot.Weapon,
                type,
                CapeSpecial.None,
                HelmSpecial.None,
                requestedWeapon
            );
            string fallbackName = GetRequestedEnhancementName(
                EnhancementSlot.Weapon,
                type,
                CapeSpecial.None,
                HelmSpecial.None,
                fallback
            );
            bool isHealthVampFallback = fallback == WeaponSpecial.Health_Vamp;
            string fallbackMessage =
                $"Weapon: {requestedName} is not unlocked. Using {fallbackName}.";

            if (isHealthVampFallback)
            {
                string playerAlias =
                    armyPlayerIndex >= 0
                        ? $"Player {armyPlayerIndex + 1}"
                        : "Current Player";
                string currentClassName =
                    Bot.Player.CurrentClass?.Name ?? "Unknown Class";
                fallbackMessage =
                    $"Weapon: {requestedName} is not unlocked on {playerAlias} ({currentClassName}). Using Health Vamp as fallback. The script may fail";
            }

            Core.Logger(
                fallbackMessage,
                "PrepareEnhancements",
                messageBox: isHealthVampFallback
            );
            PrepareEnhancementSlot(
                EnhancementSlot.Weapon,
                type,
                weaponSpecial: fallback,
                warnForElysiumUnlock: warnForElysiumUnlock,
                weaponFallbacks: Array.Empty<WeaponSpecial>()
            );
            return true;
        }

        return false;
    }

    private static bool IsMandatoryEnhancementUnlock(
        EnhancementSlot slot,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial
    ) =>
        slot switch
        {
            EnhancementSlot.Cape =>
                capeSpecial == CapeSpecial.Absolution
                || capeSpecial == CapeSpecial.Lament
                || capeSpecial == CapeSpecial.Penitence
                || capeSpecial == CapeSpecial.Vainglory,
            EnhancementSlot.Helm =>
                helmSpecial == HelmSpecial.Vim
                || helmSpecial == HelmSpecial.Examen
                || helmSpecial == HelmSpecial.Forge
                || helmSpecial == HelmSpecial.Pneuma,
            EnhancementSlot.Weapon =>
                weaponSpecial == WeaponSpecial.Awe_Blast
                || weaponSpecial == WeaponSpecial.Health_Vamp,
            _ => false,
        };

    private bool TryGetEnhancementShop(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial,
        out int shopId,
        out string enhancementName,
        out bool forgeShop
    )
    {
        shopId = 0;
        enhancementName = string.Empty;
        forgeShop = false;

        if (!Enum.IsDefined(typeof(EnhancementType), type))
            return false;

        switch (slot)
        {
            case EnhancementSlot.Class:
                shopId = GetNormalEnhancementShop(type);
                enhancementName = "armor";
                return shopId > 0;

            case EnhancementSlot.Cape:
                if (!Enum.IsDefined(typeof(CapeSpecial), capeSpecial))
                    return false;
                shopId = capeSpecial == CapeSpecial.None
                    ? GetNormalEnhancementShop(type)
                    : 2143;
                enhancementName = capeSpecial == CapeSpecial.None
                    ? "cape"
                    : NormalizeEnhancementName(capeSpecial.ToString());
                forgeShop = capeSpecial != CapeSpecial.None;
                return shopId > 0;

            case EnhancementSlot.Helm:
                if (!Enum.IsDefined(typeof(HelmSpecial), helmSpecial))
                    return false;
                shopId = helmSpecial == HelmSpecial.None
                    ? GetNormalEnhancementShop(type)
                    : 2164;
                enhancementName = helmSpecial == HelmSpecial.None
                    ? "helm"
                    : NormalizeEnhancementName(helmSpecial.ToString());
                forgeShop = helmSpecial != HelmSpecial.None;
                return shopId > 0;

            case EnhancementSlot.Weapon:
                if (!Enum.IsDefined(typeof(WeaponSpecial), weaponSpecial))
                    return false;

                if (weaponSpecial == WeaponSpecial.None)
                {
                    shopId = GetNormalEnhancementShop(type);
                    enhancementName = "weapon";
                }
                else if ((int)weaponSpecial >= 2 && (int)weaponSpecial <= 6)
                {
                    shopId = GetAweEnhancementShop(type);
                    enhancementName = NormalizeEnhancementName(weaponSpecial.ToString());
                }
                else
                {
                    shopId = 2142;
                    enhancementName = NormalizeEnhancementName(weaponSpecial.ToString());
                    forgeShop = true;
                }
                return shopId > 0;

            default:
                return false;
        }
    }

    private int GetNormalEnhancementShop(EnhancementType type)
    {
        bool levelFifty = Bot.Player.Level >= 50;
        return type switch
        {
            EnhancementType.Fighter => levelFifty ? 768 : 141,
            EnhancementType.Thief => levelFifty ? 767 : 142,
            EnhancementType.Hybrid => levelFifty ? 766 : 143,
            EnhancementType.Wizard => levelFifty ? 765 : 144,
            EnhancementType.Healer => levelFifty ? 762 : 145,
            EnhancementType.SpellBreaker => levelFifty ? 764 : 146,
            EnhancementType.Lucky => levelFifty ? 763 : 147,
            _ => 0,
        };
    }

    private static int GetAweEnhancementShop(EnhancementType type) =>
        type switch
        {
            EnhancementType.Fighter => 635,
            EnhancementType.Thief => 637,
            EnhancementType.Hybrid => 633,
            EnhancementType.Wizard => 636,
            EnhancementType.SpellBreaker => 636,
            EnhancementType.Healer => 638,
            EnhancementType.Lucky => 639,
            _ => 0,
        };

    private InventoryItem? FindEnhancedCandidate(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial,
        int enhancementLevel
    )
    {
        if (slot == EnhancementSlot.Class)
            return null;

        IEnumerable<InventoryItem> candidates = Bot.Inventory.Items.Where(item =>
            (!item.Upgrade || Bot.Player.IsMember)
            && IsItemInEnhancementSlot(item, slot)
            && IsRequestedEnhancement(
                item,
                slot,
                type,
                capeSpecial,
                helmSpecial,
                weaponSpecial,
                enhancementLevel
            )
        );

        return slot == EnhancementSlot.Weapon
            ? candidates
                .OrderByDescending(item => Core.GetBoostFloat(item, "dmgAll"))
                .FirstOrDefault()
            : candidates.FirstOrDefault();
    }

    private InventoryItem? GetEquippedEnhancementItem(EnhancementSlot slot) =>
        Bot.Inventory.Items.FirstOrDefault(item =>
            item.Equipped && IsItemInEnhancementSlot(item, slot)
        );

    private bool IsEquippedEnhancementPrepared(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial = CapeSpecial.None,
        HelmSpecial helmSpecial = HelmSpecial.None,
        WeaponSpecial weaponSpecial = WeaponSpecial.None,
        int targetLevel = 100
    )
    {
        InventoryItem? item = GetEquippedEnhancementItem(slot);
        return item != null
            && IsRequestedEnhancement(
                item,
                slot,
                type,
                capeSpecial,
                helmSpecial,
                weaponSpecial,
                targetLevel
            );
    }

    private static bool IsItemInEnhancementSlot(
        InventoryItem item,
        EnhancementSlot slot
    ) =>
        slot switch
        {
            EnhancementSlot.Class => item.Category == ItemCategory.Class,
            EnhancementSlot.Cape => item.Category == ItemCategory.Cape,
            EnhancementSlot.Helm => item.Category == ItemCategory.Helm,
            EnhancementSlot.Weapon => string.Equals(
                item.ItemGroup,
                "Weapon",
                StringComparison.OrdinalIgnoreCase
            ),
            _ => false,
        };

    private static bool IsRequestedEnhancement(
        InventoryItem item,
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial,
        int enhancementLevel
    )
    {
        if (enhancementLevel > 0 && item.EnhancementLevel > 0 && item.EnhancementLevel < Math.Min(enhancementLevel, 100))
            return false;

        return slot switch
        {
            EnhancementSlot.Class => item.EnhancementPatternID == (int)type,
            EnhancementSlot.Cape =>
                item.EnhancementPatternID
                == (capeSpecial == CapeSpecial.None
                    ? (int)type
                    : (int)capeSpecial),
            EnhancementSlot.Helm =>
                item.EnhancementPatternID
                == (helmSpecial == HelmSpecial.None
                    ? (int)type
                    : (int)helmSpecial),
            EnhancementSlot.Weapon when weaponSpecial == WeaponSpecial.None =>
                item.EnhancementPatternID == (int)type,
            EnhancementSlot.Weapon when weaponSpecial == WeaponSpecial.Forge =>
                item.ProcID == 0
                && (item.EnhancementPatternID == (int)type
                    || item.EnhancementPatternID == 10),
            EnhancementSlot.Weapon when (int)weaponSpecial <= 6 =>
                item.EnhancementPatternID == (int)type
                && item.ProcID == (int)weaponSpecial,
            EnhancementSlot.Weapon => item.ProcID == (int)weaponSpecial,
            _ => false,
        };
    }

    private bool IsEnhancementUnlocked(
        EnhancementSlot slot,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial
    )
    {
        int questId = slot switch
        {
            EnhancementSlot.Cape => capeSpecial switch
            {
                CapeSpecial.None => 0,
                CapeSpecial.Forge => 8758,
                CapeSpecial.Absolution => 8743,
                CapeSpecial.Avarice => 8745,
                CapeSpecial.Vainglory => 8744,
                CapeSpecial.Penitence => 8822,
                CapeSpecial.Lament => 8823,
                _ => -1,
            },
            EnhancementSlot.Helm => helmSpecial switch
            {
                HelmSpecial.None => 0,
                HelmSpecial.Forge => 8828,
                HelmSpecial.Vim => 8824,
                HelmSpecial.Examen => 8825,
                HelmSpecial.Anima => 8826,
                HelmSpecial.Pneuma => 8827,
                HelmSpecial.Hearty => 9466,
                _ => -1,
            },
            EnhancementSlot.Weapon when weaponSpecial == WeaponSpecial.None => 0,
            EnhancementSlot.Weapon when (int)weaponSpecial >= 2 && (int)weaponSpecial <= 6 => 2937,
            EnhancementSlot.Weapon => weaponSpecial switch
            {
                WeaponSpecial.Forge => 8738,
                WeaponSpecial.Lacerate => 8739,
                WeaponSpecial.Smite => 8740,
                WeaponSpecial.Valiance => 8741,
                WeaponSpecial.Arcanas_Concerto => 8742,
                WeaponSpecial.Acheron => 8820,
                WeaponSpecial.Elysium => 8821,
                WeaponSpecial.Praxis => 9171,
                WeaponSpecial.Dauntless => 9172,
                WeaponSpecial.Ravenous => 9560,
                _ => -1,
            },
            _ => 0,
        };

        if (questId < 0)
            return false;
        if (questId == 0)
            return true;
        if (!Core.isCompletedBefore(questId))
            return false;

        return slot != EnhancementSlot.Helm
            || helmSpecial != HelmSpecial.Hearty
            || Bot.Reputation.HasRank("Grimskull Trolling", 7);
    }

    private static string NormalizeEnhancementName(string name) =>
        name.Replace(" ", string.Empty)
            .Replace("'", string.Empty)
            .Replace("_", string.Empty)
            .ToLowerInvariant();

    private static string GetEnhancementSlotName(EnhancementSlot slot) =>
        slot.ToString().ToLowerInvariant();

    private static string GetRequestedEnhancementName(
        EnhancementSlot slot,
        EnhancementType type,
        CapeSpecial capeSpecial,
        HelmSpecial helmSpecial,
        WeaponSpecial weaponSpecial
    )
    {
        string name = slot switch
        {
            EnhancementSlot.Class => type.ToString(),
            EnhancementSlot.Cape when capeSpecial == CapeSpecial.None =>
                type.ToString(),
            EnhancementSlot.Cape => capeSpecial.ToString(),
            EnhancementSlot.Helm when helmSpecial == HelmSpecial.None =>
                type.ToString(),
            EnhancementSlot.Helm => helmSpecial.ToString(),
            EnhancementSlot.Weapon when weaponSpecial == WeaponSpecial.None =>
                type.ToString(),
            EnhancementSlot.Weapon
                when (int)weaponSpecial >= 2 && (int)weaponSpecial <= 6 =>
                $"{type} {weaponSpecial}",
            EnhancementSlot.Weapon => weaponSpecial.ToString(),
            _ => type.ToString(),
        };

        return name.Replace("_", " ");
    }

    private void LogEnhancementResult(
        EnhancementSlot slot,
        string requestedEnhancement,
        string result
    ) =>
        Core.Logger(
            $"{slot}: {requestedEnhancement} {result}.",
            "PrepareEnhancements"
        );

    public bool ValidateFunctionBasedSkillsDisabled()
    {
        IPluginContainer? plugin = Ioc.Default
            .GetRequiredService<IPluginManager>()
            .GetContainer("Function Based Skills");

        if (plugin == null)
            return true;

        bool enabled;
        try
        {
            enabled = plugin.OptionContainer.Get<bool>("Enabled");
        }
        catch
        {
            enabled = true;
        }

        if (!enabled)
            return true;

        Core.Logger(
            "Function Based Skills is detected. Disable it before running this script.\nAll LW scripts use their own custom skill engine, and this plugin will interfere with it.",
            "ValidateFunctionBasedSkillsDisabled",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    public bool ValidatePrivateRoomNumber(int roomNumber)
    {
        if (roomNumber >= 1001 && roomNumber <= 99999)
            return true;

        Core.Logger(
            $"Private room number {roomNumber} is invalid. Use 1001 through 99999.",
            "ValidatePrivateRoomNumber",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    public void JoinRoom(
    string map,
    int roomNumber,
    string cell = "Enter",
    string pad = "Spawn"
)
    {
        Core.PrivateRooms = true;
        Core.PrivateRoomNumber = roomNumber;
        string target = $"{map}-{roomNumber}";

        for (int i = 0; i < 10 && !Bot.ShouldExit; i++)
        {
            if (string.Equals(Bot.Map.FullName, target, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(cell) && !string.Equals(Bot.Player.Cell, cell, StringComparison.OrdinalIgnoreCase))
                    Core.Jump(cell, pad);
                return;
            }

            Bot.Wait.ForActionCooldown(GameActions.Transfer);
            Bot.Map.Join(target, cell, pad, autoCorrect: false);
            Bot.Wait.ForMapLoad(map);
            Bot.Sleep(1000);
        }
    }

    public bool AcceptUltraQuest(int questID)
    {
        if (questID <= 0)
        {
            Core.Logger(
                $"Warning: Ultra quest ID {questID} is invalid. Continuing.",
                "AcceptUltraQuest"
            );
            return false;
        }

        if (Bot.Quests.IsInProgress(questID))
        {
            Core.Logger(
                $"Ultra quest {questID} is already accepted.",
                "AcceptUltraQuest"
            );
            return true;
        }

        if (Bot.Quests.IsDailyComplete(questID))
        {
            Core.Logger(
                $"Ultra quest {questID} is already completed for the current reset.",
                "AcceptUltraQuest"
            );
            return false;
        }

        if (!Bot.Quests.IsAvailable(questID))
        {
            Core.Logger(
                $"Warning: Ultra quest {questID} is unavailable. Continuing.",
                "AcceptUltraQuest"
            );
            return false;
        }

        Core.EnsureAccept(questID);

        if (Bot.Quests.IsInProgress(questID))
        {
            Core.Logger($"Ultra quest {questID} accepted.", "AcceptUltraQuest");
            return true;
        }

        Core.Logger(
            $"Warning: Ultra quest {questID} could not be accepted. Continuing.",
            "AcceptUltraQuest"
        );
        return false;
    }

    public bool CompleteUltraQuest(int questID)
    {
        if (questID <= 0)
        {
            Core.Logger(
                $"Warning: Ultra quest ID {questID} is invalid. Continuing.",
                "CompleteUltraQuest"
            );
            return false;
        }

        if (questID == bypassedUltraQuestID)
        {
            Core.Logger(
                $"Ultra quest {questID} completion skipped because this account used the questline bypass.",
                "CompleteUltraQuest"
            );
            return false;
        }

        if (Bot.Quests.IsDailyComplete(questID))
        {
            Core.Logger(
                $"Ultra quest {questID} is already completed for the current reset.",
                "CompleteUltraQuest"
            );
            return true;
        }

        if (!Bot.Quests.IsInProgress(questID))
        {
            Core.Logger(
                $"Warning: Ultra quest {questID} is not accepted. Completion skipped.",
                "CompleteUltraQuest"
            );
            return false;
        }

        Core.EnsureComplete(questID);

        if (
            Bot.Quests.IsDailyComplete(questID)
            && !Bot.Quests.IsInProgress(questID)
        )
        {
            Core.Logger($"Ultra quest {questID} completed.", "CompleteUltraQuest");
            return true;
        }

        Core.Logger(
            $"Warning: Ultra quest {questID} could not be completed. Continuing.",
            "CompleteUltraQuest"
        );
        return false;
    }

    public bool ValidateUltraAccess(
        int ultraQuestID,
        int prerequisiteQuestID,
        string prerequisiteQuestName,
        int minimumLevel,
        string ultraName,
        string className
    )
    {
        bypassedUltraQuestID = 0;

        int playerLevel = Bot.Player.Level;
        if (playerLevel <= 0)
        {
            string? rawLevel = Bot.Flash.GetGameObject("world.myAvatar.objData.intLevel")
                            ?? Bot.Flash.GetGameObject("world.myAvatar.data.intLevel");
            if (int.TryParse(rawLevel, out int parsed) && parsed > 0)
                playerLevel = parsed;
            else
                playerLevel = 100;
        }

        if (
            playerLevel < minimumLevel
            && !SendArmySignal("ULTRA_LEVEL_INVALID")
        )
            return false;

        bool hasRequiredWeapon = Bot.Inventory.Items.Any(item =>
            item.Equipped
            && !Core.NoneEnhancableFilter(item)
            && Core.GetBoostFloat(item, "dmgAll") >= 1.30f
        ) || Bot.Inventory.Items.Any(item => item.Equipped && Core.GetBoostFloat(item, "dmgAll") >= 1.30f);

        if (
            !hasRequiredWeapon
            && !SendArmySignal("ULTRA_WEAPON_INVALID")
        )
            return false;

        if (!SyncArmy("ULTRA_ACCESS_CHECK"))
            return false;

        List<string> playersBelowLevel = new();
        List<string> playersMissingWeapon = new();
        for (int playerNumber = 1; playerNumber <= armyPlayers.Length; playerNumber++)
        {
            if (HasArmySignal("ULTRA_LEVEL_INVALID", playerNumber))
                playersBelowLevel.Add($"Player {playerNumber}");

            if (HasArmySignal("ULTRA_WEAPON_INVALID", playerNumber))
                playersMissingWeapon.Add($"Player {playerNumber}");
        }

        if (playersBelowLevel.Count > 0)
        {
            Core.Logger(
                $"{ultraName} requires every player to be at least level {minimumLevel}. Players below the requirement: {string.Join(", ", playersBelowLevel)}.",
                "ValidateUltraAccess",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        if (playersMissingWeapon.Count > 0)
        {
            Core.Logger(
                $"{ultraName} requires every player to equip a 30% damage boost weapon or greater before starting. Players with an invalid equipped weapon: {string.Join(", ", playersMissingWeapon)}. Equip a qualifying weapon and restart the script.",
                "ValidateUltraAccess",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        if (
            prerequisiteQuestID > 0
            && (
                !Bot.Quests.IsUnlocked(ultraQuestID)
                || !Bot.Quests.HasBeenCompleted(prerequisiteQuestID)
            )
        )
        {
            Core.Logger(
                $"{ultraName} requires quest \"{prerequisiteQuestName}\" to be completed for Player {armyPlayerIndex + 1} ({className}). The quest will be bypassed so this account can continue.",
                "ValidateUltraAccess",
                messageBox: true
            );
            Bot.Quests.UpdateQuest(prerequisiteQuestID);
            bypassedUltraQuestID = ultraQuestID;
        }

        return !Bot.ShouldExit;
    }

    public bool StartArmySync(
        string syncFileName,
        int playerCount,
        string? optionCategory = null
    )
    {
        if (!ValidateFunctionBasedSkillsDisabled())
            return false;

        ResetArmyState();

        if (
            string.IsNullOrWhiteSpace(syncFileName)
            || !string.Equals(
                Path.GetFileName(syncFileName),
                syncFileName,
                StringComparison.Ordinal
            )
            || !string.Equals(
                Path.GetExtension(syncFileName),
                ".sync",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return ArmyInitializationFailure("Sync file must be a filename ending in .sync.");

        if (playerCount < 2 || playerCount > 7)
            return ArmyInitializationFailure("Army player count must be from 2 through 7.");

        if (Bot.Config == null)
            return ArmyInitializationFailure("Army player configuration is unavailable.");

        string[] players = new string[playerCount];
        for (int index = 0; index < playerCount; index++)
        {
            string optionName = $"player{index + 1}";
            players[index] = NormalizeArmyUsername(
                string.IsNullOrEmpty(optionCategory)
                    ? Bot.Config.Get<string>(optionName)
                    : Bot.Config.Get<string>(optionCategory, optionName)
            );
        }

        if (players.Any(string.IsNullOrEmpty))
            return ArmyInitializationFailure("Every requested Army player name is required.");

        if (players.Distinct(StringComparer.OrdinalIgnoreCase).Count() != players.Length)
            return ArmyInitializationFailure("Army player names must be unique.");

        string username = NormalizeArmyUsername(Core.Username());
        int playerIndex = Array.FindIndex(
            players,
            player => string.Equals(player, username, StringComparison.OrdinalIgnoreCase)
        );
        if (playerIndex < 0)
            return ArmyInitializationFailure("Current account is not in the Army roster.");

        armyPlayers = players;
        armyUsername = username;
        armyPlayerIndex = playerIndex;
        armyLaunchToken = Guid.NewGuid().ToString("N");
        armySessionId = armyPlayerIndex == 0 ? Guid.NewGuid().ToString("N") : string.Empty;
        armySyncPath = Path.Combine(ClientFileSources.SkuaOptionsDIR, syncFileName);
        armyInitialized = true;

        bool started = armyPlayerIndex == 0
            ? StartLeaderArmySession()
            : JoinFollowerArmySession();
        armySessionStarted = started;
        return started;
    }

    public bool SyncArmy(string step)
    {
        if (!RequireArmySession())
            return false;

        if (!CanContinueArmySync(ReadArmyLines()))
            return false;

        string stepName = NormalizeArmyRecordName(step);
        if (string.IsNullOrEmpty(stepName))
        {
            Core.Logger("Army sync step name is invalid.", "CoreDUCK");
            return false;
        }

        return armyPlayerIndex == 0
            ? RunLeaderArmySync(stepName)
            : RunFollowerArmySync(stepName);
    }

    public bool SendArmySignal(string signal)
    {
        if (!RequireArmySession())
            return false;

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return false;

        string signalName = NormalizeArmyRecordName(signal);
        if (string.IsNullOrEmpty(signalName))
        {
            Core.Logger("Army signal name is invalid.", "CoreDUCK");
            return false;
        }

        return AppendArmyRecord(
            new[]
            {
                "SIGNAL",
                ArmyProtocolVersion,
                armySessionId,
                signalName,
                armyUsername,
            }
        );
    }

    public bool HasArmySignal(string signal, int senderPlayerNumber)
    {
        if (!RequireArmySession())
            return false;

        string signalName = NormalizeArmyRecordName(signal);
        if (string.IsNullOrEmpty(signalName))
        {
            Core.Logger("Army signal name is invalid.", "CoreDUCK");
            return false;
        }

        if (senderPlayerNumber < 1 || senderPlayerNumber > armyPlayers.Length)
        {
            Core.Logger("Army signal sender player number is invalid.", "CoreDUCK");
            return false;
        }

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return false;

        string expectedSender = armyPlayers[senderPlayerNumber - 1];
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length == 5
                && parts[0] == "SIGNAL"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                && IsArmyRecordName(parts[3])
                && string.Equals(parts[3], signalName, StringComparison.Ordinal)
                && string.Equals(
                    NormalizeArmyUsername(parts[4]),
                    expectedSender,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;
        }

        return false;
    }

    public bool ShouldResetFight(int fightAttempt, int deadPlayerThreshold = 2)
    {
        if (!RequireArmySession())
            return false;

        if (fightAttempt <= 0)
        {
            Core.Logger("Fight attempt must be greater than zero.", "CoreDUCK");
            return false;
        }

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return false;

        string deadSignal = $"FIGHT_DEAD_{fightAttempt}";
        string aliveSignal = $"FIGHT_ALIVE_{fightAttempt}";
        string resetSignal = $"FIGHT_RESET_{fightAttempt}";
        bool playerAlive = Bot.Player.Alive;

        if (reportedFightAttempt != fightAttempt)
        {
            reportedFightAttempt = fightAttempt;
            reportedFightAlive = true;
        }

        if (reportedFightAlive != playerAlive)
        {
            string stateSignal = playerAlive ? aliveSignal : deadSignal;
            if (
                !AppendArmyRecord(
                    new[]
                    {
                        "SIGNAL",
                        ArmyProtocolVersion,
                        armySessionId,
                        stateSignal,
                        armyUsername,
                    }
                )
            )
                return false;

            reportedFightAlive = playerAlive;
        }

        string playerOne = armyPlayers[0];
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length == 5
                && parts[0] == "SIGNAL"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                && string.Equals(parts[3], resetSignal, StringComparison.Ordinal)
                && string.Equals(
                    NormalizeArmyUsername(parts[4]),
                    playerOne,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;
        }

        Dictionary<string, bool> latestAlive = new(StringComparer.OrdinalIgnoreCase);
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string[] parts = lines[index].Split('|');
            if (
                parts.Length != 5
                || parts[0] != "SIGNAL"
                || parts[1] != ArmyProtocolVersion
                || !string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                || (
                    !string.Equals(parts[3], deadSignal, StringComparison.Ordinal)
                    && !string.Equals(parts[3], aliveSignal, StringComparison.Ordinal)
                )
            )
                continue;

            string sender = NormalizeArmyUsername(parts[4]);
            if (
                !armyPlayers.Contains(sender, StringComparer.OrdinalIgnoreCase)
                || latestAlive.ContainsKey(sender)
            )
                continue;

            latestAlive[sender] = string.Equals(
                parts[3],
                aliveSignal,
                StringComparison.Ordinal
            );
        }

        latestAlive[armyUsername] = playerAlive;
        if (
            latestAlive.Count(state => !state.Value) < deadPlayerThreshold
            || armyPlayerIndex != 0
        )
            return false;

        return AppendArmyRecord(
            new[]
            {
                "SIGNAL",
                ArmyProtocolVersion,
                armySessionId,
                resetSignal,
                armyUsername,
            }
        );
    }

    public bool HasItemAnywhere(string itemName, int id = 0, int quantity = 1)
    {
        if (string.IsNullOrEmpty(itemName))
            return false;

        if (Bot.Inventory.Contains(itemName, quantity) || Bot.Bank.Contains(itemName, quantity))
            return true;

        string curlyVariant = itemName.Replace("'", "’");
        if (curlyVariant != itemName)
        {
            if (Bot.Inventory.Contains(curlyVariant, quantity) || Bot.Bank.Contains(curlyVariant, quantity))
                return true;
        }

        if (id > 0)
        {
            if (Bot.Inventory.Contains(id, quantity) || Bot.Bank.Contains(id, quantity))
                return true;
        }

        return false;
    }

    public bool EnsureAlive(int timeoutSeconds = 15)
    {
        if (Bot.Player.Alive)
            return true;

        DateTime timeout = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!Bot.ShouldExit && !Bot.Player.Alive && DateTime.UtcNow < timeout)
        {
            Bot.Sleep(500);
        }

        if (Bot.Player.Alive)
        {
            Bot.Sleep(500);
            return true;
        }

        return false;
    }

    public bool WaitForDropOrCredit(
        Func<bool> creditCondition,
        int timeoutMs = 8000,
        int pollIntervalMs = 250
    )
    {
        DateTime start = DateTime.Now;
        while (!Bot.ShouldExit)
        {
            try
            {
                if (creditCondition())
                    return true;
            }
            catch
            {
                // Ignore transient inventory querying exceptions
            }

            if ((DateTime.Now - start).TotalMilliseconds >= timeoutMs)
                break;

            Bot.Sleep(pollIntervalMs);
        }

        try
        {
            return creditCondition();
        }
        catch
        {
            return false;
        }
    }

    public bool VerifyArmyKillCredit(
        int fightAttempt,
        Func<bool> creditCondition,
        int armySize,
        string logPrefix = "CoreDUCK",
        int timeoutMs = 8000
    )
    {
        EnsureAlive(15);
        bool myKillCredit = WaitForDropOrCredit(creditCondition, timeoutMs);

        if (myKillCredit)
        {
            SendArmySignal($"KILL_CREDIT_OK_{fightAttempt}");
            Core.Logger($"{logPrefix} Local kill credit verified for attempt {fightAttempt}.", logPrefix);
        }
        else
        {
            SendArmySignal($"KILL_CREDIT_FAIL_{fightAttempt}");
            Core.Logger($"{logPrefix} Local kill credit timed out/missing for attempt {fightAttempt}.", logPrefix);
        }

        if (!SyncArmy($"CHECK_KILL_CREDIT_{fightAttempt}"))
            return false;

        bool allVerified = true;
        for (int playerNumber = 1; playerNumber <= armySize; playerNumber++)
        {
            if (!HasArmySignal($"KILL_CREDIT_OK_{fightAttempt}", playerNumber))
            {
                allVerified = false;
                Core.Logger($"{logPrefix} Player {playerNumber} is missing kill credit on attempt {fightAttempt}.", logPrefix);
            }
        }

        if (allVerified)
        {
            Core.Logger($"{logPrefix} All {armySize} players verified kill credit for attempt {fightAttempt}.", logPrefix);
            return true;
        }

        return false;
    }

    public bool VerifyArmyKillCredit(
        int fightAttempt,
        bool myKillCredit,
        int armySize,
        string logPrefix = "CoreDUCK"
    )
    {
        return VerifyArmyKillCredit(
            fightAttempt,
            () => myKillCredit,
            armySize,
            logPrefix,
            timeoutMs: 0
        );
    }

    public bool SendArmyTimestamp(string signal, long unixMilliseconds)
    {
        if (!RequireArmySession())
            return false;

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return false;

        string signalName = NormalizeArmyRecordName(signal);
        if (string.IsNullOrEmpty(signalName))
        {
            Core.Logger("Army timestamp name is invalid.", "CoreDUCK");
            return false;
        }

        if (unixMilliseconds <= 0)
        {
            Core.Logger("Army timestamp value is invalid.", "CoreDUCK");
            return false;
        }

        return AppendArmyRecord(
            new[]
            {
                "TIMESTAMP",
                ArmyProtocolVersion,
                armySessionId,
                signalName,
                armyUsername,
                unixMilliseconds.ToString(),
            }
        );
    }

    public long GetArmyTimestamp(string signal, int senderPlayerNumber)
    {
        if (!RequireArmySession())
            return 0;

        string signalName = NormalizeArmyRecordName(signal);
        if (string.IsNullOrEmpty(signalName))
        {
            Core.Logger("Army timestamp name is invalid.", "CoreDUCK");
            return 0;
        }

        if (senderPlayerNumber < 1 || senderPlayerNumber > armyPlayers.Length)
        {
            Core.Logger("Army timestamp sender player number is invalid.", "CoreDUCK");
            return 0;
        }

        string[] lines = ReadArmyLines();
        if (!CanContinueArmySync(lines))
            return 0;

        string expectedSender = armyPlayers[senderPlayerNumber - 1];
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string[] parts = lines[index].Split('|');
            if (
                parts.Length == 6
                && parts[0] == "TIMESTAMP"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                && IsArmyRecordName(parts[3])
                && string.Equals(parts[3], signalName, StringComparison.Ordinal)
                && string.Equals(
                    NormalizeArmyUsername(parts[4]),
                    expectedSender,
                    StringComparison.OrdinalIgnoreCase
                )
                && long.TryParse(parts[5], out long unixMilliseconds)
                && unixMilliseconds > 0
            )
                return unixMilliseconds;
        }

        return 0;
    }

    public string GetArmyPlayerName(int playerNumber) =>
        playerNumber >= 1 && playerNumber <= armyPlayers.Length
            ? armyPlayers[playerNumber - 1]
            : string.Empty;

    public bool IsArmyPlayer(int playerNumber) =>
        armyInitialized
        && playerNumber >= 1
        && playerNumber <= armyPlayers.Length
        && armyPlayerIndex == playerNumber - 1;

    public bool StopArmySync(string reason = "")
    {
        if (!RequireArmySession())
            return false;

        if (!CanContinueArmySync(ReadArmyLines()))
            return false;

        if (armyPlayerIndex != 0)
        {
            Core.Logger("Only playerOne may stop Army sync.", "CoreDUCK");
            return false;
        }

        if (!IsSafeArmyField(reason))
        {
            Core.Logger("Army stop reason contains invalid data.", "CoreDUCK");
            return false;
        }

        return AppendArmyRecord(
            new[] { "STOP", ArmyProtocolVersion, armySessionId, reason }
        );
    }

    private bool StartLeaderArmySession()
    {
        if (!ClearArmyFile())
            return false;

        if (
            !AppendArmyRecord(new[] { "RESET", ArmyProtocolVersion, armySessionId })
            || !WriteArmyReady(armySessionId)
        )
            return false;

        Dictionary<string, string>? launchTokens = WaitForArmyReady(armySessionId);
        if (launchTokens == null)
            return false;

        List<string> start = new() { "START", ArmyProtocolVersion, armySessionId };
        start.AddRange(armyPlayers.Select(player => launchTokens[player]));
        return AppendArmyRecord(start);
    }

    private bool JoinFollowerArmySession()
    {
        Core.Logger("Waiting for playerOne to start Army sync.", "CoreDUCK");
        string readySession = string.Empty;

        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            string[] lines = ReadArmyLines();
            string? sessionId = FindLatestArmyReset(lines);
            if (sessionId == null)
            {
                Bot.Sleep(ArmyPollDelay);
                continue;
            }

            if (!string.Equals(readySession, sessionId, StringComparison.Ordinal))
            {
                if (!WriteArmyReady(sessionId))
                    return false;

                readySession = sessionId;
            }

            if (HasMatchingArmyStart(lines, sessionId))
            {
                if (HasArmyStop(lines, sessionId))
                    return false;

                armySessionId = sessionId;
                return true;
            }

            Bot.Sleep(ArmyPollDelay);
        }

        return false;
    }

    private bool WriteArmyReady(string sessionId) =>
        AppendArmyRecord(
            new[] { "READY", ArmyProtocolVersion, sessionId, armyUsername, armyLaunchToken }
        );

    private Dictionary<string, string>? WaitForArmyReady(string sessionId)
    {
        string previousMissing = string.Empty;
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in ReadArmyLines())
            {
                string[] parts = line.Split('|');
                if (
                    parts.Length != 5
                    || parts[0] != "READY"
                    || parts[1] != ArmyProtocolVersion
                    || !string.Equals(parts[2], sessionId, StringComparison.Ordinal)
                )
                    continue;

                string username = NormalizeArmyUsername(parts[3]);
                if (
                    armyPlayers.Contains(username, StringComparer.OrdinalIgnoreCase)
                    && Guid.TryParseExact(parts[4], "N", out _)
                )
                    tokens[username] = parts[4];
            }

            if (armyPlayers.All(tokens.ContainsKey))
                return tokens;

            string missing = LogArmyNames(
                armyPlayers.Where(player => !tokens.ContainsKey(player))
            );
            if (!string.Equals(missing, previousMissing, StringComparison.Ordinal))
            {
                Core.Logger($"Waiting for Army players: {missing}.", "CoreDUCK");
                previousMissing = missing;
            }

            Bot.Sleep(ArmyPollDelay);
        }

        return null;
    }

    private bool HasMatchingArmyStart(string[] lines, string sessionId)
    {
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string[] parts = lines[index].Split('|');
            if (
                parts.Length != 3 + armyPlayers.Length
                || parts[0] != "START"
                || parts[1] != ArmyProtocolVersion
                || !string.Equals(parts[2], sessionId, StringComparison.Ordinal)
            )
                continue;

            string[] tokens = parts.Skip(3).ToArray();
            return tokens.All(token => Guid.TryParseExact(token, "N", out _))
                && string.Equals(
                    tokens[armyPlayerIndex],
                    armyLaunchToken,
                    StringComparison.Ordinal
                );
        }

        return false;
    }

    private bool RunLeaderArmySync(string stepName)
    {
        long stepId = nextArmyStepId;
        if (
            !AppendArmyRecord(
                new[]
                {
                    "STEP",
                    ArmyProtocolVersion,
                    armySessionId,
                    stepId.ToString(),
                    stepName,
                }
            )
        )
            return false;

        nextArmyStepId++;
        if (!WriteArmyArrived(stepId))
            return false;

        string previousMissing = string.Empty;
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            string[] lines = ReadArmyLines();
            if (!CanContinueArmySync(lines))
                return false;

            HashSet<string> arrived = GetArmyArrivals(lines, stepId);
            if (armyPlayers.All(arrived.Contains))
            {
                if (
                    !AppendArmyRecord(
                        new[]
                        {
                            "CONTINUE",
                            ArmyProtocolVersion,
                            armySessionId,
                            stepId.ToString(),
                        }
                    )
                )
                    return false;

                lastArmyStepId = stepId;
                return true;
            }

            string missing = LogArmyNames(
                armyPlayers.Where(player => !arrived.Contains(player))
            );
            if (!string.Equals(missing, previousMissing, StringComparison.Ordinal))
            {
                Core.Logger($"Waiting for Army players: {missing}.", "CoreDUCK");
                previousMissing = missing;
            }

            Bot.Sleep(ArmyPollDelay);
        }

        return false;
    }

    private bool RunFollowerArmySync(string expectedStepName)
    {
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            string[] lines = ReadArmyLines();
            if (!CanContinueArmySync(lines))
                return false;

            if (!TryFindNextArmyStep(lines, out long stepId, out string stepName))
            {
                Bot.Sleep(ArmyPollDelay);
                continue;
            }

            if (!string.Equals(stepName, expectedStepName, StringComparison.Ordinal))
            {
                Core.Logger("Army sync step does not match playerOne.", "CoreDUCK");
                return false;
            }

            if (!WriteArmyArrived(stepId))
                return false;

            while (!Bot.ShouldExit && !armyTransportFailed)
            {
                string[] continueLines = ReadArmyLines();
                if (!CanContinueArmySync(continueLines))
                    return false;

                if (HasArmyContinue(continueLines, stepId))
                {
                    lastArmyStepId = stepId;
                    return true;
                }

                Bot.Sleep(ArmyPollDelay);
            }
        }

        return false;
    }

    private bool WriteArmyArrived(long stepId) =>
        AppendArmyRecord(
            new[]
            {
                "ARRIVED",
                ArmyProtocolVersion,
                armySessionId,
                stepId.ToString(),
                armyUsername,
            }
        );

    private HashSet<string> GetArmyArrivals(string[] lines, long stepId)
    {
        HashSet<string> arrived = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length != 5
                || parts[0] != "ARRIVED"
                || parts[1] != ArmyProtocolVersion
                || !string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                || !long.TryParse(parts[3], out long recordStepId)
                || recordStepId != stepId
            )
                continue;

            string username = NormalizeArmyUsername(parts[4]);
            if (armyPlayers.Contains(username, StringComparer.OrdinalIgnoreCase))
                arrived.Add(username);
        }

        return arrived;
    }

    private bool TryFindNextArmyStep(
        string[] lines,
        out long nextStepId,
        out string nextStepName
    )
    {
        nextStepId = long.MaxValue;
        nextStepName = string.Empty;

        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length != 5
                || parts[0] != "STEP"
                || parts[1] != ArmyProtocolVersion
                || !string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                || !long.TryParse(parts[3], out long stepId)
                || stepId <= lastArmyStepId
                || stepId >= nextStepId
                || !IsArmyRecordName(parts[4])
            )
                continue;

            nextStepId = stepId;
            nextStepName = parts[4];
        }

        if (nextStepId != long.MaxValue)
            return true;

        nextStepId = 0;
        return false;
    }

    private bool HasArmyContinue(string[] lines, long stepId)
    {
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length == 4
                && parts[0] == "CONTINUE"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], armySessionId, StringComparison.Ordinal)
                && long.TryParse(parts[3], out long recordStepId)
                && recordStepId == stepId
            )
                return true;
        }

        return false;
    }

    private bool CanContinueArmySync(string[] lines)
    {
        string? latestSessionId = FindLatestArmyReset(lines);
        if (
            latestSessionId != null
            && !string.Equals(latestSessionId, armySessionId, StringComparison.Ordinal)
        )
        {
            if (!armySessionFailureLogged)
            {
                Core.Logger(
                    "Army sync session was replaced.",
                    "CoreDUCK",
                    messageBox: true,
                    stopBot: true
                );
                armySessionFailureLogged = true;
            }

            return false;
        }

        if (HasArmyStop(lines, armySessionId))
        {
            if (!armyStopLogged)
            {
                Core.Logger("Army sync was stopped by playerOne.", "CoreDUCK");
                armyStopLogged = true;
            }

            return false;
        }

        return true;
    }

    private static string? FindLatestArmyReset(string[] lines)
    {
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string[] parts = lines[index].Split('|');
            if (
                parts.Length == 3
                && parts[0] == "RESET"
                && parts[1] == ArmyProtocolVersion
                && Guid.TryParseExact(parts[2], "N", out _)
            )
                return parts[2];
        }

        return null;
    }

    private static bool HasArmyStop(string[] lines, string sessionId, out string stopReason)
    {
        stopReason = string.Empty;
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (
                parts.Length >= 4
                && parts[0] == "STOP"
                && parts[1] == ArmyProtocolVersion
                && string.Equals(parts[2], sessionId, StringComparison.Ordinal)
            )
            {
                stopReason = parts[3];
                return true;
            }
        }

        return false;
    }

    private static bool HasArmyStop(string[] lines, string sessionId)
    {
        return HasArmyStop(lines, sessionId, out _);
    }

    private bool AppendArmyRecord(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0 || fields.Any(field => !IsSafeArmyField(field)))
        {
            Core.Logger("Army sync record contains invalid data.", "CoreDUCK");
            return false;
        }

        string line = string.Join("|", fields);
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            try
            {
                using FileStream stream = new(
                    armySyncPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite
                );
                using StreamWriter writer = new(stream, new UTF8Encoding(false));
                writer.WriteLine(line);
                LogArmyRecord(fields);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Bot.Sleep(ArmyFileRetryDelay);
            }
            catch (Exception ex)
            {
                SetArmyTransportFailure(ex);
            }
        }

        return false;
    }

    private string[] ReadArmyLines()
    {
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            try
            {
                if (!File.Exists(armySyncPath))
                    return Array.Empty<string>();

                using FileStream stream = new(
                    armySyncPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                );
                using StreamReader reader = new(stream, Encoding.UTF8);
                List<string> lines = new();
                string? line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
                return lines.ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Bot.Sleep(ArmyFileRetryDelay);
            }
            catch (Exception ex)
            {
                SetArmyTransportFailure(ex);
            }
        }

        return Array.Empty<string>();
    }

    private bool ClearArmyFile()
    {
        while (!Bot.ShouldExit && !armyTransportFailed)
        {
            try
            {
                using FileStream stream = new(
                    armySyncPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite
                );
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Bot.Sleep(ArmyFileRetryDelay);
            }
            catch (Exception ex)
            {
                SetArmyTransportFailure(ex);
            }
        }

        return false;
    }

    private void LogArmyRecord(IReadOnlyList<string> fields)
    {
        switch (fields[0])
        {
            case "RESET":
                Core.Logger("Army sync reset.", "CoreDUCK");
                break;
            case "START":
                Core.Logger("Army sync started.", "CoreDUCK");
                break;
            case "STEP" when fields.Count > 4:
                Core.Logger($"Army sync step {fields[4]}.", "CoreDUCK");
                break;
            case "CONTINUE":
                Core.Logger("Army sync continuing.", "CoreDUCK");
                break;
            case "STOP":
                Core.Logger("Army sync stop sent.", "CoreDUCK");
                break;
        }
    }

    private void SetArmyTransportFailure(Exception exception)
    {
        armyTransportFailed = true;
        Bot.Log($"CoreDUCK Army sync file access failed: {exception}");
        if (!armyTransportFailureLogged)
        {
            Core.Logger(
                "Army sync file access failed.",
                "CoreDUCK",
                messageBox: true,
                stopBot: true
            );
            armyTransportFailureLogged = true;
        }
    }

    private bool RequireArmySession()
    {
        if (!armyInitialized)
        {
            Core.Logger(
                "Army sync has not been initialized.",
                "CoreDUCK",
                messageBox: true,
                stopBot: true
            );
            return false;
        }

        if (armySessionStarted && !string.IsNullOrEmpty(armySessionId))
            return true;

        Core.Logger(
            "Army sync session has not started.",
            "CoreDUCK",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private bool ArmyInitializationFailure(string message)
    {
        Core.Logger(
            message,
            "CoreDUCK",
            messageBox: true,
            stopBot: true
        );
        return false;
    }

    private string LogArmyNames(IEnumerable<string> usernames) =>
        string.Join(", ", usernames.Select(LogArmyName));

    private string LogArmyName(string username)
    {
        string normalized = NormalizeArmyUsername(username);
        int index = Array.FindIndex(
            armyPlayers,
            player => string.Equals(player, normalized, StringComparison.OrdinalIgnoreCase)
        );
        return index >= 0 && index < ArmyAliases.Length ? ArmyAliases[index] : "unknownPlayer";
    }

    private void ResetArmyState()
    {
        armyPlayers = Array.Empty<string>();
        armyUsername = string.Empty;
        armySyncPath = string.Empty;
        armyLaunchToken = string.Empty;
        armySessionId = string.Empty;
        armyPlayerIndex = -1;
        nextArmyStepId = 1;
        lastArmyStepId = 0;
        armyInitialized = false;
        armySessionStarted = false;
        armySessionFailureLogged = false;
        armyStopLogged = false;
        armyTransportFailed = false;
        armyTransportFailureLogged = false;
        reportedFightAttempt = 0;
        reportedFightAlive = null;
    }

    private static string NormalizeArmyUsername(string? username) =>
        username?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeArmyRecordName(string? name)
    {
        string normalized = name?.Trim().ToUpperInvariant() ?? string.Empty;
        return IsArmyRecordName(normalized) ? normalized : string.Empty;
    }

    private static bool IsArmyRecordName(string name) =>
        name.Length > 0
        && name.All(character =>
            character is >= 'A' and <= 'Z'
            || character is >= '0' and <= '9'
            || character == '_'
        );

    private static bool IsSafeArmyField(string? value) =>
        value != null
        && !value.Contains('|')
        && !value.Contains('\r')
        && !value.Contains('\n')
        && !value.Contains('\0');

    #region Dynamic Auto-Assignment & Capability Matching

    public ClassPreset GetClassPreset(string className)
    {
        string norm = className.Trim();
        if (norm.Equals("Legion Revenant", StringComparison.OrdinalIgnoreCase) || norm.Equals("Legion Revenant (IoDA)", StringComparison.OrdinalIgnoreCase))
            return LegionRevenant();
        if (norm.Equals("Lord of Order", StringComparison.OrdinalIgnoreCase))
            return LordOfOrder();
        if (norm.Equals("ArchPaladin", StringComparison.OrdinalIgnoreCase))
            return ArchPaladin();
        if (norm.Equals("StoneCrusher", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Infinity Titan", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("InfinityTitan", StringComparison.OrdinalIgnoreCase))
            return StoneCrusher();
        if (norm.Equals("Verus DoomKnight", StringComparison.OrdinalIgnoreCase))
            return VerusDoomKnight();
        if (norm.Equals("King's Echo", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Kings Echo", StringComparison.OrdinalIgnoreCase))
            return KingsEcho();
        if (norm.Equals("Arcana Invoker", StringComparison.OrdinalIgnoreCase))
            return ArcanaInvoker();
        if (norm.Equals("Dragon of Time", StringComparison.OrdinalIgnoreCase))
            return DragonOfTime();
        if (norm.Equals("Bard", StringComparison.OrdinalIgnoreCase))
            return Bard();
        if (norm.Equals("Void Highlord", StringComparison.OrdinalIgnoreCase) || norm.Equals("Void Highlord (IoDA)", StringComparison.OrdinalIgnoreCase))
            return VoidHighlord();
        if (norm.Equals("ArchFiend", StringComparison.OrdinalIgnoreCase))
            return ArchFiend();
        if (norm.Equals("Arachnomancer", StringComparison.OrdinalIgnoreCase))
            return Arachnomancer();
        if (norm.Equals("Chaos Avenger", StringComparison.OrdinalIgnoreCase))
            return ChaosAvenger();
        if (norm.Equals("Chaos Slayer Berserker", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Chaos Slayer Mystic", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Chaos Slayer Cleric", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Chaos Slayer Thief", StringComparison.OrdinalIgnoreCase)
            || norm.StartsWith("Chaos Slayer", StringComparison.OrdinalIgnoreCase))
            return ChaosSlayer();
        if (norm.Equals("Scion of Flames", StringComparison.OrdinalIgnoreCase))
            return ScionOfFlames();
        if (norm.Equals("Shaman", StringComparison.OrdinalIgnoreCase))
            return Shaman();
        if (norm.Equals("LightCaster", StringComparison.OrdinalIgnoreCase))
            return LightCaster();
        if (norm.Equals("Chrono ShadowHunter", StringComparison.OrdinalIgnoreCase) || norm.Equals("Chrono ShadowSlayer", StringComparison.OrdinalIgnoreCase))
            return ChronoShadowHunter();
        if (norm.Equals("Hollowborn Vindicator", StringComparison.OrdinalIgnoreCase))
            return HollowbornVindicator();
        if (norm.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
            return Oracle();
        if (norm.Equals("Quantum Chronomancer", StringComparison.OrdinalIgnoreCase))
            return QuantumChronomancer();
        if (norm.Equals("Guardian", StringComparison.OrdinalIgnoreCase))
            return Guardian();
        if (norm.Equals("Yami no Ronin", StringComparison.OrdinalIgnoreCase) || norm.Equals("YnR", StringComparison.OrdinalIgnoreCase))
            return YamiNoRonin();
        if (norm.Equals("Abyssal Angel's Shadow", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Abyssal Angel", StringComparison.OrdinalIgnoreCase))
            return AbyssalAngelShadow();
        if (norm.Equals("Healer", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Healer (Rare)", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Acolyte", StringComparison.OrdinalIgnoreCase))
            return Healer();
        if (norm.Equals("Imperial Chunin", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Chunin", StringComparison.OrdinalIgnoreCase))
            return ImperialChunin();
        if (norm.Equals("Alpha Omega", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Alpha DOOMmega", StringComparison.OrdinalIgnoreCase))
            return AlphaOmega();
        if (norm.Equals("Alpha Pirate", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Pirate", StringComparison.OrdinalIgnoreCase))
            return AlphaPirate();
        if (norm.Equals("Antique Hunter", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Artifact Hunter", StringComparison.OrdinalIgnoreCase))
            return AntiqueHunter();
        if (norm.Equals("Arcane Dark Caster", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Mystical Dark Caster", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Timeless Dark Caster", StringComparison.OrdinalIgnoreCase))
            return ArcaneDarkCaster();
        if (norm.Equals("ArchMage", StringComparison.OrdinalIgnoreCase))
            return ArchMage();
        if (norm.Equals("Assassin", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Ninja Warrior", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Ninja", StringComparison.OrdinalIgnoreCase))
            return Assassin();
        if (norm.Equals("Barber", StringComparison.OrdinalIgnoreCase))
            return Barber();
        if (norm.Equals("BattleMage", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Royal BattleMage", StringComparison.OrdinalIgnoreCase))
            return BattleMage();
        if (norm.Equals("Warlord", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Warrior", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Warrior (Rare)", StringComparison.OrdinalIgnoreCase))
            return Warlord();
        if (norm.Equals("BeastMaster", StringComparison.OrdinalIgnoreCase))
            return BeastMaster();
        if (norm.Equals("Berserker", StringComparison.OrdinalIgnoreCase))
            return Berserker();
        if (norm.Equals("BladeMaster Assassin", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("SwordMaster Assassin", StringComparison.OrdinalIgnoreCase))
            return BladeMasterAssassin();
        if (norm.Equals("BladeMaster", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("SwordMaster", StringComparison.OrdinalIgnoreCase))
            return BladeMaster();
        if (norm.Equals("Blaze Binder", StringComparison.OrdinalIgnoreCase))
            return BlazeBinder();
        if (norm.Equals("Blood Ancient", StringComparison.OrdinalIgnoreCase))
            return BloodAncient();
        if (norm.Equals("Blood Sorceress", StringComparison.OrdinalIgnoreCase))
            return BloodSorceress();
        if (norm.Equals("Blood Titan", StringComparison.OrdinalIgnoreCase))
            return BloodTitan();
        if (norm.Equals("Chrono Assassin", StringComparison.OrdinalIgnoreCase))
            return ChronoAssassin();
        if (norm.Equals("Classic DoomKnight", StringComparison.OrdinalIgnoreCase))
            return ClassicDoomKnight();
        if (norm.Equals("Classic Exalted Soul Cleaver", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Classic Soul Cleaver", StringComparison.OrdinalIgnoreCase))
            return ClassicSoulCleaver();
        if (norm.Equals("Classic Legion DoomKnight", StringComparison.OrdinalIgnoreCase))
            return ClassicLegionDoomKnight();
        if (norm.Equals("Classic Ninja", StringComparison.OrdinalIgnoreCase))
            return ClassicNinja();
        if (norm.Equals("Classic Paladin", StringComparison.OrdinalIgnoreCase))
            return ClassicPaladin();
        if (norm.Equals("Classic Pirate", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Classic Alpha Pirate", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Rogue", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Rogue (Rare)", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Renegade", StringComparison.OrdinalIgnoreCase))
            return ClassicPirate();
        if (norm.Equals("Cryomancer", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Dark Cryomancer", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Sakura Cryomancer", StringComparison.OrdinalIgnoreCase))
            return Cryomancer();
        if (norm.Equals("Daimon", StringComparison.OrdinalIgnoreCase))
            return Daimon();
        if (norm.Equals("Dark Caster", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Immortal Dark Caster", StringComparison.OrdinalIgnoreCase))
            return DarkCaster();
        if (norm.Equals("Dark Harbinger", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Exalted Harbinger", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Exalted Soul Cleaver", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Soul Cleaver", StringComparison.OrdinalIgnoreCase))
            return DarkHarbinger();
        if (norm.Equals("Dark Legendary Hero", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Legendary Hero", StringComparison.OrdinalIgnoreCase))
            return DarkLegendaryHero();
        if (norm.Equals("Dark Lord", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Darkside", StringComparison.OrdinalIgnoreCase))
            return DarkLord();
        if (norm.Equals("Dark Metal Necro", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Doom Metal Necro", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Heavy Metal Necro", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Heavy Metal Rockstar", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Shadow Ripper", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Unchained Rockstar", StringComparison.OrdinalIgnoreCase))
            return DarkMetalNecro();
        if (norm.Equals("Dark Ultra OmniNight", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Ultra OmniKnight", StringComparison.OrdinalIgnoreCase))
            return DarkUltraOmniNight();
        if (norm.Equals("Darkblood StormKing", StringComparison.OrdinalIgnoreCase))
            return DarkbloodStormKing();
        if (norm.Equals("DeathKnight", StringComparison.OrdinalIgnoreCase))
            return DeathKnight();
        if (norm.Equals("DeathKnight Lord", StringComparison.OrdinalIgnoreCase))
            return DeathKnightLord();
        if (norm.Equals("DoomKnight", StringComparison.OrdinalIgnoreCase))
            return DoomKnight();
        if (norm.Equals("Draco Knight", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Dragon Knight", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Drakkar Knight", StringComparison.OrdinalIgnoreCase))
            return DracoKnight();
        if (norm.Equals("Dragon Shinobi", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("DragonSoul Shinobi", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Shadow Dragon Shinobi", StringComparison.OrdinalIgnoreCase))
            return DragonShinobi();
        if (norm.Equals("Dragonslayer", StringComparison.OrdinalIgnoreCase))
            return Dragonslayer();
        if (norm.Equals("Dragonslayer General", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("ShadowFlame DragonLord", StringComparison.OrdinalIgnoreCase))
            return DragonslayerGeneral();
        if (norm.Equals("Drakel Warlord", StringComparison.OrdinalIgnoreCase))
            return DrakelWarlord();
        if (norm.Equals("Elemental Dracomancer", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Love Caster", StringComparison.OrdinalIgnoreCase))
            return ElementalDracomancer();
        if (norm.Equals("Enchanted Vampire Lord", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Royal Vampire Lord", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Vampire", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Vampire Lord", StringComparison.OrdinalIgnoreCase))
            return EnchantedVampireLord();
        if (norm.Equals("Enforcer", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("ProtoSartorium", StringComparison.OrdinalIgnoreCase))
            return Enforcer();
        if (norm.Equals("Eternal Inversionist", StringComparison.OrdinalIgnoreCase))
            return EternalInversionist();
        if (norm.Equals("Evolved ClawSuit", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Prismatic ClawSuit", StringComparison.OrdinalIgnoreCase))
            return EvolvedClawSuit();
        if (norm.Equals("Evolved Dark Caster", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Infinite Dark Caster", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Infinite Legion Dark Caster", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Legion Evolved Dark Caster", StringComparison.OrdinalIgnoreCase))
            return EvolvedDarkCaster();
        if (norm.Equals("Evolved Leprechaun", StringComparison.OrdinalIgnoreCase))
            return EvolvedLeprechaun();
        if (norm.Equals("Evolved Pumpkin Lord", StringComparison.OrdinalIgnoreCase))
            return EvolvedPumpkinLord();
        if (norm.Equals("Evolved Shaman", StringComparison.OrdinalIgnoreCase))
            return EvolvedShaman();
        if (norm.Equals("Frost SpiritReaver", StringComparison.OrdinalIgnoreCase))
            return FrostSpiritReaver();
        if (norm.Equals("Frostval Barbarian", StringComparison.OrdinalIgnoreCase))
            return FrostvalBarbarian();
        if (norm.Equals("Glacial Berserker", StringComparison.OrdinalIgnoreCase))
            return GlacialBerserker();
        if (norm.Equals("Grunge Rocker", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Neo Metal Necro", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Nu Metal Necro", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Shadow Rocker", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Unchained Rocker", StringComparison.OrdinalIgnoreCase))
            return GrungeRocker();
        if (norm.Equals("Heroic Naval Commander", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Legendary Naval Commander", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Naval Commander", StringComparison.OrdinalIgnoreCase))
            return HeroicNavalCommander();
        if (norm.Equals("HighSeas Commander", StringComparison.OrdinalIgnoreCase))
            return HighSeasCommander();
        if (norm.Equals("Hobo Highlord", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Hollowborn No-Class", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("No Class", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Obsidian No Class", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Simple Class", StringComparison.OrdinalIgnoreCase))
            return HoboHighlord();
        if (norm.Equals("Horc Evader", StringComparison.OrdinalIgnoreCase))
            return HorcEvader();
        if (norm.Equals("Legendary Elemental Warrior", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Mythic Elemental Warrior", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Ultra Elemental Warrior", StringComparison.OrdinalIgnoreCase))
            return LegendaryElementalWarrior();
        if (norm.Equals("Legion DoomKnight", StringComparison.OrdinalIgnoreCase))
            return LegionDoomKnight();
        if (norm.Equals("Legion SwordMaster Assassin", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Legion BladeMaster Assassin", StringComparison.OrdinalIgnoreCase))
            return LegionSwordMasterAssassin();
        if (norm.Equals("Lich", StringComparison.OrdinalIgnoreCase))
            return Lich();
        if (norm.Equals("LightMage", StringComparison.OrdinalIgnoreCase))
            return LightMage();
        if (norm.Equals("Lycan", StringComparison.OrdinalIgnoreCase))
            return Lycan();
        if (norm.Equals("Mage", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Mage (Rare)", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Sorcerer", StringComparison.OrdinalIgnoreCase))
            return Mage();
        if (norm.Equals("Martial Artist", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Master Martial Artist", StringComparison.OrdinalIgnoreCase))
            return MartialArtist();
        if (norm.Equals("Master Ranger", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Ranger", StringComparison.OrdinalIgnoreCase))
            return MasterRanger();
        if (norm.Equals("MechaJouster", StringComparison.OrdinalIgnoreCase))
            return MechaJouster();
        if (norm.Equals("Necromancer", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Pinkomancer", StringComparison.OrdinalIgnoreCase))
            return Necromancer();
        if (norm.Equals("Northlands Monk", StringComparison.OrdinalIgnoreCase))
            return NorthlandsMonk();
        if (norm.Equals("Paladin", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Silver Paladin", StringComparison.OrdinalIgnoreCase))
            return Paladin();
        if (norm.Equals("Pink Romancer", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Pyromancer", StringComparison.OrdinalIgnoreCase))
            return Pyromancer();
        if (norm.Equals("Rustbucket", StringComparison.OrdinalIgnoreCase))
            return Rustbucket();
        if (norm.Equals("Scarlet Sorceress", StringComparison.OrdinalIgnoreCase))
            return ScarletSorceress();
        if (norm.Equals("ShadowScythe General", StringComparison.OrdinalIgnoreCase))
            return ShadowScytheGeneral();
        if (norm.Equals("SkyGuard Grenadier", StringComparison.OrdinalIgnoreCase))
            return SkyGuardGrenadier();
        if (norm.Equals("Sovereign of Storms", StringComparison.OrdinalIgnoreCase))
            return SovereignOfStorms();
        if (norm.Equals("The Collector", StringComparison.OrdinalIgnoreCase))
            return TheCollector();
        if (norm.Equals("Thief of Hours", StringComparison.OrdinalIgnoreCase))
            return ThiefOfHours();
        if (norm.Equals("Troll Spellsmith", StringComparison.OrdinalIgnoreCase))
            return TrollSpellsmith();
        if (norm.Equals("Undead Goat", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Unundead Goat", StringComparison.OrdinalIgnoreCase))
            return UndeadGoat();
        if (norm.Equals("Undead Leperchaun", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("Unlucky Leperchaun", StringComparison.OrdinalIgnoreCase))
            return UndeadLeperchaun();
        if (norm.Equals("UndeadSlayer", StringComparison.OrdinalIgnoreCase))
            return UndeadSlayer();

        return new ClassPreset
        {
            ClassName = norm,
            Skills = new[] { 1, 2, 3, 4 },
            SkillMode = SkillEngineMode.Simple,
            BaseEnhancement = EnhancementType.Lucky,
            CapeEnhancement = CapeSpecial.Vainglory,
            HelmEnhancement = HelmSpecial.Forge,
            WeaponEnhancement = WeaponSpecial.Valiance
        };
    }

    public List<string> ProbeForgeEnhancements()
    {
        CoreAdvanced adv = new();
        List<string> enh = new();
        try
        {
            if (adv.uElysium()) enh.Add("Elysium");
            if (adv.uArcanasConcerto()) enh.Add("ArcanasConcerto");
            if (adv.uValiance()) enh.Add("Valiance");
            if (adv.uRavenous()) enh.Add("Ravenous");
            if (adv.uPraxis()) enh.Add("Praxis");
            if (adv.uDauntless()) enh.Add("Dauntless");
            if (adv.uSmite()) enh.Add("Smite");
            if (adv.uAcheron()) enh.Add("Acheron");
            if (adv.uLacerate()) enh.Add("Lacerate");
        }
        catch { }
        return enh;
    }

    private static readonly object FileLogLock = new();
    private static readonly string DuckLogDir = Path.Combine(
        ClientFileSources.SkuaDIR,
        "Scripts",
        "DUCKScripts",
        "Logs"
    );
    private static readonly string DuckLogFile = Path.Combine(DuckLogDir, "duck_army.log");

    public void FileLog(string message, string prefix = "CoreDUCK")
    {
        try
        {
            if (!Directory.Exists(DuckLogDir))
                Directory.CreateDirectory(DuckLogDir);

            string username = Bot.Player?.Username ?? "Unknown";
            string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{username}] [{prefix}] {message}{Environment.NewLine}";

            lock (FileLogLock)
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        File.AppendAllText(DuckLogFile, logLine);
                        break;
                    }
                    catch
                    {
                        Bot.Sleep(50);
                    }
                }
            }
        }
        catch { }
    }

    public string[]? DiscoverParty(
            string syncFileName,
            int armySize = 4,
            int timeoutSeconds = 120
        )
    {
        string syncPath = Path.Combine(ClientFileSources.SkuaOptionsDIR, syncFileName);
        string username = NormalizeArmyUsername(Core.Username());

        void WritePresence()
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string entry = $"{username}:ACTIVE:{ts}";
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    List<string> lines = File.Exists(syncPath) ? File.ReadAllLines(syncPath).ToList() : new();
                    lines.RemoveAll(l => l.StartsWith($"{username}:", StringComparison.OrdinalIgnoreCase));
                    lines.Add(entry);
                    File.WriteAllLines(syncPath, lines);
                    return;
                }
                catch { Bot.Sleep(100); }
            }
        }

        WritePresence();

        DateTime waitStart = DateTime.UtcNow;
        const int staleThreshold = 30;

        while (!Bot.ShouldExit)
        {
            if ((DateTime.UtcNow - waitStart).TotalSeconds > timeoutSeconds)
            {
                FileLog($"[DiscoverParty] Timed out waiting for {armySize} accounts to join gauntlet.", "DiscoverParty");
                Core.Logger($"[DiscoverParty] Timed out waiting for {armySize} accounts to join gauntlet.", "DiscoverParty", messageBox: true, stopBot: true);
                return null;
            }

            string[] lines = Array.Empty<string>();
            try
            {
                if (File.Exists(syncPath))
                    lines = File.ReadAllLines(syncPath);
            }
            catch { }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            List<string> activePlayers = new();

            foreach (string line in lines)
            {
                string[] parts = line.Split(':');
                if (parts.Length < 3) continue;
                if (!string.Equals(parts[1], "ACTIVE", StringComparison.OrdinalIgnoreCase)) continue;
                if (!long.TryParse(parts[parts.Length - 1], out long ts)) continue;
                if (now - ts > staleThreshold) continue;

                string pName = NormalizeArmyUsername(parts[0]);
                if (!activePlayers.Contains(pName))
                    activePlayers.Add(pName);
            }

            if (activePlayers.Count >= armySize)
            {
                activePlayers.Sort(StringComparer.OrdinalIgnoreCase);
                Bot.Sleep(500);
                return activePlayers.Take(armySize).ToArray();
            }

            WritePresence();
            Bot.Sleep(500);
        }

        return null;
    }

    public int SyncPartyRequiredMask(
        string syncFileName,
        int localMask,
        int armySize = 4,
        int timeoutSeconds = 60
    )
    {
        string syncPath = Path.Combine(ClientFileSources.SkuaOptionsDIR, syncFileName);
        string username = NormalizeArmyUsername(Core.Username());

        void WriteEntry()
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string entry = $"{username}:MASK|{localMask}:{ts}";
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    List<string> lines = File.Exists(syncPath) ? File.ReadAllLines(syncPath).ToList() : new();
                    lines.RemoveAll(l => l.StartsWith($"{username}:", StringComparison.OrdinalIgnoreCase));
                    lines.Add(entry);
                    File.WriteAllLines(syncPath, lines);
                    return;
                }
                catch { Bot.Sleep(100); }
            }
        }

        WriteEntry();

        DateTime waitStart = DateTime.UtcNow;
        const int staleThreshold = 30;

        while (!Bot.ShouldExit)
        {
            if ((DateTime.UtcNow - waitStart).TotalSeconds > timeoutSeconds)
            {
                Core.Logger($"[PartySync] Timed out waiting for {armySize} accounts to register completion masks.", "PartySync");
                return localMask;
            }

            string[] lines = Array.Empty<string>();
            try
            {
                if (File.Exists(syncPath))
                    lines = File.ReadAllLines(syncPath);
            }
            catch { }

            long currentTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Dictionary<string, int> registeredMasks = new(StringComparer.OrdinalIgnoreCase);

            foreach (string l in lines)
            {
                string[] parts = l.Split(':');
                if (parts.Length < 3) continue;
                if (!parts[1].StartsWith("MASK|", StringComparison.OrdinalIgnoreCase)) continue;
                if (!long.TryParse(parts[parts.Length - 1], out long ts)) continue;
                if (currentTs - ts > staleThreshold) continue;

                if (int.TryParse(parts[1].Substring(5), out int mask))
                    registeredMasks[parts[0]] = mask;
            }

            if (registeredMasks.Count >= armySize)
            {
                int combinedMask = 0;
                foreach (int mask in registeredMasks.Values)
                    combinedMask |= mask;

                Bot.Sleep(500);
                return combinedMask;
            }

            WriteEntry();
            Bot.Sleep(500);
        }

        return localMask;
    }

    public bool ClearAllSyncFiles(string syncFileName)
    {
        try
        {
            string mainSync = Path.Combine(ClientFileSources.SkuaOptionsDIR, syncFileName);
            string assignSync = Path.Combine(
                ClientFileSources.SkuaOptionsDIR,
                Path.GetFileNameWithoutExtension(syncFileName) + "_assign.sync"
            );

            if (File.Exists(mainSync)) File.WriteAllText(mainSync, string.Empty);
            if (File.Exists(assignSync)) File.WriteAllText(assignSync, string.Empty);
            return true;
        }
        catch { return false; }
    }

    public bool ClearAssignSync(string syncFileName)
    {
        try
        {
            string assignSyncFile = Path.Combine(
                ClientFileSources.SkuaOptionsDIR,
                Path.GetFileNameWithoutExtension(syncFileName) + "_assign.sync"
            );
            if (File.Exists(assignSyncFile))
                File.WriteAllText(assignSyncFile, string.Empty);
            return true;
        }
        catch { return false; }
    }

    public DuckAssignmentResult? AutoAssignAndEquip(
            string syncFileName,
            DuckSlotRequirement[] slotRequirements,
            int armySize = 4,
            int timeoutSeconds = 120,
            bool allowDuplicates = false
        )
    {
        if (slotRequirements == null || slotRequirements.Length == 0 || armySize < 1)
            return null;

        string assignSyncFile = Path.Combine(
            ClientFileSources.SkuaOptionsDIR,
            Path.GetFileNameWithoutExtension(syncFileName) + "_assign.sync"
        );

        // Collect needed classes across all slots
        HashSet<string> allNeeded = new(StringComparer.OrdinalIgnoreCase);
        foreach (DuckSlotRequirement slot in slotRequirements)
            foreach (string cls in slot.CandidateClasses)
                allNeeded.Add(cls);

        // Check ownership in inventory + bank (matching specifically ItemCategory.Class with alias/IoDA support)
        List<string> myOwnedClasses = new();
        foreach (string cls in allNeeded)
        {
            var classItem = ResolveClassItem(cls);

            if (classItem != null)
            {
                if (Bot.Bank.Items.Any(x => x.ID == classItem.ID))
                {
                    Bot.Bank.ToInventory(classItem.ID);
                    Bot.Wait.ForTrue(() => Bot.Inventory.Items.Any(x => x.ID == classItem.ID), 20);
                }
                if (Bot.Inventory.Items.Any(x => x.ID == classItem.ID))
                    myOwnedClasses.Add(cls);
            }
        }

        List<string> myEnhancements = ProbeForgeEnhancements();
        string username = NormalizeArmyUsername(Core.Username());
        string payload = $"READY|Classes={string.Join(",", myOwnedClasses)}|Enh={string.Join(",", myEnhancements)}";

        void WriteAssignEntry(string file, string user, string data)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string line = $"{user}:{data}:{now}";
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    List<string> lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new();
                    lines.RemoveAll(l => l.StartsWith($"{user}:", StringComparison.OrdinalIgnoreCase));
                    lines.Add(line);
                    File.WriteAllLines(file, lines);
                    return;
                }
                catch { Bot.Sleep(100); }
            }
        }

        WriteAssignEntry(assignSyncFile, username, payload);
        FileLog($"[AutoAssign] Registered capabilities: Classes=[{string.Join(", ", myOwnedClasses)}], Enh=[{string.Join(", ", myEnhancements)}]", "AutoAssign");
        Core.Logger($"[AutoAssign] Registered capabilities: Classes=[{string.Join(", ", myOwnedClasses)}], Enh=[{string.Join(", ", myEnhancements)}]");

        // Wait for all members to register READY
        const int staleThreshold = 30;
        DateTime waitStart = DateTime.UtcNow;
        int lastRegisteredCount = -1;
        List<DuckPlayerCapabilities> players = new();

        while (!Bot.ShouldExit)
        {
            if ((DateTime.UtcNow - waitStart).TotalSeconds > timeoutSeconds)
            {
                FileLog($"[AutoAssign] Timed out waiting for {armySize} accounts.", "AutoAssign");
                Core.Logger($"[AutoAssign] Timed out waiting for {armySize} accounts.", "AutoAssign", messageBox: true, stopBot: true);
                return null;
            }

            string[] lines = Array.Empty<string>();
            try
            {
                if (File.Exists(assignSyncFile))
                    lines = File.ReadAllLines(assignSyncFile);
            }
            catch { }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            players.Clear();

            foreach (string line in lines)
            {
                string[] parts = line.Split(':');
                if (parts.Length < 3) continue;
                if (!parts[1].StartsWith("READY|", StringComparison.OrdinalIgnoreCase)) continue;
                if (!long.TryParse(parts[parts.Length - 1], out long ts)) continue;
                if (now - ts > staleThreshold) continue;

                var caps = new DuckPlayerCapabilities { Name = parts[0] };
                string payloadBody = parts[1].Substring(6);
                string[] sections = payloadBody.Split('|');
                foreach (string sec in sections)
                {
                    if (sec.StartsWith("Classes=", StringComparison.OrdinalIgnoreCase))
                        caps.Classes = sec.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    else if (sec.StartsWith("Enh=", StringComparison.OrdinalIgnoreCase))
                        caps.Enhancements = sec.Substring(4).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
                players.Add(caps);
            }

            if (players.Count != lastRegisteredCount)
            {
                lastRegisteredCount = players.Count;
                Core.Logger($"[AutoAssign] Registered: {players.Count}/{armySize}");
            }

            if (players.Count >= armySize)
                break;

            WriteAssignEntry(assignSyncFile, username, payload);
            Bot.Sleep(500);
        }

        if (Bot.ShouldExit) return null;
        Bot.Sleep(500);

        // Deterministic Greedy Assignment
        List<string> sortedPlayers = players
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(armySize)
            .ToList();

        int myPlayerIndex = sortedPlayers.FindIndex(p => string.Equals(p, username, StringComparison.OrdinalIgnoreCase));
        if (myPlayerIndex < 0)
        {
            Core.Logger($"[AutoAssign] Error: Account {username} is not registered in session.", "AutoAssign", messageBox: true, stopBot: true);
            return null;
        }

        // Global Optimal Weighted Priority Assignment Solver
        var solution = SolveOptimalAssignment(sortedPlayers, players, slotRequirements, allowDuplicates);
        if (solution == null)
        {
            FileLog($"[AutoAssign] No valid party assignment found satisfying constraints across {armySize} accounts.", "AutoAssign");
            Core.Logger($"[AutoAssign] No valid party assignment found satisfying constraints across {armySize} accounts.", "AutoAssign", messageBox: true, stopBot: true);
            return null;
        }

        var assignments = solution.Value.Assignments;
        var slotAssignments = solution.Value.SlotAssignments;

        for (int s = 0; s < slotRequirements.Length; s++)
        {
            if (slotAssignments.TryGetValue(s, out var pair))
            {
                FileLog($"[AutoAssign] Slot {s + 1} -> {pair.Player} ({pair.ClassName})", "AutoAssign");
                Core.Logger($"[AutoAssign] Slot {s + 1} -> {pair.Player} ({pair.ClassName})");
            }
        }

        if (!assignments.TryGetValue(username, out string? myAssignedClass) || string.IsNullOrEmpty(myAssignedClass))
        {
            Core.Logger($"[AutoAssign] {username} was not assigned a class! Check ownership.", "AutoAssign", messageBox: true, stopBot: true);
            return null;
        }

        ClassPreset assignedPreset = GetClassPreset(myAssignedClass);

        Dictionary<int, string> pNumToClass = new();
        for (int i = 0; i < sortedPlayers.Count; i++)
        {
            if (assignments.TryGetValue(sortedPlayers[i], out string? cls))
                pNumToClass[i + 1] = cls;
        }

        return new DuckAssignmentResult
        {
            Preset = assignedPreset,
            DiscoveredPlayers = sortedPlayers.ToArray(),
            PlayerNumber = myPlayerIndex + 1,
            RoleName = myAssignedClass,
            PlayerNumberToClass = pNumToClass
        };
    }

    public bool StartArmySyncDynamic(string syncFileName, string[] discoveredPlayers)
    {
        if (!ValidateFunctionBasedSkillsDisabled())
            return false;

        ResetArmyState();

        if (discoveredPlayers == null || discoveredPlayers.Length < 2 || discoveredPlayers.Length > 7)
            return ArmyInitializationFailure("Discovered players count must be from 2 through 7.");

        string username = NormalizeArmyUsername(Core.Username());
        int playerIndex = Array.FindIndex(
            discoveredPlayers,
            player => string.Equals(player, username, StringComparison.OrdinalIgnoreCase)
        );
        if (playerIndex < 0)
            return ArmyInitializationFailure("Current account is not in the discovered Army roster.");

        armyPlayers = discoveredPlayers;
        armyUsername = username;
        armyPlayerIndex = playerIndex;
        armyLaunchToken = Guid.NewGuid().ToString("N");
        armySessionId = armyPlayerIndex == 0 ? Guid.NewGuid().ToString("N") : string.Empty;
        armySyncPath = Path.Combine(ClientFileSources.SkuaOptionsDIR, syncFileName);
        armyInitialized = true;

        bool started = armyPlayerIndex == 0
            ? StartLeaderArmySession()
            : JoinFollowerArmySession();
        armySessionStarted = started;
        return started;
    }

    private static (Dictionary<string, string> Assignments, Dictionary<int, (string Player, string ClassName)> SlotAssignments)? SolveOptimalAssignment(
        List<string> sortedPlayers,
        List<DuckPlayerCapabilities> players,
        DuckSlotRequirement[] slotRequirements,
        bool allowDuplicates
    )
    {
        int slotCount = Math.Min(slotRequirements.Length, sortedPlayers.Count);
        var playerCapsMap = players.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var options = new List<(string Player, string ClassName, int Score)>[slotCount];
        for (int s = 0; s < slotCount; s++)
        {
            options[s] = new List<(string Player, string ClassName, int Score)>();
            var req = slotRequirements[s];
            foreach (string pName in sortedPlayers)
            {
                if (!playerCapsMap.TryGetValue(pName, out var pCaps)) continue;
                if (!string.IsNullOrEmpty(req.RequiredWeaponEnhancement) &&
                    !pCaps.Enhancements.Contains(req.RequiredWeaponEnhancement, StringComparer.OrdinalIgnoreCase))
                    continue;

                for (int rank = 0; rank < req.CandidateClasses.Length; rank++)
                {
                    string candidate = req.CandidateClasses[rank];
                    if (pCaps.Classes.Any(c => c.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                    {
                        int score = 10000 - (rank * 100);
                        options[s].Add((pName, candidate, score));
                        break;
                    }
                }
            }
            options[s] = options[s]
                .OrderByDescending(o => o.Score)
                .ThenBy(o => o.Player, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        int bestScore = -1;
        Dictionary<string, string>? bestAssignments = null;
        Dictionary<int, (string Player, string ClassName)>? bestSlotAssignments = null;

        var currentAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentSlotAssignments = new Dictionary<int, (string Player, string ClassName)>();
        var usedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedClasses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Backtrack(int slotIndex, int currentScore)
        {
            if (slotIndex == slotCount)
            {
                if (currentScore > bestScore)
                {
                    bestScore = currentScore;
                    bestAssignments = new Dictionary<string, string>(currentAssignments, StringComparer.OrdinalIgnoreCase);
                    bestSlotAssignments = new Dictionary<int, (string Player, string ClassName)>(currentSlotAssignments);
                }
                return;
            }

            foreach (var opt in options[slotIndex])
            {
                if (usedPlayers.Contains(opt.Player)) continue;
                int count = usedClasses.GetValueOrDefault(opt.ClassName, 0);
                if (!allowDuplicates && count >= 1) continue;

                usedPlayers.Add(opt.Player);
                usedClasses[opt.ClassName] = count + 1;
                currentAssignments[opt.Player] = opt.ClassName;
                currentSlotAssignments[slotIndex] = (opt.Player, opt.ClassName);

                Backtrack(slotIndex + 1, currentScore + opt.Score);

                usedPlayers.Remove(opt.Player);
                usedClasses[opt.ClassName] = count;
                currentAssignments.Remove(opt.Player);
                currentSlotAssignments.Remove(slotIndex);
            }
        }

        Backtrack(0, 0);

        if (bestAssignments == null || bestSlotAssignments == null)
            return null;

        return (bestAssignments, bestSlotAssignments);
    }

    #endregion

    #region Inventory & Bank Quantity Utilities

    /// <summary>
    /// Returns the combined quantity of an item across the player's Bag and Bank without forcing an unbank.
    /// </summary>
    public int GetTotalItemQuantity(string itemName)
    {
        int inv = Bot.Inventory.GetQuantity(itemName);
        int bank = Bot.Bank.TryGetItem(itemName, out InventoryItem? bItem) && bItem != null ? bItem.Quantity : 0;
        return inv + bank;
    }

    /// <summary>
    /// Checks if the item exists in the desired quantity across Bag, Bank, Temp Inventory, or House without unbanking.
    /// </summary>
    public bool CheckItemQuantity(string itemName, int targetQuantity)
    {
        if (targetQuantity <= 0)
            return true;

        return Core.CheckInventory(itemName, targetQuantity, toInv: false) || GetTotalItemQuantity(itemName) >= targetQuantity;
    }

    #endregion

}
public enum UltraRunResult
{
    Completed,
    AttemptsExhausted,
    Failed,
}

public enum SkillEngineMode
{
    Simple,
    Strict,
    KingsEcho,
    ArcanaInvoker,
    Shaman,
    LightCasterHealing,
    VoidHighlord,
    ChronoShadowHunterStable,
    ChronoShadowHunterGunslinger,
    ChaosAvengerOptimized,
    ScionOfFlames,
    Guardian,
    QuantumChronomancer,
    ArachnomancerSolo,
    ArchMageCorporeal,
    ArchMageAstral,
    BeastMaster,
    Berserker,
    ClassicSoulCleaver,
    ClassicPirate,
    DarkHarbinger,
    DarkMetalNecro,
    DarkbloodStormKing,
    DracoKnight,
    EvolvedClawSuit,
    EvolvedLeprechaun,
    EvolvedShaman,
    GrungeRocker,
    HorcEvader,
    LegionSwordMasterAssassinSolo,
    LegionSwordMasterAssassinFarm,
    Pyromancer,
    ScarletSorceress,
    SovereignOfStorms,
    UndeadSlayer,
    Dragonslayer,
    Assassin,
}

public class ClassPreset
{
    public string ClassName { get; set; } = string.Empty;
    public string[] AlternateClassNames { get; set; } = Array.Empty<string>();
    public int[] Skills { get; set; } = Array.Empty<int>();
    public SkillEngineMode SkillMode { get; set; } = SkillEngineMode.Simple;
    public int SurvivalSkill { get; set; } = 0;
    public int SurvivalHealthThreshold { get; set; } = 0;
    public EnhancementType BaseEnhancement { get; set; } = EnhancementType.Lucky;
    public CapeSpecial CapeEnhancement { get; set; } = CapeSpecial.None;
    public HelmSpecial HelmEnhancement { get; set; } = HelmSpecial.None;
    public WeaponSpecial WeaponEnhancement { get; set; } = WeaponSpecial.None;
    public WeaponSpecial[] WeaponEnhancementFallbacks { get; set; } =
        new[] { WeaponSpecial.Valiance, WeaponSpecial.Health_Vamp };
    public string Tonic { get; set; } = string.Empty;
    public string Elixir { get; set; } = string.Empty;
    public string? CombatPotion { get; set; }
}


public class DuckSlotRequirement
{
    public string[] CandidateClasses { get; set; } = Array.Empty<string>();
    public string RequiredWeaponEnhancement { get; set; } = string.Empty;
}

public class DuckPlayerCapabilities
{
    public string Name { get; set; } = string.Empty;
    public List<string> Classes { get; set; } = new();
    public List<string> Enhancements { get; set; } = new();
}

public class DuckAssignmentResult
{
    public ClassPreset Preset { get; set; } = new();
    public string[] DiscoveredPlayers { get; set; } = Array.Empty<string>();
    public int PlayerNumber { get; set; }
    public bool IsLeader => PlayerNumber == 1;
    public string RoleName { get; set; } = string.Empty;
    public Dictionary<int, string> PlayerNumberToClass { get; set; } = new();

    public int GetPlayerNumberForClass(string className)
    {
        foreach (var kv in PlayerNumberToClass)
        {
            if (string.Equals(kv.Value, className, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }
        return -1;
    }


}
