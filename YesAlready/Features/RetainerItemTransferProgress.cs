using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
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
        // 越界後 AsSpan() 會對任意位址跑 strlen → 攔不到的 AVE。
        // 這是 PostUpdate（每幀），所以讀不到就直接離開，下一幀重來。
        // ⚠️ 空指標也在這裡擋掉：標題讀不到就沒有東西可比，直接離開。
        var titleValue = AtkValueSafety.Get(am.Base, 0);
        if (titleValue == null || titleValue->String.Value == null) return;

        // 🔴 兩端剝 SeString 的實作必須是同一套(同 PurifyResult):右邊是 Lumina 的 ReadOnlySeString,
        // string 與它比會被包回 ReadOnlySeString 做原始位元組比對,標題帶任何 payload 就恆為 false 且完全沒有徵兆
        // ⇒ 兩端都走 Lumina 的 ExtractText()。
        if (new ReadOnlySeStringSpan(titleValue->String.AsSpan()).ExtractText() == Svc.Data.GetExcelSheet<Addon>().GetRow(13528).Text.ExtractText())   // GetRow 是索引查詢,不要用 First(x => x.RowId == …) 全表掃描找主鍵
        {
            // 🔴 關窗鈕按下即關，而這是 PostUpdate：關閉中的那幾幀仍會進來、標題字串也還讀得到
            // ⇒ 不擋就是每個關閉幀再按一次。
            // 🔑 鍵取實際被按的那個指標（am 是以型別名稱重查 index 1 的結果，不一定是事件帶進來的那一扇）。
            if (!AddonPressGuard.TryBeginPress("RetainerItemTransferProgress", am.Base)) return;

            PluginLog.Debug("Closing Entrust Duplicates menu");
            am.Close();
        }
    }
}
