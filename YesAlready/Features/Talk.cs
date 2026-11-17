using System.Linq;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostUpdate)]
internal class Talk : TextMatchingFeature
{
    /// <summary>強制鍵連續生效的判定空檔：隔超過這麼久才算新的一次按住，log 只在每次按住的開頭寫一行。</summary>
    private const long ForcedLogResetMs = 1000;

    private long _lastForcedTick;

    private int _rulesVersionSeen = -1;
    private bool _hasActiveRules;

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
            // PostUpdate 每幀都會走到這裡；ECommons 下被丟掉的 Debug 照樣有成本，所以只在按住的開頭寫。
            var now = System.Environment.TickCount64;
            if (now - _lastForcedTick > ForcedLogResetMs)
                PluginLog.Debug($"{nameof(Talk)}: Forced hotkey pressed");
            _lastForcedTick = now;
            return true;
        }

        // PostUpdate 每幀都走到這裡：沒有任何可用的 Talk 規則時不走整棵規則樹（結果同樣是 null）。
        if (!HasActiveRules()) return null;

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

    /// <summary>規則樹裡有沒有至少一條會被 <see cref="ShouldProceed"/> 拿來比的 Talk 規則；規則世代號變了才重算。</summary>
    private bool HasActiveRules()
    {
        var version = Configuration.RulesVersion;
        if (version != _rulesVersionSeen)
        {
            _hasActiveRules = C.GetAllNodes().OfType<TalkEntryNode>().Any(n => n.Enabled && !string.IsNullOrEmpty(n.TargetText));
            _rulesVersionSeen = version;
        }
        return _hasActiveRules;
    }

    protected override unsafe void Proceed(AtkUnitBase* atk, object? matchingNode)
    {
        // Talk 是「按一次翻一頁、窗不會因為被按而消失」的多次互動窗：守衛照樣記位址，但逃生口只用 15 幀，每頁多等約 0.25 秒。
        // 🔑 守衛的鍵取「實際被按的那個指標」：addon 是以型別名稱重查 index 1 的結果，不一定是觸發這次 PostUpdate 的那一扇。

        // 🔴 就緒檢查一定要驗**實際要按的那個指標**：TryGetAddonMaster 只判非零、完全不驗就緒，而 HandleAddonEvent 驗過的是
        //    addonInfo.Addon，與這裡重解出來的 index 1 不保證是同一扇。
        //    ⚠️ 這**擋不住**「別的外掛剛按過、正在關閉中」—— IsAddonReady 的三關在拆除途中全過。
        //    排在守衛之前：被擋下的這一發不該佔掉一次守衛登記。
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.Talk>(out var addon)
            && addon.IsAddonReady
            && AddonPressGuard.TryBeginRoutinePress("Talk", addon.Base))
            addon.Click();
    }
}
