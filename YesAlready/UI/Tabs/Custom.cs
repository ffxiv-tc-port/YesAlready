using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System.Numerics;
using System.Text;
using YesAlready.Features;

namespace YesAlready.UI.Tabs;
public static class Custom
{
    public static void DrawButtons()
    {
        var style = ImGui.GetStyle();
        var newStyle = new Vector2(style.ItemSpacing.X / 2, style.ItemSpacing.Y);
        using var _ = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, newStyle);

        if (ImGuiX.IconButton(FontAwesomeIcon.Plus, "新增項目"))
        {
            var newNode = new CustomEntryNode
            {
                Enabled = true,
                Addon = "AddonName",
                CallbackParams = "-1"
            };
            C.CustomRootFolder.Children.Add(newNode);
            C.Save();
            CustomAddonCallbacks.Toggle();
        }

        var sb = new StringBuilder();
        sb.AppendLine("這個區塊讓你可以建立自訂的「Bothers」。");
        sb.AppendLine("許多 Bothers 都很簡單，只需在指定的 Addon 出現時，傳送單一回呼（callback）參數即可。這裡就是設定這類 Bothers 的地方。");
        sb.AppendLine();
        sb.AppendLine("回呼參數的解析方式與 Something Need Doing 相同。");
        sb.AppendLine("自訂 Bothers 是透過 AddonLifeCycle 在 PostSetup 事件中註冊的。");
        sb.AppendLine();
        sb.AppendLine("有些 Bothers 可能需要不易實現的參數、等待時間或不同的 AddonEvent，這類需求仍可向一般的 Bothers 系統提出請求。");
        sb.AppendLine();
        sb.AppendLine("範例：");
        sb.AppendLine("   AddonName: Character");
        sb.AppendLine("   Parameters: -1");
        sb.AppendLine("   效果：開啟角色（Character）Addon 時，會立即將其關閉。大概沒什麼用處。");

        ImGui.SameLine();
        ImGuiX.IconButton(FontAwesomeIcon.QuestionCircle, sb.ToString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(sb.ToString());
    }

    public static void DrawPopup(CustomEntryNode node, Vector2 spacing)
    {
        using var _ = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, spacing);
        var enabled = node.Enabled;
        if (ImGui.Checkbox("啟用", ref enabled))
        {
            node.Enabled = enabled;
            C.Save();
            CustomAddonCallbacks.Toggle();
        }

        var trashAltWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.TrashAlt);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - trashAltWidth);
        if (ImGuiX.IconButton(FontAwesomeIcon.TrashAlt, "刪除"))
        {
            if (C.TryFindParent(node, out var parentNode))
            {
                parentNode!.Children.Remove(node);
                C.Save();
                CustomAddonCallbacks.Toggle();
            }
        }

        ImGui.TextUnformatted("備註：");
        var noteText = node.Text;
        if (ImGui.InputText($"##{node.Name}-{nameof(noteText)}", ref noteText, 10_000, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            node.Text = noteText;
            C.Save();
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("此欄位不會用於任何判斷，只是用來幫助記住這個 Bother 的用途。");

        ImGui.TextUnformatted("Addon 名稱：");
        var addonName = node.Addon;
        if (ImGui.InputText($"##{node.Name}-{nameof(addonName)}", ref addonName, 100, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            node.Addon = addonName;
            C.Save();
            CustomAddonCallbacks.Toggle();
        }

        ImGui.TextUnformatted("參數：");
        var callbackParams = node.CallbackParams;
        if (ImGui.InputText($"##{node.Name}-{nameof(callbackParams)}", ref callbackParams, 150, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            node.CallbackParams = callbackParams;
            C.Save();
        }
    }
}
