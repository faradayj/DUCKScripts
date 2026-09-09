# Skua Army Orchestration Framework

This document outlines the architectural framework and design patterns required to build fully automated, multi-account (army) scripts in Skua. 

This framework is highly adaptable. It powers both **strict 4-man coordinated raids** (like Ultra Speaker, which requires strict role assignments and asynchronous taunt loops) and **loose 7-man item-farming gauntlets** (like Void Dailies, which requires swarm logic and generic class equipping).

---

## 1. The Orchestration Pattern (Assess, Route, Execute)

Regardless of the army size or the boss being fought, all scripts built on this framework follow a core lifecycle: **Assess**, **Route**, and **Execute**.

### Phase 1: Initial Assessment (The Unified Queue)
When running large gauntlets of bosses (like `DoAllUltrasv4` or `VoidDailiesv4`), navigating to a boss room just to check if a drop is needed is highly inefficient. Instead, we use a **Unified Upfront Ballot**.

1. **Staging**: The entire army joins `Whitemap`.
2. **Evaluation**: Each bot evaluates its own inventory or quest list for *every* boss in the sequence simultaneously using a `Func<string, bool> isBossComplete` delegate.
3. **Payload Construction**: They write a payload string to a shared `.sync` file (e.g., `Xyfrag=true,Flibbi=false,NightBane=true`).
4. **Compilation**: `UnifiedQueuev4.GetSharedBossQueue(..., armySize)` waits until all bots have submitted their ballots. It then parses every payload. If *any* player in the army needs a boss, it is added to a single, authoritative master queue.

### Phase 2: Dynamic Routing (The Orchestrator)
Once the master queue is compiled, the main script acts as an **Orchestrator**. Its only job is to read the queue and dynamically skip to the required flags. 

To the Orchestrator, it does not matter how the target is executed. The target flag can trigger a completely standalone script (e.g., `new UltraEzrajalv4().RunBoss()`) or it can trigger an internal method (e.g., `FarmHydraChallenge(armySize)`). Both are treated identically as modular execution blocks.

### Phase 3: Execution & Completion (The Combat Sandbox)
Once routed to a specific boss block, the army enters a localized sandbox. 

1. **Class Syncing**: The script determines what classes are needed. For strict Ultras, `UltraCustomClassSyncv4` evaluates complex Forge requirements and assigns rigid roles. For generic swarms, it may bypass this to equip a standard composition or a solo class like `Yami no Ronin`.
2. **Assembly Checkpoints**: Entering a map requires precise timing. `UltraWaitForArmyv4.Instance.NewWaitForArmy` blocks execution until all `.sync` file entries are present, ensuring all accounts hop to the boss map simultaneously to prevent early deaths.
3. **The Combat Loop**: The bots run a persistent `while (!Bot.ShouldExit)` loop. Rather than dropping out as soon as they locally meet a goal (which causes race conditions if other players are still fighting), they use a **Synchronized Consensus Loop**. By evaluating `Ultra.CheckArmyProgressBool`, players who finish early will continue fighting and helping the army until every single account confirms they have reached the required condition. Once the entire army achieves consensus, they break the loop together and return to the Orchestrator.

---

## 2. Cross-Account Syncing Methodology

Because each client runs in an isolated sandbox, they must communicate externally to progress as an army. This is accomplished using flat `.sync` files written to the `AppData/Roaming/Skua/Options` directory.

### The `CoreUltrav4` Engine
`CoreUltrav4` manages all file I/O operations for these state files. 
- **`UpdateEntry(path, key, value)`**: A bot writes its unique identifier (key) and a payload (value) into a text file.
- **`ReadLines(path)`**: A bot reads the state of all other clients to make collective decisions.
- **`ClearSyncFile(path)`**: Wipes the file if the data is stale (older than 10 minutes) or at the start of a sequence.

### Typical Sync Use Cases
- **Class Assignment**: (`ultra_bossname_class-v4.sync`) Bots write the Forge enhancements they own. The script evaluates the file and determines mathematically who should play which role.
- **Lobby Checkpoints**: (`boss_wait_v4.sync`) Bots write a `1` when they enter a staging map. Execution pauses until the file contains `armySize` lines.
- **Fight Clocks**: (`FightTime.sync`) The primary Taunter writes the Unix timestamp of when the boss fight started. Secondary Taunters read this clock to offset their taunt loops by precisely `X` seconds.

---

## 3. Extending the Sandbox (Mechanics and Taunters)

For complex Ultras (like Warden or Speaker), the localized combat sandbox is extended to handle strict mechanics.

### Configuring Consumables
During initialization (before entering the map), the script identifies if an account is a Taunter. If so, it reserves their 3rd potion slot for a Scroll of Enrage.

```csharp
    bool skipThird = IsTaunter();
    Pots.EnsureRecommendedPotions(skipThird: skipThird);
    
    if (skipThird)
    {
        Scrolls.GetScrollOfEnrage();
        Engine.EquipEnrage();
    }
```

### Launching Asynchronous Listeners
Because Event Listeners run on the main thread, blocking them with `Bot.Sleep()` will freeze the client. When a mechanic triggers (via a Chat Packet or a Flash call), it spawns a background Task using `UltraAsyncv4.StartTauntLoop` to handle the button pressing.

One account acts as the primary (setting the synced fight clock), and the other acts as the secondary (reading the clock and offsetting their taunts).

```csharp
    string fightTimeSyncPath = Ultra.ResolveSyncPath("UltraWardenFightTime.sync");

    if (_role == "Taunter1")
    {
        fightStartTime = UltraAsyncv4.SetFightTime(C, fightTimeSyncPath);
        UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 0, 2, cancellationToken: _tauntCts.Token);
    }
    else if (_role == "Taunter2")
    {
        fightStartTime = UltraAsyncv4.GetFightTime(Ultra, C, fightTimeSyncPath);
        UltraAsyncv4.StartTauntLoop(Bot, C, Engine, fightStartTime, 1, 2, cancellationToken: _tauntCts.Token);
    }
```

---

## 4. Summary of Dependencies

| Dependency | Responsibility |
|------------|----------------|
| `CoreBots.Instance` | High-level Skua API wrappers (Join, Options, Banking). |
| `CoreEnginev4.Instance` | Automated skill rotation, cell hopping, and aura tracking. |
| `CoreUltrav4` | Core `.sync` file writing/reading and persistent loops. |
| `UnifiedQueuev4` | Agnostic evaluation engine that compiles a unified army gauntlet sequence. |
| `UltraCustomClassSyncv4` | Army slot assignment utilizing `SlotRequirement` JSON profiles. |
| `UltraEnhancementsv4` | Gear enhancement injection mapped to `ultras_enhancements.json`. |
| `UltraPotionsv4` | Buys and uses potions dynamically based on the boss context. |
| `GetScrollsv4` | Ensures Taunters have a stock of `Scroll of Enrage` before fights. |
| `UltraWaitForArmyv4` | Synchronization checkpoints for map hopping and combat initiation. |
