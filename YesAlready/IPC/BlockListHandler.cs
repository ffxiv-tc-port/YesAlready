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
/// 它天生是<b>按名字記帳的 refcount</b>、可以並存，本類別<b>不改</b>那個契約，只補上兩道<b>互補</b>的清除。
/// <para>
/// 🔑 <b>持有者存活檢查（主力）</b>：名字對得上某個<b>已載入</b>的外掛就當它還活著；
/// 曾經對得上、現在對不上（外掛被停用／卸載／崩潰後被 Dalamud 卸掉）⇒ <b>立刻</b>移除。
/// ⚠️ <b>只對「曾經對得上」的名字生效</b>：名字是呼叫端自己報的，從沒對上過的名字代表我們認不得它的命名，
/// 不能拿「認不得」當成「不存在」而殺掉別人的鎖。
/// </para>
/// <para>
/// <b>時間逾時（保險）</b>：外掛還活著、只是漏放時的最後一道，預設 <see cref="Configuration.BlockListEntryTimeoutMinutes"/> 分鐘。
/// 🔴 預設值刻意訂得寬：已知消費端沒有一個會連續掛超過幾分鐘，但<b>誤殺的代價遠大於晚幾十分鐘才自癒</b> ——
/// 被誤殺的呼叫端全是<b>邊緣觸發</b>的，不會知道自己的鎖沒了、也不會重新放回去，而 YesAlready 會在它的序列中途醒過來去按窗。
/// </para>
/// <para>
/// 🔴 <b>建構時刻意不 <c>BlockList.Clear()</c></b>：清空會把<b>別人的</b>名字洗掉，而那些外掛不知道自己的鎖沒了、之後也不會重新加，
/// 等於主動破壞當下正確的狀態，而且沒有任何一方會察覺。不清空留下的殘留鎖有人收：
/// 持有者已經不在的由存活檢查在一秒內清掉；持有者還在但狀態機已重置的要等時間逾時，中間 YesAlready 是啞的 ——
/// 但使用者看得到（設定視窗紅字、DTR 圖示、tooltip 的剩餘時間），也有「強制解除鎖定」可按。
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

    /// <summary>存活檢查每一輪重用的已載入名稱集合；只在主執行緒的掃描裡用，每輪先清空再填。</summary>
    private readonly HashSet<string> loadedNames = new(StringComparer.Ordinal);

    public BlockListHandler()
    {
        BlockList = Svc.PluginInterface.GetOrCreateData<HashSet<string>>(BlockListNamespace, () => []);

        // 🔴 刻意不 Clear()（理由見類別註解）。開場只是把既有項目登記起來開始計時，
        // 並且寫一行看得見的 Information —— 這是使用者判斷「重載之後為什麼還是不動」的唯一線索。
        var now = Environment.TickCount64;
        lastSweep = now;

        var existing = SnapshotNames();
        foreach (var name in existing)
            tracked[name] = new Entry { FirstSeen = now };

        if (existing.Length != 0)
            PluginLog.Information($"[BlockList] 載入時阻擋清單裡已經有 {existing.Length} 筆：{string.Join("、", existing)}。" +
                                  "這些是別的插件在本插件載入前掛上的，刻意保留（清掉會在對方序列中途讓 YesAlready 醒過來）；" +
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
                                  "這些插件不會知道自己的登記被移除，它們若還在跑序列，YesAlready 現在起會接手對話框。");
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
                    RemoveExpired(name, "持有它的插件已經不在載入中（被停用、被卸載，或崩潰後被 Dalamud 卸掉）");
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

        // 🔴 要使用者回報的診斷：這一行是「YesAlready 突然又動了」時唯一的線索，
        // 也是「哪一個外掛漏放了鎖」的證據。
        PluginLog.Information($"[BlockList] 已自動移除「{name}」的阻擋登記：{reason}。" +
                              "YesAlready 從現在起恢復接手對話框；那個插件若還在跑，它並不知道自己的登記沒了。");
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
    private HashSet<string>? TryGetLoadedPluginNames()
    {
        try
        {
            // 重用同一個集合（回傳值下一輪會被清空，只在這一輪的 Sweep 裡用）：
            // 每輪新建要長到上百格，被壓著時每秒一次約 7 KB。
            var set = loadedNames;
            set.Clear();

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
            PluginLog.Debug($"[BlockList] 取不到已載入插件清單，這一輪跳過存活檢查：{ex.Message}");
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
