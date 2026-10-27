using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
//追加:接続後に共通の対局設定を渡す。
using Waribasi.Networking;

public class RelayMatchingManager : MonoBehaviour
{
    //接続状況やエラー原因を表示するためのテキスト
    public TMP_Text statusText;

    //現在マッチング処理が進行中かどうかを判定するフラグ
    private bool isConnecting = false;
    //自分がホストとして作成したロビーのIDを保持
    private string currentLobbyId;
    //ロビーが自動消滅しないように定期連絡（ハートビート）を送るタイマー
    private float heartbeatTimer;

    private async void Start()
    {
        if (statusText != null)
        {
            statusText.text = "初期化中...";
        }

        try
        {
            //同一PCで複数起動しても別アカウントとして認識されるよう、完全にランダムなIDを生成
            InitializationOptions options = new InitializationOptions();
            options.SetProfile("Player_" + Guid.NewGuid().ToString().Substring(0, 8));

            //Unityサービスの初期化
            await UnityServices.InitializeAsync(options);

            //匿名ログインの実行
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            if (statusText != null)
            {
                statusText.text = "準備完了。ボタンを押してマッチング開始";
            }
        }
        catch (Exception e)
        {
            //変更:例外本文に接続情報が含まれる可能性があるため種類だけ記録する。
            Debug.LogError(e.GetType().Name);
            if (statusText != null)
            {
                statusText.text = "初期化に失敗しました";
            }
        }
    }

    //ホスト時のハートビート送信
    private async void Update()
    {
        if (NetworkManager.Singleton.IsHost && currentLobbyId != null)
        {
            heartbeatTimer += Time.deltaTime;

            //15秒ごとにサーバーへ「まだロビーは生きている」と報告
            if (heartbeatTimer > 15f)
            {
                heartbeatTimer = 0f;
                try
                {
                    await LobbyService.Instance.SendHeartbeatPingAsync(currentLobbyId);
                }
                catch (LobbyServiceException e)
                {
                    Debug.LogWarning(e.GetType().Name);
                }
            }
        }
    }

    //UIのボタンなどから呼び出す、全自動マッチング開始処理
    public async void StartMatchmaking()
    {
        //すでに処理中なら重複して実行しないように弾く
        if (isConnecting) { return; }

        isConnecting = true;

        if (statusText != null)
        {
            statusText.text = "空き部屋を検索中...";
        }

        try
        {
            //条件を指定せず、入れるロビー（空き部屋）を探して入室
            QuickJoinLobbyOptions quickJoinOptions = new QuickJoinLobbyOptions();
            Lobby joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync(quickJoinOptions);

            if (statusText != null)
            {
                statusText.text = "部屋を発見。接続処理中...";
            }

            //ロビーに登録されていた合言葉を取り出し、Relayサーバーへ接続
            string joinCode = joinedLobby.Data["JoinCode"].Value;
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            //Netcodeの通信設定にRelayの情報をセットし、クライアントとして開始
            //WebGLビルドかつエディタではない場合
//変更:全経路で通信方式とUseWebSocketsを一致させる。
            RelayTransportSetup.Client(joinAllocation);
//削除:旧プラットフォーム分岐。
//#if UNITY_WEBGL && !UNITY_EDITOR
////WebGL用(wss)
//            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "wss"));
//#else
//            //エディタやWindowsビルド用(dtls)
//            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));
//#endif
            //変更:開始失敗は例外復帰経路へ戻す。
            if (!NetworkManager.Singleton.StartClient())
            {
                throw new InvalidOperationException("Client start failed.");
            }

            if (statusText != null)
            {
                statusText.text = "接続処理中（クライアント）";
            }

            isConnecting = false;
        }
        catch (LobbyServiceException)
        {
            //空き部屋が見つからなかった場合は、自分がホストになって部屋を作る
            if (statusText != null)
            {
                statusText.text = "空き部屋なし。新規部屋を作成します...";
            }
            CreateNewLobbyAndHost();
        }
        catch (Exception e)
        {
            //通信エラーなどの予期せぬエラーは画面のテキストに直接表示する
            //変更:例外本文に接続情報が含まれる可能性があるため種類だけ記録する。
            Debug.LogError(e.GetType().Name);
            if (statusText != null)
            {
                statusText.text = "接続に失敗しました";
            }
            isConnecting = false;
        }
    }

    //自分がホストとなって新規ロビーを作成する処理
    private async void CreateNewLobbyAndHost()
    {
        try
        {
            //自分＋相手（計2人）のRelay通信枠を確保し、合言葉を取得
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            //誰でも入れる公開設定にし、合言葉をデータとして持たせる
            CreateLobbyOptions lobbyOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { "JoinCode", new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
                }
            };

            //ロビーサーバー上に最大2人の部屋を作成し、IDを記録
            Lobby createdLobby = await LobbyService.Instance.CreateLobbyAsync("MatchLobby", 2, lobbyOptions);
            currentLobbyId = createdLobby.Id;

            //Netcodeの通信設定に自分のRelay情報をセット
            //WebGLビルドかつエディタではない場合
//変更:全経路で通信方式とUseWebSocketsを一致させる。
            RelayTransportSetup.Host(allocation);
//削除:旧プラットフォーム分岐。
//#if UNITY_WEBGL && !UNITY_EDITOR
////WebGL用(wss)
//            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "wss"));
//#else
//            //エディタやWindowsビルド用(dtls)
//            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
//#endif

            //接続審査ルールの設定（これがないとクライアントが接続を拒否されてしまう）
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;

            //ホストとして開始
            //変更:ランダム対戦は先手ランダム・毎手番30秒を固定する。
            MatchNetworkController.ConfigureNextMatch(true);
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            if (!NetworkManager.Singleton.StartHost())
            {
                throw new InvalidOperationException("Host start failed.");
            }

            if (statusText != null)
            {
                statusText.text = "部屋を作成しました。対戦相手を待機中...";
            }

            //誰かが接続してきたときに呼ばれるイベントを登録
        }
        catch (Exception e)
        {
            //変更:例外本文に接続情報が含まれる可能性があるため種類だけ記録する。
            Debug.LogError(e.GetType().Name);
            if (statusText != null)
            {
                statusText.text = "部屋を作成できませんでした";
            }
            isConnecting = false;
        }
    }

    //クライアントの接続を許可するかどうかの審査処理
    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.Pending = true;

        //変更:試合開始後の新規参加と途中再接続は受け付けない。
        if (NetworkManager.Singleton.ConnectedClients.Count >= 2 || SceneManager.GetActiveScene().name != "StartGame")
        {
            response.Approved = false;
            response.Pending = false;
            return;
        }

        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Pending = false;
    }

    //ホストの部屋にクライアントが接続してきた際の処理
    private void OnClientConnected(ulong clientId)
    {
        //ホスト自身であり、かつ接続人数が2人（自分と相手）に達したか確認
        if (NetworkManager.Singleton.IsHost && NetworkManager.Singleton.ConnectedClients.Count >= 2)
        {
            //イベントの登録を解除
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

            if (statusText != null)
            {
                statusText.text = "マッチング成功（ホスト）。ゲームを開始します";
            }

            //マッチングが成立したので、不要になったロビーを破棄して他人が入ってこないようにする
            DeleteLobby();

            //ホスト権限でGameシーンへ同期遷移する処理を追加
            NetworkManager.Singleton.SceneManager.LoadScene("Game", LoadSceneMode.Single);
        }
    }

    //不要になったロビーをサーバー上から削除する処理
    private async void DeleteLobby()
    {
        if (currentLobbyId != null)
        {
            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(currentLobbyId);
                currentLobbyId = null;
                Debug.Log("ロビーを破棄しました");
            }
            catch (Exception e)
            {
                Debug.LogWarning(e.GetType().Name);
            }
        }
    }

    //スクリプトが破棄されたとき（ゲーム終了時など）の安全対策
    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        //ホストが強制終了した際などに、残ったロビーのゴミを消す
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            DeleteLobby();
        }
    }
}