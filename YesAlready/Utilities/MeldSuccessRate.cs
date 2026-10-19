using System;
using System.Globalization;

namespace YesAlready.Utils;

/// <summary>鑲嵌確認視窗「鑲嵌成功率」那格文字的解析；純函式，不碰遊戲記憶體。</summary>
/// <remarks>
/// 台服那格由 Addon 表的「整數.兩位數%」組成，顯示成 <c>100.00%</c>。
/// 讀不出來一律回 <see langword="false"/>，呼叫端要當成「沒有保證」。
/// </remarks>
internal static class MeldSuccessRate
{
    private const char FullWidthFirst = (char)0xFF01;
    private const char FullWidthLast = (char)0xFF5E;
    private const int FullWidthOffset = 0xFEE0;

    /// <summary>
    /// 取出文字裡唯一一個「數字＋%」的數值（全形數字與符號先轉半形）。
    /// 沒有、不只一個、數字前面緊接正負號或千分位逗號、或數值不在 0～100 之間，都回 <see langword="false"/>。
    /// </summary>
    public static bool TryParse(string? text, out float percent)
    {
        percent = 0f;
        if (string.IsNullOrEmpty(text)) return false;

        var s = ToHalfWidth(text);
        var sign = s.IndexOf('%');
        if (sign < 0 || s.IndexOf('%', sign + 1) >= 0) return false;

        var end = sign;
        while (end > 0 && char.IsWhiteSpace(s[end - 1])) end--;
        var start = end;
        while (start > 0 && (char.IsAsciiDigit(s[start - 1]) || s[start - 1] == '.')) start--;
        if (start == end || (start > 0 && s[start - 1] is '-' or '+' or ',')) return false;

        if (!float.TryParse(s.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value is < 0f or > 100f)
            return false;

        percent = value;
        return true;
    }

    /// <summary>讀得出來而且恰好是 100% 才算保證成功。</summary>
    public static bool IsGuaranteed(string? text) => TryParse(text, out var percent) && percent == 100f;

    private static string ToHalfWidth(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= FullWidthFirst and <= FullWidthLast)
                chars[i] = (char)(chars[i] - FullWidthOffset);
        }
        return new string(chars);
    }
}
