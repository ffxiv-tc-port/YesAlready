namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class RetainerTaskAsk : AddonFeature
{
    protected override bool IsEnabled() => C.RetainerTaskAskEnabled;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.RetainerTaskAsk>(out _))
        {
            // must be throttled, there's a little delay after setup before this is enabled.
            // 🔴🔴 這兩筆最快也是下一幀才跑,所以絕對不能把 am 捕獲進去:AddonMaster 的 Addon 是
            // get-only 自動屬性,值在建構的當下就凍結、永不重解析 —— 捕獲它等於跨幀保存原生指標。
            // 視窗在這之間被拆掉的話,AssignButton 讀的是已經釋放的記憶體,而那是
            // AccessViolationException(corrupted-state exception),try/catch 完全攔不到。
            // 正解＝排入佇列時只留「要做什麼」,執行的當下才重新解析視窗。
            // 📌 重解不會對到別的一扇:上面這支 TryGetAddonMaster 本來就是照型別名稱查 index 1,
            // 下面兩支重查的是同一個入口 —— 原本被捕獲的那個指標也是從它來的。
            Service.TaskManager.Enqueue(IsAssignEnabled);
            Service.TaskManager.Enqueue(Assign);
        }
    }

    /// <summary>執行的當下重新解析視窗,再判派遣鈕能不能按。</summary>
    /// <remarks>
    /// 視窗不在、或按鈕(含它的 <c>OwnerNode</c>)還沒好時回 <see langword="false"/>,讓 TaskManager
    /// 照既有的等待/重試邏輯繼續等(收不了場時由它自己的 30 秒逾時清掉整條佇列),
    /// 而不是變成無法攔截的 <c>AccessViolationException</c>。
    /// </remarks>
    private static unsafe bool IsAssignEnabled()
        => GenericHelpers.TryGetAddonMaster<AddonMaster.RetainerTaskAsk>(out var am)
           && GenericHelpers.IsComponentEnabled(am.AssignButton);

    /// <summary>執行的當下重新解析視窗再按下去;視窗不在就這一發不送。</summary>
    /// <remarks>維持 <c>Action</c> 型任務(一次性、跑完就算完成),與改動前的 <c>Enqueue(am.Assign)</c> 一致。</remarks>
    private static void Assign()
    {
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.RetainerTaskAsk>(out var am))
            am.Assign();
    }
}
