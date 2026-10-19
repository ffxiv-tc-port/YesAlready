using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace YesAlready.Utils;

/// <summary>鑲嵌確認視窗開窗模式（<c>AtkValues[0]</c>）的判讀；純函式，不碰遊戲記憶體。</summary>
/// <remarks>
/// 0＝自己鑲嵌；1～3＝魔晶石鑲嵌委託；4＝接受委託。
/// 型別不是 Int／UInt 一律算「不是自己鑲嵌」：遊戲對未定義型別的格子取值回 0，照值判會把它認成自己鑲嵌。
/// </remarks>
internal static class MeldDialogMode
{
    public const int AtkValueIndex = 0;
    public const uint SelfMeld = 0;

    /// <summary>型別是 Int 或 UInt、而且值是 0 才算自己鑲嵌。</summary>
    /// <param name="raw">該格值欄位的 32 位元原樣（Int 與 UInt 讀的是同一段位元組）。</param>
    public static bool IsSelfMeld(ValueType type, uint raw)
        => type is ValueType.Int or ValueType.UInt && raw == SelfMeld;

    /// <summary>記錄用的描述，例如「模式 1（魔晶石鑲嵌委託）」。</summary>
    public static string Describe(ValueType type, uint raw) => type switch
    {
        ValueType.Int => $"模式 {(int)raw}（{NameOf((int)raw)}）",
        ValueType.UInt => $"模式 {raw}（{NameOf(raw)}）",
        _ => $"模式格的型別是 {type}，不是整數",
    };

    private static string NameOf(long mode) => mode switch
    {
        0 => "自己鑲嵌",
        1 or 2 or 3 => "魔晶石鑲嵌委託",
        4 => "接受委託",
        _ => "不認得的模式",
    };
}
