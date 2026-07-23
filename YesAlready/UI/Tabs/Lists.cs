using Dalamud.Interface;
using ECommons.LanguageHelpers;
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

        if (ImGuiX.IconButton(FontAwesomeIcon.Plus, "Add new entry".Loc()))
        {
            var newNode = new ListEntryNode { Enabled = false, Text = "Your text goes here" };
            ListRootFolder.Children.Add(newNode);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "Add last selected as new entry".Loc()))
        {
            var newNode = new ListEntryNode { Enabled = true, Text = Service.Watcher.LastSeenListSelection, TargetRestricted = true, TargetText = Service.Watcher.LastSeenListTarget };
            ListRootFolder.Children.Add(newNode);
            C.Save();
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.FolderPlus, "Add folder".Loc()))
        {
            var newNode = new TextFolderNode { Name = "Untitled folder" };
            ListRootFolder.Children.Add(newNode);
            C.Save();
        }

        var sb = new StringBuilder();
        sb.AppendLine("Enter into the input all or part of the text inside a line in a list dialog.".Loc());
        sb.AppendLine("For example: \"Purchase a Mini Cactpot ticket\" in the Gold Saucer.".Loc());
        sb.AppendLine();
        sb.AppendLine("Alternatively, wrap your text in forward slashes to use as a regex.".Loc());
        sb.AppendLine("As such: \"/Purchase a .*? ticket/\"".Loc());
        sb.AppendLine();
        sb.AppendLine("If any line in the list matches, then that line will be chosen.".Loc());
        sb.AppendLine();
        sb.AppendLine("Right click a line to view options.".Loc());
        sb.AppendLine("Double click an entry for quick enable/disable.".Loc());
        sb.AppendLine("Ctrl-Shift right click a line to delete it and any children.".Loc());
        sb.AppendLine();
        sb.AppendLine("Currently supported list addons:".Loc());
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
            ImGuiX.TextTooltip("Invalid Text and Target Regex".Loc());
        else if (!validRegex)
            ImGuiX.TextTooltip("Invalid Text Regex".Loc());
        else if (!validTarget)
            ImGuiX.TextTooltip("Invalid Target Regex".Loc());

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
        if (ImGui.Checkbox("Enabled".Loc(), ref enabled))
        {
            node.Enabled = enabled;
            C.Save();
        }

        var trashAltWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.TrashAlt);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - trashAltWidth);
        if (ImGuiX.IconButton(FontAwesomeIcon.TrashAlt, "Delete".Loc()))
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
        if (ImGui.Checkbox("Target Restricted".Loc(), ref targetRestricted))
        {
            node.TargetRestricted = targetRestricted;
            C.Save();
        }

        var searchPlusWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.SearchPlus);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - searchPlusWidth);
        if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "Fill with current target".Loc()))
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
                node.TargetText = "Could not find target";
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
