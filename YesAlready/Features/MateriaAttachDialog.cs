using Dalamud.Game.ClientState.Conditions;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class MateriaAttachDialog : AddonFeature
{
    protected override bool IsEnabled() => C.MaterialAttachDialogEnabled;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.MateriaAttachDialog>(out var am))
        {
            if (!IsSelfMeld(am.Base, string.Empty))
                return;

            if (C.OnlyMeldWhenGuaranteed && !IsGuaranteed(am.SuccessRateText))
                return;

            Service.TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.MeldingMateria]);

            // 🔴🔴 am.Meld 這個方法群組會把 am 一起帶進委派,而任務最快也是下一幀才跑。
            // AddonMaster 的 Addon 是 get-only 自動屬性,值在建構的當下就凍結、永不重解析 ——
            // 帶著它跨幀等於跨幀保存原生指標,視窗被拆掉之後 GetComponentButtonById 讀的是
            // 已經釋放的記憶體,而那是 try/catch 攔不到的 AccessViolationException。
            // 📌 重解不會對到別的一扇:上面這支 TryGetAddonMaster 本來就是照型別名稱查 index 1。
            Service.TaskManager.Enqueue(TryMeld);
        }
    }

    /// <summary>
    /// 同一幀讀開窗模式，只有自己鑲嵌才回 <see langword="true"/>；委託、接受委託、讀不出來都不按。
    /// 不按時寫一行 Information，附讀到的模式；<paramref name="when"/> 是那行的開頭。
    /// </summary>
    private static unsafe bool IsSelfMeld(AtkUnitBase* addon, string when)
    {
        var mode = AtkValueSafety.Get(addon, MeldDialogMode.AtkValueIndex);
        if (mode == null)
        {
            PluginLog.Information($"[{nameof(MateriaAttachDialog)}] {when}讀不到鑲嵌確認視窗的模式（AtkValues 只有 {AtkValueSafety.CountOf(addon)} 格），當成不是你自己鑲嵌，這次不替你按。");
            return false;
        }

        var type = mode->Type;
        var raw = mode->UInt;
        if (!MeldDialogMode.IsSelfMeld(type, raw))
        {
            PluginLog.Information($"[{nameof(MateriaAttachDialog)}] {when}這扇鑲嵌確認視窗是{MeldDialogMode.Describe(type, raw)}，不是你自己鑲嵌，這次不替你按。");
            return false;
        }

        return true;
    }

    /// <summary>成功率讀得出來而且恰好是 100% 才回 <see langword="true"/>；讀不出來當成沒有保證。</summary>
    /// <remarks>不用 ECommons 的 <c>SuccessRateFloat</c>：它把數字後面接上 <c>.-</c> 再解析，任何輸入都擲例外。</remarks>
    private static bool IsGuaranteed(string text)
    {
        if (!MeldSuccessRate.TryParse(text, out var rate))
        {
            PluginLog.Information($"[{nameof(MateriaAttachDialog)}] 讀不出鑲嵌成功率（原文「{text}」），當成沒有保證，這次不替你按。");
            return false;
        }

        if (rate != 100f)
        {
            PluginLog.Information($"[{nameof(MateriaAttachDialog)}] 鑲嵌成功率「{text}」不到 100%，這次不替你按。");
            return false;
        }

        PluginLog.Information($"[{nameof(MateriaAttachDialog)}] 鑲嵌成功率「{text}」，準備替你按下「鑲嵌」。");
        return true;
    }

    /// <summary>執行的當下重新解析視窗再按下鑲嵌;視窗不在就這一發不送。</summary>
    /// <remarks>維持 <c>Action</c> 型任務(一次性、跑完就算完成)。</remarks>
    private static unsafe void TryMeld()
    {
        if (!GenericHelpers.TryGetAddonMaster<AddonMaster.MateriaAttachDialog>(out var am))
            return;

        if (!IsSelfMeld(am.Base, "按下「鑲嵌」前再讀一次，"))
            return;

        // 按下前同一幀再讀一次：開窗那次與這次都是確定的 100% 才按；不是就這一發不送，也不重試。
        if (C.OnlyMeldWhenGuaranteed)
        {
            var text = am.SuccessRateText;
            if (!MeldSuccessRate.IsGuaranteed(text))
            {
                PluginLog.Information($"[{nameof(MateriaAttachDialog)}] 按下「鑲嵌」前再讀一次，鑲嵌成功率是「{text}」，不是確定的 100%，這次不替你按。");
                return;
            }
        }

        am.Meld();
    }
}
