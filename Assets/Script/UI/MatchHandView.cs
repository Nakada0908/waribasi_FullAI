using Unity.Netcode;
using UnityEngine;
using Waribasi.Networking;
using Waribasi.Rules;

namespace Waribasi.Presentation
{
    public sealed class MatchHandView : MonoBehaviour
    {
        private bool local;
        private PlayerId localPlayer;
        private HandSide localSide;
        private NetworkObject networkHand;
        private HandRL handSide;
        public Vector3 Home { get; private set; }
        public NetworkObject NetworkHand => networkHand;
        public HandSide Side => local ? localSide : MatchClientState.ToSide(handSide.handType);

        private void Awake()
        {
            networkHand = GetComponent<NetworkObject>();
            handSide = GetComponent<HandRL>();
        }

        public void BindLocal(PlayerId player, HandSide side)
        {
            local = true;
            localPlayer = player;
            localSide = side;
            Home = transform.position;
        }

        public bool TryGetPlayer(out PlayerId player)
        {
            return ResolvePlayer(MatchSessionAccess.State, out player);
        }

        public bool ResolvePlayer(MatchClientState view, out PlayerId player)
        {
            player = localPlayer;
            if (local)
            {
                return MatchSessionAccess.IsLocal;
            }
            networkHand = networkHand != null ? networkHand : GetComponent<NetworkObject>();
            handSide = handSide != null ? handSide : GetComponent<HandRL>();
            var state = view?.Public;
            if (networkHand == null || handSide == null || state == null)
            {
                return false;
            }
            if (networkHand.OwnerClientId == state.playerAClientId)
            {
                player = PlayerId.PlayerA;
                return true;
            }
            if (networkHand.OwnerClientId == state.playerBClientId)
            {
                player = PlayerId.PlayerB;
                return true;
            }
            return false;
        }
    }
}
