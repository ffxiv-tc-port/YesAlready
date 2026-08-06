using System.Linq;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
[AddonFeature(AddonEvent.PostUpdate)]
internal class HWDLottery : AddonFeature
{
    protected override bool IsEnabled() => C.KupoOfFortune;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        switch (eventType)
        {
            case AddonEvent.PostSetup:
                Callback.Fire(atk, true, 0, 1);
                break;
            case AddonEvent.PostUpdate:
                // 🔴 GetAsAtkComponentButton() 是原生呼叫,對 null 節點一樣是 AVE(try/catch 攔不到),
                // 所以節點必須在呼叫前就驗過;NodeList 在版面還沒建好時可能是 null 或不足 8 格。
                if (atk->UldManager.NodeList == null || atk->UldManager.NodeListCount <= 7) break;
                var closeNode = atk->UldManager.NodeList[7];
                if (closeNode == null) break;

                // AtkValues 同樣要先驗長度,否則讀的是配置外的記憶體。
                if (atk->AtkValues == null || atk->AtkValuesCount <= 36) break;

                var closeButton = closeNode->GetAsAtkComponentButton();
                if (Enumerable.Range(32, 5).Select(i => atk->AtkValues[i].UInt).ToList().All(x => x != 0) && GenericHelpers.IsComponentEnabled(closeButton))
                {
                    var eventData = new AtkEvent();
                    var inputData = stackalloc int[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                    atk->ReceiveEvent(AtkEventType.ButtonClick, 0, &eventData);
                }
                break;
        }
    }
}
