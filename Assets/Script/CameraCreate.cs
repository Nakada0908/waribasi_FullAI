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
            //追加:画面比率が変わっても、左右の手を含む盤面全体を表示する。
            Waribasi.Presentation.MatchCameraViewport.Attach(cam);
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
        //追加:参加側も同じ画面比率と安全領域で盤面を表示する。
        Waribasi.Presentation.MatchCameraViewport.Attach(cam);
    }
}
