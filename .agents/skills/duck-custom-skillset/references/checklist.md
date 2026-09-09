# CoreDUCK Custom Skillset Implementation Checklist

When implementing a custom class skillset, verify all 6 mandatory architectural steps before running compilation:

- [ ] **Step 1: Enum Registration (`CoreDUCK.cs` ~line 6825)**
  - Added new identifier to `public enum SkillEngineMode`.
  - Used PascalCase naming matching the class or playstyle (e.g. `QuantumChronomancer`, `ArachnomancerSolo`).

- [ ] **Step 2: Class Preset Factory Method (`CoreDUCK.cs` ~lines 236–726)**
  - Defined `public ClassPreset [ClassName](...)` in the Class Presets region.
  - Specified `ClassName` and `AlternateClassNames`.
  - Configured `Skills` array (builder/filler loop, e.g. `{ 1, 2 }` or `{ 1, 4, 2 }`).
  - Set `SkillMode = SkillEngineMode.[Mode]`.
  - Configured Safe Skill: `SurvivalSkill` (1..4 or 0) and `SurvivalHealthThreshold` (1..100% or 0).
  - Configured full Forge setup: `BaseEnhancement`, `CapeEnhancement`, `HelmEnhancement`, `WeaponEnhancement`.
  - Configured `WeaponEnhancementFallbacks` (e.g. `{ WeaponSpecial.Valiance, WeaponSpecial.Health_Vamp }`).
  - Configured consumables: `Tonic`, `Elixir`, and `CombatPotion`.

- [ ] **Step 3: Combat Engine Logic (`CoreDUCK.cs` ~lines 1950–2500)**
  - Defined `public void [ClassName]SkillEngine()`.
  - Added dead/target safety guards:
    ```csharp
    if (!Bot.Player.Alive) { skillIndex = 0; return; }
    if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0) return;
    ```
  - Used thread-safe aura lookups:
    - Player: `Bot.Self.GetAura("Aura Name")?.Value ?? 0`
    - Target: `Bot.Target.GetAura("Debuff Name")`
    - Aura time remaining: `GetAuraSecondsRemaining(aura)`
  - Sequenced skills to prevent GCD stealing (Buffs -> Setup -> Nuke).
  - After casting a skill, immediately returned (`return;`) to yield the 25ms tick.
  - Ended with clean fallthrough to `CustomSkillEngine()` to avoid deadlocking mana/auto-attacks.

- [ ] **Step 4: Worker Dispatch Routing (`CoreDUCK.cs` ~lines 1665–1710)**
  - Registered `else if (skillEngineMode == SkillEngineMode.[Mode]) [ClassName]SkillEngine();` inside `SkillEngineLoop()`.

- [ ] **Step 5: Dynamic Preset Resolution (`CoreDUCK.cs` ~lines 6125–6226)**
  - Registered string mapping in `GetClassPreset(string className)` using **only** the exact in-game names the user provided:
    ```csharp
    if (norm.Equals("Exact Name 1", StringComparison.OrdinalIgnoreCase)
        || norm.Equals("Exact Name 2", StringComparison.OrdinalIgnoreCase))
        return [ClassName]();
    ```
  - Verified that NO invented acronyms (e.g. `AO`), spaceless names, or assumed aliases were added.

- [ ] **Step 6: Testing & Multi-Mode Option (`TestClassDUCK.cs`)**
  - If class has multiple modes (e.g. Solo vs Default):
    - Added nested enum inside `public class TestClassDUCK`.
    - Added `Option<[ModeEnum]>` in `Options` list.
    - Handled in `ResolvePreset(string currentClass)`.
  - Verified `public class TestClassDUCK` remains the FIRST type in the file.
  - Verified `public bool DontPreconfigure = true;` and `Core.AntiLag = false;`.

- [ ] **Verification**:
  - Ran `verify_skillset.ps1` or `SkuaCompileTester`.
  - Verified **0 errors, 0 warnings**.
