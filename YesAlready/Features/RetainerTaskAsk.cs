namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class RetainerTaskAsk : AddonFeature
{
    protected override bool IsEnabled() => C.RetainerTaskAskEnabled;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.RetainerTaskAsk>(out var am))
        {
            // must be throttled, there's a little delay after setup before this is enabled.
            // 🔴 這個 lambda 是下一幀之後才跑的,addon 可能已經被拆掉 → AssignButton 或它的 OwnerNode
            // 會是 null,而 IsEnabled 解的正是 OwnerNode。IsComponentEnabled 任一層 null 回 false,
            // 讓 TaskManager 照既有的等待/重試邏輯繼續等,不會變成無法攔截的 AccessViolationException。
            Service.TaskManager.Enqueue(() => GenericHelpers.IsComponentEnabled(am.AssignButton));
            Service.TaskManager.Enqueue(am.Assign);
        }
    }
}
