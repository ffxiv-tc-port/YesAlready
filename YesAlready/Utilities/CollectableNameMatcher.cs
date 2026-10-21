using System;
using System.Collections.Generic;
using System.Linq;

namespace YesAlready.Utils;

/// <summary>
/// 收藏品確認框（Addon#156）上顯示的道具名 → 收藏品道具列號。
/// 先比正規化後整句相等（Singular 先、Name 次；同名取列號小的）；都不相等才退回舊規則「顯示名含 Singular」，但取最長的那個。
/// 舊規則取列號最小的命中：「第二期重建用的合板」會被當成「重建用的合板」，套錯收藏價值門檻。
/// </summary>
/// <remarks>只依賴 BCL，離線驗收直接編譯這個檔。</remarks>
internal sealed class CollectableNameMatcher
{
    private const char PuaFirst = (char)0xE000;
    private const char PuaLast = (char)0xF8FF;
    private const char FullWidthSpace = (char)0x3000;
    private const char NoBreakSpace = (char)0x00A0;

    private readonly Dictionary<string, uint> bySingular = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly (uint RowId, string Singular)[] singulars;

    /// <param name="collectables">收藏品列（舊規則的範圍：IsCollectable 且 Singular 非空）。</param>
    internal CollectableNameMatcher(IEnumerable<(uint RowId, string Singular, string Name)> collectables)
    {
        var rows = collectables
            .Select(r => (r.RowId, Singular: Normalize(r.Singular), Name: Normalize(r.Name)))
            .Where(r => r.Singular.Length > 0)
            .OrderBy(r => r.RowId)
            .ToArray();
        foreach (var r in rows)
        {
            bySingular.TryAdd(r.Singular, r.RowId);
            if (r.Name.Length > 0)
                byName.TryAdd(r.Name, r.RowId);
        }

        singulars = rows.Select(r => (r.RowId, r.Singular)).ToArray();
    }

    internal int Count => singulars.Length;

    /// <returns>比中的道具列號；沒比中回 null。</returns>
    internal uint? Match(string? shown)
    {
        var name = Normalize(shown);
        if (name.Length == 0) return null;
        if (bySingular.TryGetValue(name, out var id)) return id;
        if (byName.TryGetValue(name, out id)) return id;

        // 退路的命中範圍與舊規則相同（含 Singular 才算），只改成取最長的；比舊規則多認的只有上面 Name 整句相等那一種。
        uint? best = null;
        var bestLength = 0;
        foreach (var (rowId, singular) in singulars)
        {
            if (singular.Length > bestLength && name.Contains(singular, StringComparison.InvariantCultureIgnoreCase))
            {
                best = rowId;
                bestLength = singular.Length;
            }
        }

        return best;
    }

    /// <summary>全形空白與不換行空白當成半形空白、去掉私用區圖示字元（收藏品／優質圖示）、去掉前後空白；其餘逐字保留。</summary>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var chars = new char[text.Length];
        var n = 0;
        foreach (var c in text)
        {
            if (c >= PuaFirst && c <= PuaLast) continue;
            chars[n++] = c is FullWidthSpace or NoBreakSpace ? ' ' : c;
        }

        return new string(chars, 0, n).Trim();
    }
}
