using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Linq;
using System.Numerics;

namespace YesAlready.Interface;

internal class ZoneListWindow : Window
{
    public static string Title = $"{Name} 區域列表";
    private bool sortZoneByName = false;
    public ZoneListWindow() : base(Title)
    {
        Size = new Vector2(525, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        using var _ = ImRaii.PushColor(ImGuiCol.ResizeGrip, 0);

        ImGui.Text($"目前 ID：{Svc.ClientState.TerritoryType}");

        ImGui.Checkbox("依名稱排序", ref sortZoneByName);

        ImGui.Columns(2);

        ImGui.Text("ID");
        ImGui.NextColumn();

        ImGui.Text("名稱");
        ImGui.NextColumn();

        ImGui.Separator();

        var names = P.TerritoryNames.AsEnumerable();

        if (sortZoneByName)
            names = names.ToList().OrderBy(kvp => kvp.Value);

        foreach (var kvp in names)
        {
            ImGui.Text($"{kvp.Key}");
            ImGui.NextColumn();

            ImGui.Text($"{kvp.Value}");
            ImGui.NextColumn();
        }

        ImGui.Columns(1);
    }
}
