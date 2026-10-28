using System;
using System.Collections.Generic;
using System.Linq;

namespace Waribasi.Rules
{
    //公開盤面と本人札だけを使う問い合わせ。状態更新と乱数を保持しない。
    public sealed class RuleActionQuery
    {
        private readonly GameState state;
        private readonly IReadOnlyList<HandCard> ownHand;

        internal RuleActionQuery(GameState state, IReadOnlyList<HandCard> ownHand)
        {
            this.state = state;
            this.ownHand = ownHand;
        }

        public static RuleActionQuery FromPublic(PlayerHands playerA, PlayerHands playerB,
            PlayerId currentPlayer, MatchPhase phase, TurnPhase turnPhase, long version,
            PlayerEffects playerAEffects, PlayerEffects playerBEffects, IEnumerable<HandCard> ownHand)
        {
            if (!GameState.IsValidPlayer(currentPlayer) || version < 0 || ownHand == null)
            {
                throw new ArgumentException("A current public position and own hand are required.");
            }
            var cards = ownHand.ToList();
            if (cards.Count > 5 || cards.Any(c => c == null) || cards.Select(c => c.Id).Distinct().Count() != cards.Count)
            {
                throw new ArgumentException("Invalid own hand.");
            }
            var state = new GameState(playerA, playerB, currentPlayer, phase, turnPhase, 0, version,
                playerAEffects: playerAEffects, playerBEffects: playerBEffects);
            return new RuleActionQuery(state, cards.AsReadOnly());
        }

        public ActionRejection Validate(PlayerId actor, GameAction action, long expectedVersion)
        {
            if (!GameState.IsValidPlayer(actor))
            {
                return ActionRejection.InvalidPlayer;
            }

            if (expectedVersion != state.StateVersion)
            {
                return ActionRejection.StaleState;
            }

            if (state.MatchPhase != MatchPhase.Active)
            {
                return ActionRejection.MatchNotActive;
            }

            if (actor != state.CurrentPlayer)
            {
                return ActionRejection.NotYourTurn;
            }

            //カードの検証も同じ要求入口に集約する。
            if (action is UseCardAction cardAction)
            {
                return ValidateCard(actor, cardAction);
            }

            var hands = state.GetHands(actor);
            //追加:任意のパスを認めず、最後の生存手が封印された場合に限る。
            if (action is SkipAction)
            {
                return RequiresSkip(actor) ? ActionRejection.None : ActionRejection.SkipNotAllowed;
            }
            if (action is AttackAction attack)
            {
                if (!GameState.IsValidSide(attack.Source) || !GameState.IsValidSide(attack.Target))
                {
                    return ActionRejection.InvalidHandSide;
                }

                if (hands.GetCount(attack.Source) == 0)
                {
                    return ActionRejection.SourceHandEliminated;
                }

                //封印された生存手も通常攻撃元にはできない。
                if (state.GetEffects(actor).Get(attack.Source).IsSealed)
                {
                    return ActionRejection.SourceHandSealed;
                }

                var opponent = state.GetHands(GameState.GetOpponent(actor));
                return opponent.GetCount(attack.Target) == 0
                    ? ActionRejection.TargetHandEliminated
                    : ActionRejection.None;
            }

            if (action is RedistributeAction distribution)
            {
                //追加:封印手を含む本人の配置変更は全体を禁止する。
                if (HasSealedHand(actor))
                {
                    return ActionRejection.OwnHandSealed;
                }
                if (distribution.Left < 0 || distribution.Left > 4 ||
                    distribution.Right < 0 || distribution.Right > 4)
                {
                    return ActionRejection.FingerCountOutOfRange;
                }

                if (distribution.Left + distribution.Right != hands.Total)
                {
                    return ActionRejection.TotalNotPreserved;
                }

                //追加:片手を移すだけの交換は禁止し、両手を生かす分配は残す。
                if ((hands.Left == 0 || hands.Right == 0) &&
                    distribution.Left == hands.Right && distribution.Right == hands.Left)
                {
                    return ActionRejection.SingleHandSwap;
                }

                return distribution.Left == hands.Left && distribution.Right == hands.Right
                    ? ActionRejection.DistributionUnchanged
                    : ActionRejection.None;
            }

            return ActionRejection.InvalidAction;
        }

        //追加:カード判断前後・通信・表示で共通のスキップ条件を問い合わせる。
        public bool RequiresSkip(PlayerId actor)
        {
            if (!GameState.IsValidPlayer(actor) || state.MatchPhase != MatchPhase.Active || actor != state.CurrentPlayer)
            {
                return false;
            }
            var hands = state.GetHands(actor);
            return (hands.Left == 0 && hands.Right > 0 && state.GetEffects(actor).Right.IsSealed) ||
                (hands.Right == 0 && hands.Left > 0 && state.GetEffects(actor).Left.IsSealed);
        }

        private bool HasSealedHand(PlayerId actor)
        {
            var effects = state.GetEffects(actor);
            return effects.Left.IsSealed || effects.Right.IsSealed;
        }

        public IReadOnlyList<GameAction> GetLegalActions(PlayerId actor)
        {
            var actions = new List<GameAction>();
            if (!GameState.IsValidPlayer(actor) || state.MatchPhase != MatchPhase.Active ||
                actor != state.CurrentPlayer)
            {
                return actions.AsReadOnly();
            }

            var sides = new[] { HandSide.Left, HandSide.Right };
            foreach (var source in sides)
            {
                foreach (var target in sides)
                {
                    AddIfLegal(actions, actor, new AttackAction(source, target));
                }
            }

            int total = state.GetHands(actor).Total;
            for (int left = 0; left <= 4; left++)
            {
                AddIfLegal(actions, actor, new RedistributeAction(left, total - left));
            }

            //候補生成は検証だけを行い、抽選や手札を消費しない。
            AddCardCandidates(actions, actor);
            //追加:NPCのカードなし・カード後の仮想手番も同じ行動で終了できる。
            AddIfLegal(actions, actor, new SkipAction());
            return actions.AsReadOnly();
        }

        private void AddIfLegal(List<GameAction> actions, PlayerId actor, GameAction action)
        {
            if (Validate(actor, action, state.StateVersion) == ActionRejection.None)
            {
                actions.Add(action);
            }
        }

        private ActionRejection ValidateCard(PlayerId actor, UseCardAction action)
        {
            if (state.TurnPhase != TurnPhase.CardOrNormalAction)
            {
                return ActionRejection.CardAlreadyUsedThisTurn;
            }

            var handCard = ownHand.FirstOrDefault(c => c.Id == action.CardId);
            if (handCard == null)
            {
                return ActionRejection.CardNotFound;
            }

            var card = handCard.Definition;
            if (card.Kind == CardKind.Revival)
            {
                return ActionRejection.AutomaticCardOnly;
            }

            if ((card.NeedsHand ? !action.Target.HasValue : action.Target.HasValue) ||
                (action.SwapValues && !card.IsBoth))
            {
                return ActionRejection.InvalidCardTarget;
            }

            if (action.Target.HasValue && !GameState.IsValidSide(action.Target.Value))
            {
                return ActionRejection.InvalidHandSide;
            }

            if (card.IsDraw)
            {
                return ActionRejection.None;
            }

            var target = card.IsSelf ? actor : GameState.GetOpponent(actor);
            var hands = state.GetHands(target);
            if (card.IsBoth ? hands.Left == 0 || hands.Right == 0 : hands.GetCount(action.Target.Value) == 0)
            {
                return ActionRejection.TargetHandEliminated;
            }

            //追加:本人の自己対象札だけを制限し、相手対象札と補充には適用しない。
            if (card.IsSelf && (card.IsBoth ? HasSealedHand(actor) : state.GetEffects(actor).Get(action.Target.Value).IsSealed))
            {
                return ActionRejection.OwnHandSealed;
            }

            if (card.IsNumber)
            {
                var after = ResolveNumberHands(state, actor, target, card, action);
                if (after.Left == hands.Left && after.Right == hands.Right)
                {
                    return ActionRejection.CardUnchanged;
                }
            }

            return ActionRejection.None;
        }

        internal static PlayerHands ResolveNumberHands(GameState state, PlayerId actor, PlayerId target,
            CardDefinition card, UseCardAction action)
        {
            var hands = state.GetHands(target);
            foreach (var side in new[] { HandSide.Left, HandSide.Right })
            {
                if (!card.IsBoth && action.Target != side)
                {
                    continue;
                }

                if (actor != target && state.GetEffects(target).Get(side).IsDefended)
                {
                    continue;
                }

                int count = card.IsBoth && ((side == HandSide.Right) != action.SwapValues)
                    ? card.Second : card.First;
                hands = hands.WithCount(side, count);
            }

            return hands;
        }

        private void AddCardCandidates(List<GameAction> actions, PlayerId actor)
        {
            foreach (var handCard in ownHand)
            {
                var card = handCard.Definition;
                if (card.NeedsHand)
                {
                    AddIfLegal(actions, actor, new UseCardAction(handCard.Id, HandSide.Left));
                    AddIfLegal(actions, actor, new UseCardAction(handCard.Id, HandSide.Right));
                }
                else
                {
                    AddIfLegal(actions, actor, new UseCardAction(handCard.Id));
                    if (card.IsBoth && card.First != card.Second)
                    {
                        AddIfLegal(actions, actor, new UseCardAction(handCard.Id, swapValues: true));
                    }
                }
            }
        }
    }
}
