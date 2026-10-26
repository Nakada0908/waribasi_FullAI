using System.Collections.Generic;

namespace Waribasi.Rules
{
    //正式エンジン・相手の非公開状態・正式な乱数への参照を保持しない。
    public sealed class PlayerObservation
    {
        public PlayerId Player { get; }
        public GameState PublicState { get; }
        public IReadOnlyList<HandCard> OwnHand => Cards.Hand;
        public bool RevivalAcquired => Cards.RevivalAcquired;
        internal PrivateCards Cards { get; }

        internal PlayerObservation(PlayerId player, GameState publicState, PrivateCards cards)
        {
            Player = player;
            PublicState = publicState;
            Cards = cards;
        }
    }
}
