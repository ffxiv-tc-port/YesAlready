using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using ECommons.EzHookManager;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace YesAlready;

public class Watcher : IDisposable
{
    private unsafe delegate void* FireCallbackDelegate(AtkUnitBase* atkUnitBase, int valueCount, AtkValue* atkValues, byte updateVisibility);
    [EzHook("E8 ?? ?? ?? ?? 0F B6 E8 8B 44 24 20", detourName: nameof(FireCallbackDetour), true)]
    private readonly EzHook<FireCallbackDelegate> FireCallbackHook = null!;

    /// <summary>
    /// 距離上一次 callback 超過這麼久就當作是新的一輪操作，重複的內容也重新印一次。
    /// </summary>
    private const long CallbackLogResetMs = 1000;

    private bool _wasDisableKeyPressed;
    private uint _lastTargetId;
    private string? _lastLoggedCallback;
    private long _lastCallbackLogTick;

    public string LastSeenDialogText { get; set; } = string.Empty;
    public string LastSeenOkText { get; set; } = string.Empty;
    public string LastSeenListSelection { get; set; } = string.Empty;
    public int LastSeenListIndex { get; set; }
    public string LastSeenListTarget { get; set; } = string.Empty;
    public (int Index, string Text)[] LastSeenListEntries { get; set; } = [];
    public string LastSeenTalkTarget { get; set; } = string.Empty;
    public string LastSeenNumericsText { get; set; } = string.Empty;
    public DateTime EscapeLastPressed { get; set; } = DateTime.MinValue;
    public string EscapeTargetName { get; set; } = string.Empty;
    public bool ForcedYesKeyPressed { get; set; }
    public bool ForcedTalkKeyPressed { get; set; }
    public bool DisableKeyPressed { get; set; }
    public LastListEntry? LastSelectedListEntry { get; set; } = new();

    public Watcher()
    {
        EzSignatureHelper.Initialize(this);
        Svc.Framework.Update += FrameworkUpdate;
    }

    public void Dispose() => Svc.Framework.Update -= FrameworkUpdate;

    public class LastListEntry
    {
        public uint TargetDataId { get; set; }
        public ListEntryNode? Node { get; set; }
    }

    private void FrameworkUpdate(IFramework framework)
    {
        if (!P.Active && !_wasDisableKeyPressed) return;
        DisableKeyPressed = C.DisableKey != VirtualKey.NO_KEY && Svc.KeyState[C.DisableKey];

        if (P.Active && DisableKeyPressed && !_wasDisableKeyPressed)
            C.Enabled = false;
        else if (!P.Active && !DisableKeyPressed && _wasDisableKeyPressed)
            C.Enabled = true;

        _wasDisableKeyPressed = DisableKeyPressed;

        ForcedYesKeyPressed = C.ForcedYesKey != VirtualKey.NO_KEY && Svc.KeyState[C.ForcedYesKey];

        ForcedTalkKeyPressed = C.ForcedTalkKey != VirtualKey.NO_KEY && C.SeparateForcedKeys && Svc.KeyState[C.ForcedTalkKey];

        if (Svc.KeyState[VirtualKey.ESCAPE])
        {
            EscapeLastPressed = DateTime.Now;

            var target = Svc.Targets.Target;
            EscapeTargetName = target != null ? target.Name.GetText() : string.Empty;
        }

        if (Svc.Targets.Target is { BaseId: var id })
        {
            if (id != _lastTargetId)
                Service.Watcher.LastSelectedListEntry = null;
            _lastTargetId = id;
        }
        else
            Service.Watcher.LastSelectedListEntry = null;
    }

    private unsafe void* FireCallbackDetour(AtkUnitBase* atkUnitBase, int valueCount, AtkValue* atkValues, byte updateVisibility)
    {
        if (atkUnitBase->NameString is not ("SelectString" or "SelectIconString"))
            return FireCallbackHook.Original(atkUnitBase, valueCount, atkValues, updateVisibility);

        try
        {
            var atkValueList = Enumerable.Range(0, valueCount)
                .Select<int, object>(i => atkValues[i].Type switch
                {
                    ValueType.Int => atkValues[i].Int,
                    ValueType.String => Marshal.PtrToStringUTF8(new IntPtr(atkValues[i].String)) ?? string.Empty,
                    ValueType.UInt => atkValues[i].UInt,
                    ValueType.Bool => atkValues[i].Byte != 0,
                    _ => $"Unknown Type: {atkValues[i].Type}"
                })
                .ToList();
            // 這個 hook 每次 SelectString/SelectIconString 觸發 callback 都會進來，逐次印會
            // 把整份 log 洗掉（實測 2.88 萬行、峰值 862 行/分），而連續的內容幾乎都一模一樣。
            // 只在內容跟上一次不同、或距離上一次超過 CallbackLogResetMs 毫秒時才印。
            // LastSeenListIndex 照樣每次更新，行為沒變。
            var message = $"[{nameof(Watcher)}] Callback triggered on {atkUnitBase->NameString} with values: {string.Join(", ", atkValueList.Select(value => value.ToString()))}";
            var nowTick = Environment.TickCount64;
            if (_lastCallbackLogTick == 0 || nowTick - _lastCallbackLogTick > CallbackLogResetMs
                || !string.Equals(message, _lastLoggedCallback, StringComparison.Ordinal))
            {
                _lastLoggedCallback = message;
                PluginLog.Debug(message);
            }
            _lastCallbackLogTick = nowTick;

            LastSeenListIndex = atkValues[0].Int;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Exception in {nameof(FireCallbackDetour)}: {ex.Message}");
            return FireCallbackHook.Original(atkUnitBase, valueCount, atkValues, updateVisibility);
        }
        return FireCallbackHook.Original(atkUnitBase, valueCount, atkValues, updateVisibility);
    }
}
