namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostDraw)]
internal class RetainerItemTransferList : AddonFeature
{
    protected override bool IsEnabled() => C.RetainerTransferListConfirm;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        // 🔴 這裡是 PostDraw：原本每一個繪製幀都無條件再按一次確認鈕，連 IsAddonReady 都沒有。
        // 確認鈕按下即關（轉到 RetainerItemTransferProgress），而關閉中的那幾幀 PostDraw 仍會進來
        // ⇒ 不擋就是對正在關閉的視窗一路連按。守衛記位址，一個實例只准按一次。
        if (!AddonPressGuard.TryBeginPress(addonInfo.AddonName, atk)) return;

        new AddonMaster.RetainerItemTransferList(atk).Confirm();
    }
}
