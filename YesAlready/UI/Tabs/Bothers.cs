using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using ECommons.LanguageHelpers;
using Dalamud.Bindings.ImGui;
using System;
using System.Linq;

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

    public static void Draw()
    {
        using var tab = ImRaii.TabItem("Bothers".Loc());
        if (!tab) return;
        using var idScope = ImRaii.PushId($"BothersOptions");

        #region Hotkey Settings

        if (ImGui.CollapsingHeader("Hotkey Settings".Loc()))
        {
            // 1. Disable Hotkey
            if (!hotkeyValues.Contains(C.DisableKey))
            {
                C.DisableKey = VirtualKey.NO_KEY;
                C.Save();
            }

            var disableHotkeyIndex = Array.IndexOf(hotkeyValues, C.DisableKey);

            ImGui.SetNextItemWidth(85);
            if (ImGui.Combo("Disable Hotkey".Loc(), ref disableHotkeyIndex, hotkeyChoices, hotkeyChoices.Length))
            {
                C.DisableKey = hotkeyValues[disableHotkeyIndex];
                C.Save();
            }

            ImGuiX.IndentedTextColored("While this key is held, the plugin is disabled.".Loc());

            // 2. Forced Yes Hotkey
            if (!hotkeyValues.Contains(C.ForcedYesKey))
            {
                C.ForcedYesKey = VirtualKey.NO_KEY;
                C.Save();
            }

            var forcedYesHotkeyIndex = Array.IndexOf(hotkeyValues, C.ForcedYesKey);

            ImGui.SetNextItemWidth(85);
            if (ImGui.Combo("Forced Yes Hotkey".Loc(), ref forcedYesHotkeyIndex, hotkeyChoices, hotkeyChoices.Length))
            {
                C.ForcedYesKey = hotkeyValues[forcedYesHotkeyIndex];
                C.Save();
            }

            ImGui.SameLine();
            var separateForcedKeys = C.SeparateForcedKeys;
            if (ImGui.Checkbox("Separate Yes/Talk".Loc(), ref separateForcedKeys))
            {
                C.SeparateForcedKeys = separateForcedKeys;
                C.Save();
            }

            if (C.SeparateForcedKeys)
            {
                var forcedTalkHotkeyIndex = Array.IndexOf(hotkeyValues, C.ForcedTalkKey);
                ImGui.SetNextItemWidth(85);
                if (ImGui.Combo("Forced Talk Hotkey".Loc(), ref forcedTalkHotkeyIndex, hotkeyChoices, hotkeyChoices.Length))
                {
                    C.ForcedTalkKey = hotkeyValues[forcedTalkHotkeyIndex];
                    C.Save();
                }
            }

            ImGuiX.IndentedTextColored("2. While this key is held, any Yes/No prompt will always default to yes, and all talk dialogue will be skipped. Be careful.".Loc());
        }

        #endregion
        #region Desynthesis/AetherialReduction

        if (ImGui.CollapsingHeader("Desynthesis and Aetherial Reduction".Loc()))
        {
            // 3. SalvageDialog
            var desynthDialog = C.DesynthDialogEnabled;
            if (ImGui.Checkbox("SalvageDialog".Loc(), ref desynthDialog))
            {
                C.DesynthDialogEnabled = desynthDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Remove the Desynthesis menu confirmation.".Loc());

            // 4. SalvageDialog (Bulk)
            //var desynthBulkDialog = C.DesynthBulkDialogEnabled;
            //if (ImGui.Checkbox("SalvageDialog (Bulk)", ref desynthBulkDialog))
            //{
            //    C.DesynthBulkDialogEnabled = desynthBulkDialog;
            //    C.Save();
            //}
            //ImGuiEx.IndentedTextColored("Check the bulk desynthesis button when using the SalvageDialog feature.");

            // 5. SalvageResults
            var desynthResultsDialog = C.DesynthesisResults;
            if (ImGui.Checkbox("SalvageResults".Loc(), ref desynthResultsDialog))
            {
                C.DesynthesisResults = desynthResultsDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically closes the SalvageResults window when done desynthesizing.".Loc());

            var purifyResultsDialog = C.AetherialReductionResults;
            if (ImGui.Checkbox("PurifyResult".Loc(), ref purifyResultsDialog))
            {
                C.AetherialReductionResults = purifyResultsDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically closes the PurifyResult window when done reducing.".Loc());

            var purifyAutomatic = C.AetherialReductionAutomatic;
            if (ImGui.Checkbox("PurifyResult (Automatic)".Loc(), ref purifyAutomatic))
            {
                C.AetherialReductionAutomatic = purifyAutomatic;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Press the game's own Automatic button on the PurifyResult window, so the game reduces every remaining item by itself. You still reduce the first item yourself.".Loc());
        }

        #endregion
        #region Melding

        if (ImGui.CollapsingHeader("Melding".Loc()))
        {
            var meld = C.MaterialAttachDialogEnabled;
            if (ImGui.Checkbox("MateriaAttachDialog".Loc(), ref meld))
            {
                C.MaterialAttachDialogEnabled = meld;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Remove the materia melding confirmation menu.".Loc());

            var materialize = C.MaterializeDialogEnabled;
            if (ImGui.Checkbox("MaterializeDialog".Loc(), ref materialize))
            {
                C.MaterializeDialogEnabled = materialize;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Remove the create new (extract) materia confirmation.".Loc());

            var materiaRetrieve = C.MateriaRetrieveDialogEnabled;
            if (ImGui.Checkbox("MateriaRetrieveDialog".Loc(), ref materiaRetrieve))
            {
                C.MateriaRetrieveDialogEnabled = materiaRetrieve;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Remove the retrieve materia confirmation.".Loc());
        }

        #endregion
        #region Retainers & Submersibles

        if (ImGui.CollapsingHeader("Retainers and Submersibles".Loc()))
        {
            var retainerTaskAsk = C.RetainerTaskAskEnabled;
            if (ImGui.Checkbox("RetainerTaskAsk".Loc(), ref retainerTaskAsk))
            {
                C.RetainerTaskAskEnabled = retainerTaskAsk;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Skip the confirmation in the final dialog before sending out a retainer.".Loc());

            var retainerTaskResult = C.RetainerTaskResultEnabled;
            if (ImGui.Checkbox("RetainerTaskResult".Loc(), ref retainerTaskResult))
            {
                C.RetainerTaskResultEnabled = retainerTaskResult;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically send a retainer on the same venture as before when receiving an item.".Loc());

            var retainerListDialog = C.RetainerTransferListConfirm;
            if (ImGui.Checkbox("RetainerItemTransferList".Loc(), ref retainerListDialog))
            {
                C.RetainerTransferListConfirm = retainerListDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Skip the confirmation in the RetainerItemTransferList window to entrust all items to the retainer.".Loc());

            var retainerProgressDialog = C.RetainerTransferProgressConfirm;
            if (ImGui.Checkbox("RetainerItemTransferProgress".Loc(), ref retainerProgressDialog))
            {
                C.RetainerTransferProgressConfirm = retainerProgressDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically closes the RetainerItemTransferProgress window when finished entrusting items.".Loc());

            var finalize = C.AirShipExplorationResultFinalize;
            if (ImGui.Checkbox("AirShipExplorationResult - Finalize".Loc(), ref finalize))
            {
                if (finalize && C.AirShipExplorationResultRedeploy)
                    C.AirShipExplorationResultRedeploy = false;
                C.AirShipExplorationResultFinalize = finalize;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically finalize submersible reports when the AirShipExplorationResult window opens.".Loc());

            var redeploy = C.AirShipExplorationResultRedeploy;
            if (ImGui.Checkbox("AirShipExplorationResult - Redeploy".Loc(), ref redeploy))
            {
                if (redeploy && C.AirShipExplorationResultFinalize)
                    C.AirShipExplorationResultFinalize = false;
                C.AirShipExplorationResultRedeploy = redeploy;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically redeploy submersibles when the AirShipExplorationResult window opens.".Loc());
        }

        #endregion
        #region Duties

        if (ImGui.CollapsingHeader("Duties".Loc()))
        {
            var contentsFinderConfirm = C.ContentsFinderConfirmEnabled;
            if (ImGui.Checkbox("ContentsFinderConfirm".Loc(), ref contentsFinderConfirm))
            {
                C.ContentsFinderConfirmEnabled = contentsFinderConfirm;

                if (!contentsFinderConfirm)
                    C.ContentsFinderOneTimeConfirmEnabled = false;

                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically commence duties when ready.".Loc());

            var contentsFinderOneTimeConfirm = C.ContentsFinderOneTimeConfirmEnabled;
            if (ImGui.Checkbox("ContentsFinderOneTimeConfirm".Loc(), ref contentsFinderOneTimeConfirm))
            {
                C.ContentsFinderOneTimeConfirmEnabled = contentsFinderOneTimeConfirm;

                if (contentsFinderOneTimeConfirm)
                    C.ContentsFinderConfirmEnabled = true;

                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically commence duties when ready, but only once. Requires Contents Finder Confirm, and disables both after activation.".Loc());

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

        if (ImGui.CollapsingHeader("PvP"))
        {
            var ccquit = C.MKSRecordQuit;
            if (ImGui.Checkbox("MKSRecord".Loc(), ref ccquit))
            {
                C.MKSRecordQuit = ccquit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically leave the Crystalline Conflict match when the results appear.".Loc());

            var flquit = C.FrontlineRecordQuit;
            if (ImGui.Checkbox("FrontlineRecord".Loc(), ref flquit))
            {
                C.FrontlineRecordQuit = flquit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically leave the Frontline match when the results appear.".Loc());
        }

        #endregion
        #region Gold Saucer

        if (ImGui.CollapsingHeader("Minigames and Special Events".Loc()))
        {
            var lotto = C.LotteryWeeklyInput;
            if (ImGui.Checkbox("LotteryWeeklyInput".Loc(), ref lotto))
            {
                C.LotteryWeeklyInput = lotto;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically purchase a Jumbo Cactpot ticket with a random number.".Loc());

            // 19. HWDLottery
            var kupo = C.KupoOfFortune;
            if (ImGui.Checkbox("HWDLottery".Loc(), ref kupo))
            {
                C.KupoOfFortune = kupo;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically select a kupo of fortune reward. This will instantly complete a single kupo ticket but is unable to continue to the next automatically.".Loc());

            var lovQuit = C.LordOfVerminionQuit;
            if (ImGui.Checkbox("LovmResult".Loc(), ref lovQuit))
            {
                C.LordOfVerminionQuit = lovQuit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically quit Lord of Verminion when the results menu appears.".Loc());

            var fgsEnter = C.FallGuysRegisterConfirm;
            if (ImGui.Checkbox("FGSEnterDialog".Loc(), ref fgsEnter))
            {
                C.FallGuysRegisterConfirm = fgsEnter;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically register for Blunderville when speaking with the Blunderville Registrar.".Loc());

            var fgsExit = C.FallGuysExitConfirm;
            if (ImGui.Checkbox("FGSExitDialog".Loc(), ref fgsExit))
            {
                C.FallGuysExitConfirm = fgsExit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically confirm the exit prompt when leaving Blunderville.".Loc());

            var fashionQuit = C.FashionCheckQuit;
            if (ImGui.Checkbox("FashionCheck".Loc(), ref fashionQuit))
            {
                C.FashionCheckQuit = fashionQuit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically confirm the Fashion Reports results.".Loc());

            var chocoboQuit = C.ChocoboRacingQuit;
            if (ImGui.Checkbox("RaceChocoboResult".Loc(), ref chocoboQuit))
            {
                C.ChocoboRacingQuit = chocoboQuit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically quit Chocobo Racing when the results menu appears.".Loc());

            var shopCard = C.ShopCardDialog;
            if (ImGui.Checkbox("ShopCardDialog".Loc(), ref shopCard))
            {
                C.ShopCardDialog = shopCard;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically confirm selling Triple Triad cards in the saucer.".Loc());
        }

        #endregion
        #region Shops

        if (ImGui.CollapsingHeader("Shops".Loc()))
        {
            var inclusionShopRemember = C.InclusionShopRememberEnabled;
            if (ImGui.Checkbox("InclusionShopRemember".Loc(), ref inclusionShopRemember))
            {
                C.InclusionShopRememberEnabled = inclusionShopRemember;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Remember the last panel visited on the scrip exchange window.".Loc());

            var shopItemExchange = C.ShopExchangeItemDialogEnabled;
            if (ImGui.Checkbox("ShopExchangeItemDialog".Loc(), ref shopItemExchange))
            {
                C.ShopExchangeItemDialogEnabled = shopItemExchange;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically exchange items/currencies in various shops (e.g., scrip vendors).".Loc());
        }
        #endregion
        #region Other

        if (ImGui.CollapsingHeader("Other".Loc()))
        {
            var deliveries = C.CustomDeliveries;
            if (ImGui.Checkbox("SatisfactionSupply".Loc(), ref deliveries))
            {
                C.CustomDeliveries = deliveries;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically turn in any available collectibles for Custom Deliveries.".Loc());

            var grandCompanySupplyReward = C.GrandCompanySupplyReward;
            if (ImGui.Checkbox("GrandCompanySupplyReward".Loc(), ref grandCompanySupplyReward))
            {
                C.GrandCompanySupplyReward = grandCompanySupplyReward;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Skip the confirmation when submitting Grand Company expert delivery items.".Loc());

            var journalResultComplete = C.JournalResultCompleteEnabled;
            if (ImGui.Checkbox("JournalResultComplete".Loc(), ref journalResultComplete))
            {
                C.JournalResultCompleteEnabled = journalResultComplete;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically confirm quest reward acceptance when there is nothing to choose.".Loc());

            var guildLeveDifficulty = C.GuildLeveDifficultyConfirm;
            if (ImGui.Checkbox("GuildLeveDifficulty".Loc(), ref guildLeveDifficulty))
            {
                C.GuildLeveDifficultyConfirm = guildLeveDifficulty;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically confirms guild leves upon initiation at the highest difficulty.".Loc());

            var dkt = C.DataCentreTravelConfirmEnabled;
            if (ImGui.Checkbox("DataCentreTravelConfirm".Loc(), ref dkt))
            {
                C.DataCentreTravelConfirmEnabled = dkt;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically accept the Data Center travel confirmation.".Loc());

            var mpr = C.MiragePrismRemoveDispel;
            if (ImGui.Checkbox("MiragePrismRemoveDispel".Loc(), ref mpr))
            {
                C.MiragePrismRemoveDispel = mpr;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically dispel glamours when using Glamour Dispellers.".Loc());

            var mpe = C.MiragePrismExecuteCast;
            if (ImGui.Checkbox("MiragePrismExecuteCast".Loc(), ref mpe))
            {
                C.MiragePrismExecuteCast = mpe;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically cast glamours when using Glamour Prisms.".Loc());

            var bpu = C.BannerPreviewUpdate;
            if (ImGui.Checkbox("BannerPreviewUpdate".Loc(), ref bpu))
            {
                C.BannerPreviewUpdate = bpu;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically update portraits.".Loc());
        }

        #endregion

        #region Forays
        if (ImGui.CollapsingHeader("Forays".Loc()))
        {
            var itemInspection = C.ItemInspectionResultEnabled;
            if (ImGui.Checkbox("ItemInspectionResult".Loc(), ref itemInspection))
            {
                C.ItemInspectionResultEnabled = itemInspection;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Eureka/Bozja lockboxes, forgotten fragments, and more. Warning: this does not check if you are maxed on items. Rate limiter (pause after N items).".Loc());

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
                ImGuiX.IndentedTextColored("Rate limiter (pause after N items, 0 to disable).".Loc());
            }

            var wksAnnounceHide = C.WKSAnnounceHide;
            if (ImGui.Checkbox("WKSAnnounceHide".Loc(), ref wksAnnounceHide))
            {
                C.WKSAnnounceHide = wksAnnounceHide;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Hide Cosmic Exploration announcements.".Loc());

            var wksRewardClose = C.WKSRewardClose;
            if (ImGui.Checkbox("WKSRewardHide".Loc(), ref wksRewardClose))
            {
                C.WKSRewardClose = wksRewardClose;
                C.Save();
            }
            ImGuiX.IndentedTextColored("Automatically close the Cosmic Exploration rewards window.".Loc());
        }
        #endregion
    }
}
