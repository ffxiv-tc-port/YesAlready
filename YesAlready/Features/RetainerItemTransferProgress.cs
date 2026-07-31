using Dalamud.Memory;
using Lumina.Excel.Sheets;
using System.Linq;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostUpdate)]
internal class RetainerItemTransferProgress : AddonFeature
{
    protected override bool IsEnabled() => C.RetainerTransferProgressConfirm;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (!GenericHelpers.TryGetAddonMaster<AddonMaster.RetainerItemTransferProgress>(out var am)) return;

        if (MemoryHelper.ReadSeStringNullTerminated(new nint(am.Base->AtkValues[0].String)).GetText() == Svc.Data.GetExcelSheet<Addon>().GetRow(13528).Text)   // 原為 First(x => x.RowId == 13528):O(n) 全表掃描找主鍵,GetRow 是索引查詢
        {
            PluginLog.Debug("Closing Entrust Duplicates menu");
            am.Close();
        }
    }
}
