using System;
using System.Collections.Generic;
using System.Linq;

namespace YesAlready.IPC;

/// <summary>
/// 「請 YesAlready 在我這段序列期間讓開」的<b>租約（lease）登記處</b>：多個外掛各自持有一把
/// 帶到期時間的租約，只要還有任何一把沒到期，YesAlready 就不接手對話框。
/// </summary>
/// <remarks>
/// 🔴🔴 <b>存在的理由＝舊的開關沒有主人。</b>
/// 舊端點 <c>SetPluginEnabled</c> 寫的是<b>使用者的開關</b> <c>C.Enabled</c>（單一格布林），
/// 而 <c>IsPluginEnabled</c> 讀的是複合值 <see cref="YesAlready.Active"/>
/// （<c>C.Enabled &amp;&amp; !BlockListHandler.Locked</c>）—— <b>讀到的和寫進去的不是同一個東西</b>。
/// 於是三個消費端（Questionable 每幀 <c>OnUpdate</c> 自帶 <c>_wasEnabled</c> 快照、
/// SomethingNeedDoing 的 <c>EnableAsync</c>/<c>DisableAsync</c> 裸寫、BOCCHI/Ocelot）
/// 各自持一份快照互相覆蓋：跑任務時巨集碰一下開關，另一邊的守衛條件就再也不成立，
/// 結果不是「整趟任務 YesAlready 一直開著搶按窗」就是「任務跑完被永久關掉、使用者以為外掛壞了」。
/// <b>全程零訊息。</b>
/// <para>
/// 🔑 <b>租約解掉的是「誰的意思」這個資訊</b>：每一把租約記名字、記到期時間；
/// <see cref="IsSuppressed"/> 是 <b>refcount &gt; 0</b>（不是布林覆寫），
/// <see cref="Acquire"/> 的到期時間對同一把租約<b>取 max 不取序列</b>。
/// </para>
/// <para>
/// 🔴 <b>逾時上限是硬性的</b>：租用者當掉／被卸載／忘了放開，都不能讓 YesAlready 永久失效。
/// 每一把租約都有 <see cref="MaxLeaseMilliseconds"/> 的天花板，長工作要自己
/// <see cref="Renew"/> 續約（心跳）。到期時寫 <c>Information</c>，使用者的 log 看得到是誰。
/// </para>
/// <para>
/// 📌 <b>與既有的 <see cref="BlockListHandler"/>（共享資料 <c>"YesAlready.StopRequests"</c>）並存、不取代。</b>
/// 那條路是 ECommons 系外掛（AutoRetainer／Artisan／ICE／Lifestream／Saucy／TCToolbox）
/// 已經在用的標準做法，本身就是按名字記帳的 refcount，只是<b>沒有到期時間</b>
/// —— 掛著鎖的外掛崩掉就永遠鎖著。租約是它的「有逃生口」版本；兩邊都算數，
/// 任一邊有東西 YesAlready 就讓開。
/// </para>
/// <para>
/// ⚠️ <b>執行緒</b>：IPC 呼叫在呼叫端的執行緒上同步跑（SomethingNeedDoing 的巨集不在主執行緒），
/// 所以這裡全程上鎖。<see cref="IsSuppressed"/> 每幀被讀（DTR）也被每個 addon 事件讀，
/// 所以「一把租約都沒有」的常態走 <see cref="anyLeases"/> 這條不上鎖的快路。
/// </para>
/// </remarks>
internal static class SuppressionLeases
{
    /// <summary>沒指定時長時的預設租期（5 分鐘）。</summary>
    /// <remarks>
    /// 挑這個值的理由：比任何一段「自動化序列」都長（交納、改名、換區、跑一段任務），
    /// 又短到租用者整個當掉時使用者不會以為外掛壞了。長工作請用 <see cref="Renew"/> 續約。
    /// <para>
    /// 📌 刻意與 <see cref="MaxLeaseMilliseconds"/> 相同，也與 AutoRetainer 那套相同 ——
    /// 全艦隊的壓制租約時間政策統一成 5 分鐘。
    /// </para>
    /// </remarks>
    public const int DefaultLeaseMilliseconds = 300_000;

    /// <summary>
    /// 單一把租約的<b>硬性</b>上限（5 分鐘）。要求更長會被夾到這個值。
    /// </summary>
    /// <remarks>
    /// 🔴 這是「租用者當掉不能讓 YesAlready 永久失效」的最後一道保險，<b>不是</b>建議值。
    /// 需要壓住超過 5 分鐘的呼叫端必須自己續約 —— 續約失敗（呼叫端已經不在了）正是
    /// 我們想要偵測的那件事。
    /// <para>
    /// 🔴 <b>砍短上限的前提是呼叫端先留好餘裕。</b><see cref="Renew"/> 的第一件事是
    /// <see cref="SweepLocked"/>，而掃除條件是 <c>now &gt;= ExpiresAt</c> ⇒ 續約間隔只要不
    /// 明顯小於租期，第一次心跳送到時那把已經被掃掉、續約<b>必定</b>回
    /// <see langword="false"/>（不是競態，是每次都會發生）。三個自有呼叫端
    /// （AutoDuty／Questionable／SomethingNeedDoing）已經一起改成「租 5 分鐘、每 30 秒續約」
    /// 的 10 倍餘裕，與 AutoRetainer 一致。
    /// </para>
    /// <para>
    /// ⚠️ 舊端點 <c>PausePlugin</c> <b>沒有續約管道</b>：要求超過這個上限的暫停會被夾短，
    /// 時間一到 YesAlready 就恢復搶按窗。那不再是靜默的 —— 見 <see cref="ClampDuration"/>。
    /// </para>
    /// </remarks>
    public const int MaxLeaseMilliseconds = 300_000;

    /// <summary>舊端點 <c>PausePlugin</c> 用的<b>單一匿名租約</b>持有者名稱。</summary>
    /// <remarks>
    /// 舊端點沒有呼叫端身分可用（Dalamud IPC 不帶呼叫者），所有走舊端點的人共用這一把。
    /// 它們之間仍然會互相影響（那是舊語意，本來就這樣），但<b>不會再蓋掉具名租約</b>。
    /// </remarks>
    internal const string LegacyPauseOwner = "(PausePlugin)";

    private sealed class Lease(Guid id, string owner, long expiresAt)
    {
        public Guid Id { get; } = id;
        public string Owner { get; } = owner;

        /// <summary><see cref="Environment.TickCount64"/> 座標系的到期時刻。</summary>
        public long ExpiresAt { get; set; } = expiresAt;

        /// <summary>續約時沿用的時長（<see cref="Renew(Guid)"/> 不帶參數時用）。</summary>
        public int DurationMs { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, Lease> Leases = [];

    /// <summary>舊端點共用的那一把；<see cref="Guid.Empty"/>＝目前沒有。</summary>
    private static Guid legacyPauseLease;

    /// <summary>
    /// 「現在一把租約都沒有」的不上鎖快路。
    /// </summary>
    /// <remarks>
    /// 🔴 只用來<b>提早否定</b>：<see langword="false"/> 一定代表沒有租約（清空一定在設它之前），
    /// <see langword="true"/> 只代表「可能有」，還是要進鎖裡掃過期。
    /// 反過來寫（樂觀地相信 true）會讓已經到期的租約繼續壓住。
    /// </remarks>
    private static volatile bool anyLeases;

    /// <summary>目前是否有<b>任何一把</b>沒到期的租約壓著。</summary>
    public static bool IsSuppressed
    {
        get
        {
            if (!anyLeases) return false;
            lock (Gate)
            {
                SweepLocked();
                return Leases.Count != 0;
            }
        }
    }

    /// <summary>目前壓著的租用者名字（含舊端點的匿名那一把）。沒有就是空陣列。</summary>
    public static string[] Owners
    {
        get
        {
            if (!anyLeases) return [];
            lock (Gate)
            {
                SweepLocked();
                if (Leases.Count == 0) return [];
                return Leases.Values.Select(x => x.Owner).Distinct(StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <summary>目前有效的租約把數（診斷用；<b>語意就是 refcount</b>）。</summary>
    public static int Count
    {
        get
        {
            if (!anyLeases) return 0;
            lock (Gate)
            {
                SweepLocked();
                return Leases.Count;
            }
        }
    }

    /// <summary>
    /// 目前每一把有效租約的診斷快照：租用者名字 ＋ 距離逾時還有多久（毫秒）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 只給 UI／tooltip 用（會配置陣列），呼叫前先判 <see cref="IsSuppressed"/>。
    /// 📌 同一個名字持有多把時<b>只留最晚到期的那一把</b> —— 使用者要看的是「還要等多久才會自己解除」，
    /// 不是「這個外掛開了幾把」。
    /// </remarks>
    public static (string Owner, long RemainingMs)[] Snapshot()
    {
        if (!anyLeases) return [];

        lock (Gate)
        {
            SweepLocked();
            if (Leases.Count == 0) return [];

            var now = Environment.TickCount64;
            var byOwner = new Dictionary<string, long>(StringComparer.Ordinal);

            foreach (var lease in Leases.Values)
            {
                var remaining = lease.ExpiresAt - now;
                if (remaining < 0) remaining = 0;
                if (!byOwner.TryGetValue(lease.Owner, out var existing) || remaining > existing)
                    byOwner[lease.Owner] = remaining;
            }

            return byOwner.Select(x => (x.Key, x.Value)).ToArray();
        }
    }

    /// <summary>
    /// 取得一把新的租約。回傳的 <see cref="Guid"/> 就是憑證，放開時交回
    /// <see cref="Release(Guid)"/>。
    /// </summary>
    /// <param name="owner">租用者名字（建議用自己的 InternalName）；空白會被換成 <c>"(unnamed)"</c>。</param>
    /// <param name="milliseconds">租期毫秒；夾在 <c>1</c> 與 <see cref="MaxLeaseMilliseconds"/> 之間。</param>
    /// <remarks>
    /// 📌 <b>每次呼叫都是一把新的</b>（不是「同名就共用」）：同一個外掛內部有兩段序列並行時
    /// 各自持一把，先結束的那段放開自己那把不會影響另一段 —— 這正是舊端點做不到的事。
    /// </remarks>
    public static Guid Acquire(string? owner, int milliseconds)
    {
        var name = string.IsNullOrWhiteSpace(owner) ? "(unnamed)" : owner!.Trim();
        var duration = ClampDuration(milliseconds, name);
        var id = Guid.NewGuid();

        lock (Gate)
        {
            SweepLocked();
            Leases[id] = new Lease(id, name, Environment.TickCount64 + duration) { DurationMs = duration };
            anyLeases = true;
        }

        PluginLog.Information($"[SuppressionLease] 「{name}」取得壓制租約 {id}（{duration} 毫秒）。");
        return id;
    }

    /// <summary>交回一把租約。回 <see langword="false"/>＝這把不存在（已經放開過或已經到期）。</summary>
    public static bool Release(Guid id)
    {
        string? owner = null;

        lock (Gate)
        {
            if (Leases.Remove(id, out var lease))
                owner = lease.Owner;

            if (id == legacyPauseLease) legacyPauseLease = Guid.Empty;

            SweepLocked();
            if (Leases.Count == 0) anyLeases = false;
        }

        if (owner == null) return false;

        PluginLog.Information($"[SuppressionLease] 「{owner}」放開壓制租約 {id}。");
        return true;
    }

    /// <summary>
    /// 續約（心跳）。回 <see langword="false"/>＝這把已經不在了，呼叫端必須重新
    /// <see cref="Acquire"/>，<b>不要當成續約成功</b>。
    /// </summary>
    /// <param name="milliseconds">新的租期；<c>null</c>＝沿用取得時的時長。</param>
    public static bool Renew(Guid id, int? milliseconds = null)
    {
        lock (Gate)
        {
            SweepLocked();
            if (!Leases.TryGetValue(id, out var lease)) return false;

            var duration = milliseconds is { } ms ? ClampDuration(ms, lease.Owner) : lease.DurationMs;
            lease.DurationMs = duration;

            // 🔴 取 max：續約永遠只會往後延，不會把別人（或自己先前）已經談好的
            // 到期時間往前搬。
            var until = Environment.TickCount64 + duration;
            if (until > lease.ExpiresAt) lease.ExpiresAt = until;
            return true;
        }
    }

    /// <summary>
    /// 舊端點 <c>PausePlugin</c> 的實作：把<b>單一匿名租約</b>的到期時間延到
    /// <c>max(現有, 現在 + milliseconds)</c>。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>這支修掉的是佇列序列化那個 bug。</b>舊實作是
    /// <c>C.Enabled = false; TaskManager.EnqueueDelay(ms); TaskManager.Enqueue(() =&gt; C.Enabled = true)</c>，
    /// 而 <c>Service.TaskManager</c> 是<b>單一</b> NeoTaskManager 實例、<c>Tasks</c> 是一條
    /// <c>List&lt;TaskManagerTask&gt;</c> 的 FIFO ⇒ A 要求暫停 5 秒、B 要求 60 秒，
    /// 佇列變成 <c>[等 5s, 開, 等 60s, 開]</c>，<b>B 的 60 秒在第 5 秒就被打開</b>。
    /// 取 max 之後 B 拿到的就是 60 秒。
    /// <para>
    /// 📌 <b>單一呼叫端看到的行為不變</b>：呼叫後 <c>IsPluginEnabled()</c> 回
    /// <see langword="false"/>、時間到之後回 <see langword="true"/>。
    /// 差別是它<b>不再去動使用者的開關 <c>C.Enabled</c></b> —— 舊實作會把設定視窗的勾勾
    /// 取消掉、DTR 顯示成「關閉」，而且「使用者本來就關著」時時間一到還會<b>幫他打開</b>。
    /// 現在顯示成「暫停」，使用者的開關原封不動。
    /// </para>
    /// </remarks>
    public static void LegacyPause(int milliseconds)
    {
        var duration = ClampDuration(milliseconds, LegacyPauseOwner);
        var until = Environment.TickCount64 + duration;

        lock (Gate)
        {
            SweepLocked();

            if (legacyPauseLease != Guid.Empty && Leases.TryGetValue(legacyPauseLease, out var existing))
            {
                if (until > existing.ExpiresAt) existing.ExpiresAt = until;
                existing.DurationMs = duration;
                anyLeases = true;
                return;
            }

            var id = Guid.NewGuid();
            Leases[id] = new Lease(id, LegacyPauseOwner, until) { DurationMs = duration };
            legacyPauseLease = id;
            anyLeases = true;
        }

        PluginLog.Information($"[SuppressionLease] 舊端點 PausePlugin 取得匿名壓制租約（{duration} 毫秒）。");
    }

    /// <summary>把所有租約丟掉（設定視窗的「強制解除鎖定」、外掛卸載）。</summary>
    public static void ReleaseAll(string reason)
    {
        string[] owners;

        lock (Gate)
        {
            if (Leases.Count == 0)
            {
                anyLeases = false;
                return;
            }

            owners = Leases.Values.Select(x => x.Owner).Distinct(StringComparer.Ordinal).ToArray();
            Leases.Clear();
            legacyPauseLease = Guid.Empty;
            anyLeases = false;
        }

        PluginLog.Information($"[SuppressionLease] 丟掉全部壓制租約（{reason}）：{string.Join("、", owners)}。");
    }

    /// <summary>已經回報過「租期被夾值」的租用者名字。<b>同一個名字只寫一次。</b></summary>
    private static readonly HashSet<string> ClampReported = new(StringComparer.Ordinal);

    /// <summary>
    /// 只保護 <see cref="ClampReported"/>。<b>刻意不共用 <see cref="Gate"/></b>：
    /// <see cref="Renew"/> 是在已經持有 <see cref="Gate"/> 的狀態下呼叫進來的。
    /// </summary>
    private static readonly object ClampGate = new();

    /// <summary>
    /// 把要求的租期夾進 <c>1</c>～<see cref="MaxLeaseMilliseconds"/>，並在<b>真的夾到</b>時
    /// 對同一個租用者寫一次 <c>Information</c>。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>夾值以前是完全靜默的。</b>最需要看得見的是舊端點 <c>PausePlugin</c>
    /// （SomethingNeedDoing 的 Lua <c>IPC.YesAlready.PausePlugin(毫秒)</c>）：使用者自己寫的
    /// 巨集可以傳任意毫秒數，而那條路<b>沒有續約管道</b> ⇒ 超過上限就被砍短、YesAlready
    /// 在巨集跑到一半醒過來搶按窗，而 log 上一個字都沒有。這一行至少讓 log 說得出
    /// 發生了什麼事。
    /// <para>
    /// 📌 <b>同一個租用者只寫一次</b>（<see cref="ClampReported"/> 永不清空）：續約是每 30 秒
    /// 一次的心跳，每次都寫會把使用者的 log 洗掉。
    /// </para>
    /// <para>
    /// 📌 用 <c>Information</c> 而不是 <c>Warning</c>：這是「說明發生了什麼」的診斷，
    /// 不是錯誤。也<b>不</b>走 DuoLog —— 那會無條件印進使用者的聊天視窗。
    /// </para>
    /// </remarks>
    private static int ClampDuration(int milliseconds, string owner)
    {
        if (milliseconds >= 1 && milliseconds <= MaxLeaseMilliseconds)
            return milliseconds;

        var clamped = milliseconds < 1 ? 1 : MaxLeaseMilliseconds;

        bool first;
        lock (ClampGate)
            first = ClampReported.Add(owner);

        if (first)
            PluginLog.Information(
                $"[SuppressionLease] 「{owner}」要求的租期 {milliseconds} 毫秒超出範圍，已夾成 {clamped} 毫秒"
                + $"（上限 {MaxLeaseMilliseconds} 毫秒）。要壓住更久必須自己續約；"
                + "舊端點 PausePlugin 沒有續約管道，時間一到 YesAlready 就會恢復搶按窗。"
                + "這行訊息對同一個租用者只會出現一次。");

        return clamped;
    }

    /// <summary>清掉已經到期的租約。<b>呼叫端必須先持有 <see cref="Gate"/>。</b></summary>
    private static void SweepLocked()
    {
        if (Leases.Count == 0)
        {
            anyLeases = false;
            return;
        }

        var now = Environment.TickCount64;
        List<Guid>? expired = null;

        foreach (var (id, lease) in Leases)
            if (now >= lease.ExpiresAt)
                (expired ??= []).Add(id);

        if (expired == null) return;

        foreach (var id in expired)
        {
            var owner = Leases[id].Owner;
            Leases.Remove(id);
            if (id == legacyPauseLease) legacyPauseLease = Guid.Empty;

            // 🔴 寫 Information：使用者跑 LogLevel 1。租約到期＝「有人壓著 YesAlready 卻沒放開」，
            // 這一行是使用者回報「YesAlready 突然不動了／突然又動了」時唯一的線索。
            PluginLog.Information($"[SuppressionLease] 「{owner}」的壓制租約 {id} 已逾時，自動放開" +
                                  "（租用者沒有續約，可能已經當掉或被卸載）。");
        }

        if (Leases.Count == 0) anyLeases = false;
    }
}
