using Dalamud.Game.Text;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
internal class SelectYesno : TextMatchingFeature
{
    protected override unsafe string GetSetLastSeenText(AtkUnitBase* atk)
    {
        var text = new AddonMaster.SelectYesno(atk).TextLegacy;
        Service.Watcher.LastSeenDialogText = text;
        return text;
    }

    protected override unsafe object? ShouldProceed(string text, AtkUnitBase* atk)
    {
        if (Service.Watcher.ForcedYesKeyPressed)
        {
            Log($"Forced yes hotkey pressed");
            return new TextEntryNode { IsYes = true };
        }

        if (C.GimmickYesNo && Svc.Data.GetExcelSheet<GimmickYesNo>().Where(x => !x.Unknown0.IsEmpty).Select(x => x.Unknown0).ToList().Any(g => g.Equals(text)))
        {
            Log($"Entry is a gimmick");
            return new TextEntryNode { IsYes = true };
        }

        if (C.PartyFinderJoinConfirm && GenericHelpers.TryGetAddonByName<AtkUnitBase>("LookingForGroupDetail", out var _) && IsPartyJoinPrompt(text))
        {
            Log($"Entry is party finder join confirmation");
            return new TextEntryNode { IsYes = true };
        }

        if (C.AutoCollectable && IsCollectablePrompt(text))
        {
            Log($"Entry is collectable");
            var name = Enum.GetValues<SeIconChar>().Cast<SeIconChar>().Aggregate(atk->AtkValues[15].String.AsDalamudSeString().GetText(), (current, enumValue) => current.Replace(enumValue.ToIconString(), "")).Trim();
            if (GenericHelpers.FindRow<Item>(x => x.IsCollectable && !x.Singular.IsEmpty && name.Contains(x.Singular.GetText(), StringComparison.InvariantCultureIgnoreCase)) is { RowId: > 0 } item)
            {
                Log($"Detected item [{item}] {item.Name}");
                if (int.TryParse(Regex.Match(text, @"\d+").Value, out var value))
                {
                    if (GenericHelpers.FindRow<CollectablesShopItem>(x => x.Item.Value.RowId == item.RowId) is { } collectability)
                    {
                        var min = collectability.CollectablesShopRefine.Value.LowCollectability;
                        Log($"Minimum collectability required is {min}, value detected is {value}");
                        if (value >= min)
                        {
                            Log($"Entry is [{item}] {item.Name} with a sufficient collectability of {value}");
                            return new TextEntryNode { IsYes = true };
                        }
                        else
                        {
                            Log($"Entry is [{item}] {item.Name} with an insufficient collectability of {value}");
                            return new TextEntryNode { IsYes = false };
                        }
                    }
                    else
                    {
                        if (item.AetherialReduce > 0) // aethersand fish aren't turned in for scrips so collectability doesn't matter
                        {
                            Log($"Entry is [#{item.RowId}] {item.Name} and probably an aethersand fish. Skipping collectability check.");
                            return new TextEntryNode { IsYes = true };
                        }
                        else if (GenericHelpers.TryGetRow<WKSItemInfo>(item.AdditionalData.RowId, out var wksItem)) // stellar fish are scored based on collective collectability so individual doesn't matter
                        {
                            Log($"Entry is [#{item.RowId}] {item.Name} for {wksItem.WKSItemSubCategory.ValueNullable?.Name ?? "null"}. Skipping collectability check.");
                            return new TextEntryNode { IsYes = true };
                        }
                        else
                            Log($"Failed to find matching CollectablesShopItem for [{item.RowId}] {item.Name}. Not an aethersand fish or a CE fish. Ping the dev or create a git issue if you found this message erroneously.");
                    }
                }
            }
            else
                Log($"Failed to match any collectable to {name} [original={atk->AtkValues[15].String}]");
        }

        var nodes = C.GetAllNodes().OfType<TextEntryNode>();
        foreach (var node in nodes)
        {
            if (!node.Enabled || string.IsNullOrEmpty(node.Text))
                continue;

            if (!CheckRestrictions(node))
                continue;

            if (EntryMatchesText(node.Text, text, node.IsTextRegex))
                return node;
        }

        return null;
    }

    protected override unsafe void Proceed(AtkUnitBase* atk, object? matchingNode)
    {
        if (matchingNode is not TextEntryNode node) return;
        if (node.IsYes)
            new AddonMaster.SelectYesno(atk).Yes();
        else
            new AddonMaster.SelectYesno(atk).No();
    }

    /// <summary>
    /// Addon#120＝「確定要加入&lt;名字&gt;的小隊嗎？」（EN "Join &lt;name&gt;'s party?"）。
    /// 這是玩家點下招募看板的加入鈕時跳出來的那句。
    /// </summary>
    private const uint PartyJoinAddonRow = 120;

    /// <summary>
    /// Addon#1056＝「收藏價值」（EN "Collectability" / JA「収集価値」/ DE "Sammlerwert" /
    /// FR "Valeur de collection"）。收藏品交出的確認句 Addon#156 是
    /// 「&lt;道具&gt;的收藏價值為&lt;數字&gt;，確定要降低品質變換成以下道具嗎？」，一定含這個詞。
    /// </summary>
    private const uint CollectabilityAddonRow = 1056;

    /// <summary>
    /// 原本只靠寫死的四國語言 regex 比對，台服（以及韓、簡）永遠對不上——比對失敗是完全靜默的，
    /// 症狀只是「沒有自動點」。主要判斷改成直接讀遊戲自己的 Addon 表，語言由客戶端決定，不需要
    /// 為每個新語言補 pattern；舊清單留著一起 OR，萬一列號日後被官方挪動也不會讓原本能動的
    /// 四種語言跟著壞掉。⚠️ <see cref="GenericHelpers.ContainsPartOf"/> 是大小寫敏感的，但這裡
    /// 兩邊都來自同一張表所以不受影響。
    /// </summary>
    private static bool IsPartyJoinPrompt(string text)
        => GenericHelpers.GetRow<Addon>(PartyJoinAddonRow) is { } row && !row.Text.IsEmpty && text.ContainsPartOf(row.Text)
            || lfgPatterns.Any(r => r.IsMatch(text));

    /// <summary>
    /// 同上，改讀 Addon#1056。⚠️ 英文的確認句寫的是小寫的 "collectability of"，而表裡的標籤是
    /// 大寫開頭的 "Collectability"，所以這裡一定要忽略大小寫比對，不能用
    /// <see cref="GenericHelpers.ContainsPartOf"/>。
    /// </summary>
    private static bool IsCollectablePrompt(string text)
        => GenericHelpers.GetRow<Addon>(CollectabilityAddonRow)?.Text.GetText() is { Length: > 0 } label
            && text.Contains(label, StringComparison.OrdinalIgnoreCase)
            || collectablePatterns.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Addon#<see cref="PartyJoinAddonRow"/> 之外的後備比對。</summary>
    private static readonly List<Regex> lfgPatterns =
    [
        new Regex(@"Join .* party\?"),
        new Regex(@".*のパーティに参加します。よろしいですか？"),
        new Regex(@"Der Gruppe von .* beitreten\?"),
        new Regex(@"Rejoindre l'équipe de .*\?")
    ];

    /// <summary>Addon#<see cref="CollectabilityAddonRow"/> 之外的後備比對。</summary>
    private static readonly List<string> collectablePatterns =
    [
        "collectability of",
        "収集価値",
        "Sammlerwert",
        "Valeur de collection"
    ];
}
