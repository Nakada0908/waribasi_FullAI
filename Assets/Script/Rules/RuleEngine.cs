using System;
using System.Collections.Generic;

namespace Waribasi.Rules
{
    public sealed class RuleEngine
    {
        private GameState state;

        //状態は不変なので、保持したSnapshotが後の操作で書き換わることはない。
        public GameState Snapshot => state;

        public RuleEngine(PlayerId firstPlayer = PlayerId.PlayerA)
        {
            if (!GameState.IsValidPlayer(firstPlayer))
            {
                throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            }

            state = new GameState(new PlayerHands(1, 1), new PlayerHands(1, 1), firstPlayer,
                MatchPhase.Preparing, TurnPhase.CardOrNormalAction, 0, 0);
        }

        private RuleEngine(GameState snapshot)
        {
            state = snapshot;
        }

        public static RuleEngine FromPosition(PlayerHands playerA, PlayerHands playerB,
            PlayerId currentPlayer, TurnPhase turnPhase = TurnPhase.CardOrNormalAction)
        {
            if (playerA.IsEliminated || playerB.IsEliminated)
            {
                throw new ArgumentException("An active position requires both players to have a living hand.");
            }

            if (!GameState.IsValidPlayer(currentPlayer))
            {
                throw new ArgumentOutOfRangeException(nameof(currentPlayer));
            }

            if (turnPhase != TurnPhase.CardOrNormalAction && turnPhase != TurnPhase.NormalActionOnly)
            {
                throw new ArgumentOutOfRangeException(nameof(turnPhase));
            }

            return new RuleEngine(new GameState(playerA, playerB, currentPlayer,
                MatchPhase.Active, turnPhase, 1, 0));
        }

        public RuleEngine Fork()
        {
            return new RuleEngine(state);
        }

        public ActionResult TryStart(long expectedVersion)
        {
            if (expectedVersion != state.StateVersion)
            {
                return Reject(ActionRejection.StaleState);
            }

            if (state.MatchPhase != MatchPhase.Preparing)
            {
                return Reject(ActionRejection.MatchNotPreparing);
            }

            var next = new GameState(state.PlayerA, state.PlayerB, state.CurrentPlayer,
                MatchPhase.Active, TurnPhase.CardOrNormalAction, 1, state.StateVersion + 1);
            return Accept(next, new[]
            {
                new RuleEvent(RuleEventType.MatchStarted),
                new RuleEvent(RuleEventType.TurnChanged, next.CurrentPlayer)
            });
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

            var hands = state.GetHands(actor);
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

                var opponent = state.GetHands(GameState.GetOpponent(actor));
                return opponent.GetCount(attack.Target) == 0
                    ? ActionRejection.TargetHandEliminated
                    : ActionRejection.None;
            }

            if (action is RedistributeAction distribution)
            {
                if (distribution.Left < 0 || distribution.Left > 4 ||
                    distribution.Right < 0 || distribution.Right > 4)
                {
                    return ActionRejection.FingerCountOutOfRange;
                }

                if (distribution.Left + distribution.Right != hands.Total)
                {
                    return ActionRejection.TotalNotPreserved;
                }

                return distribution.Left == hands.Left && distribution.Right == hands.Right
                    ? ActionRejection.DistributionUnchanged
                    : ActionRejection.None;
            }

            return ActionRejection.InvalidAction;
        }

        public ActionResult TryApply(PlayerId actor, GameAction action, long expectedVersion)
        {
            var rejection = Validate(actor, action, expectedVersion);
            if (rejection != ActionRejection.None)
            {
                return Reject(rejection, GameState.IsValidPlayer(actor) ? actor : (PlayerId?)null);
            }

            var playerA = state.PlayerA;
            var playerB = state.PlayerB;
            var events = new List<RuleEvent>
            {
                new RuleEvent(RuleEventType.ActionAccepted, actor)
            };

            PlayerId changedPlayer;
            PlayerHands changedHands;
            if (action is AttackAction attack)
            {
                changedPlayer = GameState.GetOpponent(actor);
                var original = state.GetHands(changedPlayer);
                int count = (original.GetCount(attack.Target) + state.GetHands(actor).GetCount(attack.Source)) % 5;
                changedHands = original.WithCount(attack.Target, count);
            }
            else
            {
                var distribution = (RedistributeAction)action;
                changedPlayer = actor;
                changedHands = new PlayerHands(distribution.Left, distribution.Right);
            }

            AddHandEvents(events, changedPlayer, state.GetHands(changedPlayer), changedHands);
            if (changedPlayer == PlayerId.PlayerA)
            {
                playerA = changedHands;
            }
            else
            {
                playerB = changedHands;
            }

            return CompleteNormalAction(playerA, playerB, events);
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

            return actions.AsReadOnly();
        }

        //通信側が本人確認と期限判定を済ませた後に呼ぶ終了入口。
        public ActionResult TryEnd(PlayerId loser, MatchEndReason reason, long expectedVersion)
        {
            if (!GameState.IsValidPlayer(loser))
            {
                return Reject(ActionRejection.InvalidPlayer);
            }

            if (expectedVersion != state.StateVersion)
            {
                return Reject(ActionRejection.StaleState);
            }

            if (state.MatchPhase != MatchPhase.Active)
            {
                return Reject(ActionRejection.MatchNotActive);
            }

            if (reason != MatchEndReason.Surrender && reason != MatchEndReason.Timeout &&
                reason != MatchEndReason.Disconnected)
            {
                return Reject(ActionRejection.InvalidEndReason);
            }

            var next = new GameState(state.PlayerA, state.PlayerB, state.CurrentPlayer,
                MatchPhase.Finished, state.TurnPhase, state.TurnNumber, state.StateVersion + 1,
                GameState.GetOpponent(loser), reason);
            return Accept(next, new[] { new RuleEvent(RuleEventType.MatchEnded, endReason: reason) });
        }

        public ActionResult TryInterrupt(long expectedVersion)
        {
            if (expectedVersion != state.StateVersion)
            {
                return Reject(ActionRejection.StaleState);
            }

            if (state.MatchPhase == MatchPhase.Finished || state.MatchPhase == MatchPhase.Interrupted)
            {
                return Reject(ActionRejection.MatchNotActive);
            }

            var next = new GameState(state.PlayerA, state.PlayerB, state.CurrentPlayer,
                MatchPhase.Interrupted, state.TurnPhase, state.TurnNumber, state.StateVersion + 1,
                endReason: MatchEndReason.CommunicationInterrupted);
            return Accept(next, new[]
            {
                new RuleEvent(RuleEventType.MatchEnded, endReason: MatchEndReason.CommunicationInterrupted)
            });
        }

        private void AddIfLegal(List<GameAction> actions, PlayerId actor, GameAction action)
        {
            if (Validate(actor, action, state.StateVersion) == ActionRejection.None)
            {
                actions.Add(action);
            }
        }

        private ActionResult CompleteNormalAction(PlayerHands playerA, PlayerHands playerB,
            List<RuleEvent> events)
        {
            //T02の自動復活は、脱落通知の生成後、この勝敗確定前に接続する。
            if (playerA.IsEliminated || playerB.IsEliminated)
            {
                var winner = playerA.IsEliminated ? PlayerId.PlayerB : PlayerId.PlayerA;
                var finished = new GameState(playerA, playerB, state.CurrentPlayer,
                    MatchPhase.Finished, TurnPhase.NormalActionOnly, state.TurnNumber,
                    state.StateVersion + 1, winner, MatchEndReason.HandsEliminated);
                events.Add(new RuleEvent(RuleEventType.MatchEnded, endReason: MatchEndReason.HandsEliminated));
                return Accept(finished, events);
            }

            var next = new GameState(playerA, playerB, GameState.GetOpponent(state.CurrentPlayer),
                MatchPhase.Active, TurnPhase.CardOrNormalAction, state.TurnNumber + 1,
                state.StateVersion + 1);
            events.Add(new RuleEvent(RuleEventType.TurnChanged, next.CurrentPlayer));
            return Accept(next, events);
        }

        private static void AddHandEvents(List<RuleEvent> events, PlayerId player,
            PlayerHands before, PlayerHands after)
        {
            AddOneHandEvents(events, player, HandSide.Left, before.Left, after.Left);
            AddOneHandEvents(events, player, HandSide.Right, before.Right, after.Right);
        }

        private static void AddOneHandEvents(List<RuleEvent> events, PlayerId player,
            HandSide side, int before, int after)
        {
            if (before == after)
            {
                return;
            }

            events.Add(new RuleEvent(RuleEventType.HandChanged, player, side, before, after));
            if (after == 0)
            {
                events.Add(new RuleEvent(RuleEventType.HandEliminated, player, side, before, after));
            }
            else if (before == 0)
            {
                events.Add(new RuleEvent(RuleEventType.HandRevived, player, side, before, after));
            }
        }

        private ActionResult Accept(GameState next, IEnumerable<RuleEvent> events)
        {
            var result = new ActionResult(true, ActionRejection.None, next, events);
            state = next;
            return result;
        }

        private ActionResult Reject(ActionRejection rejection, PlayerId? actor = null)
        {
            return new ActionResult(false, rejection, state,
                new[] { new RuleEvent(RuleEventType.ActionRejected, actor, rejection: rejection) });
        }
    }
}
