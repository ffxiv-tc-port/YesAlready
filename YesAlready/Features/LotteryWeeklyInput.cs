using System;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class LotteryWeeklyInput : AddonFeature
{
    protected override bool IsEnabled() => C.LotteryWeeklyInput;

    /// <summary>遊戲允許的號碼範圍是 0000–9999。</summary>
    private const int MaxNumber = 9999;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
        => Callback.Fire(atk, true, PickNumber());

    private static int PickNumber() => C.LotteryWeeklyNumberMode switch
    {
        Configuration.JumboCactpotNumberMode.Fixed => Math.Clamp(C.LotteryWeeklyFixedNumber, 0, MaxNumber),
        _ => new Random().Next(0, MaxNumber + 1),
    };
}
