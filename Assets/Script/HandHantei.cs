//変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using Waribasi.Networking;

public class HandHantei : NetworkBehaviour
{
    public NetworkObject hand1, hand2;
    private MatchClientState view;

    public override void OnNetworkSpawn()
    {
        view = MatchClientState.Instance;
        if (view != null)
        {
            view.Changed += OnStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (view != null)
        {
            view.Changed -= OnStateChanged;
        }
        ResetSelect();
        base.OnNetworkDespawn();
    }

    private void OnStateChanged()
    {
        ResetSelect();
    }

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null)
        {
            return false;
        }
        if (Input.touchCount > 0)
        {
            return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
        }
        return EventSystem.current.IsPointerOverGameObject();
    }

    private void Update()
    {
        //追加:OnNetworkSpawnの順序に依存せず、遅れて作られた本人観測へ接続する。
        if (view == null && MatchClientState.Instance != null)
        {
            view = MatchClientState.Instance;
            view.Changed += OnStateChanged;
        }
        if (view == null || !view.CanAct || IsPointerOverUI() || !Input.GetMouseButtonDown(0) || Camera.main == null)
        {
            return;
        }
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit))
        {
            return;
        }
        var hand = hit.collider.GetComponentInParent<NetworkObject>();
        if (hand == null || !hand.CompareTag("Hand"))
        {
            return;
        }
        if (hand1 == null)
        {
            if (!hand.IsOwner)
            {
                return;
            }
            hand1 = hand;
            hand1.GetComponentInChildren<HandHighlight>()?.SetHighlight(true);
            return;
        }
        if (hand.OwnerClientId == hand1.OwnerClientId)
        {
            ResetSelect();
            return;
        }
        hand2 = hand;
        MatchNetworkController.Instance?.SendAttack(hand1, hand2);
        ResetSelect();
    }

    private void ResetSelect()
    {
        if (hand1 != null)
        {
            hand1.GetComponentInChildren<HandHighlight>()?.SetHighlight(false);
        }
        hand1 = null;
        hand2 = null;
    }
}

//削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
//using System.Collections;//IEnumerator（コルーチン）を使うために必要
//using Unity.Netcode;
//using UnityEngine;
//using UnityEngine.EventSystems; //UIクリックの貫通防止に必要
//
//public class HandHantei : NetworkBehaviour
//{
//    public NetworkObject hand1, hand2;
//    PlayerStatus mystatus;
//
//    // 入力ロック用（ローカル専用）
//    bool syorinow = false;
//
//    public override void OnNetworkSpawn()
//    {
//        //自分のPlayerStatusを取得するため全部探すまで繰り返す
//        //FindObjectsByTypeはPlayerStatusをすべて探す
//        foreach (var status in FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None))
//        {
//            //ステータスに設定したクライアントIDと自分のIDが同じなら自分のステータス
//            if (status.OwnerClientId == NetworkManager.Singleton.LocalClientId)
//            {
//                mystatus = status;
//                break;
//            }
//        }
//    }
//
//    //PCのマウス操作とスマホのタッチ操作の両方でUI貫通を防ぐ
//    private bool IsPointerOverUI()
//    {
//        //スマホなどのタッチ操作が検出された場合
//        if (Input.touchCount > 0)
//        {
//            //タッチしている最初の指(0番)のIDを使ってUI上かどうかを判定する
//            return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
//        }
//
//        //PCなどのマウス操作の場合
//        //マウスカーソルがUI要素の上にある場合は、これ以降の処理を中断する
//        //Raycast Target」がオンになっている物の上にUIあるときキャンセルされる
//        return EventSystem.current.IsPointerOverGameObject();
//    }
//
//    void Update()
//    {
//        //シーン内のPlayerStatusの数を数え、2人未満(相手がいない)なら操作させない
//        if (FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None).Length < 2) return;
//
//        //PCとスマホの両方に対応した自作のUI貫通防止
//        if (IsPointerOverUI()) return;
//
//        if (syorinow) return;//処理中は操作不可
//        FindMyStatus();
//        if (mystatus == null) return;//エラー回避
//        //権限を付与された人だけが操作可能
//        if (!mystatus.myturn.Value) { return; }
//
//        if (Input.GetMouseButtonDown(0)) //左クリック時
//        {
//            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
//            if (!Physics.Raycast(ray, out RaycastHit hit)) return;
//
//            NetworkObject hand = hit.collider.GetComponentInParent<NetworkObject>();
//            if (hand == null) return;
//
//            //選択された手のハイライト用スクリプトを取得
//            HandHighlight handHighlight = hand.GetComponentInChildren<HandHighlight>();
//
//            if (hand1 == null)
//            {
//                //自分の手のみ選択可能にする
//                if (!hand.IsOwner) { return; }
//                hand1 = hand;
//                Debug.Log("1つ目選択: " + hand.name);
//
//                //1つ目の手をハイライト（オン）
//                if (handHighlight != null) handHighlight.SetHighlight(true);
//            }
//            else
//            {
//                // 同じオーナーの手を選んだら即リセット
//                if (hand.OwnerClientId == hand1.OwnerClientId)
//                {
//                    Debug.Log("同じ手を選択したためリセット");
//
//                    //リセット前に1つ目の手のハイライトをオフにする
//                    HandHighlight h1Highlight = hand1.GetComponentInChildren<HandHighlight>();
//                    if (h1Highlight != null) h1Highlight.SetHighlight(false);
//
//                    hand1 = null;
//                    hand2 = null;
//                    return;
//                }
//
//                hand2 = hand;
//                Debug.Log("2つ目選択: " + hand.name);
//
//                // 入力ロック
//                syorinow = true;
//
//                // 判定処理をサーバーに依頼
//                RequestJudgeServerRpc(hand1.NetworkObjectId, hand2.NetworkObjectId);
//            }
//        }
//    }
//
//    //旧型式[ServerRpc(RequireOwnership = false)] //サーバーで処理
//    //serverrpcの新型式、誰でも呼び出せるように //クライアント → サーバー
//    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
//    void RequestJudgeServerRpc(ulong myHandId, ulong enemyHandId)
//    {
//        NetworkObject myHand = NetworkManager.Singleton.SpawnManager.SpawnedObjects[myHandId];
//        NetworkObject enemyHand = NetworkManager.Singleton.SpawnManager.SpawnedObjects[enemyHandId];
//
//        //コルーチンを開始
//        //（プログラムの実行を途中で一時停止（中断）し、後でその続きから再開できる）
//        StartCoroutine(MoveHand(myHand, enemyHand));
//    }
//
//    //IEnumeratorは途中で止まることのできる関数的なもの、コルーチン
//    //これやんないと１フレームで動いてワープしちゃう
//    IEnumerator MoveHand(NetworkObject myHand, NetworkObject enemyHand)
//    {
//        //位置リセット用に情報を保持しておく
//        Vector3 resetPos = myHand.transform.position;
//        float speed = 10.0f;//移動速度
//
//        //２つのオブジェクトの距離を求めて接触するまで近づける
//        Transform mytarget = myHand.transform.Find("targetpos");
//        Transform enemytarget = enemyHand.transform.Find("targetpos");
//
//        //移動方向を正規化(.normalized)して、モデルが激しく振動したりワープするバグ（オーバーシュート）を防止
//        while (Vector3.Distance(mytarget.position, enemytarget.position) > 0.1f)
//        {
//            //方向ベクトルに.normalizedを付けて長さを1に固定し、常に一定の速度で移動させる
//            Vector3 dir = (enemytarget.position - mytarget.position).normalized;
//            myHand.transform.position += dir * speed * Time.deltaTime;
//            yield return null;//１フレーム待つ
//        }
//
//        Debug.Log("接触！");
//
//        //元の位置に戻る
//        while (Vector3.Distance(myHand.transform.position, resetPos) > 0.01f)
//        {
//            myHand.transform.position = Vector3.MoveTowards(//目標オブジェクトに向かって動く
//                myHand.transform.position,//現在地
//                resetPos,//ターゲットの座標,元の位置に戻す
//                speed * Time.deltaTime//速度
//            );
//            yield return null;
//        }
//
//        //判定処理
//        //指の本数を加算する
//        AddFinger.Instance.Judge(myHand, enemyHand);
//        //手の見た目を変更する
//        ChangeHand.Instance.Anime(myHand, enemyHand);
//
//        //５本の場合手を画面外に移動させる
//        var handRL = enemyHand.GetComponentInParent<HandRL>();
//        var status = FindPlayer(enemyHand.OwnerClientId);
//
//        bool isFive = false;
//
//        if (handRL.handType == HandType.Right)
//        {
//            isFive = (status.Ryubi.Value == 5);
//        }
//        else if (handRL.handType == HandType.Left)
//        {
//            isFive = (status.Lyubi.Value == 5);
//        }
//
//        if (isFive)
//        {
//            Transform stayposobj = enemyHand.transform.Find("staypos");
//            Vector3 staypos = stayposobj.position;
//
//            while (Vector3.Distance(enemyHand.transform.position, staypos) > 0.1f)
//            {
//                enemyHand.transform.position = Vector3.MoveTowards(
//                    enemyHand.transform.position,
//                    staypos,
//                    speed * Time.deltaTime
//                );
//                yield return null;
//            }
//        }
//        //ターン切り替え
//        ChangeTurn(myHand.OwnerClientId);
//        //選択のリセット
//        EndSelectResetClientRpc();
//        //５５になったかを知らべる
//        Syouhai.Instance.Isfive();
//    }
//
//    void FindMyStatus()
//    {
//        if (mystatus != null) return;
//
//        foreach (var status in FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None))
//        {
//            if (status.OwnerClientId == NetworkManager.Singleton.LocalClientId)
//            {
//                mystatus = status;
//                Debug.Log("PlayerStatus取得成功");
//                break;
//            }
//        }
//    }
//
//    void ChangeTurn(ulong currentPlayerId)
//    {
//        var players = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);
//
//        foreach (var p in players)
//        {
//            if (p.OwnerClientId == currentPlayerId)
//            {
//                p.myturn.Value = false;
//            }
//            else
//            {
//                p.myturn.Value = true;
//            }
//        }
//    }
//
//    [ClientRpc]//サーバーからクライアントへ
//    void EndSelectResetClientRpc()
//    {
//        syorinow = false; // ローカル解除
//        ResetSelect();
//    }
//
//    void ResetSelect()
//    {
//        //リセット時に両方の手のハイライトを確実にオフにする
//        if (hand1 != null)
//        {
//            HandHighlight h1Highlight = hand1.GetComponentInChildren<HandHighlight>();
//            if (h1Highlight != null) h1Highlight.SetHighlight(false);
//        }
//
//        hand1 = null;
//        hand2 = null;
//        Debug.Log("選択リセット");
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
