using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class ItemInspectionResult : AddonFeature
{
    private int itemInspectionCount = 0;

    protected override bool IsEnabled() => C.ItemInspectionResultEnabled;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.ItemInspectionResult>(out var am))
        {
            if (am.Base->UldManager.NodeListCount < 64) return;
            if (!am.NameNode->IsVisible() || !am.DescNode->IsVisible()) return;

            // This is hackish, but works well enough (for now).
            // Languages that dont contain the magic character will need special handling.
            if (am.Description.TextValue.Contains('※') || am.Description.TextValue.Contains("liées à Garde-la-Reine"))
            {
                am.ItemName.Payloads.Insert(0, new TextPayload("Received: "));
                Svc.Chat.PrintPluginMessage(am.ItemName);
            }

            itemInspectionCount++;
            var rateLimiter = C.ItemInspectionResultRateLimiter;
            if (rateLimiter != 0 && itemInspectionCount % rateLimiter == 0)
            {
                itemInspectionCount = 0;
                Svc.Chat.PrintPluginMessage("Rate limited, pausing item inspection loop.");
                return;
            }

            // 🔴 IsEnabled 解的是 OwnerNode(不是 AtkResNode),兩者都可能是 null。
            // 這裡是三態:可按→下一件、不可按→關窗、「讀不出來」→這次不做事,等下一次事件再說。
            // 讀不出來時不能落到 Close(),那會把「不知道」當成「已確認不可按」。
            var nextButton = am.NextButton;
            if (nextButton == null || nextButton->OwnerNode == null) return;

            if (nextButton->IsEnabled)
                am.Next();
            else
                am.Close();
        }
    }
}
