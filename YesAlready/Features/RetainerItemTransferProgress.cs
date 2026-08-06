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

        // 🔴 AtkValues 越界讀到的是堆積垃圾不是 null ⇒ 判空擋不住；這一格是字串指標，
        // 越界後 ReadSeStringNullTerminated 會對任意位址跑 strlen → 攔不到的 AVE。
        // 這是 PostUpdate（每幀），所以讀不到就直接離開，下一幀重來。
        // ⚠️ 連空指標一起擋：MemoryHelper.ReadSeStringNullTerminated 對位址 0 會直接跑 strlen，
        // 那同樣是 AVE。原本的寫法只是「剛好」拿到有效指標，不是有人擋過。
        var titleValue = AtkValueSafety.Get(am.Base, 0);
        if (titleValue == null || titleValue->String.Value == null) return;

        if (MemoryHelper.ReadSeStringNullTerminated(new nint(titleValue->String.Value)).GetText() == Svc.Data.GetExcelSheet<Addon>().GetRow(13528).Text)   // 原為 First(x => x.RowId == 13528):O(n) 全表掃描找主鍵,GetRow 是索引查詢
        {
            PluginLog.Debug("Closing Entrust Duplicates menu");
            am.Close();
        }
    }
}
