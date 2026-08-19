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

            // 🔴 NameNode/DescNode 直接轉傳 GetTextNodeById 的結果,節點不存在時合法回 null
            // (ECommons 端保留可為 null 的語意是刻意的,判空的責任在這裡)。IsVisible() 是
            // [MemberFunction],對 null 呼叫等於把 this=0 交給遊戲原生碼當場 AccessViolation,
            // 而 AVE 是 corrupted-state exception,try/catch 攔不到。
            // 讀不出來時這次不做事、等下一次事件再說 —— 與下面 NextButton 的三態處理同一個原則:
            // 不能把「不知道」當成「已確認不可見」。
            var nameNode = am.NameNode;
            var descNode = am.DescNode;
            if (nameNode == null || descNode == null) return;
            if (!nameNode->IsVisible() || !descNode->IsVisible()) return;

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
