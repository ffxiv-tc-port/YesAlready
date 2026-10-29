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
/// 🔴🔴 <b>存在的唯一理由</b>：<c>SelectYesno</c> 這類「按下即關」的視窗被按下之後有「正在關閉中」的幾幀，
/// 這段期間 <c>GetAddonByName</c> 仍回得到實例、<c>IsAddonReady</c> 三關全過；此時再送一次 callback 就是原生 AVE，
/// <c>try</c>/<c>catch</c> 攔不到 —— <b>唯一的防護是「不要送第二次」</b>。呼叫端的 <c>EzThrottler</c> 不是防護。
/// <para>
/// 粒度＝（視窗位址，參數組）；位址<b>只做等值比較，永遠不解參</b>。空參數鍵＋非多次互動＝終結按法：
/// 它還熱著時，同一位址換任何參數都不准。解除點：位址從同名清單消失（每幀輪詢）、同位址 <c>PostSetup</c>
/// （這一幀才登記的不清）、常駐窗連續隱藏 N 幀、逃生口。<c>PreFinalize</c> 是<b>上鎖點</b>，鎖到位址消失或同位址重建。
/// ⚠️ 刻意<b>不</b>把 <c>PostRefresh</c> 也當解除點：它有可能在「關閉中」那幾幀觸發。
/// </para>
/// <para>
/// 📌 本外掛是事件驅動的回應者：守衛只擋「同一扇關閉中的窗被再按一次」，不改變「看到框就回應」。
/// ⚠️ 只在主執行緒使用（與呼叫端的 <c>EzThrottler</c> 同一個前提）。任何不確定一律回 <see langword="false"/>。
/// </para>
/// </remarks>
internal static unsafe class AddonPressGuard
{
    /// <summary>
    /// 終結按法（按下即關）的逃生口與熱窗長度（幀）：已經按過、那扇窗卻既沒消失也沒重建時，最多等這麼多幀才允許補按一次。
    /// </summary>
    /// <remarks>
    /// 🔑 這不是節流 —— 真正的防護是「同一扇窗只按一次」，這個值只是防死鎖的逃生口。
    /// 用幀數而不是毫秒：危險窗口的長度本來就是以幀計的，遊戲卡頓時兩者一起拉長。
    /// </remarks>
    private const int ReleaseEscapeFrames = 90;

    /// <summary>
    /// 多次互動窗（<c>Talk</c> 類）的逃生口：同一實例按過之後，至少隔這麼多幀才准再按。
    /// </summary>
    /// <remarks>15 幀（約 0.25 秒）已經在「關閉中」危險窗口（&lt;10 幀）之外，而使用者幾乎感覺不到。</remarks>
    private const int RoutineRePressEscapeFrames = 15;

    /// <summary>位址表膨脹到這個數量時，先回收已過逃生口的冷紀錄；回收不到就這一發不放行。</summary>
    private const int PruneThreshold = 64;

    /// <summary>超過這麼多幀沒再被按過、也沒上鎖的位址紀錄，定期回收（早就過了逃生口，不改變任何判定）。</summary>
    private const int SweepMinAgeFrames = 3600;

    private const int SweepIntervalFrames = 60;

    /// <summary>
    /// 常駐視窗：關閉只是隱藏，位址整場留在清單裡，<see cref="AddonEvent.PreFinalize"/>／<see cref="AddonEvent.PostSetup"/>
    /// 都不會再發生。這些窗改由「按下後連續隱藏 <see cref="HiddenReleaseFrames"/> 幀」解除，而且送出前必須看得見（兩者互為反面）。
    /// </summary>
    /// <remarks>⚠️ 只收實機證實常駐的名字：把會被銷毀的窗誤收進來，等於在它拆除途中提早解除封鎖。</remarks>
    private static readonly HashSet<string> PersistentAddons = new(StringComparer.Ordinal)
    {
        "ContextIconMenu",
        "ContextMenu",
        "Talk",
    };

    /// <summary>常駐視窗按下後，要連續觀察到「還在清單裡、但已隱藏」這麼多幀才解除；看到可見就歸零重數。</summary>
    private const int HiddenReleaseFrames = 20;

    /// <summary>同名實例最多掃到第幾個（遊戲同名實例上限）；掃到第一個空的就提早停。</summary>
    private const int MaxAddonIndex = 256;

    /// <param name="Frame">按下時的守衛幀號。</param>
    /// <param name="Routine">以多次互動窗身分登記；為 <see langword="false"/> 且鍵為空＝終結按法。</param>
    private readonly record struct PressRecord(long Frame, bool Routine);

    private sealed class AddressRecords(string addonName)
    {
        public string AddonName { get; set; } = addonName;
        public Dictionary<string, PressRecord> ByParam { get; } = new(StringComparer.Ordinal);
        public long LastFrame { get; set; }

        /// <summary>連續觀察到「還在清單裡、但已隱藏」的幀數；只有常駐窗會累加，看得見或重新按下就歸零。</summary>
        public int HiddenFrames { get; set; }

        /// <summary>登記時在同名清單裡找得到這個位址；只有它成立才准用「從清單消失」解除。</summary>
        /// <remarks>名稱對不上時清單裡永遠找不到它，不設這道就會在下一幀被當成「已消失」而提早解除。</remarks>
        public bool Listed { get; set; }

        /// <summary>看過 <see cref="AddonEvent.PreFinalize"/>：在位址消失或同位址重建之前，任何按法都不准，也沒有逃生口。</summary>
        public bool Finalizing { get; set; }
    }

    /// <summary>這一幀在同名清單裡對某個位址的觀察。零值必須是「不解除」的答案。</summary>
    private enum AddonPresence
    {
        /// <summary>查詢不成立（名稱空、位址 0、同名實例超過掃描上限）。零值。</summary>
        Unknown = 0,

        /// <summary>掃完同名清單都沒有這個位址＝那扇窗已經收乾淨。</summary>
        NotListed = 1,

        Visible = 2,
        Hidden = 3,
    }

    private static readonly List<nint> ReleaseBuf = [];
    private static readonly List<string> StaleKeysBuf = [];

    /// <summary>「這個窗名第一次走隱藏解除」只寫一行 Information。</summary>
    private static readonly HashSet<string> HiddenReleaseReported = new(StringComparer.Ordinal);

    /// <summary>位址 → 這個實例被按過的參數組。位址只當字典鍵，從不解參考。</summary>
    private static readonly Dictionary<nint, AddressRecords> Pressed = [];

    /// <summary>曾經守護過的窗名：這些名字的實例一進 <see cref="AddonEvent.PreFinalize"/> 就上鎖（即使還沒按過）。</summary>
    private static readonly HashSet<string> GuardedNames = new(StringComparer.Ordinal);

    /// <summary>0＝還沒掛監聽器。用 <see cref="Interlocked"/> 而不是 <c>bool</c>：重複訂閱會讓計數器一個 tick 前進 2，逃生口對半砍。</summary>
    private static int watching;

    private static long frameCount;
    private static IAddonLifecycle.AddonEventDelegate? lifecycleHandler;

    /// <summary>
    /// 合格的幀時鐘：掛在 <see cref="Svc.Framework"/> 的 Update 上自己數，過場動畫／隱藏 UI／
    /// GPose 期間照樣前進。<b>本外掛任何「等 N 幀」的閘門一律讀這裡。</b>
    /// </summary>
    /// <remarks>
    /// 🔴🔴 <b>不要用 <c>UiBuilder.FrameCount</c></b>：它在隱藏 UI／過場／GPose 期間完全不前進，等待閘門永不到期；
    /// 也<b>絕對不要做混合時鐘</b>（兩個計數器的絕對值完全不同）。
    /// 📌 這裡順手 <see cref="EnsureWatching"/>：呼叫端可能在還沒有任何按下紀錄之前就先讀時鐘。
    /// </remarks>
    public static long CurrentFrame
    {
        get
        {
            EnsureWatching();
            return frameCount;
        }
    }

    /// <summary>
    /// 掛上幀計數器與解除封鎖用的全域監聽器（重複呼叫是 no-op）。
    /// </summary>
    /// <remarks>
    /// 📌 <b>外掛建構子裡就先呼叫一次</b>，而且要排在其他 <c>Framework.Update</c> 訂閱之前：
    /// 同一個外掛的 <c>Framework.Update</c> 處理常式共用一個 try/catch，排在前面的擲例外時後面那個 tick 全部不跑。
    /// ⚠️ 監聽器之間的呼叫順序不能當保證；真正把順序這個變數拿掉的是 <see cref="OnAddonLifecycle"/> 的同幀豁免。
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
        GuardedNames.Clear();
    }

    /// <summary>
    /// 登記「即將對這扇<b>按下即關</b>的視窗送出動作」。<b>回 <see langword="false"/> ＝這一幀絕對不能送。</b>
    /// </summary>
    /// <remarks>
    /// 呼叫點要放在<b>緊接著送出動作之前</b> —— 這支一回 <see langword="true"/> 就已經把
    /// 「按過了」記下去，登記完卻不按的話會白白封鎖到逃生口為止。
    /// </remarks>
    /// <param name="addonName">視窗名稱，也是輪詢解除用的名字；空的一律不放行。</param>
    /// <param name="addon">實例位址，只做等值比較。</param>
    /// <param name="paramKey">參數組的字串形狀；空字串＝終結按法（這扇窗一個實例只准按一次，不分參數）。</param>
    public static bool TryBeginPress(string? addonName, AtkUnitBase* addon, string? paramKey = null)
        => TryBeginPressCore(addonName, addon, paramKey, routine: false);

    /// <summary>
    /// 登記「即將對這扇<b>按了不會關</b>的多次互動視窗送出動作」（翻頁式對話框、逐格開子視窗）。
    /// <b>回 <see langword="false"/> ＝這一幀不要送，下一輪再來。</b>
    /// </summary>
    /// <remarks>逃生口是 <see cref="RoutineRePressEscapeFrames"/> 幀，走逃生口是常態、只寫 Debug。</remarks>
    public static bool TryBeginRoutinePress(string? addonName, AtkUnitBase* addon, string? paramKey = null)
        => TryBeginPressCore(addonName, addon, paramKey, routine: true);

    /// <summary>
    /// 記下「不經守衛的既有路徑已經對這扇按下即關的窗按了終結按法」：只登記、不判斷、不改呼叫端行為，
    /// 讓之後別的路徑對同一實例的 <see cref="TryBeginPress"/> 擋得住。表滿就不記（只會少擋，不會多擋）。
    /// </summary>
    public static void NotePress(string? addonName, AtkUnitBase* addon)
    {
        EnsureWatching();
        if (addon == null || string.IsNullOrEmpty(addonName)) return;

        var address = (nint)addon;
        GuardedNames.Add(addonName);
        if (!Pressed.TryGetValue(address, out var records))
        {
            if (!MakeRoom()) return;
            records = new AddressRecords(addonName);
            Pressed[address] = records;
        }

        if (!records.Listed && LookUp(addonName, address, readVisibility: false) == AddonPresence.Visible)
        {
            records.AddonName = addonName;
            records.Listed = true;
        }

        records.ByParam[string.Empty] = new PressRecord(frameCount, false);
        records.LastFrame = frameCount;
        records.HiddenFrames = 0;
    }

    /// <summary>
    /// 先把窗名列進守護名單：之後這個名字的任何實例一進 <see cref="AddonEvent.PreFinalize"/> 就上鎖，
    /// 即使本外掛還沒按過它（例如別的外掛先送了關窗的 callback）。
    /// </summary>
    public static void WatchName(string? addonName)
    {
        EnsureWatching();
        if (!string.IsNullOrEmpty(addonName)) GuardedNames.Add(addonName);
    }

    /// <summary>這個實例已經有人登記按過終結按法、或已上鎖（正在拆除）；只查不登記。</summary>
    public static bool IsAnsweredOrFinalizing(AtkUnitBase* addon)
    {
        if (addon == null || !Pressed.TryGetValue((nint)addon, out var records)) return false;
        return records.Finalizing || records.ByParam.ContainsKey(string.Empty);
    }

    private static bool TryBeginPressCore(string? addonName, AtkUnitBase* addon, string? paramKey, bool routine)
    {
        // 🔴 這一行要在所有 early return 之前：沒有掛上時鐘的話逃生口永遠不會到期。
        EnsureWatching();

        if (addon == null || string.IsNullOrEmpty(addonName)) return false;

        var name = addonName;
        var key = paramKey ?? string.Empty;
        var address = (nint)addon;
        var frame = frameCount;

        // 常駐窗的解除條件是「連續隱藏」，這裡是它的反面：隱藏中（或這一幀查不到）的常駐窗一律不送。
        if (PersistentAddons.Contains(name) && LookUp(name, address, readVisibility: true) != AddonPresence.Visible) return false;

        GuardedNames.Add(name);
        Pressed.TryGetValue(address, out var records);
        if (records != null)
        {
            if (records.Finalizing)
            {
                LogHold(name, address, key, "已經看到它 PreFinalize、還沒看到它消失");
                return false;
            }

            // 終結按法還熱著：同一扇窗換任何一組參數都不准——那之後它就是在關閉中。
            if (key.Length != 0 &&
                records.ByParam.TryGetValue(string.Empty, out var answeredRec) &&
                !answeredRec.Routine &&
                frame - answeredRec.Frame < ReleaseEscapeFrames)
            {
                LogHold(name, address, key, "終結按法按過之後還沒觀察到它收掉");
                return false;
            }

            if (records.ByParam.TryGetValue(key, out var pressed) && !PassesEscape(name, address, key, pressed, routine, frame))
                return false;
        }

        if (records == null)
        {
            if (!MakeRoom())
            {
                if (EzThrottler.Throttle("YesAlready.AddonPressGuard.Full", 10000))
                    PluginLog.Information($"[AddonPressGuard] 按下紀錄已滿 {PruneThreshold} 筆且都還熱著，「{name}」這一發不送。");
                return false;
            }

            records = new AddressRecords(name);
            Pressed[address] = records;
        }

        if (!records.Listed && LookUp(name, address, readVisibility: false) == AddonPresence.Visible)
        {
            records.AddonName = name;
            records.Listed = true;
        }

        records.ByParam[key] = new PressRecord(frame, routine);
        records.LastFrame = frame;
        // 🔴 承重：隱藏解除是「從最後一次按下起算」連續隱藏幾幀；漏掉這一行，前一次累到一半的幀數會被沿用。
        records.HiddenFrames = 0;

        // 跨外掛重按診斷：全艦隊各份守衛逐字統一這一行，格式改了就無法交叉比對。
        // 不節流（節流會蓋掉「同毫秒重按」這個唯一有意義的樣本）；只印位址數值，永不解參考。
        PluginLog.Verbose($"[按窗診斷] plugin=YesAlready addon={name} addr=0x{address:X} key={key}");
        return true;
    }

    /// <summary>同位址同參數按過：還在逃生口內回 <see langword="false"/>；到期就記一行並放行。</summary>
    private static bool PassesEscape(string name, nint address, string key, PressRecord pressed, bool routine, long frame)
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
                LogHold(name, address, key, "按過之後還沒觀察到它收掉");
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

        return true;
    }

    /// <summary>被擋那一幀的診斷：要使用者回報的診斷寫 Information，每個窗名 1 秒一則。</summary>
    private static void LogHold(string name, nint address, string key, string reason)
    {
        if (EzThrottler.Throttle($"YesAlready.AddonPressGuard.Hold.{name}", 1000))
            PluginLog.Information($"[AddonPressGuard] 「{name}」（實例 0x{address:X}{DescribeKey(key)}）{reason}，" +
                                  "這一幀不再送 —— 對關閉中的視窗送 callback 是攔不到的存取違規。");
    }

    private static string DescribeKey(string paramKey) => paramKey.Length == 0 ? string.Empty : $"，參數 {paramKey}";

    /// <summary>🔴 遞增必須是第一行、前面不可以有任何條件：一旦時鐘會停，逃生口就跟著失準。</summary>
    private static void OnFrameworkUpdate(IFramework framework)
    {
        frameCount++;
        PollRecords();
        if (frameCount % SweepIntervalFrames == 0) SweepColdRecords();
    }

    /// <summary>
    /// 每幀的解除路徑：①登記時找得到、現在從同名清單消失 ⇒ 解除（含 PreFinalize 鎖）；
    /// ②常駐窗連續隱藏 <see cref="HiddenReleaseFrames"/> 幀 ⇒ 解除。
    /// </summary>
    /// <remarks>
    /// 判準刻意<b>不</b>用「還可不可見」當「窗不見了」（常駐窗例外）：拆除途中那幾幀三關全過。
    /// 只有「找到了而且看不見」才累加隱藏幀；關閉中那幾幀窗還看得見，永遠累不滿。
    /// </remarks>
    private static void PollRecords()
    {
        if (Pressed.Count == 0) return;

        ReleaseBuf.Clear();
        foreach (var (address, records) in Pressed)
        {
            var persistent = PersistentAddons.Contains(records.AddonName);
            var presence = LookUp(records.AddonName, address, readVisibility: persistent);
            var present = presence != AddonPresence.NotListed;
            if (!present)
            {
                if (records.Listed) ReleaseBuf.Add(address);
                else records.HiddenFrames = 0;
                continue;
            }

            if (!persistent || records.Finalizing) continue;
            if (presence != AddonPresence.Hidden)
            {
                records.HiddenFrames = 0;
                continue;
            }

            if (++records.HiddenFrames < HiddenReleaseFrames) continue;

            ReleaseBuf.Add(address);
            if (HiddenReleaseReported.Add(records.AddonName))
                PluginLog.Information($"[AddonPressGuard] 「{records.AddonName}」是常駐視窗（隱藏而不銷毀），按下記號改由「連續隱藏 {HiddenReleaseFrames} 幀」解除。這一行每個視窗名每次遊戲只寫一次。");
        }

        foreach (var address in ReleaseBuf) Pressed.Remove(address);
        ReleaseBuf.Clear();
    }

    /// <summary>在同名的所有實例裡找 <paramref name="address"/>。</summary>
    /// <remarks>
    /// 位址只做等值比較；解參考的是 <c>GetAddonByName</c> 這一幀交回來的指標，而且只在 <paramref name="readVisibility"/> 時才讀。
    /// 不讀可見性時，找到一律回 <see cref="AddonPresence.Visible"/>。
    /// </remarks>
    private static AddonPresence LookUp(string addonName, nint address, bool readVisibility)
    {
        if (string.IsNullOrEmpty(addonName) || address == nint.Zero) return AddonPresence.Unknown;

        for (var index = 1; index <= MaxAddonIndex; index++)
        {
            var unit = Svc.GameGui.GetAddonByName<AtkUnitBase>(addonName, index);
            if (unit == null) return AddonPresence.NotListed;
            if ((nint)unit != address) continue;

            if (!readVisibility) return AddonPresence.Visible;
            return unit->IsVisible ? AddonPresence.Visible : AddonPresence.Hidden;
        }

        return AddonPresence.Unknown;
    }

    /// <summary>回收「已經不可能再擋住任何人」的槽位（不改變任何一次「按／不按」的判定）。</summary>
    private static void SweepColdRecords()
    {
        if (Pressed.Count == 0) return;

        ReleaseBuf.Clear();
        foreach (var (address, records) in Pressed)
        {
            if (records.Finalizing || frameCount - records.LastFrame < SweepMinAgeFrames) continue;
            ReleaseBuf.Add(address);
        }

        foreach (var address in ReleaseBuf) Pressed.Remove(address);
        ReleaseBuf.Clear();
    }

    /// <summary>表滿時只回收已過終結逃生口、沒上鎖的冷紀錄；回收完仍滿回 <see langword="false"/>。</summary>
    private static bool MakeRoom()
    {
        if (Pressed.Count < PruneThreshold) return true;

        ReleaseBuf.Clear();
        foreach (var (address, records) in Pressed)
        {
            if (!records.Finalizing && frameCount - records.LastFrame >= ReleaseEscapeFrames) ReleaseBuf.Add(address);
        }

        foreach (var address in ReleaseBuf) Pressed.Remove(address);
        ReleaseBuf.Clear();
        return Pressed.Count < PruneThreshold;
    }

    /// <summary>該位址進入銷毀（上鎖）或重新開始（解除）生命週期。</summary>
    /// <remarks>
    /// 🔴 <b>按位址比對，不是按名稱清空</b>：同名的兩扇窗可以並存（第一扇正在關閉、第二扇剛建好）。
    /// 🔴 <see cref="AddonEvent.PostSetup"/> 只清「不是這一幀才登記的」紀錄：模組在 <c>PostSetup</c> 處理常式裡按下、
    /// 而守衛排在它後面時，不設這條豁免就會把剛登記的位址清掉。
    /// <see cref="AddonEvent.PreFinalize"/> 把位址鎖到它從清單消失：事件內回頭按同一扇窗的路徑也擋得住。
    /// </remarks>
    private static void OnAddonLifecycle(AddonEvent type, AddonArgs args)
    {
        var address = args.Addon.Address;
        if (address == nint.Zero) return;

        if (type == AddonEvent.PreFinalize)
        {
            LockFinalizing(address, args);
            return;
        }

        if (!Pressed.TryGetValue(address, out var records)) return;

        records.Finalizing = false;
        StaleKeysBuf.Clear();
        foreach (var (paramKey, record) in records.ByParam)
        {
            if (record.Frame == frameCount) continue;
            StaleKeysBuf.Add(paramKey);
        }

        foreach (var paramKey in StaleKeysBuf) records.ByParam.Remove(paramKey);
        StaleKeysBuf.Clear();
        if (records.ByParam.Count == 0) Pressed.Remove(address);
    }

    private static void LockFinalizing(nint address, AddonArgs args)
    {
        if (Pressed.TryGetValue(address, out var records))
        {
            records.Finalizing = true;
            return;
        }

        if (GuardedNames.Count == 0) return;

        // 鎖不受容量上限限制：上鎖只會讓這扇窗多擋幾幀，消失輪詢會收掉它。
        var name = args.AddonName;
        if (!GuardedNames.Contains(name)) return;

        Pressed[address] = new AddressRecords(name) { Listed = true, Finalizing = true, LastFrame = frameCount };
    }
}
