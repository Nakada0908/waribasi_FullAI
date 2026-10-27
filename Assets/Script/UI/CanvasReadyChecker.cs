//変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
using UnityEngine;
using Waribasi.Networking;

[RequireComponent(typeof(CanvasGroup))]
public class CanvasReadyChecker : MonoBehaviour
{
    public static bool IsGameReady;
    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        SetReady(false);
    }

    private void Update()
    {
        SetReady(MatchClientState.Instance != null && MatchClientState.Instance.CanAct);
    }

    //変更:履歴閲覧を止めず、ゲーム操作の可否は各操作ボタンで制御する。
    private void SetReady(bool ready)
    {
        IsGameReady = ready;
        canvasGroup.interactable = true;
        canvasGroup.alpha = 1;
    }

//削除:従来の処理は参照用に保存する。
//    private void SetReady(bool ready)
//    {
//        IsGameReady = ready;
//        canvasGroup.interactable = ready;
//        canvasGroup.alpha = ready ? 1 : 0.5f;
//    }
}

//削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
//using UnityEngine;
//
////CanvasGroupがアタッチされていない場合は自動で追加する
//[RequireComponent(typeof(CanvasGroup))]
//public class CanvasReadyChecker : MonoBehaviour
//{
//    //他のスクリプトからも「ゲーム開始状態か」を確認できるようにしておく
//    public static bool IsGameReady = false;
//
//    CanvasGroup canvasGroup;
//
//    void Awake()
//    {
//        IsGameReady = false;
//        canvasGroup = GetComponent<CanvasGroup>();
//
//        //対戦相手が来るまではCanvas内の全ボタンを押せなくする
//        canvasGroup.interactable = false;
//
//        //必要であれば見た目を少し暗くして「待機中」感を出す
//        canvasGroup.alpha = 0.5f;
//    }
//
//    void Update()
//    {
//        //プレイヤーが2人揃っているかチェック
//        if (FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None).Length < 2)
//        {
//            return;//揃っていなければ何もしない
//        }
//
//        //2人揃ったらフラグをtrueにする
//        IsGameReady = true;
//
//        //Canvas内のボタン操作を有効化する
//        canvasGroup.interactable = true;
//        canvasGroup.alpha = 1.0f;//透明度を戻す
//
//        //監視が終わったので、このスクリプト自体のUpdateを停止する
//        this.enabled = false;
//    }
//}
