//変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
using Unity.Netcode;
using Waribasi.Networking;

public class ChangeHand : NetworkBehaviour
{
    public static ChangeHand Instance;

    private void Awake()
    {
        Instance = this;
    }

    public void Anime(NetworkObject myHand, NetworkObject enemyHand)
    {
        MatchNetworkController.Instance?.RefreshHandPoses();
    }
}

//削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
//using Unity.Netcode;
//using UnityEngine;
//using UnityEngine.XR;
//
//public class ChangeHand : NetworkBehaviour
//{
//    public static ChangeHand Instance;
//
//    void Awake()
//    {
//        Instance = this;
//    }
//
//    //指の本数変更後、見た目を更新
//    public void Anime(NetworkObject myHand, NetworkObject enemyHand)
//    {
//        UpdateOneHand(myHand);
//        UpdateOneHand(enemyHand);
//    }
//
//    void UpdateOneHand(NetworkObject hand)
//    {
//        //どっちの誰の手でステータスを取得
//        var handRL = hand.GetComponentInParent<HandRL>();
//        var owner = hand.GetComponent<HandOwner>();
//        var player = FindPlayer(owner.OwnerClientId.Value);
//
//        //自分の手の指の本数を取得
//        int fingerCount;
//        if (handRL.handType == HandType.Right)
//        {
//            fingerCount = player.Ryubi.Value;
//        }
//        else
//        {
//            fingerCount = player.Lyubi.Value;
//        }
//
//        //手それぞれにアニメーションの設定が必要だからスクリプトを分けて、取得する
//        var controller = hand.GetComponentInChildren<HandFingerController>();
//        if (controller != null)
//        {
//            //見た目の変更を行う
//            controller.SetFinger(fingerCount);
//        }
//    }
//
//    PlayerStatus FindPlayer(ulong clientId)
//    {
//        foreach (var p in FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None))
//        {
//            if (p.OwnerClientId == clientId) return p;
//        }
//        return null;
//    }
//}
