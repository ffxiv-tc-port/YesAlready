namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class GuildLeveDifficulty : AddonFeature
{
    protected override bool IsEnabled() => C.GuildLeveDifficultyConfirm;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        // 🔴 AtkValues 的長度恰為 AtkValuesCount，越界讀到的是堆積垃圾不是 null ⇒ 只判空擋不住。
        // 這裡送出去的是難度值，讀到垃圾等於拿隨機整數去按確認鈕。讀不到就這一次不送 callback，
        // 失敗形式是「沒有自動選難度」而不是崩潰或誤操作。
        var difficulty = AtkValueSafety.Get(atk, 1);
        if (difficulty == null)
        {
            PluginLog.Information($"[{nameof(GuildLeveDifficulty)}] AtkValues 只有 {AtkValueSafety.CountOf(atk)} 格（需要 2），這次不送難度 callback");
            return;
        }

        Callback.Fire(atk, true, 0, difficulty->Int);
    }
}
