using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using System;
using System.Collections.Generic;
using System.Threading;

namespace YesAlready.Utils;

/// <summary>
/// 「同一扇視窗按過就不要再按，直到它真的收掉」的共用閘門。
/// </summary>
/// <remarks>
/// 🔴🔴 <b>存在的唯一理由</b>：<c>SelectYesno</c> 這類「按下即關」的視窗被按下之後有
/// <b>「正在關閉中」的幾幀</b>，這段期間 <c>GetAddonByName</c> 仍然回得到實例、
/// <c>IsVisible</c> 與 <c>UldManager.LoadedState == Loaded</c> 也都還成立 ——
/// 也就是說 <c>IsAddonReady</c> <b>三關全過、擋不住這個窗口</b>。
/// 此時再對它送一次 callback／模擬點擊就是原生 AccessViolation（<c>C0000005</c>）。
/// AVE 在 .NET Core 是 corrupted-state exception，<c>try</c>/<c>catch</c> 與任何例外隔離
/// 都攔不到，遊戲當場關閉 —— <b>唯一的防護是「不要送第二次」，不是「送了再接住」</b>。
/// <para>
/// ⚠️ 呼叫端原有的 <c>EzThrottler</c>／「按鈕還是可按的」<b>都不是</b>防護：
/// 節流記的是「上一次動作在哪個時刻」而不是「這扇窗按過了」，而 ECommons 的
/// <c>AddonMaster</c> 遇到停用的按鈕會翻 <c>NodeFlags</c> 強制啟用再點。
/// </para>
/// <para>
/// 🔑 <b>做法</b>：按下之前先登記「哪一個實例位址、用哪一組參數被按過」，
/// 在觀察到那扇窗<b>真的走完生命週期</b>之前不准再對同一個位址送同一組參數。
/// 🔴 全程只做<b>位址等值比較，永遠不解參</b> —— 被記下的那個位址隨時可能已經失效。
/// </para>
/// <para>
/// <b>解除封鎖的觀察點是 <c>AddonLifecycle</c> 的兩個事件，不是輪詢</b>：本外掛的按下點
/// 幾乎全部掛在 <c>PostUpdate</c>／<c>PostDraw</c> 上，而<b>窗消失的那一幀那些監聽器根本不會被叫到</b>，
/// 照抄「輪詢到位址從清單消失就解除」等於旗標永不解除。
/// <list type="bullet">
/// <item><see cref="AddonEvent.PreFinalize"/>＝這一扇正在被銷毀 ⇒ 按過的那扇已經到終點。</item>
/// <item><see cref="AddonEvent.PostSetup"/>＝有新的一扇被建立起來（含<b>位址重用</b>）⇒ 我們按過的那扇已經不是它了。</item>
/// </list>
/// 🔴 兩條監聽器<b>都按位址比對，不是按名稱清空</b>：同名的第二扇窗在第一扇還在關閉中時被建立
/// （台服交納「按下交出後連著出現兩扇確認框」就是這個形狀），按名稱清空會把第一扇的紀錄一起清掉，
/// 接下來對關閉中的第一扇送第二發＝原生 AVE。
/// ⚠️ 刻意<b>不</b>把 <c>PostRefresh</c> 也當解除點：它有可能在「關閉中」那幾幀觸發，
/// 那會把封鎖提早解除，正好把這道防線變成沒有。
/// </para>
/// <para>
/// 📌 <b>粒度＝（視窗位址，參數組）</b>：「同一扇窗連送不同參數」是正常流程
/// （交納視窗逐格填入），只擋「同位址同參數在窗走完前再按」。
/// 「回答一次即終結」的窗由呼叫端傳空參數鍵，等於一個實例只准按一次。
/// </para>
/// <para>
/// 🔴 <b>逃生口是刻意的</b>：萬一某扇窗既不 finalize 也不重新 setup（例如上一次的 callback
/// 根本沒生效、視窗就是還開著），沒有逃生口的話呼叫端會<b>永遠</b>按不下去，
/// 等於把崩潰換成靜默失效。用<b>幀數</b>而不是毫秒：危險窗口的長度本來就是以幀計的，
/// 遊戲卡頓時兩者一起拉長。
/// <list type="bullet">
/// <item>單答終結窗（按下即關）：<see cref="ReleaseEscapeFrames"/>，走逃生口寫 <c>Information</c>。</item>
/// <item>多次互動窗（按一次翻一頁／開子視窗，窗不會因為被按而消失，代表是 <c>Talk</c>）：
/// <see cref="RoutineRePressEscapeFrames"/>，走逃生口是<b>常態</b>，寫 <c>Debug</c> 不洗版。
/// 判斷依據：關閉中的危險窗口 &lt;10 幀，15 幀不落在裡面。</item>
/// </list>
/// </para>
/// <para>
/// 🔴🔴 <b>時鐘一定要自己數，不能用 <c>UiBuilder.FrameCount</c></b>：Dalamud 的
/// <c>UiBuilder.OnDraw()</c> 在「使用者隱藏 UI」「過場動畫」「GPose」三種情況下會<b>提早 return</b>，
/// 而 <c>FrameCount++</c> 在那些 return 之後（三個開關預設全開）⇒ 過場期間那個計數器完全不前進，
/// 逃生口永不到期，而按下點走的是原生 detour 照常每幀被叫到 —— 結果是 Talk 之類的窗停在第一頁。
/// <see cref="Svc.Framework"/> 的 Update 不受 UI 隱藏影響，所以時鐘掛在那裡。
/// </para>
/// <para>⚠️ 只在主執行緒使用（與呼叫端的 <c>EzThrottler</c> 同一個前提）。</para>
/// </remarks>
internal static unsafe class AddonPressGuard
{
    /// <summary>
    /// 單答終結窗（按下即關）已經按過、那扇窗卻既沒消失也沒重建時，最多再等這麼多幀才允許補按一次。
    /// </summary>
    /// <remarks>
    /// 🔑 這不是節流 —— 真正的防護是「同一扇窗只按一次」，這個值只是防死鎖的逃生口。
    /// 90 幀（60fps 下約 1.5 秒）遠遠大於「關閉中的那幾幀」，補按永遠不會落在危險窗口內。
    /// </remarks>
    private const int ReleaseEscapeFrames = 90;

    /// <summary>
    /// 多次互動窗（<c>Talk</c> 類）的逃生口：同一實例按過之後，至少隔這麼多幀才准再按。
    /// </summary>
    /// <remarks>
    /// 這種窗「重按」本來就是流程本身（翻頁、逐格開子視窗），用 90 幀會把每一頁多拖 1.5 秒。
    /// 15 幀（約 0.25 秒）已經在「關閉中」危險窗口（&lt;10 幀）之外，而使用者幾乎感覺不到。
    /// </remarks>
    private const int RoutineRePressEscapeFrames = 15;

    /// <summary>位址表膨脹到這個數量時，順手清掉太久沒動的紀錄（正常情況下表裡只有個位數）。</summary>
    private const int PruneThreshold = 64;

    /// <summary>超過這麼多幀沒再被按過的位址紀錄，清理時可以丟（早就過了逃生口）。</summary>
    private const int PruneAgeFrames = 3600;

    private readonly record struct PressRecord(long Frame);

    private sealed class AddressRecords(string addonName)
    {
        public string AddonName { get; } = addonName;
        public Dictionary<string, PressRecord> ByParam { get; } = new(StringComparer.Ordinal);
        public long LastFrame { get; set; }
    }

    /// <summary>位址 → 這個實例被按過的參數組。位址只當字典鍵，從不解參考。</summary>
    private static readonly Dictionary<nint, AddressRecords> Pressed = [];

    /// <summary>0＝還沒掛監聽器。用 <see cref="Interlocked"/> 而不是 <c>bool</c>：重複訂閱不是沒效果，
    /// 而是計數器一個 tick 前進 2＝所有逃生口對半砍，會把補按往危險窗口推。</summary>
    private static int watching;

    private static long frameCount;
    private static IAddonLifecycle.AddonEventDelegate? lifecycleHandler;

    /// <summary>
    /// 掛上幀計數器與解除封鎖用的全域監聽器（重複呼叫是 no-op）。
    /// </summary>
    /// <remarks>
    /// 📌 <b>外掛建構子裡就先呼叫一次</b>，而且要排在其他 <c>Framework.Update</c> 訂閱之前：
    /// 同一個外掛內部的 <c>Framework.Update</c> 多播委派<b>包在單一 try/catch 裡</b>
    /// （<c>FrameworkPluginScoped</c> 每個外掛只掛一個 forward），排在前面的處理常式擲例外時，
    /// 後面所有處理常式那個 tick 完全不會被呼叫 —— 時鐘停住＝逃生口失準。
    /// <para>
    /// ⚠️ <b>監聽器之間的呼叫順序不能當保證</b>：<c>RegisterListener</c> 走
    /// <c>Framework.RunOnTick</c>，而它底下的 <c>ThreadBoundTaskScheduler</c> 用
    /// <c>ConcurrentDictionary</c> 存待跑的工作、<c>Run()</c> 直接列舉 <c>Keys</c> ——
    /// <b>列舉順序與排入順序無關</b>。真正把順序這個變數拿掉的是
    /// <see cref="OnAddonLifecycle"/> 裡的「這一幀才登記的不清」。
    /// </para>
    /// </remarks>
    public static void EnsureWatching()
    {
        if (Interlocked.CompareExchange(ref watching, 1, 0) != 0) return;

        Svc.Framework.Update += OnFrameworkUpdate;
        lifecycleHandler = OnAddonLifecycle;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, lifecycleHandler);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, lifecycleHandler);
    }

    /// <summary>外掛卸載時硬拆所有監聽器（不留指向本組件的委派）。</summary>
    public static void ForceTeardown()
    {
        if (Interlocked.Exchange(ref watching, 0) == 1)
        {
            Svc.Framework.Update -= OnFrameworkUpdate;
            if (lifecycleHandler != null)
            {
                Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, lifecycleHandler);
                Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, lifecycleHandler);
            }
        }

        lifecycleHandler = null;
        Pressed.Clear();
    }

    /// <summary>
    /// 登記「即將對這扇<b>按下即關</b>的視窗送出動作」。<b>回 <see langword="false"/> ＝這一幀絕對不能送。</b>
    /// </summary>
    /// <remarks>
    /// 呼叫點要放在<b>緊接著送出動作之前</b> —— 這支一回 <see langword="true"/> 就已經把
    /// 「按過了」記下去，登記完卻不按的話會白白封鎖到逃生口為止。
    /// </remarks>
    /// <param name="addonName">視窗名稱，只用在 log 與節流鍵。</param>
    /// <param name="addon">實例位址，只做等值比較。</param>
    /// <param name="paramKey">參數組的字串形狀；空字串＝「這扇窗一個實例只准按一次，不分參數」。</param>
    public static bool TryBeginPress(string? addonName, AtkUnitBase* addon, string? paramKey = null)
        => TryBeginPressCore(addonName, addon, paramKey, routine: false);

    /// <summary>
    /// 登記「即將對這扇<b>按了不會關</b>的多次互動視窗送出動作」（翻頁式對話框、逐格開子視窗）。
    /// <b>回 <see langword="false"/> ＝這一幀不要送，下一輪再來。</b>
    /// </summary>
    /// <remarks>逃生口是 <see cref="RoutineRePressEscapeFrames"/> 幀，走逃生口是常態、只寫 Debug。</remarks>
    public static bool TryBeginRoutinePress(string? addonName, AtkUnitBase* addon, string? paramKey = null)
        => TryBeginPressCore(addonName, addon, paramKey, routine: true);

    private static bool TryBeginPressCore(string? addonName, AtkUnitBase* addon, string? paramKey, bool routine)
    {
        // 🔴 這一行要在所有 early return 之前：沒有掛上時鐘的話逃生口永遠不會到期。
        EnsureWatching();

        if (addon == null) return false;

        var name = addonName ?? string.Empty;
        var key = paramKey ?? string.Empty;
        var address = (nint)addon;
        var frame = frameCount;

        Pressed.TryGetValue(address, out var records);
        if (records != null && records.ByParam.TryGetValue(key, out var pressed))
        {
            var waited = frame - pressed.Frame;
            var escape = routine ? RoutineRePressEscapeFrames : ReleaseEscapeFrames;

            if (waited < escape)
            {
                if (routine)
                {
                    // 多次互動窗的正常等待：每幀都會回來問，寫 Debug 且節流。
                    if (EzThrottler.Throttle($"YesAlready.AddonPressGuard.RoutineHold.{name}", 1000))
                        PluginLog.Debug($"[AddonPressGuard] 「{name}」（實例 0x{address:X}{DescribeKey(key)}）{waited} 幀前才按過，" +
                                        $"等滿 {RoutineRePressEscapeFrames} 幀再按。");
                }
                else
                {
                    // 🔴 這就是崩潰的那一幀。診斷寫 Information（使用者跑 LogLevel 2），並節流免得洗版。
                    if (EzThrottler.Throttle($"YesAlready.AddonPressGuard.Hold.{name}", 1000))
                        PluginLog.Information($"[AddonPressGuard] 「{name}」（實例 0x{address:X}{DescribeKey(key)}）按過之後還沒觀察到它收掉，" +
                                              "這一幀不再送 —— 對關閉中的視窗送 callback 是攔不到的存取違規。");
                }

                return false;
            }

            if (routine)
            {
                if (EzThrottler.Throttle($"YesAlready.AddonPressGuard.RoutineEscape.{name}", 10000))
                    PluginLog.Debug($"[AddonPressGuard] 「{name}」（實例 0x{address:X}{DescribeKey(key)}）按下後 {waited} 幀仍是同一實例，" +
                                    "多次互動窗走逃生口再按一次。");
            }
            else if (EzThrottler.Throttle($"YesAlready.AddonPressGuard.Release.{name}", 10000))
            {
                PluginLog.Information($"[AddonPressGuard] 「{name}」（實例 0x{address:X}{DescribeKey(key)}）按下後 {waited} 幀" +
                                      "既沒有被銷毀也沒有重新建立，判定為「上一次按下沒生效」而不是「正在關閉」，解除封鎖讓呼叫端重試。");
            }
        }

        if (records == null)
        {
            PruneIfCrowded(frame);
            records = new AddressRecords(name);
            Pressed[address] = records;
        }

        records.ByParam[key] = new PressRecord(frame);
        records.LastFrame = frame;
        return true;
    }

    private static string DescribeKey(string paramKey) => paramKey.Length == 0 ? string.Empty : $"，參數 {paramKey}";

    /// <summary>
    /// 🔴 函式體內不可以有任何條件：一旦時鐘會停，逃生口就跟著失準。
    /// </summary>
    private static void OnFrameworkUpdate(IFramework framework) => frameCount++;

    /// <summary>該位址走完（或重新開始）生命週期：把它底下的紀錄清掉。</summary>
    /// <remarks>
    /// 🔴 <b>按位址比對，不是按名稱清空。</b>同名的兩扇窗可以並存（第一扇正在關閉、第二扇剛建好），
    /// 按名稱清空會把還在關閉中的那一扇的紀錄一起清掉，下一幀就會對它送第二發。
    /// <para>
    /// 🔴 <see cref="AddonEvent.PostSetup"/> 只清「不是這一幀才登記的」紀錄：本 pin 的 Dalamud
    /// 對同一個事件是在同一次派送裡依清單順序逐一呼叫監聽器（不做快照），而排序不是我們能決定的。
    /// 有模組在 <c>PostSetup</c> 處理常式裡就按下時，只要守衛排在它後面，模組剛登記完位址就輪到
    /// 這支把同一個位址清掉 —— 那扇窗接下來的每幀重送就完全沒有守衛。
    /// 「這一幀才登記的不清」把順序這個變數整個拿掉。
    /// ⚙️ 一幀之內不可能發生「舊的還在、新的已經建在同一個位址」：位址要被重用得先 finalize，
    /// 而 <see cref="AddonEvent.PreFinalize"/> 沒有這個豁免，所以重用場景裡紀錄早就被清掉了。
    /// </para>
    /// </remarks>
    private static void OnAddonLifecycle(AddonEvent type, AddonArgs args)
    {
        var address = args.Addon.Address;
        if (address == nint.Zero) return;

        if (type != AddonEvent.PostSetup)
        {
            Pressed.Remove(address);
            return;
        }

        if (!Pressed.TryGetValue(address, out var records)) return;

        List<string>? stale = null;
        foreach (var (paramKey, record) in records.ByParam)
        {
            if (record.Frame == frameCount) continue;
            (stale ??= []).Add(paramKey);
        }

        // 整筆都是這一幀才登記的 ⇒ 是「模組剛在這次 PostSetup 派送裡按下」，不是新的一扇。
        if (stale == null) return;

        foreach (var paramKey in stale) records.ByParam.Remove(paramKey);
        if (records.ByParam.Count == 0) Pressed.Remove(address);
    }

    private static void PruneIfCrowded(long frame)
    {
        if (Pressed.Count < PruneThreshold) return;

        List<nint>? stale = null;
        foreach (var (address, records) in Pressed)
        {
            if (frame - records.LastFrame > PruneAgeFrames) (stale ??= []).Add(address);
        }

        if (stale == null) return;
        foreach (var address in stale) Pressed.Remove(address);
    }
}
