using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.LanguageHelpers;
using Dalamud.Bindings.ImGui;
using System;
using System.Linq;
using System.Numerics;

namespace YesAlready.Interface;

internal class ConditionsListWindow : Window
{
    public static string Title = $"{Name} " + "Conditions List".Loc();
    private static (string Name, string Id)[]? flagRows;
    public ConditionsListWindow() : base(Title)
    {
        Size = new Vector2(525, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        using var _ = ImRaii.PushColor(ImGuiCol.ResizeGrip, 0);

        ImGui.TextUnformatted("Current Conditions: ??".Loc(string.Join(", ", Svc.Condition.AsReadOnlySet().Where(x => Svc.Condition[x]).Select(flag => flag.ToString()).ToList())));

        ImGui.Columns(2);

        ImGui.TextUnformatted("ID");
        ImGui.NextColumn();

        ImGui.TextUnformatted("Name".Loc());
        ImGui.NextColumn();

        ImGui.Separator();

        // 列舉的名稱與數值在執行期不會變：第一次畫時建好，之後每幀只畫。
        flagRows ??= Enum.GetValues<ConditionFlag>().Select(flag => (flag.ToString(), ((int)flag).ToString())).ToArray();
        foreach (var (name, id) in flagRows)
        {
            ImGui.TextUnformatted(name);
            ImGui.NextColumn();

            ImGui.TextUnformatted(id);
            ImGui.NextColumn();
        }

        ImGui.Columns(1);
    }
}
