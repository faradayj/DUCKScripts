---
name: duck-custom-skillset
description: >-
  Assembles and wires user-specified custom AQW class skillsets, presets, and Forge enhancement
  profiles in CoreDUCK.cs and TestClassDUCK.cs without making assumptions or freestyling rotations.
  Validates strictly via compilation check.
---

# CoreDUCK Custom Skillset Assembler

This skill translates user-specified combat rotations, aura triggers, and enhancement profiles into clean, thread-safe C# across the `CoreDUCK.cs` framework.

---

## 1. Zero-Assumption Principle (Strict Rule)

- **NEVER freestyle, guess, or invent builds**: The user will explicitly specify the class name, enhancements (Base, Helm, Cape, Weapon, Potions), mode(s), skill numbers, engine mode (Simple, Strict, or Custom), and exact conditional rules.
- **NEVER assume archetypes or suggest unsolicited rotations**: If the user says "create a custom skillset for [Class]", do not generate arbitrary skills or guess Forge setups. Ask for their exact specification or implement exactly what they have provided.
- **NEVER invent alternate class names or acronyms**: In `GetClassPreset` and `AlternateClassNames`, map **strictly and only** the exact in-game names the user provided (e.g. `Healer`, `Healer (Rare)`, `Acolyte`). Do not invent slang, acronyms, or spaceless strings (e.g. do NOT invent `AO`, `AlphaOmega`, or `AlphaDOOMmega`).
- **Assemble, do not invent**: The skill's sole responsibility is knowing the **core architecture and exact syntax** needed to translate the user's logic into CoreDUCK without bugs, deadlocks, or compiler errors.

---

## 2. Core Architectural Translation Reference (What APIs to Call)

When translating the user's conditional requirements into C#:

| User Requirement | CoreDUCK API / C# Syntax |
|---|---|
| **Check player aura stacks** (e.g. "at 4 stacks of Temporal Rift") | `float stacks = Bot.Self.GetAura("Temporal Rift")?.Value ?? 0;`<br>`if (stacks >= 4) ...` |
| **Check enemy aura exists** (e.g. "if target has Panic") | `var debuff = Bot.Target.GetAura("Panic");`<br>`if (debuff != null) ...` |
| **Check enemy aura time remaining** (e.g. "refresh skill 4 if Cocooned < 1.0s left") | `var debuff = Bot.Target.GetAura("Cocooned");`<br>`double timeLeft = GetAuraSecondsRemaining(debuff);`<br>`if (debuff == null \|\| timeLeft < 1.0) ...` |
| **Emergency heal / Safe Skill** (e.g. "use skill 3 when HP < 60%") | On `ClassPreset`: `SurvivalSkill = 3; SurvivalHealthThreshold = 60;`<br>*(No custom engine method needed unless complex state gates are required!)* |
| **Player alive & target guard** (Header of every custom engine) | `if (!Bot.Player.Alive) { skillIndex = 0; return; }`<br>`if (!Bot.Player.HasTarget \|\| Bot.Player.Target?.HP <= 0) return;` |
| **Fire skill safely & yield 25ms tick** | `if (Bot.Skills.CanUseSkill(N) && Bot.Skills.UseSkill(N)) { FileLog(...); return; }` |
| **Base spam loop / Fallthrough** (Prevent mana deadlocks) | End custom engine with `CustomSkillEngine();` |

> **Aura Timer Helper (`CoreDUCK.cs` ~line 2192)**:
> ```csharp
> private static double GetAuraSecondsRemaining(Aura? aura)
> {
>     if (aura == null || aura.UnixTimeStamp <= 0 || aura.Duration <= 0)
>         return 0;
>     double remaining = (DateTimeOffset.FromUnixTimeMilliseconds(aura.UnixTimeStamp)
>         .AddSeconds(aura.Duration) - DateTimeOffset.UtcNow).TotalSeconds;
>     return Math.Max(0, remaining);
> }
> ```

---

## 3. The 6 Mandatory Implementation Steps

Every custom skillset touches up to 6 distinct architectural locations in `CoreDUCK.cs` (and `TestClassDUCK.cs` if multiple modes):

### Step 1: Enum Registration (`CoreDUCK.cs` ~line 6825)
Add the mode identifier to `public enum SkillEngineMode` (only needed if using a custom engine mode, not for built-in `Simple` or `Strict`).

### Step 2: Class Preset Factory Method (`CoreDUCK.cs` ~lines 236–726)
Define the public factory method using the user's exact specifications:
- `ClassName` & `AlternateClassNames`
- `Skills` array (user's builder/filler numbers)
- `SkillMode` (`SkillEngineMode.Simple`, `Strict`, or custom)
- `SurvivalSkill` & `SurvivalHealthThreshold` (if user requested safe skill)
- `BaseEnhancement`, `CapeEnhancement`, `HelmEnhancement`, `WeaponEnhancement`, `WeaponEnhancementFallbacks`
- `Tonic`, `Elixir`, `CombatPotion`
- Multi-mode support: If user requested multiple modes, accept a parameter (e.g. `bool soloMode = false`, or a mode enum) and branch the preset properties accordingly.

### Step 3: Combat Engine Logic (`CoreDUCK.cs` ~lines 1950–2500)
*(Only if the user specified conditional rotation rules requiring a custom engine):*
- Add `public void [ClassName]SkillEngine()`.
- Place dead/target guards at top.
- Implement the user's exact aura checks and skill conditions.
- Yield tick on cast (`return;`).
- Fall through to `CustomSkillEngine()` at bottom.

### Step 4: Worker Dispatch Routing (`CoreDUCK.cs` ~lines 1665–1710)
*(Only if custom engine added in Step 3):*
Register in `SkillEngineLoop()` dispatch chain:
```csharp
else if (skillEngineMode == SkillEngineMode.[Mode])
    [ClassName]SkillEngine();
```

### Step 5: Dynamic Preset Resolution (`CoreDUCK.cs` ~lines 6125–6226)
Map the class name string and aliases in `GetClassPreset(string className)`:
- **STRICT RULE**: Only map the exact names the user explicitly provided. Do NOT invent acronyms (e.g. `AO`), abbreviations, or spaceless versions (e.g. `AlphaOmega`).
```csharp
if (norm.Equals("Alpha Omega", StringComparison.OrdinalIgnoreCase)
    || norm.Equals("Alpha DOOMmega", StringComparison.OrdinalIgnoreCase))
    return AlphaOmega();
```

### Step 6: Multi-Mode Option (`TestClassDUCK.cs`)
*(Only if the user specified multiple modes for this class):*
- Nest an enum inside `public class TestClassDUCK`:
  ```csharp
  public enum [ClassName]Mode { ModeA, ModeB }
  ```
- Add an `Option<[ClassName]Mode>` to the `Options` list.
- In `ResolvePreset()`, map the selected option to the factory method.
- **Rule**: `public class TestClassDUCK` must remain the very first type in the file.

---

## 4. Critical Architectural Rules & Anti-Patterns

1. **Thread Safety for Auras**:
   - **NEVER** use LINQ over `Bot.Self.Auras` or `Bot.Target.Auras` on background threads (throws `Collection was modified`).
   - **ALWAYS** use `Bot.Self.GetAura("Name")?.Value ?? 0` or `Bot.Target.GetAura("Name")`.
2. **Aura Remaining Duration Math**:
   - `Aura` has no `SecondsRemaining` property.
   - Use `GetAuraSecondsRemaining(aura)` to calculate remaining seconds from `UnixTimeStamp` + `Duration`.
3. **No Deadlocks**:
   - Never put an unconditional `return;` inside a conditional block.
   - Always fall through to `CustomSkillEngine()` so auto-attacks and mana generation continue.
4. **Yield the 25ms Tick on Cast**:
   - After any successful `UseSkill(X)`, call `return;` to let the packet register and the GCD begin.
5. **Skua Script Compiler First-Type Rule**:
   - `TestClassDUCK` must be the very first type in `TestClassDUCK.cs`. Enums must be nested inside or declared at the bottom.
6. **Suppress Native Popups & Keep Combat Visible**:
   - In `TestClassDUCK.cs`: `public bool DontPreconfigure = true;`, `Core.AntiLag = false;`, and `Bot.Options.LagKiller = false;`.

---

## 5. Verification (Compilation Only)

1. Run the verification script:
   ```powershell
   pwsh -File C:\Users\farad\AppData\Roaming\Skua\Scripts\.agents\skills\duck-custom-skillset\scripts\verify_skillset.ps1
   ```
2. Report the compilation result to the user:
   - If exit code is 0: Confirm that the code compiles with 0 errors.
   - If exit code is non-zero: Show the compiler errors and resolve them immediately.
3. **Do not provide directions or tutorials on how to test with the dummy**. Just report the compilation status.
