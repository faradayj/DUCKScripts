/*
name: UltraEnhancementsv4
description: Centralized dynamic enhancement router using JSON configuration.
tags: ultra, enhancements, json
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/UltraCustomClassSyncv4.cs

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

public class EnhancementProfile
{
    public string Type { get; set; } = "Lucky";
    public string Helm { get; set; } = "None";
    public string Weapon { get; set; } = "None";
    public string Cape { get; set; } = "None";
}

// SlotRequirement is now defined in UltraCustomClassSyncv4.cs
// public class SlotRequirement ...

public class UltraEnhancementsv4
{
    public IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots C => CoreBots.Instance;

    private static Dictionary<string, Dictionary<string, EnhancementProfile>>? _configCache;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Skua", "Scripts", "Customv2", "Ultrasv4", "DependenciesUltras", "ultras_enhancements.json"
    );

    /// <summary>
    /// Thread-safe lazy loader for ultras_enhancements.json using FileShare.ReadWrite.
    /// </summary>
    private static Dictionary<string, Dictionary<string, EnhancementProfile>> LoadConfig()
    {
        if (_configCache != null)
            return _configCache;

        if (!File.Exists(ConfigPath))
        {
            CoreBots.Instance.Logger($"[UltraEnhancementsv4] Config missing at {ConfigPath}", "Warning");
            return _configCache = new(StringComparer.OrdinalIgnoreCase);
        }

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using FileStream fs = new(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader sr = new(fs);
                string json = sr.ReadToEnd();

                _configCache = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, EnhancementProfile>>>(json)
                    ?? new(StringComparer.OrdinalIgnoreCase);

                return _configCache;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                CoreBots.Instance.Logger($"[UltraEnhancementsv4] Failed to load JSON: {ex.Message}", "Error");
                break;
            }
        }

        return _configCache = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Retrieves the profile for a given boss context and class name.
    /// Falls back to the "default" block if no boss-specific entry exists.
    /// </summary>
    public static EnhancementProfile GetProfile(string bossContext, string className)
    {
        var config = LoadConfig();
        string ctx = string.IsNullOrWhiteSpace(bossContext) ? "default" : bossContext.Trim().ToLowerInvariant();
        string cls = className.Trim();

        // 1. Check specific boss context
        if (config.TryGetValue(ctx, out var bossMap) && bossMap.TryGetValue(cls, out var profile))
            return profile;

        // 2. Fall back to global default context
        if (config.TryGetValue("default", out var defaultMap) && defaultMap.TryGetValue(cls, out var defaultProfile))
            return defaultProfile;

        return new EnhancementProfile();
    }

    /// <summary>
    /// Extracts top-tier Forge weapon specials required for capability-aware army matching.
    /// </summary>
    public static string GetRequiredEnhancement(string bossContext, string className)
    {
        var profile = GetProfile(bossContext, className);
        string weapon = profile.Weapon.Trim();

        return weapon switch
        {
            "Elysium" => "Elysium",
            "Arcanas_Concerto" or "Arcana" or "ArcanasConcerto" => "ArcanasConcerto",
            "Valiance" => "Valiance",
            "Praxis" => "Praxis",
            "Dauntless" => "Dauntless",
            "Ravenous" => "Ravenous",
            _ => "" // Basic or Awe enhancements do not constrain class matching
        };
    }

    /// <summary>
    /// Converts standard class slot matrices into capability-aware SlotRequirement arrays.
    /// </summary>
    public static SlotRequirement[] GetSlotRequirements(string bossContext, string[][] classSlots)
    {
        var requirements = new SlotRequirement[classSlots.Length];

        for (int i = 0; i < classSlots.Length; i++)
        {
            string[] acceptedClasses = classSlots[i];
            string primaryClass = acceptedClasses.Length > 0 ? acceptedClasses[0] : "";

            requirements[i] = new SlotRequirement
            {
                Classes = acceptedClasses,
                Enhancement = GetRequiredEnhancement(bossContext, primaryClass)
            };
        }

        return requirements;
    }

    /// <summary>
    /// Applies enhancements dynamically from JSON based on the boss context and currently equipped class.
    /// </summary>
    public void ApplyContext(string bossContext)
    {
        string className = Bot.Player?.CurrentClass?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(className))
            return;

        C.Logger($"[UltraEnhancementsv4] Applying [{bossContext}] enhancements for: {className}");
        var profile = GetProfile(bossContext, className);

        EnhancementType type = Enum.TryParse(profile.Type, true, out EnhancementType t) ? t : EnhancementType.Lucky;
        HelmSpecial hSpec = Enum.TryParse(profile.Helm, true, out HelmSpecial h) ? h : HelmSpecial.None;
        WeaponSpecial wSpec = Enum.TryParse(profile.Weapon, true, out WeaponSpecial w) ? w : WeaponSpecial.None;
        CapeSpecial cSpec = Enum.TryParse(profile.Cape, true, out CapeSpecial c) ? c : CapeSpecial.None;

        CoreAdvanced.Instance.EnhanceEquipped(
            type, 
            hSpecial: hSpec, 
            wSpecial: wSpec, 
            cSpecial: cSpec
        );
    }

    // ── Backward-Compatibility Wrappers ──────────────────────────────────────

    public void Apply() => ApplyContext("default");
    public void ApplyTyndarius() => ApplyContext("tyndarius");
    public void ApplyDage() => ApplyContext("dage");
    public void ApplyDrakath() => ApplyContext("drakath");
    public void ApplyDarkon() => ApplyContext("darkon");
    public void ApplySpeaker() => ApplyContext("speaker");
    public void ApplyGramiel() => ApplyContext("gramiel");
    public void ApplyNulgath() => ApplyContext("nulgath");
    public void ApplyAstralEmpyrean() => ApplyContext("astral empyrean");
    public void ApplyKolr() => ApplyContext("kolr");
}
