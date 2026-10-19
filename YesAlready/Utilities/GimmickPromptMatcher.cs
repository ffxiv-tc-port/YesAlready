using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YesAlready.Utils;

/// <summary>
/// 自動 GimmickYesNo 的退路比對，只給原始位元組整句比對永遠比不中的列用。
/// 表裡的換行是 NewLine 巨集、畫面文字同一處是空白 ⇒ 兩邊都去掉空白再整句比；
/// 表裡道具名巨集的位置，畫面上必須剛好是一個 EventItem 名稱，前後文字錨定句首與句尾。
/// </summary>
/// <remarks>只依賴 Lumina，離線驗收直接編譯這個檔。</remarks>
internal sealed class GimmickPromptMatcher
{
    /// <summary>
    /// 退路只套用在這幾列，都是照原文判斷過的非退出類。
    /// 刻意不列入會讓你離開或放棄目前內容的列：#6、#103、#104、#105、#110、#118、#119。
    /// 要加列先讀原文，退出類一律不加。
    /// </summary>
    internal static readonly IReadOnlySet<uint> EligibleRows = new HashSet<uint> { 38, 99, 117, 160, 165, 166 };

    private const string NameSheet = "EventItem";

    private readonly Template[] templates;
    private readonly HashSet<string> names;

    internal GimmickPromptMatcher(IEnumerable<(uint RowId, ReadOnlySeString Text)> rows, IEnumerable<ReadOnlySeString> eventItemNames, IReadOnlySet<uint> eligibleRows)
    {
        templates = rows.Where(r => eligibleRows.Contains(r.RowId)).Select(r => Build(r.RowId, r.Text)).OfType<Template>().ToArray();
        names = templates.Any(t => t.HasName)
            ? eventItemNames.Select(n => Normalize(TextOnly(n))).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    internal static GimmickPromptMatcher FromSheets(ExcelSheet<GimmickYesNo> gimmicks, ExcelSheet<EventItem> eventItems)
        => new(gimmicks.Select(x => (x.RowId, x.Unknown0)), eventItems.Select(x => x.Singular), EligibleRows);

    /// <returns>比中的 GimmickYesNo 列號；沒比中回 null。</returns>
    internal uint? Match(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var actual = Normalize(text);
        foreach (var t in templates)
        {
            if (!t.HasName)
            {
                if (string.Equals(actual, t.Head, StringComparison.Ordinal)) return t.RowId;
                continue;
            }

            var nameLength = actual.Length - t.Head.Length - t.Tail.Length;
            if (nameLength > 0
                && actual.StartsWith(t.Head, StringComparison.Ordinal)
                && actual.EndsWith(t.Tail, StringComparison.Ordinal)
                && names.Contains(actual.Substring(t.Head.Length, nameLength)))
                return t.RowId;
        }

        return null;
    }

    /// <returns>null＝這列含有重現不了的巨集（例如字串參數），退路不用它、維持比不中。</returns>
    private static Template? Build(uint rowId, ReadOnlySeString text)
    {
        var head = new StringBuilder();
        var tail = new StringBuilder();
        var hasName = false;
        foreach (var payload in text)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
                (hasName ? tail : head).Append(Normalize(Encoding.UTF8.GetString(payload.Body.Span)));
            else if (payload.Type == ReadOnlySePayloadType.Macro && payload.MacroCode == MacroCode.NewLine)
                continue;
            else if (!hasName && IsEventItemName(payload))
                hasName = true;
            else
                return null;
        }

        return head.Length + tail.Length == 0 ? null : new Template(rowId, head.ToString(), tail.ToString(), hasName);
    }

    /// <summary>‹sheet(EventItem, 任意列, 0)›＝該 EventItem 的第 0 欄（Singular）。</summary>
    private static bool IsEventItemName(ReadOnlySePayload payload)
        => payload.Type == ReadOnlySePayloadType.Macro && payload.MacroCode == MacroCode.Sheet
            && payload.TryGetExpression(out var sheet, out _, out var column)
            && sheet.TryGetString(out var sheetName) && sheetName.ExtractText() == NameSheet
            && column.TryGetInt(out var columnIndex) && columnIndex == 0;

    internal static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    private static string TextOnly(ReadOnlySeString value)
    {
        var sb = new StringBuilder();
        foreach (var payload in value)
        {
            if (payload.Type == ReadOnlySePayloadType.Text) sb.Append(Encoding.UTF8.GetString(payload.Body.Span));
        }
        return sb.ToString();
    }

    private sealed record Template(uint RowId, string Head, string Tail, bool HasName);
}
