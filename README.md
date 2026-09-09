# DUCKScripts - AQW Multi-Client Ultra & Army Framework

**DUCKScripts** is a distributed, high-performance multi-client automation framework for **AdventureQuest Worlds (AQW)** built on the **Skua** botting client. It coordinates 4 to 7 concurrent game clients through local filesystem lockstep signals to take down every daily, weekly, and challenge Ultra boss with 100% raid reliability and zero user intervention.

---

## Acknowledgements & Lineage

> **Credits**: Heavily inspired by and building upon foundational ideas and combat rotations from [l0newolf12/UltrasLW](https://github.com/l0newolf12/UltrasLW). DUCKScripts extends and transforms these concepts into a fully dynamic, distributed consensus architecture with automated constraint-satisfaction class assignment, pad-agnostic movement, and comprehensive kill-credit verification.

---

## Core Framework Features

1. **Zero-Configuration Gauntlet Runners**:
   - Master runners (`0AllUltrasDUCK.cs`, `0AllUltras7DUCK.cs`, `VoidDailiesDUCK.cs`) execute full sets of daily/weekly bosses sequentially without modal popups.
   - Consensus Skip: Automatically probes all accounts and only fights bosses that at least one account needs.
2. **Dynamic Multi-Client Roster Discovery**:
   - Any squad of accounts from an arbitrary pool automatically discovers each other using timestamped heartbeats without hardcoded usernames.
3. **Dynamic Greedy Auto-Assignment (Constraint Satisfaction Engine)**:
   - Probes inventories and banks across all clients for candidate classes, unbanks them on the fly, probes unlocked Forge enhancements, and solves slot assignments deterministically.
4. **Resilient Lockstep Synchronization**:
   - 4-phase synchronization (`STEP` -> `ARRIVED` -> `CONTINUE` -> `UNBLOCK`).
   - Safe-room pre-combat barrier (`Sync("FIGHT_READY")`) with immediate jump-and-engage to prevent post-jump deadlocks.
5. **Aura-Based Combat Engine & Non-Blocking Taunts**:
   - High-frequency asynchronous skill thread managing cooldowns, mana thresholds, potion re-buffing, and aura timers.
   - Non-blocking interrupts (`RequestImmediateTaunt`, `RequestTaunt`) preventing skill starvation.
6. **"No Player Left Behind" Consensus Protocol**:
   - If any player dies just before a boss is defeated and misses drop/quest credit during respawn, the entire army stays and retries together.
7. **Built-in Agent Skills (`.agents/skills/`)**:
   - `duck-ultra-scripting`: Authoring, auditing, and debugging ultra boss scripts.
   - `duck-custom-skillset`: Assembling custom class presets and Forge enhancement profiles.

---

## Repository Structure

```text
DUCKScripts/
├── UltrasDUCK/                 # Production DUCK Ultra scripts
│   ├── 0AllUltrasDUCK.cs       # Master 4-player weekly/daily gauntlet runner
│   ├── 0AllUltras7DUCK.cs      # Master 7-player daily gauntlet runner
│   ├── CoreDUCK.cs             # Core framework (CSP assignment, lockstep sync, skill engine)
│   ├── UltraDageDUCK.cs        # Ultra Dage (dynamic decay timing, alternating taunts)
│   ├── UltraSpeakerDUCK.cs     # Ultra Speaker (zone movement, truth/listen packet detection)
│   ├── UltraGramielDUCK.cs     # Ultra Gramiel (phase 1 & 2 crystal packet detection)
│   ├── Dailies/                # Ultra Ezrajal, Warden, Engineer, Tyndarius, Kala
│   ├── SevenPlayerUltras/      # IceWing, Astral Empyrean, Kathool, Lich Lord, The Beast, etc.
│   ├── VoidBosses/             # Void Dailies (Xyfrag, Nightbane, Flibbi, Nerfkitten)
│   └── Extras/                 # Repeatable army farming (ArmyPrismatasGoldFarmDUCK)
├── Tools/                      # Army utility scripts (TurretDUCK, Butlerv4DUCK)
├── BISEnhancements.cs          # Dynamic Best-in-Slot Forge enhancement manager
├── bis_enhancements.json       # Database of class Forge enhancements
├── EditSkuaConfig.py           # Script options management utility
└── .agents/                    # Specialized AI agent skills
    └── skills/
        ├── duck-ultra-scripting/ # Script authoring rules, recipes, and checklists
        └── duck-custom-skillset/ # Class preset & Forge enhancement assembler
```

---

## Installation & Setup

1. Clone or place this folder into your Skua scripts directory:
   ```bash
   git clone <REPO_URL> "DUCKScripts"
   # or
   git clone <REPO_URL> "DUCKScripts"
   ```
2. In Skua, open the Script Manager and navigate to the folder to run any individual boss or master runner.
3. Verify compilation at any time using:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .agents/skills/duck-custom-skillset/scripts/verify_skillset.ps1
   ```

---

## License & Disclaimer

This project is for educational and private server testing purposes. Use responsibly.
