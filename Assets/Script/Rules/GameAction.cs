namespace Waribasi.Rules
{
    public abstract class GameAction
    {
    }

    public sealed class AttackAction : GameAction
    {
        public HandSide Source { get; }
        public HandSide Target { get; }

        //攻撃先のプレイヤーは実行者の相手に固定する。
        public AttackAction(HandSide source, HandSide target)
        {
            Source = source;
            Target = target;
        }
    }

    public sealed class RedistributeAction : GameAction
    {
        public int Left { get; }
        public int Right { get; }

        public RedistributeAction(int left, int right)
        {
            Left = left;
            Right = right;
        }
    }
}
