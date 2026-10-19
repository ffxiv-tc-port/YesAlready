using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace YesAlready.Utils;

/// <summary>
/// 台服 7.25 起遊戲文字的中間點由 U+00B7 改成 U+30FB，照舊寫法存的規則會對不上。
/// 比對前兩邊都折成 U+30FB；只用在比對，使用者存的規則文字原樣不動。
/// </summary>
internal static class MiddleDot
{
    public const char Legacy = (char)0xB7;
    public const char Current = (char)0x30FB;

    private static readonly ConditionalWeakTable<string, string> FoldedRules = new();

    /// <summary>不含 U+00B7 就回傳同一個實例，不配置。</summary>
    [return: NotNullIfNotNull(nameof(s))]
    public static string? Fold(string? s)
        => s is null || !s.Contains(Legacy) ? s : s.Replace(Legacy, Current);

    /// <summary>規則字串是設定裡長駐的同一個實例，折過就記住：每幀評估的 Talk 不會對同一條舊規則反覆配置。</summary>
    [return: NotNullIfNotNull(nameof(s))]
    public static string? FoldRule(string? s)
        => s is null || !s.Contains(Legacy) ? s : FoldedRules.GetValue(s, static r => r.Replace(Legacy, Current));
}
