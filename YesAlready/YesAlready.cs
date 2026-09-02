using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using ECommons.EzDTR;
using ECommons.EzHookManager;
using ECommons.GameHelpers;
using ECommons.LanguageHelpers;
using ECommons.SimpleGui;
using ECommons.Singletons;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using YesAlready.Interface;
using YesAlready.UI;

namespace YesAlready;

public class YesAlready : IDalamudPlugin
{
    public static string Name => "YesAlready";
    public static YesAlready P { get; private set; } = null!;
    public static Configuration C { get; set; } = null!;

    private const string Command = "/yesalready";
    private readonly string[] Aliases = ["/pyes"];

    internal bool Active => C.Enabled && !Service.BlockListHandler.Locked;

    public YesAlready(IDalamudPluginInterface pluginInterface)
    {
        P = this;
        ECommonsMain.Init(pluginInterface, P);

        // 🔴 守衛的幀計數器要排在本外掛所有其他 Framework.Update 訂閱之前：同一個外掛內部的
        // Framework.Update 多播委派包在單一 try/catch 裡，排在前面的處理常式擲例外時，
        // 後面所有處理常式那個 tick 完全不會被呼叫 —— 時鐘停住＝守衛的逃生口失準。
        AddonPressGuard.EnsureWatching();

        ECommons.LanguageHelpers.Localization.Init("ChineseTraditional");

        C = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        C.Migrate();

        SingletonServiceManager.Initialize(typeof(Service));
        EzConfigGui.Init(new MainWindow().Draw);
        EzConfigGui.WindowSystem.AddWindow(new ZoneListWindow());
        EzConfigGui.WindowSystem.AddWindow(new ConditionsListWindow());

        EzCmd.Add(Command, OnCommand, "Opens the plugin window.".Loc(), int.MinValue);
        Aliases.Each(a => EzCmd.Add(a, OnCommand, $"{Command} alias"));

        // green dot = actively running, no-entry sign = off/paused - game icon font has no
        // spinner glyph, so this uses an IconPayload/BitmapFontIcon (bitmap icon) instead of
        // text, same technique as LazyLoot/WrathCombo's DTR entries; tooltip keeps the exact
        // state in words since the bar itself is icon-only now. EzDtr only auto-refreshes
        // .Text each frame, not .Tooltip, so the tooltip is updated as a side effect inside
        // the same per-frame Text callback (yesAlreadyDtr isn't invoked until the next
        // Framework.Update tick, well after this local is assigned, so the self-reference
        // inside its own initializer is safe). Arrow glyph identifies the entry as YesAlready
        // (BitmapFontIcon has no play/fast-forward icon in this API generation - SeIconChar's
        // smaller glyph set has ArrowRight instead, mixed into the same SeString as a
        // TextPayload alongside the IconPayload state icon).
        EzDtr yesAlreadyDtr = null!;
        yesAlreadyDtr = new EzDtr(() =>
        {
            yesAlreadyDtr.Entry!.Tooltip = new SeString(new TextPayload(
                $"{Name}: {(C.Enabled ? (Service.BlockListHandler.Locked ? "Paused".Loc() : "On".Loc()) : "Off".Loc())}"
                + "\n" + "Left click: toggle on/off".Loc()
                + "\n" + "Right click: open/close settings".Loc()));
            return new SeString(
                new TextPayload(SeIconChar.ArrowRight.ToIconString()),
                new IconPayload(Active ? BitmapFontIcon.GreenDot : BitmapFontIcon.NoCircle));
        });

        // ⚠️ 點擊處理不能交給 EzDtr 的 onClick 參數：那個型別是 `Action`（收不到事件），
        // 分不出左右鍵。而 EzDtr.OnUpdate 只在自己的 OnClick 非 null 時才每幀覆寫
        // Entry.OnClick——所以這裡傳 null、改成自己設，才不會被它蓋掉。
        yesAlreadyDtr.Entry!.OnClick = ev =>
        {
            // 右鍵開關設定視窗（再按一次關閉）；左鍵維持原本的啟用切換。
            if (ev.ClickType == MouseClickType.Right)
            {
                EzConfigGui.Toggle();
                return;
            }
            C.Enabled ^= true;
            C.Save();
        };

        LoadTerritories();
        ToggleFeatures(true);

        Svc.PluginInterface.UiBuilder.OpenMainUi += EzConfigGui.Toggle;
    }

    /// <summary>
    /// 外掛啟動時建起來、<b>實際掛著監聽器</b>的那一份功能實例。
    /// </summary>
    /// <remarks>
    /// 🔴 這份清單存在的理由:<see cref="BaseFeature.Enabled"/> 與 <c>AddonFeature._attributes</c>
    /// 都是<b>實例狀態</b>,而監聽器綁的是<b>某一個特定實例</b>的 <c>OnAddonEvent</c>。
    /// 以前這裡與 IPC 的查詢各自 <c>Activator.CreateInstance</c> 造一個全新的實例,
    /// 那個實例的 <c>Enabled</c> 恆為 <see langword="false"/>、<c>_attributes</c> 恆為
    /// <see langword="null"/> ⇒ 查詢恆回「沒啟用」、<c>Disable()</c> 拆不掉真正註冊的那一組、
    /// <c>Enable()</c> 反而再掛一組重複的監聽器(同一個 addon 事件被處理兩次)。
    /// </remarks>
    private static readonly List<BaseFeature> FeatureInstances = [];

    public static void ToggleFeatures(bool enable)
    {
        if (FeatureInstances.Count == 0)
        {
            var featureAssembly = Assembly.GetExecutingAssembly();

            foreach (var type in featureAssembly.GetTypes())
            {
                if (typeof(BaseFeature).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    if (Activator.CreateInstance(type) is BaseFeature feature)
                        FeatureInstances.Add(feature);
                }
            }
        }

        foreach (var feature in FeatureInstances)
        {
            if (enable)
                feature.Enable();
            else
                feature.Disable();
        }
    }

    /// <summary>照型別取得<b>實際註冊的</b>那一份功能實例;沒有就回 <see langword="null"/>。</summary>
    public static BaseFeature? FindFeature(Type type)
    {
        foreach (var feature in FeatureInstances)
            if (feature.GetType() == type)
                return feature;
        return null;
    }

    /// <summary>照名稱取得<b>實際註冊的</b>那一份功能實例(bother IPC 用)。</summary>
    /// <remarks>
    /// 先照 <see cref="Type.GetType(string)"/> 解 —— 那是這組 IPC 一開始的約定,名稱要含
    /// 命名空間(例如 <c>YesAlready.Features.Talk</c>);解不出來再照 <see cref="BaseFeature.Key"/>
    /// (＝類別簡名,例如 <c>Talk</c>)比對一次。
    /// ⚠️ 後面那條是<b>加法</b>:原本認得的名稱行為逐字不變,只是多認得簡名 —— 呼叫端
    /// (SomethingNeedDoing 把這三支 IPC 直接開給使用者的 Lua 巨集用)手上有的通常就是簡名。
    /// </remarks>
    public static BaseFeature? FindFeature(string name)
    {
        if (Type.GetType(name) is { } type && typeof(BaseFeature).IsAssignableFrom(type) && !type.IsAbstract
            && FindFeature(type) is { } byType)
            return byType;

        foreach (var feature in FeatureInstances)
            if (string.Equals(feature.Key, name, StringComparison.Ordinal))
                return feature;
        return null;
    }

    public T? GetFeature<T>() where T : BaseFeature => FindFeature(typeof(T)) as T;

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.OpenMainUi -= EzConfigGui.Toggle;
        AddonPressGuard.ForceTeardown();
        ECommonsMain.Dispose();
    }

    internal Dictionary<uint, string> TerritoryNames { get; private set; } = [];

    private void LoadTerritories()
        => TerritoryNames = GenericHelpers.FindRows<TerritoryType>(r => r.PlaceName.IsValid && !r.PlaceName.Value.Name.IsEmpty)
            .Select((r, n) => (r.RowId, PlaceName: r.PlaceName.Value.Name.ToString())).ToDictionary(t => t.RowId, t => t.PlaceName);

    #region Commands

    private void OnCommand(string command, string arguments)
    {
        if (arguments.IsNullOrEmpty())
        {
            EzConfigGui.Toggle();
            return;
        }

        switch (arguments)
        {
            case "help":
                CommandHelpMenu();
                break;
            case "toggle":
                C.Enabled ^= true;
                C.Save();
                break;
            case "last":
                CommandAddNode(false, false, false);
                break;
            case "last no":
                CommandAddNode(false, false, true);
                break;
            case "last zone":
                CommandAddNode(true, false, false);
                break;
            case "last zone no":
                CommandAddNode(true, false, true);
                break;
            case "last zone folder":
                CommandAddNode(true, true, false);
                break;
            case "last zone folder no":
                CommandAddNode(true, true, true);
                break;
            case "lastok":
                CommandAddOkNode(false);
                break;
            case "lastlist":
                CommandAddListNode();
                break;
            case "lasttalk":
                CommandAddTalkNode();
                break;
            case "dutyconfirm":
                ToggleDutyConfirm();
                break;
            case "onetimeconfirm":
                ToggleOneTimeConfirm();
                break;
            default:
                PluginLog.Error("I didn't quite understand that.");
                return;
        }
    }

    private static void CommandHelpMenu()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Help menu".Loc());
        sb.AppendLine($"{Command} - " + "Toggle the config window.".Loc());
        sb.AppendLine($"{Command} toggle - " + "Toggle the plugin on/off.".Loc());
        sb.AppendLine($"{Command} last - " + "Add the last seen YesNo dialog.".Loc());
        sb.AppendLine($"{Command} last no - " + "Add the last seen YesNo dialog as a no.".Loc());
        sb.AppendLine($"{Command} last zone - " + "Add the last seen YesNo dialog with the current zone name.".Loc());
        sb.AppendLine($"{Command} last zone no - " + "Add the last seen YesNo dialog with the current zone name as a no.".Loc());
        sb.AppendLine($"{Command} last zone folder - " + "Add the last seen YesNo dialog with the current zone name in a folder with the current zone name.".Loc());
        sb.AppendLine($"{Command} last zone folder no - " + "Add the last seen YesNo dialog with the current zone name in a folder with the current zone name as a no.".Loc());
        sb.AppendLine($"{Command} lastlist - " + "Add the last selected list dialog with the target at the time.".Loc());
        sb.AppendLine($"{Command} lasttalk - " + "Add the last seen target during a Talk dialog.".Loc());
        sb.AppendLine($"{Command} dutyconfirm - " + "Toggle duty confirm.".Loc());
        sb.AppendLine($"{Command} onetimeconfirm - " + "Toggles duty confirm as well as one-time confirm.".Loc());
        Svc.Chat.PrintPluginMessage(sb);
    }

    private void CommandAddNode(bool zoneRestricted, bool createFolder, bool selectNo)
    {
        var text = Service.Watcher.LastSeenDialogText;

        if (text.IsNullOrEmpty())
        {
            PluginLog.Error("No dialog has been seen.");
            return;
        }

        Configuration.CreateNode<TextEntryNode>(C.RootFolder, createFolder, zoneRestricted ? GenericHelpers.GetRow<TerritoryType>(Player.Territory)?.Name.ExtractText() : null, !selectNo);
        C.Save();

        Svc.Chat.PrintPluginMessage("Added a new text entry.".Loc());
    }

    private void CommandAddOkNode(bool createFolder)
    {
        var text = Service.Watcher.LastSeenOkText;

        if (text.IsNullOrEmpty())
        {
            PluginLog.Error("No dialog has been seen.");
            return;
        }

        Configuration.CreateNode<OkEntryNode>(C.RootFolder, createFolder);
        C.Save();

        Svc.Chat.PrintPluginMessage("Added a new text entry.".Loc());
    }

    private void CommandAddListNode()
    {
        var text = Service.Watcher.LastSeenListSelection;
        var target = Service.Watcher.LastSeenListTarget;

        if (text.IsNullOrEmpty())
        {
            PluginLog.Error("No dialog has been selected.");
            return;
        }

        var newNode = new ListEntryNode { Enabled = true, Text = text };

        if (!target.IsNullOrEmpty())
        {
            newNode.TargetRestricted = true;
            newNode.TargetText = target;
        }

        var parent = C.ListRootFolder;
        parent.Children.Add(newNode);
        C.Save();

        Svc.Chat.PrintPluginMessage("Added a new list entry.".Loc());
    }

    private void CommandAddTalkNode()
    {
        var target = Service.Watcher.LastSeenTalkTarget;

        if (target.IsNullOrEmpty())
        {
            PluginLog.Error("No talk dialog has been seen.");
            return;
        }

        var newNode = new TalkEntryNode { Enabled = true, TargetText = target };

        var parent = C.TalkRootFolder;
        parent.Children.Add(newNode);
        C.Save();

        Svc.Chat.PrintPluginMessage("Added a new talk entry.".Loc());
    }

    private void ToggleDutyConfirm()
    {
        C.ContentsFinderConfirmEnabled ^= true;
        C.ContentsFinderOneTimeConfirmEnabled = false;
        C.Save();

        var state = C.ContentsFinderConfirmEnabled ? "enabled".Loc() : "disabled".Loc();
        Svc.Chat.PrintPluginMessage("Duty Confirm ??.".Loc(state));
    }

    private void ToggleOneTimeConfirm()
    {
        C.ContentsFinderOneTimeConfirmEnabled ^= true;
        C.ContentsFinderConfirmEnabled = C.ContentsFinderOneTimeConfirmEnabled;
        C.Save();

        var state = C.ContentsFinderOneTimeConfirmEnabled ? "enabled".Loc() : "disabled".Loc();
        Svc.Chat.PrintPluginMessage("Duty Confirm and One Time Confirm ??.".Loc(state));
    }

    #endregion
}

