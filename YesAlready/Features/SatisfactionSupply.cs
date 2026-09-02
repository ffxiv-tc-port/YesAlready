using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using ECommons.UIHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;

namespace YesAlready.Features;

[AddonFeature(AddonEvent.PostUpdate)]
internal class SatisfactionSupply : AddonFeature
{
    protected override bool IsEnabled() => C.CustomDeliveries;

    private static bool Disabled;
    private static List<int> SlotsFilled { get; set; } = [];
    private static ulong RequestAllow;

    protected override unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs addonInfo, AtkUnitBase* atk)
    {
        if (Disabled || !GenericHelpers.IsAddonReady(atk)) return;

        // AgentSatisfactionSupply.Instance() 走 CS 的 [Agent] 產生器(agentModule == null ? null : ...),
        // 也就是說它是合法會回 null 的。這支是 SatisfactionSupply 的 addon 事件回呼,實務上代理人
        // 幾乎一定活著 —— 但「幾乎一定」不是守衛,而解參考 null 是 corrupted-state 的
        // AccessViolation,try/catch 完全攔不到。
        // 取一次本地指標、判空後在同一次回呼內重用(原本一輪迴圈裡裸呼叫兩次),不跨幀保存。
        var agent = AgentSatisfactionSupply.Instance();
        if (agent == null)
        {
            // fail-closed:這一次回呼不交任何東西。addon 還開著的話下次 PostUpdate 會再進來重試。
            return;
        }

        var reader = new ReaderSatisfactionSupply(atk);

        foreach (var (value, index) in reader.Quantities.WithIndex())
        {
            if (value != 0 && !GenericHelpers.TryGetAddonByName<AtkUnitBase>("Request", out var _))
            {
                if (reader.WillItemOvercap(agent->Items[index], Log))
                {
                    Svc.Chat.PrintPluginMessage("Further turn in will overcap scrips.");
                    Disabled = true;
                    return;
                }
                // 送出交付事件之後 SatisfactionSupply 本身不會關（它開出 Request 子視窗），
                // 所以歸「多次互動窗」：守衛擋掉的是「Request 還沒出現就每一幀重送」，
                // 逃生口 15 幀之後照樣補送。粒度含 index —— 同一幀對不同格各送一次是正常流程。
                // 真正的危險形狀是使用者自己把 SatisfactionSupply 關掉、還有剩餘數量而 Request 不在時，
                // 關閉中的那幾幀 PostUpdate 仍會進來。
                if (!AddonPressGuard.TryBeginRoutinePress(addonInfo.AddonName, atk, $"turnin:{index}")) continue;

                Log($"Turning in item #{agent->Items[index].Id}");
                Callback.Fire(atk, false, 1, index);
            }
        }
    }

    public override void Enable()
    {
        base.Enable();
        Svc.Framework.Update += RequestFill;
        Svc.Framework.Update += RequestComplete;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreSetup, "SatisfactionSupply", Reset);
    }

    public override void Disable()
    {
        base.Disable();
        Svc.Framework.Update -= RequestFill;
        Svc.Framework.Update -= RequestComplete;
        Svc.AddonLifecycle.UnregisterListener(Reset);
    }

    private void Reset(AddonEvent type, AddonArgs args) => Disabled = false;

    private static unsafe void RequestFill(IFramework framework)
    {
        if (!P.Active || !C.CustomDeliveries || !GenericHelpers.TryGetAddonByName<AddonRequest>("SatisfactionSupply", out var _))
            return;

        if (GenericHelpers.TryGetAddonByName<AddonRequest>("Request", out var addon) && GenericHelpers.IsAddonReady((AtkUnitBase*)addon))
        {
            for (var i = 1; i <= addon->EntryCount; i++)
            {
                if (SlotsFilled.Contains(addon->EntryCount))
                {
                    Service.TaskManager.Abort();
                    return;
                }
                if (SlotsFilled.Contains(i)) return;
                var val = i;
                Service.TaskManager.Enqueue(() => TryClickItem(addon, val));
            }
        }
        else
        {
            SlotsFilled.Clear();
            Service.TaskManager.Abort();
        }
    }

    private static unsafe bool? TryClickItem(AddonRequest* addon, int i)
    {
        if (SlotsFilled.Contains(i)) return true;

        var contextMenu = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextIconMenu", 1).Address;

        if (contextMenu is null || !contextMenu->IsVisible)
        {
            var slot = i - 1;
            var unk = 44 * i + (i - 1);

            // 這一發是「在 Request 上開出該格的選單」，Request 不會因此關閉 ⇒ 多次互動窗。
            // 擋掉的是「選單一直沒開起來就每個 tick 重送」；回傳值與改動前一樣是 false
            //（＝這一輪沒做完、下個 tick 再來），呼叫端控制流完全沒變。
            if (AddonPressGuard.TryBeginRoutinePress("Request", &addon->AtkUnitBase, $"slot:{slot}"))
                Callback.Fire(&addon->AtkUnitBase, false, 2, slot, 0, 0);

            return false;
        }
        else
        {
            // 🔴 選單按下即關。NeoTaskManager 一個 framework tick 只跑一次 CurrentTask，
            // 下一個 tick 換下一格的 TryClickItem 進來時選單正在關閉中 ——
            // GetAddonByName 仍回實例、IsVisible 仍為真，於是走到這裡對關閉中的選單再送一發＝AVE。
            // 被擋下時回 false（＝這一輪沒按到、下個 tick 再來），不是 null（null 會清掉整條佇列），
            // 而且順帶不再把這一格誤記成「已填」。
            if (!AddonPressGuard.TryBeginPress("ContextIconMenu", contextMenu)) return false;

            Callback.Fire(contextMenu, false, 0, 0, 1021003, 0, 0);
            PluginLog.Debug($"Filled slot {i}");
            SlotsFilled.Add(i);
            return true;
        }
    }

    private static unsafe void RequestComplete(IFramework framework)
    {
        if (!P.Active || !C.CustomDeliveries || !GenericHelpers.TryGetAddonByName<AddonRequest>("SatisfactionSupply", out var _))
            return;

        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Request", out var addon) && GenericHelpers.IsAddonReady(addon))
        {
            if (RequestAllow == 0)
                RequestAllow = Svc.PluginInterface.UiBuilder.FrameCount + 4;

            if (Svc.PluginInterface.UiBuilder.FrameCount < RequestAllow) return;
            var m = new AddonMaster.Request(addon);
            if (m.IsHandOverEnabled && m.IsFilled)
            {
                // 🔴 交出之後 Request 就關掉了。EzThrottler 記的是「上一次動作在哪個時刻」而不是
                // 「這扇窗按過了」，低 FPS 時 500 毫秒可能還落在關閉中的那幾幀裡；按鈕啟用檢查同樣不算防護
                //（m.IsFilled 在 vendored ECommons 裡迴圈永不執行、恆回 true，更是等於沒檢查）。
                // 節流先判、守衛後判：守衛一回 true 就已經登記，登記完卻因為節流沒按會白白封鎖到逃生口。
                if (EzThrottler.Throttle("Handin") && AddonPressGuard.TryBeginPress("Request", addon, "handover"))
                {
                    PluginLog.Debug("Handing over request");
                    m.HandOver();
                }
            }
        }
        else
            RequestAllow = 0;
    }
}

public unsafe class ReaderSatisfactionSupply(AtkUnitBase* UnitBase, int BeginOffset = 0) : AtkReader(UnitBase, BeginOffset)
{
    public List<int> Quantities => [DoHQuantity, MinBotQuantity, FshQuantity];
    public int DoHQuantity => ReadInt(22) ?? 0;
    public int MinBotQuantity => ReadInt(31) ?? 0;
    public int FshQuantity => ReadInt(40) ?? 0;

    // 下面五個成員目前在本 repo 沒有任何呼叫端(保留不刪),但它們都是裸解參考
    // AgentSatisfactionSupply.Instance() —— 該取得器合法會回 null,一旦有人開始用就是
    // 攔不到的 AccessViolation。先把守衛補上,退化值取各自真正中性的那個:
    // ItemInfo 回 default(Id 為 0,呼叫端本來就得當成「沒有這個項目」),Span 回空跨度。
    public AgentSatisfactionSupply.ItemInfo DoHItem => GetItemInfo(0);
    public AgentSatisfactionSupply.ItemInfo MinBotItem => GetItemInfo(1);
    public AgentSatisfactionSupply.ItemInfo FshItem => GetItemInfo(2);

    private static AgentSatisfactionSupply.ItemInfo GetItemInfo(int index)
    {
        var agent = AgentSatisfactionSupply.Instance();
        if (agent == null) return default;
        return agent->Items[index];
    }

    public Span<uint> CraftScripIds
    {
        get
        {
            var agent = AgentSatisfactionSupply.Instance();
            if (agent == null) return default;
            return agent->CrafterScripIds;
        }
    }

    public Span<uint> GatherScripIds
    {
        get
        {
            var agent = AgentSatisfactionSupply.Instance();
            if (agent == null) return default;
            return agent->GathererScripIds;
        }
    }

    public bool WillItemOvercap(AgentSatisfactionSupply.ItemInfo item, Action<string> log)
    {
        // CurrencyManager.Instance() 在 CS 裡是 [StaticAddress(..., isPointer: true)] —— 讀的是「指標的位址」,
        // 遊戲還沒把那個管理器配起來時真的會回 null,解參考就是攔不到的 AVE。
        // 這一支原本在同一個方法裡裸呼叫十二次(六行、每行兩次),改成取一次本地指標、判空後重用。
        var currency = CurrencyManager.Instance();
        if (currency == null)
            throw new Exception("CurrencyManager unavailable; cannot tell whether the reward would overcap");
        if (GetItem(item.Id) is { SpiritbondOrCollectability: var collectability })
        {
            log($"Checking overcap for item #{item.Id} with collectability {collectability}");
            if (collectability > item.Collectability3)
            {
                log($"Item #{item.Id} [{item.Reward1Quantity[2]} > {currency->GetItemCountRemaining(item.Reward1Id)} || {item.Reward2Quantity[2]} > {currency->GetItemCountRemaining(item.Reward2Id)}]");
                return currency->GetItemCountRemaining(item.Reward1Id) < item.Reward1Quantity[2] || currency->GetItemCountRemaining(item.Reward2Id) < item.Reward2Quantity[2];
            }
            if (collectability > item.Collectability2)
            {
                log($"Item #{item.Id} [{item.Reward1Quantity[1]} > {currency->GetItemCountRemaining(item.Reward1Id)} || {item.Reward2Quantity[1]} > {currency->GetItemCountRemaining(item.Reward2Id)}]");
                return currency->GetItemCountRemaining(item.Reward1Id) < item.Reward1Quantity[1] || currency->GetItemCountRemaining(item.Reward2Id) < item.Reward2Quantity[1];
            }
            if (collectability > item.Collectability1)
            {
                log($"Item #{item.Id} [{item.Reward1Quantity[0]} > {currency->GetItemCountRemaining(item.Reward1Id)} || {item.Reward2Quantity[0]} > {currency->GetItemCountRemaining(item.Reward2Id)}]");
                return currency->GetItemCountRemaining(item.Reward1Id) < item.Reward1Quantity[0] || currency->GetItemCountRemaining(item.Reward2Id) < item.Reward2Quantity[0];
            }
        }
        throw new Exception($"Failed to find item [{item.Id}] in inventory");
    }

    public List<CollectabilityReward> DoHRewards => Loop<CollectabilityReward>(59, 1, 6);
    public List<CollectabilityReward> MinBotRewards => Loop<CollectabilityReward>(87, 1, 6);
    public List<CollectabilityReward> FshRewards => Loop<CollectabilityReward>(115, 1, 6);
    public class CollectabilityReward(nint UnitBasePtr, int BeginOffset = 0) : AtkReader(UnitBasePtr, BeginOffset)
    {
        public uint Scrip1LowCollectability => ReadUInt(0) ?? 0;
        public uint Scrip1MedCollectability => ReadUInt(1) ?? 0;
        public uint Scrip1HighCollectability => ReadUInt(2) ?? 0;
        public uint Scrip2LowCollectability => ReadUInt(3) ?? 0;
        public uint Scrip2MedCollectability => ReadUInt(4) ?? 0;
        public uint Scrip2HighCollectability => ReadUInt(5) ?? 0;
    }

    private GameInventoryItem? GetItem(uint itemId)
    {
        IEnumerable<GameInventoryType> types = [GameInventoryType.Inventory1, GameInventoryType.Inventory2, GameInventoryType.Inventory3, GameInventoryType.Inventory4];
        foreach (var type in types)
        {
            var items = Svc.GameInventory.GetInventoryItems(type);
            foreach (var item in items)
                if (item.BaseItemId == itemId)
                    return item;
        }
        return null;
    }
}
