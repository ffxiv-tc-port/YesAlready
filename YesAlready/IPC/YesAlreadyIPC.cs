using ECommons.EzIpcManager;
using System;

namespace YesAlready.IPC;

public class YesAlreadyIPC
{
    public YesAlreadyIPC() => EzIPC.Init(this);

    #region 查詢

    /// <summary>YesAlready <b>現在會不會接手對話框</b>（＝複合值 <see cref="YesAlready.Active"/>）。</summary>
    /// <remarks>
    /// 🔴 <b>這支和 <see cref="SetPluginEnabled"/> 讀寫的不是同一個東西</b>，這是刻意保留的舊語意：
    /// 它回的是「使用者的開關 <c>C.Enabled</c>」<b>且</b>「沒有任何外掛掛著阻擋清單」
    /// <b>且</b>「沒有任何壓制租約」，而 <c>SetPluginEnabled</c> 只寫第一項。
    /// ⇒ <b>不能</b>拿這支的回傳值推斷「使用者本來有沒有開」（那是 <see cref="IsUserEnabled"/>），
    /// 也不能拿來做「事後恢復原狀」的快照 —— 別的外掛在同一段時間掛鎖，你就會誤判成
    /// 「本來就關著」而整段跳過，對方一放開 YesAlready 就在你的序列中途醒過來。
    /// 要壓制請改用 <see cref="AcquireSuppression"/>／<see cref="ReleaseSuppression"/>，那組是記名的。
    /// </remarks>
    [EzIPC] public bool IsPluginEnabled() => P.Active;

    /// <summary><b>使用者自己的開關</b>（<c>C.Enabled</c>）—— 就是 <see cref="SetPluginEnabled"/> 寫進去的那一格。</summary>
    /// <remarks>
    /// 📌 這支存在的唯一理由是補上 <see cref="IsPluginEnabled"/> 的讀寫不對稱：
    /// 想知道「我剛剛寫進去的值還在不在」問這支，想知道「它現在會不會動」問 <see cref="IsPluginEnabled"/>。
    /// </remarks>
    [EzIPC] public bool IsUserEnabled() => C.Enabled;

    /// <summary>現在是不是被<b>別人</b>壓著（壓制租約或阻擋清單任一有東西）。</summary>
    [EzIPC]
    public bool IsSuppressed()
        => SuppressionLeases.IsSuppressed || Service.BlockListHandler.Locked;

    /// <summary>
    /// 現在<b>被誰</b>壓著：壓制租約的租用者名字 ＋ 阻擋清單裡的外掛名。沒有就是空陣列。
    /// </summary>
    /// <remarks>
    /// 📌 給呼叫端做「我被誰擋住了」的診斷用；名字是租用者自己報的，不保證是 InternalName。
    /// </remarks>
    [EzIPC]
    public string[] GetSuppressionOwners()
    {
        var leases = SuppressionLeases.Owners;
        var blockList = Service.BlockListHandler.BlockList;

        if (blockList.Count == 0) return leases;

        var result = new string[leases.Length + blockList.Count];
        leases.CopyTo(result, 0);
        blockList.CopyTo(result, leases.Length);
        return result;
    }

    #endregion

    #region 壓制租約（lease）

    /// <summary>
    /// 請 YesAlready 在你的序列期間讓開，租期 <see cref="SuppressionLeases.DefaultLeaseMilliseconds"/> 毫秒。
    /// 回傳的 <see cref="Guid"/> 是憑證，結束時交回 <see cref="ReleaseSuppression"/>。
    /// </summary>
    /// <remarks>
    /// 🔑 <b>這是取代 <see cref="SetPluginEnabled"/> 的正解</b>：多個外掛可以同時各持一把，
    /// 誰放開自己那把都不會影響別人（refcount &gt; 0 才算被壓住），也完全不碰使用者的開關。
    /// 🔴 租約<b>會逾時</b>（上限 <see cref="SuppressionLeases.MaxLeaseMilliseconds"/> 毫秒）——
    /// 你當掉的話 YesAlready 會自己醒過來，這是刻意的。長工作請定期呼叫
    /// <see cref="RenewSuppression"/>，並且<b>把它的回傳值當真</b>：回
    /// <see langword="false"/> 代表你那把已經沒了，要重新 <see cref="AcquireSuppression"/>。
    /// </remarks>
    /// <param name="owner">你的名字（建議用 InternalName），會顯示在設定視窗與 log 裡。</param>
    [EzIPC]
    public Guid AcquireSuppression(string owner)
        => SuppressionLeases.Acquire(owner, SuppressionLeases.DefaultLeaseMilliseconds);

    /// <summary>
    /// 同 <see cref="AcquireSuppression"/>，但自己指定租期。
    /// 超過 <see cref="SuppressionLeases.MaxLeaseMilliseconds"/> 會被夾到上限。
    /// </summary>
    [EzIPC]
    public Guid AcquireSuppressionFor(string owner, int milliseconds)
        => SuppressionLeases.Acquire(owner, milliseconds);

    /// <summary>交回一把租約。回 <see langword="false"/>＝這把不存在（已放開或已逾時）。冪等。</summary>
    [EzIPC]
    public bool ReleaseSuppression(Guid lease) => SuppressionLeases.Release(lease);

    /// <summary>
    /// 續約（心跳），沿用取得時的租期。
    /// <b>回 <see langword="false"/> 代表你那把已經沒了</b>，必須重新取得，不要當成成功。
    /// </summary>
    [EzIPC]
    public bool RenewSuppression(Guid lease) => SuppressionLeases.Renew(lease);

    /// <summary>同 <see cref="RenewSuppression"/>，但指定新的租期。</summary>
    [EzIPC]
    public bool RenewSuppressionFor(Guid lease, int milliseconds)
        => SuppressionLeases.Renew(lease, milliseconds);

    #endregion

    #region 舊端點（相容包裝，語意不變）

    /// <summary>直接寫<b>使用者的開關</b> <c>C.Enabled</c>。</summary>
    /// <remarks>
    /// ⚠️ <b>這支沒有主人</b>：任何呼叫端都會蓋掉別人剛寫的值，而且它不是暫時的 ——
    /// 呼叫端當在序列中途，YesAlready 就<b>永遠</b>關著，使用者會以為外掛壞了。
    /// <para>
    /// 🔴 <b>刻意不把它改成租約</b>（想改的人先讀這段）：
    /// <c>SetPluginEnabled(true)</c> 的既有語意是「不管使用者原本開沒開，都給我打開」，
    /// 而 SomethingNeedDoing 把這支<b>直接開給使用者的 Lua 巨集</b>；
    /// 改成「放開租約」會讓那些巨集靜默失效。
    /// 而給它加逾時又會讓長時間的任務（Questionable 一趟可以跑幾小時）跑到一半
    /// YesAlready 自己醒過來，正是這整組改動要消滅的那個症狀。兩種都是回退既有行為。
    /// </para>
    /// 🔑 <b>新的呼叫端請改用 <see cref="AcquireSuppression"/>／<see cref="ReleaseSuppression"/>。</b>
    /// </remarks>
    [EzIPC] public void SetPluginEnabled(bool state) => C.Enabled = state;

    /// <remarks>
    /// 🔴 這三支 bother IPC 以前拿到的都是 <c>Activator.CreateInstance</c> 現造的<b>丟棄用實例</b>,
    /// 而 <c>Enabled</c> 與 <c>AddonFeature._attributes</c> 都是實例狀態 ⇒ 查詢恆回
    /// <see langword="false"/>、停用是 no-op(拆不掉真正註冊的那一組)、啟用反而<b>再掛一組
    /// 重複的監聽器</b>。現在一律取 <see cref="YesAlready.FindFeature(string)"/> 回的那一份 ——
    /// 也就是外掛啟動時建起來、實際掛著監聽器的實例。
    /// </remarks>
    [EzIPC] public bool IsBotherEnabled(string name) => FindFeature(name) is { Enabled: true };

    /// <remarks>
    /// 走 <see cref="BaseFeature.TrySetEnabled"/> 而不是直接 <c>Enable()</c>:對已經啟用的
    /// 功能再 <c>Enable()</c> 一次會掛出第二組監聽器(Dalamud 的 <c>RegisterListener</c> 不去重),
    /// 同一個事件就會被處理兩次。名稱找不到時與改動前一樣什麼都不做。
    /// </remarks>
    [EzIPC]
    public void SetBotherEnabled(string name, bool state) => FindFeature(name)?.TrySetEnabled(state);

    /// <summary>暫停 YesAlready 指定的毫秒數。</summary>
    /// <remarks>
    /// 🔴 <b>舊實作有兩個 bug，這裡都修掉了，而單一呼叫端看到的行為不變。</b>
    /// 舊實作是 <c>C.Enabled = false</c> ＋ <c>TaskManager.EnqueueDelay(ms)</c> ＋
    /// <c>TaskManager.Enqueue(() =&gt; C.Enabled = true)</c>：
    /// <list type="number">
    /// <item><c>Service.TaskManager</c> 是<b>單一</b> NeoTaskManager 實例、<c>Tasks</c> 是
    /// <c>List&lt;TaskManagerTask&gt;</c> 的 FIFO ⇒ A 要求 5 秒、B 要求 60 秒時佇列變成
    /// <c>[等 5s, 開, 等 60s, 開]</c>，<b>B 的 60 秒在第 5 秒就被打開</b>。
    /// 現在走 <see cref="SuppressionLeases.LegacyPause"/>，到期時間<b>取 max</b>。</item>
    /// <item>它會去動<b>使用者的開關</b>：設定視窗的勾勾被取消、DTR 顯示成「關閉」，
    /// 而且使用者本來就關著的話，時間一到還會<b>幫他打開</b>。
    /// 現在完全不碰 <c>C.Enabled</c>，顯示成「暫停」。</item>
    /// </list>
    /// 📌 對單一呼叫端而言 <see cref="IsPluginEnabled"/> 的回傳序列一模一樣
    /// （呼叫後 <see langword="false"/>、時間到後 <see langword="true"/>）。
    /// </remarks>
    [EzIPC]
    public void PausePlugin(int milliseconds) => SuppressionLeases.LegacyPause(milliseconds);

    [EzIPC]
    public bool PauseBother(string name, int milliseconds)
    {
        var feature = FindFeature(name);
        if (feature is null || !feature.Enabled)
            return false;
        feature.Disable();
        Service.TaskManager.EnqueueDelay(milliseconds);
        // 恢復也走冪等包裝:暫停期間若有人先把它開回來,直接 Enable() 就會掛出第二組監聽器。
        // 保持陳述式主體 ⇒ 仍然綁到 Enqueue(Action) 多載(一次性、跑完就算完成),
        // 不會變成回傳 false 的 Func<bool>(那會一路重試到 30 秒逾時)。
        Service.TaskManager.Enqueue(() => { feature.TrySetEnabled(true); });
        return true;
    }

    #endregion
}
