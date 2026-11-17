using System;
using System.Linq;
using YesAlready.IPC;

namespace YesAlready.BaseFeatures;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class AddonFeatureAttribute(AddonEvent eventType, string? addonName = null) : Attribute
{
    /// <summary>
    /// Name of the addon to register the listener for. Will use the class name if one is not provided.
    /// </summary>
    public string? AddonName { get; } = addonName;
    public AddonEvent EventType { get; } = eventType;
}

public abstract class AddonFeature : BaseFeature
{
    private AddonFeatureAttribute[]? _attributes;

    public override void Enable()
    {
        base.Enable();
        _attributes = [.. GetType().GetCustomAttributes(typeof(AddonFeatureAttribute), true).Cast<AddonFeatureAttribute>()];

        if (_attributes != null)
            foreach (var attr in _attributes)
                Svc.AddonLifecycle.RegisterListener(attr.EventType, attr.AddonName ?? GetType().Name, OnAddonEvent);
    }

    public override void Disable()
    {
        base.Disable();
        if (_attributes != null)
            foreach (var attr in _attributes)
                Svc.AddonLifecycle.UnregisterListener(OnAddonEvent);
    }

    protected virtual unsafe void OnAddonEvent(AddonEvent eventType, AddonArgs addonInfo)
    {
        // 🔴 IPC 的 PauseBother／SetBotherEnabled 不去拆監聽器（那是跨執行緒改 Dalamud 的
        // 裸 List），而是在這裡讓開。常態是兩個欄位讀取，沒有配置也沒有上鎖。
        if (!P.Active || !IsEnabled() || BotherPauses.IsMuted(Key)) return;
        HandleAddonEvent(eventType, addonInfo, (AtkUnitBase*)addonInfo.Addon.Address);
    }

    protected abstract unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk);
    protected abstract bool IsEnabled();

    /// <summary>與 <see cref="OnAddonEvent"/> 同一道總閘門。</summary>
    /// <remarks>
    /// 🔴 事件之後才跑的步驟（<c>Framework.Update</c> 處理器、排進 <c>Service.TaskManager</c> 的按鈕步驟）
    /// 不經 <see cref="OnAddonEvent"/>，必須在按下那一刻自己判這一道；否則 <c>PauseBother</c> 擋不到。
    /// </remarks>
    protected bool GateOpen => P.Active && IsEnabled() && !BotherPauses.IsMuted(Key);

    /// <summary>同一次事件排進 <c>Service.TaskManager</c> 的一串步驟共用的狀態。</summary>
    protected sealed class PressChain
    {
        public bool Abandoned;
    }

    /// <summary>這一串步驟還能不能繼續：閘門一關就整串放棄（之後閘門再開也不續），只在放棄那一刻寫一行 Information。</summary>
    /// <remarks>
    /// 🔴 等待步驟拿到 <see langword="false"/> 要回 <see langword="true"/>（結束等待、交給下一步放棄），
    /// 不回 <see langword="null"/>：null 會清掉整條共用佇列，連別的功能排的步驟一起丟。
    /// </remarks>
    protected bool ChainStillOwned(PressChain chain, string step)
    {
        if (chain.Abandoned) return false;
        if (GateOpen) return true;

        chain.Abandoned = true;
        PluginLog.Information($"[{Key}] 排隊中的步驟（{step}）執行前發現 YesAlready 已停用、這項功能被關閉／暫停，或被別的插件壓住：放棄這一串，不替你按。");
        return false;
    }

    protected void Log(string msg) => PluginLog.Debug($"[{GetType().Name}] {msg}");
    protected void LogVerbose(string message) => PluginLog.Verbose($"[{GetType().Name}] {message}");
    protected void LogError(string message) => PluginLog.Error($"[{GetType().Name}] {message}");

    /// <summary>Verbose 現在會不會被寫下（Dalamud 全域等級，或 ECommons 遊戲內紀錄）。</summary>
    /// <remarks>內插字串在 log 閘門之外求值：每幀都會走的比對路徑要先判再組字串。</remarks>
    protected static bool VerboseRecorded
        => global::Dalamud.EntryPoint.LogLevelSwitch.MinimumLevel <= Serilog.Events.LogEventLevel.Verbose
           || InternalLog.IsCaptureEnabled(Serilog.Events.LogEventLevel.Verbose);
}
