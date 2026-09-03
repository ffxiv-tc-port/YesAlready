using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.GameHelpers;
using ECommons.LanguageHelpers;
using ECommons.Reflection;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using YesAlready.IPC;
using YesAlready.UI.Tabs;
using static YesAlready.UI.Tabs.CoreTab;

namespace YesAlready.UI;

internal class MainWindow : Window
{
    private static readonly CoreTab coreTab = new();

    public MainWindow() : base($"{Name} {P.GetType().Assembly.GetName().Version}###{Name}")
    {
        Size = new Vector2(525, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public static readonly ComparisonType[] ComparisonTypes =
    [
        ComparisonType.LessThan,
        ComparisonType.LessThanOrEqual,
        ComparisonType.GreaterThan,
        ComparisonType.GreaterThanOrEqual,
    ];

    private static ITextNode? DraggedNode;

    private static TextFolderNode YesNoRootFolder => C.RootFolder;
    private static TextFolderNode OkRootFolder => C.OkRootFolder;
    private static TextFolderNode ListRootFolder => C.ListRootFolder;
    private static TextFolderNode TalkRootFolder => C.TalkRootFolder;
    private static TextFolderNode NumericsRootFolder => C.NumericsRootFolder;

    // 🔴 Dalamud 的 Window.PreDraw/PostDraw 不是空實作:它們負責推/彈 ImGuiStyleVar.Alpha,
    // 也就是 Dalamud 視窗設定裡的「每視窗不透明度」。override 掉而不呼叫 base,
    // 使用者拉這個視窗的透明度就會**靜默無效** —— 沒有錯誤訊息,只是滑桿拉了沒反應。
    // ⚠️ 兩支必須成對:base.PreDraw 推的 style var 只會由 base.PostDraw 彈掉。
    // 自家的 PushStyleColor 夾在內側(push 在 base.PreDraw 之後、pop 在 base.PostDraw 之前),
    // 維持 LIFO 對稱。
    public override void PreDraw()
    {
        base.PreDraw();
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, 0);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor();
        base.PostDraw();
    }

    public override void Draw()
    {
        // 🔑 阻擋清單與壓制租約都會讓 YesAlready 停手，兩者都要顯示、
        // 「強制解除鎖定」也要兩邊一起清 —— 只清一邊的話按鈕按了沒反應，
        // 使用者會判定成「這個按鈕壞了」而不是「還有另一個外掛壓著」。
        if (Suppressed)
        {
            // 🔑 列上（紅字）放的是「被誰壓著」——「有沒有問題」必須在列上看得見；
            // 「每一筆還要多久才會自己解除」是「起疑才查」的資訊，放 tooltip 不佔版面。
            ECommons.ImGuiMethods.ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, "Yes Already function is paused because following plugins have requested it: ??".Loc(SuppressedBy() ?? ""));

            if (ImGui.IsItemHovered())
            {
                var details = SuppressionDetails();
                if (details.Length != 0)
                    ImGuiX.TextTooltip(string.Join("\n", details));
            }

            if (ImGui.Button("Force unlock".Loc()))
            {
                // 🔴 走 ForceClear 而不是直接 BlockList.Clear()：那條路徑不會寫 log，
                // 而清掉別人的阻擋登記是不可逆且對方不會察覺的破壞 —— 至少要留下誰被清掉的紀錄。
                Service.BlockListHandler.ForceClear("使用者在設定視窗按下強制解除鎖定");
                SuppressionLeases.ReleaseAll("使用者在設定視窗按下強制解除鎖定");
            }
        }

        var enabled = C.Enabled;
        if (ImGui.Checkbox("Enabled".Loc(), ref enabled))
        {
            C.Enabled = enabled;
            C.Save();
        }

        using var tabs = ImRaii.TabBar("Tabs");
        if (tabs)
        {
            //coreTab.Draw();
            DisplayGenericOptions("YesNo".Loc(), YesNo.DrawButtons, () => DisplayNodes(YesNoRootFolder, () => new TextEntryNode() { Enabled = false, Text = "Add some text here!" }));
            DisplayGenericOptions("Ok".Loc(), Ok.DrawButtons, () => DisplayNodes(OkRootFolder, () => new OkEntryNode() { Enabled = false, Text = "Add some text here!" }));
            DisplayGenericOptions("List".Loc(), Lists.DrawButtons, () => DisplayNodes(ListRootFolder, () => new ListEntryNode() { Enabled = false, Text = "Add some text here!" }));
            DisplayGenericOptions("Talk".Loc(), Talk.DrawButtons, () => DisplayNodes(TalkRootFolder, () => new TalkEntryNode { Enabled = false, TargetText = "Your text goes here" }));
            DisplayGenericOptions("Numerics".Loc(), Numerics.DrawButtons, () => DisplayNodes(NumericsRootFolder, () => new NumericsEntryNode() { Enabled = false, Text = "Add some text here!" }));
            Bothers.Draw();
            DisplayGenericOptions("Custom".Loc(), Custom.DrawButtons, () => DisplayNodes(C.CustomRootFolder, () => new CustomEntryNode() { Enabled = false, Text = "Add some text here!" }));
            DisplayMiscOptions();
            using (var tab = ImRaii.TabItem("Log".Loc()))
                if (tab)
                    InternalLog.PrintImgui();
        }
    }

    // ====================================================================================================

    private void DisplayGenericOptions(string tabName, Action displayButtons, Action displayNodes)
    {
        using var tab = ImRaii.TabItem(tabName);
        if (!tab) return;
        using var idScope = ImRaii.PushId($"{tabName}Options");
        displayButtons();
        displayNodes();
    }

    private readonly XivChatType? selectedChannel;
    private void DisplayMiscOptions()
    {
        using var tab = ImRaii.TabItem("Settings".Loc());
        if (!tab) return;
        using (ImRaii.PushId("Server info bar"))
        {
            try
            {
                var config = DalamudReflector.GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
                var dtrList = config.GetFoP<List<string>>("DtrIgnore");
                var enabled = !dtrList.Contains(Svc.PluginInterface.InternalName);
                if (ImGui.Checkbox("DTR", ref enabled))
                {
                    if (enabled)
                    {
                        dtrList.Remove(Svc.PluginInterface.InternalName);
                    }
                    else
                    {
                        dtrList.Add(Svc.PluginInterface.InternalName);
                    }
                    config.Call("QueueSave", []);
                }
                ImGuiX.IndentedTextColored("Display the status of the ?? in the Server Info Bar (DTR Bar). Clicking toggles the plugin.".Loc(Name));
            }
            catch (Exception e)
            {
                ECommons.ImGuiMethods.ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, $"{e}");
            }
        }

        using (var combo = ImRaii.Combo("###ChatChannelSelect", $"{Enum.GetName(typeof(XivChatType), C.MessageChannel)}"))
        {
            if (combo)
            {
                foreach (var type in Enum.GetValues<XivChatType>())
                {
                    using (ImRaii.PushId(type.ToString()))
                    {
                        var selected = ImGui.Selectable($"{type}", type == selectedChannel);

                        if (selected)
                        {
                            C.MessageChannel = type;
                            C.Save();
                        }
                    }
                }
            }
        }
        ImGuiX.IndentedTextColored("Select the chat channel for ?? messages to output to.".Loc(Name));

        ImGui.Separator();

        // 🔴 這一格是保險絲不是節流：真正處理「掛著鎖的外掛不在了」的是持有者存活檢查
        // （BlockListHandler，外掛被停用／卸載就立刻移除），這裡只涵蓋「還活著但漏放」。
        var blockListTimeout = C.BlockListEntryTimeoutMinutes;
        if (ImGui.InputInt("Block list timeout (minutes, 0 = off)".Loc(), ref blockListTimeout, 10, 30))
        {
            if (blockListTimeout < 0) blockListTimeout = 0;
            if (blockListTimeout > 1440) blockListTimeout = 1440;
            C.BlockListEntryTimeoutMinutes = blockListTimeout;
            C.Save();
        }
        ImGuiX.IndentedTextColored("How long another plugin may keep ?? paused before the entry is released automatically. Plugins that are no longer loaded are released immediately regardless of this setting.".Loc(Name));
    }

    // ====================================================================================================

    private void DisplayNodes<T>(TextFolderNode root, Func<T> createNewNode)
    {
        TextNodeDragDrop(root);

        if (root.Children.Count == 0)
        {
            root.Children.Add((ITextNode)createNewNode());
            C.Save();
        }

        foreach (var node in root.Children.ToArray())
            DisplayTextNode(node, root);
    }

    // ====================================================================================================

    public static void DisplayTextNode(ITextNode node, TextFolderNode rootNode)
    {
        if (node is TextFolderNode folderNode)
            DisplayFolderNode(folderNode, rootNode);
        else if (node is TextEntryNode textNode)
            YesNo.DisplayEntryNode(textNode);
        else if (node is OkEntryNode okNode)
            DisplayEntryNode<OkEntryNode>(okNode);
        else if (node is ListEntryNode listNode)
            DisplayEntryNode<ListEntryNode>(listNode);
        else if (node is TalkEntryNode talkNode)
            DisplayEntryNode<TalkEntryNode>(talkNode);
        else if (node is NumericsEntryNode numNode)
            DisplayEntryNode<NumericsEntryNode>(numNode);
        else if (node is CustomEntryNode customNode)
            DisplayEntryNode<CustomEntryNode>(customNode);
    }

    private static void DisplayEntryNode<T>(ITextNode node)
    {
        var validRegex = node.IsRegex && node.Regex != null || !node.IsRegex;

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
            ImGuiX.TextTooltip("Invalid Text Regex".Loc());

        if (ImGui.IsItemHovered())
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                node.Enabled = !node.Enabled;
                C.Save();
                if (node is CustomEntryNode)
                    Features.CustomAddonCallbacks.Toggle();
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
                        if (node is CustomEntryNode)
                            Features.CustomAddonCallbacks.Toggle();
                    }

                    return;
                }
                else
                {
                    ImGui.OpenPopup($"{node.GetHashCode()}-popup");
                }
            }
        }

        TextNodePopup(node);
        TextNodeDragDrop(node);
    }

    private static void DisplayFolderNode(TextFolderNode node, TextFolderNode root)
    {
        var expanded = ImGui.TreeNodeEx($"{node.Name}##{node.GetHashCode()}-tree");

        if (ImGui.IsItemHovered())
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
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

        TextNodePopup(node, root);
        TextNodeDragDrop(node);

        if (expanded)
        {
            foreach (var childNode in node.Children.ToArray())
                DisplayTextNode(childNode, root);

            ImGui.TreePop();
        }
    }

    public static void TextNodePopup(ITextNode node, TextFolderNode? root = null)
    {
        var style = ImGui.GetStyle();
        var spacing = new Vector2(style.ItemSpacing.X / 2, style.ItemSpacing.Y);

        if (ImGui.BeginPopup($"{node.GetHashCode()}-popup"))
        {
            if (node is TextEntryNode entryNode)
                YesNo.DrawPopup(entryNode, spacing);

            if (node is OkEntryNode okNode)
                Ok.DrawPopup(okNode, spacing);

            if (node is ListEntryNode listNode)
                Lists.DrawPopup(listNode, spacing);

            if (node is TalkEntryNode talkNode)
                Talk.DrawPopup(talkNode, spacing);

            if (node is NumericsEntryNode numNode)
                Numerics.DrawPopup(numNode, spacing);

            if (node is CustomEntryNode customNode)
                Custom.DrawPopup(customNode, spacing);

            if (node is TextFolderNode folderNode)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, spacing);

                if (ImGuiX.IconButton(FontAwesomeIcon.Plus, "Add entry".Loc()))
                {
                    if (root == YesNoRootFolder)
                    {
                        var newNode = new TextEntryNode { Enabled = false, Text = "Your text goes here" };
                        folderNode.Children.Add(newNode);
                    }
                    else if (root == ListRootFolder)
                    {
                        var newNode = new ListEntryNode { Enabled = false, Text = "Your text goes here" };
                        folderNode.Children.Add(newNode);
                    }

                    C.Save();
                }

                ImGui.SameLine();
                if (ImGuiX.IconButton(FontAwesomeIcon.SearchPlus, "Add last seen as new entry".Loc()))
                {
                    if (root == YesNoRootFolder)
                    {
                        var io = ImGui.GetIO();
                        var zoneRestricted = io.KeyCtrl;
                        var createFolder = io.KeyShift;
                        var selectNo = io.KeyAlt;

                        Configuration.CreateNode<TextEntryNode>(C.RootFolder, createFolder, zoneRestricted ? GenericHelpers.GetRow<Lumina.Excel.Sheets.TerritoryType>(Player.Territory)?.Name.ExtractText() : null, !selectNo);
                        C.Save();
                    }
                    else if (root == OkRootFolder || root == NumericsRootFolder)
                    {
                        var createFolder = ImGui.GetIO().KeyShift;
                        Configuration.CreateNode<OkEntryNode>(folderNode, createFolder);
                        C.Save();
                    }
                    else if (root == ListRootFolder)
                    {
                        var newNode = new ListEntryNode() { Enabled = true, Text = Service.Watcher.LastSeenListSelection, TargetRestricted = true, TargetText = Service.Watcher.LastSeenListTarget };
                        folderNode.Children.Add(newNode);
                        C.Save();
                    }
                    else if (root == TalkRootFolder)
                    {
                        var newNode = new TalkEntryNode() { Enabled = true, TargetText = Service.Watcher.LastSeenTalkTarget };
                        folderNode.Children.Add(newNode);
                        C.Save();
                    }
                }

                ImGui.SameLine();
                if (ImGuiX.IconButton(FontAwesomeIcon.FolderPlus, "Add folder".Loc()))
                {
                    var newNode = new TextFolderNode { Name = "Untitled folder" };
                    folderNode.Children.Add(newNode);
                    C.Save();
                }

                var trashWidth = ImGuiX.GetIconButtonWidth(FontAwesomeIcon.TrashAlt);
                ImGui.SameLine(ImGui.GetContentRegionMax().X - trashWidth);
                if (ImGuiX.IconButton(FontAwesomeIcon.TrashAlt, "Delete".Loc()))
                {
                    if (C.TryFindParent(node, out var parentNode))
                    {
                        parentNode!.Children.Remove(node);
                        C.Save();
                    }
                }

                ImGui.PopStyleVar(); // ItemSpacing

                var folderName = folderNode.Name;
                if (ImGui.InputText($"##{node.Name}-rename", ref folderName, 10_000, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    folderNode.Name = folderName;
                    C.Save();
                }
            }

            ImGui.EndPopup();
        }
    }

    public static string ComparisonTypeToText(ComparisonType comparisonType) => comparisonType switch
    {
        ComparisonType.LessThan => "Less than".Loc(),
        ComparisonType.LessThanOrEqual => "Less than or equal".Loc(),
        ComparisonType.GreaterThan => "Greater than".Loc(),
        ComparisonType.GreaterThanOrEqual => "Greater than or equal".Loc(),
        ComparisonType.Equal => "Equal".Loc(),
        _ => throw new Exception("Invalid enum value"),
    };

    public static void TextNodeDragDrop(ITextNode node)
    {
        // Don't allow drag and drop in Alphabetical or Folders view
        if (coreTab.CurrentViewMode != ViewMode.ByType)
            return;

        // Don't allow drag and drop for root folders
        if (node == YesNoRootFolder || node == ListRootFolder || node == TalkRootFolder)
            return;

        if (ImGui.BeginDragDropSource())
        {
            DraggedNode = node;

            ImGui.Text(node.Name);
            ImGui.SetDragDropPayload("TextNodePayload", default, 0);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("TextNodePayload");

            bool nullPtr;
            unsafe
            {
                nullPtr = payload.Handle == null;
            }

            var targetNode = node;
            if (!nullPtr && payload.IsDelivery() && DraggedNode != null)
            {
                // Get the root folders for both nodes
                var draggedRoot = GetRootFolder(DraggedNode);
                var targetRoot = GetRootFolder(targetNode);

                // Only allow drag and drop within the same root folder
                // Reject drops that would nest a folder under itself
                if (draggedRoot == targetRoot && !WouldCreateLoop(DraggedNode, targetNode))
                {
                    if (C.TryFindParent(DraggedNode, out var draggedNodeParent))
                    {
                        if (targetNode is TextFolderNode targetFolderNode)
                        {
                            draggedNodeParent!.Children.Remove(DraggedNode);
                            targetFolderNode.Children.Add(DraggedNode);
                            C.Save();
                        }
                        else
                        {
                            if (C.TryFindParent(targetNode, out var targetNodeParent))
                            {
                                var targetNodeIndex = targetNodeParent!.Children.IndexOf(targetNode);
                                if (targetNodeParent == draggedNodeParent)
                                {
                                    var draggedNodeIndex = targetNodeParent.Children.IndexOf(DraggedNode);
                                    if (draggedNodeIndex < targetNodeIndex)
                                    {
                                        targetNodeIndex -= 1;
                                    }
                                }

                                draggedNodeParent!.Children.Remove(DraggedNode);
                                targetNodeParent.Children.Insert(targetNodeIndex, DraggedNode);
                                C.Save();
                            }
                            else
                            {
                                throw new Exception($"Could not find parent of node \"{targetNode.Name}\"");
                            }
                        }
                    }
                    else
                    {
                        throw new Exception($"Could not find parent of node \"{DraggedNode.Name}\"");
                    }
                }

                DraggedNode = null;
            }

            ImGui.EndDragDropTarget();
        }
    }

    // moving a folder into itself or a descendant orphans the subtree from the root
    private static bool WouldCreateLoop(ITextNode dragged, ITextNode target)
    {
        if (dragged is not TextFolderNode draggedFolder)
            return false;

        if (ReferenceEquals(target, draggedFolder))
            return true;

        var current = target;
        while (C.TryFindParent(current, out var parent) && parent != null)
        {
            if (ReferenceEquals(parent, draggedFolder))
                return true;
            current = parent;
        }

        return false;
    }

    private static TextFolderNode GetRootFolder(ITextNode node)
    {
        if (node == YesNoRootFolder) return YesNoRootFolder;
        if (node == OkRootFolder) return OkRootFolder;
        if (node == ListRootFolder) return ListRootFolder;
        if (node == TalkRootFolder) return TalkRootFolder;
        if (node == NumericsRootFolder) return NumericsRootFolder;
        if (node == C.CustomRootFolder) return C.CustomRootFolder;

        var current = node;
        while (C.TryFindParent(current, out var parent))
        {
            if (parent == YesNoRootFolder) return YesNoRootFolder;
            if (parent == OkRootFolder) return OkRootFolder;
            if (parent == ListRootFolder) return ListRootFolder;
            if (parent == TalkRootFolder) return TalkRootFolder;
            if (parent == NumericsRootFolder) return NumericsRootFolder;
            if (parent == C.CustomRootFolder) return C.CustomRootFolder;
            current = parent;
        }

        throw new Exception($"Could not find root folder for node \"{node.Name}\"");
    }
}
