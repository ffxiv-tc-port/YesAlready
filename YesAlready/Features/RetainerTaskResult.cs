using Lumina.Excel.Sheets;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class RetainerTaskResult : AddonFeature
{
    protected override bool IsEnabled() => C.RetainerTaskResultEnabled;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.RetainerTaskResult>(out var am))
        {
            // ReassignButton 與它的文字節點在 setup 當下都可能還是 null,讀不到就這次不做事
            // (照原本判不出「召回」時的保守方向:不排下一輪派遣)。
            var reassignButton = am.ReassignButton;
            if (reassignButton == null || reassignButton->ButtonTextNode == null) return;

            var buttonText = reassignButton->ButtonTextNode->NodeText.GetText();
            if (buttonText == Svc.Data.GetExcelSheet<Addon>(Svc.ClientState.ClientLanguage).GetRow(2365).Text) // Recall
                return;

            // must be throttled, there's a little delay after setup before this is enabled.
            // 🔴 lambda 在後續影格才跑,addon 可能已拆 → IsEnabled 解的 OwnerNode 會是 null。
            // IsComponentEnabled 回 false 讓 TaskManager 繼續等,而不是丟無法攔截的 AVE。
            Service.TaskManager.Enqueue(() => GenericHelpers.IsComponentEnabled(am.ReassignButton));
            Service.TaskManager.Enqueue(am.Reassign);
        }
    }
}
