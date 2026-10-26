namespace Waribasi.Rules
{
    public abstract class GameAction
    {
    }

    //追加:内容は所持札から解決し、要求には札IDと対象・割り当てだけを載せる。
    public sealed class UseCardAction : GameAction
    {
        public long CardId { get; }
        public HandSide? Target { get; }
        public bool SwapValues { get; }

        public UseCardAction(long cardId, HandSide? target = null, bool swapValues = false)
        {
            CardId = cardId;
            Target = target;
            SwapValues = swapValues;
        }
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
