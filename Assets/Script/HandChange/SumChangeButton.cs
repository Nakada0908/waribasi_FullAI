using Unity.Netcode;
using UnityEngine;

public class SumChangeButton : MonoBehaviour
{
    PlayerHandAction pha;

    //11、12、13、22、14、23、24，33の統合、分裂を可能にするボタンを実装する
    public void Sum11()
    {
        SendHandChangeRequest(1, 1);
    }

    public void Sum12()
    {
        SendHandChangeRequest(1, 2);
    }

    public void Sum13()
    {
        SendHandChangeRequest(1, 3);
    }

    public void Sum22()
    {
        SendHandChangeRequest(2, 2);
    }

    public void Sum14() 
    {
        SendHandChangeRequest(1, 4);
    }

    public void Sum23()
    {
        SendHandChangeRequest(2, 3);
    }

    public void Sum24()
    {
        SendHandChangeRequest(2, 4);
    }

    public void Sum33()
    {
        SendHandChangeRequest(3, 3);
    }

    private void SendHandChangeRequest(int lHand, int rHand)
    {
        // 自分のクライアントIDを取得
        ulong myClientId = NetworkManager.Singleton.LocalClientId;

        //シーン内の全PlayerStatusから、自分（LocalClientId）の持ち物を探す
        foreach (var p in FindObjectsByType<PlayerHandAction>(FindObjectsSortMode.None))
        {
            if (p.OwnerClientId == myClientId)
            {
                pha = p;
                break;
            }
        }

        //変更:旧RPCを経由せず、本人の新しい要求へ転送する。
        if (pha != null)
        {
            pha.RequestHandChange(lHand, rHand);
        }
    }
}
