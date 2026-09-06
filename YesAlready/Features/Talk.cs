using System.Linq;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostUpdate)]
internal class Talk : TextMatchingFeature
{
    protected override unsafe string GetSetLastSeenText(AtkUnitBase* atk)
    {
        var text = Svc.Targets.Target is { Name: var name } ? name.TextValue : string.Empty;
        Service.Watcher.LastSeenTalkTarget = text;
        return text;
    }

    protected override unsafe object? ShouldProceed(string text, AtkUnitBase* atk)
    {
        if (Service.Watcher.ForcedYesKeyPressed && !C.SeparateForcedKeys || Service.Watcher.ForcedTalkKeyPressed)
        {
            PluginLog.Debug($"{nameof(Talk)}: Forced hotkey pressed");
            return true;
        }

        var nodes = C.GetAllNodes().OfType<TalkEntryNode>();
        foreach (var node in nodes)
        {
            if (!node.Enabled || string.IsNullOrEmpty(node.TargetText))
                continue;

            if (EntryMatchesText(node.TargetText, text, node.TargetIsRegex))
                return node;
        }

        return null;
    }

    protected override unsafe void Proceed(AtkUnitBase* atk, object? matchingNode)
    {
        // Talk 是「按一次翻一頁、窗不會因為被按而消失」的多次互動窗：守衛照樣記位址，
        // 但逃生口只用 15 幀（關閉中的危險窗口 <10 幀，15 幀不落在裡面），每頁多等約 0.25 秒。
        // 🔑 守衛的鍵取「實際被按的那個指標」：addon 是以型別名稱重查 index 1 的結果，
        // 不一定是觸發這次 PostUpdate 的那一扇。
        // 🔴 就緒檢查一定要驗**實際要按的那個指標**：TryGetAddonMaster 底下是
        //    TryGetAddonByName → GetAddonByName(name, 1)，它**只判非零、完全不驗就緒**。
        //    HandleAddonEvent 驗過的是 addonInfo.Addon（觸發這次 PostUpdate 的那一扇），
        //    與這裡重解出來的 index 1 不保證是同一扇 ⇒ 少了這一道，被按的那個實例
        //    在整條路徑上從頭到尾沒有被驗證過。
        //    ⚠️ 這**擋不住**「別的外掛剛按過、正在關閉中」—— IsAddonReady 的三關在拆除途中全過。
        //    排在守衛之前：被擋下的這一發不該佔掉一次守衛登記。
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.Talk>(out var addon)
            && addon.IsAddonReady
            && AddonPressGuard.TryBeginRoutinePress("Talk", addon.Base))
            addon.Click();
    }
}
