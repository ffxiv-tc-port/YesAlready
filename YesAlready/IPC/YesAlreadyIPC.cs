using ECommons.EzIpcManager;

namespace YesAlready.IPC;

public class YesAlreadyIPC
{
    public YesAlreadyIPC() => EzIPC.Init(this);

    [EzIPC] public bool IsPluginEnabled() => P.Active;

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

    [EzIPC]
    public void PausePlugin(int milliseconds)
    {
        C.Enabled = false;
        Service.TaskManager.EnqueueDelay(milliseconds);
        Service.TaskManager.Enqueue(() => C.Enabled = true);
    }

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
}
