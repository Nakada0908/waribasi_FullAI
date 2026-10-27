using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
//追加:双方のSceneロード完了を待って手を生成する。
using UnityEngine.SceneManagement;

public class CreateHand : NetworkBehaviour
{
    public NetworkObject RHandPrefab;
    public NetworkObject LHandPrefab;

    //ネットワークのIDに使ってるulong型のリスト
    List<ulong> playerIDs = new List<ulong>();
    //追加:接続済みでもGameの読み込み完了前には手を生成しない。
    private bool sceneReady;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        //変更:読み込み中の接続側で手が旧Sceneと一緒に消えないよう、双方の完了を待つ。
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnGameLoaded;
        //削除:SceneのSpawn直後に生成していた処理を参照用に保存する。
//        // 既に接続しているプレイヤー（ホスト含む）
//        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
//        {
//            SetPlayerID(client.ClientId);
//        }

        //ネットワークマネージャーを１回介して接続イベントを受け取る
        //クライアントIDを受け取る
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    //抜けた人用にIDリストから消す処理
    //親のOnDestroyを無視するためにoverrideしている
    public override void OnDestroy()
    {
        //親クラスの終了処理も必ず呼ぶ
        //でもまずは、親が元々持っている大事な掃除処理を先にやる
        base.OnDestroy();

        //NetworkManagerが生きている時だけ、イベントの登録（+=）を解除（-=）する
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            //追加:Scene終了時にロード完了の購読も解除する。
            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnGameLoaded;
            }
        }
    }

    //追加:Gameを双方が読み込んだ後に、各席の左右を一度だけ生成する。
    private void OnGameLoaded(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
    {
        if (!IsServer || sceneName != "Game" || completed.Count != 2 || timedOut.Count != 0)
        {
            return;
        }
        sceneReady = true;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            SetPlayerID(client.ClientId);
        }
    }

    void OnClientConnected(ulong clientId)
    {
        SetPlayerID(clientId);
    }

    void SetPlayerID(ulong clientId)
    {
        //再接続や重複防止、既にいる場合は処理しない
        //変更:2席を超える生成と重複を拒否する。
        //変更:ロード完了前の通知も生成に使わない。
        if (!sceneReady || playerIDs.Contains(clientId) || playerIDs.Count >= 2)
        {
            return;
        }
        //リストに追加
        playerIDs.Add(clientId);
        //リストの位置から何人目のプレイヤーかを取得
        int playerIndex = playerIDs.IndexOf(clientId);
        //IDを使って手を生成
        CreatePlayerHand(clientId, playerIndex);
    }

    void CreatePlayerHand(ulong clientId, int playerIndex)
    {
        int handIndexR = 1;
        int handIndexL = 0; 

        float Rx = 3f * Mathf.Abs(handIndexR - playerIndex);

        //右手の生成
        NetworkObject r = Instantiate(RHandPrefab);
        r.transform.position = new Vector3(Rx, 0, playerIndex * 10);
        r.transform.rotation = new Quaternion(0, 180 * playerIndex, 0, 0);
        //clientIdの物（各プレイヤーのもの）
        //変更:対戦画面の手をSceneと同時に破棄し、再戦で二重生成しない。
        r.SpawnWithOwnership(clientId, true);
        //削除:Sceneをまたいで残る既定の生成。
        //r.SpawnWithOwnership(clientId);

        float Lx = 3f * Mathf.Abs(handIndexL - playerIndex);

        //左手の生成
        NetworkObject l = Instantiate(LHandPrefab);
        l.transform.position = new Vector3(Lx, 0, playerIndex * 10);
        l.transform.rotation = new Quaternion(0, 180 * playerIndex, 0, 0);
        //変更:対戦画面の手をSceneと同時に破棄し、再戦で二重生成しない。
        l.SpawnWithOwnership(clientId, true);
        //削除:Sceneをまたいで残る既定の生成。
        //l.SpawnWithOwnership(clientId);

        //手の所有者を自分に設定
        r.GetComponent<HandOwner>().OwnerClientId.Value = clientId;
        l.GetComponent<HandOwner>().OwnerClientId.Value = clientId;

        //ホストに初めに操作権を与える
//削除:先手は通信進行側の確定設定から共通ルールへ渡す。
//        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
//        PlayerStatus status = localPlayer.GetComponent<PlayerStatus>();
//        if (clientId == NetworkManager.ServerClientId)
//        {
//            status.myturn.Value = true;
//        }
    }
}
