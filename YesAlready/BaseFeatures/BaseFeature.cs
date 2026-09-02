namespace YesAlready.BaseFeatures;

public abstract class BaseFeature
{
    public virtual bool Enabled { get; protected set; }
    public virtual string Key => GetType().Name;

    /// <summary>啟用這個功能(<c>AddonFeature</c> 會在這裡掛上 <c>AddonLifecycle</c> 監聽器)。</summary>
    /// <remarks>
    /// 🔴 <b>這支不是冪等的。</b>對<b>已經啟用</b>的實例再呼叫一次,<c>AddonFeature.Enable</c>
    /// 會再掛一組一模一樣的監聽器 —— Dalamud 的 <c>RegisterListener</c> 一律 <c>Add</c>、
    /// <b>完全不去重</b>(<c>AddonLifecyclePluginScoped.RegisterListener</c>),
    /// 結果是同一個 addon 事件被處理兩次,也就是同一扇窗被按兩下。
    /// ⇒ <b>不確定目前狀態時一律改用 <see cref="TrySetEnabled"/>。</b>
    /// </remarks>
    public virtual void Enable()
    {
        PluginLog.Debug($"Enabling {Key}");
        Enabled = true;
    }

    /// <summary>停用這個功能(<c>AddonFeature</c> 會在這裡拆掉監聽器)。</summary>
    /// <remarks>對<b>還沒啟用</b>的實例呼叫是無害的,但同樣建議走 <see cref="TrySetEnabled"/>。</remarks>
    public virtual void Disable()
    {
        PluginLog.Debug($"Disabling {Key}");
        Enabled = false;
    }

    /// <summary>把啟用狀態切到 <paramref name="state"/>;<b>已經是那個狀態就什麼都不做</b>。</summary>
    /// <returns>真的切換了回 <see langword="true"/>;本來就是那個狀態回 <see langword="false"/>。</returns>
    /// <remarks>
    /// 🔴 這是 <see cref="Enable"/>／<see cref="Disable"/> 的冪等包裝。從外面進來的呼叫(IPC)
    /// 現在拿到的是<b>真正掛著監聽器的那一份實例</b>,重複 <c>Enable()</c> 會掛出第二組監聽器
    /// 而且沒有任何徵兆,所以那條路徑一律走這支。
    /// </remarks>
    public bool TrySetEnabled(bool state)
    {
        if (Enabled == state) return false;

        if (state)
            Enable();
        else
            Disable();

        return true;
    }
}
