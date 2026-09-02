using ECommons.Throttlers;

namespace YesAlready.Features;

/// <summary>
/// 精選結果視窗出現時，替你按下視窗上的「自動」鈕——之後整批精選由<b>遊戲自己</b>跑完。
/// </summary>
/// <remarks>
/// <para>
/// 對應 DailyRoutines 的 AutoAetherialReduction。DR 的作法是在外掛端自己開一條迴圈：
/// 反覆讀 <c>AgentPurify.ReducibleItems</c> 取出第一件、呼叫 <c>ReduceItem</c>、延遲 1 秒、再來一次，
/// 中間自己判斷背包滿／騎乘／戰鬥／OccupiedInEvent。
/// </para>
/// <para>
/// 這裡沒有照抄，因為<b>遊戲本身就有「自動精選」</b>：精選結果視窗（PurifyResult）上的
/// 「自動」鈕（節點 19）按下去之後，遊戲會自己把所有可精選的道具跑完，並顯示自己的進度視窗
/// （PurifyAutoDialog，附取消鈕）。用遊戲內建的批次功能，就不需要外掛端的計時迴圈、
/// 不需要自己重算背包狀態、也不會有「送太快被拒」的競態——同時使用者隨時能按遊戲自己的取消鈕停下。
/// </para>
/// <para>
/// 觸發點仍然是使用者自己動手：要先由使用者精選第一件道具、讓結果視窗出現，這個功能才會接手，
/// 與本外掛其他功能（視窗出現→按下某顆鈕）的形狀一致。預設關閉。
/// </para>
/// </remarks>
[AddonFeature(AddonEvent.PostUpdate, "PurifyResult")]
internal class PurifyResultAutomatic : AddonFeature
{
    protected override bool IsEnabled() => C.AetherialReductionAutomatic;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (!GenericHelpers.IsAddonReady(atk)) return;

        var master = new AddonMaster.PurifyResult(atk);

        // ⚠️ 節點不在就會解參考空指標。AutomaticButton 是每次都重查的屬性，取兩次等於 TOCTOU，
        // 先快取成區域變數；IsComponentEnabled 會一路擋掉 button／OwnerNode 的 null
        // （IsEnabled 解的是 OwnerNode，不是 AtkResNode），任一層讀不出來就當成「這次不做事」。
        var automaticButton = master.AutomaticButton;
        if (!GenericHelpers.IsComponentEnabled(automaticButton)) return;

        // PostUpdate 每幀都會進來；按鈕按下後到視窗換掉之間會有數幀空窗
        if (!EzThrottler.Throttle("YesAlready.PurifyResultAutomatic", 1000)) return;

        // 🔴 節流不是防護：它記的是「上一次動作在哪個時刻」而不是「這扇窗按過了」。
        // 按下之後這扇窗就要被 PurifyAutoDialog 換掉，那段關閉中的幀 IsComponentEnabled 也不可信。
        // 空參數鍵＝與 PurifyResult 的關窗 -1 併成同一筆，同一個實例只准按一次。
        if (!AddonPressGuard.TryBeginPress(addonInfo.AddonName, atk)) return;

        Log("Pressing the automatic aetherial reduction button");
        master.Automatic();
    }
}
