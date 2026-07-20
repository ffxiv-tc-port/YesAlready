using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using YesAlready.UI.Components;

namespace YesAlready.UI.Tabs;

public class CoreTab : BaseTab
{
    public enum ViewMode
    {
        ByType,
        Alphabetical,
        Folders
    }

    public ViewMode CurrentViewMode { get; private set; } = ViewMode.ByType;

    private string searchFilter = "";
    private bool showDisabled = true;
    private bool showEnabled = true;
    private bool showInvalid = true;

    protected override string TabName => "Core";
    protected override string HelpText => GetHelpText();

    protected override void DrawContent()
    {
        DrawToolbar();
        DrawViewOptions();
        DrawNodeList();
    }

    private void DrawToolbar()
    {
        var style = ImGui.GetStyle();
        var newStyle = new Vector2(style.ItemSpacing.X / 2, style.ItemSpacing.Y);
        using var _ = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, newStyle);

        // Add new entry button with type selection
        if (ImGui.Button("新增"))
        {
            ImGui.OpenPopup("AddNewEntry");
        }
        DrawAddNewEntryPopup();

        // Add last seen button with preview
        ImGui.SameLine();
        if (ImGui.Button("新增最近出現"))
        {
            ImGui.OpenPopup("AddLastSeen");
        }
        DrawAddLastSeenPopup();

        // Add folder button
        ImGui.SameLine();
        if (ImGui.Button("新增資料夾"))
        {

        }

        // Search filter
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("##Search", ref searchFilter, 100))
        {
            // Update filtered results
        }

        // Filter toggles
        ImGui.SameLine();
        if (ImGui.Checkbox("顯示已啟用", ref showEnabled)) { }
        ImGui.SameLine();
        if (ImGui.Checkbox("顯示已停用", ref showDisabled)) { }
        ImGui.SameLine();
        if (ImGui.Checkbox("顯示無效項目", ref showInvalid)) { }

        DrawHelpButton();
    }

    private void DrawViewOptions()
    {
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##ViewMode", CurrentViewMode.ToString()))
        {
            foreach (ViewMode mode in Enum.GetValues(typeof(ViewMode)))
            {
                if (ImGui.Selectable(mode.ToString(), CurrentViewMode == mode))
                {
                    CurrentViewMode = mode;
                }
            }
            ImGui.EndCombo();
        }
    }

    private void DrawAddNewEntryPopup()
    {
        if (ImGui.BeginPopup("AddNewEntry"))
        {
            if (ImGui.Selectable("是/否對話框"))
            {
                var newNode = new TextEntryNode { Enabled = false, Text = "Your text goes here" };
                C.RootFolder.Children.Add(newNode);
                C.Save();
            }
            if (ImGui.Selectable("確定對話框"))
            {
                var newNode = new OkEntryNode { Enabled = false, Text = "Your text goes here" };
                C.OkRootFolder.Children.Add(newNode);
                C.Save();
            }
            if (ImGui.Selectable("清單選擇"))
            {
                var newNode = new ListEntryNode { Enabled = false, Text = "Your text goes here" };
                C.ListRootFolder.Children.Add(newNode);
                C.Save();
            }
            if (ImGui.Selectable("對話框"))
            {
                var newNode = new TalkEntryNode { Enabled = false, TargetText = "Your text goes here" };
                C.TalkRootFolder.Children.Add(newNode);
                C.Save();
            }
            if (ImGui.Selectable("數值輸入"))
            {
                var newNode = new NumericsEntryNode { Enabled = false, Text = "Your text goes here" };
                C.NumericsRootFolder.Children.Add(newNode);
                C.Save();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawAddLastSeenPopup()
    {
        if (ImGui.BeginPopup("AddLastSeen"))
        {
            ImGui.Text("選擇類型並預覽：");

            if (ImGui.CollapsingHeader("是/否對話框"))
            {
                if (ImGui.Selectable(Service.Watcher.LastSeenDialogText))
                {
                    var newNode = new TextEntryNode
                    {
                        Enabled = false,
                        Text = Service.Watcher.LastSeenDialogText
                    };
                    C.RootFolder.Children.Add(newNode);
                    C.Save();
                }
            }

            if (ImGui.CollapsingHeader("確定對話框"))
            {
                if (ImGui.Selectable(Service.Watcher.LastSeenOkText))
                {
                    var newNode = new OkEntryNode
                    {
                        Enabled = false,
                        Text = Service.Watcher.LastSeenOkText
                    };
                    C.RootFolder.Children.Add(newNode);
                    C.Save();
                }
            }

            if (ImGui.CollapsingHeader("清單對話框"))
            {
                if (ImGui.Selectable(Service.Watcher.LastSeenListSelection))
                {
                    var newNode = new ListEntryNode
                    {
                        Enabled = false,
                        Text = Service.Watcher.LastSeenListSelection
                    };
                    C.RootFolder.Children.Add(newNode);
                    C.Save();
                }
            }

            if (ImGui.CollapsingHeader("對話框"))
            {
                if (ImGui.Selectable(Service.Watcher.LastSeenTalkTarget))
                {
                    var newNode = new TalkEntryNode
                    {
                        Enabled = false,
                        TargetText = Service.Watcher.LastSeenTalkTarget
                    };
                    C.RootFolder.Children.Add(newNode);
                    C.Save();
                }
            }

            if (ImGui.CollapsingHeader("數值對話框"))
            {
                if (ImGui.Selectable(Service.Watcher.LastSeenNumericsText))
                {
                    var newNode = new NumericsEntryNode
                    {
                        Enabled = false,
                        Text = Service.Watcher.LastSeenNumericsText
                    };
                    C.RootFolder.Children.Add(newNode);
                    C.Save();
                }
            }

            ImGui.EndPopup();
        }
    }

    private void DrawNodeList()
    {
        switch (CurrentViewMode)
        {
            case ViewMode.ByType:
                DrawByTypeView();
                break;
            case ViewMode.Alphabetical:
                DrawAlphabeticalView();
                break;
            case ViewMode.Folders:
                DrawFolderView();
                break;
        }
    }

    private void DrawByTypeView()
    {
        if (ImGui.CollapsingHeader("是/否對話框", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DisplayNodes(C.RootFolder, () => new TextEntryNode() { Enabled = false, Text = "Add some text here!" });
        }

        if (ImGui.CollapsingHeader("確定對話框", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DisplayNodes(C.OkRootFolder, () => new OkEntryNode() { Enabled = false, Text = "Add some text here!" });
        }

        if (ImGui.CollapsingHeader("清單對話框", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DisplayNodes(C.ListRootFolder, () => new ListEntryNode() { Enabled = false, Text = "Add some text here!" });
        }

        if (ImGui.CollapsingHeader("對話框（Talk）", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DisplayNodes(C.TalkRootFolder, () => new TalkEntryNode { Enabled = false, TargetText = "Your text goes here" });
        }

        if (ImGui.CollapsingHeader("數值對話框", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DisplayNodes(C.NumericsRootFolder, () => new NumericsEntryNode() { Enabled = false, Text = "Add some text here!" });
        }
    }

    private void DrawAlphabeticalView()
    {
        var nodes = GetAllNodes()
            .OrderBy(n => n.Name)
            .Where(FilterNode);

        foreach (var node in nodes)
        {
            MainWindow.DisplayTextNode(node, C.RootFolder);
        }
    }

    private void DrawFolderView()
    {
        DisplayNodes(C.RootFolder, () => new TextEntryNode() { Enabled = false, Text = "Add some text here!" });
    }

    private void DisplayNodes<T>(TextFolderNode root, Func<T> createNewNode) where T : ITextNode
    {
        MainWindow.TextNodeDragDrop(root);

        if (root.Children.Count == 0)
        {
            root.Children.Add(createNewNode());
            C.Save();
        }

        foreach (var node in root.Children.ToArray())
            MainWindow.DisplayTextNode(node, root);
    }

    private IEnumerable<ITextNode> GetAllNodes()
    {
        return C.RootFolder.Children
            .SelectMany(GetAllNodesRecursive);
    }

    private IEnumerable<ITextNode> GetAllNodesRecursive(ITextNode node)
    {
        return node is TextFolderNode folder ? folder.Children.SelectMany(GetAllNodesRecursive) : [node];
    }

    private bool FilterNode(ITextNode node)
    {
        if (!showEnabled && node.Enabled) return false;
        if (!showDisabled && !node.Enabled) return false;
        if (!showInvalid && node is IValidatable validatable && !validatable.IsValid) return false;
        if (!string.IsNullOrEmpty(searchFilter))
        {
            return node.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private string GetHelpText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("YesAlready 的核心功能：");
        sb.AppendLine();
        sb.AppendLine("檢視模式：");
        sb.AppendLine("  - 依類型：依對話框類型分組項目");
        sb.AppendLine("  - 依字母排序：簡單的字母排序清單");
        sb.AppendLine("  - 資料夾：目前的資料夾結構");
        sb.AppendLine();
        sb.AppendLine("功能：");
        sb.AppendLine("  - 新增：建立任意類型的新項目");
        sb.AppendLine("  - 新增最近出現：從最近看到的對話框新增");
        sb.AppendLine("  - 搜尋：依名稱或文字篩選項目");
        sb.AppendLine("  - 顯示/隱藏：切換已啟用/已停用/無效項目的顯示狀態");
        return sb.ToString();
    }
}
