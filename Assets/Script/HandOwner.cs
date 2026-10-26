using Unity.Netcode;
using UnityEngine;

public class HandOwner : NetworkBehaviour
{
    // 手の所有者のクライアントIDを管理するNetworkVariable
    //それぞれの手に持たせる
    public new NetworkVariable<ulong> OwnerClientId =
        new NetworkVariable<ulong>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
}
