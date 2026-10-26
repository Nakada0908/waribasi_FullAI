using Unity.Netcode;
using UnityEngine;

public class HandPos : NetworkBehaviour
{
    public Vector3 resetPos;

    public override void OnNetworkSpawn()
    {
        resetPos = transform.position;
    }

    public void ResetPosition()
    {
        transform.position = resetPos;
    }
}
