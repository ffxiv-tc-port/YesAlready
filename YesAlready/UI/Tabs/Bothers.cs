using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using ImGuiNET;
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
        using var tab = ImRaii.TabItem("其他選項");
        if (!tab) return;
        using var idScope = ImRaii.PushId($"BothersOptions");

        #region Hotkey Settings

        if (ImGui.CollapsingHeader("快捷鍵設定"))
        {
            // 1. Disable Hotkey
            if (!hotkeyValues.Contains(C.DisableKey))
            {
                C.DisableKey = VirtualKey.NO_KEY;
                C.Save();
            }

            var disableHotkeyIndex = Array.IndexOf(hotkeyValues, C.DisableKey);

            ImGui.SetNextItemWidth(85);
            if (ImGui.Combo("停用快捷鍵", ref disableHotkeyIndex, hotkeyChoices, hotkeyChoices.Length))
            {
                C.DisableKey = hotkeyValues[disableHotkeyIndex];
                C.Save();
            }

            ImGuiX.IndentedTextColored("按住此鍵時，外掛將暫時停用。");

            // 2. Forced Yes Hotkey
            if (!hotkeyValues.Contains(C.ForcedYesKey))
            {
                C.ForcedYesKey = VirtualKey.NO_KEY;
                C.Save();
            }

            var forcedYesHotkeyIndex = Array.IndexOf(hotkeyValues, C.ForcedYesKey);

            ImGui.SetNextItemWidth(85);
            if (ImGui.Combo("強制確認快捷鍵", ref forcedYesHotkeyIndex, hotkeyChoices, hotkeyChoices.Length))
            {
                C.ForcedYesKey = hotkeyValues[forcedYesHotkeyIndex];
                C.Save();
            }

            ImGui.SameLine();
            var separateForcedKeys = C.SeparateForcedKeys;
            if (ImGui.Checkbox("分開設定確認/對話快捷鍵", ref separateForcedKeys))
            {
                C.SeparateForcedKeys = separateForcedKeys;
                C.Save();
            }

            if (C.SeparateForcedKeys)
            {
                var forcedTalkHotkeyIndex = Array.IndexOf(hotkeyValues, C.ForcedTalkKey);
                ImGui.SetNextItemWidth(85);
                if (ImGui.Combo("強制對話快捷鍵", ref forcedTalkHotkeyIndex, hotkeyChoices, hotkeyChoices.Length))
                {
                    C.ForcedTalkKey = hotkeyValues[forcedTalkHotkeyIndex];
                    C.Save();
                }
            }

            ImGuiX.IndentedTextColored("2. 按住此鍵時，任何是/否對話框都會自動選擇「是」，且所有對話都會被跳過。請小心使用。");
        }

        #endregion
        #region Desynthesis/AetherialReduction

        if (ImGui.CollapsingHeader("分解與精煉"))
        {
            // 3. SalvageDialog
            var desynthDialog = C.DesynthDialogEnabled;
            if (ImGui.Checkbox("分解確認視窗", ref desynthDialog))
            {
                C.DesynthDialogEnabled = desynthDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("移除分解選單的確認提示。");

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
            if (ImGui.Checkbox("分解結果視窗", ref desynthResultsDialog))
            {
                C.DesynthesisResults = desynthResultsDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("分解完成後自動關閉分解結果視窗。");

            var purifyResultsDialog = C.AetherialReductionResults;
            if (ImGui.Checkbox("精煉結果視窗", ref purifyResultsDialog))
            {
                C.AetherialReductionResults = purifyResultsDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("精煉完成後自動關閉精煉結果視窗。");
        }

        #endregion
        #region Melding

        if (ImGui.CollapsingHeader("鑲嵌魔晶石"))
        {
            var meld = C.MaterialAttachDialogEnabled;
            if (ImGui.Checkbox("鑲嵌確認視窗", ref meld))
            {
                C.MaterialAttachDialogEnabled = meld;
                C.Save();
            }
            ImGuiX.IndentedTextColored("移除魔晶石鑲嵌的確認選單。");

            var materialize = C.MaterializeDialogEnabled;
            if (ImGui.Checkbox("精製魔晶石確認視窗", ref materialize))
            {
                C.MaterializeDialogEnabled = materialize;
                C.Save();
            }
            ImGuiX.IndentedTextColored("移除精製（萃取）魔晶石的確認提示。");

            var materiaRetrieve = C.MateriaRetrieveDialogEnabled;
            if (ImGui.Checkbox("取出魔晶石確認視窗", ref materiaRetrieve))
            {
                C.MateriaRetrieveDialogEnabled = materiaRetrieve;
                C.Save();
            }
            ImGuiX.IndentedTextColored("移除取出魔晶石的確認提示。");
        }

        #endregion
        #region Retainers & Submersibles

        if (ImGui.CollapsingHeader("雇員與潛水艇"))
        {
            var retainerTaskAsk = C.RetainerTaskAskEnabled;
            if (ImGui.Checkbox("派遣委託確認", ref retainerTaskAsk))
            {
                C.RetainerTaskAskEnabled = retainerTaskAsk;
                C.Save();
            }
            ImGuiX.IndentedTextColored("跳過派遣雇員前最後一個確認對話框。");

            var retainerTaskResult = C.RetainerTaskResultEnabled;
            if (ImGui.Checkbox("委託結果自動重新派遣", ref retainerTaskResult))
            {
                C.RetainerTaskResultEnabled = retainerTaskResult;
                C.Save();
            }
            ImGuiX.IndentedTextColored("領取物品時，自動以相同的委託再次派遣雇員。");

            var retainerListDialog = C.RetainerTransferListConfirm;
            if (ImGui.Checkbox("雇員物品轉移清單", ref retainerListDialog))
            {
                C.RetainerTransferListConfirm = retainerListDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("跳過將所有物品委託給雇員的確認提示。");

            var retainerProgressDialog = C.RetainerTransferProgressConfirm;
            if (ImGui.Checkbox("雇員物品轉移進度", ref retainerProgressDialog))
            {
                C.RetainerTransferProgressConfirm = retainerProgressDialog;
                C.Save();
            }
            ImGuiX.IndentedTextColored("委託完成後自動關閉物品轉移進度視窗。");

            var finalize = C.AirShipExplorationResultFinalize;
            if (ImGui.Checkbox("潛水艇探索報告 - 完成", ref finalize))
            {
                if (finalize && C.AirShipExplorationResultRedeploy)
                    C.AirShipExplorationResultRedeploy = false;
                C.AirShipExplorationResultFinalize = finalize;
                C.Save();
            }
            ImGuiX.IndentedTextColored("開啟潛水艇探索報告視窗時，自動完成回報。");

            var redeploy = C.AirShipExplorationResultRedeploy;
            if (ImGui.Checkbox("潛水艇探索報告 - 重新派遣", ref redeploy))
            {
                if (redeploy && C.AirShipExplorationResultFinalize)
                    C.AirShipExplorationResultFinalize = false;
                C.AirShipExplorationResultRedeploy = redeploy;
                C.Save();
            }
            ImGuiX.IndentedTextColored("開啟潛水艇探索報告視窗時，自動重新派遣潛水艇。");
        }

        #endregion
        #region Duties

        if (ImGui.CollapsingHeader("任務"))
        {
            var contentsFinderConfirm = C.ContentsFinderConfirmEnabled;
            if (ImGui.Checkbox("任務準備完成確認", ref contentsFinderConfirm))
            {
                C.ContentsFinderConfirmEnabled = contentsFinderConfirm;

                if (!contentsFinderConfirm)
                    C.ContentsFinderOneTimeConfirmEnabled = false;

                C.Save();
            }
            ImGuiX.IndentedTextColored("準備完成時自動開始任務。");

            var contentsFinderOneTimeConfirm = C.ContentsFinderOneTimeConfirmEnabled;
            if (ImGui.Checkbox("任務準備完成確認（單次）", ref contentsFinderOneTimeConfirm))
            {
                C.ContentsFinderOneTimeConfirmEnabled = contentsFinderOneTimeConfirm;

                if (contentsFinderOneTimeConfirm)
                    C.ContentsFinderConfirmEnabled = true;

                C.Save();
            }
            ImGuiX.IndentedTextColored("準備完成時自動開始任務，但僅執行一次。需要啟用「任務準備完成確認」，觸發後兩者皆會停用。");

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
            if (ImGui.Checkbox("水晶塔紛爭結果", ref ccquit))
            {
                C.MKSRecordQuit = ccquit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("結果視窗出現時自動離開水晶塔紛爭。");

            var flquit = C.FrontlineRecordQuit;
            if (ImGui.Checkbox("前線結果", ref flquit))
            {
                C.FrontlineRecordQuit = flquit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("結果視窗出現時自動離開前線。");
        }

        #endregion
        #region Gold Saucer

        if (ImGui.CollapsingHeader("小遊戲與特殊活動"))
        {
            var lotto = C.LotteryWeeklyInput;
            if (ImGui.Checkbox("每週彩券輸入", ref lotto))
            {
                C.LotteryWeeklyInput = lotto;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動以隨機號碼購買夢幻彩券。");

            // 19. HWDLottery
            var kupo = C.KupoOfFortune;
            if (ImGui.Checkbox("庫波的幸運籤", ref kupo))
            {
                C.KupoOfFortune = kupo;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動選擇庫波的幸運籤獎勵。此功能會立即完成單張籤，但無法自動繼續下一張。");

            var lovQuit = C.LordOfVerminionQuit;
            if (ImGui.Checkbox("魔物使決鬥結果", ref lovQuit))
            {
                C.LordOfVerminionQuit = lovQuit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("結果選單出現時自動離開魔物使決鬥。");

            var fgsEnter = C.FallGuysRegisterConfirm;
            if (ImGui.Checkbox("糖豆人報名視窗", ref fgsEnter))
            {
                C.FallGuysRegisterConfirm = fgsEnter;
                C.Save();
            }
            ImGuiX.IndentedTextColored("與糖豆人報名接待員交談時自動報名。");

            var fgsExit = C.FallGuysExitConfirm;
            if (ImGui.Checkbox("糖豆人離開視窗", ref fgsExit))
            {
                C.FallGuysExitConfirm = fgsExit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("離開糖豆人時自動確認離開提示。");

            var fashionQuit = C.FashionCheckQuit;
            if (ImGui.Checkbox("時尚品鑑結果", ref fashionQuit))
            {
                C.FashionCheckQuit = fashionQuit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動確認時尚品鑑的結果。");

            var chocoboQuit = C.ChocoboRacingQuit;
            if (ImGui.Checkbox("陸行鳥競賽結果", ref chocoboQuit))
            {
                C.ChocoboRacingQuit = chocoboQuit;
                C.Save();
            }
            ImGuiX.IndentedTextColored("結果選單出現時自動離開陸行鳥競賽。");

            var shopCard = C.ShopCardDialog;
            if (ImGui.Checkbox("卡牌販售確認視窗", ref shopCard))
            {
                C.ShopCardDialog = shopCard;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動確認在黃金水都販售九宮飛牌卡牌。");
        }

        #endregion
        #region Shops

        if (ImGui.CollapsingHeader("商店"))
        {
            var inclusionShopRemember = C.InclusionShopRememberEnabled;
            if (ImGui.Checkbox("記住納品交換所頁籤", ref inclusionShopRemember))
            {
                C.InclusionShopRememberEnabled = inclusionShopRemember;
                C.Save();
            }
            ImGuiX.IndentedTextColored("記住納品交換所視窗上次瀏覽的頁籤。");

            var shopItemExchange = C.ShopExchangeItemDialogEnabled;
            if (ImGui.Checkbox("商店物品交換確認視窗", ref shopItemExchange))
            {
                C.ShopExchangeItemDialogEnabled = shopItemExchange;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動在各種商店（例如納品點數兌換商）交換物品/貨幣。");
        }
        #endregion
        #region Other

        if (ImGui.CollapsingHeader("其他"))
        {
            var deliveries = C.CustomDeliveries;
            if (ImGui.Checkbox("特殊納品", ref deliveries))
            {
                C.CustomDeliveries = deliveries;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動繳交特殊納品所需的可用收藏品。");

            var grandCompanySupplyReward = C.GrandCompanySupplyReward;
            if (ImGui.Checkbox("軍隊納品確認", ref grandCompanySupplyReward))
            {
                C.GrandCompanySupplyReward = grandCompanySupplyReward;
                C.Save();
            }
            ImGuiX.IndentedTextColored("跳過提交軍隊高階納品物品時的確認提示。");

            var journalResultComplete = C.JournalResultCompleteEnabled;
            if (ImGui.Checkbox("任務獎勵完成確認", ref journalResultComplete))
            {
                C.JournalResultCompleteEnabled = journalResultComplete;
                C.Save();
            }
            ImGuiX.IndentedTextColored("當任務獎勵沒有可選項目時，自動確認領取。");

            var guildLeveDifficulty = C.GuildLeveDifficultyConfirm;
            if (ImGui.Checkbox("行會令確認難度", ref guildLeveDifficulty))
            {
                C.GuildLeveDifficultyConfirm = guildLeveDifficulty;
                C.Save();
            }
            ImGuiX.IndentedTextColored("開始行會令時自動以最高難度確認。");

            var dkt = C.DataCentreTravelConfirmEnabled;
            if (ImGui.Checkbox("資料中心旅行確認", ref dkt))
            {
                C.DataCentreTravelConfirmEnabled = dkt;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動接受資料中心旅行確認提示。");

            var mpr = C.MiragePrismRemoveDispel;
            if (ImGui.Checkbox("幻化解除自動確認", ref mpr))
            {
                C.MiragePrismRemoveDispel = mpr;
                C.Save();
            }
            ImGuiX.IndentedTextColored("使用幻化解除鏡時自動解除幻化。");

            var mpe = C.MiragePrismExecuteCast;
            if (ImGui.Checkbox("幻化施加自動確認", ref mpe))
            {
                C.MiragePrismExecuteCast = mpe;
                C.Save();
            }
            ImGuiX.IndentedTextColored("使用幻化鏡時自動施加幻化。");

            var bpu = C.BannerPreviewUpdate;
            if (ImGui.Checkbox("肖像預覽自動更新", ref bpu))
            {
                C.BannerPreviewUpdate = bpu;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動更新肖像。");
        }

        #endregion

        #region Forays
        if (ImGui.CollapsingHeader("大型地圖探索"))
        {
            var itemInspection = C.ItemInspectionResultEnabled;
            if (ImGui.Checkbox("物品確認視窗", ref itemInspection))
            {
                C.ItemInspectionResultEnabled = itemInspection;
                C.Save();
            }
            ImGuiX.IndentedTextColored("尤利卡/波茲雅的寶箱、遺忘的殘片等。警告：此功能不會檢查物品是否已達上限。限速器（每 N 個物品暫停一次）。");

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
                ImGuiX.IndentedTextColored("限速器（每 N 個物品暫停一次，設為 0 停用）。");
            }

            var wksAnnounceHide = C.WKSAnnounceHide;
            if (ImGui.Checkbox("隱藏宇宙探索公告", ref wksAnnounceHide))
            {
                C.WKSAnnounceHide = wksAnnounceHide;
                C.Save();
            }
            ImGuiX.IndentedTextColored("隱藏宇宙探索的公告訊息。");

            var wksRewardClose = C.WKSRewardClose;
            if (ImGui.Checkbox("隱藏宇宙探索獎勵視窗", ref wksRewardClose))
            {
                C.WKSRewardClose = wksRewardClose;
                C.Save();
            }
            ImGuiX.IndentedTextColored("自動關閉宇宙探索的獎勵視窗。");
        }
        #endregion
    }
}
