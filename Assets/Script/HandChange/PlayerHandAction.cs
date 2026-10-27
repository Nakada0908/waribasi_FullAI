//変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
using Unity.Netcode;
using UnityEngine;
using Waribasi.Networking;

[RequireComponent(typeof(PlayerStatus))]
public class PlayerHandAction : NetworkBehaviour
{
    //変更:旧RPCを登録せず、ローカルの本人操作を新しい要求へ変換する。
    public void RequestHandChange(int left, int right)
    {
        if (IsOwner)
        {
            MatchNetworkController.Instance?.SendRedistribution(left, right);
        }
    }
}

//削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
//using Unity.Netcode;
//using UnityEngine;
//
//[RequireComponent(typeof(PlayerStatus))] // 付け忘れ防止
//public class PlayerHandAction : NetworkBehaviour
//{
//    private PlayerStatus ps;
//
//    private void Awake()
//    {
//        ps = GetComponent<PlayerStatus>();
//    }
//
//    [Rpc(SendTo.Server)]
//    public void HandChangeServerRpc(int Lhand, int Rhand, RpcParams rpcParams = default)
//    {
//        //自分のターンじゃないなら操作を受け付けない
//        if (!ps.myturn.Value) return;
//
//        //OwnerClientId で自分のIDを取得
//        ulong myClientId = OwnerClientId;
//
//        NetworkObject hand1 = null, hand2 = null;
//
//        //手のオブジェクトを取得
//        foreach (var h in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
//        {
//            if (h.CompareTag("Hand") && h.OwnerClientId == myClientId)
//            {
//                if (hand1 == null)
//                {
//                    hand1 = h;
//                }
//                else
//                {
//                    hand2 = h; break;
//                }
//            }
//        }
//
//        int yubigoukei = ps.Sumyubi;
//
//        //片方5本だとそれも足しちゃって正常に処理できてないから、その分引いておく
//        if (ps.Ryubi.Value == 5 || ps.Lyubi.Value == 5)
//        {
//            yubigoukei -= 5;
//        }
//
//        //合計値チェック（チート防止）
//        if (yubigoukei == Rhand + Lhand)
//        {
//            //手をもとの位置に戻す
//            MoveHukkatu(hand1, hand2);
//
//            ps.Ryubi.Value = Rhand;
//            ps.Lyubi.Value = Lhand;
//
//            //手の見た目を変更する
//            ChangeHand.Instance.Anime(hand1, hand2);
//        }
//    }
//
//    private void MoveHukkatu(NetworkObject hand1, NetworkObject hand2)
//    {
//        if (hand1 == null || hand2 == null) return;
//
//        //どちらか５本だった時、手をもとの位置に戻す
//        if (ps.Ryubi.Value == 5)
//        {
//            //手が右か左かをゲットする
//            var LRhand1 = hand1.GetComponentInParent<HandRL>().handType;
//            var LRhand2 = hand2.GetComponentInParent<HandRL>().handType;
//
//            if (LRhand1 == HandType.Right) hand1.GetComponent<HandPos>().ResetPosition();
//            else if (LRhand2 == HandType.Right) hand2.GetComponent<HandPos>().ResetPosition();
//        }
//
//        if (ps.Lyubi.Value == 5)
//        {
//            var LRhand1 = hand1.GetComponentInParent<HandRL>().handType;
//            var LRhand2 = hand2.GetComponentInParent<HandRL>().handType;
//
//            if (LRhand1 == HandType.Left) hand1.GetComponent<HandPos>().ResetPosition();
//            else if (LRhand2 == HandType.Left) hand2.GetComponent<HandPos>().ResetPosition();
//        }
//    }
//}
