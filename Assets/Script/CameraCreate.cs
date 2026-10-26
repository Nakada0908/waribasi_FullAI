using Unity.Netcode;
using UnityEngine;

public class CameraCreate : NetworkBehaviour
{
    public Camera maincamera;
    public Camera subcamera;

    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            Camera cam = Instantiate(maincamera);
            cam.transform.position = new Vector3(1.5f, 3.0f, 2.0f); //(0, 5.5f, -4.5f);
            cam.transform.rotation = Quaternion.Euler(50, 0, 0);
        }
        else
        {
            CreateClientCamera();
        }
    }

    void CreateClientCamera()
    {
        Camera cam = Instantiate(subcamera);
        cam.transform.position = new Vector3(1.5f, 3.0f, 8.0f); //(0, 5.5f, 9.5f);
        cam.transform.rotation = Quaternion.Euler(50, 180, 0);
    }
}
