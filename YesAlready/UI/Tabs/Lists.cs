using Dalamud.Interface;
using ImGuiNET;
using System.Numerics;
using System.Text;

namespace YesAlready.UI.Tabs;
public static class Lists
{
    private static TextFolderNode ListRootFolder => C.ListRootFolder;

    public static void DrawButtons()
    {
        var style = ImGui.GetStyle();
        var newStyle = new Vector2(style.ItemSpacing.X / 2, style.ItemSpacing.Y);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, newStyle);

        if (ImGuiX.IconButton(FontAwesomeIcon.Plus, "新增項目"))
        {
            var newNode = new ListEntryNode { Enabled = false, Text = "Your text goes here" };
            ListRootFolder.Children.Add(newNode);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "將最近選擇的內容新增為項目"))
        {
            var newNode = new ListEntryNode { Enabled = true, Text = Service.Watcher.LastSeenListSelection, TargetRestricted = true, TargetText = Service.Watcher.LastSeenListTarget };
            ListRootFolder.Children.Add(newNode);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.FolderPlus, "新增資料夾"))
        {
            var newNode = new TextFolderNode { Name = "Untitled folder" };
            ListRootFolder.Children.Add(newNode);
            C.Save();
        }

        var sb = new StringBuilder();
        sb.AppendLine("在輸入框中輸入清單對話框中某一列文字的全部或部分內容。");
        sb.AppendLine("例如：黃金水都中可輸入「Purchase a Mini Cactpot ticket」。");
        sb.AppendLine();
        sb.AppendLine("也可以將文字用斜線包起來作為正規表示式使用。");
        sb.AppendLine("如：\"/Purchase a .*? ticket/\"");
        sb.AppendLine();
        sb.AppendLine("若清單中任一列符合，該列即會被選擇。");
        sb.AppendLine();
        sb.AppendLine("右鍵點擊一列可檢視選項。");
        sb.AppendLine("雙擊項目可快速啟用/停用。");
        sb.AppendLine("Ctrl-Shift 右鍵點擊一列可刪除該項目及其子項目。");
        sb.AppendLine();
        sb.AppendLine("目前支援的清單 Addon：");
        sb.AppendLine("  - SelectString");
        sb.AppendLine("  - SelectIconString");

        ImGui.SameLine();
        ImGuiX.IconButton(FontAwesomeIcon.QuestionCircle, sb.ToString());

        ImGui.PopStyleVar(); // ItemSpacing
    }

    public static void DisplayListEntryNode(ListEntryNode node)
    {
        var validRegex = node.IsTextRegex && node.TextRegex != null || !node.IsTextRegex;
        var validTarget = !node.TargetRestricted || node.TargetIsRegex && node.TargetRegex != null || !node.TargetIsRegex;

        if (!node.Enabled && (!validRegex || !validTarget))
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.5f, 0, 0, 1));
        else if (!node.Enabled)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.5f, .5f, .5f, 1));
        else if (!validRegex || !validTarget)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));

        ImGui.TreeNodeEx($"{node.Name}##{node.Name}-tree", ImGuiTreeNodeFlags.Leaf);
        ImGui.TreePop();

        if (!node.Enabled || !validRegex || !validTarget)
            ImGui.PopStyleColor();

        if (!validRegex && !validTarget)
            ImGuiX.TextTooltip("無效的文字與目標正規表示式");
        else if (!validRegex)
            ImGuiX.TextTooltip("無效的文字正規表示式");
        else if (!validTarget)
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

    public static void DrawPopup(ListEntryNode node, Vector2 spacing)
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

        var matchText = node.Text;
        if (ImGui.InputText($"##{node.Name}-matchText", ref matchText, 10_000, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            node.Text = matchText;
            C.Save();
        }

        var targetRestricted = node.TargetRestricted;
        if (ImGui.Checkbox("限制目標", ref targetRestricted))
        {
            node.TargetRestricted = targetRestricted;
            C.Save();
        }

        var searchPlusWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.SearchPlus);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - searchPlusWidth);
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
