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
        // 🔴 IPC 的 PauseBother／SetBotherEnabled 不再去拆監聽器（那是跨執行緒改 Dalamud 的
        // 裸 List），改成在這裡讓開。常態是兩個欄位讀取，沒有配置也沒有上鎖。
        if (!P.Active || !IsEnabled() || BotherPauses.IsMuted(Key)) return;
        HandleAddonEvent(eventType, addonInfo, (AtkUnitBase*)addonInfo.Addon.Address);
    }

    protected abstract unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk);
    protected abstract bool IsEnabled();

    protected void Log(string msg) => PluginLog.Debug($"[{GetType().Name}] {msg}");
    protected void LogVerbose(string message) => PluginLog.Verbose($"[{GetType().Name}] {message}");
    protected void LogError(string message) => PluginLog.Error($"[{GetType().Name}] {message}");
}
