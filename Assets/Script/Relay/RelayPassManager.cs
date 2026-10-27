//追加:接続開始の失敗も同じ復帰経路で扱う。
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Services.Core; //Unityのクラウドサービス本体を使うために必要
using Unity.Services.Authentication; //匿名ログインなどの認証機能を使うために必要
using Unity.Services.Relay; //Relayサーバーに接続するために必要
using Unity.Services.Relay.Models; //Allocationなど、Relay専用のデータ型を使うために必要
using Unity.Netcode.Transports.UTP; //Netcodeの通信設定をRelay用の上書きするために必要
using TMPro;
//追加:接続後に共通の対局設定を渡す。
using Waribasi.Networking;

public class RelayPassManager : MonoBehaviour
{
    public TMP_Text joinCodeText;//合言葉を画面に出す用
    public TMP_InputField joinCodeInput;//相手が合言葉を打ち込む用
    //ロード中やエラーなどの状況を表示するためのテキスト
    public TMP_Text statusText;

    //ボタンの連打（多重処理）を防ぐためのストッパー変数
    bool isConnecting = false;
    //追加:T05の設定UIから指定できる8択。試合開始後は進行側へ固定値を渡す。
    public FirstPlayerChoice firstPlayer = FirstPlayerChoice.Random;
    public RoomTurnLimit turnLimit = RoomTurnLimit.ThirtySeconds;

    async void Start()//非同期処理(await)を使えるようにasyncをつける
    {
        //初期設定
        Application.targetFrameRate = 60;
        Screen.SetResolution(1280, 720, false);

        //左向きを有効にする
        Screen.autorotateToLandscapeLeft = true;
        //右向きを有効にする
        Screen.autorotateToLandscapeRight = true;
        //スマホを縦に持っても縦画面にならないようにストッパーをかける
        Screen.autorotateToPortrait = false;
        //縦画面の上下逆さまも無効化する
        Screen.autorotateToPortraitUpsideDown = false;
        //画面の向きを自動回転に設定する
        Screen.orientation = ScreenOrientation.AutoRotation;

        //初期状態では合言葉を空にする
        joinCodeText.text = "";

        //ゲーム起動直後のログイン中もテキストを出しておく
        if (statusText != null) statusText.text = "サーバーに接続中...";

        await UnityServices.InitializeAsync();//Unityクラウドサービスの初期化が終わるまで「待つ」
        if (!AuthenticationService.Instance.IsSignedIn)//もしログインしていなければ
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();//匿名ログインが完了するまで「待つ」
        }

        //初期化が終わったらテキストを準備完了にする
        if (statusText != null) statusText.text = "準備完了！";
    }

    public async void StartHost()
    {
        //すでに処理中ならこの先の処理を行わずに弾く
        if (isConnecting) return;
        //処理を開始した印をつける
        isConnecting = true;

        //ボタンを押した直後にロード中表示にする
        if (statusText != null) statusText.text = "部屋を作成中...少し待ってね";

        try
        {
            //自分以外の参加者を最大1人に設定して部屋を確保する
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
            //その部屋専用の合言葉（JoinCode）を取得する
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            //部屋作成が終わったらテキストを更新して相手を待つ
            if (statusText != null) statusText.text = "相手の参加を待っています...";
            //取得した合言葉をここで初めて画面に表示する
            if (joinCodeText != null) joinCodeText.text = "合言葉: " + joinCode;

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

            //相手が入ってきたときの審査ルール（ApprovalCheck）を設定する
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
            //Netcodeのホストとして起動する
            //変更:ホスト自身の接続通知も受けられる順序にする。
            MatchNetworkController.ConfigureNextMatch(false, firstPlayer, (int)turnLimit);
            NetworkManager.Singleton.OnClientConnectedCallback -= CheckPlayersAndStartGame;
            NetworkManager.Singleton.OnClientConnectedCallback += CheckPlayersAndStartGame;
            if (!NetworkManager.Singleton.StartHost())
            {
                throw new InvalidOperationException("Host start failed.");
            }
            //クライアントが接続してきたらCheckPlayersAndStartGameを実行するように予約する
        }
        catch (Exception e)
        {
            Debug.Log("接続エラー: " + e.GetType().Name);

            //エラーが起きたら画面にも出す
            if (statusText != null) statusText.text = "接続失敗。合言葉を確認してね";
            //エラーが起きた場合は再度ボタンを押せるようにストッパーを解除する
            isConnecting = false;
        }
    }

    private void CheckPlayersAndStartGame(ulong clientId)
    {
        //自分がホストで、かつ接続人数が2人（自分＋相手）になったら
        if (NetworkManager.Singleton.IsHost && NetworkManager.Singleton.ConnectedClients.Count >= 2)
        {
            //何度も呼ばれないように予約を解除する
            NetworkManager.Singleton.OnClientConnectedCallback -= CheckPlayersAndStartGame;
            //Netcodeの機能を使って「Game」シーンへ全員で移動する
            NetworkManager.Singleton.SceneManager.LoadScene("Game", LoadSceneMode.Single);
        }
    }

    public async void StartClient()
    {
        //入力欄が空っぽならここで処理を止める
        if (joinCodeInput == null || string.IsNullOrEmpty(joinCodeInput.text)) return;
        //前後の見えない空白や改行をTrim()で削ぎ落とし、さらに入力をすべて大文字(ToUpper)に強制変換する
        string inputCode = joinCodeInput.text.Trim().ToUpper();

        //すでに処理中ならこの先の処理を行わずに弾く
        if (isConnecting) return;
        //処理を開始した印をつける
        isConnecting = true;

        //参加ボタンを押した直後にロード中表示にする
        if (statusText != null) statusText.text = "部屋に接続中...少し待ってね";

        try
        {
            //入力された合言葉を使ってRelayサーバーに入室リクエストを送る
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(inputCode);

            //変更:ルーム参加もWebGLではwssを使う。
            RelayTransportSetup.Client(joinAllocation);
//削除:dtls固定の旧参加処理。
//            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(//Netcodeの通信設定を
//                AllocationUtils.ToRelayServerData(joinAllocation, "dtls")//接続先（ホスト）のRelayサーバーの情報で上書きする
//            );

            //Netcodeのクライアントとして起動する
            //変更:開始の戻り値を接続完了とは扱わない。
            NetworkManager.Singleton.OnClientConnectedCallback -= OnLocalConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnLocalConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnLocalDisconnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnLocalDisconnected;
            if (!NetworkManager.Singleton.StartClient())
            {
                throw new InvalidOperationException("Client start failed.");
            }
            //接続がうまくいったらテキストを更新
            if (statusText != null) statusText.text = "接続処理中...";
        }
        catch (Exception e)
        {
            Debug.Log("接続エラー: " + e.GetType().Name);
            //エラーが起きたら画面にも出す
            if (statusText != null) statusText.text = "接続失敗。合言葉を確認してね";
            //エラーが起きた場合は再度ボタンを押せるようにストッパーを解除する
            isConnecting = false;
        }
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        //追加の承認手順が必要な場合は、追加の手順が完了するまでこれを true に設定します
        //true から false に遷移すると、接続承認応答が処理されます
        response.Pending = true;

        //最大人数をチェック(この場合は2人まで)
        //変更:試合開始後の新規参加と途中再接続は受け付けない。
        if (NetworkManager.Singleton.ConnectedClients.Count >= 2 || SceneManager.GetActiveScene().name != "StartGame")
        {
            response.Approved = false;//接続を許可しない
            response.Pending = false;
            return;
        }
        //ここからは接続成功クライアントに向けた処理
        response.Approved = true;//接続を許可

        //PlayerObjectを生成するかどうか
        response.CreatePlayerObject = true;

        response.Pending = false;
    }

    //追加:開始要求の成功と、実際の接続完了を区別する。
    private void OnLocalConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId && statusText != null)
        {
            statusText.text = "接続完了。対戦準備中...";
        }
    }

    private void OnLocalDisconnected(ulong clientId)
    {
        isConnecting = false;
        if (statusText != null)
        {
            statusText.text = "接続が終了しました";
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= CheckPlayersAndStartGame;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnLocalConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnLocalDisconnected;
        }
    }
}