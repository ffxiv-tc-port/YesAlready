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
            if (C.OnlyMeldWhenGuaranteed && am.SuccessRateFloat < 100)
            {
                PluginLog.Debug($"Success rate {am.SuccessRateFloat} less than 100%, aborting meld.");
                return;
            }

            Service.TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.MeldingMateria]);

            // 🔴🔴 am.Meld 這個方法群組會把 am 一起帶進委派,而任務最快也是下一幀才跑。
            // AddonMaster 的 Addon 是 get-only 自動屬性,值在建構的當下就凍結、永不重解析 ——
            // 帶著它跨幀等於跨幀保存原生指標,視窗被拆掉之後 GetComponentButtonById 讀的是
            // 已經釋放的記憶體,而那是 try/catch 攔不到的 AccessViolationException。
            // 📌 重解不會對到別的一扇:上面這支 TryGetAddonMaster 本來就是照型別名稱查 index 1。
            Service.TaskManager.Enqueue(TryMeld);
        }
    }

    /// <summary>執行的當下重新解析視窗再按下鑲嵌;視窗不在就這一發不送。</summary>
    /// <remarks>維持 <c>Action</c> 型任務,與改動前的 <c>Enqueue(am.Meld)</c> 一致。</remarks>
    private static void TryMeld()
    {
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.MateriaAttachDialog>(out var am))
            am.Meld();
    }
}
