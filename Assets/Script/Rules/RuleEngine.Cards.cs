using System;
using System.Collections.Generic;
using System.Linq;

namespace Waribasi.Rules
{
    public sealed partial class RuleEngine
    {
        private sealed class WorkingState
        {
            internal readonly PlayerHands[] Hands;
            internal readonly PlayerEffects[] Effects;
            internal readonly PrivateCards[] Cards;
            internal readonly List<CardUseRecord> History;
            internal RuleRandom Random;
            internal bool InitialDealt;

            internal WorkingState(RuleEngine engine)
            {
                Hands = new[] { engine.state.PlayerA, engine.state.PlayerB };
                Effects = new[] { engine.state.PlayerAEffects, engine.state.PlayerBEffects };
                Cards = new[] { engine.cardsA, engine.cardsB };
                History = new List<CardUseRecord>(engine.state.CardHistory);
                Random = engine.random;
                InitialDealt = engine.state.InitialCardsDealt;
            }
        }

        public PlayerObservation GetObservation(PlayerId player)
        {
            if (!GameState.IsValidPlayer(player))
            {
                throw new ArgumentOutOfRangeException(nameof(player));
            }

            return new PlayerObservation(player, state, GetPrivateCards(player));
        }

        //正式エンジンを引数に取らず、観測と明示した仮説だけから独立局面を作る。
        public static RuleEngine FromObservation(PlayerObservation observation,
            IEnumerable<CardDefinition> assumedOpponentCards, bool opponentRevivalAcquired,
            RuleRandom simulationRandom)
        {
            if (observation == null || simulationRandom == null || assumedOpponentCards == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            var snapshot = observation.PublicState;
            if (snapshot.MatchPhase != MatchPhase.Active)
            {
                throw new ArgumentException("Simulation requires an active observation.");
            }

            var opponent = GameState.GetOpponent(observation.Player);
            var assumed = PrivateCards.FromDefinitions(assumedOpponentCards, opponentRevivalAcquired);
            bool usedRevival = snapshot.CardHistory.Any(r => r.Player == opponent && r.Card.Kind == CardKind.Revival);
            if (assumed.Hand.Count != snapshot.GetCardCount(opponent) ||
                (usedRevival && (!assumed.RevivalAcquired || assumed.Hand.Any(c => c.Definition.Kind == CardKind.Revival))))
            {
                throw new ArgumentException("Hypothesis contradicts public card counts or revival history.");
            }

            return observation.Player == PlayerId.PlayerA
                ? new RuleEngine(snapshot, observation.Cards, assumed, simulationRandom)
                : new RuleEngine(snapshot, assumed, observation.Cards, simulationRandom);
        }

        public ActionResult TryDealInitialCards(long expectedVersion)
        {
            if (expectedVersion != state.StateVersion)
            {
                return Reject(ActionRejection.StaleState);
            }

            if (state.MatchPhase != MatchPhase.Preparing)
            {
                return Reject(ActionRejection.MatchNotPreparing);
            }

            if (state.InitialCardsDealt)
            {
                return Reject(ActionRejection.InitialCardsAlreadyDealt);
            }

            var work = new WorkingState(this);
            var events = new List<RuleEvent>();
            foreach (var player in new[] { PlayerId.PlayerA, PlayerId.PlayerB })
            {
                int seat = (int)player;
                work.Cards[seat] = work.Cards[seat].Add(CardDrawing.DrawNumber(ref work.Random, true));
                work.Cards[seat] = work.Cards[seat].Add(CardDrawing.DrawNumber(ref work.Random, false));
                DrawCards(work, player, 3);
                events.Add(new RuleEvent(RuleEventType.CardsDrawn, player, drawnCount: 5));
            }

            work.InitialDealt = true;
            return Commit(work, state.CurrentPlayer, MatchPhase.Preparing, state.TurnPhase, 0, events);
        }

        private PrivateCards GetPrivateCards(PlayerId player)
        {
            return player == PlayerId.PlayerA ? cardsA : cardsB;
        }

        //変更:公開情報だけで行う照会はRuleActionQueryへ移動した。

//削除:従来の処理は参照用に保存する。
//        private ActionRejection ValidateCard(PlayerId actor, UseCardAction action)
//        {
//            if (state.TurnPhase != TurnPhase.CardOrNormalAction)
//            {
//                return ActionRejection.CardAlreadyUsedThisTurn;
//            }
//
//            var handCard = GetPrivateCards(actor).Hand.FirstOrDefault(c => c.Id == action.CardId);
//            if (handCard == null)
//            {
//                return ActionRejection.CardNotFound;
//            }
//
//            var card = handCard.Definition;
//            if (card.Kind == CardKind.Revival)
//            {
//                return ActionRejection.AutomaticCardOnly;
//            }
//
//            if ((card.NeedsHand ? !action.Target.HasValue : action.Target.HasValue) ||
//                (action.SwapValues && !card.IsBoth))
//            {
//                return ActionRejection.InvalidCardTarget;
//            }
//
//            if (action.Target.HasValue && !GameState.IsValidSide(action.Target.Value))
//            {
//                return ActionRejection.InvalidHandSide;
//            }
//
//            if (card.IsDraw)
//            {
//                return ActionRejection.None;
//            }
//
//            var target = card.IsSelf ? actor : GameState.GetOpponent(actor);
//            var hands = state.GetHands(target);
//            if (card.IsBoth ? hands.Left == 0 || hands.Right == 0 : hands.GetCount(action.Target.Value) == 0)
//            {
//                return ActionRejection.TargetHandEliminated;
//            }
//
//            if (card.IsNumber)
//            {
//                var after = ResolveNumberHands(actor, target, card, action);
//                if (after.Left == hands.Left && after.Right == hands.Right)
//                {
//                    return ActionRejection.CardUnchanged;
//                }
//            }
//
//            return ActionRejection.None;
//        }

        //変更:公開情報だけで行う照会はRuleActionQueryへ移動した。

//削除:従来の処理は参照用に保存する。
//        private PlayerHands ResolveNumberHands(PlayerId actor, PlayerId target,
//            CardDefinition card, UseCardAction action)
//        {
//            var hands = state.GetHands(target);
//            foreach (var side in new[] { HandSide.Left, HandSide.Right })
//            {
//                if (!card.IsBoth && action.Target != side)
//                {
//                    continue;
//                }
//
//                if (actor != target && state.GetEffects(target).Get(side).IsDefended)
//                {
//                    continue;
//                }
//
//                int count = card.IsBoth && ((side == HandSide.Right) != action.SwapValues)
//                    ? card.Second : card.First;
//                hands = hands.WithCount(side, count);
//            }
//
//            return hands;
//        }

        private void ApplyCard(WorkingState work, PlayerId actor, UseCardAction action, List<RuleEvent> events)
        {
            int seat = (int)actor;
            var card = work.Cards[seat].Hand.Single(c => c.Id == action.CardId).Definition;
            work.Cards[seat] = work.Cards[seat].Remove(action.CardId);
            PlayerId? target = card.IsDraw ? (PlayerId?)null : card.IsSelf ? actor : GameState.GetOpponent(actor);
            int requested = card.Kind == CardKind.DrawTwo ? 2 : card.Kind == CardKind.DrawThree ? 3 :
                card.Kind == CardKind.DrawRandom ? CardDrawing.RollDrawCount(ref work.Random) : 0;
            int drawn = card.IsDraw ? DrawCards(work, actor, requested) : 0;
            AddCardRecord(work, actor, card, target, action.Target, action.SwapValues, false, requested, drawn, events);

            if (card.IsDraw)
            {
                events.Add(new RuleEvent(RuleEventType.CardsDrawn, actor, drawnCount: drawn));
            }
            else if (card.IsNumber)
            {
                if (target.Value != actor)
                {
                    foreach (var side in new[] { HandSide.Left, HandSide.Right })
                    {
                        if ((card.IsBoth || action.Target == side) && work.Effects[(int)target.Value].Get(side).IsDefended)
                        {
                            events.Add(new RuleEvent(RuleEventType.ChangeBlocked, target, side));
                        }
                    }
                }

                //変更:本数の解決も照会と同じ処理を使う。
                ApplyHands(work, target.Value, RuleActionQuery.ResolveNumberHands(state, actor, target.Value, card, action), events);
            }
            else
            {
                var kind = card.Kind == CardKind.Defense ? EffectKind.Defense : EffectKind.Seal;
                int targetSeat = (int)target.Value;
                var hand = action.Target.Value;
                long expiry = state.TurnNumber + 1;
                var effects = work.Effects[targetSeat].Get(hand).With(kind, expiry);
                work.Effects[targetSeat] = work.Effects[targetSeat].With(hand, effects);
                events.Add(new RuleEvent(RuleEventType.EffectApplied, target, hand,
                    effect: kind, expiresAfterTurn: expiry));
            }
        }

        private static int DrawCards(WorkingState work, PlayerId player, int requested)
        {
            int seat = (int)player;
            int count = Math.Min(requested, 5 - work.Cards[seat].Hand.Count);
            for (int index = 0; index < count; index++)
            {
                var card = CardDrawing.Draw(ref work.Random, work.Cards[seat].RevivalAcquired);
                work.Cards[seat] = work.Cards[seat].Add(card);
            }

            return count;
        }

        private void AddCardRecord(WorkingState work, PlayerId actor, CardDefinition card,
            PlayerId? target, HandSide? hand, bool swap, bool automatic, int requested, int drawn,
            List<RuleEvent> events)
        {
            var record = new CardUseRecord(actor, card, target, hand, swap, automatic, requested, drawn,
                state.TurnNumber, state.StateVersion + 1);
            work.History.Add(record);
            events.Add(new RuleEvent(RuleEventType.CardUsed, actor, hand, cardUse: record));
        }

        private static void ApplyHands(WorkingState work, PlayerId player, PlayerHands after, List<RuleEvent> events)
        {
            int seat = (int)player;
            var before = work.Hands[seat];
            work.Hands[seat] = after;
            foreach (var side in new[] { HandSide.Left, HandSide.Right })
            {
                AddOneHandEvents(events, player, side, before.GetCount(side), after.GetCount(side));
                if (before.GetCount(side) != 0 && after.GetCount(side) == 0)
                {
                    RemoveEffects(work, player, side, true, 0, events);
                }
            }
        }

        private void TryAutomaticRevival(WorkingState work, PlayerId player, List<RuleEvent> events)
        {
            int seat = (int)player;
            if (!work.Hands[seat].IsEliminated)
            {
                return;
            }

            var revival = work.Cards[seat].Hand.FirstOrDefault(c => c.Definition.Kind == CardKind.Revival);
            if (revival == null)
            {
                return;
            }

            var last = events.Last(e => e.Type == RuleEventType.HandEliminated && e.Player == player).Hand.Value;
            work.Cards[seat] = work.Cards[seat].Remove(revival.Id);
            AddCardRecord(work, player, revival.Definition, player, last, false, true, 0, 0, events);
            ApplyHands(work, player, work.Hands[seat].WithCount(last, 1), events);
        }

        private void ExpireEffects(WorkingState work, List<RuleEvent> events)
        {
            foreach (var player in new[] { PlayerId.PlayerA, PlayerId.PlayerB })
            {
                foreach (var side in new[] { HandSide.Left, HandSide.Right })
                {
                    RemoveEffects(work, player, side, false, state.TurnNumber, events);
                }
            }
        }

        private static void RemoveEffects(WorkingState work, PlayerId player, HandSide side,
            bool eliminated, long turn, List<RuleEvent> events)
        {
            int seat = (int)player;
            var effects = work.Effects[seat].Get(side);
            foreach (var kind in new[] { EffectKind.Defense, EffectKind.Seal })
            {
                long expiry = kind == EffectKind.Defense ? effects.DefenseUntilTurn : effects.SealUntilTurn;
                if (expiry != 0 && (eliminated || expiry <= turn))
                {
                    effects = effects.With(kind, 0);
                    events.Add(new RuleEvent(RuleEventType.EffectRemoved, player, side,
                        effect: kind, expiresAfterTurn: expiry,
                        removalReason: eliminated ? EffectRemovalReason.HandEliminated : EffectRemovalReason.Expired));
                }
            }

            work.Effects[seat] = work.Effects[seat].With(side, effects);
        }

        //変更:公開情報だけで行う照会はRuleActionQueryへ移動した。

//削除:従来の処理は参照用に保存する。
//        private void AddCardCandidates(List<GameAction> actions, PlayerId actor)
//        {
//            foreach (var handCard in GetPrivateCards(actor).Hand)
//            {
//                var card = handCard.Definition;
//                if (card.NeedsHand)
//                {
//                    AddIfLegal(actions, actor, new UseCardAction(handCard.Id, HandSide.Left));
//                    AddIfLegal(actions, actor, new UseCardAction(handCard.Id, HandSide.Right));
//                }
//                else
//                {
//                    AddIfLegal(actions, actor, new UseCardAction(handCard.Id));
//                    if (card.IsBoth && card.First != card.Second)
//                    {
//                        AddIfLegal(actions, actor, new UseCardAction(handCard.Id, swapValues: true));
//                    }
//                }
//            }
//        }

        private GameState BuildPublicState(PlayerHands playerA, PlayerHands playerB, PlayerId currentPlayer,
            MatchPhase matchPhase, TurnPhase turnPhase, long turnNumber, long stateVersion,
            PlayerId? winner = null, MatchEndReason endReason = MatchEndReason.None, WorkingState work = null)
        {
            work = work ?? new WorkingState(this);
            return new GameState(playerA, playerB, currentPlayer, matchPhase, turnPhase, turnNumber,
                stateVersion, winner, endReason, work.Effects[0], work.Effects[1],
                work.Cards[0].Hand.Count, work.Cards[1].Hand.Count, work.History, work.InitialDealt);
        }

        private ActionResult Commit(WorkingState work, PlayerId currentPlayer, MatchPhase matchPhase,
            TurnPhase turnPhase, long turnNumber, List<RuleEvent> events, PlayerId? winner = null,
            MatchEndReason endReason = MatchEndReason.None)
        {
            var next = BuildPublicState(work.Hands[0], work.Hands[1], currentPlayer, matchPhase, turnPhase,
                turnNumber, state.StateVersion + 1, winner, endReason, work);
            var result = new ActionResult(true, ActionRejection.None, next, events);
            cardsA = work.Cards[0];
            cardsB = work.Cards[1];
            random = work.Random;
            state = next;
            return result;
        }

        private static void ValidatePositionEffects(PlayerHands hands, PlayerEffects effects, long turnNumber)
        {
            if (turnNumber < 1 || turnNumber > long.MaxValue - 2)
            {
                throw new ArgumentOutOfRangeException(nameof(turnNumber));
            }

            foreach (var side in new[] { HandSide.Left, HandSide.Right })
            {
                var effect = effects.Get(side);
                foreach (long expiry in new[] { effect.DefenseUntilTurn, effect.SealUntilTurn })
                {
                    if (expiry != 0 && (hands.GetCount(side) == 0 || expiry < turnNumber || expiry > turnNumber + 1))
                    {
                        throw new ArgumentException("Eliminated hands and expired or distant effects are invalid.");
                    }
                }
            }
        }
    }
}
