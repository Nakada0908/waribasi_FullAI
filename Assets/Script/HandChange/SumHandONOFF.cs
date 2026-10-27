//変更:カード／振り分けの選択を共通管理へ接続する。
using TMPro;
using UnityEngine;
using Waribasi.Presentation;

public class SumHandONOFF : MonoBehaviour
{
    public GameObject SumHand;
    public GameObject HandCard;
    public TextMeshProUGUI ButtonTMP;

    public void OnButtonClick()
    {
        MatchInteractionController.Instance?.OpenRedistribution();
    }
}

//削除:T04移行前の実装を参照用に保存する。
//using TMPro;
//using UnityEngine;
//
//public class SumHandONOFF : MonoBehaviour
//{
//    public GameObject SumHand;//奇数の時
//    public GameObject HandCard;//偶数の時
//
//    public TextMeshProUGUI ButtonTMP;
//
//    int cnt = 0;
//
//    void Start()
//    {
//        //0の時はカードの選択
//        SumHand.SetActive(false);
//        HandCard.SetActive(true);
//        ButtonTMP.text = "手を変える";
//    }
//
//    public void OnButtonClick()
//    {
//        cnt++;
//
//        if (cnt % 2 == 1)
//        {
//            SumHand.SetActive(true);
//            HandCard.SetActive(false);
//            ButtonTMP.text = "カードを選択";
//        }
//        else
//        {
//
//            SumHand.SetActive(false);
//            HandCard.SetActive(true);
//            ButtonTMP.text = "手を変える";
//        }
//    }
//}
