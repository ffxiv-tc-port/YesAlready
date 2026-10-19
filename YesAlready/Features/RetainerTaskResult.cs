using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

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
            // (保守方向:判不出是不是「召回」就不排下一輪派遣)。
            var reassignButton = am.ReassignButton;
            if (reassignButton == null || reassignButton->ButtonTextNode == null) return;

            // 🔴 兩端剝 SeString 的實作必須是同一套(同 PurifyResult):下面那個 == 的右邊是 Lumina 的 ReadOnlySeString,
            // 左邊是 string ⇒ 會被包回 ReadOnlySeString 做**原始位元組序列比對**,鈕上的字只要帶一個 payload 就恆不相等,
            // 不擲例外、不寫 log,表現是「明明按到召回卻還是排了下一輪派遣」。所以左邊也走 Lumina 的 ExtractText()。
            var buttonText = new ReadOnlySeStringSpan(reassignButton->ButtonTextNode->NodeText.AsSpan()).ExtractText();
            if (buttonText == Svc.Data.GetExcelSheet<Addon>(Svc.ClientState.ClientLanguage).GetRow(2365).Text.ExtractText()) // Recall
                return;

            // must be throttled, there's a little delay after setup before this is enabled.
            // 🔴🔴 這兩筆在後續影格才跑,所以絕對不能把 am 捕獲進去(同 RetainerTaskAsk):AddonMaster 的 Addon 在建構的當下就凍結,
            // 捕獲它等於跨幀保存原生指標,視窗被拆掉之後就是攔不到的 AccessViolationException。
            // 正解＝排入佇列時只留「要做什麼」,執行的當下才重新解析視窗。
            // 📌 重解不會對到別的一扇:上面這支 TryGetAddonMaster 本來就是照型別名稱查 index 1。
            Service.TaskManager.Enqueue(IsReassignEnabled);
            Service.TaskManager.Enqueue(Reassign);
        }
    }

    /// <summary>執行的當下重新解析視窗,再判再派遣鈕能不能按。</summary>
    /// <remarks>
    /// 視窗不在、或按鈕(含它的 <c>OwnerNode</c>)還沒好時回 <see langword="false"/>,讓 TaskManager
    /// 繼續等,而不是丟無法攔截的 <c>AccessViolationException</c>。
    /// </remarks>
    private static unsafe bool IsReassignEnabled()
        => GenericHelpers.TryGetAddonMaster<AddonMaster.RetainerTaskResult>(out var am)
           && GenericHelpers.IsComponentEnabled(am.ReassignButton);

    /// <summary>執行的當下重新解析視窗再按下去;視窗不在就這一發不送。</summary>
    /// <remarks>維持 <c>Action</c> 型任務(一次性、跑完就算完成)。</remarks>
    private static void Reassign()
    {
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.RetainerTaskResult>(out var am))
            am.Reassign();
    }
}
