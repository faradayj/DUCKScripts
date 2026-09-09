/*
name: BISEnhancements
description: Applies Best-in-Slot (BiS) enhancements to the currently equipped class based on a JSON config.
tags: custom, enhancements, bis, best-in-slot
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

public class BISEnhancements
{
    public IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots C => CoreBots.Instance;
    private CoreAdvanced Adv => CoreAdvanced.Instance;

    private static Dictionary<string, BISEnhancementProfile>? _configCache;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Skua", "Scripts", "Customv2", "bis_enhancements.json"
    );

    public void ScriptMain(IScriptInterface bot)
    {
        C.SetOptions();
        Apply();
        C.SetOptions(false);
    }

    private static Dictionary<string, BISEnhancementProfile> LoadConfig()
    {
        if (_configCache != null)
            return _configCache;

        if (!File.Exists(ConfigPath))
        {
            CoreBots.Instance.Logger($"[BISEnhancements] Config missing at {ConfigPath}", "Warning");
            return _configCache = new(StringComparer.OrdinalIgnoreCase);
        }

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using FileStream fs = new(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader sr = new(fs);
                string json = sr.ReadToEnd();

                var temp = JsonConvert.DeserializeObject<Dictionary<string, BISEnhancementProfile>>(json);
                if (temp != null)
                    _configCache = new Dictionary<string, BISEnhancementProfile>(temp, StringComparer.OrdinalIgnoreCase);
                else
                    _configCache = new Dictionary<string, BISEnhancementProfile>(StringComparer.OrdinalIgnoreCase);

                return _configCache;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                CoreBots.Instance.Logger($"[BISEnhancements] Failed to load JSON: {ex.Message}", "Error");
                break;
            }
        }

        return _configCache = new(StringComparer.OrdinalIgnoreCase);
    }

    public static BISEnhancementProfile GetProfile(string className)
    {
        var config = LoadConfig();
        string cls = className.Trim();

        if (config.TryGetValue(cls, out var profile))
            return profile;

        return new BISEnhancementProfile(); // Default fallback
    }

    public void SmartEnhance(string className)
    {
        if (string.IsNullOrEmpty(className))
            return;

        if (!C.CheckInventory(className))
        {
            C.Logger($"[BISEnhancements] Class {className} not found in inventory!");
            return;
        }

        if (Bot.Player.CurrentClass?.Name.ToLower().Trim() != className.ToLower().Trim())
            C.Equip(className);

        Apply();
    }

    public void Apply()
    {
        string className = Bot.Player?.CurrentClass?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(className))
        {
            C.Logger("[BISEnhancements] No class equipped!");
            return;
        }

        C.Logger($"[BISEnhancements] Checking BiS enhancements for: {className}");
        var profile = GetProfile(className);

        if (profile.Type == "Lucky" && profile.Helm == "None" && profile.Weapon == "None" && profile.Cape == "None")
        {
            C.Logger($"[BISEnhancements] No specific BiS profile found for {className}. Applying default Lucky.", "Warning");
        }

        EnhancementType type = Enum.TryParse(profile.Type, true, out EnhancementType t) ? t : EnhancementType.Lucky;
        HelmSpecial hSpec = Enum.TryParse(profile.Helm, true, out HelmSpecial h) ? h : HelmSpecial.None;
        WeaponSpecial wSpec = Enum.TryParse(profile.Weapon, true, out WeaponSpecial w) ? w : WeaponSpecial.None;
        CapeSpecial cSpec = Enum.TryParse(profile.Cape, true, out CapeSpecial c) ? c : CapeSpecial.None;

        Adv.EnhanceEquipped(
            type, 
            cSpecial: cSpec,
            hSpecial: hSpec, 
            wSpecial: wSpec
        );
    }
}

public class BISEnhancementProfile
{
    public string Type { get; set; } = "Lucky";
    public string Helm { get; set; } = "None";
    public string Weapon { get; set; } = "None";
    public string Cape { get; set; } = "None";
}
