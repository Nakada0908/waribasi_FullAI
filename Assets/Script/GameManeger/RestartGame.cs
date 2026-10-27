//変更:再戦・タイトル復帰・終了を対戦フローへ接続する。
using UnityEngine;
using Waribasi.Presentation;

public class RestartGame : MonoBehaviour
{
    private void Update()
    {
        if (GameInputRouter.RestartPressed && !GameInputRouter.IsEditingText &&
            MatchFlowPresenter.Instance != null && !MatchFlowPresenter.Instance.ModalOpen)
        {
            MatchFlowPresenter.Instance.Rematch();
        }
    }

    public void OnClickRestartButton()
    {
        MatchFlowPresenter.Instance?.ReturnTitle();
    }

    public void OnClickQuitGameButton()
    {
        MatchFlowPresenter.Instance?.ConfirmQuit();
    }
}

//削除:T05移行前の実装とコメントを参照用に保存する。
//using UnityEngine;
//using UnityEngine.SceneManagement;
//using Unity.Netcode;
//using System.Collections;
//
//public class RestartGame : MonoBehaviour
//{
//    //フリーズ防止のため、連打できないようにするフラグ
//    private bool isDisconnecting = false;
//
//    //変更:既存のタイトル復帰処理をInput SystemのSpace入力へ接続する。
//    void Update()
//    {
//        if (Waribasi.Presentation.GameInputRouter.RestartPressed &&
//            !Waribasi.Presentation.GameInputRouter.IsEditingText && !isDisconnecting)
//        {
//            StartCoroutine(SafeDisconnectAndLoad());
//        }
//    }
//
////削除:従来の処理は参照用に保存する。
////    void Update()
////    {
////        //Spaceキーが押され、かつ現在切断処理中でない場合
////        if (Input.GetKeyDown(KeyCode.Space) && !isDisconnecting)
////        {
////            StartCoroutine(SafeDisconnectAndLoad());
////        }
////    }
//
//    //モバイルの「タイトルに戻る」ボタンのOnClickに設定する
//    public void OnClickRestartButton()
//    {
//        //切断処理中でなければコルーチンを開始する
//        if (!isDisconnecting)
//        {
//            //タイトルへ戻る処理を呼び出す
//            StartCoroutine(SafeDisconnectAndLoad());
//        }
//    }
//
//    //モバイルの「ゲーム終了」ボタンのOnClickに設定する
//    public void OnClickQuitGameButton()
//    {
//        if (!isDisconnecting)
//        {
//            Application.Quit();
//        }
//    }
//
//    //安全に切断してからシーンを移動するためのコルーチン
//    private IEnumerator SafeDisconnectAndLoad()
//    {
//        isDisconnecting = true;
//
//        if (NetworkManager.Singleton != null)
//        {
//            //まず通信のシャットダウンを命令する
//            NetworkManager.Singleton.Shutdown();
//
//            //裏側の片付けが終わるまで少し待つ（フリーズ対策）
//            yield return new WaitForSeconds(0.5f);
//
//            //片付けが終わったら、古いNetworkManagerを破壊する
//            Destroy(NetworkManager.Singleton.gameObject);
//        }
//
//        //安全にシーンを移動する
//        SceneManager.LoadScene("Title");
//    }
//}
