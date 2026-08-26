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

        // GetTextNodeById(2) 找不到節點合法回 null——&null->NodeText 是 0xC0 毒指標,ReadSeString 的判空擋不住。
        var titleNode = atk->GetTextNodeById(2);
        if (titleNode == null) return;

        if (MemoryHelper.ReadSeString(&titleNode->NodeText).GetText() == Svc.Data.GetExcelSheet<Addon>().GetRow(2171).Text)   // 原為 First(x => x.RowId == 2171):O(n) 全表掃描找主鍵,GetRow 是索引查詢
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

            PluginLog.Debug("Closing Purify Results menu");
            Callback.Fire(atk, true, -1);
        }
    }
}
