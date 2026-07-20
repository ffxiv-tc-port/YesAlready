using Dalamud.Interface;
using ImGuiNET;
using System.Numerics;
using System.Text;

namespace YesAlready.UI.Tabs;
public static class Ok
{
    private static TextFolderNode OkRootFolder => C.OkRootFolder;

    public static void DrawButtons()
    {
        var style = ImGui.GetStyle();
        var newStyle = new Vector2(style.ItemSpacing.X / 2, style.ItemSpacing.Y);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, newStyle);

        if (ImGuiX.IconButton(FontAwesomeIcon.Plus, "新增項目"))
        {
            var newNode = new OkEntryNode { Enabled = false, Text = "Your text goes here" };
            OkRootFolder.Children.Add(newNode);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "將最近出現的內容新增為項目"))
        {
            var io = ImGui.GetIO();
            var createFolder = io.KeyShift;

            Configuration.CreateNode<OkEntryNode>(OkRootFolder, createFolder);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.FolderPlus, "新增資料夾"))
        {
            var newNode = new TextFolderNode { Name = "Untitled folder" };
            OkRootFolder.Children.Add(newNode);
            C.Save();
        }

        var sb = new StringBuilder();
        sb.AppendLine("在輸入框中輸入對話框內文字的全部或部分內容。");
        sb.AppendLine("例如：信箱已滿的對話框可輸入「You cannot carry any more letters」。");
        sb.AppendLine();
        sb.AppendLine("也可以將文字用斜線包起來作為正規表示式使用。");
        sb.AppendLine("如：\"/.* carry any more letters .*/\"");
        sb.AppendLine();
        sb.AppendLine("若符合，會自動點擊確定按鈕。");
        sb.AppendLine();
        sb.AppendLine("右鍵點擊一列可檢視選項。");
        sb.AppendLine("雙擊項目可快速啟用/停用。");
        sb.AppendLine("Ctrl-Shift 右鍵點擊一列可刪除該項目及其子項目。");
        sb.AppendLine();
        sb.AppendLine("「將最近出現的內容新增為項目」按鈕的修飾鍵：");
        sb.AppendLine("   Shift-點擊：新增到新的或既有的第一個資料夾中。");
        sb.AppendLine();
        sb.AppendLine("目前支援的文字 Addon：");
        sb.AppendLine("  - SelectOk");

        ImGui.SameLine();
        ImGuiX.IconButton(FontAwesomeIcon.QuestionCircle, sb.ToString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(sb.ToString());

        ImGui.PopStyleVar(); // ItemSpacing
    }

    public static void DisplayOkEntryNode(OkEntryNode node)
    {
        var validRegex = node.IsTextRegex && node.TextRegex != null || !node.IsTextRegex;

        if (!node.Enabled && !validRegex)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.5f, 0, 0, 1));
        else if (!node.Enabled)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.5f, .5f, .5f, 1));
        else if (!validRegex)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));

        ImGui.TreeNodeEx($"{node.Name}##{node.Name}-tree", ImGuiTreeNodeFlags.Leaf);
        ImGui.TreePop();

        if (!node.Enabled || !validRegex)
            ImGui.PopStyleColor();

        if (!validRegex)
            ImGuiX.TextTooltip("無效的文字正規表示式");

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

    public static void DrawPopup(OkEntryNode node, Vector2 spacing)
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

        ImGui.PopStyleVar(); // ItemSpacing
    }
}
