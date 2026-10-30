using System;
using System.Collections.Generic;

namespace YesAlready.Utils;

/// <summary>交易對象白名單的一筆：角色名＋原始伺服器（World 列號）。</summary>
/// <remarks>契約：<see cref="HomeWorld"/> 為 0 的條目永遠不命中（查不到伺服器時不按）。</remarks>
public class TradePartnerEntry
{
    public string Name { get; set; } = string.Empty;
    public uint HomeWorld { get; set; }
}

/// <summary>玩家交易自動確認的判定結果；只有 <see cref="Press"/> 會真的按。</summary>
internal enum TradeConfirmVerdict
{
    /// <summary>狀態不是「本機 5、對方 6」，或確認框／交易視窗還沒就緒。零值，必須是不按。</summary>
    NotReady = 0,

    /// <summary>限時沒開，白名單也沒中（或對方身分驗不過）。</summary>
    NotAuthorized = 1,

    /// <summary>插件被暫停、單項被暫停，或 AutoRetainer／Lifestream 正在忙。</summary>
    Yielding = 2,

    /// <summary>條件全成立，但「實際自動按」關著（紀錄模式）。</summary>
    WouldPress = 3,

    Press = 4,

    /// <summary>提出條件：對方提出條件後交易內容又變了，這次交給玩家自己按。</summary>
    ContentChanged = 5,

    /// <summary>提出條件：按鈕不在、停用、看不見，或文字不是「提出條件」。</summary>
    ButtonNotReady = 6,

    /// <summary>提出條件：玩家正在操作（數量輸入、右鍵選單、確認框開著）。</summary>
    UserBusy = 7,

    /// <summary>提出條件：對方提出條件、或交易內容最後一次變動，到現在還不滿 <see cref="TradeConfirmRules.OfferSettleMs"/>。</summary>
    Settling = 8,
}

/// <summary>第一段（交易視窗「提出條件」鈕）的判定輸入。</summary>
/// <param name="ContentChangedSinceRemoteOffer">對方提出條件之後，雙方任一邊的交易內容變過。</param>
/// <param name="StableMs">從「對方提出條件」與「交易內容最後一次變動」兩者較晚的那一刻起算的毫秒。</param>
/// <param name="AlreadyPressed">這次交易已經自動按過一次。</param>
internal readonly record struct TradeOfferInputs(
    int LocalState,
    int RemoteState,
    bool TradeWindowReady,
    bool AlreadyPressed,
    bool ContentChangedSinceRemoteOffer,
    bool OfferButtonReady,
    bool UserBusy,
    long StableMs,
    bool TimedActive,
    bool PartnerWhitelisted,
    bool PluginActive,
    bool Muted,
    bool OtherPluginBusy,
    bool PressEnabled,
    long SettleMs = TradeConfirmRules.OfferSettleMs);

internal readonly record struct TradeConfirmInputs(
    int LocalState,
    int RemoteState,
    bool ConfirmDialogMatched,
    bool TradeWindowReady,
    bool TimedActive,
    bool PartnerWhitelisted,
    bool PluginActive,
    bool Muted,
    bool OtherPluginBusy,
    bool PressEnabled);

internal enum TradeCommandKind
{
    Help = 0,
    Add = 1,
    Auto = 2,
    Off = 3,
    Invalid = 4,
}

/// <summary>玩家交易自動確認的純邏輯（不碰遊戲、不碰 Dalamud，離線測試直接編這個檔）。</summary>
internal static class TradeConfirmRules
{
    /// <summary>CS <c>TradeState.WaitingForConfirmation</c>：雙方都鎖定、確認框開著。</summary>
    public const int StateWaitingForConfirmation = 5;

    /// <summary>CS <c>TradeState.Confirmed</c>。</summary>
    public const int StateConfirmed = 6;

    /// <summary>CS <c>TradeState.SelectingTradeGoods</c>：還在挑物品、還沒按「提出條件」。</summary>
    public const int StateSelectingTradeGoods = 3;

    /// <summary>CS <c>TradeState.LockedIn</c>：按了「提出條件」、等另一方。</summary>
    public const int StateLockedIn = 4;

    /// <summary>第一段按之前，對方提出條件與交易內容都要維持不變這麼久（預設值；實際用設定 <c>TradeOfferSettleMs</c>）。</summary>
    public const long OfferSettleMs = 1000;

    public const int MaxOfferSettleMs = 5000;

    public const int DefaultTimedMinutes = 5;
    public const int MaxTimedMinutes = 60;

    /// <summary>
    /// 第一段判定順序：就緒 → 授權 → 內容變動 → 按鈕 → 玩家操作中 → 穩定時間 → 讓步 → 紀錄模式；任何一關不過都不按。
    /// </summary>
    public static TradeConfirmVerdict DecideOffer(in TradeOfferInputs i)
    {
        if (i.LocalState != StateSelectingTradeGoods || i.RemoteState != StateLockedIn
            || !i.TradeWindowReady || i.AlreadyPressed)
            return TradeConfirmVerdict.NotReady;
        if (!i.TimedActive && !i.PartnerWhitelisted)
            return TradeConfirmVerdict.NotAuthorized;
        if (i.ContentChangedSinceRemoteOffer)
            return TradeConfirmVerdict.ContentChanged;
        if (!i.OfferButtonReady)
            return TradeConfirmVerdict.ButtonNotReady;
        if (i.UserBusy)
            return TradeConfirmVerdict.UserBusy;
        if (i.StableMs < i.SettleMs)
            return TradeConfirmVerdict.Settling;
        if (!i.PluginActive || i.Muted || i.OtherPluginBusy)
            return TradeConfirmVerdict.Yielding;
        return i.PressEnabled ? TradeConfirmVerdict.Press : TradeConfirmVerdict.WouldPress;
    }

    /// <summary>
    /// 對方提出條件那一段的追蹤：<paramref name="since"/> 為 0＝不在這一段；進入時記下內容指紋，
    /// 之後指紋一變就把 <paramref name="changed"/> 設起來，直到離開這一段才清。
    /// </summary>
    public static void TrackRemoteOffer(bool inOffer, ulong content, long now, ref long since, ref ulong snapshot, ref bool changed)
    {
        if (!inOffer)
        {
            since = 0;
            changed = false;
            return;
        }

        if (since == 0)
        {
            since = now;
            snapshot = content;
            changed = false;
        }
        else if (content != snapshot)
        {
            changed = true;
        }
    }

    /// <summary>不在對方提出條件那一段回 0；否則從那一段開始與最後一次內容變動兩者較晚者起算。</summary>
    public static long StableMs(long remoteOfferSince, long lastContentChangeAt, long now)
        => remoteOfferSince == 0 ? 0 : now - Math.Max(remoteOfferSince, lastContentChangeAt);

    /// <summary>交易一格的內容指紋（道具、數量、旗標），12 格依序疊進同一個值。</summary>
    public static ulong MixContent(ulong acc, uint itemId, int quantity, uint flags)
    {
        unchecked
        {
            acc = (acc ^ itemId) * 0x100000001B3UL;
            acc = (acc ^ (uint)quantity) * 0x100000001B3UL;
            acc = (acc ^ flags) * 0x100000001B3UL;
            return acc;
        }
    }

    public const ulong ContentSeed = 0xCBF29CE484222325UL;

    /// <summary>判定順序：就緒 → 授權 → 讓步 → 紀錄模式；任何一關不過都不按。</summary>
    public static TradeConfirmVerdict Decide(in TradeConfirmInputs i)
    {
        if (i.LocalState != StateWaitingForConfirmation || i.RemoteState != StateConfirmed
            || !i.ConfirmDialogMatched || !i.TradeWindowReady)
            return TradeConfirmVerdict.NotReady;
        if (!i.TimedActive && !i.PartnerWhitelisted)
            return TradeConfirmVerdict.NotAuthorized;
        if (!i.PluginActive || i.Muted || i.OtherPluginBusy)
            return TradeConfirmVerdict.Yielding;
        return i.PressEnabled ? TradeConfirmVerdict.Press : TradeConfirmVerdict.WouldPress;
    }

    /// <summary>確認框文字是不是交易確認句：兩邊去掉換行後逐字相等；樣板句空的一律不算。</summary>
    public static bool IsTradeConfirmText(string? dialogText, string? templateText)
    {
        if (string.IsNullOrEmpty(templateText) || dialogText == null) return false;
        return string.Equals(dialogText.ReplaceLineEndings(string.Empty), templateText.ReplaceLineEndings(string.Empty), StringComparison.Ordinal);
    }

    /// <summary>
    /// 交易對象的身分：交易欄位記的名字要與物件表那位玩家的名字逐字相同，伺服器不能是 0；否則回 <see langword="null"/>。
    /// </summary>
    public static TradePartnerEntry? ValidatePartner(string? tradePartnerName, string? objectName, uint homeWorld)
    {
        if (string.IsNullOrEmpty(tradePartnerName) || string.IsNullOrEmpty(objectName) || homeWorld == 0) return null;
        if (!string.Equals(tradePartnerName, objectName, StringComparison.Ordinal)) return null;
        return new TradePartnerEntry { Name = objectName, HomeWorld = homeWorld };
    }

    public static bool IsWhitelisted(IEnumerable<TradePartnerEntry>? list, TradePartnerEntry? partner)
    {
        if (list == null || partner == null || partner.HomeWorld == 0 || string.IsNullOrEmpty(partner.Name)) return false;
        foreach (var e in list)
            if (e != null && e.HomeWorld == partner.HomeWorld && string.Equals(e.Name, partner.Name, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>加進名單；已存在或身分不完整回 <see langword="false"/>。</summary>
    public static bool TryAdd(List<TradePartnerEntry> list, TradePartnerEntry? partner)
    {
        if (partner == null || partner.HomeWorld == 0 || string.IsNullOrEmpty(partner.Name)) return false;
        if (IsWhitelisted(list, partner)) return false;
        list.Add(new TradePartnerEntry { Name = partner.Name, HomeWorld = partner.HomeWorld });
        return true;
    }

    public static int ClampMinutes(int minutes) => Math.Clamp(minutes, 1, MaxTimedMinutes);

    /// <summary>限時期限（<c>Environment.TickCount64</c> 座標）。0＝沒開。</summary>
    public static long Deadline(long nowMs, int minutes) => nowMs + ClampMinutes(minutes) * 60_000L;

    public static bool TimedActive(long deadlineMs, long nowMs) => deadlineMs != 0 && nowMs < deadlineMs;

    public static long RemainingMs(long deadlineMs, long nowMs) => TimedActive(deadlineMs, nowMs) ? deadlineMs - nowMs : 0;

    /// <summary>剩餘分鐘（無條件進位：剩 10 秒也顯示 1 分，不會在還有效時顯示 0）。</summary>
    public static int RemainingMinutesCeil(long deadlineMs, long nowMs) => MinutesCeil(RemainingMs(deadlineMs, nowMs));

    public static int MinutesCeil(long ms) => ms <= 0 ? 0 : (int)((ms + 59_999) / 60_000);

    /// <summary>解析 <c>trade</c> 之後的參數（不含 <c>trade</c> 本身）。<c>auto</c> 不帶數字＝預設分鐘數。</summary>
    public static TradeCommandKind ParseCommand(string? args, out int minutes)
    {
        minutes = 0;
        var parts = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return TradeCommandKind.Help;
        switch (parts[0].ToLowerInvariant())
        {
            case "add" when parts.Length == 1:
                return TradeCommandKind.Add;
            case "off" when parts.Length == 1:
                return TradeCommandKind.Off;
            case "auto" when parts.Length == 1:
                minutes = DefaultTimedMinutes;
                return TradeCommandKind.Auto;
            case "auto" when parts.Length == 2 && int.TryParse(parts[1], out var m) && m > 0:
                minutes = ClampMinutes(m);
                return TradeCommandKind.Auto;
            case "help" when parts.Length == 1:
                return TradeCommandKind.Help;
            default:
                return TradeCommandKind.Invalid;
        }
    }
}
