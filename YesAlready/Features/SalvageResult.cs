namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
[AddonFeature(AddonEvent.PostUpdate, "SalvageAutoDialog")]
internal class SalvageResult : AddonFeature
{
    protected override bool IsEnabled() => C.DesynthesisResults;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (!GenericHelpers.IsAddonReady(atk)) return;

        switch (addonInfo.AddonName)
        {
            case "SalvageResult":
                new AddonMaster.SalvageResult(atk).Close();
                break;

            case "SalvageAutoDialog":
                // 🔴「結束分解」按下即關對話框，而這是 PostUpdate：關閉中的那幾幀按鈕文字仍是同一列、
                // 三關也全過 ⇒ 不擋就是每個關閉幀再按一次。鍵取實際被按的那個指標。
                if (GenericHelpers.TryGetAddonMaster<AddonMaster.SalvageAutoDialog>(out var am)
                    && am.DesynthesisInactive
                    && AddonPressGuard.TryBeginPress("SalvageAutoDialog", am.Base))
                {
                    am.EndDesynthesis();
                }
                break;
        }
    }
}
