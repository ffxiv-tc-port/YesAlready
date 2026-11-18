using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using ECommons.LanguageHelpers;
using Dalamud.Bindings.ImGui;
using System;
using System.Linq;
using System.Text;

namespace YesAlready.UI.Tabs;
public static class Bothers
{
    private static readonly string[] hotkeyChoices =
    [
        "None",
        "Control",
        "Alt",
        "Shift",
    ];

    private static readonly VirtualKey[] hotkeyValues =
    [
        VirtualKey.NO_KEY,
        VirtualKey.CONTROL,
        VirtualKey.MENU,
        VirtualKey.SHIFT,
    ];

    #region Search

    /// <summary>搜尋比對用的英文原文（同時是 loc 鍵）；比對時英文與繁中都看。</summary>
    private readonly record struct Entry(string Label, string Desc);

    private static class E
    {
        public static readonly Entry DisableHotkey = new("Disable Hotkey", "While this key is held, the plugin is disabled.");
        public static readonly Entry ForcedYesHotkey = new("Forced Yes Hotkey", "2. While this key is held, any Yes/No prompt will always default to yes, and all talk dialogue will be skipped. Be careful.");
        public static readonly Entry SeparateForcedKeys = new("Separate Yes/Talk", "");
        public static readonly Entry ForcedTalkHotkey = new("Forced Talk Hotkey", "");

        public static readonly Entry SalvageDialog = new("SalvageDialog", "Remove the Desynthesis menu confirmation.");
        public static readonly Entry DesynthBulk = new("Desynthesize until none are left", "Before confirming, also tick the game's own checkbox to keep desynthesizing until none of the item are left. The game only offers it when you hold more than one.");
        public static readonly Entry SalvageResults = new("SalvageResults", "Automatically closes the SalvageResults window when done desynthesizing.");
        public static readonly Entry PurifyResult = new("PurifyResult", "Automatically closes the PurifyResult window when done reducing.");
        public static readonly Entry PurifyAutomatic = new("PurifyResult (Automatic)", "Press the game's own Automatic button on the PurifyResult window, so the game reduces every remaining item by itself. You still reduce the first item yourself.");

        public static readonly Entry MateriaAttach = new("MateriaAttachDialog", "Remove the materia melding confirmation menu.");
        public static readonly Entry Materialize = new("MaterializeDialog", "Remove the create new (extract) materia confirmation.");
        public static readonly Entry MateriaRetrieve = new("MateriaRetrieveDialog", "Remove the retrieve materia confirmation.");

        public static readonly Entry RetainerTaskAsk = new("RetainerTaskAsk", "Skip the confirmation in the final dialog before sending out a retainer.");
        public static readonly Entry RetainerTaskResult = new("RetainerTaskResult", "Automatically send a retainer on the same venture as before when receiving an item.");
        public static readonly Entry RetainerTransferList = new("RetainerItemTransferList", "Skip the confirmation in the RetainerItemTransferList window to entrust all items to the retainer.");
        public static readonly Entry RetainerTransferProgress = new("RetainerItemTransferProgress", "Automatically closes the RetainerItemTransferProgress window when finished entrusting items.");
        public static readonly Entry AirShipFinalize = new("AirShipExplorationResult - Finalize", "Automatically finalize submersible reports when the AirShipExplorationResult window opens.");
        public static readonly Entry AirShipRedeploy = new("AirShipExplorationResult - Redeploy", "Automatically redeploy submersibles when the AirShipExplorationResult window opens.");

        public static readonly Entry ContentsFinderConfirm = new("ContentsFinderConfirm", "Automatically commence duties when ready.");
        public static readonly Entry ContentsFinderOneTime = new("ContentsFinderOneTimeConfirm", "Automatically commence duties when ready, but only once. Requires Contents Finder Confirm, and disables both after activation.");

        public static readonly Entry MKSRecord = new("MKSRecord", "Automatically leave the Crystalline Conflict match when the results appear.");
        public static readonly Entry FrontlineRecord = new("FrontlineRecord", "Automatically leave the Frontline match when the results appear.");

        public static readonly Entry LotteryWeekly = new("LotteryWeeklyInput", "Automatically purchase a Jumbo Cactpot ticket when the number entry window opens.");
        public static readonly Entry HWDLottery = new("HWDLottery", "Automatically select a kupo of fortune reward. This will instantly complete a single kupo ticket but is unable to continue to the next automatically.");
        public static readonly Entry LovmResult = new("LovmResult", "Automatically quit Lord of Verminion when the results menu appears.");
        public static readonly Entry FGSEnter = new("FGSEnterDialog", "Automatically register for Blunderville when speaking with the Blunderville Registrar.");
        public static readonly Entry FGSExit = new("FGSExitDialog", "Automatically confirm the exit prompt when leaving Blunderville.");
        public static readonly Entry FashionCheck = new("FashionCheck", "Automatically confirm the Fashion Reports results.");
        public static readonly Entry RaceChocobo = new("RaceChocoboResult", "Automatically quit Chocobo Racing when the results menu appears.");
        public static readonly Entry ShopCard = new("ShopCardDialog", "Automatically confirm selling Triple Triad cards in the saucer.");

        public static readonly Entry InclusionShopRemember = new("InclusionShopRemember", "Remember the last panel visited on the scrip exchange window.");
        public static readonly Entry ShopExchangeItem = new("ShopExchangeItemDialog", "Automatically exchange items/currencies in various shops (e.g., scrip vendors).");

        public static readonly Entry SatisfactionSupply = new("SatisfactionSupply", "Automatically turn in any available collectibles for Custom Deliveries.");
        public static readonly Entry GCSupplyReward = new("GrandCompanySupplyReward", "Skip the confirmation when submitting Grand Company expert delivery items.");
        public static readonly Entry JournalResultComplete = new("JournalResultComplete", "Automatically confirm quest reward acceptance when there is nothing to choose.");
        public static readonly Entry GuildLeveDifficulty = new("GuildLeveDifficulty", "Automatically confirms guild leves upon initiation at the highest difficulty.");
        public static readonly Entry DataCentreTravel = new("DataCentreTravelConfirm", "Automatically accept the Data Center travel confirmation.");
        public static readonly Entry MiragePrismRemoveDispel = new("MiragePrismRemoveDispel", "Automatically dispel glamours when using Glamour Dispellers.");
        public static readonly Entry MiragePrismExecuteCast = new("MiragePrismExecuteCast", "Automatically cast glamours when using Glamour Prisms.");
        public static readonly Entry BannerPreviewUpdate = new("BannerPreviewUpdate", "Automatically update portraits.");

        public static readonly Entry TradeAutoConfirm = new("Auto-offer and confirm after the trade partner offers", "Step 1: when the other player has pressed Offer and the trade contents have not changed for the delay set below, press Offer for you (at most once per trade). If the contents change after the other player offered, you press it yourself; while you are typing a quantity or have a menu open, it waits. Step 2: when the other player has confirmed, press OK on the trade confirmation for you. Only acts while timed auto-confirm is running, or the whitelist is on and the partner is on it. While this is on, every trade state change is written to the log (search for [TradeAutoConfirm]).");
        public static readonly Entry TradeTimed = new("Timed auto-confirm", "Any partner is accepted while it runs. Never saved: reloading the plugin, logging in or logging out always turns it off.");
        public static readonly Entry TradeWhitelist = new("Trade partner whitelist", "Only partners on this list (name + home world) are auto-confirmed. If the partner cannot be verified, nothing is pressed.");

        public static readonly Entry ItemInspection = new("ItemInspectionResult", "Eureka/Bozja lockboxes, forgotten fragments, and more. Warning: this does not check if you are maxed on items. Rate limiter (pause after N items).");
        public static readonly Entry ItemInspectionRate = new("", "Rate limiter (pause after N items, 0 to disable).");
        public static readonly Entry WKSAnnounceHide = new("WKSAnnounceHide", "Hide Cosmic Exploration announcements.");
        public static readonly Entry WKSRewardHide = new("WKSRewardHide", "Automatically close the Cosmic Exploration rewards window.");

        // 欄位初始化依文字順序執行 ⇒ 分類陣列必須寫在所有 Entry 之後。
        public static readonly Entry[] Hotkeys = [DisableHotkey, ForcedYesHotkey, SeparateForcedKeys, ForcedTalkHotkey];
        public static readonly Entry[] Desynth = [SalvageDialog, DesynthBulk, SalvageResults, PurifyResult, PurifyAutomatic];
        public static readonly Entry[] Melding = [MateriaAttach, Materialize, MateriaRetrieve];
        public static readonly Entry[] Retainers = [RetainerTaskAsk, RetainerTaskResult, RetainerTransferList, RetainerTransferProgress, AirShipFinalize, AirShipRedeploy];
        public static readonly Entry[] Duties = [ContentsFinderConfirm, ContentsFinderOneTime];
        public static readonly Entry[] PvP = [MKSRecord, FrontlineRecord];
        public static readonly Entry[] Minigames = [LotteryWeekly, HWDLottery, LovmResult, FGSEnter, FGSExit, FashionCheck, RaceChocobo, ShopCard];
        public static readonly Entry[] Shops = [InclusionShopRemember, ShopExchangeItem];
        public static readonly Entry[] Other = [SatisfactionSupply, GCSupplyReward, JournalResultComplete, GuildLeveDifficulty, DataCentreTravel, MiragePrismRemoveDispel, MiragePrismExecuteCast, BannerPreviewUpdate];
        public static readonly Entry[] Trade = [TradeAutoConfirm, TradeTimed, TradeWhitelist];
        public static readonly Entry[] Forays = [ItemInspection, ItemInspectionRate, WKSAnnounceHide, WKSRewardHide];
    }

    // 輸入框直接讀寫固定位元組緩衝，內容沒變的幀不產生新字串。
    private static readonly byte[] filterBuf = new byte[256];
    private static string filter = string.Empty;
    private static bool filterChanged;
    private static bool anyHeaderShown;

    private static bool Hit(string s) => s.Length > 0 && s.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static bool HitLoc(string s) => s.Length > 0 && (Hit(s) || Hit(s.Loc()));

    private static bool Hit(Entry e) => HitLoc(e.Label) || HitLoc(e.Desc);

    /// <summary>沒有搜尋字、或分類名稱本身符合時 <paramref name="all"/> 為真＝整個分類照常顯示。</summary>
    private static bool Show(bool all, Entry e) => all || Hit(e);

    private static bool Header(string title, Entry[] entries, out bool all)
    {
        all = filter.Length == 0 || HitLoc(title);
        if (!all)
        {
            var any = false;
            foreach (var e in entries)
                if (Hit(e)) { any = true; break; }
            if (!any) return false;
        }
        anyHeaderShown = true;
        if (filterChanged)
            ImGui.SetNextItemOpen(filter.Length > 0);
        return ImGui.CollapsingHeader(title.Loc());
    }

    private static void DrawFilterInput()
    {
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##BothersFilter", "Search...".Loc(), filterBuf))
        {
            var len = Array.IndexOf(filterBuf, (byte)0);
            filter = Encoding.UTF8.GetString(filterBuf, 0, len < 0 ? filterBuf.Length : len).Trim();
            filterChanged = true;
        }

        if (filter.Length == 0) return;
        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.Times, "Clear search".Loc()))
        {
            Array.Clear(filterBuf);
            filter = string.Empty;
            filterChanged = true;
        }
    }

    #endregion

    public static void Draw()
    {
        using var tab = ImRaii.TabItem("Bothers".Loc());
        if (!tab) return;
        using var idScope = ImRaii.PushId($"BothersOptions");

        DrawFilterInput();
        anyHeaderShown = false;
        bool all;

        #region Hotkey Settings

        if (Header("Hotkey Settings", E.Hotkeys, out _))
        {
            // 1. Disable Hotkey
            if (!hotkeyValues.Contains(C.DisableKey))
            {
                C.DisableKey = VirtualKey.NO_KEY;
                C.Save();
            }

            var disableHotkeyIndex = Array.IndexOf(hotkeyValues, C.DisableKey);

            ImGui.SetNextItemWidth(85);
            if (ImGui.Combo(E.DisableHotkey.Label.Loc(), ref disableHotkeyIndex, hotkeyChoices, hotkeyChoices.Length))
            {
                C.DisableKey = hotkeyValues[disableHotkeyIndex];
                C.Save();
            }

            ImGuiX.IndentedTextColored(E.DisableHotkey.Desc.Loc());

            // 2. Forced Yes Hotkey
            if (!hotkeyValues.Contains(C.ForcedYesKey))
            {
                C.ForcedYesKey = VirtualKey.NO_KEY;
                C.Save();
            }

            var forcedYesHotkeyIndex = Array.IndexOf(hotkeyValues, C.ForcedYesKey);

            ImGui.SetNextItemWidth(85);
            if (ImGui.Combo(E.ForcedYesHotkey.Label.Loc(), ref forcedYesHotkeyIndex, hotkeyChoices, hotkeyChoices.Length))
            {
                C.ForcedYesKey = hotkeyValues[forcedYesHotkeyIndex];
                C.Save();
            }

            ImGui.SameLine();
            var separateForcedKeys = C.SeparateForcedKeys;
            if (ImGui.Checkbox(E.SeparateForcedKeys.Label.Loc(), ref separateForcedKeys))
            {
                C.SeparateForcedKeys = separateForcedKeys;
                C.Save();
            }

            if (C.SeparateForcedKeys)
            {
                var forcedTalkHotkeyIndex = Array.IndexOf(hotkeyValues, C.ForcedTalkKey);
                ImGui.SetNextItemWidth(85);
                if (ImGui.Combo(E.ForcedTalkHotkey.Label.Loc(), ref forcedTalkHotkeyIndex, hotkeyChoices, hotkeyChoices.Length))
                {
                    C.ForcedTalkKey = hotkeyValues[forcedTalkHotkeyIndex];
                    C.Save();
                }
            }

            ImGuiX.IndentedTextColored(E.ForcedYesHotkey.Desc.Loc());
        }

        #endregion
        #region Desynthesis/AetherialReduction

        if (Header("Desynthesis and Aetherial Reduction", E.Desynth, out all))
        {
            if (Show(all, E.SalvageDialog) || Show(all, E.DesynthBulk))
            {
                // 3. SalvageDialog
                var desynthDialog = C.DesynthDialogEnabled;
                if (ImGui.Checkbox(E.SalvageDialog.Label.Loc(), ref desynthDialog))
                {
                    C.DesynthDialogEnabled = desynthDialog;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.SalvageDialog.Desc.Loc());

                // 4. SalvageDialog (Bulk)
                if (C.DesynthDialogEnabled)
                {
                    using var indent = ImRaii.PushIndent();
                    var desynthBulkDialog = C.DesynthBulkDialogEnabled;
                    if (ImGui.Checkbox(E.DesynthBulk.Label.Loc(), ref desynthBulkDialog))
                    {
                        C.DesynthBulkDialogEnabled = desynthBulkDialog;
                        C.Save();
                    }
                    ImGuiX.IndentedTextColored(E.DesynthBulk.Desc.Loc());
                }
            }

            // 5. SalvageResults
            if (Show(all, E.SalvageResults))
            {
                var desynthResultsDialog = C.DesynthesisResults;
                if (ImGui.Checkbox(E.SalvageResults.Label.Loc(), ref desynthResultsDialog))
                {
                    C.DesynthesisResults = desynthResultsDialog;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.SalvageResults.Desc.Loc());
            }

            if (Show(all, E.PurifyResult))
            {
                var purifyResultsDialog = C.AetherialReductionResults;
                if (ImGui.Checkbox(E.PurifyResult.Label.Loc(), ref purifyResultsDialog))
                {
                    C.AetherialReductionResults = purifyResultsDialog;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.PurifyResult.Desc.Loc());
            }

            if (Show(all, E.PurifyAutomatic))
            {
                var purifyAutomatic = C.AetherialReductionAutomatic;
                if (ImGui.Checkbox(E.PurifyAutomatic.Label.Loc(), ref purifyAutomatic))
                {
                    C.AetherialReductionAutomatic = purifyAutomatic;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.PurifyAutomatic.Desc.Loc());
            }
        }

        #endregion
        #region Melding

        if (Header("Melding", E.Melding, out all))
        {
            if (Show(all, E.MateriaAttach))
            {
                var meld = C.MaterialAttachDialogEnabled;
                if (ImGui.Checkbox(E.MateriaAttach.Label.Loc(), ref meld))
                {
                    C.MaterialAttachDialogEnabled = meld;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.MateriaAttach.Desc.Loc());
            }

            if (Show(all, E.Materialize))
            {
                var materialize = C.MaterializeDialogEnabled;
                if (ImGui.Checkbox(E.Materialize.Label.Loc(), ref materialize))
                {
                    C.MaterializeDialogEnabled = materialize;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.Materialize.Desc.Loc());
            }

            if (Show(all, E.MateriaRetrieve))
            {
                var materiaRetrieve = C.MateriaRetrieveDialogEnabled;
                if (ImGui.Checkbox(E.MateriaRetrieve.Label.Loc(), ref materiaRetrieve))
                {
                    C.MateriaRetrieveDialogEnabled = materiaRetrieve;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.MateriaRetrieve.Desc.Loc());
            }
        }

        #endregion
        #region Retainers & Submersibles

        if (Header("Retainers and Submersibles", E.Retainers, out all))
        {
            if (Show(all, E.RetainerTaskAsk))
            {
                var retainerTaskAsk = C.RetainerTaskAskEnabled;
                if (ImGui.Checkbox(E.RetainerTaskAsk.Label.Loc(), ref retainerTaskAsk))
                {
                    C.RetainerTaskAskEnabled = retainerTaskAsk;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.RetainerTaskAsk.Desc.Loc());
            }

            if (Show(all, E.RetainerTaskResult))
            {
                var retainerTaskResult = C.RetainerTaskResultEnabled;
                if (ImGui.Checkbox(E.RetainerTaskResult.Label.Loc(), ref retainerTaskResult))
                {
                    C.RetainerTaskResultEnabled = retainerTaskResult;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.RetainerTaskResult.Desc.Loc());
            }

            if (Show(all, E.RetainerTransferList))
            {
                var retainerListDialog = C.RetainerTransferListConfirm;
                if (ImGui.Checkbox(E.RetainerTransferList.Label.Loc(), ref retainerListDialog))
                {
                    C.RetainerTransferListConfirm = retainerListDialog;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.RetainerTransferList.Desc.Loc());
            }

            if (Show(all, E.RetainerTransferProgress))
            {
                var retainerProgressDialog = C.RetainerTransferProgressConfirm;
                if (ImGui.Checkbox(E.RetainerTransferProgress.Label.Loc(), ref retainerProgressDialog))
                {
                    C.RetainerTransferProgressConfirm = retainerProgressDialog;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.RetainerTransferProgress.Desc.Loc());
            }

            if (Show(all, E.AirShipFinalize))
            {
                var finalize = C.AirShipExplorationResultFinalize;
                if (ImGui.Checkbox(E.AirShipFinalize.Label.Loc(), ref finalize))
                {
                    if (finalize && C.AirShipExplorationResultRedeploy)
                        C.AirShipExplorationResultRedeploy = false;
                    C.AirShipExplorationResultFinalize = finalize;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.AirShipFinalize.Desc.Loc());
            }

            if (Show(all, E.AirShipRedeploy))
            {
                var redeploy = C.AirShipExplorationResultRedeploy;
                if (ImGui.Checkbox(E.AirShipRedeploy.Label.Loc(), ref redeploy))
                {
                    if (redeploy && C.AirShipExplorationResultFinalize)
                        C.AirShipExplorationResultFinalize = false;
                    C.AirShipExplorationResultRedeploy = redeploy;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.AirShipRedeploy.Desc.Loc());
            }
        }

        #endregion
        #region Duties

        if (Header("Duties", E.Duties, out all))
        {
            if (Show(all, E.ContentsFinderConfirm))
            {
                var contentsFinderConfirm = C.ContentsFinderConfirmEnabled;
                if (ImGui.Checkbox(E.ContentsFinderConfirm.Label.Loc(), ref contentsFinderConfirm))
                {
                    C.ContentsFinderConfirmEnabled = contentsFinderConfirm;

                    if (!contentsFinderConfirm)
                        C.ContentsFinderOneTimeConfirmEnabled = false;

                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.ContentsFinderConfirm.Desc.Loc());
            }

            if (Show(all, E.ContentsFinderOneTime))
            {
                var contentsFinderOneTimeConfirm = C.ContentsFinderOneTimeConfirmEnabled;
                if (ImGui.Checkbox(E.ContentsFinderOneTime.Label.Loc(), ref contentsFinderOneTimeConfirm))
                {
                    C.ContentsFinderOneTimeConfirmEnabled = contentsFinderOneTimeConfirm;

                    if (contentsFinderOneTimeConfirm)
                        C.ContentsFinderConfirmEnabled = true;

                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.ContentsFinderOneTime.Desc.Loc());
            }

            //var dutyDifficulty = C.DifficultySelectYesNoEnabled;
            //if (ImGui.Checkbox("SelectYesNoDifficulty", ref dutyDifficulty))
            //{
            //    C.DifficultySelectYesNoEnabled = dutyDifficulty;
            //    C.Save();
            //}

            //if (C.DifficultySelectYesNoEnabled)
            //{
            //    var difficulty = C.DifficultySelectYesNo;
            //    if (ImGuiEx.EnumCombo("SelectYesNoDifficulty", ref difficulty))
            //    {
            //        C.DifficultySelectYesNo = difficulty;
            //        C.Save();
            //    }
            //}
            //ImGuiX.IndentedTextColored("Automatically commence solo duties at the selected difficulty.");
        }

        #endregion
        #region PvP

        if (Header("PvP", E.PvP, out all))
        {
            if (Show(all, E.MKSRecord))
            {
                var ccquit = C.MKSRecordQuit;
                if (ImGui.Checkbox(E.MKSRecord.Label.Loc(), ref ccquit))
                {
                    C.MKSRecordQuit = ccquit;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.MKSRecord.Desc.Loc());
            }

            if (Show(all, E.FrontlineRecord))
            {
                var flquit = C.FrontlineRecordQuit;
                if (ImGui.Checkbox(E.FrontlineRecord.Label.Loc(), ref flquit))
                {
                    C.FrontlineRecordQuit = flquit;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.FrontlineRecord.Desc.Loc());
            }
        }

        #endregion
        #region Gold Saucer

        if (Header("Minigames and Special Events", E.Minigames, out all))
        {
            if (Show(all, E.LotteryWeekly))
            {
                var lotto = C.LotteryWeeklyInput;
                if (ImGui.Checkbox(E.LotteryWeekly.Label.Loc(), ref lotto))
                {
                    C.LotteryWeeklyInput = lotto;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.LotteryWeekly.Desc.Loc());

                if (C.LotteryWeeklyInput)
                {
                    using var indent = ImRaii.PushIndent();

                    var randomMode = C.LotteryWeeklyNumberMode == Configuration.JumboCactpotNumberMode.Random;
                    if (ImGui.RadioButton("Random number".Loc(), randomMode))
                    {
                        C.LotteryWeeklyNumberMode = Configuration.JumboCactpotNumberMode.Random;
                        C.Save();
                    }

                    ImGui.SameLine();
                    if (ImGui.RadioButton("Fixed number".Loc(), !randomMode))
                    {
                        C.LotteryWeeklyNumberMode = Configuration.JumboCactpotNumberMode.Fixed;
                        C.Save();
                    }

                    if (!randomMode)
                    {
                        var fixedNumber = C.LotteryWeeklyFixedNumber;
                        ImGui.SetNextItemWidth(100);
                        if (ImGui.InputInt("Number (0-9999)".Loc(), ref fixedNumber))
                        {
                            C.LotteryWeeklyFixedNumber = Math.Clamp(fixedNumber, 0, 9999);
                            C.Save();
                        }
                    }
                }
            }

            // 19. HWDLottery
            if (Show(all, E.HWDLottery))
            {
                var kupo = C.KupoOfFortune;
                if (ImGui.Checkbox(E.HWDLottery.Label.Loc(), ref kupo))
                {
                    C.KupoOfFortune = kupo;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.HWDLottery.Desc.Loc());
            }

            if (Show(all, E.LovmResult))
            {
                var lovQuit = C.LordOfVerminionQuit;
                if (ImGui.Checkbox(E.LovmResult.Label.Loc(), ref lovQuit))
                {
                    C.LordOfVerminionQuit = lovQuit;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.LovmResult.Desc.Loc());
            }

            if (Show(all, E.FGSEnter))
            {
                var fgsEnter = C.FallGuysRegisterConfirm;
                if (ImGui.Checkbox(E.FGSEnter.Label.Loc(), ref fgsEnter))
                {
                    C.FallGuysRegisterConfirm = fgsEnter;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.FGSEnter.Desc.Loc());
            }

            if (Show(all, E.FGSExit))
            {
                var fgsExit = C.FallGuysExitConfirm;
                if (ImGui.Checkbox(E.FGSExit.Label.Loc(), ref fgsExit))
                {
                    C.FallGuysExitConfirm = fgsExit;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.FGSExit.Desc.Loc());
            }

            if (Show(all, E.FashionCheck))
            {
                var fashionQuit = C.FashionCheckQuit;
                if (ImGui.Checkbox(E.FashionCheck.Label.Loc(), ref fashionQuit))
                {
                    C.FashionCheckQuit = fashionQuit;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.FashionCheck.Desc.Loc());
            }

            if (Show(all, E.RaceChocobo))
            {
                var chocoboQuit = C.ChocoboRacingQuit;
                if (ImGui.Checkbox(E.RaceChocobo.Label.Loc(), ref chocoboQuit))
                {
                    C.ChocoboRacingQuit = chocoboQuit;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.RaceChocobo.Desc.Loc());
            }

            if (Show(all, E.ShopCard))
            {
                var shopCard = C.ShopCardDialog;
                if (ImGui.Checkbox(E.ShopCard.Label.Loc(), ref shopCard))
                {
                    C.ShopCardDialog = shopCard;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.ShopCard.Desc.Loc());
            }
        }

        #endregion
        #region Shops

        if (Header("Shops", E.Shops, out all))
        {
            if (Show(all, E.InclusionShopRemember))
            {
                var inclusionShopRemember = C.InclusionShopRememberEnabled;
                if (ImGui.Checkbox(E.InclusionShopRemember.Label.Loc(), ref inclusionShopRemember))
                {
                    C.InclusionShopRememberEnabled = inclusionShopRemember;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.InclusionShopRemember.Desc.Loc());
            }

            if (Show(all, E.ShopExchangeItem))
            {
                var shopItemExchange = C.ShopExchangeItemDialogEnabled;
                if (ImGui.Checkbox(E.ShopExchangeItem.Label.Loc(), ref shopItemExchange))
                {
                    C.ShopExchangeItemDialogEnabled = shopItemExchange;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.ShopExchangeItem.Desc.Loc());
            }
        }
        #endregion
        #region Other

        if (Header("Other", E.Other, out all))
        {
            if (Show(all, E.SatisfactionSupply))
            {
                var deliveries = C.CustomDeliveries;
                if (ImGui.Checkbox(E.SatisfactionSupply.Label.Loc(), ref deliveries))
                {
                    C.CustomDeliveries = deliveries;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.SatisfactionSupply.Desc.Loc());
            }

            if (Show(all, E.GCSupplyReward))
            {
                var grandCompanySupplyReward = C.GrandCompanySupplyReward;
                if (ImGui.Checkbox(E.GCSupplyReward.Label.Loc(), ref grandCompanySupplyReward))
                {
                    C.GrandCompanySupplyReward = grandCompanySupplyReward;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.GCSupplyReward.Desc.Loc());
            }

            if (Show(all, E.JournalResultComplete))
            {
                var journalResultComplete = C.JournalResultCompleteEnabled;
                if (ImGui.Checkbox(E.JournalResultComplete.Label.Loc(), ref journalResultComplete))
                {
                    C.JournalResultCompleteEnabled = journalResultComplete;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.JournalResultComplete.Desc.Loc());
            }

            if (Show(all, E.GuildLeveDifficulty))
            {
                var guildLeveDifficulty = C.GuildLeveDifficultyConfirm;
                if (ImGui.Checkbox(E.GuildLeveDifficulty.Label.Loc(), ref guildLeveDifficulty))
                {
                    C.GuildLeveDifficultyConfirm = guildLeveDifficulty;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.GuildLeveDifficulty.Desc.Loc());
            }

            if (Show(all, E.DataCentreTravel))
            {
                var dkt = C.DataCentreTravelConfirmEnabled;
                if (ImGui.Checkbox(E.DataCentreTravel.Label.Loc(), ref dkt))
                {
                    C.DataCentreTravelConfirmEnabled = dkt;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.DataCentreTravel.Desc.Loc());
            }

            if (Show(all, E.MiragePrismRemoveDispel))
            {
                var mpr = C.MiragePrismRemoveDispel;
                if (ImGui.Checkbox(E.MiragePrismRemoveDispel.Label.Loc(), ref mpr))
                {
                    C.MiragePrismRemoveDispel = mpr;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.MiragePrismRemoveDispel.Desc.Loc());
            }

            if (Show(all, E.MiragePrismExecuteCast))
            {
                var mpe = C.MiragePrismExecuteCast;
                if (ImGui.Checkbox(E.MiragePrismExecuteCast.Label.Loc(), ref mpe))
                {
                    C.MiragePrismExecuteCast = mpe;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.MiragePrismExecuteCast.Desc.Loc());
            }

            if (Show(all, E.BannerPreviewUpdate))
            {
                var bpu = C.BannerPreviewUpdate;
                if (ImGui.Checkbox(E.BannerPreviewUpdate.Label.Loc(), ref bpu))
                {
                    C.BannerPreviewUpdate = bpu;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.BannerPreviewUpdate.Desc.Loc());
            }
        }

        #endregion

        #region Player Trade

        // 交易設定彼此相依（總開關收合其餘），整段一起顯示、不逐項過濾。
        if (Header("Player Trade", E.Trade, out _))
            DrawPlayerTrade();

        #endregion

        #region Forays
        if (Header("Forays", E.Forays, out all))
        {
            if (Show(all, E.ItemInspection) || Show(all, E.ItemInspectionRate))
            {
                var itemInspection = C.ItemInspectionResultEnabled;
                if (ImGui.Checkbox(E.ItemInspection.Label.Loc(), ref itemInspection))
                {
                    C.ItemInspectionResultEnabled = itemInspection;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.ItemInspection.Desc.Loc());

                if (itemInspection)
                {
                    ImGui.Indent();
                    var rateLimit = C.ItemInspectionResultRateLimiter;
                    if (ImGui.InputInt(string.Empty, ref rateLimit))
                    {
                        C.ItemInspectionResultRateLimiter = rateLimit;
                        C.Save();
                    }
                    ImGui.Unindent();
                    ImGuiX.IndentedTextColored(E.ItemInspectionRate.Desc.Loc());
                }
            }

            if (Show(all, E.WKSAnnounceHide))
            {
                var wksAnnounceHide = C.WKSAnnounceHide;
                if (ImGui.Checkbox(E.WKSAnnounceHide.Label.Loc(), ref wksAnnounceHide))
                {
                    C.WKSAnnounceHide = wksAnnounceHide;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.WKSAnnounceHide.Desc.Loc());
            }

            if (Show(all, E.WKSRewardHide))
            {
                var wksRewardClose = C.WKSRewardClose;
                if (ImGui.Checkbox(E.WKSRewardHide.Label.Loc(), ref wksRewardClose))
                {
                    C.WKSRewardClose = wksRewardClose;
                    C.Save();
                }
                ImGuiX.IndentedTextColored(E.WKSRewardHide.Desc.Loc());
            }
        }
        #endregion

        if (!anyHeaderShown)
            ImGui.TextDisabled("No matching options.".Loc());
        filterChanged = false;
    }

    /// <summary>「限時自動確認」輸入框的分鐘數：只是 UI 暫存，不存檔。</summary>
    private static int tradeTimedMinutes = TradeConfirmRules.DefaultTimedMinutes;

    private static void DrawPlayerTrade()
    {
        var enabled = C.TradeAutoConfirm;
        if (ImGui.Checkbox(E.TradeAutoConfirm.Label.Loc(), ref enabled))
        {
            C.TradeAutoConfirm = enabled;
            C.Save();
        }
        ImGuiX.IndentedTextColored(E.TradeAutoConfirm.Desc.Loc());
        if (!C.TradeAutoConfirm) return;

        using var indent = ImRaii.PushIndent();

        var press = C.TradeAutoConfirmPress;
        if (ImGui.Checkbox("Actually press".Loc(), ref press))
        {
            C.TradeAutoConfirmPress = press;
            C.Save();
        }
        ImGuiX.IndentedTextColored("Off = log mode: nothing is pressed, the log only says it would have.".Loc());

        var settleMs = C.TradeOfferSettleMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Offer delay (ms)".Loc(), ref settleMs, 0, TradeConfirmRules.MaxOfferSettleMs))
        {
            C.TradeOfferSettleMs = Math.Clamp(settleMs, 0, TradeConfirmRules.MaxOfferSettleMs);
            C.Save();
        }
        ImGuiX.IndentedTextColored("How long the other player's offer and the trade contents must stay unchanged before Offer is pressed for you.".Loc());

        ImGui.Spacing();
        ImGui.TextUnformatted(E.TradeTimed.Label.Loc());
        ImGui.SameLine();
        var remaining = Features.TradeAutoConfirm.TimedRemainingMs;
        if (remaining > 0)
            ImGui.TextUnformatted("Running: ?? min left".Loc(TradeConfirmRules.MinutesCeil(remaining)));
        else
            ImGui.TextDisabled("Not running".Loc());
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Minutes".Loc() + "##TradeTimedMinutes", ref tradeTimedMinutes))
            tradeTimedMinutes = TradeConfirmRules.ClampMinutes(tradeTimedMinutes);
        ImGui.SameLine();
        if (ImGui.Button("Start".Loc() + "##TradeTimedStart"))
            Features.TradeAutoConfirm.StartTimed(tradeTimedMinutes);
        if (remaining > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Stop".Loc() + "##TradeTimedStop"))
                Features.TradeAutoConfirm.StopTimed("設定頁關閉");
        }
        ImGuiX.IndentedTextColored(E.TradeTimed.Desc.Loc());

        ImGui.Spacing();
        var whitelist = C.TradeWhitelistEnabled;
        if (ImGui.Checkbox(E.TradeWhitelist.Label.Loc(), ref whitelist))
        {
            C.TradeWhitelistEnabled = whitelist;
            C.Save();
        }
        ImGuiX.IndentedTextColored(E.TradeWhitelist.Desc.Loc());
        if (ImGui.Button("Add current trade partner / targeted player".Loc() + "##TradeWhitelistAdd"))
            Features.TradeAutoConfirm.AddCandidate();

        var removeAt = -1;
        if (C.TradeWhitelist.Count == 0)
            ImGui.TextDisabled("No entries.".Loc());
        for (var i = 0; i < C.TradeWhitelist.Count; i++)
        {
            var entry = C.TradeWhitelist[i];
            if (ImGui.SmallButton("Remove".Loc() + $"##TradeWhitelist{i}"))
                removeAt = i;
            ImGui.SameLine();
            ImGui.TextUnformatted(entry == null ? "?" : $"{entry.Name} @ {Features.TradeAutoConfirm.WorldName(entry.HomeWorld)}");
        }
        if (removeAt >= 0)
        {
            C.TradeWhitelist.RemoveAt(removeAt);
            C.Save();
        }

        ImGui.Spacing();
        ImGuiX.IndentedTextColored("Your existing Yes/No rules (such as a zone rule for the trade confirmation) keep working as before. Materia and high-quality item warnings are never pressed. Yields while AutoRetainer or Lifestream is busy. Do not use together with Splatoon's DenyAllTrades script.".Loc());
    }
}
