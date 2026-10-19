using ECommons.GameHelpers;
using Lumina.Excel.Sheets;

namespace YesAlready.Utils;

/// <summary>「限定區域」的寫入端與比對端共用的目前區域讀法（框架執行緒）。</summary>
internal static class CurrentZone
{
    /// <summary>比對端用的地名字串；PlaceName 列不存在時為 null（比對端此時不檢查區域）。</summary>
    public static string? PlaceName(in TerritoryType row) => row.PlaceName.ValueNullable?.Name.ToString();

    /// <summary>新項目的區域欄；讀不到目前區域時為 null（與改前相同：建立不限區域的項目）。</summary>
    public static string? TextForNewEntry()
        => GenericHelpers.GetRow<TerritoryType>(Player.Territory) is { } row
            ? ZoneTextRules.ForNewEntry(PlaceName(row), row.Name.ExtractText())
            : null;
}
