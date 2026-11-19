using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.LanguageHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using YesAlready.Features;
using YesAlready.UI.Tabs;
using Entry = YesAlready.UI.Tabs.Bothers.Entry;
using E = YesAlready.UI.Tabs.Bothers.E;

namespace YesAlready.UI;

// 本檔假設與契約：
// - 遊戲視窗指標只在同一次 Draw 內使用，每幀照名字重取，不跨幀保存。
// - 只畫 ImGui，不按任何遊戲視窗；點徽章只開 ImGui 彈出框。
// - 預設關（ShowBotherBadges=false）：關著時每幀只判一個布林；開著時每幀不配置。
public sealed class BotherBadges : IDisposable
{
    private const string IconResource = "YesAlready.Assets.yesalready_badge.png";
    private const string PopupId = "BotherBadgePopup";

    private const float BadgeSize = 28f;
    private const float TitleGap = 6f;
    private const float CornerInset = 8f;
    private const float OutlineWidth = 1.5f;

    private static readonly Vector4 DimmedTint = new(0.85f, 0.85f, 0.85f, 1f);
    private static readonly Vector4 OutlineColour = new(0f, 0f, 0f, 0.85f);
    private static readonly Vector4 HoveredOutlineColour = new(0f, 0f, 0f, 1f);
    private static readonly Vector2[] OutlineDirections = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1), new(-1, -1), new(1, -1), new(-1, 1), new(1, 1)];

    /// <summary>徽章彈出框裡的一個開關；<see cref="Visible"/> 為假時不畫（子選項，母選項關著）。</summary>
    private sealed record Toggle(Entry Entry, Func<bool> Get, Action<bool> Set, bool CountsAsEnabled = true, Func<bool>? Visible = null);

    private sealed class BadgedWindow(string addonName, Toggle[] toggles, string focusTerm)
    {
        public readonly string AddonName = addonName;
        public readonly string WindowId = "###YesAlreadyBadge_" + addonName;
        public readonly Toggle[] Toggles = toggles;
        public readonly string FocusTerm = focusTerm;

        public bool AnyEnabled
        {
            get
            {
                foreach (var t in Toggles)
                    if (t.CountsAsEnabled && t.Get())
                        return true;
                return false;
            }
        }
    }

    // 「其他選項」分頁裡掛在特定遊戲視窗上的項目；視窗名取自各功能類別的 AddonFeature 屬性。
    // 刻意不收：交易（不是 AddonFeature、設定彼此相依）、WKSAnnounceHide（功能本身停用中）。
    // FocusTerm＝「完整設定」按鈕帶去分頁搜尋框的字，要能同時命中該視窗的所有項目。
    private static readonly (Type Feature, string? FocusTerm, Toggle[] Toggles)[] Table =
    [
        (typeof(SalvageDialog), null, [
            new(E.SalvageDialog, () => C.DesynthDialogEnabled, v => C.DesynthDialogEnabled = v),
            new(E.DesynthBulk, () => C.DesynthBulkDialogEnabled, v => C.DesynthBulkDialogEnabled = v, false, () => C.DesynthDialogEnabled)]),
        (typeof(SalvageResult), null, [new(E.SalvageResults, () => C.DesynthesisResults, v => C.DesynthesisResults = v)]),
        (typeof(PurifyResult), "PurifyResult", [new(E.PurifyResult, () => C.AetherialReductionResults, v => C.AetherialReductionResults = v)]),
        (typeof(PurifyResultAutomatic), "PurifyResult", [new(E.PurifyAutomatic, () => C.AetherialReductionAutomatic, v => C.AetherialReductionAutomatic = v)]),
        (typeof(MateriaAttachDialog), null, [new(E.MateriaAttach, () => C.MaterialAttachDialogEnabled, v => C.MaterialAttachDialogEnabled = v)]),
        (typeof(MaterializeDialog), null, [new(E.Materialize, () => C.MaterializeDialogEnabled, v => C.MaterializeDialogEnabled = v)]),
        (typeof(MateriaRetrieveDialog), null, [new(E.MateriaRetrieve, () => C.MateriaRetrieveDialogEnabled, v => C.MateriaRetrieveDialogEnabled = v)]),
        (typeof(RetainerTaskAsk), null, [new(E.RetainerTaskAsk, () => C.RetainerTaskAskEnabled, v => C.RetainerTaskAskEnabled = v)]),
        (typeof(RetainerTaskResult), null, [new(E.RetainerTaskResult, () => C.RetainerTaskResultEnabled, v => C.RetainerTaskResultEnabled = v)]),
        (typeof(RetainerItemTransferList), null, [new(E.RetainerTransferList, () => C.RetainerTransferListConfirm, v => C.RetainerTransferListConfirm = v)]),
        (typeof(RetainerItemTransferProgress), null, [new(E.RetainerTransferProgress, () => C.RetainerTransferProgressConfirm, v => C.RetainerTransferProgressConfirm = v)]),
        (typeof(AirShipExplorationResult), "AirShipExplorationResult", [
            new(E.AirShipFinalize, () => C.AirShipExplorationResultFinalize, Bothers.SetAirShipFinalize),
            new(E.AirShipRedeploy, () => C.AirShipExplorationResultRedeploy, Bothers.SetAirShipRedeploy)]),
        (typeof(ContentsFinderConfirm), "ContentsFinder", [
            new(E.ContentsFinderConfirm, () => C.ContentsFinderConfirmEnabled, Bothers.SetContentsFinderConfirm),
            new(E.ContentsFinderOneTime, () => C.ContentsFinderOneTimeConfirmEnabled, Bothers.SetContentsFinderOneTime)]),
        (typeof(MKSRecord), null, [new(E.MKSRecord, () => C.MKSRecordQuit, v => C.MKSRecordQuit = v)]),
        (typeof(FrontlineRecord), null, [new(E.FrontlineRecord, () => C.FrontlineRecordQuit, v => C.FrontlineRecordQuit = v)]),
        (typeof(LotteryWeeklyInput), null, [new(E.LotteryWeekly, () => C.LotteryWeeklyInput, v => C.LotteryWeeklyInput = v)]),
        (typeof(HWDLottery), null, [new(E.HWDLottery, () => C.KupoOfFortune, v => C.KupoOfFortune = v)]),
        (typeof(LovmResult), null, [new(E.LovmResult, () => C.LordOfVerminionQuit, v => C.LordOfVerminionQuit = v)]),
        (typeof(FGSEnterDialog), null, [new(E.FGSEnter, () => C.FallGuysRegisterConfirm, v => C.FallGuysRegisterConfirm = v)]),
        (typeof(FGSExitDialog), null, [new(E.FGSExit, () => C.FallGuysExitConfirm, v => C.FallGuysExitConfirm = v)]),
        (typeof(FashionCheck), null, [new(E.FashionCheck, () => C.FashionCheckQuit, v => C.FashionCheckQuit = v)]),
        (typeof(RaceChocoboResult), null, [new(E.RaceChocobo, () => C.ChocoboRacingQuit, v => C.ChocoboRacingQuit = v)]),
        (typeof(ShopCardDialog), null, [new(E.ShopCard, () => C.ShopCardDialog, v => C.ShopCardDialog = v)]),
        (typeof(InclusionShop), null, [new(E.InclusionShopRemember, () => C.InclusionShopRememberEnabled, v => C.InclusionShopRememberEnabled = v)]),
        (typeof(ShopExchangeItemDialog), null, [new(E.ShopExchangeItem, () => C.ShopExchangeItemDialogEnabled, v => C.ShopExchangeItemDialogEnabled = v)]),
        (typeof(SatisfactionSupply), null, [new(E.SatisfactionSupply, () => C.CustomDeliveries, v => C.CustomDeliveries = v)]),
        (typeof(GrandCompanySupplyReward), null, [new(E.GCSupplyReward, () => C.GrandCompanySupplyReward, v => C.GrandCompanySupplyReward = v)]),
        (typeof(JournalResult), null, [new(E.JournalResultComplete, () => C.JournalResultCompleteEnabled, v => C.JournalResultCompleteEnabled = v)]),
        (typeof(GuildLeveDifficulty), null, [new(E.GuildLeveDifficulty, () => C.GuildLeveDifficultyConfirm, v => C.GuildLeveDifficultyConfirm = v)]),
        (typeof(LobbyDKTCheck), null, [new(E.DataCentreTravel, () => C.DataCentreTravelConfirmEnabled, v => C.DataCentreTravelConfirmEnabled = v)]),
        (typeof(MiragePrismRemove), null, [new(E.MiragePrismRemoveDispel, () => C.MiragePrismRemoveDispel, v => C.MiragePrismRemoveDispel = v)]),
        (typeof(MiragePrismExecute), null, [new(E.MiragePrismExecuteCast, () => C.MiragePrismExecuteCast, v => C.MiragePrismExecuteCast = v)]),
        (typeof(BannerPreview), null, [new(E.BannerPreviewUpdate, () => C.BannerPreviewUpdate, v => C.BannerPreviewUpdate = v)]),
        (typeof(ItemInspectionResult), null, [new(E.ItemInspection, () => C.ItemInspectionResultEnabled, v => C.ItemInspectionResultEnabled = v)]),
        (typeof(WKSReward), null, [new(E.WKSRewardHide, () => C.WKSRewardClose, v => C.WKSRewardClose = v)]),
    ];

    private static BadgedWindow[]? windows;
    private static BadgedWindow? popupOwner;
    private ISharedImmediateTexture? icon;

    public BotherBadges() => Svc.PluginInterface.UiBuilder.Draw += Draw;

    public void Dispose() => Svc.PluginInterface.UiBuilder.Draw -= Draw;

    private static BadgedWindow[] BuildWindows()
    {
        var byAddon = new Dictionary<string, (string Term, List<Toggle> Toggles)>(StringComparer.Ordinal);
        foreach (var (feature, focusTerm, toggles) in Table)
        {
            var names = feature.GetCustomAttributes<AddonFeatureAttribute>(true).Select(a => a.AddonName ?? feature.Name).Distinct();
            foreach (var name in names)
            {
                if (!byAddon.TryGetValue(name, out var slot))
                    byAddon[name] = slot = (focusTerm ?? toggles[0].Entry.Label, []);
                slot.Toggles.AddRange(toggles);
            }
        }
        return [.. byAddon.Select(kv => new BadgedWindow(kv.Key, [.. kv.Value.Toggles], kv.Value.Term))];
    }

    // 每幀會呼叫的遊戲函式位址；任一沒解到就不呼叫（找視窗的沒解到＝整個不畫，量標題的沒解到＝放左上角）。
    // 位址在插件載入前就解好、執行期不變 ⇒ 只算一次。UIModule.GetRaptureAtkModule 是虛擬函式、沒有位址可判。
    private static bool? canFindAddons;
    private static bool? canMeasureTitle;

    private static bool CanFindAddons => canFindAddons ??= CheckNative("找遊戲視窗", "徽章整個不畫",
        ("Framework.Instance", FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Addresses.Instance.Value),
        ("Framework.GetUIModule", FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Addresses.GetUIModule.Value),
        ("AtkUnitManager.GetAddonByName", AtkUnitManager.Addresses.GetAddonByName.Value));

    private static bool CanMeasureTitle => canMeasureTitle ??= CheckNative("量標題文字", "徽章改放視窗左上角",
        ("AtkUldManager.SearchNodeById", AtkUldManager.Addresses.SearchNodeById.Value),
        ("AtkResNode.IsVisible", AtkResNode.Addresses.IsVisible.Value),
        ("AtkTextNode.GetTextDrawSize", AtkTextNode.Addresses.GetTextDrawSize.Value));

    private static bool CheckNative(string purpose, string fallback, params (string Name, nint Address)[] members)
    {
        var missing = members.Where(m => m.Address == 0).Select(m => m.Name).ToArray();
        if (missing.Length == 0) return true;
        PluginLog.Debug($"[BotherBadges] {purpose}需要的遊戲函式沒解到位址（{string.Join("、", missing)}），{fallback}。");
        return false;
    }

    private unsafe void Draw()
    {
        if (!C.ShowBotherBadges || !CanFindAddons) return;

        windows ??= BuildWindows();
        foreach (var window in windows)
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(window.AddonName, 1).Address;
            if (addon == null || !addon->IsVisible || !addon->IsReady || addon->RootNode == null)
                continue;

            if (C.HideBadgeWhenBotherEnabled && window.AnyEnabled && !ReferenceEquals(window, popupOwner))
                continue;

            // InvisibleButton 不接受零尺寸，縮放值異常就不畫。
            var scale = addon->Scale;
            if (!(scale > 0.05f && scale < 20f))
                continue;

            var size = new Vector2(BadgeSize * scale);
            DrawBadge(window, GetBadgePosition(addon, size), size, scale);
        }
    }

    private void DrawBadge(BadgedWindow window, Vector2 pos, Vector2 size, float scale)
    {
        var open = BeginInvisibleWindow(window.WindowId, pos);
        try
        {
            if (!open) return;

            var clicked = ImGui.InvisibleButton("##badge", size);
            var hovered = ImGui.IsItemHovered();
            var active = P.Active && window.AnyEnabled;
            DrawIcon(pos, size, scale, dimmed: !hovered && !active, strongOutline: hovered);

            if (hovered && !ImGui.IsPopupOpen(PopupId))
                DrawTooltip(window);

            if (clicked)
                ImGui.OpenPopup(PopupId);

            DrawPopup(window);
        }
        finally
        {
            ImGui.End();
        }
    }

    private static bool BeginInvisibleWindow(string id, Vector2 pos)
    {
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(pos);

        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero)
            .Push(ImGuiStyleVar.WindowMinSize, Vector2.One)
            .Push(ImGuiStyleVar.WindowBorderSize, 0f);

        return ImGui.Begin(id,
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking);
    }

    private void DrawIcon(Vector2 pos, Vector2 size, float scale, bool dimmed, bool strongOutline)
    {
        icon ??= Svc.Texture.GetFromManifestResource(Assembly.GetExecutingAssembly(), IconResource);
        if (!icon.TryGetWrap(out var wrap, out _))
            return;

        var drawList = ImGui.GetWindowDrawList();
        var outlineWidth = MathF.Max(1f, OutlineWidth * scale);
        var outlineColour = ImGui.GetColorU32(strongOutline ? HoveredOutlineColour : OutlineColour);

        foreach (var direction in OutlineDirections)
        {
            var nudge = direction * outlineWidth;
            drawList.AddImage(wrap.Handle, pos + nudge, pos + size + nudge, Vector2.Zero, Vector2.One, outlineColour);
        }

        drawList.AddImage(wrap.Handle, pos, pos + size, Vector2.Zero, Vector2.One, ImGui.GetColorU32(dimmed ? DimmedTint : Vector4.One));
    }

    private static void DrawTooltip(BadgedWindow window)
    {
        using var tooltip = ImRaii.Tooltip();
        using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 22);

        ImGui.TextUnformatted("?? can automate this window".Loc(Name));
        if (!P.Active)
            ImGui.TextColored(ImGuiColors.DalamudRed, "Plugin is currently off or paused".Loc());

        foreach (var t in window.Toggles)
        {
            if (!t.CountsAsEnabled) continue;
            var on = t.Get();
            var colour = on ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey;
            ImGui.TextColored(colour, on ? "On".Loc() : "Off".Loc());
            ImGui.SameLine();
            ImGui.TextColored(colour, t.Entry.Label.Loc());
        }

        ImGui.TextDisabled("Click to configure".Loc());
    }

    private static void DrawPopup(BadgedWindow window)
    {
        using var popup = ImRaii.Popup(PopupId);
        if (!popup)
        {
            if (ReferenceEquals(popupOwner, window))
                popupOwner = null;
            return;
        }

        popupOwner = window;
        using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 22);

        ImGui.TextUnformatted(Name);
        ImGui.Separator();
        for (var i = 0; i < window.Toggles.Length; i++)
        {
            var t = window.Toggles[i];
            if (t.Visible is { } visible && !visible())
                continue;

            using var id = ImRaii.PushId(i);
            using var indent = t.Visible != null ? ImRaii.PushIndent() : null;
            var value = t.Get();
            if (ImGui.Checkbox(t.Entry.Label.Loc(), ref value))
            {
                t.Set(value);
                C.Save();
            }
            ImGuiX.IndentedTextColored(t.Entry.Desc.Loc());
        }

        ImGui.Separator();
        if (ImGui.Button("Open full settings".Loc()))
        {
            Bothers.Focus(window.FocusTerm);
            ImGui.CloseCurrentPopup();
        }
    }

    private static unsafe Vector2 GetBadgePosition(AtkUnitBase* addon, Vector2 size)
    {
        var scale = addon->Scale;
        var gamePos = TryGetTitleEnd(addon, out var titleEnd)
            ? new Vector2(titleEnd.X + TitleGap * scale, titleEnd.Y - size.Y / 2f)
            : new Vector2(addon->X, addon->Y) + new Vector2(CornerInset * scale);

        return ImGuiHelpers.MainViewport.Pos + gamePos;
    }

    /// <summary>標題（或副標題）文字的右端、垂直置中點；視窗沒有標題列時回 <see langword="false"/>。</summary>
    private static unsafe bool TryGetTitleEnd(AtkUnitBase* addon, out Vector2 titleEnd)
    {
        titleEnd = default;
        if (!CanMeasureTitle || addon->WindowNode == null || addon->WindowNode->Component == null)
            return false;

        var window = (AtkComponentWindow*)addon->WindowNode->Component;
        var found = false;

        for (var i = 0; i < 2; i++) // NodeIds[0]＝標題、[1]＝副標題
        {
            var node = window->UldManager.SearchNodeById((uint)window->NodeIds[i]);
            if (node == null || node->Type != NodeType.Text || !node->IsVisible())
                continue;

            ushort width, height;
            ((AtkTextNode*)node)->GetTextDrawSize(&width, &height);
            if (width == 0)
                continue;

            var end = new Vector2(node->ScreenX + width * addon->Scale, node->ScreenY + height * addon->Scale / 2f);
            if (!found || end.X > titleEnd.X)
            {
                titleEnd = end;
                found = true;
            }
        }

        return found;
    }

    public void DrawSettings()
    {
        var show = C.ShowBotherBadges;
        if (ImGui.Checkbox("Window badges".Loc(), ref show))
        {
            C.ShowBotherBadges = show;
            C.Save();
        }

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(ImGui.GetFrameHeight()));
        DrawIcon(ImGui.GetItemRectMin(), ImGui.GetItemRectSize(), ImGuiHelpers.GlobalScale, dimmed: !C.ShowBotherBadges, strongOutline: false);

        ImGuiX.IndentedTextColored("Show a ?? badge next to the title of game windows that the ?? tab can automate. Hover it to see which options are on, click it to turn them on or off. Off by default.".Loc(Name, "Bothers".Loc()));

        if (!C.ShowBotherBadges)
            return;

        using var indent = ImRaii.PushIndent();
        var hideWhenEnabled = C.HideBadgeWhenBotherEnabled;
        if (ImGui.Checkbox("Hide when already enabled".Loc(), ref hideWhenEnabled))
        {
            C.HideBadgeWhenBotherEnabled = hideWhenEnabled;
            C.Save();
        }
        ImGuiX.IndentedTextColored("Only show the badge on windows where none of its options are turned on.".Loc());
    }
}
