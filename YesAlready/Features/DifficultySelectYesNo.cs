using ECommons.EzHookManager;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostSetup)]
public class DifficultySelectYesNo : AddonFeature
{
    protected override bool IsEnabled() => C.DifficultySelectYesNoEnabled;

    public delegate nint ExecuteCommandDelegate(int command, int a1 = 0, int a2 = 0, int a3 = 0, int a4 = 0);
    // 沒解到＝null、不擲：靜態初始式一擲，之後每次讀這個型別的靜態成員都是 TypeInitializationException。
    public static readonly ExecuteCommandDelegate? ExecuteCommand = ResolveExecuteCommand();

    private static ExecuteCommandDelegate? ResolveExecuteCommand()
    {
        if (Svc.SigScanner.TryScanText("E8 ?? ?? ?? ?? 8D 46 0A", out var address)) return EzDelegate.Get<ExecuteCommandDelegate>(address);
        PluginLog.Warning("[YesAlready] 特徵碼對不上（DifficultySelectYesNo.ExecuteCommand）：自動選擇難度（DifficultySelectYesNo 視窗）停用，其他功能不受影響。");
        return null;
    }

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        Log($"Selecting difficulty: {C.DifficultySelectYesNo} [{ExecuteCommand != null}]");
        ExecuteCommand?.Invoke(823, (int)C.DifficultySelectYesNo);
    }

    public enum Difficulty
    {
        Normal = 0,
        Easy = 1,
        VeryEasy = 2,
    }
}
