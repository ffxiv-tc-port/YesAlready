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
        // inside its own initializer is safe).
        EzDtr yesAlreadyDtr = null!;
        yesAlreadyDtr = new EzDtr(() =>
        {
            yesAlreadyDtr.Entry!.Tooltip = new SeString(new TextPayload($"{Name}: {(C.Enabled ? (Service.BlockListHandler.Locked ? "Paused".Loc() : "On".Loc()) : "Off".Loc())}"));
            return new SeString(new IconPayload(Active ? BitmapFontIcon.GreenDot : BitmapFontIcon.NoCircle));
        }, () => C.Enabled ^= true);

        LoadTerritories();
        ToggleFeatures(true);

        Svc.PluginInterface.UiBuilder.OpenMainUi += EzConfigGui.Toggle;
    }

    public static void ToggleFeatures(bool enable)
    {
        var featureAssembly = Assembly.GetExecutingAssembly();

        foreach (var type in featureAssembly.GetTypes())
        {
            if (typeof(BaseFeature).IsAssignableFrom(type) && !type.IsAbstract)
            {
                if (Activator.CreateInstance(type) is BaseFeature feature)
                {
                    if (enable)
                        feature.Enable();
                    else
                        feature.Disable();
                }
            }
        }
    }

    public T? GetFeature<T>() where T : BaseFeature
    {
        var type = typeof(T);

        if (!typeof(BaseFeature).IsAssignableFrom(type) || type.IsAbstract)
            return null;

        if (Activator.CreateInstance(type) is T feature)
            return feature;

        return null;
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.OpenMainUi -= EzConfigGui.Toggle;
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

