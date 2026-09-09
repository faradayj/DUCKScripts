# CoreDUCK Architectural Syntax Reference

This reference documents the exact C# function calls and structural syntax used in `CoreDUCK.cs` and `TestClassDUCK.cs`.
It contains **no assumed class builds or archetypes**; use it solely as a syntax blueprint to wire user-specified logic.

---

## 1. Aura & Combat State Syntax (Inside Custom Engines)

### 1.1 Self Aura Stack Check
```csharp
float stacks = Bot.Self.GetAura("<UserSpecifiedAuraName>")?.Value ?? 0;
if (stacks >= <UserSpecifiedThreshold>)
{
    if (Bot.Skills.CanUseSkill(<UserSkillIndex>) && Bot.Skills.UseSkill(<UserSkillIndex>))
    {
        FileLog("[<ClassName>] Cast skill <UserSkillIndex> on <UserSpecifiedAuraName> threshold.");
        return;
    }
}
```

### 1.2 Target / Enemy Debuff Existence Check
```csharp
var debuff = Bot.Target.GetAura("<UserSpecifiedDebuffName>");
if (debuff != null)
{
    if (Bot.Skills.CanUseSkill(<UserSkillIndex>) && Bot.Skills.UseSkill(<UserSkillIndex>))
    {
        FileLog("[<ClassName>] Cast skill <UserSkillIndex> on active <UserSpecifiedDebuffName>.");
        return;
    }
}
```

### 1.3 Target / Enemy Debuff Remaining Time Check
```csharp
var debuff = Bot.Target.GetAura("<UserSpecifiedDebuffName>");
double timeLeft = GetAuraSecondsRemaining(debuff);
if (debuff == null || timeLeft < <UserSpecifiedSeconds>)
{
    if (Bot.Skills.CanUseSkill(<UserSkillIndex>) && Bot.Skills.UseSkill(<UserSkillIndex>))
    {
        FileLog("[<ClassName>] Refreshed skill <UserSkillIndex> (<UserSpecifiedDebuffName> < <UserSpecifiedSeconds>s).");
        return;
    }
}
```

> **Aura Duration Helper (`CoreDUCK.cs` ~line 2192)**:
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

### 1.4 Custom Engine Method Skeleton
```csharp
public void <ClassName>SkillEngine()
{
    if (!Bot.Player.Alive)
    {
        skillIndex = 0;
        return;
    }

    if (!Bot.Player.HasTarget || Bot.Player.Target?.HP <= 0)
        return;

    // --- User's conditional checks here ---

    // End with clean fallthrough to user's builder/filler spam
    CustomSkillEngine();
}
```

---

## 2. Class Preset Factory Syntax (`CoreDUCK.cs` ~lines 236–726)

### 2.1 Single-Mode Preset Structure
```csharp
public ClassPreset <ClassName>() =>
    new()
    {
        ClassName = "<UserClassName>",
        AlternateClassNames = new[] { "<UserSpecifiedAlias1>", "<UserSpecifiedAlias2>" }, // ONLY names user explicitly provided
        Skills = new[] { <UserSkillNumbers> },
        SkillMode = SkillEngineMode.<UserSpecifiedMode>,
        SurvivalSkill = <UserSurvivalSkillOr0>,
        SurvivalHealthThreshold = <UserSurvivalThresholdOr0>,
        BaseEnhancement = EnhancementType.<UserBase>,
        CapeEnhancement = CapeSpecial.<UserCape>,
        HelmEnhancement = HelmSpecial.<UserHelm>,
        WeaponEnhancement = WeaponSpecial.<UserWeapon>,
        WeaponEnhancementFallbacks = new[] { WeaponSpecial.<UserFallback1>, WeaponSpecial.<UserFallback2> },
        Tonic = "<UserTonic>",
        Elixir = "<UserElixir>",
        CombatPotion = "<UserCombatPotion>",
    };
```

### 2.2 Multi-Mode Preset Structure (When User Specifies 2+ Modes)
```csharp
public ClassPreset <ClassName>(bool <userModeParam> = false) =>
    new()
    {
        ClassName = "<UserClassName>",
        AlternateClassNames = new[] { "<UserSpecifiedAliases>" },
        Skills = <userModeParam> ? new[] { <ModeASkills> } : new[] { <ModeBSkills> },
        SkillMode = <userModeParam> ? SkillEngineMode.<ModeAEngine> : SkillEngineMode.<ModeBEngine>,
        SurvivalSkill = <userModeParam> ? <ModeASurvivalSkill> : <ModeBSurvivalSkill>,
        SurvivalHealthThreshold = <userModeParam> ? <ModeAThreshold> : <ModeBThreshold>,
        BaseEnhancement = <userModeParam> ? EnhancementType.<ModeABase> : EnhancementType.<ModeBBase>,
        CapeEnhancement = <userModeParam> ? CapeSpecial.<ModeACape> : CapeSpecial.<ModeBCape>,
        HelmEnhancement = <userModeParam> ? HelmSpecial.<ModeAHelm> : HelmSpecial.<ModeBHelm>,
        WeaponEnhancement = <userModeParam> ? WeaponSpecial.<ModeAWeapon> : WeaponSpecial.<ModeBWeapon>,
        WeaponEnhancementFallbacks = new[] { WeaponSpecial.<Fallback1>, WeaponSpecial.<Fallback2> },
        Tonic = "<UserTonic>",
        Elixir = "<UserElixir>",
        CombatPotion = "<UserCombatPotion>",
    };
```

---

## 3. Name Mapping Syntax (`CoreDUCK.cs` ~lines 6125–6226)

> **STRICT RULE**: Only map the exact names the user explicitly provided. NEVER invent acronyms, spaceless variants, or slang aliases (e.g. only map `"Alpha Omega"` and `"Alpha DOOMmega"` if that is what the user said; never invent `"AO"` or `"AlphaOmega"`).

```csharp
if (norm.Equals("<ExactName1>", StringComparison.OrdinalIgnoreCase)
    || norm.Equals("<ExactName2>", StringComparison.OrdinalIgnoreCase))
    return <FactoryMethod>();
```

---

## 4. Multi-Mode Script Option Syntax (`TestClassDUCK.cs`)

When the user specifies multiple modes, wire them into `TestClassDUCK.cs`:

### 4.1 Nested Enum Declaration (Inside `public class TestClassDUCK`)
```csharp
public class TestClassDUCK
{
    public enum <ClassName>Mode
    {
        <UserModeAName>,
        <UserModeBName>
    }
```

### 4.2 Option Declaration
```csharp
    new Option<<ClassName>Mode>(
        "<ClassName>Mode",
        "<ClassName> Mode",
        "<Description of modes>",
        <ClassName>Mode.<DefaultMode>
    ),
```

### 4.3 Resolution in `ResolvePreset()`
```csharp
if (norm.Equals("<ExactName1>", StringComparison.OrdinalIgnoreCase))
{
    var mode = Bot.Config?.Get<<ClassName>Mode>("<ClassName>Mode") ?? <ClassName>Mode.<DefaultMode>;
    return Duck.<FactoryMethod>(<param>: mode == <ClassName>Mode.<TargetMode>);
}
```
