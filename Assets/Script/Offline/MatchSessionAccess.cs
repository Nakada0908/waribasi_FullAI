using UnityEngine;
using Waribasi.MatchFlow;
using Waribasi.Networking;
using Waribasi.Rules;

namespace Waribasi.Presentation
{
    public static class MatchSessionAccess
    {
        public static LocalMatchController Local => LocalMatchController.Instance;
        public static bool IsLocal => Local != null && Local.Active;
        public static MatchClientState State => IsLocal ? Local.View : MatchClientState.Instance;

        public static bool Send(GameAction action, long version, MatchHandView source = null, MatchHandView target = null)
        {
            if (IsLocal)
            {
                return Local.Send(action, version);
            }
            var network = MatchNetworkController.Instance;
            if (network == null)
            {
                return false;
            }
            if (action is AttackAction)
            {
                return source != null && target != null && network.SendAttack(source.NetworkHand, target.NetworkHand);
            }
            if (action is UseCardAction card)
            {
                return network.SendCard(card.CardId, card.Target, card.SwapValues, version);
            }
            //追加:条件付きスキップをオンラインの正式要求へ渡す。
            if (action is SkipAction)
            {
                return network.SendSkip(version);
            }
            return action is RedistributeAction distribution && network.SendRedistribution(distribution.Left, distribution.Right);
        }

        public static bool Surrender()
        {
            return IsLocal ? Local.Surrender() : MatchNetworkController.Instance != null && MatchNetworkController.Instance.SendSurrender();
        }

        public static void Leave(string destination)
        {
            if (IsLocal)
            {
                Local.Leave(destination);
            }
            else
            {
                OnlineSessionController.Ensure().RequestLeave(destination);
            }
        }
    }
}
