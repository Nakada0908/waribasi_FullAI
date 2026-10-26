using System;
using System.Collections.Generic;

namespace Waribasi.Rules
{
    //変更:カード関連の実装を同じ共通更新元の別ファイルへ分ける。
    public sealed partial class RuleEngine
    {
        private GameState state;
        //追加:非公開状態は公開Snapshotから分離し、各値を不変に保つ。
        private PrivateCards cardsA = new PrivateCards();
        private PrivateCards cardsB = new PrivateCards();
        private RuleRandom random;

        //状態は不変なので、保持したSnapshotが後の操作で書き換わることはない。
        public GameState Snapshot => state;

        //変更:検証用の乱数を指定できる。指定なしなら新しい試合用に生成する。
        public RuleEngine(PlayerId firstPlayer = PlayerId.PlayerA, RuleRandom random = null)
        {
            if (!GameState.IsValidPlayer(firstPlayer))
            {
                throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            }

            state = new GameState(new PlayerHands(1, 1), new PlayerHands(1, 1), firstPlayer,
                MatchPhase.Preparing, TurnPhase.CardOrNormalAction, 0, 0);
            //追加:正式乱数はエンジン外の観測情報へ渡さない。
            this.random = random ?? RuleRandom.Create();
        }

        //変更:Forkは不変な非公開状態と乱数も共有開始でき、適用後は別の値になる。
        private RuleEngine(GameState snapshot, PrivateCards cardsA, PrivateCards cardsB, RuleRandom random)
        {
            state = snapshot;
            this.cardsA = cardsA;
            this.cardsB = cardsB;
            this.random = random;
        }

        public static RuleEngine FromPosition(PlayerHands playerA, PlayerHands playerB,
            //変更:テスト・仮想局面だけで手札と効果も指定できる。
            PlayerId currentPlayer, TurnPhase turnPhase = TurnPhase.CardOrNormalAction,
            IEnumerable<CardDefinition> playerACards = null, IEnumerable<CardDefinition> playerBCards = null,
            bool playerARevivalAcquired = false, bool playerBRevivalAcquired = false,
            PlayerEffects playerAEffects = default, PlayerEffects playerBEffects = default,
            RuleRandom random = null, long turnNumber = 1)
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

            //変更:仮想局面の矛盾を拒否し、手札なしの既存呼出しも維持する。
            ValidatePositionEffects(playerA, playerAEffects, turnNumber);
            ValidatePositionEffects(playerB, playerBEffects, turnNumber);
            var aCards = PrivateCards.FromDefinitions(playerACards, playerARevivalAcquired);
            var bCards = PrivateCards.FromDefinitions(playerBCards, playerBRevivalAcquired);
            return new RuleEngine(new GameState(playerA, playerB, currentPlayer,
                MatchPhase.Active, turnPhase, turnNumber, 0, playerAEffects: playerAEffects,
                playerBEffects: playerBEffects, playerACardCount: aCards.Hand.Count,
                playerBCardCount: bCards.Hand.Count, initialCardsDealt: true),
                aCards, bCards, random ?? RuleRandom.Create());
        }

        public RuleEngine Fork()
        {
            //変更:すべて不変なので、Forkの適用で正式な手札や乱数も進まない。
            return new RuleEngine(state, cardsA, cardsB, random);
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

            //追加:本番の開始は両者の配布後だけ受け付ける。
            if (!state.InitialCardsDealt)
            {
                return Reject(ActionRejection.InitialCardsNotDealt);
            }

            //変更:開始時にも公開効果・枚数・履歴を維持する。
            var next = BuildPublicState(state.PlayerA, state.PlayerB, state.CurrentPlayer,
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

            //追加:カードの検証も同じ要求入口に集約する。
            if (action is UseCardAction cardAction)
            {
                return ValidateCard(actor, cardAction);
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

                //追加:封印された生存手も通常攻撃元にはできない。
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

            //変更:手札・効果・乱数を含む作業状態で解決し、最後に一度だけ確定する。
            var work = new WorkingState(this);
            var events = new List<RuleEvent>
            {
                new RuleEvent(RuleEventType.ActionAccepted, actor)
            };
            //追加:カードは手番を進めず、通常行動だけを残す。
            if (action is UseCardAction cardAction)
            {
                ApplyCard(work, actor, cardAction, events);
                return Commit(work, state.CurrentPlayer, MatchPhase.Active,
                    TurnPhase.NormalActionOnly, state.TurnNumber, events);
            }

            PlayerId changedPlayer;
            PlayerHands changedHands;
            if (action is AttackAction attack)
            {
                changedPlayer = GameState.GetOpponent(actor);
                var original = state.GetHands(changedPlayer);
                //変更:防御先への攻撃も成立させ、本数変化だけを抑止する。
                //削除:防御を考慮しない旧加算。
                //int count = (original.GetCount(attack.Target) + state.GetHands(actor).GetCount(attack.Source)) % 5;
                bool defended = state.GetEffects(changedPlayer).Get(attack.Target).IsDefended;
                int count = defended ? original.GetCount(attack.Target) :
                    (original.GetCount(attack.Target) + state.GetHands(actor).GetCount(attack.Source)) % 5;
                if (defended)
                {
                    events.Add(new RuleEvent(RuleEventType.ChangeBlocked, changedPlayer, attack.Target));
                }
                changedHands = original.WithCount(attack.Target, count);
            }
            else
            {
                var distribution = (RedistributeAction)action;
                changedPlayer = actor;
                changedHands = new PlayerHands(distribution.Left, distribution.Right);
            }

            //変更:脱落した左右の効果解除も同じ作業状態で実行する。
            ApplyHands(work, changedPlayer, changedHands, events);
            return CompleteNormalAction(work, changedPlayer, events);
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

            //追加:候補生成は検証だけを行い、抽選や手札を消費しない。
            AddCardCandidates(actions, actor);
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

            //変更:外部終了では手札・効果を維持し、自動復活を呼ばない。
            var next = BuildPublicState(state.PlayerA, state.PlayerB, state.CurrentPlayer,
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

            //変更:通信中断でも公開状態を失わず、自動復活を呼ばない。
            var next = BuildPublicState(state.PlayerA, state.PlayerB, state.CurrentPlayer,
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

        //変更:脱落効果解除後に自動復活を解決してから勝敗を確定する。
        private ActionResult CompleteNormalAction(WorkingState work, PlayerId changedPlayer,
            List<RuleEvent> events)
        {
            //T02の自動復活は、脱落通知の生成後、この勝敗確定前に接続する。
            //追加:最後に脱落した左右は今回の解決結果から取得する。
            TryAutomaticRevival(work, changedPlayer, events);
            if (work.Hands[0].IsEliminated || work.Hands[1].IsEliminated)
            {
                //変更:復活後にも脱落が残る場合だけ敗北を確定する。
                var winner = work.Hands[0].IsEliminated ? PlayerId.PlayerB : PlayerId.PlayerA;
                events.Add(new RuleEvent(RuleEventType.MatchEnded, endReason: MatchEndReason.HandsEliminated));
                return Commit(work, state.CurrentPlayer, MatchPhase.Finished, TurnPhase.NormalActionOnly,
                    state.TurnNumber, events, winner, MatchEndReason.HandsEliminated);
            }

            //変更:終了した手番までの期限を解除してから次の手番を通知する。
            ExpireEffects(work, events);
            var nextPlayer = GameState.GetOpponent(state.CurrentPlayer);
            events.Add(new RuleEvent(RuleEventType.TurnChanged, nextPlayer));
            return Commit(work, nextPlayer, MatchPhase.Active, TurnPhase.CardOrNormalAction,
                state.TurnNumber + 1, events);
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
