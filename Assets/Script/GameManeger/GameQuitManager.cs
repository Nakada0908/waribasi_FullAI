//変更:Escapeは入力欄・モーダル・未確定選択・画面ごとの戻るの順に扱う。
using UnityEngine;
using Waribasi.Presentation;

public class GameQuitManager : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        var manager = new GameObject("GameQuitManager");
        manager.AddComponent<GameQuitManager>();
        DontDestroyOnLoad(manager);
    }

    private void LateUpdate()
    {
        if (!GameInputRouter.EscapePressed || GameInputRouter.EscapeReservedForText)
        {
            return;
        }
        var interaction = MatchInteractionController.Instance;
        if (MatchFlowPresenter.Instance != null && MatchFlowPresenter.Instance.ModalOpen)
        {
            MatchFlowPresenter.Instance.HandleEscape();
        }
        else if (interaction != null && interaction.HistoryOpen)
        {
            MatchHudPresenter.Instance?.ShowHistory(false);
        }
        else if (interaction != null && interaction.HasSelection)
        {
            interaction.CancelSelection();
        }
        else if (MatchSetupPresenter.Instance != null)
        {
            MatchSetupPresenter.Instance.Back();
        }
        else if (MatchFlowPresenter.Instance != null)
        {
            MatchFlowPresenter.Instance.HandleEscape();
        }
        else
        {
            Application.Quit();
        }
    }
}

//削除:T05移行前の実装とコメントを参照用に保存する。
//using UnityEngine;
//
//public class GameQuitManager : MonoBehaviour
//{
//    //ゲーム起動時（最初のシーンが読み込まれる直前）に自動で呼ばれるアタッチしなくていい！すご！
//    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
//    private static void Initialize()
//    {
//        //空のゲームオブジェクトを自動生成し、自分自身をアタッチする
//        GameObject manager = new GameObject("GameQuitManager");
//        manager.AddComponent<GameQuitManager>();
//
//        //シーンが切り替わってもこのオブジェクトが破壊されないようにする
//        DontDestroyOnLoad(manager);
//    }
//
//    //変更:UIの取消と入力欄の編集を、既存の全体終了より優先する。
//    void LateUpdate()
//    {
//        if (!Waribasi.Presentation.GameInputRouter.EscapePressed ||
//            Waribasi.Presentation.GameInputRouter.EscapeReservedForText)
//        {
//            return;
//        }
//        var interaction = Waribasi.Presentation.MatchInteractionController.Instance;
//        if (interaction != null && interaction.HistoryOpen)
//        {
//            Waribasi.Presentation.MatchHudPresenter.Instance?.ShowHistory(false);
//        }
//        else if (interaction != null && interaction.HasSelection)
//        {
//            interaction.CancelSelection();
//        }
//        else
//        {
//            Application.Quit();
//        }
//    }
//
////削除:従来の処理は参照用に保存する。
////    void Update()
////    {
////        //Escキーが押されたら終了処理を呼ぶ、Androidの戻るボタン（KeyCode.Escape）にも対応
////        if (Input.GetKeyDown(KeyCode.Escape))
////        {
////            Application.Quit();
////        }
////    }
//}
