using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using System.Linq;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostUpdate)]
internal class PurifyResult : AddonFeature
{
    protected override bool IsEnabled() => C.AetherialReductionResults;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (!GenericHelpers.IsAddonReady(atk)) return;

        // GetTextNodeById(2) 找不到節點合法回 null——&null->NodeText 是 0xC0 毒指標,AsSpan() 的判空擋不住。
        var titleNode = atk->GetTextNodeById(2);
        if (titleNode == null) return;

        // 🔴 兩端剝 SeString 的實作必須是同一套。右邊是 Lumina 的 ReadOnlySeString,
        // 左邊原本是 ECommons 的 GetText()(只留文字 payload,把連字符 payload 整個丟掉);
        // 而且 string 直接與 ReadOnlySeString 比,會走它的 implicit operator(string) 把
        // 左邊包回 ReadOnlySeString,Equals 是 Data.SequenceEqual ⇒ **原始位元組序列比對**。
        // 標題裡只要有一個 payload(連字符、換行、不斷行空格)就恆為 false —— 不擲例外、
        // 不寫 log,表現就是「這扇窗從來不會被自動關掉」。兩端一律回到 Lumina ExtractText()。
        if (new ReadOnlySeStringSpan(titleNode->NodeText.AsSpan()).ExtractText() == Svc.Data.GetExcelSheet<Addon>().GetRow(2171).Text.ExtractText())   // 原為 First(x => x.RowId == 2171):O(n) 全表掃描找主鍵,GetRow 是索引查詢
        {
            // 「自動精選」也開著的時候，這個視窗還留著就是為了讓 PurifyResultAutomatic 去按那顆
            // 「自動」鈕；先關掉視窗會讓自動精選永遠按不到。等按鈕不再可按（沒東西可自動了）
            // 再照原本的行為關窗。
            if (C.AetherialReductionAutomatic)
            {
                // IsComponentEnabled 連 OwnerNode 一起擋(IsEnabled 解的是 OwnerNode 不是 AtkResNode);
                // 任一層是 null 就回 false,與原本 automaticButton == null 時的流向一致。
                var automaticButton = new AddonMaster.PurifyResult(atk).AutomaticButton;
                if (GenericHelpers.IsComponentEnabled(automaticButton)) return;
            }

            // 🔴 這一發是關窗。PostUpdate 每一幀都會進來，而關閉中的那幾幀三關全過、標題文字也還讀得到，
            // 不擋就是每個關閉幀再送一次＝攔不到的存取違規。
            // 空參數鍵：與 PurifyResultAutomatic 的「自動」鈕併成同一筆（同一個實例只准按一次）——
            // 按下自動鈕之後這扇窗就在被換掉的路上，此時再送 -1 是同一種形狀的第二發，
            // 而那段期間 IsComponentEnabled 本來就不可信。
            if (!AddonPressGuard.TryBeginPress(addonInfo.AddonName, atk)) return;

            PluginLog.Debug("Closing Purify Results menu");
            Callback.Fire(atk, true, -1);
        }
    }
}
