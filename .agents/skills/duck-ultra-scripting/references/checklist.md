# AQW DUCK Ultra Scripting - Pre-Flight Audit Checklist

Before finalizing, running, or submitting any DUCK Ultra script, verify every item on this checklist:

---

## 1. Synchronization & Lockstep
- [ ] **No In-Combat Barrier**: Is `Sync("START_FIGHT")` absent? Ensure there are NO lockstep barriers after jumping into the boss combat room.
- [ ] **Safe-Room Barrier**: Is `Sync("FIGHT_READY")` executed strictly inside `SafeCell` (`Enter`, `Spawn`) after applying potions and prebuffs?
- [ ] **Dual-Mode Master Runner Support**: Does `Run(bool isMasterMode, int masterRoomNumber)` return `UltraRunResult` and set `masterMode = false` in a `finally` block?
- [ ] **File-Sharing Concurrency**: Does `Sync(stepName)` include descriptive `Duck.FileLog` entry and exit statements?

---

## 2. Combat Engine & Taunt Architecture
- [ ] **Non-Blocking Cyclic Taunts**: Are alternating taunts (Ultra Dage, Ultra Speaker, Ultra Tyndarius) using `Duck.RequestImmediateTaunt` or `Duck.RequestTaunt`?
- [ ] **Absolute Priority Taunt Restraint**: Is `Duck.RequestAbsolutePriorityTaunt` reserved **strictly** for single-event threshold spikes (e.g. Warden 500k HP enrage) where a GCD lock would cause an instant party wipe?
- [ ] **Skill Engine Pre-Activation**: Is `Duck.StartSkillEngine(...)` started immediately before the jump to `BossCell` so players are protected upon entrance aggro?
- [ ] **Clean Shutdown**: Is `Duck.StopSkillEngine()` called in the `finally` block of `RunFightAttempts()` and `Run()`?

---

## 3. Room Geometry & Coordinate Movement
- [ ] **Pad-Agnostic Room State**: Does `IsInBossRoom()` check **strictly** `string.Equals(Bot.Player.Cell, BossCell, StringComparison.OrdinalIgnoreCase)` without checking `Bot.Player.Pad == BossPad`?
- [ ] **Hostile Entrance Check**: If `Enter, Spawn` contains hostile trash mobs (e.g. `frozenlair`, `sevencircleswar`), is `Duck.GenericPrebuff()` omitted from `PrepareSafeRoom()`?
- [ ] **Target Acquisition Safeguard**: If fighting in a multi-room map, is targeting performed by `BossName` (string) rather than `BossMapId = 1`?

---

## 4. Drop Handling & Kill Consensus
- [ ] **AC & Drop Configuration**: Are `Bot.Options.AcceptACDrops = true` and `Bot.Options.RejectAllDrops = false` set in `ScriptMain` and `ExecuteScript`?
- [ ] **No Player Left Behind**: Does boss defeat verify kill credit using `Duck.VerifyArmyKillCredit(fightAttempt, CreditCheck, TargetArmySize, LogPrefix)`?
- [ ] **Safe Room Turn-in**: Do players jump back to `SafeCell` before calling `Duck.CompleteUltraQuest(...)` and `Sync("FINISH")`?

---

## 5. Compilation & Tooling
- [ ] Run the compilation checker:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .agents/skills/duck-custom-skillset/scripts/verify_skillset.ps1
  ```
  Target: **0 Compilation Errors**.
