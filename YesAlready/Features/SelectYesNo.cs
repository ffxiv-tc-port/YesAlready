using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
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

            // 🔴 AtkValues 的長度恰為 AtkValuesCount，越界讀到的是堆積垃圾**不是 null** ⇒ 判空擋不住。
            // 這一格是字串指標，越界時等於拿任意 8 bytes 當 char* 解參考 → 攔不到的 AVE
            // （corrupted-state exception，try/catch 無效）。原本還在 :83 又裸讀了同一格一次。
            // 讀不到就把名稱當空字串：下面的 FindRow 比對必然落空 → 走既有的「配對失敗」分支，
            // 再往下掉回一般的 TextEntryNode 比對迴圈，控制流跟「這個對話框沒有收藏品名稱」一致。
            var itemNameValue = AtkValueSafety.Get(atk, 15);
            if (itemNameValue == null)
                PluginLog.Information($"[{nameof(SelectYesno)}] AtkValues 只有 {AtkValueSafety.CountOf(atk)} 格（需要 16），跳過收藏品名稱判定");

            var rawName = itemNameValue == null ? string.Empty : itemNameValue->String.AsDalamudSeString().GetText();
            var name = Enum.GetValues<SeIconChar>().Cast<SeIconChar>().Aggregate(rawName, (current, enumValue) => current.Replace(enumValue.ToIconString(), "")).Trim();
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
                Log($"Failed to match any collectable to {name} [original={rawName}]");
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
    /// 四種語言跟著壞掉。
    /// <para>
    /// 🔴 這裡刻意**不用** <see cref="GenericHelpers.ContainsPartOf"/>：它是 any-match，
    /// needle 的任何一個文字片段命中就回 true。台服 Addon#120「確定要加入UNKNOWN的小隊嗎？」
    /// 被 placeholder 切成「確定要加入」＋「的小隊嗎？」兩段，光靠前半段就會在另外三句上誤中：
    /// #10213「確定要加入新人頻道？」、#12945 與 #12973「確定要加入「（同好會名）」嗎？」。
    /// 那三句都不是小隊邀請，自動按「是」等於替使用者加入新人頻道或同好會。
    /// 改用 <see cref="ContainsAllPartsInOrder"/> 要求全部片段依序命中。
    /// </para>
    /// ⚠️ 兩者都是大小寫敏感的，但這裡 needle 與 haystack 都出自遊戲同一份語言資料所以不受影響。
    /// </summary>
    private static bool IsPartyJoinPrompt(string text)
        => GenericHelpers.GetRow<Addon>(PartyJoinAddonRow) is { } row && !row.Text.IsEmpty && ContainsAllPartsInOrder(text, row.Text)
            || lfgPatterns.Any(r => r.IsMatch(text));

    /// <summary>
    /// Addon 表的句子含 placeholder（玩家名、同好會名之類），拆掉之後剩下若干段固定文字。
    /// 這個函式要求 <paramref name="haystack"/> **依序含有全部片段**才算命中，
    /// 也就是 all-match，而不是 <see cref="GenericHelpers.ContainsPartOf"/> 的 any-match。
    /// <para>
    /// 「依序」比「每段各自 Contains」嚴格：找到第 n 段之後，第 n+1 段只從該段結尾之後繼續找，
    /// 所以片段順序顛倒的句子不會算命中——這正好還原 placeholder 原本的位置關係。
    /// </para>
    /// <para>
    /// ⚠️ 片段必須維持 payload 的原始順序，**不可以像 ECommons 那樣依長度排序**：
    /// 那是 any-match 為了「先試最長的」才做的，對依序比對會直接算出錯的答案。
    /// </para>
    /// </summary>
    /// <param name="haystack">遊戲當下顯示的對話框文字。</param>
    /// <param name="needle">Addon 表裡的樣板句。</param>
    private static bool ContainsAllPartsInOrder(string haystack, ReadOnlySeString needle)
    {
        var fragments = needle.ToDalamudString().Payloads
            .OfType<TextPayload>()
            .Select(p => p.Text ?? string.Empty)
            .Where(t => t.Trim().Length > 0)
            .ToArray();

        // 一段固定文字都沒有＝這句樣板整句都是 placeholder，比不出東西來。
        // 回 true 會變成「任何對話框都命中」，所以這裡一定要回 false。
        if (fragments.Length == 0) return false;

        var cursor = 0;
        foreach (var fragment in fragments)
        {
            var at = haystack.IndexOf(fragment, cursor, StringComparison.Ordinal);
            if (at < 0) return false;
            cursor = at + fragment.Length;
        }

        return true;
    }

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
