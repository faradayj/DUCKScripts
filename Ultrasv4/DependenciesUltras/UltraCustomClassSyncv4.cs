/*
name: null
description: null
tags: null
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/Customv2/Ultrasv4/DependenciesUltras/CoreUltrav4.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

public class SlotRequirement
{
    public string[] Classes { get; set; } = Array.Empty<string>();
    public string Enhancement { get; set; } = string.Empty;
}

public class PlayerCapabilities
{
    public string Name { get; set; } = string.Empty;
    public List<string> Classes { get; set; } = new();
    public List<string> Enhancements { get; set; } = new();
}

public class UltraCustomClassSyncv4
{
    public static string CustomClassSync(
        dynamic ultra,
        IScriptInterface bot,
        SlotRequirement[] slotRequirements,
        int armySize,
        string syncFilePath = "class_assign.sync",
        bool allowDuplicates = false,
        Dictionary<string, string>? preferredUsernameAssignments = null
    )
    {
        if (slotRequirements == null || slotRequirements.Length == 0 || armySize < 1)
            return string.Empty;

        string syncFile = ultra.ResolveSyncPath(syncFilePath);

        // Clear stale file (>15 min old)
        try
        {
            if (
                File.Exists(syncFile)
                && (DateTime.UtcNow - File.GetLastWriteTimeUtc(syncFile)).TotalMinutes > 15
            )
                File.WriteAllText(syncFile, "");
        }
        catch { }

        // Collect every unique class name across all slots
        HashSet<string> allNeeded = new(StringComparer.OrdinalIgnoreCase);
        foreach (SlotRequirement slot in slotRequirements)
            foreach (string cls in slot.Classes)
                allNeeded.Add(cls);

        // Check which of those this client owns (inventory + bank)
        List<string> myClasses = new();
        foreach (string cls in allNeeded)
        {
            var item = bot.Inventory.Items.Concat(bot.Bank.Items).FirstOrDefault(x => x.Name.Equals(cls, StringComparison.OrdinalIgnoreCase) && x.Category == ItemCategory.Class);
            if (item != null)
            {
                myClasses.Add(cls);
                if (bot.Bank.Contains(item.ID))
                    CoreBots.Instance.Unbank(item.ID);
            }
        }
        
        CoreAdvanced Adv = new CoreAdvanced();
        List<string> myEnhancements = new();
        if (Adv.uElysium()) myEnhancements.Add("Elysium");
        if (Adv.uArcanasConcerto()) myEnhancements.Add("ArcanasConcerto");
        if (Adv.uValiance()) myEnhancements.Add("Valiance");
        if (Adv.uSmite()) myEnhancements.Add("Smite");
        if (Adv.uAcheron()) myEnhancements.Add("Acheron");
        if (Adv.uDauntless()) myEnhancements.Add("Dauntless");
        if (Adv.uPraxis()) myEnhancements.Add("Praxis");
        if (Adv.uRavenous()) myEnhancements.Add("Ravenous");
        if (Adv.uLacerate()) myEnhancements.Add("Lacerate");

        string username = bot.Player.Username;
        string payload = $"READY|Classes={string.Join(",", myClasses)}|Enh={string.Join(",", myEnhancements)}";
        ultra.UpdateEntry(syncFile, username, payload);
        bot.Log(
            "[CustomClassSync] Account owns: " + (myClasses.Count > 0 ? string.Join(",", myClasses) : "NONE of the needed classes")
        );

        // Wait for all members to register (with 5 min timeout)
        const int staleThreshold = 600; // 10 minutes
        const int registrationTimeoutSec = 300;
        DateTime waitStart = DateTime.UtcNow;
        int lastCount = -1;

        while (!bot.ShouldExit)
        {
            if ((DateTime.UtcNow - waitStart).TotalSeconds > registrationTimeoutSec)
            {
                bot.Log($"[CustomClassSync] Timed out after {registrationTimeoutSec}s waiting for {armySize} clients (got {lastCount}). Aborting.");
                bot.StopSync();
                return string.Empty;
            }

            string[] lines = ultra.ReadLines(syncFile);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int validCount = 0;

            foreach (string line in lines)
            {
                string[] parts = line.Split(':');
                if (parts.Length < 3)
                    continue;
                if (!parts[1].StartsWith("READY|", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!long.TryParse(parts[^1], out long ts))
                    continue;
                if (now - ts <= staleThreshold)
                    validCount++;
            }

            if (validCount != lastCount)
            {
                lastCount = validCount;
                bot.Log($"[CustomClassSync] Registered: {validCount}/{armySize}");
            }

            if (validCount >= armySize)
                break;

            // Re-poke to keep entry fresh
            ultra.UpdateEntry(syncFile, username, payload);
            bot.Sleep(500);
        }

        if (bot.ShouldExit)
            return string.Empty;

        // Small buffer to ensure file system has flushed all writes
        bot.Sleep(500);

        // Build sorted account list once for friendly log labels
        string[] _rawLines = ultra.ReadLines(syncFile);
        List<string> _sortedAccounts = _rawLines
            .Select(l => l.Split(':')[0])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string AccountLabel(string user)
        {
            int idx = _sortedAccounts.FindIndex(a => string.Equals(a, user, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 ? $"Account-{idx + 1}" : user;
        }

        List<PlayerCapabilities> players = new();
        {
            string[] lines = ultra.ReadLines(syncFile);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (string line in lines)
            {
                string[] parts = line.Split(':');
                if (parts.Length < 3)
                    continue;

                string playerName = parts[0];
                string payloadPart = parts[1];

                if (!long.TryParse(parts[^1], out long ts))
                    continue;
                if (now - ts > staleThreshold)
                    continue;

                var caps = new PlayerCapabilities { Name = playerName };

                if (payloadPart.StartsWith("READY|", StringComparison.OrdinalIgnoreCase))
                    payloadPart = payloadPart.Substring(6); // Remove "READY|" prefix

                string[] payloadSections = payloadPart.Split('|');
                foreach (string section in payloadSections)
                {
                    if (section.StartsWith("Classes=", StringComparison.OrdinalIgnoreCase))
                        caps.Classes = section.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    else if (section.StartsWith("Enh=", StringComparison.OrdinalIgnoreCase))
                        caps.Enhancements = section.Substring(4).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    else
                        caps.Classes = section.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
                players.Add(caps);
            }
        }

        bot.Log($"[CustomClassSync] {players.Count} player(s) registered. Assigning classes...");

        // Pre-count how many times each class appears across all slot definitions
        // This determines the max allowed duplicates for each class
        Dictionary<string, int> classMaxCount = new(StringComparer.OrdinalIgnoreCase);
        if (allowDuplicates)
        {
            foreach (SlotRequirement slot in slotRequirements)
            {
                foreach (string cls in slot.Classes)
                {
                    if (!classMaxCount.ContainsKey(cls))
                        classMaxCount[cls] = 0;
                    classMaxCount[cls]++;
                }
            }
        }

        Dictionary<string, string> assignments = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> assignedPlayers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> classUsedCount = new(StringComparer.OrdinalIgnoreCase);
        HashSet<int> filledSlots = new();

        if (preferredUsernameAssignments != null && preferredUsernameAssignments.Count > 0)
        {
            foreach (var kvp in preferredUsernameAssignments)
            {
                string preferredUser = kvp.Key.Trim();
                string preferredClass = kvp.Value.Trim();

                if (string.IsNullOrEmpty(preferredUser) || string.IsNullOrEmpty(preferredClass))
                    continue;

                PlayerCapabilities? userCaps = players.FirstOrDefault(p => p.Name.Equals(preferredUser, StringComparison.OrdinalIgnoreCase));
                if (userCaps == null)
                {
                    bot.Log($"[CustomClassSync] Preferred assignment requested for '{preferredUser}' but that user is not registered.");
                    continue;
                }

                if (!userCaps.Classes.Any(c => c.Equals(preferredClass, StringComparison.OrdinalIgnoreCase)))
                {
                    bot.Log($"[CustomClassSync] Preferred assignment for '{preferredUser}' cannot be honored because they do not own '{preferredClass}'.");
                    continue;
                }

                for (int s = 0; s < slotRequirements.Length; s++)
                {
                    if (filledSlots.Contains(s))
                        continue;

                    if (!slotRequirements[s].Classes.Any(c => c.Equals(preferredClass, StringComparison.OrdinalIgnoreCase)))
                        continue;
                        
                    if (!string.IsNullOrEmpty(slotRequirements[s].Enhancement) && !userCaps.Enhancements.Contains(slotRequirements[s].Enhancement, StringComparer.OrdinalIgnoreCase))
                        continue;

                    int used = classUsedCount.GetValueOrDefault(preferredClass, 0);
                    if (!allowDuplicates && used >= 1)
                        continue;

                    if (allowDuplicates)
                    {
                        int max = classMaxCount.GetValueOrDefault(preferredClass, 0);
                        if (used >= max)
                            continue;
                    }

                    assignments[preferredUser] = preferredClass;
                    assignedPlayers.Add(preferredUser);
                    classUsedCount[preferredClass] = used + 1;
                    filledSlots.Add(s);
                    bot.Log($"[CustomClassSync] Preferred slot {s} > {AccountLabel(preferredUser)} ({preferredClass})");
                    break;
                }
            }
        }

        // Deterministic greedy assignment
        List<string> sortedPlayers = players
            .Select(p => p.Name)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
            
        // Order slots to evaluate the most critical ones first:
        // 1. Slots requiring an enhancement take absolute priority over slots that don't.
        // 2. Slots with the fewest eligible candidates are evaluated first (prevents rare classes from going unfilled).
        // 3. Fallback to alphabetical Enhancement name for tie-breaking.
        // 4. Stable sort preserves original slot order for any remaining ties.
        List<int> orderedSlots = Enumerable.Range(0, slotRequirements.Length)
            .OrderByDescending(s => !string.IsNullOrEmpty(slotRequirements[s].Enhancement))
            .ThenBy(s => 
            {
                return players.Count(p => 
                    slotRequirements[s].Classes.Any(c => p.Classes.Any(pc => pc.Equals(c, StringComparison.OrdinalIgnoreCase))) &&
                    (string.IsNullOrEmpty(slotRequirements[s].Enhancement) || p.Enhancements.Contains(slotRequirements[s].Enhancement, StringComparer.OrdinalIgnoreCase))
                );
            })
            .ThenBy(s => slotRequirements[s].Enhancement ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (int s in orderedSlots)
        {
            if (filledSlots.Contains(s))
                continue;

            bool filled = false;

            // Try each accepted class in preference order
            foreach (string acceptedClass in slotRequirements[s].Classes)
            {
                // Check if this class can still be used
                int used = classUsedCount.GetValueOrDefault(acceptedClass, 0);
                if (allowDuplicates)
                {
                    int max = classMaxCount.GetValueOrDefault(acceptedClass, 0);
                    if (used >= max)
                        continue;
                }
                else
                {
                    // No duplicates: each class used at most once
                    if (used >= 1)
                        continue;
                }

                List<string> candidates = sortedPlayers
                    .Where(pName =>
                    {
                        if (assignedPlayers.Contains(pName)) return false;
                        var pCaps = players.First(p => p.Name == pName);
                        if (!pCaps.Classes.Any(c => c.Equals(acceptedClass, StringComparison.OrdinalIgnoreCase))) return false;
                        if (!string.IsNullOrEmpty(slotRequirements[s].Enhancement) && !pCaps.Enhancements.Contains(slotRequirements[s].Enhancement, StringComparer.OrdinalIgnoreCase)) return false;
                        return true;
                    })
                    .ToList();

                if (candidates.Count == 0)
                    continue;

                // Most-constrained candidate first (fewest open slots it can fill),
                // tiebreak alphabetical.
                string best = candidates
                    .OrderBy(pName =>
                    {
                        int canFill = 0;
                        var pCaps = players.First(p => p.Name == pName);
                        for (int s2 = 0; s2 < slotRequirements.Length; s2++)
                        {
                            if (filledSlots.Contains(s2))
                                continue;
                            
                            bool matchesEnhancement = string.IsNullOrEmpty(slotRequirements[s2].Enhancement) || pCaps.Enhancements.Contains(slotRequirements[s2].Enhancement, StringComparer.OrdinalIgnoreCase);
                            if (matchesEnhancement && slotRequirements[s2].Classes.Any(c => pCaps.Classes.Any(pc => pc.Equals(c, StringComparison.OrdinalIgnoreCase))))
                                canFill++;
                        }
                        return canFill;
                    })
                    .ThenBy(pName => pName, StringComparer.OrdinalIgnoreCase)
                    .First();

                assignments[best] = acceptedClass;
                assignedPlayers.Add(best);
                classUsedCount[acceptedClass] = used + 1;
                filledSlots.Add(s);
                bot.Log($"[CustomClassSync] Slot {s} > {AccountLabel(best)} ({acceptedClass})");
                filled = true;
                break;
            }

            if (!filled)
                bot.Log($"[CustomClassSync] WARNING: No candidate for slot {s} ({string.Join("/", slotRequirements[s].Classes)})!");
        }

        // Find this client's assignment
        if (!assignments.TryGetValue(username, out string? myClass) || string.IsNullOrEmpty(myClass))
        {
            CoreBots.Instance.Logger($"[CustomClassSync] {AccountLabel(username)} was not assigned any class! Check that all of your accounts own the required classes and enhancements.", "CustomClassSync", true, true);
            bot.StopSync();
            return string.Empty;
        }

        bot.Log($"[CustomClassSync] - Equipping: {myClass}");
        var classItem = bot.Inventory.Items.Concat(bot.Bank.Items).FirstOrDefault(x => x.Name.Equals(myClass, StringComparison.OrdinalIgnoreCase) && x.Category == ItemCategory.Class);
        if (classItem != null)
            CoreBots.Instance.Equip(classItem.ID);
        else
            CoreBots.Instance.Equip(myClass);
        bot.Sleep(1000);

        // Clear sync file for next run
        try { File.WriteAllText(syncFile, ""); } catch { }

        return myClass;
    }

    public static string CustomClassSync(
        dynamic ultra,
        IScriptInterface bot,
        string[] classes,
        int armySize,
        string syncFilePath = "class_assign.sync",
        bool allowDuplicates = false
    )
    {
        SlotRequirement[] wrapped = new SlotRequirement[classes.Length];
        for (int i = 0; i < classes.Length; i++)
            wrapped[i] = new SlotRequirement { Classes = new[] { classes[i] } };
        return CustomClassSync(ultra, bot, wrapped, armySize, syncFilePath, allowDuplicates);
    }
}
