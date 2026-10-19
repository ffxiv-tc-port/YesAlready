using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace YesAlready.Features;

/// <summary>
/// 分解確認視窗：替你按「分解」；另可先勾遊戲自己的「持續分解到持有數量歸零」（預設關）。
/// </summary>
/// <remarks>
/// 兩個勾選框欄位的用途不固定：道具帶警示（珍稀／鑲嵌魔晶石／收藏品）時 0x248 是警示確認框、
/// 整組分解框排到 0x250；沒有警示時整組分解框在 0x248；持有數量只有 1 個時根本不接。
/// 而且兩個框是在 PostSetup 之後（vf50 → OnRefresh）才接上事件的，所以要勾的時候，
/// 確認改到同一實例的第一個 PostUpdate 才按；沒開這個選項時照舊在 PostSetup 按。
/// </remarks>
[AddonFeature(AddonEvent.PostSetup)]
[AddonFeature(AddonEvent.PostUpdate)]
internal class SalvageDialog : AddonFeature
{
    /// <summary>整組分解框的 ButtonClick 參數；視窗收到它才會回報 callback 13 給 AgentSalvage。</summary>
    private const uint BulkToggleEventParam = 4;

    /// <summary>整組分解框已接上但還沒顯示時，最多等這麼多幀。</summary>
    private const long BulkCheckboxWaitFrames = 10;

    /// <summary>PostSetup 之後超過這麼多幀才輪到處理＝中間被暫停或停用過，那一筆作廢不按。</summary>
    private const long PendingExpireFrames = 90;

    // 只在框架執行緒讀寫；位址只做等值比較，從不解參。
    private nint pendingAddon;
    private long pendingSinceFrame;

    protected override bool IsEnabled() => C.DesynthDialogEnabled;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (eventType == AddonEvent.PostUpdate)
        {
            ContinueDeferredConfirm(atk);
            return;
        }

        pendingAddon = 0;
        if (!GenericHelpers.IsAddonReady(atk)) return;

        if (GenericHelpers.TryGetAddonMaster<AddonMaster.SalvageDialog>(out var am))
        {
            switch (eventType)
            {
                case AddonEvent.PostSetup:
                    if (C.DesynthDialogEnabled)
                    {
                        if (C.DesynthBulkDialogEnabled)
                        {
                            pendingAddon = (nint)atk;
                            pendingSinceFrame = AddonPressGuard.CurrentFrame;
                            break;
                        }

                        am.Checkbox();
                        am.Desynthesize();
                    }
                    break;
            }
        }
    }

    private unsafe void ContinueDeferredConfirm(AtkUnitBase* atk)
    {
        if (pendingAddon == 0 || pendingAddon != (nint)atk) return;

        var waited = AddonPressGuard.CurrentFrame - pendingSinceFrame;
        if (waited > PendingExpireFrames)
        {
            pendingAddon = 0;
            PluginLog.Information($"[{nameof(SalvageDialog)}] 分解確認視窗開啟 {waited} 幀後才輪到處理（中間被暫停或停用過），這次不替你按。");
            return;
        }

        if (!GenericHelpers.IsAddonReady(atk)) return;

        // 「分解」的處理常式直接解參 DesynthItemSlot；延後的這幾幀裡若已被取消（欄位清空），按下去就是存取違規。
        var agent = AgentSalvage.Instance();
        if (agent == null || agent->DesynthItemSlot == null)
        {
            pendingAddon = 0;
            PluginLog.Information($"[{nameof(SalvageDialog)}] 分解確認視窗已經沒有待分解的道具（可能已被取消），這次不替你按。");
            return;
        }

        if (C.DesynthBulkDialogEnabled && !TryTickBulk(atk, waited)) return;

        pendingAddon = 0;
        var am = new AddonMaster.SalvageDialog(atk);
        am.Checkbox();
        am.Desynthesize();
    }

    /// <returns><see langword="false"/>＝整組分解框已接上但還沒顯示，下一幀再看；其餘情況都回 <see langword="true"/>。</returns>
    private unsafe bool TryTickBulk(AtkUnitBase* atk, long waited)
    {
        var addon = (AddonSalvageDialog*)atk;
        var listener = (AtkEventListener*)atk;
        AtkEvent* clickEvent;
        var checkbox = addon->BulkDesynthCheckboxNode;
        if (!IsWiredAsBulk(checkbox, listener, out clickEvent))
        {
            checkbox = addon->UnkCheckboxNode;
            if (!IsWiredAsBulk(checkbox, listener, out clickEvent))
            {
                PluginLog.Information($"[{nameof(SalvageDialog)}] 這次的分解確認視窗沒有「持續分解到持有數量歸零」（持有數量只有 1 個時遊戲不提供），不勾選，照常分解。");
                return true;
            }
        }

        if (!checkbox->OwnerNode->AtkResNode.IsVisible())
        {
            if (waited < BulkCheckboxWaitFrames) return false;
            PluginLog.Information($"[{nameof(SalvageDialog)}] 「持續分解到持有數量歸零」的勾選框等了 {waited} 幀仍未顯示，不勾選，照常分解。");
            return true;
        }

        if (checkbox->IsChecked)
        {
            Log("「持續分解到持有數量歸零」本來就是勾選狀態");
            return true;
        }

        // 與實際點擊同一條路：元件先變成勾選，再把它掛著的 ButtonClick 交給視窗，
        // 視窗讀到勾選狀態才回報 callback 13；只改外觀不會讓遊戲整組分解。
        checkbox->SetChecked(true);
        var eventData = new AtkEventData();
        atk->ReceiveEvent(clickEvent->State.EventType, (int)clickEvent->Param, clickEvent, &eventData);
        PluginLog.Information($"[{nameof(SalvageDialog)}] 已勾選「持續分解到持有數量歸零」。");
        return true;
    }

    private static unsafe bool IsWiredAsBulk(AtkComponentCheckBox* checkbox, AtkEventListener* listener, out AtkEvent* clickEvent)
    {
        clickEvent = null;
        if (checkbox == null || checkbox->OwnerNode == null) return false;

        var owner = &checkbox->OwnerNode->AtkResNode;
        for (var evt = owner->AtkEventManager.Event; evt != null; evt = evt->NextEvent)
        {
            if (evt->State.EventType == AtkEventType.ButtonClick && evt->Param == BulkToggleEventParam
                && evt->Listener == listener && evt->Target == (AtkEventTarget*)owner)
            {
                clickEvent = evt;
                return true;
            }
        }

        return false;
    }
}
