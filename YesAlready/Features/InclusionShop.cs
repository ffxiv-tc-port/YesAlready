using System;
using Dalamud.Plugin.Services;
using ECommons.EzHookManager;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class InclusionShop : AddonFeature
{
    protected override bool IsEnabled() => C.InclusionShopRememberEnabled;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (!GenericHelpers.IsAddonReady(atk)) return;

        PluginLog.Debug($"Firing 12,{C.InclusionShopRememberCategory}");
        using var categoryValues = new AtkValueArray(12, C.InclusionShopRememberCategory);
        atk->FireCallback(2, categoryValues);

        PluginLog.Debug($"Firing 13,{C.InclusionShopRememberSubcategory}");
        using var subcategoryValues = new AtkValueArray(13, C.InclusionShopRememberSubcategory);
        atk->FireCallback(2, subcategoryValues);
    }

    [EzHook("40 53 48 83 EC ?? 48 8B DA 4D 8B D0", detourName: nameof(AgentReceiveEventDetour), true)]
    private readonly EzHook<AgentReceiveEventDelegate> agentReceiveEventHook = null!;
    private unsafe delegate IntPtr AgentReceiveEventDelegate(IntPtr agent, IntPtr eventData, AtkValue* values, uint valueCount, ulong eventKind);

    /// <summary>detour 記下了新分類、還沒寫進設定檔。存檔在下一次 <c>Framework.Update</c> 做，不在遊戲的 hook 裡做檔案 I/O。</summary>
    private volatile bool savePending;

    private bool detourFaultLogged;

    public InclusionShop()
    {
        EzSignatureHelper.Initialize(this);
        // 只在載入時印一次；沒掛到時 detour 不會被呼叫，其餘照常。
        if (agentReceiveEventHook == null) PluginLog.Warning("[YesAlready] 特徵碼對不上（InclusionShop.agentReceiveEventHook）：記不到 InclusionShop 視窗上次選的分類，開啟時沿用先前記下的那一組。");
        // 與 detour 同壽命（不跟 Enable／Disable）：detour 不論功能開關都在記，存檔也要照舊不論開關。
        else Svc.Framework.Update += FlushPendingSave;
    }

    /// <remarks>🔴 不論記錄那段出什麼事，一定呼叫一次原函式（不呼叫＝吃掉遊戲的分類切換事件）。</remarks>
    private unsafe IntPtr AgentReceiveEventDetour(IntPtr agent, IntPtr eventData, AtkValue* values, uint valueCount, ulong eventKind)
    {
        try
        {
            Remember(values, valueCount);
        }
        catch (Exception ex)
        {
            if (!detourFaultLogged)
            {
                detourFaultLogged = true;
                PluginLog.Error($"[InclusionShop] 記錄分類時出錯（之後同樣的錯不再寫）：{ex}");
            }
        }

        return agentReceiveEventHook.Original(agent, eventData, values, valueCount, eventKind);
    }

    private unsafe void Remember(AtkValue* values, uint valueCount)
    {
        if (valueCount != 2)
            return;

        var atkValue0 = values[0];
        if (atkValue0.Type != ValueType.Int)
            return;

        var val0 = atkValue0.Int;
        if (val0 == 12)
        {
            var val1 = values[1].UInt;
            if (val1 != C.InclusionShopRememberCategory)
            {
                PluginLog.Debug($"Remembring InclusionShop category: {val1}");
                C.InclusionShopRememberCategory = val1;
                C.InclusionShopRememberSubcategory = 0;
                savePending = true;
            }
        }
        else if (val0 == 13)
        {
            var val1 = values[1].UInt;
            if (val1 != C.InclusionShopRememberSubcategory)
            {
                PluginLog.Debug($"Remembring InclusionShop subcategory: {val1}");
                C.InclusionShopRememberSubcategory = val1;
                savePending = true;
            }
        }
    }

    /// <remarks>同一插件的 <c>Framework.Update</c> 處理器共用一個 try/catch：這裡擲出去會讓排在後面的整幀不跑，所以自己接住。</remarks>
    private void FlushPendingSave(IFramework framework)
    {
        if (!savePending) return;
        savePending = false;

        try
        {
            C.Save();
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[InclusionShop] 記下的分類存檔失敗（下次切換分類或任何設定存檔時會再寫一次）：{ex.Message}");
        }
    }
}
