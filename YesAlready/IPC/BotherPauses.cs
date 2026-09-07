using Dalamud.Plugin.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace YesAlready.IPC;

/// <summary>
/// 單一 bother（<see cref="BaseFeature"/>）的<b>暫時關閉</b>登記處：以功能名稱記一個到期時刻，
/// 時間到自己醒過來，全程不碰監聽器的註冊狀態。
/// </summary>
/// <remarks>
/// 🔴🔴 <b>存在的理由＝舊的 <c>PauseBother</c> 把「等一段時間再開回來」排進了整個外掛共用的
/// 那條任務佇列。</b>舊實作是 <c>feature.Disable()</c> ＋
/// <c>Service.TaskManager.EnqueueDelay(ms)</c> ＋ <c>Service.TaskManager.Enqueue(開回來)</c>，
/// 三個各自獨立的缺陷：
/// <list type="number">
/// <item><b>那條佇列不是給 IPC 用的，是功能自己的自動化序列在用的。</b>
/// <c>Service.TaskManager</c> 同時被 <c>SatisfactionSupply</c>、<c>RetainerTaskAsk</c>、
/// <c>RetainerTaskResult</c>、<c>MateriaAttachDialog</c> 拿來排「按下一顆按鈕」的步驟。
/// 一次 <c>PauseBother(name, 60000)</c> 會在那條佇列中間插進 60 秒的空等，
/// <b>把交納／派遣／攻魔的按鈕序列整整卡住一分鐘</b>。</item>
/// <item>🔴 反過來，<c>SatisfactionSupply</c> 有兩處 <c>Service.TaskManager.Abort()</c>，
/// 而 <c>Abort()</c> 的第一件事是 <c>Tasks.Clear()</c> ⇒ <b>還沒輪到的那筆「開回來」被一起清掉</b>，
/// 這個 bother 就<b>永遠</b>關著，直到使用者重載外掛。全程零訊息。</item>
/// <item><c>TaskManager.Tasks</c> 是裸 <c>List&lt;TaskManagerTask&gt;</c>、<c>EnqueueMulti</c> 零同步，
/// 而 IPC 端點跑在<b>呼叫端的執行緒</b>上（SomethingNeedDoing 把這支直接開給使用者的 Lua 巨集，
/// 那不在主執行緒），與 framework 執行緒的 <c>Tasks[0]</c>／<c>RemoveAt(0)</c> 並行 ⇒
/// 失敗形式不是「慢一拍」而是<b>那個 List 本身壞掉</b>，連帶弄壞上面那四個功能的按鈕序列。</item>
/// </list>
/// <para>
/// 🔑 現在的做法與 <see cref="SuppressionLeases"/> 同形：<b>只記一個到期時刻</b>，由讀取端
/// 每次自己判「現在算不算被壓著」。沒有計時器、沒有佇列、沒有跨執行緒的監聽器增刪；
/// 呼叫端當掉也只是等到期。
/// </para>
/// <para>
/// 🔴 <b>鎖內絕不寫 log、絕不做檔案 I/O、絕不呼叫 ImGui</b>（同 <see cref="SuppressionLeases"/>）：
/// 逾時與夾值訊息在鎖內先收進一個 list，出了鎖才送出去。
/// </para>
/// </remarks>
internal static class BotherPauses
{
    private static readonly object Gate = new();

    /// <summary>功能名稱（<see cref="BaseFeature.Key"/>）→ <see cref="Environment.TickCount64"/> 座標的到期時刻。</summary>
    private static readonly Dictionary<string, long> PausedUntil = new(StringComparer.Ordinal);

    /// <summary>
    /// 「現在一筆暫停都沒有」的不上鎖快路。
    /// </summary>
    /// <remarks>
    /// 🔴 只用來<b>提早否定</b>：<see langword="false"/> 一定代表沒有暫停（清空一定在設它之前），
    /// <see langword="true"/> 只代表「可能有」，還是要進鎖裡掃過期。
    /// 這條路徑是<b>每個 addon 事件</b>都會走到的，所以常態必須零配置、零上鎖。
    /// </remarks>
    private static volatile bool anyPauses;

    /// <summary>
    /// 還沒套用的 <c>SetBotherEnabled</c> 請求：功能名稱 → 呼叫端要求的狀態。
    /// </summary>
    /// <remarks>
    /// 🔴🔴 <b>為什麼要有這一層。</b><c>BaseFeature.Enable()</c>／<c>Disable()</c> 會呼叫
    /// <c>Svc.AddonLifecycle.RegisterListener</c>／<c>UnregisterListener</c>，而 Dalamud 的
    /// <c>AddonLifecyclePluginScoped.eventListeners</c> 是<b>裸 <c>List</c></b>：
    /// <c>RegisterListener</c> 直接 <c>Add</c>、<c>UnregisterListener</c> 直接 <c>RemoveAll</c>，
    /// 兩支都<b>零同步</b>（本 pin 的 <c>Dalamud/Game/Addon/Lifecycle/AddonLifecycle.cs</c>）。
    /// IPC 端點跑在呼叫端的執行緒上 ⇒ Lua 巨集呼叫 <c>SetBotherEnabled</c> 的同時，
    /// 使用者在設定視窗按下自訂回呼的開關（<c>CustomAddonCallbacks.Toggle()</c>，繪製執行緒）
    /// 就會與它並行增刪同一個 List。
    /// <para>
    /// 🔑 所以請求先記在這裡，真正的 <c>TrySetEnabled</c> 由 <see cref="Drain"/> 在 framework
    /// 執行緒上套用。<b>而讀取端一律看「有效狀態」</b>（<see cref="EffectiveEnabled"/>／
    /// <see cref="IsMuted"/>）⇒ 呼叫端<b>當下</b>就看得到自己寫進去的值，也<b>當下</b>就不再被
    /// 接手，中間那一畫格的落差在外面完全觀察不到。
    /// </para>
    /// <para>
    /// 📌 <b>同一個功能的重複請求會被合併成「最後一次的意思」</b>——這是刻意的：
    /// <c>TrySetEnabled</c> 本來就是冪等的，中途狀態沒有意義。
    /// </para>
    /// </remarks>
    private static readonly ConcurrentDictionary<string, bool> PendingStates = new(StringComparer.Ordinal);

    /// <summary>目前有沒有任何暫停或未套用的請求（給 UI 判斷要不要組字串用）。</summary>
    public static bool Any => anyPauses || !PendingStates.IsEmpty;

    #region 暫停

    /// <summary>
    /// 把某個 bother 暫停到 <c>max(現有到期時刻, 現在 + milliseconds)</c>。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>取 max 不取序列</b>：兩個呼叫端同時暫停同一個 bother 時，先結束的那個不會把
    /// 後者談好的到期時間往前搬。舊實作在這裡是 FIFO 的空等，A 排 5 秒、B 排 60 秒會變成
    /// 「B 的 60 秒從第 5 秒才開始算」，而且中間那 65 秒整條共用佇列都被卡住。
    /// <para>
    /// ⚠️ 租期會被夾到 <see cref="SuppressionLeases.MaxLeaseMilliseconds"/>（與 <c>PausePlugin</c>
    /// 同一套時間政策），<b>真的夾到時會寫一次 <c>Information</c></b>——這條路沒有續約管道，
    /// 時間一到 bother 就恢復接手，那件事必須在 log 上看得見。
    /// </para>
    /// </remarks>
    public static void Pause(string key, int milliseconds)
    {
        List<string>? logs = null;
        var duration = ClampDuration(milliseconds, key, ref logs);
        var until = Environment.TickCount64 + duration;
        bool alreadyLonger;

        lock (Gate)
        {
            SweepLocked(ref logs);

            alreadyLonger = PausedUntil.TryGetValue(key, out var existing) && existing >= until;
            if (!alreadyLonger) PausedUntil[key] = until;
            anyPauses = true;
        }

        Flush(logs);
        if (alreadyLonger) return;

        PluginLog.Information($"[BotherPause] 「{key}」暫停 {duration} 毫秒。");
    }

    /// <summary>這個 bother 現在是不是正在暫停中。</summary>
    public static bool IsPaused(string key)
    {
        if (!anyPauses) return false;

        List<string>? logs = null;
        bool paused;

        lock (Gate)
        {
            SweepLocked(ref logs);
            paused = PausedUntil.ContainsKey(key);
        }

        Flush(logs);
        return paused;
    }

    /// <summary>
    /// 這個 bother 現在<b>應不應該讓開</b>：正在暫停，或有一筆還沒套用的停用請求。
    /// </summary>
    /// <remarks>
    /// 🔑 這支就是實際的閘門，掛在 <c>AddonFeature.OnAddonEvent</c>、
    /// <c>TextMatchingFeature.OnRetryTick</c> 與 <c>CustomAddonCallbacks.AddonSetup</c> 上。
    /// 每個 addon 事件都會走到，所以常態（沒有任何暫停也沒有待套用請求）是兩個欄位讀取。
    /// </remarks>
    public static bool IsMuted(string key)
    {
        if (!anyPauses)
            return !PendingStates.IsEmpty && PendingStates.TryGetValue(key, out var pending) && !pending;

        if (PendingStates.TryGetValue(key, out var state) && !state) return true;
        return IsPaused(key);
    }

    /// <summary>取消某個 bother 的暫停（<c>SetBotherEnabled(name, true)</c> 用）。</summary>
    /// <remarks>
    /// 📌 <b>這是為了保住舊行為。</b>舊實作的暫停就是 <c>Disable()</c>，所以呼叫端在暫停期間
    /// 呼叫 <c>SetBotherEnabled(name, true)</c> 會直接把它開回來、等於提早結束暫停。
    /// 現在暫停與啟用狀態是兩層，所以要在這裡明確把那一層也清掉。
    /// </remarks>
    public static void Clear(string key)
    {
        if (!anyPauses) return;

        bool removed;
        lock (Gate)
        {
            removed = PausedUntil.Remove(key);
            if (PausedUntil.Count == 0) anyPauses = false;
        }

        if (removed)
            PluginLog.Information($"[BotherPause] 「{key}」的暫停被提早解除。");
    }

    /// <summary>
    /// 目前每一筆暫停的診斷快照：功能名稱 ＋ 距離自動解除還有多久（毫秒）。
    /// </summary>
    /// <remarks>⚠️ 只給 UI／tooltip 用（會配置陣列），呼叫前先判 <see cref="Any"/>。</remarks>
    public static (string Key, long RemainingMs)[] Snapshot()
    {
        if (!anyPauses) return [];

        List<string>? logs = null;
        (string, long)[] result;

        lock (Gate)
        {
            SweepLocked(ref logs);
            var now = Environment.TickCount64;
            result = PausedUntil.Count == 0
                ? []
                : PausedUntil.Select(x => (x.Key, Math.Max(0, x.Value - now))).ToArray();
        }

        Flush(logs);
        return result;
    }

    #endregion

    #region 待套用的啟用／停用請求

    /// <summary>
    /// 記下一筆 <c>SetBotherEnabled</c> 請求；真正的切換由 <see cref="Drain"/> 在 framework
    /// 執行緒上做。<paramref name="state"/> 為 <see langword="true"/> 時順便解除暫停。
    /// </summary>
    public static void RequestSetEnabled(string key, bool state)
    {
        if (state) Clear(key);
        PendingStates[key] = state;
    }

    /// <summary>
    /// 這個功能<b>從外面看起來</b>啟用著沒有：有待套用的請求就以請求為準，否則看實例狀態；
    /// 正在暫停一律算沒啟用。
    /// </summary>
    /// <remarks>
    /// 📌 「正在暫停算沒啟用」是刻意保住舊行為：舊實作的暫停就是 <c>Disable()</c>，
    /// 所以 <c>IsBotherEnabled</c> 在暫停期間本來就回 <see langword="false"/>。
    /// 這與 <c>IsPluginEnabled</c>（複合值）／<c>IsUserEnabled</c>（單一格）的分工是同一套。
    /// </remarks>
    public static bool EffectiveEnabled(BaseFeature feature)
        => EnabledIgnoringPause(feature) && !IsPaused(feature.Key);

    /// <summary>
    /// 同 <see cref="EffectiveEnabled"/>，但<b>不看暫停那一層</b>——問的是「這個功能有沒有被關掉」。
    /// </summary>
    /// <remarks>
    /// 🔑 <c>PauseBother</c> 用這支判「值不值得暫停」：用 <see cref="EffectiveEnabled"/> 的話，
    /// 第二個呼叫端在暫停期間打進來會拿到 <see langword="false"/>、暫停也不會被延長 ——
    /// 那正是這組改動要消滅的重疊情境。
    /// </remarks>
    public static bool EnabledIgnoringPause(BaseFeature feature)
        => PendingStates.TryGetValue(feature.Key, out var pending) ? pending : feature.Enabled;

    #endregion

    #region 生命週期

    private static bool watching;

    /// <summary>開始在 framework 執行緒上套用待處理的請求。</summary>
    /// <remarks>
    /// ⚠️ 要排在 <c>AddonPressGuard.EnsureWatching()</c> <b>之後</b>：同一個外掛內部的
    /// <c>Framework.Update</c> 多播委派包在單一 try/catch 裡，排在前面的處理常式擲例外時，
    /// 後面所有處理常式那個 tick 完全不會被呼叫，而守衛的時鐘不能停。
    /// </remarks>
    public static void EnsureWatching()
    {
        if (watching) return;
        Svc.Framework.Update += Drain;
        watching = true;
    }

    /// <summary>把所有暫停與待套用請求丟掉（外掛卸載）。</summary>
    /// <remarks>
    /// 🔴 這是<b>行程內的靜態狀態</b>：外掛被停用／重載時一定要清掉，否則重載之後
    /// 舊的暫停還壓著，而當初要求它的呼叫端早就不知道有這回事了。
    /// </remarks>
    public static void ReleaseAll(string reason)
    {
        if (watching)
        {
            Svc.Framework.Update -= Drain;
            watching = false;
        }

        PendingStates.Clear();

        string[] keys;
        lock (Gate)
        {
            if (PausedUntil.Count == 0)
            {
                anyPauses = false;
                return;
            }

            keys = [.. PausedUntil.Keys];
            PausedUntil.Clear();
            anyPauses = false;
        }

        PluginLog.Information($"[BotherPause] 丟掉全部 bother 暫停（{reason}）：{string.Join("、", keys)}。");
    }

    /// <summary>在 framework 執行緒上套用待處理的 <c>SetBotherEnabled</c> 請求。</summary>
    /// <remarks>
    /// 🔴 <b>先套用再移除</b>：反過來寫的話，「移除」與「套用」之間有一個讀取端會看到舊實例
    /// 狀態的空窗。而移除時比對值（<c>TryRemove(KeyValuePair)</c>）⇒ 套用途中進來的新請求
    /// 不會被誤刪，下一畫格照樣會被處理。
    /// </remarks>
    private static void Drain(IFramework framework)
    {
        if (PendingStates.IsEmpty) return;

        foreach (var key in PendingStates.Keys)
        {
            if (!PendingStates.TryGetValue(key, out var state)) continue;

            FindFeature(key)?.TrySetEnabled(state);
            PendingStates.TryRemove(new KeyValuePair<string, bool>(key, state));
        }
    }

    #endregion

    #region 內部

    /// <summary>已經回報過「暫停時間被夾值」的功能名稱。<b>同一個名稱只寫一次。</b></summary>
    private static readonly HashSet<string> ClampReported = new(StringComparer.Ordinal);

    /// <summary>只保護 <see cref="ClampReported"/>；<b>刻意不共用 <see cref="Gate"/></b>。</summary>
    private static readonly object ClampGate = new();

    /// <summary>
    /// 把要求的暫停時間夾進 <c>1</c>～<see cref="SuppressionLeases.MaxLeaseMilliseconds"/>，
    /// 並在<b>真的夾到</b>時對同一個功能寫一次 <c>Information</c>。
    /// </summary>
    private static int ClampDuration(int milliseconds, string key, ref List<string>? logs)
    {
        if (milliseconds >= 1 && milliseconds <= SuppressionLeases.MaxLeaseMilliseconds)
            return milliseconds;

        var clamped = milliseconds < 1 ? 1 : SuppressionLeases.MaxLeaseMilliseconds;

        bool first;
        lock (ClampGate)
            first = ClampReported.Add(key);

        if (first)
            (logs ??= []).Add(
                $"[BotherPause] 「{key}」要求的暫停 {milliseconds} 毫秒超出範圍，已夾成 {clamped} 毫秒"
                + $"（上限 {SuppressionLeases.MaxLeaseMilliseconds} 毫秒，與 PausePlugin 同一套時間政策）。"
                + "這條路沒有續約管道，時間一到這個 bother 就會恢復接手。"
                + "這行訊息對同一個功能只會出現一次。");

        return clamped;
    }

    /// <summary>清掉已經到期的暫停。<b>呼叫端必須先持有 <see cref="Gate"/>。</b></summary>
    private static void SweepLocked(ref List<string>? logs)
    {
        if (PausedUntil.Count == 0)
        {
            anyPauses = false;
            return;
        }

        var now = Environment.TickCount64;
        List<string>? expired = null;

        foreach (var (key, until) in PausedUntil)
            if (now >= until)
                (expired ??= []).Add(key);

        if (expired == null) return;

        foreach (var key in expired)
        {
            PausedUntil.Remove(key);

            // 🔴 寫 Information：使用者跑 LogLevel 1。「這個 bother 什麼時候恢復接手的」
            // 是使用者回報「巨集跑到一半 YesAlready 又開始亂按」時唯一的線索。
            (logs ??= []).Add($"[BotherPause] 「{key}」的暫停已到期，恢復接手。");
        }

        if (PausedUntil.Count == 0) anyPauses = false;
    }

    /// <summary>把鎖內收集到的診斷訊息寫出去。<b>一定要在鎖外呼叫。</b></summary>
    private static void Flush(List<string>? logs)
    {
        if (logs == null) return;

        foreach (var message in logs)
            PluginLog.Information(message);
    }

    #endregion
}
