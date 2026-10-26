using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CreateHand : NetworkBehaviour
{
    public NetworkObject RHandPrefab;
    public NetworkObject LHandPrefab;

    //ネットワークのIDに使ってるulong型のリスト
    List<ulong> playerIDs = new List<ulong>();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // 既に接続しているプレイヤー（ホスト含む）
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            SetPlayerID(client.ClientId);
        }

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
        }
    }

    void OnClientConnected(ulong clientId)
    {
        SetPlayerID(clientId);
    }

    void SetPlayerID(ulong clientId)
    {
        //再接続や重複防止、既にいる場合は処理しない
        if (playerIDs.Contains(clientId)) return;
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
        r.SpawnWithOwnership(clientId);

        float Lx = 3f * Mathf.Abs(handIndexL - playerIndex);

        //左手の生成
        NetworkObject l = Instantiate(LHandPrefab);
        l.transform.position = new Vector3(Lx, 0, playerIndex * 10);
        l.transform.rotation = new Quaternion(0, 180 * playerIndex, 0, 0);
        l.SpawnWithOwnership(clientId);

        //手の所有者を自分に設定
        r.GetComponent<HandOwner>().OwnerClientId.Value = clientId;
        l.GetComponent<HandOwner>().OwnerClientId.Value = clientId;

        //ホストに初めに操作権を与える
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        PlayerStatus status = localPlayer.GetComponent<PlayerStatus>();
        if (clientId == NetworkManager.ServerClientId)
        {
            status.myturn.Value = true;
        }
    }
}
