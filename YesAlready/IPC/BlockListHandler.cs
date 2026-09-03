using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace YesAlready.IPC;

/// <summary>
/// 共享資料 <c>"YesAlready.StopRequests"</c>（阻擋清單）的持有者與<b>看門狗</b>。
/// </summary>
/// <remarks>
/// 📌 <b>這條路是 ECommons 系外掛的既有標準做法</b>，本外掛只是提供那個 <c>HashSet</c>：
/// AutoRetainer（<c>NewYesAlreadyManager</c>）／Artisan（<c>TaskInteractWithNearestBell</c>）／
/// ICE／Lifestream／Saucy／TCToolbox 各自把自己的名字放進去，
/// <see cref="Locked"/> 成立時 YesAlready 就不接手對話框。
/// 它天生是<b>按名字記帳的 refcount</b>、可以並存 —— 設計是好的，本類別<b>不改</b>那個契約。
/// <para>
/// 🔴🔴 <b>但它原本有兩個會讓 YesAlready 永久失效的洞，而且失敗形式全程零訊息。</b>
/// </para>
/// <para>
/// <b>洞一：沒有到期時間。</b>掛著鎖的外掛崩掉／被停用／流程從例外路徑中斷，名字就<b>永遠</b>留著。
/// 使用者看到的是「YesAlready 從此不再幫我按」，而設定視窗的勾勾還是打開的。
/// 修法是兩道<b>互補</b>的清除，不是一個計時器：
/// <list type="number">
/// <item>🔑 <b>持有者存活檢查（主力）</b>：名字對得上某個<b>已載入</b>的外掛就當它還活著；
/// 曾經對得上、現在對不上（外掛被停用／卸載／崩潰後被 Dalamud 卸掉）⇒ <b>立刻</b>移除。
/// 這一道<b>不猜時間</b>，正好命中「掛著鎖的外掛不在了」這個真正的故障形狀。
/// ⚠️ <b>只對「曾經對得上」的名字生效</b>：名字是呼叫端自己報的（AutoRetainer 報
/// <c>P.Name</c>、其餘報 <c>InternalName</c>），從沒對上過的名字代表我們認不得它的命名，
/// 不能拿「認不得」當成「不存在」而殺掉別人的鎖。</item>
/// <item><b>時間逾時（保險）</b>：外掛還活著、只是漏放（狀態機從例外路徑跳出去）時的最後一道。
/// 預設 <see cref="Configuration.BlockListEntryTimeoutMinutes"/> 分鐘，設定視窗可調、可關。</item>
/// </list>
/// 🔴 <b>為什麼預設值要那麼長</b>：六個已知消費端裡沒有一個會連續掛超過幾分鐘
/// （AR／Lifestream 綁 <c>TaskManager.IsBusy</c>、ICE 綁兩個短暫狀態、Artisan 綁一次傳喚鈴、
/// Saucy 綁一局幻卡、TCToolbox 綁一次改名序列），但<b>誤殺的代價遠大於晚幾十分鐘才自癒</b>：
/// 被誤殺的呼叫端不會知道自己的鎖沒了、也不會重新放回去（它們全都是<b>邊緣觸發</b>的），
/// 而 YesAlready 會在它的序列中途醒過來去按窗。存活檢查已經涵蓋了「外掛不在了」，
/// 時間這道只需要涵蓋「還活著但漏放」，所以寧可訂得寬。
/// </para>
/// <para>
/// <b>洞二：建構時 <c>BlockList.Clear()</c>。</b>重新載入 YesAlready 會把<b>別人的</b>名字洗掉，
/// 而那些外掛不知道自己的鎖沒了、之後也不會重新加（同樣是邊緣觸發）。
/// 🔑 <b>選擇：不再清空</b>，改成開場把既有項目登記起來、寫一行 <c>Information</c> 讓使用者看得見。
/// 兩害相權的理由：
/// <list type="bullet">
/// <item>清空的害處是<b>主動破壞當下正確的狀態</b> —— 別的外掛正在跑序列、鎖是對的，
/// 被洗掉之後 YesAlready 立刻開始搶按窗，而且沒有任何一方會察覺。</item>
/// <item>不清空的害處是<b>可能留下沒人會來放的殘留鎖</b>。但這個害處現在<b>有人收</b>：
/// 持有者已經不在的話存活檢查在一秒內就清掉；持有者還在但狀態機已重置（外掛在
/// YesAlready 沒載入的那段空窗期被重載）則由時間逾時收尾。
/// 🔴 <b>殘餘風險寫在這裡</b>：後面那種情形要等到逾時才會自癒，中間 YesAlready 是啞的 ——
/// 但使用者看得到（設定視窗紅字、DTR 圖示、tooltip 的剩餘時間），也有「強制解除鎖定」可按。
/// 相較之下清空是<b>看不見</b>的破壞。</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ <b>執行緒</b>：這個 <c>HashSet</c> 是跨外掛共用的，別的外掛隨時可能在我們列舉它的當下增刪。
/// 掃描全部掛在 <see cref="IFramework.Update"/>（主執行緒，多數消費端也在那裡動它），
/// 列舉一律先 <c>ToArray()</c> 並接住 <see cref="InvalidOperationException"/> —— 撞到就跳過這一輪，
/// 一秒後還會再來，不值得把整個 tick 賠進去。
/// </para>
/// </remarks>
public class BlockListHandler : IDisposable
{
    internal const string BlockListNamespace = "YesAlready.StopRequests";
    internal HashSet<string> BlockList;
    internal bool Locked => BlockList.Count != 0;

    /// <summary>掃描間隔。清單空著的常態下連這個都不會走到（先判 <c>Count == 0</c>）。</summary>
    private const int SweepIntervalMs = 1000;

    /// <summary>一筆阻擋登記的觀測紀錄。</summary>
    private sealed class Entry
    {
        /// <summary><see cref="Environment.TickCount64"/> 座標系：這個名字<b>連續</b>掛在清單上的起點。</summary>
        public long FirstSeen;

        /// <summary>
        /// 這個名字<b>曾經</b>對上過一個已載入的外掛。
        /// </summary>
        /// <remarks>
        /// 🔴 存活檢查的閘門：只有<see langword="true"/> 之後又對不上，才算「持有者不在了」。
        /// 沒有這個閘門的話，任何我們認不得的命名都會被當成死鎖殺掉。
        /// </remarks>
        public bool OwnerSeenLoaded;
    }

    private readonly Dictionary<string, Entry> tracked = new(StringComparer.Ordinal);
    private long lastSweep;

    public BlockListHandler()
    {
        BlockList = Svc.PluginInterface.GetOrCreateData<HashSet<string>>(BlockListNamespace, () => []);

        // 🔴 刻意不 Clear()（理由見類別註解「洞二」）。開場只是把既有項目登記起來開始計時，
        // 並且寫一行看得見的 Information —— 這是使用者判斷「重載之後為什麼還是不動」的唯一線索。
        var now = Environment.TickCount64;
        lastSweep = now;

        var existing = SnapshotNames();
        foreach (var name in existing)
            tracked[name] = new Entry { FirstSeen = now };

        if (existing.Length != 0)
            PluginLog.Information($"[BlockList] 載入時阻擋清單裡已經有 {existing.Length} 筆：{string.Join("、", existing)}。" +
                                  "這些是別的外掛在本外掛載入前掛上的，刻意保留（清掉會在對方序列中途讓 YesAlready 醒過來）；" +
                                  "持有者若已經不在，看門狗會在一秒內移除。");

        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose() => Svc.Framework.Update -= OnFrameworkUpdate;

    /// <summary>
    /// 目前每一筆阻擋登記的診斷快照：名字 ＋ 距離自動移除還有多久（毫秒）。
    /// </summary>
    /// <remarks>
    /// 📌 <c>RemainingMs</c> 為 <c>-1</c>＝沒有設定時間逾時（使用者把逾時關掉了），
    /// 此時仍然有存活檢查那一道，只是沒有可以顯示的倒數。
    /// ⚠️ 只給 UI／tooltip 用（會配置字串與陣列），呼叫前先判 <see cref="Locked"/>。
    /// </remarks>
    internal (string Owner, long RemainingMs)[] Snapshot()
    {
        var names = SnapshotNames();
        if (names.Length == 0) return [];

        var timeout = TimeoutMilliseconds;
        var now = Environment.TickCount64;
        var result = new (string, long)[names.Length];

        for (var i = 0; i < names.Length; i++)
        {
            var remaining = -1L;

            if (timeout > 0)
            {
                // 還沒被掃到的名字（剛加進來、還沒跑過 sweep）就當它從現在開始算。
                var firstSeen = tracked.TryGetValue(names[i], out var entry) ? entry.FirstSeen : now;
                remaining = firstSeen + timeout - now;
                if (remaining < 0) remaining = 0;
            }

            result[i] = (names[i], remaining);
        }

        return result;
    }

    /// <summary>把整份阻擋清單丟掉（設定視窗的「強制解除鎖定」）。</summary>
    /// <remarks>
    /// 🔴 這是<b>使用者主動</b>要求的破壞性操作，所以照做並寫一行 <c>Information</c> 說清楚誰被清掉了 ——
    /// 被清掉的外掛不會知道自己的鎖沒了。與建構時的「不清空」不衝突：那邊是我們自己的重載，
    /// 使用者沒有表示意見。
    /// </remarks>
    internal void ForceClear(string reason)
    {
        var names = SnapshotNames();

        try
        {
            BlockList.Clear();
        }
        catch (InvalidOperationException)
        {
            // 別的外掛正好在改：下一次按還會再試，不要把例外丟進 ImGui 的繪製流程。
            return;
        }

        tracked.Clear();

        if (names.Length != 0)
            PluginLog.Information($"[BlockList] 已清空阻擋清單（{reason}）：{string.Join("、", names)}。" +
                                  "這些外掛不會知道自己的登記被移除，它們若還在跑序列，YesAlready 現在起會接手對話框。");
    }

    /// <summary>設定裡的逾時毫秒數；<c>0</c>＝使用者把時間逾時關掉了。</summary>
    private static int TimeoutMilliseconds
    {
        get
        {
            var minutes = C.BlockListEntryTimeoutMinutes;
            return minutes <= 0 ? 0 : minutes * 60_000;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // 🔑 常態是清單空的：這條路徑每幀都會走到，所以第一件事就是最便宜的那個判斷。
        if (BlockList.Count == 0)
        {
            if (tracked.Count != 0) tracked.Clear();
            return;
        }

        var now = Environment.TickCount64;
        if (now - lastSweep < SweepIntervalMs) return;
        lastSweep = now;

        Sweep(now);
    }

    private void Sweep(long now)
    {
        var names = SnapshotNames();
        if (names.Length == 0) return;

        // 已經被持有者自己拿掉的名字要停止追蹤，這樣同一個名字重新加回來時計時器會<b>重新開始</b>
        // —— 沒有這一步的話，會反覆上鎖／解鎖的呼叫端（AR、Lifestream 綁 IsBusy）會被
        // 第一次上鎖的時間一路算下去，逾時就變成「用了幾小時之後隨機被砍」。
        if (tracked.Count != 0)
        {
            List<string>? gone = null;
            foreach (var key in tracked.Keys)
                if (Array.IndexOf(names, key) < 0)
                    (gone ??= []).Add(key);

            if (gone != null)
                foreach (var key in gone)
                    tracked.Remove(key);
        }

        var loaded = TryGetLoadedPluginNames();
        var timeout = TimeoutMilliseconds;

        foreach (var name in names)
        {
            if (!tracked.TryGetValue(name, out var entry))
                tracked[name] = entry = new Entry { FirstSeen = now };

            if (loaded != null)
            {
                if (loaded.Contains(name))
                {
                    entry.OwnerSeenLoaded = true;
                }
                else if (entry.OwnerSeenLoaded)
                {
                    RemoveExpired(name, "持有它的外掛已經不在載入中（被停用、被卸載，或崩潰後被 Dalamud 卸掉）");
                    continue;
                }
            }

            if (timeout > 0 && now - entry.FirstSeen >= timeout)
                RemoveExpired(name, $"已經連續掛了 {(now - entry.FirstSeen) / 60_000} 分鐘，超過設定的逾時上限 {timeout / 60_000} 分鐘");
        }
    }

    /// <summary>移除一筆逾期／持有者已不在的登記，並寫一行使用者看得到的診斷。</summary>
    private void RemoveExpired(string name, string reason)
    {
        try
        {
            if (!BlockList.Remove(name)) return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        tracked.Remove(name);

        // 🔴 寫 Information：使用者跑 LogLevel 1。這一行是「YesAlready 突然又動了」時唯一的線索，
        // 也是「哪一個外掛漏放了鎖」的證據。
        PluginLog.Information($"[BlockList] 已自動移除「{name}」的阻擋登記：{reason}。" +
                              "YesAlready 從現在起恢復接手對話框；那個外掛若還在跑，它並不知道自己的登記沒了。");
    }

    /// <summary>
    /// 目前<b>已載入</b>的外掛識別名集合（<c>InternalName</c> 與 <c>Name</c> 都收）。
    /// 問不到就回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>空集合一律當成「問不到」</b>而不是「一個外掛都沒載入」：本外掛自己一定在裡面，
    /// 真的數到 0 只可能是查詢本身壞了 —— 這種時候若照字面解讀，會把清單上<b>每一筆</b>都判成死鎖清掉。
    /// 「一致的全 0 先懷疑查詢」在這裡的代價是全艦隊的壓制同時失效。
    /// </remarks>
    private static HashSet<string>? TryGetLoadedPluginNames()
    {
        try
        {
            var set = new HashSet<string>(StringComparer.Ordinal);

            foreach (var plugin in Svc.PluginInterface.InstalledPlugins)
            {
                if (!plugin.IsLoaded) continue;
                set.Add(plugin.InternalName);
                set.Add(plugin.Name);
            }

            return set.Count == 0 ? null : set;
        }
        catch (Exception ex)
        {
            // 問不到就這一輪不做存活檢查，時間逾時那道照常。
            PluginLog.Debug($"[BlockList] 取不到已載入外掛清單，這一輪跳過存活檢查：{ex.Message}");
            return null;
        }
    }

    /// <summary>把共用的 <c>HashSet</c> 抄成陣列；別的外掛正在改就回空陣列（下一輪再來）。</summary>
    private string[] SnapshotNames()
    {
        try
        {
            return BlockList.ToArray();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }
}
