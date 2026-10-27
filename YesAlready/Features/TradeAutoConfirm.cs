using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons.LanguageHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;
using System.Text;
using YesAlready.IPC;

namespace YesAlready.Features;

/// <summary>玩家交易：雙方都鎖定、對方已確認時，替我方按交易確認框的「確定」。</summary>
/// <remarks>
/// 契約：每幀照名字重找確認框與交易視窗，位址只做等值比較、不跨幀存指標；只有「本機 5＋對方 6」那一幀才可能按，
/// 且要「限時有效」或「白名單開且對方在名單」，按前走 <see cref="AddonPressGuard"/>。
/// 刻意不掛 PostSetup：對方確認可能在確認框開出來之後才到，只能每幀輪詢。
/// 限時期限只放記憶體、不存檔：載入／登入不可能自動開。
/// </remarks>
internal unsafe class TradeAutoConfirm : BaseFeature
{
    private const string AddonSelectYesno = "SelectYesno";
    private const string AddonTrade = "Trade";
    private const uint TradeConfirmAddonRow = 102223;
    private const string Tag = "[TradeAutoConfirm]";

    private enum DialogState { None, NotReady, TradeConfirm, Other }
    private enum WindowState { None, NotReady, Ready }

    private static long timedDeadline;
    private static string? templateText;
    private static bool templateLoaded;
    private static ICallGateSubscriber<bool>? autoRetainerBusy;
    private static ICallGateSubscriber<bool>? lifestreamBusy;

    private bool subscribed;
    private bool faultLogged;
    private bool tracking;
    private long tradeStartedAt;
    private (int Local, int Remote, DialogState Dialog, WindowState Window) lastSnapshot;
    private string? lastDecision;

    public override void Enable()
    {
        base.Enable();
        if (subscribed) return;
        Svc.Framework.Update += OnUpdate;
        subscribed = true;
    }

    public override void Disable()
    {
        base.Disable();
        if (subscribed)
        {
            Svc.Framework.Update -= OnUpdate;
            subscribed = false;
        }
        StopTimed("功能停用");
        tracking = false;
    }

    #region 限時

    internal static long TimedRemainingMs => TradeConfirmRules.RemainingMs(timedDeadline, Environment.TickCount64);

    internal static void StartTimed(int minutes)
    {
        minutes = TradeConfirmRules.ClampMinutes(minutes);
        timedDeadline = TradeConfirmRules.Deadline(Environment.TickCount64, minutes);
        PluginLog.Information($"{Tag} 限時自動確認開始：{minutes} 分鐘。");
    }

    internal static void StopTimed(string reason)
    {
        if (timedDeadline == 0) return;
        timedDeadline = 0;
        PluginLog.Information($"{Tag} 限時自動確認結束（{reason}）。");
    }

    #endregion

    private void OnUpdate(IFramework framework)
    {
        // 同一插件的 Framework.Update 共用一個 try/catch：這裡擲出去會讓排在後面的處理常式整幀不跑。
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            if (faultLogged) return;
            faultLogged = true;
            PluginLog.Error($"{Tag} 每幀判斷擲出例外（之後同類錯誤不再重複記）：{ex}");
        }
    }

    private void Tick()
    {
        if (timedDeadline != 0)
        {
            if (!Svc.ClientState.IsLoggedIn) StopTimed("登出");
            else if (!TradeConfirmRules.TimedActive(timedDeadline, Environment.TickCount64)) StopTimed("時間到");
        }

        if (!C.TradeAutoConfirm)
        {
            tracking = false;
            return;
        }

        var im = InventoryManager.Instance();
        var local = im == null ? 0 : (int)im->TradeLocalState;
        var remote = im == null ? 0 : (int)im->TradeRemoteState;
        var now = Environment.TickCount64;

        if (local is < 2 or > 6)
        {
            if (tracking)
            {
                PluginLog.Information($"{Tag} 交易結束 本機={local} 對方={remote} 經過={now - tradeStartedAt}ms");
                tracking = false;
            }
            return;
        }

        if (!tracking)
        {
            tracking = true;
            tradeStartedAt = now;
            lastSnapshot = (-1, -1, DialogState.None, WindowState.None);
            lastDecision = null;
        }

        var dialog = ReadDialog(out var yesno, out var dialogText);
        var window = ReadTradeWindow();
        var snapshot = (local, remote, dialog, window);
        if (snapshot != lastSnapshot)
        {
            lastSnapshot = snapshot;
            PluginLog.Information($"{Tag} 狀態 本機={local} 對方={remote} 確認框={DescribeDialog(dialog, dialogText)} 交易視窗={DescribeWindow(window)} 經過={now - tradeStartedAt}ms");
        }

        var inputs = new TradeConfirmInputs(local, remote, dialog == DialogState.TradeConfirm, window == WindowState.Ready,
            false, false, false, false, false, false);
        if (TradeConfirmRules.Decide(inputs) == TradeConfirmVerdict.NotReady)
        {
            lastDecision = null;
            return;
        }

        var timed = TradeConfirmRules.TimedActive(timedDeadline, now);
        string? partnerDetail = null;
        var whitelisted = !timed && C.TradeWhitelistEnabled
            && TradeConfirmRules.IsWhitelisted(C.TradeWhitelist, ResolvePartner(im, out partnerDetail));
        string? busyDetail = null;
        var busy = (timed || whitelisted) && OtherPluginBusy(out busyDetail);
        inputs = inputs with
        {
            TimedActive = timed,
            PartnerWhitelisted = whitelisted,
            PluginActive = P.Active,
            Muted = BotherPauses.IsMuted(Key) || BotherPauses.IsMuted(AddonSelectYesno),
            OtherPluginBusy = busy,
            PressEnabled = C.TradeAutoConfirmPress,
        };
        var verdict = TradeConfirmRules.Decide(inputs);
        var basis = timed ? "限時" : "白名單";

        var decision = verdict switch
        {
            TradeConfirmVerdict.NotAuthorized => C.TradeWhitelistEnabled
                ? $"不按：限時未開，白名單沒中（{partnerDetail}）"
                : "不按：限時未開，白名單未開",
            TradeConfirmVerdict.Yielding => $"不按：讓步（插件啟用={inputs.PluginActive} 單項暫停={inputs.Muted} {busyDetail ?? "其他插件不忙"}）",
            TradeConfirmVerdict.WouldPress => $"本來會按（依據={basis}），但「實際自動按」關著（紀錄模式）",
            TradeConfirmVerdict.Press => $"按（依據={basis}）",
            _ => verdict.ToString(),
        };
        if (!string.Equals(decision, lastDecision, StringComparison.Ordinal))
        {
            lastDecision = decision;
            PluginLog.Information($"{Tag} 判定 {decision} 經過={now - tradeStartedAt}ms");
        }

        if (verdict != TradeConfirmVerdict.Press) return;
        if (!AddonPressGuard.TryBeginPress(AddonSelectYesno, yesno)) return;
        new AddonMaster.SelectYesno(yesno).Yes();
        PluginLog.Information($"{Tag} 已按「確定」 本機={local} 對方={remote} 依據={basis} 經過={now - tradeStartedAt}ms");
    }

    private static DialogState ReadDialog(out AtkUnitBase* yesno, out string? text)
    {
        text = null;
        if (!GenericHelpers.TryGetAddonByName(AddonSelectYesno, out yesno) || yesno == null)
        {
            yesno = null;
            return DialogState.None;
        }

        if (!GenericHelpers.IsAddonReady(yesno)) return DialogState.NotReady;
        text = new AddonMaster.SelectYesno(yesno).Text;
        return TradeConfirmRules.IsTradeConfirmText(text, TemplateText) ? DialogState.TradeConfirm : DialogState.Other;
    }

    private static WindowState ReadTradeWindow()
    {
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AddonTrade, out var trade) || trade == null) return WindowState.None;
        return GenericHelpers.IsAddonReady(trade) ? WindowState.Ready : WindowState.NotReady;
    }

    private static string DescribeDialog(DialogState state, string? text) => state switch
    {
        DialogState.None => "無",
        DialogState.NotReady => "未就緒",
        DialogState.TradeConfirm => "交易確認",
        _ => $"其他「{text?.ReplaceLineEndings(" ")}」",
    };

    private static string DescribeWindow(WindowState state) => state switch
    {
        WindowState.None => "無",
        WindowState.NotReady => "未就緒",
        _ => "就緒",
    };

    /// <summary>Addon 表的交易確認句；讀不到時回 <see langword="null"/>（之後的比對一律不中＝不按）。</summary>
    private static string? TemplateText
    {
        get
        {
            if (templateLoaded) return templateText;
            templateLoaded = true;
            templateText = GenericHelpers.GetRow<Addon>(TradeConfirmAddonRow)?.Text.GetText();
            if (string.IsNullOrEmpty(templateText))
                PluginLog.Warning($"{Tag} 讀不到 Addon#{TradeConfirmAddonRow} 的交易確認句，交易自動確認不會動作。");
            return templateText;
        }
    }

    /// <summary>目前交易對象的身分；交易欄位的名字與物件表那位玩家對不上、查不到或 World 為 0 都回 <see langword="null"/>。</summary>
    /// <remarks>物件只在這一幀內讀，不保存。</remarks>
    private static TradePartnerEntry? ResolvePartner(InventoryManager* im, out string detail)
    {
        if (im == null)
        {
            detail = "讀不到交易資料";
            return null;
        }

        var id = im->TradePartnerEntityId;
        var tradeName = im->TradePartnerNameString;
        if (id is 0 or 0xE0000000 || Svc.Objects.SearchByEntityId(id) is not IPlayerCharacter pc)
        {
            detail = $"交易欄位名字={tradeName} 物件表查不到這位玩家";
            return null;
        }

        var name = pc.Name.TextValue;
        var world = pc.HomeWorld.RowId;
        var partner = TradeConfirmRules.ValidatePartner(tradeName, name, world);
        detail = partner == null
            ? $"身分驗不過：交易欄位名字={tradeName} 物件名字={name} World={world}"
            : $"對方={name} World={world}";
        return partner;
    }

    /// <summary>AutoRetainer／Lifestream 正在忙就讓開；問不出答案也當作忙（不按）。</summary>
    private static bool OtherPluginBusy(out string? detail)
    {
        autoRetainerBusy ??= Svc.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.IsBusy");
        lifestreamBusy ??= Svc.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        return QueryBusy(autoRetainerBusy, "AutoRetainer", out detail) || QueryBusy(lifestreamBusy, "Lifestream", out detail);
    }

    private static bool QueryBusy(ICallGateSubscriber<bool> endpoint, string name, out string? detail)
    {
        detail = null;
        try
        {
            if (!endpoint.HasFunction || !endpoint.InvokeFunc()) return false;
            detail = $"{name} 忙碌中";
            return true;
        }
        catch (Exception ex)
        {
            detail = $"{name} 問不出是否忙碌（{ex.GetType().Name}），當作忙碌";
            return true;
        }
    }

    #region 指令與名單

    /// <summary>可加進白名單的人：交易中＝目前交易對象（驗不過就不加，不退回選取目標）；否則＝選取中的玩家。</summary>
    internal static TradePartnerEntry? FindAddCandidate(out string failure)
    {
        failure = "Trade: no trade partner or targeted player to add. The player must be nearby, and the home world must be readable.".Loc();
        var im = InventoryManager.Instance();
        var local = im == null ? 0 : (int)im->TradeLocalState;
        if (im != null && local is >= 2 and <= 6 && im->TradePartnerEntityId != 0)
        {
            var partner = ResolvePartner(im, out var detail);
            if (partner == null)
            {
                failure = "Trade: you are trading but the partner could not be verified (name mismatch or home world unknown); nothing added.".Loc();
                PluginLog.Information($"{Tag} 加入白名單失敗：{detail}");
            }
            return partner;
        }

        if (Svc.Targets.Target is IPlayerCharacter target)
        {
            var name = target.Name.TextValue;
            var world = target.HomeWorld.RowId;
            return TradeConfirmRules.ValidatePartner(name, name, world);
        }

        return null;
    }

    internal static void AddCandidate()
    {
        var partner = FindAddCandidate(out var failure);
        if (partner == null)
        {
            Svc.Chat.PrintPluginMessage(failure);
            return;
        }

        var world = WorldName(partner.HomeWorld);
        if (!TradeConfirmRules.TryAdd(C.TradeWhitelist, partner))
        {
            Svc.Chat.PrintPluginMessage("Trade: ?? (??) is already on the whitelist.".Loc(partner.Name, world));
            return;
        }

        C.Save();
        PluginLog.Information($"{Tag} 白名單加入 {partner.Name}（World {partner.HomeWorld}）");
        Svc.Chat.PrintPluginMessage("Trade: added ?? (??) to the auto-confirm whitelist.".Loc(partner.Name, world));
        PrintModeNotes();
    }

    internal static string WorldName(uint world)
        => GenericHelpers.GetRow<World>(world)?.Name.GetText() is { Length: > 0 } name ? name : "World #??".Loc(world);

    internal static void HandleCommand(string arguments)
    {
        switch (TradeConfirmRules.ParseCommand(arguments, out var minutes))
        {
            case TradeCommandKind.Add:
                AddCandidate();
                break;
            case TradeCommandKind.Auto:
                StartTimed(minutes);
                Svc.Chat.PrintPluginMessage("Trade: auto-confirm for ?? minutes (until ??).".Loc(minutes, DateTime.Now.AddMinutes(minutes).ToString("HH:mm")));
                PrintModeNotes();
                break;
            case TradeCommandKind.Off:
                if (timedDeadline == 0)
                    Svc.Chat.PrintPluginMessage("Trade: timed auto-confirm was not running.".Loc());
                else
                {
                    StopTimed("指令關閉");
                    Svc.Chat.PrintPluginMessage("Trade: timed auto-confirm stopped.".Loc());
                }
                break;
            default:
                Svc.Chat.PrintPluginMessage(HelpText());
                break;
        }
    }

    internal static string HelpText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("/yesalready trade add - " + "Add the current trade partner (or targeted player) to the trade whitelist.".Loc());
        sb.AppendLine("/yesalready trade auto [minutes] - " + "Auto-confirm trades for a while (default ??, max ?? minutes).".Loc(TradeConfirmRules.DefaultTimedMinutes, TradeConfirmRules.MaxTimedMinutes));
        sb.Append("/yesalready trade off - " + "Stop timed auto-confirm.".Loc());
        return sb.ToString();
    }

    /// <summary>功能或「實際自動按」沒開時提醒一句，免得使用者以為已經會自動確認。</summary>
    private static void PrintModeNotes()
    {
        if (!C.TradeAutoConfirm)
            Svc.Chat.PrintPluginMessage("Note: player trade auto-confirm is off in settings, so nothing will happen.".Loc());
        else if (!C.TradeAutoConfirmPress)
            Svc.Chat.PrintPluginMessage("Note: \"Actually press\" is off, so this only writes to the log (log mode).".Loc());
    }

    #endregion
}
