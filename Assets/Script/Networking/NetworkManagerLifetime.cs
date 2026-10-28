using Unity.Netcode;
using UnityEngine;

namespace Waribasi.Networking
{
    internal static class NetworkManagerLifetime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            NetworkManager.OnInstantiated -= RemoveDuplicate;
            NetworkManager.OnInstantiated += RemoveDuplicate;
            Application.quitting -= Unsubscribe;
            Application.quitting += Unsubscribe;
        }

        private static void Unsubscribe()
        {
            NetworkManager.OnInstantiated -= RemoveDuplicate;
            Application.quitting -= Unsubscribe;
        }

        private static void RemoveDuplicate(NetworkManager candidate)
        {
            if (!Application.isPlaying || candidate == null)
            {
                return;
            }
            var current = NetworkManager.Singleton;
            if (current == null || current == candidate)
            {
                return;
            }
            //Awakeの通知で無効化し、NGOのOnEnableによる常駐化を防ぐ。
            candidate.gameObject.SetActive(false);
            Object.Destroy(candidate.gameObject);
        }
    }
}
