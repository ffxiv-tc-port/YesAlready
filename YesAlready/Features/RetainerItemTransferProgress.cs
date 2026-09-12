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
        // 越界後 ReadSeStringNullTerminated 會對任意位址跑 strlen → 攔不到的 AVE。
        // 這是 PostUpdate（每幀），所以讀不到就直接離開，下一幀重來。
        // ⚠️ 連空指標一起擋：CStringPointer.AsSpan() 對位址 0 會直接跑 strlen，
        // 那同樣是 AVE。原本的寫法只是「剛好」拿到有效指標，不是有人擋過。
        var titleValue = AtkValueSafety.Get(am.Base, 0);
        if (titleValue == null || titleValue->String.Value == null) return;

        // 🔴 兩端剝 SeString 的實作必須是同一套(同 PurifyResult):右邊是 Lumina 的
        // ReadOnlySeString,而 string 與它比會被包回 ReadOnlySeString 做原始位元組比對,
        // 標題帶任何 payload 就恆為 false 且完全沒有徵兆。改成兩端都走 Lumina ExtractText()。
        // CStringPointer.AsSpan() 就是 MemoryMarshal.CreateReadOnlySpanFromNullTerminated,
        // 與原本的 ReadSeStringNullTerminated 讀的是同一段位元組(上面的空指標守衛照樣必要)。
        if (new ReadOnlySeStringSpan(titleValue->String.AsSpan()).ExtractText() == Svc.Data.GetExcelSheet<Addon>().GetRow(13528).Text.ExtractText())   // 原為 First(x => x.RowId == 13528):O(n) 全表掃描找主鍵,GetRow 是索引查詢
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
