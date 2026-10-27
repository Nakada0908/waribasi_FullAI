//変更:スマホWebでも必須ボタンを隠さず、キー説明だけを補助表示する。
using UnityEngine;
using UnityEngine.InputSystem;

public class PlatformVisibility : MonoBehaviour
{
    public GameObject PCText;
    public GameObject MBRestart;
    public GameObject MBFinish;

    private void Start()
    {
        PCText.SetActive(Keyboard.current != null);
        MBRestart.SetActive(true);
        MBFinish.SetActive(true);
    }
}

//削除:T04移行前の実装を参照用に保存する。
//using UnityEngine;
//
//public class PlatformUIController : MonoBehaviour
//{
//    //PC用の操作説明テキスト（TMPオブジェクト）を割り当てる
//    public GameObject PCText;
//    //スマホ用の操作ボタンオブジェクトを割り当てる
//    public GameObject MBRestart;
//    public GameObject MBFinish;
//
//    void Start()
//    {
//        //メイン環境であるPC（モバイル以外）かどうかを最初に判定する
//        if (!Application.isMobilePlatform)
//        {
//            //PC環境なら操作テキストを表示し、スマホ用ボタンを非表示にする
//            PCText.SetActive(true);
//
//            MBRestart.SetActive(false);
//            MBFinish.SetActive(false);
//        }
//        //スマホ（iOS/Androidなど）環境の場合
//        else
//        {
//            //スマホ環境なら操作テキストを非表示にし、スマホ用ボタンを表示する
//            PCText.SetActive(false);
//
//            MBRestart.SetActive(true);
//            MBFinish.SetActive(true);
//        }
//    }
//}
