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
        private MatchClientState view;
        private RuleActionQuery query;
        private IReadOnlyList<GameAction> legalActions = Array.Empty<GameAction>();
        private readonly List<GameAction> options = new List<GameAction>();
        private NetworkObject attackSource;
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
            swapButton.onClick.AddListener(SelectSwap);
            RefreshControls();
        }

        private void Update()
        {
            if (view == null && MatchClientState.Instance != null)
            {
                Bind(MatchClientState.Instance);
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
            if (HasSelection && (!CanInteract || view.Public.matchId != selectedMatch || view.Public.stateVersion != selectedVersion))
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

        private void BeginSelection(MatchSelectionMode mode)
        {
            ClearSelection();
            Mode = mode;
            selectedMatch = view.Public.matchId;
            selectedVersion = view.Public.stateVersion;
        }

        public void SelectHand(NetworkObject hand)
        {
            if (!CanInteract || hand == null || !hand.IsSpawned || hand.GetComponent<HandRL>() == null)
            {
                return;
            }
            HandSide side = MatchClientState.ToSide(hand.GetComponent<HandRL>().handType);
            bool own = hand.OwnerClientId == NetworkManager.Singleton.LocalClientId;
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
                var action = new AttackAction(MatchClientState.ToSide(attackSource.GetComponent<HandRL>().handType), side);
                if (IsLegal(action))
                {
                    bool sent = MatchNetworkController.Instance != null && MatchNetworkController.Instance.SendAttack(attackSource, hand);
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
            if (SelectedCard != null && SelectedCard.Id == card.Id)
            {
                CancelSelection();
                return;
            }
            if (card.Definition.Kind == CardKind.Revival)
            {
                ShowMessage("復活カードは敗北直前に自動発動します");
                return;
            }
            BeginSelection(MatchSelectionMode.Card);
            SelectedCard = card;
            options.AddRange(legalActions.OfType<UseCardAction>().Where(a => a.CardId == card.Id));
            message = MatchText.Card(card.Definition) + "\n" +
                (options.Count == 0 ? "現在は使用できません。取消して別の操作を選べます" :
                card.Definition.NeedsHand ? "対象本人の左右を選択してください" :
                card.Definition.IsBoth ? "対象本人の左・右への割り当てを選択してください" : "確定で使用します");
            if (card.Definition.IsDraw && options.Count == 1)
            {
                selectedAction = options[0];
            }
            PublishSelection();
        }

        public void OpenRedistribution()
        {
            if (!CanInteract)
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
            message = "左右の配分を選択してください（合体・復活・交換も手番を消費）";
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
            message = (SelectedCard != null ? MatchText.Card(SelectedCard.Definition) + "\n" : "振り分け：") +
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
            var network = MatchNetworkController.Instance;
            bool sent = false;
            if (network != null && selectedAction is UseCardAction card)
            {
                sent = network.SendCard(card.CardId, card.Target, card.SwapValues, selectedVersion);
            }
            else if (network != null && selectedAction is RedistributeAction distribution)
            {
                sent = network.SendRedistribution(distribution.Left, distribution.Right);
            }
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
                bool visible = i < options.Count;
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
            if (view?.Public != null)
            {
                int offset = view.LocalPlayer * 2;
                swapButton.interactable = IsLegal(new RedistributeAction(view.Public.hands[offset + 1].count, view.Public.hands[offset].count));
            }
            else
            {
                swapButton.interactable = false;
            }
        }

        public bool IsHandSelected(NetworkObject hand)
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
                bool own = hand.OwnerClientId == NetworkManager.Singleton.LocalClientId;
                return own == SelectedCard.Definition.IsSelf &&
                    (!card.Target.HasValue || card.Target == MatchClientState.ToSide(hand.GetComponent<HandRL>().handType));
            }
            return Mode == MatchSelectionMode.Redistribution && hand.OwnerClientId == NetworkManager.Singleton.LocalClientId;
        }
    }
}
