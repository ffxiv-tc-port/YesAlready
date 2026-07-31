using Dalamud.Memory;
using Lumina.Excel.Sheets;
using System.Linq;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostUpdate)]
internal class PurifyResult : AddonFeature
{
    protected override bool IsEnabled() => C.AetherialReductionResults;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (!GenericHelpers.IsAddonReady(atk)) return;

        if (MemoryHelper.ReadSeString(&atk->GetTextNodeById(2)->NodeText).GetText() == Svc.Data.GetExcelSheet<Addon>().GetRow(2171).Text)   // 原為 First(x => x.RowId == 2171):O(n) 全表掃描找主鍵,GetRow 是索引查詢
        {
            PluginLog.Debug("Closing Purify Results menu");
            Callback.Fire(atk, true, -1);
        }
    }
}
