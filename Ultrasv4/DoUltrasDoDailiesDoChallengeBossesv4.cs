/*
name: Do Ultras and Dailies and Challenge Bosses
description: Runs all ultras, dailies, and challenge bosses.
tags: ultras,dailies,challenge bosses,all
*/
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreEnginev4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/CoreUltrav4.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesUltras/UltraWaitForArmyv4.cs

//cs_include Scripts/DUCKScripts/Ultrasv4/DoAllUltrasv4.cs
//cs_include Scripts/DUCKScripts/Ultrasv4/DependenciesDailies/DoAllChallengeBossesv4.cs
//cs_include Scripts/Dailies/0AllDailies.cs

using Skua.Core.Interfaces;

public class DoUltrasDoDailiesDoChallengeBossesv4
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots C => CoreBots.Instance;
    private static CoreEnginev4 Core => CoreEnginev4.Instance;
    private static CoreUltrav4 Ultra => _Ultra ??= new CoreUltrav4();
    private static CoreUltrav4 _Ultra;

    public void ScriptMain(IScriptInterface Bot)
    {
        C.SetOptions(true);
        Core.Boot();

        new DoAllUltrasv4().RunAll();
        UltraWaitForArmyv4.Instance.NewWaitForArmy(3, "doall_sync.sync", useSkill: false);

        new DoAllChallengeBossesv4().RunAll();
        UltraWaitForArmyv4.Instance.NewWaitForArmy(3, "doall_sync.sync", useSkill: false);

        new FarmAllDailies().DoAllDailies();
        UltraWaitForArmyv4.Instance.NewWaitForArmy(3, "doall_sync.sync", useSkill: false);

        Core.DisableSkills();
        C.SetOptions(false);
    }
}
