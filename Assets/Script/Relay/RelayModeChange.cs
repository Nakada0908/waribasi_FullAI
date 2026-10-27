//変更:旧2モード切替の入口を3モード選択画面へ接続する。
using UnityEngine;
using UnityEngine.UI;
using Waribasi.Presentation;

public class RelayModeChange : MonoBehaviour
{
    public Canvas RelayPass;
    public Canvas RelayMatching;
    public GameObject RelayPassManager;
    public GameObject RelayMatchingManager;
    public Button changeButton;

    public void RelayModeChangeClick()
    {
        MatchSetupPresenter.Instance?.ShowMode();
    }

    public void LockChangeButton()
    {
        //変更:操作中の受付はセッションが管理し、旧ボタンを永久にロックしない。
    }
}

//削除:T05移行前の実装とコメントを参照用に保存する。
//using UnityEngine;
//using UnityEngine.UI;
//
//public class RelayModeChange : MonoBehaviour
//{
//    [Header("キャンバスを割り当てる")]
//    public Canvas RelayPass;
//    public Canvas RelayMatching;
//    [Header("relayがついてるオブジェクトを割り当てる")]
//    public GameObject RelayPassManager;
//    public GameObject RelayMatchingManager;
//    [Header("モード切り替えボタンを割り当てる")]
//    public Button changeButton;
//
//    void Start()
//    {
//        //初期状態は合言葉モードをON、マッチングモードをOFFにする
//        RelayPass.gameObject.SetActive(true);
//        RelayPassManager.SetActive(true);
//        RelayMatching.gameObject.SetActive(false);
//        RelayMatchingManager.SetActive(false);
//
//        //最初は切り替えボタンを押せる状態にする
//        changeButton.interactable = true;
//    }
//
//
//    public void RelayModeChangeClick()
//    {
//        //現在の合言葉モードのUIがONになっているかを取得し基準とする
//        bool isPassActive = RelayPass.gameObject.activeSelf;
//
//        //合言葉モードのUIを現在の逆（ONならOFF、OFFならON）にする
//        RelayPass.gameObject.SetActive(!isPassActive);
//        RelayPassManager.SetActive(!isPassActive);
//
//        //マッチングモードのUIは、合言葉モードと「常に真逆」の状態にする（ズレ防止）
//        RelayMatching.gameObject.SetActive(isPassActive);
//        RelayMatchingManager.SetActive(isPassActive);
//    }
//
//    //通信を開始した時に呼び出して、切り替えボタンを押せなくするメソッド
//    public void LockChangeButton()
//    {
//        changeButton.interactable = false;
//    }
//}
