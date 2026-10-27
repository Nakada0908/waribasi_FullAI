//変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
using TMPro;
using UnityEngine;
using Waribasi.MatchFlow;
using Waribasi.Networking;
using Waribasi.Rules;

public class MyTurnTMP : MonoBehaviour
{
    private TextMeshProUGUI text;

    private void Awake()
    {
        text = GetComponent<TextMeshProUGUI>();
    }

    private void Update()
    {
        var view = MatchClientState.Instance;
        if (view == null || view.Public == null)
        {
            text.text = "対戦準備中";
            return;
        }
        var state = view.Public;
        if (view.CommunicationInterrupted || state.matchPhase == (int)MatchPhase.Interrupted)
        {
            text.text = "通信中断";
        }
        else if (state.matchPhase == (int)MatchPhase.Finished)
        {
            text.text = state.winner == view.LocalPlayer ? "勝利" : "敗北";
        }
        else if (state.matchPhase == (int)MatchPhase.Preparing)
        {
            text.text = state.currentPlayer == view.LocalPlayer ? "あなたが先手・対戦準備中" : "相手が先手・対戦準備中";
        }
        else if (state.gate == (int)InputGate.Presenting)
        {
            text.text = "行動を反映中";
        }
        else
        {
            text.text = state.currentPlayer == view.LocalPlayer ? "自分のターン" : "相手のターン";
        }
    }
}

//削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
//using TMPro;
//using UnityEngine;
//using System.Collections;
//
//public class MyTurnTMP : MonoBehaviour
//{
//    private PlayerStatus ps;
//    TextMeshProUGUI tmp;
//
//    //スポーンして見つかるまで続けられるようにする
//    IEnumerator Start()
//    {
//        tmp = GetComponent<TextMeshProUGUI>();
//
//        //自分がオーナーの PlayerStatus を探す
//        while (ps == null)
//        {
//            foreach (var status in FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None))
//            {
//                if (status.IsOwner)
//                {
//                    ps = status;
//                    break;
//                }
//            }
//            yield return null;
//        }
//
//    }
//
//    void Update()
//    {
//        if (ps == null) return;
//
//        if (ps.myturn.Value)
//        {
//            tmp.text = "自分のターン";
//        }
//        else
//        {
//            tmp.text = "相手のターン";
//        }
//    }
//}
