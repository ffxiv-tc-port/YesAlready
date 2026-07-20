using Dalamud.Interface;
using ImGuiNET;
using System.Numerics;
using System.Text;

namespace YesAlready.UI.Tabs;
public static class Talk
{
    private static TextFolderNode TalkRootFolder => C.TalkRootFolder;

    public static void DrawButtons()
    {
        var style = ImGui.GetStyle();
        var newStyle = new Vector2(style.ItemSpacing.X / 2, style.ItemSpacing.Y);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, newStyle);

        if (ImGuiX.IconButton(FontAwesomeIcon.Plus, "新增項目"))
        {
            var newNode = new TalkEntryNode { Enabled = false, TargetText = "Your text goes here" };
            TalkRootFolder.Children.Add(newNode);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "將目前目標新增為項目"))
        {
            var target = Svc.Targets.Target;
            if (target != null)
            {
                var targetName = Service.Watcher.LastSeenTalkTarget = target.Name.TextValue;
                var newNode = new TalkEntryNode { Enabled = true, TargetText = targetName };
                TalkRootFolder.Children.Add(newNode);
                C.Save();
            }
            else
                Svc.Toasts.ShowError("無法新增項目：未選擇任何目標。");
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.FolderPlus, "新增資料夾"))
        {
            var newNode = new TextFolderNode { Name = "Untitled folder" };
            TalkRootFolder.Children.Add(newNode);
            C.Save();
        }

        var sb = new StringBuilder();
        sb.AppendLine("在輸入框中輸入對話視窗中所選目標名稱的全部或部分內容。");
        sb.AppendLine("例如：水晶都可輸入「Moyce」。");
        sb.AppendLine();
        sb.AppendLine("也可以將文字用斜線包起來作為正規表示式使用。");
        sb.AppendLine("如：\"/(Moyce|Eirikur)/\"");
        sb.AppendLine();
        sb.AppendLine("若要略過你的雇員，請加入召喚鈴。");
        sb.AppendLine();
        sb.AppendLine("右鍵點擊一列可檢視選項。");
        sb.AppendLine("雙擊項目可快速啟用/停用。");
        sb.AppendLine("Ctrl-Shift 右鍵點擊一列可刪除該項目及其子項目。");
        sb.AppendLine();
        sb.AppendLine("目前支援的清單 Addon：");
        sb.AppendLine("  - Talk");

        ImGui.SameLine();
        ImGuiX.IconButton(FontAwesomeIcon.QuestionCircle, sb.ToString());

        ImGui.PopStyleVar(); // ItemSpacing
    }

    public static void DisplayTalkEntryNode(TalkEntryNode node)
    {
        var validTarget = node.TargetIsRegex && node.TargetRegex != null || !node.TargetIsRegex;

        if (!node.Enabled && !validTarget)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.5f, 0, 0, 1));
        else if (!node.Enabled)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.5f, .5f, .5f, 1));
        else if (!validTarget)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));

        ImGui.TreeNodeEx($"{node.Name}##{node.Name}-tree", ImGuiTreeNodeFlags.Leaf);
        ImGui.TreePop();

        if (!node.Enabled || !validTarget)
            ImGui.PopStyleColor();

        if (!validTarget)
            ImGuiX.TextTooltip("無效的目標正規表示式");

        if (ImGui.IsItemHovered())
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                node.Enabled = !node.Enabled;
                C.Save();
                return;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                var io = ImGui.GetIO();
                if (io.KeyCtrl && io.KeyShift)
                {
                    if (C.TryFindParent(node, out var parent))
                    {
                        parent!.Children.Remove(node);
                        C.Save();
                    }

                    return;
                }
                else
                {
                    ImGui.OpenPopup($"{node.GetHashCode()}-popup");
                }
            }
        }

        MainWindow.TextNodePopup(node);
        MainWindow.TextNodeDragDrop(node);
    }

    public static void DrawPopup(TalkEntryNode node, Vector2 spacing)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, spacing);

        var enabled = node.Enabled;
        if (ImGui.Checkbox("啟用", ref enabled))
        {
            node.Enabled = enabled;
            C.Save();
        }

        var trashAltWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.TrashAlt);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - trashAltWidth);
        if (ImGuiX.IconButton(FontAwesomeIcon.TrashAlt, "刪除"))
        {
            if (C.TryFindParent(node, out var parentNode))
            {
                parentNode!.Children.Remove(node);
                C.Save();
            }
        }

        var searchPlusWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.SearchPlus);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - searchPlusWidth - trashAltWidth - spacing.X);
        if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "填入目前目標"))
        {
            var target = Svc.Targets.Target;
            var name = target?.Name?.TextValue ?? string.Empty;

            if (!string.IsNullOrEmpty(name))
            {
                node.TargetText = name;
                C.Save();
            }
            else
            {
                node.TargetText = "找不到目標";
                C.Save();
            }
        }

        ImGui.PopStyleVar(); // ItemSpacing

        var targetText = node.TargetText;
        if (ImGui.InputText($"##{node.Name}-targetText", ref targetText, 10_000, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            node.TargetText = targetText;
            C.Save();
        }
    }
}
