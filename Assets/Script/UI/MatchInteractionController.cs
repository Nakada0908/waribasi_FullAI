using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Waribasi.MatchFlow;
using Waribasi.Networking;
using Waribasi.Rules;

namespace Waribasi.Presentation
{
    public enum MatchSelectionMode
    {
        None,
        Attack,
        Card,
        Redistribution
    }

    public sealed class MatchInteractionController : MonoBehaviour
    {
        public static MatchInteractionController Instance { get; private set; }
        [SerializeField] private GameObject selectionPanel;
        [SerializeField] private TMP_Text selectionText;
        [SerializeField] private Button[] optionButtons;
        [SerializeField] private TMP_Text[] optionTexts;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button attackCancelButton;
        [SerializeField] private Button redistributeButton;
        [SerializeField] private Button swapButton;
        //変更:既存の交換ボタンは、条件付きスキップの判断専用にする。
        private TMP_Text swapText;
        //削除:private string swapLabel;
        private MatchClientState view;
        private RuleActionQuery query;
        private IReadOnlyList<GameAction> legalActions = Array.Empty<GameAction>();
        private readonly List<GameAction> options = new List<GameAction>();
        //変更:手の選択は通信上のオブジェクトではなく共通の席と左右で保持する。
        private MatchHandView attackSource;
        private GameAction selectedAction;
        private string selectedMatch;
        private long selectedVersion;
        private string message = "自分の手を選び、相手の手をタップして攻撃";

        public MatchClientState ClientState => view;
        public MatchSelectionMode Mode { get; private set; }
        public HandCard SelectedCard { get; private set; }
        public bool HistoryOpen { get; private set; }
        //追加:退出確認中は盤面・札のローカル入力だけを止める。
        public bool ModalBlocked { get; private set; }
        //変更:確認モーダルを通常操作の受付条件へ含める。
        public bool CanInteract => view != null && view.CanAct && !HistoryOpen && !ModalBlocked;
        //削除:履歴だけを考慮していた条件。
        //public bool CanInteract => view != null && view.CanAct && !HistoryOpen;
        public bool HasSelection => Mode != MatchSelectionMode.None;
        public IReadOnlyList<GameAction> LegalActions => legalActions;
        public string Message => message;
        public event Action Changed;

        private void Awake()
        {
            Instance = this;
            for (int i = 0; i < optionButtons.Length; i++)
            {
                int index = i;
                optionButtons[i].onClick.AddListener(() => SelectOption(index));
            }
            confirmButton.onClick.AddListener(ConfirmSelection);
            cancelButton.onClick.AddListener(CancelSelection);
            attackCancelButton.onClick.AddListener(CancelSelection);
            redistributeButton.onClick.AddListener(OpenRedistribution);
            //変更:配分と交換は同じ入口にまとめ、既存Sceneの参照を利用する。
            redistributeButton.GetComponentInChildren<TMP_Text>(true).text = "手を変更";
            //変更:封印で行動できないときだけカード辞退操作を表示する。
            //削除:swapButton.onClick.AddListener(SelectSwap);
            swapText = swapButton.GetComponentInChildren<TMP_Text>(true);
            //削除:swapLabel = swapText != null ? swapText.text : "左右交換";
            //削除:swapButton.onClick.AddListener(SelectSwapOrSkip);
            swapButton.onClick.AddListener(SkipWithoutCard);
            RefreshControls();
        }

        private void Update()
        {
            if (view != MatchSessionAccess.State)
            {
                //変更:モード変更時は古い状態の購読を解除して接続し直す。
                Bind(MatchSessionAccess.State);
            }
        }

        public void Bind(MatchClientState state)
        {
            if (view != null)
            {
                view.Changed -= OnStateChanged;
                view.Responded -= OnResponse;
            }
            view = state;
            if (view != null)
            {
                view.Changed += OnStateChanged;
                view.Responded += OnResponse;
            }
            OnStateChanged();
        }

        private void OnDestroy()
        {
            if (view != null)
            {
                view.Changed -= OnStateChanged;
                view.Responded -= OnResponse;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnStateChanged()
        {
            query = CreateQuery(view);
            legalActions = query != null ? query.GetLegalActions((PlayerId)view.LocalPlayer) : Array.Empty<GameAction>();
            //変更:同じ版の本人札が更新された場合も、無効になった選択を破棄する。
            if (HasSelection && (!CanInteract || view.Public.matchId != selectedMatch || view.Public.stateVersion != selectedVersion ||
                (SelectedCard != null && !CanUseCard(SelectedCard.Id)) || (selectedAction != null && !IsLegal(selectedAction))))
            {
                ClearSelection();
            }
            RefreshControls();
            Changed?.Invoke();
        }

        public static RuleActionQuery CreateQuery(MatchClientState state)
        {
            if (state == null || state.Public == null || !state.HandIsCurrent)
            {
                return null;
            }
            var p = state.Public;
            return RuleActionQuery.FromPublic(new PlayerHands(p.hands[0].count, p.hands[1].count),
                new PlayerHands(p.hands[2].count, p.hands[3].count), (PlayerId)p.currentPlayer,
                (MatchPhase)p.matchPhase, (TurnPhase)p.turnPhase, p.stateVersion,
                new PlayerEffects(Effects(p.hands[0]), Effects(p.hands[1])),
                new PlayerEffects(Effects(p.hands[2]), Effects(p.hands[3])), state.OwnHand);
        }

        private static HandEffects Effects(HandStateMessage hand)
        {
            return new HandEffects(hand.defenseUntil, hand.sealUntil);
        }

        public bool IsLegal(GameAction action)
        {
            return CanInteract && query != null &&
                query.Validate((PlayerId)view.LocalPlayer, action, view.Public.stateVersion) == ActionRejection.None;
        }

        //追加:見た目と直接呼出しのどちらも、共通ルールが返す本人札の合法候補で判定する。
        public bool CanUseCard(long cardId)
        {
            return CanInteract && legalActions.OfType<UseCardAction>().Any(action => action.CardId == cardId);
        }

        private void BeginSelection(MatchSelectionMode mode)
        {
            ClearSelection();
            Mode = mode;
            selectedMatch = view.Public.matchId;
            selectedVersion = view.Public.stateVersion;
        }

        //変更:従来のオンライン呼出しを共通手識別へ転送する。
        public void SelectHand(NetworkObject hand)
        {
            SelectMatchHand(hand != null ? hand.GetComponent<MatchHandView>() : null);
        }

        //追加:人間とNPCの表示手を同じ選択・合法性判定へ通す。
        public void SelectMatchHand(MatchHandView hand)
        {
            //変更:オンライン入力には引き続き実際のSpawnを要求し、表示照会とは分ける。
            if (!CanInteract || hand == null || !hand.TryGetPlayer(out var player) ||
                (!MatchSessionAccess.IsLocal && hand.NetworkHand?.IsSpawned != true))
            {
                return;
            }
            HandSide side = hand.Side;
            bool own = (int)player == view.LocalPlayer;
            if (Mode == MatchSelectionMode.Card)
            {
                if (SelectedCard.Definition.NeedsHand && own == SelectedCard.Definition.IsSelf)
                {
                    ChooseAction(new UseCardAction(SelectedCard.Id, side));
                }
                return;
            }
            if (Mode == MatchSelectionMode.Redistribution)
            {
                return;
            }
            if (own)
            {
                if (attackSource == hand)
                {
                    CancelSelection();
                    return;
                }
                if (!legalActions.OfType<AttackAction>().Any(a => a.Source == side))
                {
                    ShowMessage("この手は攻撃元にできません");
                    return;
                }
                BeginSelection(MatchSelectionMode.Attack);
                attackSource = hand;
                message = "相手の生きている手を選択（同じ自分の手で取消）";
                PublishSelection();
            }
            else if (Mode == MatchSelectionMode.Attack && attackSource != null)
            {
                //変更:左右を表示手から読み、通信とローカルで同じ要求を作る。
                var action = new AttackAction(attackSource.Side, side);
                if (IsLegal(action))
                {
                    //変更:現在の対戦モードの窓口へ送る。
                    bool sent = MatchSessionAccess.Send(action, view.Public.stateVersion, attackSource, hand);
                    AfterSend(sent);
                }
            }
        }

        public void SelectCard(int index)
        {
            if (!CanInteract || index < 0 || index >= view.OwnHand.Count)
            {
                return;
            }
            var card = view.OwnHand[index];
            //追加:使用済み・自動復活・合法な対象のない札は選択自体を受け付けない。
            if (!CanUseCard(card.Id))
            {
                return;
            }
            if (SelectedCard != null && SelectedCard.Id == card.Id)
            {
                CancelSelection();
                return;
            }
            //削除:復活札を選択してから通知する分岐は、上の共通候補判定へ統合。
            BeginSelection(MatchSelectionMode.Card);
            SelectedCard = card;
            options.AddRange(legalActions.OfType<UseCardAction>().Where(a => a.CardId == card.Id));
            //変更:合法な候補が1つなら対象・割当てを選び直さず、確認と取消だけにする。
            //削除:if (card.Definition.IsDraw && options.Count == 1)
            if (options.Count == 1)
            {
                ChooseAction(options[0]);
                return;
            }
            message = MatchText.Card(card.Definition) + "\n" +
                (card.Definition.NeedsHand ? "対象本人の左右を選択してください" : "対象本人の左・右への割り当てを選択してください");
            PublishSelection();
        }

        public void OpenRedistribution()
        {
            //変更:合法な手の変更がある場合だけ開き、配分と交換を同じ候補一覧に表示する。
            if (!CanInteract || !legalActions.OfType<RedistributeAction>().Any())
            {
                return;
            }
            if (Mode == MatchSelectionMode.Redistribution)
            {
                CancelSelection();
                return;
            }
            BeginSelection(MatchSelectionMode.Redistribution);
            options.AddRange(legalActions.OfType<RedistributeAction>());
            //変更:入口の名称に合わせ、合体・復活・交換も同じ手の変更として案内する。
            message = "手の変更を選択してください（配分・合体・復活・交換で手番を消費）";
            PublishSelection();
        }

        public void SelectSwap()
        {
            if (!CanInteract)
            {
                return;
            }
            int offset = view.LocalPlayer * 2;
            var swap = new RedistributeAction(view.Public.hands[offset + 1].count, view.Public.hands[offset].count);
            if (!IsLegal(swap))
            {
                return;
            }
            if (Mode != MatchSelectionMode.Redistribution)
            {
                OpenRedistribution();
            }
            ChooseAction(swap);
        }

        //変更:旧呼出しとの互換口もスキップ専用にし、交換の別入口にしない。
        public void SelectSwapOrSkip()
        {
            SkipWithoutCard();
        }

        //追加:カード辞退は手の変更と別に残し、共有の合法性照会と送信経路へ通す。
        public void SkipWithoutCard()
        {
            var skip = new SkipAction();
            if (IsLegal(skip))
            {
                AfterSend(MatchSessionAccess.Send(skip, view.Public.stateVersion));
                return;
            }
            //削除:SelectSwap();
        }

        public void SelectOption(int index)
        {
            if (CanInteract && index >= 0 && index < options.Count)
            {
                ChooseAction(options[index]);
            }
        }

        public void SelectCardTarget(HandSide side)
        {
            if (Mode == MatchSelectionMode.Card && SelectedCard != null && SelectedCard.Definition.NeedsHand)
            {
                ChooseAction(new UseCardAction(SelectedCard.Id, side));
            }
        }

        private void ChooseAction(GameAction action)
        {
            if (!IsLegal(action))
            {
                return;
            }
            selectedAction = action;
            //変更:確認内容も統一した入口名に揃える。
            message = (SelectedCard != null ? MatchText.Card(SelectedCard.Definition) + "\n" : "手の変更：") +
                MatchText.Option(action, SelectedCard?.Definition) + "\n確定前は取消できます";
            PublishSelection();
        }

        public void ConfirmSelection()
        {
            if (!CanInteract || selectedAction == null || view.Public.matchId != selectedMatch ||
                view.Public.stateVersion != selectedVersion || !IsLegal(selectedAction))
            {
                return;
            }
            //変更:選択版と操作を共通窓口へ渡し、確定は各進行側へ任せる。
            bool sent = MatchSessionAccess.Send(selectedAction, selectedVersion);
            AfterSend(sent);
        }

        private void AfterSend(bool sent)
        {
            ClearSelection();
            message = sent ? "要求を送信しました" : "送信できませんでした。現在の状態を確認してください";
            PublishSelection();
        }

        public void CancelSelection()
        {
            ClearSelection();
            message = "自分の手を選び、相手の手をタップして攻撃";
            PublishSelection();
        }

        public void SetHistoryOpen(bool open)
        {
            HistoryOpen = open;
            ClearSelection();
            PublishSelection();
        }

        //追加:確認UIを開くと未確定選択を破棄し、成立済みの処理は戻さない。
        public void SetModalBlocked(bool blocked)
        {
            ModalBlocked = blocked;
            ClearSelection();
            PublishSelection();
        }

        private void ClearSelection()
        {
            Mode = MatchSelectionMode.None;
            message = "自分の手を選び、相手の手をタップして攻撃";
            SelectedCard = null;
            selectedAction = null;
            attackSource = null;
            options.Clear();
        }

        private void ShowMessage(string value)
        {
            message = value;
            PublishSelection();
        }

        private void OnResponse(RequestResponseMessage response)
        {
            if (!response.accepted)
            {
                ClearSelection();
                message = MatchText.Rejection(response);
            }
            PublishSelection();
        }

        private void PublishSelection()
        {
            RefreshControls();
            Changed?.Invoke();
        }

        private void RefreshControls()
        {
            if (selectionPanel == null)
            {
                return;
            }
            selectionPanel.SetActive(HasSelection && Mode != MatchSelectionMode.Attack && !HistoryOpen);
            attackCancelButton.gameObject.SetActive(Mode == MatchSelectionMode.Attack && !HistoryOpen);
            selectionText.text = message;
            for (int i = 0; i < optionButtons.Length; i++)
            {
                //変更:カード候補が1つのときは確定済みの対象を文面に示し、選択ボタンを省く。
                bool visible = i < options.Count && !(Mode == MatchSelectionMode.Card && options.Count == 1);
                optionButtons[i].gameObject.SetActive(visible);
                optionButtons[i].interactable = visible && CanInteract;
                if (visible)
                {
                    optionTexts[i].text = MatchText.Option(options[i], SelectedCard?.Definition);
                }
            }
            confirmButton.interactable = CanInteract && selectedAction != null;
            cancelButton.interactable = HasSelection;
            redistributeButton.interactable = CanInteract && legalActions.OfType<RedistributeAction>().Any();
            //変更:交換は手の変更一覧へ統合したため、旧ボタンはスキップ判断中だけ表示する。
            bool canSkip = legalActions.Any(action => action is SkipAction);
            swapButton.gameObject.SetActive(canSkip && !HistoryOpen && !ModalBlocked);
            swapButton.interactable = CanInteract && canSkip;
            if (swapText != null)
            {
                swapText.text = "カードを使わない";
            }
        }

        //変更:既存オンラインの照会口は互換用に残す。
        public bool IsHandSelected(NetworkObject hand)
        {
            return IsMatchHandSelected(hand != null ? hand.GetComponent<MatchHandView>() : null);
        }

        //追加:選択表示も共通の席と左右を使う。
        public bool IsMatchHandSelected(MatchHandView hand)
        {
            if (!HasSelection || hand == null || view?.Public == null)
            {
                return false;
            }
            if (Mode == MatchSelectionMode.Attack)
            {
                return hand == attackSource;
            }
            if (Mode == MatchSelectionMode.Card && SelectedCard != null && !SelectedCard.Definition.IsDraw && selectedAction is UseCardAction card)
            {
                //変更:ローカル手の所有者は明示された席で判定する。
                bool own = hand.TryGetPlayer(out var player) && (int)player == view.LocalPlayer;
                return own == SelectedCard.Definition.IsSelf &&
                    //変更:共通の左右で対象を照合する。
                    (!card.Target.HasValue || card.Target == hand.Side);
            }
            //変更:振り分け選択は本人の手だけに表示する。
            return Mode == MatchSelectionMode.Redistribution && hand.TryGetPlayer(out var owner) && (int)owner == view.LocalPlayer;
        }
    }
}

//削除:T06の共通入力・ローカル接続へ移行する前の箇所を参照用に保存する。
//        private NetworkObject attackSource;
//if (view == null && MatchClientState.Instance != null)
//            {
//                Bind(MatchClientState.Instance);
//        public void SelectHand(NetworkObject hand)
//        {
//            if (!CanInteract || hand == null || !hand.IsSpawned || hand.GetComponent<HandRL>() == null)
//            {
//                return;
//            }
//            HandSide side = MatchClientState.ToSide(hand.GetComponent<HandRL>().handType);
//            bool own = hand.OwnerClientId == NetworkManager.Singleton.LocalClientId;
//var action = new AttackAction(MatchClientState.ToSide(attackSource.GetComponent<HandRL>().handType), side);
//bool sent = MatchNetworkController.Instance != null && MatchNetworkController.Instance.SendAttack(attackSource, hand);
//            var network = MatchNetworkController.Instance;
//            bool sent = false;
//            if (network != null && selectedAction is UseCardAction card)
//            {
//                sent = network.SendCard(card.CardId, card.Target, card.SwapValues, selectedVersion);
//            }
//            else if (network != null && selectedAction is RedistributeAction distribution)
//            {
//                sent = network.SendRedistribution(distribution.Left, distribution.Right);
//            }
//        public bool IsHandSelected(NetworkObject hand)
//        {
//bool own = hand.OwnerClientId == NetworkManager.Singleton.LocalClientId;
//(!card.Target.HasValue || card.Target == MatchClientState.ToSide(hand.GetComponent<HandRL>().handType));
//return Mode == MatchSelectionMode.Redistribution && hand.OwnerClientId == NetworkManager.Singleton.LocalClientId;

//削除:T11で共通候補判定とスキップ専用表示へ置き換えた旧処理を参照用に残す。
//if (card.Definition.Kind == CardKind.Revival)
//{
//    ShowMessage("復活カードは敗北直前に自動発動します");
//    return;
//}
//message = MatchText.Card(card.Definition) + "\n" +
//    (options.Count == 0 ? "現在は使用できません。取消して別の操作を選べます" :
//    card.Definition.NeedsHand ? "対象本人の左右を選択してください" :
//    card.Definition.IsBoth ? "対象本人の左・右への割り当てを選択してください" : "確定で使用します");
//if (view?.Public != null)
//{
//    int offset = view.LocalPlayer * 2;
//    bool canSkip = legalActions.Any(action => action is SkipAction);
//    swapButton.interactable = CanInteract && (canSkip || IsLegal(new RedistributeAction(view.Public.hands[offset + 1].count, view.Public.hands[offset].count)));
//    if (swapText != null)
//    {
//        swapText.text = canSkip ? "カードを使わない" : swapLabel;
//    }
//}
//else
//{
//    swapButton.interactable = false;
//}
