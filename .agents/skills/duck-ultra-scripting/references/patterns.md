# AQW DUCK Ultra Scripting - Architectural Code Patterns

This document details production-tested code patterns for authoring resilient multi-client Ultra scripts in the DUCK framework.

---

## Pattern 1: Synchronized Jump-and-Engage Opener (Standard)

Use this pattern for 90% of Ultra bosses (`ultraezrajal`, `ultrawarden`, `ultraengineer`, `championdrakath`, `ultradrago`, `ultranulgath`, `ultradage`, `ultradarkon`, `ultragramiel`, `ultraspeaker`).

```csharp
private bool RunFightAttempts(ClassPreset preset)
{
    for (int fightAttempt = 1; fightAttempt <= MaxFightAttempts && !Bot.ShouldExit; fightAttempt++)
    {
        Duck.JoinRoom(MapName, privateRoomNumber, SafeCell, SafePad);
        Duck.FileLog($"{playerAlias} joined {MapName}-{privateRoomNumber} for attempt {fightAttempt}.", LogPrefix);

        // 1. Prepare Safe Room: Apply potions and cast group prebuffs
        if (!PrepareSafeRoom(preset))
            return false;

        FightResult result;
        try
        {
            // 2. The ONLY pre-combat barrier: Synchronize all players in the safe room
            if (!Sync("FIGHT_READY"))
                return false;

            // 3. Start skill engine BEFORE jump so player is never defenseless
            Duck.StartSkillEngine(
                preset.Skills,
                playerAlias,
                isTaunter,
                LogPrefix,
                preset.SkillMode,
                maintainedPotion: preset.CombatPotion
            );

            // 4. Jump simultaneously to combat cell
            Core.Jump(BossCell, BossPad);
            Duck.FileLog($"{playerAlias} jumped to {BossCell} for attempt {fightAttempt}.", LogPrefix);

            // 5. CRITICAL: NEVER call Sync("START_FIGHT") here! Engage immediately.
            result = Fight(preset, fightAttempt);
        }
        finally
        {
            Duck.StopSkillEngine();
        }

        // 6. Verification and retry routing
        if (result == FightResult.Defeated)
        {
            Duck.FileLog($"{playerAlias} confirmed {BossName} defeated on attempt {fightAttempt}.", LogPrefix);
            bool CreditCheck() =>
                Bot.Quests.CanComplete(UltraQuestId) || Bot.Quests.IsDailyComplete(UltraQuestId);

            if (Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, TargetArmySize, LogPrefix))
                return true;

            Core.Logger($"{LogPrefix} One or more players missed kill credit on attempt {fightAttempt}. Retrying fight with entire army...");
            result = FightResult.Reset;
        }

        if (result != FightResult.Reset || !HandleFightReset(fightAttempt))
            return false;

        if (fightAttempt >= MaxFightAttempts)
        {
            runResult = UltraRunResult.AttemptsExhausted;
            return false;
        }
    }

    return false;
}
```

---

## Pattern 2: ArchPaladin Solo Opener with Army Signal Handshake

Use this pattern when the boss inflicts lethal entrance burst that 1-shots squishy classes before buffs ramp up (`ultratyndarius`, `frozenlair` [Legion Lich Lord], `sevencircleswar` [The Beast]).

```csharp
// Inside RunFightAttempts:
if (!Sync("FIGHT_READY"))
    return false;

bool isAP = string.Equals(preset.ClassName, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
int apPlayerNumber = currentAssignResult?.GetPlayerNumberForClass("ArchPaladin") ?? -1;

if (isAP)
{
    // ArchPaladin starts engine, enters combat solo, lands Righteous Seal (-90% boss damage)
    Duck.StartSkillEngine(preset.Skills, playerAlias, isTaunter, LogPrefix, preset.SkillMode);
    if (!PrepareRighteousSeal(fightAttempt) || !SendRighteousSealSignal(fightAttempt))
        return false;
}
else
{
    // Teammates wait safely in Enter, Spawn until AP signals that seal is active
    if (apPlayerNumber > 0)
        WaitForRighteousSealSignal(fightAttempt, apPlayerNumber);

    Duck.StartSkillEngine(preset.Skills, playerAlias, isTaunter, LogPrefix, preset.SkillMode);
    Core.Jump(BossCell, BossPad);
}

// All enter combat immediately without any post-jump lockstep barrier!
result = Fight(preset, fightAttempt);
```

Helper implementations:
```csharp
private bool PrepareRighteousSeal(int fightAttempt)
{
    while (!Bot.ShouldExit)
    {
        if (!Bot.Player.Alive)
        {
            while (!Bot.ShouldExit && !Bot.Player.Alive)
                Bot.Sleep(RespawnPollDelay);
            if (Bot.ShouldExit) return false;
        }

        if (!IsInBossRoom())
            Core.Jump(BossCell, BossPad);

        if (!Duck.IsMonsterAlive(BossName))
            return true;

        Duck.MaintainTarget(BossName);

        if (Bot.Target.GetAura("Righteous Seal") != null)
            return true;

        if (Bot.Skills.CanUseSkill(3))
            Bot.Skills.UseSkill(3);

        Bot.Sleep(FightPollDelay);
    }
    return !Bot.ShouldExit;
}

private bool SendRighteousSealSignal(int fightAttempt) =>
    Duck.SendArmySignal($"SEAL_READY_{fightAttempt}");

private bool WaitForRighteousSealSignal(int fightAttempt, int apPlayerNumber)
{
    string signal = $"SEAL_READY_{fightAttempt}";
    DateTime timeout = DateTime.UtcNow.AddSeconds(15);
    while (!Bot.ShouldExit && DateTime.UtcNow < timeout)
    {
        if (Duck.HasArmySignal(signal, apPlayerNumber))
            return true;
        Bot.Sleep(FightPollDelay);
    }
    return !Bot.ShouldExit;
}
```

---

## Pattern 3: Cyclic Alternating Taunts (e.g. Ultra Dage, Ultra Speaker, Tyndarius)

**RULE**: Always use non-blocking `Duck.RequestImmediateTaunt(mapId)` or `Duck.RequestTaunt(mapId)`. **NEVER** use `RequestAbsolutePriorityTaunt`.

```csharp
private void HandleAlternatingTaunt(int bossMapId, int playerNumber, int partnerPlayerNumber)
{
    if (!isTaunter || !Bot.Player.Alive)
        return;

    var focusAura = Bot.Target.GetAura("Focus");
    double focusRemaining = GetAuraSecondsRemaining(focusAura);

    // If boss is not taunted, or partner's Focus is expiring (< 1.5s remaining), request taunt!
    if (focusAura == null || focusRemaining < 1.5)
    {
        // RequestImmediateTaunt checks CanUseSkill(5) without freezing the engine
        Duck.RequestImmediateTaunt(bossMapId);
    }
}
```

---

## Pattern 4: Single-Event Threshold Spike Taunt (e.g. Ultra Warden 500k HP)

Use `Duck.RequestAbsolutePriorityTaunt(mapId)` **strictly and only** when an upcoming mechanic will 1-shot the party unless taunted, and no cyclic rotation is in progress.

```csharp
int bossHP = Duck.GetMonsterHP(BossMapId);
if (isTaunter && !enrageTaunted && bossHP is > 0 and <= 500000)
{
    // Halts other skills to guarantee Skill 5 is not locked behind global cooldown (GCD)
    Duck.RequestAbsolutePriorityTaunt(BossMapId);
    enrageTaunted = true;
}
```

---

## Pattern 5: Coordinate Movement & Pad-Agnostic Room State

In combat rooms with movement mechanics (e.g. Ultra Speaker zones, Ultra Tyndarius orbs, Ultra Gramiel crystals):
- **ALWAYS** check `Cell` only.
- **NEVER** check `Bot.Player.Pad == BossPad`.

```csharp
// Standard pad-agnostic check
private bool IsInBossRoom() =>
    string.Equals(Bot.Player.Cell, BossCell, StringComparison.OrdinalIgnoreCase);

// Moving to a safe zone coordinate without breaking room checks
private void MoveToSafeZone(int targetX, int targetY)
{
    if (!IsInBossRoom())
    {
        Core.Jump(BossCell, BossPad);
        return;
    }

    // Direct Flash coordinate walk
    Bot.Player.WalkTo(targetX, targetY);
}
```

---

## Pattern 6: Universal Thread-Safe Lockstep Barrier with Logging

```csharp
private bool Sync(string stepName)
{
    Duck.FileLog($"{playerAlias} syncing on {stepName}...", LogPrefix);
    bool success = Duck.SyncArmy(stepName);
    Duck.FileLog($"{playerAlias} sync {stepName} => {(success ? "SUCCESS" : "TIMEOUT/FAILED")}", LogPrefix);
    return success;
}
```
