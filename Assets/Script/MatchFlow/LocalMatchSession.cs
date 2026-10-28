using System;
using Waribasi.Rules;

namespace Waribasi.MatchFlow
{
    public sealed class LocalMatchSession
    {
        private readonly ulong[] requests = new ulong[2];
        //追加:結果と保存は正式なローカル対局の経路で結び付ける。
        private readonly NpcUnlockProgress progress;
        public NpcMatchSetup Setup { get; }
        public MatchAuthority Authority { get; private set; }
        public PlayerId Human => PlayerId.PlayerA;
        public PlayerId Npc => PlayerId.PlayerB;

        //変更:隔離した保存先も注入できる。既存呼び出しは登録済みの保存を使う。
        public LocalMatchSession(NpcMatchSetup setup, PlayerId randomFirst, NpcUnlockProgress progress = null)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            //追加:直接構築も未解放の最強を受け付けない。
            this.progress = progress ?? NpcMatchEntry.Progress;
            if (setup.Difficulty == NpcDifficulty.Strongest && this.progress?.IsStrongestUnlocked != true)
            {
                throw new InvalidOperationException("Strongest NPC is locked.");
            }
            //変更:上級も共通のローカル進行で開始する。
            //削除:上級未実装時の構築拒否を保存する。
            //if (setup.Difficulty == NpcDifficulty.Advanced)
            //{
            //    throw new ArgumentException("Advanced NPC is not implemented.", nameof(setup));
            //}
            Restart(randomFirst);
        }

        public void Restart(PlayerId randomFirst)
        {
            if (Authority != null && Authority.State.MatchPhase == MatchPhase.Active)
            {
                throw new InvalidOperationException("Finish the current match before restarting.");
            }
            PlayerId first = Setup.First == NpcFirstPlayerChoice.Player ? Human :
                Setup.First == NpcFirstPlayerChoice.Npc ? Npc : randomFirst;
            Authority = new MatchAuthority(Guid.NewGuid(), first, 0);
            requests[0] = requests[1] = 0;
        }

        public MatchResponse Submit(PlayerId actor, GameAction action, long version, double now)
        {
            //変更:受理した行動で正式な盤面勝利が確定した後にだけ保存する。
            //変更:ローカルの自動スキップはControllerの待機判定を通したTickだけで進める。
            var response = Authority.Submit(actor, Request(actor, action, version, false), now, allowAutomaticSkip: false);
            if (response.Accepted)
            {
                progress?.RecordResult(Setup.Difficulty, Authority.State, Human);
            }
            return response;
        }

        public MatchResponse Surrender(long version, double now)
        {
            //変更:NPCの待機中でも提示した版で降参でき、保留中スキップを取り消す。
            return Authority.Submit(Human, Request(Human, null, version, true), now, allowAutomaticSkip: false);
        }

        private MatchRequest Request(PlayerId actor, GameAction action, long version, bool surrender)
        {
            if (actor != Human && actor != Npc)
            {
                throw new ArgumentOutOfRangeException(nameof(actor));
            }
            ulong id = ++requests[(int)actor];
            if (surrender)
            {
                return new MatchRequest(Authority.MatchId, id, version, MatchOperation.Surrender);
            }
            if (action is AttackAction attack)
            {
                return new MatchRequest(Authority.MatchId, id, version, MatchOperation.Attack,
                    (int)attack.Source, (int)attack.Target);
            }
            if (action is RedistributeAction distribution)
            {
                return new MatchRequest(Authority.MatchId, id, version, MatchOperation.Redistribute,
                    left: distribution.Left, right: distribution.Right);
            }
            if (action is UseCardAction card)
            {
                return new MatchRequest(Authority.MatchId, id, version, MatchOperation.UseCard,
                    target: card.Target.HasValue ? (int)card.Target.Value : -1,
                    cardId: card.CardId, swapValues: card.SwapValues);
            }
            //追加:人間とNPCのスキップも版・要求IDを伴う正式要求へ通す。
            if (action is SkipAction)
            {
                return new MatchRequest(Authority.MatchId, id, version, MatchOperation.Skip);
            }
            throw new ArgumentException("A supported action is required.", nameof(action));
        }
    }
}
