using Dalamud.Interface;
using ImGuiNET;
using System.Numerics;
using System.Text;

namespace YesAlready.UI.Tabs;
public static class Numerics
{
    private static TextFolderNode NumericsRootFolder => C.NumericsRootFolder;

    public static void DrawButtons()
    {
        var style = ImGui.GetStyle();
        var newStyle = new Vector2(style.ItemSpacing.X / 2, style.ItemSpacing.Y);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, newStyle);

        if (ImGuiX.IconButton(FontAwesomeIcon.Plus, "新增項目"))
        {
            var newNode = new NumericsEntryNode { Enabled = false, Text = "Your text goes here" };
            NumericsRootFolder.Children.Add(newNode);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "將最近出現的內容新增為項目"))
        {
            var io = ImGui.GetIO();
            var createFolder = io.KeyShift;

            Configuration.CreateNode<NumericsEntryNode>(NumericsRootFolder, createFolder);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.FolderPlus, "新增資料夾"))
        {
            var newNode = new TextFolderNode { Name = "Untitled folder" };
            NumericsRootFolder.Children.Add(newNode);
            C.Save();
        }

        var sb = new StringBuilder();
        sb.AppendLine("在輸入框中輸入對話框內文字的全部或部分內容。");
        sb.AppendLine("例如：拆分堆疊對話框可輸入「Remove how many from stack?」。");
        sb.AppendLine();
        sb.AppendLine("也可以將文字用斜線包起來作為正規表示式使用。");
        sb.AppendLine("如：\"/Remove .*/\"");
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
        sb.AppendLine("目前支援的數值 Addon：");
        sb.AppendLine("  - InputNumeric");

        ImGui.SameLine();
        ImGuiX.IconButton(FontAwesomeIcon.QuestionCircle, sb.ToString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(sb.ToString());

        ImGui.PopStyleVar(); // ItemSpacing
    }

    public static void DrawPopup(NumericsEntryNode node, Vector2 spacing)
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

        var percent = node.IsPercent;
        if (ImGui.Checkbox("百分比", ref percent))
        {
            node.IsPercent = percent;
            C.Save();
        }
        if (node.IsPercent)
        {
            var percentage = node.Percentage;
            if (ImGui.SliderInt($"最大值百分比##{node.GetHashCode()}", ref percentage, 0, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp))
            {
                if (percentage < 0) node.Percentage = 0;
                else node.Percentage = percentage;
                if (percentage > 100) node.Percentage = 100;
                else node.Percentage = percentage;
                C.Save();
            }
        }
        else
        {
            var quantity = node.Quantity;
            if (ImGui.InputInt($"預設數量##{node.GetHashCode()}", ref quantity))
            {
                if (quantity < 1) node.Quantity = 1;
                else node.Quantity = quantity;
                C.Save();
            }
        }

        //var targetRestricted = node.TargetRestricted;
        //if (ImGui.Checkbox("Target Restricted", ref targetRestricted))
        //{
        //    node.TargetRestricted = targetRestricted;
        //    C.Save();
        //}

        //var searchPlusWidth = Utils.ImGuiEx.GetIconButtonWidth(FontAwesomeIcon.SearchPlus);

        //ImGui.SameLine(ImGui.GetContentRegionMax().X - searchPlusWidth);
        //if (Utils.ImGuiEx.IconButton(FontAwesomeIcon.SearchPlus, "Fill with current target"))
        //{
        //    var target = Svc.Targets.Target;
        //    var name = target?.Name?.TextValue ?? string.Empty;

        //    if (!string.IsNullOrEmpty(name))
        //    {
        //        node.TargetText = name;
        //        C.Save();
        //    }
        //    else
        //    {
        //        node.TargetText = "Could not find target";
        //        C.Save();
        //    }
        //}

        //var targetText = node.TargetText;
        //if (ImGui.InputText($"##{node.Name}-targetText", ref targetText, 10_000, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        //{
        //    node.TargetText = targetText;
        //    C.Save();
        //}
    }
}
