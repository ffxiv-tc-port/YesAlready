using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.Linq;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostUpdate)]
internal class Talk : TextMatchingFeature
{
    /// <summary>強制鍵連續生效的判定空檔：隔超過這麼久才算新的一次按住，log 只在每次按住的開頭寫一行。</summary>
    private const long ForcedLogResetMs = 1000;

    private long _lastForcedTick;

    // 規則世代號或設定實例換了才重建；節點的啟用與目標文字仍在比對時現讀。
    private int _rulesVersionSeen;
    private Configuration? _rulesConfigSeen;
    private TalkEntryNode[] _talkNodes = [];

    // 鍵是目標名字的原始位元組（SeString.Parse 只吃這段），不留任何原生指標或物件包裝。
    private readonly byte[] _targetNameKey = new byte[64];
    private int _targetNameKeyLength = -1;
    private string _targetNameText = string.Empty;

    protected override unsafe string GetSetLastSeenText(AtkUnitBase* atk)
    {
        var text = Svc.Targets.Target is { } target ? TargetName(target) : string.Empty;
        Service.Watcher.LastSeenTalkTarget = text;
        return text;
    }

    /// <summary>等同 <c>target.Name.TextValue</c>；名字位元組與上次相同就回上次的字串。</summary>
    private unsafe string TargetName(IGameObject target)
    {
        ReadOnlySpan<byte> raw = ((CSGameObject*)target.Address)->Name;
        var end = raw.IndexOf((byte)0);
        var name = end < 0 ? raw : raw[..end];
        if (_targetNameKeyLength >= 0 && name.SequenceEqual(_targetNameKey.AsSpan(0, _targetNameKeyLength)))
            return _targetNameText;

        _targetNameText = target.Name.TextValue;
        if (name.Length <= _targetNameKey.Length)
        {
            name.CopyTo(_targetNameKey);
            _targetNameKeyLength = name.Length;
        }
        else
        {
            _targetNameKeyLength = -1;
        }
        return _targetNameText;
    }

    protected override unsafe object? ShouldProceed(string text, AtkUnitBase* atk)
    {
        if (Service.Watcher.ForcedYesKeyPressed && !C.SeparateForcedKeys || Service.Watcher.ForcedTalkKeyPressed)
        {
            // PostUpdate 每幀都會走到這裡；ECommons 下被丟掉的 Debug 照樣有成本，所以只在按住的開頭寫。
            var now = System.Environment.TickCount64;
            if (now - _lastForcedTick > ForcedLogResetMs)
                PluginLog.Debug($"{nameof(Talk)}: Forced hotkey pressed");
            _lastForcedTick = now;
            return true;
        }

        foreach (var node in TalkNodes())
        {
            if (!node.Enabled || string.IsNullOrEmpty(node.TargetText))
                continue;

            if (EntryMatchesText(node.TargetText, text, node.TargetIsRegex))
                return node;
        }

        return null;
    }

    /// <summary>規則樹裡全部的 Talk 規則，順序同 <see cref="Configuration.GetAllNodes()"/>。</summary>
    /// <remarks>PostUpdate 每幀都走到這裡，所以只在規則世代號或 <c>C</c> 換了才重新走訪整棵樹。</remarks>
    private TalkEntryNode[] TalkNodes()
    {
        var version = Configuration.RulesVersion;
        var config = C;
        if (version != _rulesVersionSeen || !ReferenceEquals(config, _rulesConfigSeen))
        {
            _talkNodes = [.. config.GetAllNodes().OfType<TalkEntryNode>()];
            _rulesVersionSeen = version;
            _rulesConfigSeen = config;
        }
        return _talkNodes;
    }

    protected override unsafe void Proceed(AtkUnitBase* atk, object? matchingNode)
    {
        // Talk 是「按一次翻一頁、窗不會因為被按而消失」的多次互動窗：守衛照樣記位址，但逃生口只用 15 幀，每頁多等約 0.25 秒。
        // 🔑 守衛的鍵取「實際被按的那個指標」：addon 是以型別名稱重查 index 1 的結果，不一定是觸發這次 PostUpdate 的那一扇。

        // 🔴 就緒檢查一定要驗**實際要按的那個指標**：TryGetAddonMaster 只判非零、完全不驗就緒，而 HandleAddonEvent 驗過的是
        //    addonInfo.Addon，與這裡重解出來的 index 1 不保證是同一扇。
        //    ⚠️ 這**擋不住**「別的外掛剛按過、正在關閉中」—— IsAddonReady 的三關在拆除途中全過。
        //    排在守衛之前：被擋下的這一發不該佔掉一次守衛登記。
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.Talk>(out var addon)
            && addon.IsAddonReady
            && AddonPressGuard.TryBeginRoutinePress("Talk", addon.Base))
            addon.Click();
    }
}
