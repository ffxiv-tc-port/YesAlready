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
            // 🔴🔴 這兩筆在後續影格才跑,所以絕對不能把 am 捕獲進去:AddonMaster 的 Addon 是
            // get-only 自動屬性,值在建構的當下就凍結、永不重解析 —— 捕獲它等於跨幀保存原生指標。
            // 視窗在這之間被拆掉的話,ReassignButton 讀的是已經釋放的記憶體,而那是
            // AccessViolationException(corrupted-state exception),try/catch 完全攔不到。
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
    /// <remarks>維持 <c>Action</c> 型任務,與改動前的 <c>Enqueue(am.Reassign)</c> 一致。</remarks>
    private static void Reassign()
    {
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.RetainerTaskResult>(out var am))
            am.Reassign();
    }
}
