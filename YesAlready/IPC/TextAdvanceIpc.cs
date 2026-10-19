using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;

namespace YesAlready.IPC;

/// <summary>
/// 唯讀問 TextAdvance「這一類視窗它現在會不會自己按」，讓兩邊對同一扇窗只有一個主人。只問，不改它任何狀態。
/// </summary>
/// <remarks>
/// 契約：只在框架執行緒呼叫；端點名與型別逐字對應 TextAdvance 的 <c>IPCProvider</c>（無參數、回 <c>bool</c>），
/// 都是它上游就有的端點，新舊版都認得。每次呼叫都重問，不快取：被別人暫停或解除的下一幀就換主人。
/// </remarks>
internal static class TextAdvanceIpc
{
    internal enum Handling
    {
        /// <summary>沒載入、沒啟用、被暫停，或這一類開關關著 ⇒ 由 YesAlready 按。</summary>
        NotHandling,

        /// <summary>它會按 ⇒ YesAlready 讓位。</summary>
        Handling,

        /// <summary>它載入了卻問不出答案 ⇒ 確認不了就不按（標準 §1.7），YesAlready 也讓位。</summary>
        Unknown,
    }

    internal const string RequestFill = "TextAdvance.GetEnableRequestFill";
    internal const string RequestHandin = "TextAdvance.GetEnableRequestHandin";
    internal const string QuestComplete = "TextAdvance.GetEnableQuestComplete";

    private static ICallGateSubscriber<bool>? isEnabled;
    private static ICallGateSubscriber<bool>? isPaused;
    private static readonly Dictionary<string, ICallGateSubscriber<bool>> Flags = new(StringComparer.Ordinal);

    /// <summary>每個呼叫點上一次寫進 log 的答案；只在答案改變時寫一行。</summary>
    private static readonly Dictionary<string, Handling> LastLogged = new(StringComparer.Ordinal);

    /// <summary>TextAdvance 現在會不會處理 <paramref name="flagEndpoint"/> 那一類視窗。</summary>
    /// <param name="flagEndpoint">本類別的常數之一（TextAdvance 該類功能的開關端點）。</param>
    /// <param name="caller">寫 log 用的呼叫點名稱。</param>
    public static Handling Query(string flagEndpoint, string caller)
    {
        string detail;
        Handling result;
        try
        {
            isEnabled ??= Svc.PluginInterface.GetIpcSubscriber<bool>("TextAdvance.IsEnabled");
            isPaused ??= Svc.PluginInterface.GetIpcSubscriber<bool>("TextAdvance.IsPaused");
            if (!Flags.TryGetValue(flagEndpoint, out var flag))
                Flags[flagEndpoint] = flag = Svc.PluginInterface.GetIpcSubscriber<bool>(flagEndpoint);

            if (!isEnabled.HasFunction)
                (result, detail) = (Handling.NotHandling, "TextAdvance 沒有載入");
            else if (!isEnabled.InvokeFunc())
                (result, detail) = (Handling.NotHandling, "IsEnabled=False");
            // 被別的插件暫停時它這一幀不會按；暫停解除的那一幀這裡就改答 Handling。
            else if (isPaused.HasFunction && isPaused.InvokeFunc())
                (result, detail) = (Handling.NotHandling, "IsPaused=True");
            else if (!flag.HasFunction)
                (result, detail) = (Handling.Unknown, $"它已載入但沒有 {flagEndpoint}");
            else
            {
                var on = flag.InvokeFunc();
                (result, detail) = (on ? Handling.Handling : Handling.NotHandling, $"{flagEndpoint}={on}");
            }
        }
        catch (Exception ex)
        {
            (result, detail) = (Handling.Unknown, $"查詢失敗（{ex.GetType().Name}）：{ex.Message}");
        }

        if (!LastLogged.TryGetValue(caller, out var last) || last != result)
        {
            LastLogged[caller] = result;
            PluginLog.Information($"[{caller}] TextAdvance 協調：{result}（{detail}）⇒ {(result == Handling.NotHandling ? "由 YesAlready 處理" : "YesAlready 讓位、不按")}");
        }
        return result;
    }
}
