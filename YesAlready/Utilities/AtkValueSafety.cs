namespace YesAlready.Utils;

/// <summary>
/// <c>AtkUnitBase.AtkValues</c> 的邊界守衛。
/// </summary>
/// <remarks>
/// 🔴 <b>只判空是半套，攔不住越界。</b><c>AtkValues</c> 是原生指標陣列，長度恰為
/// <c>AtkValuesCount</c>（<c>ushort</c>，FieldOffset 0x1E2）。索引超出時讀到的是陣列後方的
/// <b>堆積垃圾，不是 null</b> —— 把它當字串指標解參考就是 AccessViolationException，而 AVE 在
/// .NET Core 是 corrupted-state exception，<c>try/catch</c> 與任何例外隔離包裝<b>完全攔不到</b>。
/// <para>
/// 所以順序一定是：①容器本身判空（<c>AtkValues != null</c>）②上界檢查（<c>index &lt; AtkValuesCount</c>）
/// ③才索引。失敗形式一律是「這一次不動作」，不丟例外、不改既有行為的其餘部分。
/// </para>
/// <para>
/// ⚠️ 這裡刻意<b>不</b>驗 <c>AtkValue.Type</c>：型別在各語言／各版本客戶端可能是
/// <c>String</c>／<c>String8</c>／<c>ManagedString</c> 其中之一，硬性白名單會把原本能動的路徑
/// 靜默關掉。空指標本身由 <c>CStringPointer.AsSpan()</c> 安全處理（null 回空 span）。
/// </para>
/// </remarks>
internal static unsafe class AtkValueSafety
{
    /// <summary>AtkValues 是否至少有 <paramref name="count"/> 格可以安全讀取。</summary>
    public static bool HasValues(AtkUnitBase* atk, int count)
        => atk != null && atk->AtkValues != null && atk->AtkValuesCount >= count;

    /// <summary>取第 <paramref name="index"/> 格；容器為空或索引越界時回 <c>null</c>。</summary>
    public static AtkValue* Get(AtkUnitBase* atk, int index)
        => index >= 0 && HasValues(atk, index + 1) ? &atk->AtkValues[index] : null;

    /// <summary>診斷訊息用的實際格數；<paramref name="atk"/> 為空時回 -1。</summary>
    public static int CountOf(AtkUnitBase* atk) => atk == null ? -1 : atk->AtkValuesCount;
}
