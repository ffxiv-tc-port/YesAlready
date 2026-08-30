using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace YesAlready.BaseFeatures;

public abstract class TextMatchingFeature : AddonFeature
{
    /// <summary>
    /// PostSetup 時視窗還沒準備好的話，最多再等這麼多畫格。60 畫格約 1 秒，足以涵蓋
    /// 「Setup 完成但同一幀還沒 Show」的空窗，又不會在真的永遠不顯示的視窗上纏太久。
    /// </summary>
    private const int ReadyRetryFrames = 60;

    /// <summary>
    /// 距離上一次評估超過這麼久，就當作是換了一個新的對話框，log 重新印一次。
    /// 掛在 PostUpdate 上的功能每畫格都會評估，所以只要有這麼長的空檔就一定是關掉了。
    /// </summary>
    private const long DecisionLogResetMs = 1000;

    private string? _retryAddonName;
    private int _retryFramesLeft;
    private bool _retrySubscribed;

    private string? _lastLoggedText;
    private bool _lastLoggedProceeding;
    private long _lastDecisionTick;

    protected override bool IsEnabled() => true;
    protected abstract unsafe string GetSetLastSeenText(AtkUnitBase* atk);
    protected abstract unsafe object? ShouldProceed(string text, AtkUnitBase* atk);
    protected abstract unsafe void Proceed(AtkUnitBase* atk, object? matchingNode = null);

    public override void Disable()
    {
        CancelRetry();
        base.Disable();
    }

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (!P.Active) return;

        if (eventType is AddonEvent.PreFinalize && addonInfo.AddonName is "SelectString" or "SelectIconString")
        {
            SetEntry();
            return;
        }

        if (!GenericHelpers.IsAddonReady(atk))
        {
            if (addonInfo.AddonName is "Talk") return; // don't bother logging this
            Log("Addon not ready");

            // PostSetup 只會來一次。以前這裡直接 return，視窗只要在 Setup 當下還沒被 Show
            // （道具交易的確認框實測就是這樣），這個對話框就再也不會被檢查一次——沒有錯誤、
            // 沒有訊息，症狀只是「沒有自動點」。改成掛在畫格更新上重試，並且每次都用名稱
            // 重新解析位址，絕不跨幀保存原生指標。
            if (eventType is AddonEvent.PostSetup)
                ScheduleRetry(addonInfo.AddonName);
            return;
        }

        CancelRetry();
        Process(atk);
    }

    private unsafe void Process(AtkUnitBase* atk)
    {
        var text = GetSetLastSeenText(atk);
        var matchingNode = ShouldProceed(text, atk);
        var proceeding = matchingNode is not null;

        if (ShouldLogDecision(text, proceeding))
        {
            Log($"text={text}");
            Log(proceeding ? "Proceeding" : "Not proceeding");
        }

        if (matchingNode is not null)
            Proceed(atk, matchingNode);
    }

    /// <summary>
    /// 判斷這次的評估結果值不值得寫一行 log。Talk 這類掛在 PostUpdate 上的功能，
    /// 對話框開著就是每一畫格評估一次，逐次印會把整份 log 洗掉（實測 3.85 萬行的
    /// 「Not proceeding」、峰值 862 行/分），而且連續幾百行的內容一模一樣。
    /// 只有「看到的文字」或「要不要動作」跟上一次不同時才印；另外距離上一次評估
    /// 超過 <see cref="DecisionLogResetMs"/> 毫秒就視為換了一個對話框，重新印一次，
    /// 這樣「同一個 NPC 再講一次話」不會被誤當成重複而整段消失。
    /// 這個方法只影響 log，判斷與動作本身完全沒變。
    /// </summary>
    private bool ShouldLogDecision(string text, bool proceeding)
    {
        var now = Environment.TickCount64;
        var stale = _lastDecisionTick == 0 || now - _lastDecisionTick > DecisionLogResetMs;
        var changed = stale
            || proceeding != _lastLoggedProceeding
            || !string.Equals(text, _lastLoggedText, StringComparison.Ordinal);

        _lastDecisionTick = now;
        if (!changed) return false;

        _lastLoggedText = text;
        _lastLoggedProceeding = proceeding;
        return true;
    }

    private void ScheduleRetry(string addonName)
    {
        _retryAddonName = addonName;
        _retryFramesLeft = ReadyRetryFrames;
        if (_retrySubscribed) return;
        Svc.Framework.Update += OnRetryTick;
        _retrySubscribed = true;
    }

    private void CancelRetry()
    {
        _retryAddonName = null;
        if (!_retrySubscribed) return;
        Svc.Framework.Update -= OnRetryTick;
        _retrySubscribed = false;
    }

    private unsafe void OnRetryTick(IFramework framework)
    {
        if (_retryAddonName is not { } addonName || !P.Active)
        {
            CancelRetry();
            return;
        }

        if (--_retryFramesLeft < 0)
        {
            Log($"Gave up waiting for {addonName} to become ready");
            CancelRetry();
            return;
        }

        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(addonName, out var atk) || atk == null) return;
        if (!GenericHelpers.IsAddonReady(atk)) return;

        Log($"Addon became ready after {ReadyRetryFrames - _retryFramesLeft} frame(s)");
        CancelRetry();
        Process(atk);
    }

    protected bool EntryMatchesText(string pattern, string text, bool isRegex)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (isRegex)
        {
            try
            {
                var regex = RegexExtensions.TryCreateRegex(pattern.Trim('/'), RegexOptions.Compiled | RegexOptions.IgnoreCase);
                if (regex is null)
                {
                    LogError($"Invalid regex pattern {pattern}");
                    return false;
                }

                if (regex.IsMatch(text))
                {
                    LogVerbose($"Matched on regex {pattern} ({text})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError($"Invalid regex pattern {pattern}: {ex.Message}");
                return false;
            }
        }
        else if (text.Contains(pattern))
        {
            LogVerbose($"Matched on text {pattern} ({text})");
            return true;
        }
        LogVerbose($"No match on {pattern} ({text})");
        return false;
    }

    protected int? GetMatchingIndex(string pattern, string text, bool isRegex)
    {
        if (isRegex)
        {
            try
            {
                var regex = RegexExtensions.TryCreateRegex(pattern.Trim('/'), RegexOptions.Compiled | RegexOptions.IgnoreCase);
                if (regex is null)
                {
                    LogError($"Invalid regex pattern {pattern}");
                    return null;
                }

                var match = regex.Match(text);
                if (match.Success)
                {
                    LogVerbose($"Matched on regex {pattern} ({text})");
                    return match.Index;
                }
            }
            catch (Exception ex)
            {
                LogError($"Invalid regex pattern {pattern}: {ex.Message}");
                return null;
            }
        }
        else
        {
            var index = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index != -1)
            {
                LogVerbose($"Matched on text {pattern} ({text})");
                return index;
            }
        }
        LogVerbose($"No match on {pattern} ({text})");
        return null;
    }

    protected int? GetMatchingIndex(string[] entries, string pattern, bool isRegex)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            if (EntryMatchesText(pattern, entries[i], isRegex))
                return i;
        }
        return null;
    }

    private void SetEntry()
    {
        try
        {
            Service.Watcher.LastSeenListSelection = Service.Watcher.LastSeenListIndex < Service.Watcher.LastSeenListEntries.Length ? Service.Watcher.LastSeenListEntries?[Service.Watcher.LastSeenListIndex].Text ?? string.Empty : string.Empty;
            Service.Watcher.LastSeenListTarget = Service.Watcher.LastSeenListTarget = Svc.Targets.Target != null ? Svc.Targets.Target.Name.GetText() ?? string.Empty : string.Empty;
        }
        catch { }
    }

    protected class LastEntry
    {
        public uint TargetDataId { get; set; }
        public string EntryText { get; set; } = string.Empty;
    }

    protected bool CheckRestrictions(ITextNode node)
    {
        if (node is IZoneRestrictedNode { ZoneRestricted: true } zoneNode)
        {
            if (GenericHelpers.GetRow<TerritoryType>(Player.Territory) is { PlaceName.ValueNullable.Name: var name })
            {
                if (!EntryMatchesText(zoneNode.ZoneText, name.ToString(), zoneNode.ZoneIsRegex))
                {
                    Log($"Zone restriction not met: {name} does not match {zoneNode.ZoneText}");
                    return false;
                }
            }
        }

        if (node is ITargetRestrictedNode { TargetRestricted: true } targetNode)
        {
            if (Svc.Targets.Target is { Name: var name })
            {
                if (!EntryMatchesText(targetNode.TargetText, name.ToString(), targetNode.TargetIsRegex))
                {
                    Log($"Target restriction not met: {name} does not match {targetNode.TargetText}");
                    return false;
                }
            }
            else
            {
                Log($"Target restriction not met: No target selected");
                return false;
            }
        }

        if (node is IPlayerConditionRestrictedNode { RequiresPlayerConditions: true } playerConditionNode)
        {
            var conditions = playerConditionNode.PlayerConditions.Replace(" ", "").Split(',');
            Log($"[{nameof(IPlayerConditionRestrictedNode)}] Conditions: {string.Join(", ", conditions)}");
            if (!conditions.All(condition => Enum.TryParse<ConditionFlag>(condition.StartsWith('!') ? condition[1..] : condition, out var flag) && (condition.StartsWith('!') ? !Svc.Condition[flag] : Svc.Condition[flag])))
            {
                Log($"Matched on {node.Name}, but not all conditions were met");
                return false;
            }
        }

        if (node is INumberRestrictedNode { IsConditional: true } numberNode)
        {
            if (numberNode.ConditionalNumberRegex?.IsMatch(node.Name) ?? false)
            {
                PluginLog.Debug("AddonSelectYesNo: Is conditional matches");
                if (numberNode.ConditionalNumberRegex?.Match(node.Name) is { Success: true, Value: var result } && int.TryParse(result, out var value))
                {
                    PluginLog.Debug($"AddonSelectYesNo: Is conditional - {value}");
                    return numberNode.ComparisonType switch
                    {
                        ComparisonType.LessThan => value < numberNode.ConditionalNumber,
                        ComparisonType.GreaterThan => value > numberNode.ConditionalNumber,
                        ComparisonType.LessThanOrEqual => value <= numberNode.ConditionalNumber,
                        ComparisonType.GreaterThanOrEqual => value >= numberNode.ConditionalNumber,
                        ComparisonType.Equal => value == numberNode.ConditionalNumber,
                        _ => throw new Exception("Uncaught enum"),
                    };
                }
            }

            return false;
        }

        return true;
    }
}
