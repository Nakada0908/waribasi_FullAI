//変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
using Unity.Netcode;

public class AddFinger : NetworkBehaviour
{
    public static AddFinger Instance;

    private void Awake()
    {
        Instance = this;
    }

    //変更:旧呼出しが残っても本数を書き換えない。正式な攻撃は新窓口だけで受け付ける。
    public void Judge(NetworkObject myHand, NetworkObject enemyHand)
    {
    }
}

//削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
//using Unity.Netcode;
//using UnityEngine;
//
//public class AddFinger : NetworkBehaviour
//{
//    public static AddFinger Instance;
//
//    void Awake()
//    {
//        Instance = this;
//    }
//
//    public void Judge(NetworkObject myHand, NetworkObject enemyHand)
//    {
//        //手が右か左かをゲットする
//        var mytype = myHand.GetComponentInParent<HandRL>().handType;
//        var enemytype = enemyHand.GetComponentInParent<HandRL>().handType;
//        //自分と相手のステータスを取得
//        //まず、そのオブジェクトの所有者のクライアントIDを取得
//        ulong myOwnerId = myHand.GetComponent<HandOwner>().OwnerClientId.Value;
//        ulong enemyOwnerId = enemyHand.GetComponent<HandOwner>().OwnerClientId.Value;
//        //クライアントIDからプレイヤーステータスを取得
//        var myPlayer = FindPlayer(myOwnerId);
//        var enemyPlayer = FindPlayer(enemyOwnerId);
//
//        //自分の手の指の本数を取得
//        int myfinger;
//        if (mytype==HandType.Right)
//        {
//            myfinger= myPlayer.Ryubi.Value;
//        }
//        else
//        {
//            myfinger = myPlayer.Lyubi.Value;
//        }
//
//        //相手の手に自分の指の本数を加算
//        if (enemytype == HandType.Right)
//        {
//            enemyPlayer.Ryubi.Value += myfinger;
//            //５本を超えたら余りの数にする
//            if (enemyPlayer.Ryubi.Value > 5)
//            {
//                enemyPlayer.Ryubi.Value %= 5;
//            }
//        }
//        else//左手
//        {
//            enemyPlayer.Lyubi.Value += myfinger;
//            if (enemyPlayer.Lyubi.Value > 5)
//            {
//                enemyPlayer.Lyubi.Value %= 5;
//            }
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
