using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using ECommons.SimpleGui;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Text;
using YesAlready.Interface;

namespace YesAlready.UI.Tabs;
public class YesNo
{
    private static TextFolderNode RootFolder => C.RootFolder;

    public static void DrawButtons()
    {
        var style = ImGui.GetStyle();
        var newStyle = new Vector2(style.ItemSpacing.X / 2, style.ItemSpacing.Y);
        using var _ = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, newStyle);

        if (ImGuiX.IconButton(FontAwesomeIcon.Plus, "新增項目"))
        {
            var newNode = new TextEntryNode { Enabled = false, Text = "Your text goes here" };
            RootFolder.Children.Add(newNode);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "將最近出現的內容新增為項目"))
        {
            var io = ImGui.GetIO();
            var zoneRestricted = io.KeyCtrl;
            var createFolder = io.KeyShift;
            var selectNo = io.KeyAlt;

            Configuration.CreateNode<TextEntryNode>(C.RootFolder, createFolder, zoneRestricted ? GenericHelpers.GetRow<TerritoryType>(Player.Territory)?.Name.ExtractText() : null, !selectNo);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.FolderPlus, "新增資料夾"))
        {
            var newNode = new TextFolderNode { Name = "未命名資料夾" };
            RootFolder.Children.Add(newNode);
            C.Save();
        }

        var sb = new StringBuilder();
        sb.AppendLine("在輸入框中輸入對話框內文字的全部或部分內容。");
        sb.AppendLine("例如：傳送對話框可輸入「Teleport to 」。");
        sb.AppendLine();
        sb.AppendLine("也可以將文字用斜線包起來作為正規表示式使用。");
        sb.AppendLine("如：\"/Teleport to .*? for \\d+(,\\d+)? gil\\?/\"");
        sb.AppendLine("或更簡單：\"/Teleport to .*?/\"（但要注意可能意外符合到其他內容）");
        sb.AppendLine();
        sb.AppendLine("若符合，會自動點擊「是」按鈕（如有勾選框則一併勾選）。");
        sb.AppendLine();
        sb.AppendLine("右鍵點擊一列可檢視選項。");
        sb.AppendLine("雙擊項目可快速啟用/停用。");
        sb.AppendLine("Ctrl-Shift 右鍵點擊一列可刪除該項目及其子項目。");
        sb.AppendLine();
        sb.AppendLine("「將最近出現的內容新增為項目」按鈕的修飾鍵：");
        sb.AppendLine("   Shift-點擊：新增到以目前區域名稱命名的新（或既有）資料夾，並限制在該區域內。");
        sb.AppendLine("   Ctrl-點擊：建立一個限制在目前區域內的項目，不建立具名資料夾。");
        sb.AppendLine("   Alt-點擊：建立「選擇否」項目，而非「選擇是」。");
        sb.AppendLine("   Alt-點擊可與 Shift/Ctrl-點擊組合使用。");
        sb.AppendLine();
        sb.AppendLine("目前支援的文字 Addon：");
        sb.AppendLine("  - SelectYesNo");

        ImGui.SameLine();
        ImGuiX.IconButton(FontAwesomeIcon.QuestionCircle, sb.ToString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(sb.ToString());

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.EllipsisH))
            ImGui.OpenPopup("SelectYesno additional options");
        DrawYesnoBothers();
    }

    private static void DrawYesnoBothers()
    {
        using var popup = ImRaii.Popup("SelectYesno additional options");
        if (popup.Success)
        {
            var gimmickConfirm = C.GimmickYesNo;
            if (ImGui.Checkbox("自動 GimmickYesNo", ref gimmickConfirm))
            {
                C.GimmickYesNo = gimmickConfirm;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動確認屬於 GimmickYesNo 表格的是/否對話框。\n這些多半是副本中的是/否對話，例如「要解鎖這扇門嗎？」或「要撿起這個物品嗎？」", wrapped: false);

            var pfConfirm = C.PartyFinderJoinConfirm;
            if (ImGui.Checkbox("尋求小隊 x 是/否選擇", ref pfConfirm))
            {
                C.PartyFinderJoinConfirm = pfConfirm;
                C.Save();
            }

            ImGuiX.IndentedTextColored("加入招募小隊時自動確認。", wrapped: false);

            var autoCollect = C.AutoCollectable;
            if (ImGui.Checkbox("自動收藏品", ref autoCollect))
            {
                C.AutoCollectable = autoCollect;
                C.Save();
            }

            ImGuiX.IndentedTextColored("自動接受值得繳交的收藏品，並拒絕價值不足的收藏品。", wrapped: false);
        }
    }

    public static void DisplayEntryNode(TextEntryNode node)
    {
        var validRegex = node.IsTextRegex && node.TextRegex != null || !node.IsTextRegex;
        var validZone = !node.ZoneRestricted || node.ZoneIsRegex && node.ZoneRegex != null || !node.ZoneIsRegex;

        if (!node.Enabled && (!validRegex || !validZone))
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.5f, 0, 0, 1));
        else if (!node.Enabled)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.5f, .5f, .5f, 1));
        else if (!validRegex || !validZone)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));

        ImGui.TreeNodeEx($"{node.Name}##{node.Name}-tree", ImGuiTreeNodeFlags.Leaf);
        ImGui.TreePop();

        if (!node.Enabled || !validRegex || !validZone)
            ImGui.PopStyleColor();

        if (!validRegex && !validZone)
            ImGuiX.TextTooltip("無效的文字與區域正規表示式");
        else if (!validRegex)
            ImGuiX.TextTooltip("無效的文字正規表示式");
        else if (!validZone)
            ImGuiX.TextTooltip("無效的區域正規表示式");

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

    public static void DrawPopup(TextEntryNode textNode, Vector2 spacing)
    {
        using var _ = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, spacing);

        var enabled = textNode.Enabled;
        if (ImGui.Checkbox("啟用", ref enabled))
        {
            textNode.Enabled = enabled;
            C.Save();
        }

        ImGui.SameLine(100f);
        var isYes = textNode.IsYes;
        var title = isYes ? "點擊「是」" : "點擊「否」";
        if (ImGui.Button(title))
        {
            textNode.IsYes = !isYes;
            C.Save();
        }

        var trashAltWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.TrashAlt);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - trashAltWidth);
        if (ImGuiX.IconButton(FontAwesomeIcon.TrashAlt, "刪除"))
        {
            if (C.TryFindParent(textNode, out var parentNode))
            {
                parentNode!.Children.Remove(textNode);
                C.Save();
            }
        }

        var matchText = textNode.Text;
        if (ImGui.InputText($"##{textNode.Name}-matchText", ref matchText, 10_000, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            textNode.Text = matchText;
            C.Save();
        }

        var zoneRestricted = textNode.ZoneRestricted;
        if (ImGui.Checkbox("限制區域", ref zoneRestricted))
        {
            textNode.ZoneRestricted = zoneRestricted;
            C.Save();
        }

        var searchWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.Search);
        var searchPlusWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.SearchPlus);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - searchWidth);
        if (ImGuiX.IconButton(FontAwesomeIcon.Search, "區域列表"))
            EzConfigGui.GetWindow<ZoneListWindow>()?.Toggle();

        ImGui.SameLine(ImGui.GetContentRegionMax().X - searchWidth - searchPlusWidth - spacing.X);
        if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "填入目前區域"))
        {
            var currentID = Svc.ClientState.TerritoryType;
            if (P.TerritoryNames.TryGetValue(currentID, out var zoneName))
            {
                textNode.ZoneText = zoneName;
                C.Save();
            }
            else
            {
                textNode.ZoneText = "找不到名稱";
                C.Save();
            }
        }

        var zoneText = textNode.ZoneText;
        if (ImGui.InputText($"##{textNode.Name}-zoneText", ref zoneText, 10_000, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            textNode.ZoneText = zoneText;
            C.Save();
        }

        var conditionRestricted = textNode.RequiresPlayerConditions;
        if (ImGui.Checkbox("限制條件", ref conditionRestricted))
        {
            textNode.RequiresPlayerConditions = conditionRestricted;
            C.Save();
        }
        ImGuiComponents.HelpMarker($"條件可以是名稱（區分大小寫）或 ID，若有多個需以逗號分隔。限制條件只有在所有條件都符合時才會通過。若要反轉某個條件，請在前面加上「!」。");

        ImGui.SameLine(ImGui.GetContentRegionMax().X - searchWidth);
        if (ImGuiX.IconButton(FontAwesomeIcon.Search, "條件列表"))
            EzConfigGui.GetWindow<ConditionsListWindow>()?.Toggle();

        var playerConditions = textNode.PlayerConditions;
        if (ImGui.InputText($"##{textNode.Name}-playerConditionsText", ref playerConditions, 10_000, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            textNode.PlayerConditions = playerConditions;
            C.Save();
        }

        ImGui.NewLine();

        var conditional = textNode.IsConditional;
        if (ImGui.Checkbox("是條件式", ref conditional))
        {
            textNode.IsConditional = conditional;
            C.Save();
        }

        ImGui.Text("目前僅支援數字擷取");

        var conditionalText = textNode.ConditionalNumberTemplate;
        if (ImGui.InputText($"##{textNode.Name}-conditionalText", ref conditionalText, 10_000, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            textNode.ConditionalNumberTemplate = conditionalText;
            C.Save();
        }

        var comparisonType = textNode.ComparisonType;
        if (ImGui.BeginCombo($"##{textNode.Name}-comparisonType", MainWindow.ComparisonTypeToText(comparisonType)))
        {
            foreach (var c in MainWindow.ComparisonTypes)
            {
                var isSelected = comparisonType == c;
                if (ImGui.Selectable(MainWindow.ComparisonTypeToText(c), isSelected))
                {
                    textNode.ComparisonType = c;
                    C.Save();
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        var conditionalNumber = textNode.ConditionalNumber;
        if (ImGui.InputInt($"##{textNode.Name}-conditionalNumber", ref conditionalNumber, 1, 10, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            textNode.ConditionalNumber = conditionalNumber;
            C.Save();
        }
    }
}
