using System;
using System.Collections.Generic;

namespace YesAlready.Utils;

/// <summary>
/// 「限定區域」區域欄的寫入與舊項目相容規則（純函式，不碰遊戲狀態）。
/// 比對端拿目前區域的地名（PlaceName）比，新項目就寫地名；舊版寫的是 TerritoryType.Name
/// 內部代碼（例 s1t1），比對端另外認得「目前區域的內部代碼」，使用者的設定不改寫。
/// </summary>
internal static class ZoneTextRules
{
    /// <summary>
    /// 新項目要寫進區域欄的值。<paramref name="placeName"/>＝比對端用的那個地名字串；
    /// 地名為空的區域退回內部代碼，讓項目仍只限這個區域（比對端靠 <see cref="IsLegacyCodeMatch"/> 認得）。
    /// </summary>
    public static string ForNewEntry(string? placeName, string internalCode)
        => !string.IsNullOrEmpty(placeName) ? placeName : internalCode;

    /// <summary>
    /// 區域欄整欄（去頭尾空白、不分大小寫）等於目前區域的內部代碼。正規式項目不走這條：
    /// 內部代碼是整串寫入的，用包含比對會讓 a2fc 誤中 a2fc_2 這種別的區域。
    /// </summary>
    public static bool IsLegacyCodeMatch(string zoneText, bool isRegex, string? currentCode)
        => !isRegex && !string.IsNullOrEmpty(currentCode)
            && string.Equals(zoneText.Trim(), currentCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>區域欄是內部代碼形式（<paramref name="codes"/> 要以不分大小寫比較建立）。</summary>
    public static bool LooksLikeLegacyCode(string zoneText, bool isRegex, IReadOnlySet<string> codes)
        => !isRegex && zoneText.Trim() is { Length: > 0 } t && codes.Contains(t);
}
