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
    /// 也不能拿來做「事後恢復原狀」的快照 —— 別的外掛正掛著鎖時，你會誤判成「本來就關著」而整段跳過。
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

    // 🔑🔑 這一組的「形狀」是全艦隊壓制租約的標準形狀：AutoRetainer 同名端點的簽章逐字相同（只有 EzIPC 的 prefix 不同）。
    //     AcquireSuppressionFor(string owner, int ms) -> Guid
    //     ReleaseSuppression   (Guid lease)           -> bool
    //     RenewSuppression     (Guid lease)           -> bool
    //     RenewSuppressionFor  (Guid lease, int ms)   -> bool

    // 🔴 形狀必須一致的理由是「不一致會靜默出錯」：Dalamud 的 CallGate 在型別對不上時不會直接報錯，而是把值 JSON 序列化再
    //    反序列化成呼叫端宣告的型別；轉得過去（例如 Guid → string）就拿到一個看似成功的結果，真正的租約卻繼續壓著直到逾時。
    // ⚠️ 預設租期與硬性上限都是 5 分鐘（與 AutoRetainer 相同），要求的租期兩邊都會被夾，所以呼叫端一律要續約
    //    （建議每 30 秒一次），不要假設自己拿到了要求的時長。真的被夾到時，提供端會對同一個租用者寫一次 Information。
    // 📌 AutoRetainer 沒有不帶時長的便利版 AcquireSuppression(string) -> Guid：那個名字在它那邊曾經是別的回傳型別，同名改型別會讓舊消費端撞上 IpcTypeMismatchError。

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
    /// 而 SomethingNeedDoing 把這支<b>直接開給使用者的 Lua 巨集</b>，改成「放開租約」會讓那些巨集靜默失效；
    /// 給它加逾時又會讓長時間的任務（Questionable 一趟可以跑幾小時）跑到一半 YesAlready 自己醒過來。
    /// </para>
    /// 🔑 <b>新的呼叫端請改用 <see cref="AcquireSuppression"/>／<see cref="ReleaseSuppression"/>。</b>
    /// </remarks>
    [EzIPC] public void SetPluginEnabled(bool state) => C.Enabled = state;

    /// <remarks>
    /// 🔴 這三支 bother IPC 一律取 <see cref="YesAlready.FindFeature(string)"/> 回的那一份 —— 外掛啟動時建起來、
    /// 實際掛著監聽器的實例。<c>Enabled</c> 與 <c>AddonFeature._attributes</c> 都是實例狀態，另造一個實例的話
    /// 查詢恆回 <see langword="false"/>、停用是 no-op、啟用反而<b>再掛一組重複的監聽器</b>。
    /// <para>
    /// 📌 <b>回的是複合值</b>（與 <see cref="IsPluginEnabled"/> 同一套分工）：實例的啟用狀態、
    /// 還沒套用的 <see cref="SetBotherEnabled"/> 請求、以及 <see cref="PauseBother"/> 的暫停，
    /// 三者都要放行才回 <see langword="true"/>。暫停期間回 <see langword="false"/> 是既有契約，刻意保留。
    /// </para>
    /// </remarks>
    [EzIPC] public bool IsBotherEnabled(string name) => FindFeature(name) is { } feature && BotherPauses.EffectiveEnabled(feature);

    /// <remarks>
    /// 走 <see cref="BaseFeature.TrySetEnabled"/> 而不是直接 <c>Enable()</c>:對已經啟用的
    /// 功能再 <c>Enable()</c> 一次會掛出第二組監聽器(Dalamud 的 <c>RegisterListener</c> 不去重),
    /// 同一個事件就會被處理兩次。名稱找不到時什麼都不做。
    /// <para>
    /// 🔴 <b>切換本身在 framework 執行緒上做</b>（<see cref="BotherPauses"/>）：
    /// <c>Enable()</c>／<c>Disable()</c> 會去增刪 Dalamud <c>AddonLifecyclePluginScoped</c> 的
    /// <b>裸 <c>List</c></b>，而這支 IPC 跑在<b>呼叫端的執行緒</b>上（SomethingNeedDoing 的 Lua
    /// 巨集不在主執行緒），與使用者在設定視窗按下自訂回呼開關的那條路並行 ⇒ 那個 List 會壞掉。
    /// 📌 呼叫端觀察不到這一畫格的落差：<see cref="IsBotherEnabled"/> 與實際的按窗閘門都看「有效狀態」，寫進去<b>當下</b>就生效。
    /// </para>
    /// </remarks>
    [EzIPC]
    public void SetBotherEnabled(string name, bool state)
    {
        if (FindFeature(name) is not { } feature) return;
        BotherPauses.RequestSetEnabled(feature.Key, state);
    }

    /// <summary>暫停 YesAlready 指定的毫秒數。</summary>
    /// <remarks>
    /// 走 <see cref="SuppressionLeases.LegacyPause"/>：單一匿名租約，重疊的暫停到期時間<b>取 max</b>；
    /// 完全不碰使用者的開關 <c>C.Enabled</c>，顯示成「暫停」。
    /// 📌 契約：呼叫後 <see cref="IsPluginEnabled"/> 回 <see langword="false"/>、時間到後回 <see langword="true"/>。
    /// ⚠️ 超過 <see cref="SuppressionLeases.MaxLeaseMilliseconds"/> 會被夾短，而這條路沒有續約管道。
    /// </remarks>
    [EzIPC]
    public void PausePlugin(int milliseconds) => SuppressionLeases.LegacyPause(milliseconds);

    /// <summary>把<b>單一個</b> bother 暫停指定的毫秒數；時間一到自己恢復。</summary>
    /// <remarks>
    /// 🔴 只記一個到期時刻（<see cref="BotherPauses"/>），與 <see cref="PausePlugin"/> 同形；刻意不排進外掛共用的任務佇列，
    /// 理由逐條寫在 <see cref="BotherPauses"/> 的類別註解（卡住交納／派遣的按鈕序列、
    /// 被 <c>TaskManager.Abort()</c> 清掉之後<b>永遠</b>關著、跨執行緒寫裸 <c>List</c>）。
    /// <para>
    /// 📌 <b>契約</b>：呼叫後 <see cref="IsBotherEnabled"/> 回
    /// <see langword="false"/>、時間到之後回 <see langword="true"/>。
    /// </para>
    /// <para>
    /// ⚠️ ①重疊的暫停<b>取 max</b> 並回 <see langword="true"/>（暫停期間再呼叫一次是延長，不是被拒絕）
    /// ②要求的時間會被夾到 <see cref="SuppressionLeases.MaxLeaseMilliseconds"/>，
    /// 與 <see cref="PausePlugin"/> 同一套時間政策，<b>夾到時 log 看得見</b>。
    /// </para>
    /// </remarks>
    /// <returns><see langword="false"/>＝找不到這個名稱，或它本來就沒啟用（＝什麼都沒做）。</returns>
    [EzIPC]
    public bool PauseBother(string name, int milliseconds)
    {
        if (FindFeature(name) is not { } feature || !BotherPauses.EnabledIgnoringPause(feature))
            return false;

        BotherPauses.Pause(feature.Key, milliseconds);
        return true;
    }

    #endregion
}
