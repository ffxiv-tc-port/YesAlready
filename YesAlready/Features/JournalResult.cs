using YesAlready.IPC;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class JournalResult : AddonFeature
{
    protected override bool IsEnabled() => C.JournalResultCompleteEnabled;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        // TextAdvance 也會按「完成」（等按鈕啟用才按，不會搶在選獎勵之前交出）；它會處理時讓位，同一扇窗只有一個主人。
        if (TextAdvanceIpc.Query(TextAdvanceIpc.QuestComplete, nameof(JournalResult)) != TextAdvanceIpc.Handling.NotHandling)
            return;
        new AddonMaster.JournalResult(atk).Complete();
    }
}
