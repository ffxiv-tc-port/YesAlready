using System;
using System.Linq;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class InputNumeric : TextMatchingFeature
{
    protected override unsafe string GetSetLastSeenText(AtkUnitBase* atk)
    {
        // 🔴 AtkValues 越界讀到的是堆積垃圾不是 null ⇒ 判空擋不住。這一格是**字串指標**，
        // 越界時等於拿任意 8 bytes 當 char* 解參考 → 攔不到的 AVE（corrupted-state exception）。
        // 讀不到就回空字串：下游的 EntryMatchesText 對空字串一律不匹配 ⇒ 這個對話框不自動點。
        var value = AtkValueSafety.Get(atk, 6);
        if (value == null)
        {
            PluginLog.Information($"[{nameof(InputNumeric)}] AtkValues 只有 {AtkValueSafety.CountOf(atk)} 格（需要 7），讀不到提示文字");
            Service.Watcher.LastSeenNumericsText = string.Empty;
            return string.Empty;
        }

        // 原本這裡讀了兩次同一格（TOCTOU），改成取一次共用。
        string text = value->String;
        Service.Watcher.LastSeenNumericsText = text;
        return text;
    }

    protected override unsafe object? ShouldProceed(string text, AtkUnitBase* atk)
    {
        var nodes = C.GetAllNodes().OfType<NumericsEntryNode>();
        foreach (var node in nodes)
        {
            if (!node.Enabled || string.IsNullOrEmpty(node.Text))
                continue;

            if (EntryMatchesText(node.Text, text, node.IsTextRegex))
                return node;
        }

        return null;
    }

    protected override unsafe void Proceed(AtkUnitBase* atk, object? matchingNode)
    {
        if (matchingNode is not NumericsEntryNode node) return;

        // 同上：上界要驗到本區塊用到的最大索引 + 1（用到 [2] 與 [3]，所以是 4）。
        // min/max 是後面 Math.Clamp 的依據，讀到垃圾會讓夾限失效（甚至 min > max 直接丟例外），
        // 等於用一個隨機數量按下確認。讀不到就這一次不送，下一次事件再來。
        var minValue = AtkValueSafety.Get(atk, 2);
        var maxValue = AtkValueSafety.Get(atk, 3);
        if (minValue == null || maxValue == null)
        {
            PluginLog.Information($"[{nameof(InputNumeric)}] AtkValues 只有 {AtkValueSafety.CountOf(atk)} 格（需要 4），讀不到數量上下限，這次不送 callback");
            return;
        }

        var min = minValue->UInt;
        var max = maxValue->UInt;

        Log("Selecting ok");
        var value = Math.Clamp(node.IsPercent ? (uint)Math.Ceiling(max * (node.Percentage / 100f)) : (uint)node.Quantity, min, max);
        Callback.Fire(atk, true, (int)value);
    }
}
