using Skua.Core.Interfaces;
using System.Linq;

public class Script
{
    public void ScriptMain(IScriptInterface bot)
    {
        bot.Log("Checking Quest 8653 requirements...");
        var quest = bot.Quests.Tree.Find(q => q.ID == 8653);
        if (quest == null)
        {
            bot.Log("Quest 8653 not found in Tree.");
            return;
        }
        foreach(var req in quest.Requirements)
        {
            bot.Log($"- Req: {req.Name} (ID: {req.ID}) x{req.Quantity}");
        }
    }
}
