//変更:スロット番号を共通の選択窓口へ渡す。
using UnityEngine;
using UnityEngine.UI;
using Waribasi.Presentation;

public class SendCardButton : MonoBehaviour
{
    public int myIndex;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClickThisButton);
    }

    private void OnClickThisButton()
    {
        MatchInteractionController.Instance?.SelectCard(myIndex);
    }
}

//削除:T04移行前の実装を参照用に保存する。
//using Unity.Netcode;
//using UnityEngine;
//using UnityEngine.UI;
//
//public class SendCardButton : MonoBehaviour
//{
//    [Header("このボタンのインデックス (0~4)")]
//    public int myIndex; //インスペクターから 0, 1, 2, 3, 4 をそれぞれ設定する
//
//    Button button;
//
//    void Awake()
//    {
//        //自分のオブジェクトについているButtonコンポーネントを取得
//        button = GetComponent<Button>();
//
//        //ボタンが押された時の処理を登録
//        //ここに書くことでヒューマンエラー回避やチーム開発などでのミスを減らすことができる
//        button.onClick.AddListener(OnClickThisButton);
//    }
//
//    private void OnClickThisButton()
//    {
//        //自分のローカルプレイヤーのオブジェクトを探す
//        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
//        //ローカルプレイヤーについている CardAction スクリプトを取得
//        var cardAction = localPlayer.GetComponent<CardAction>();
//
//        //自分のインデックス（0〜4）を渡して、プレイヤー側の処理を呼び出す
//        cardAction.OnCardSelected(myIndex);
//    }
//}
