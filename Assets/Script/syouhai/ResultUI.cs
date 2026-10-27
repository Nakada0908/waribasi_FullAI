//変更:受信済みの正式結果だけを表示し、後続の切断でも維持する。
using TMPro;
using Unity.Netcode;
using UnityEngine;
using Waribasi.Networking;

public class ResultUI : NetworkBehaviour
{
    public TextMeshProUGUI resultText;
    private MatchClientState view;

    private void Start()
    {
        //変更:ローカル対戦も同じ確定勝敗を表示する。
        view = Waribasi.Presentation.MatchSessionAccess.State;
        if (view != null)
        {
            view.Changed += Refresh;
        }
        Refresh();
    }

    public override void OnDestroy()
    {
        if (view != null)
        {
            view.Changed -= Refresh;
        }
        base.OnDestroy();
    }

    private void Refresh()
    {
        if (view == null || !view.HasConfirmedResult)
        {
            resultText.text = "通信中断";
            //変更:明るい結果背景でも通信中断を読める色にする。
            resultText.color = new Color(0.12f, 0.18f, 0.23f);
            //削除:従来の白文字。
            //resultText.color = Color.white;
            return;
        }
        bool won = view.Public.winner == view.LocalPlayer;
        resultText.text = won ? "勝利！" : "敗北……";
        resultText.color = won ? Color.red : Color.blue;
    }
}

//削除:T05移行前の実装とコメントを参照用に保存する。
////変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
//using TMPro;
//using Unity.Netcode;
//using UnityEngine;
//using Waribasi.Networking;
//
//public class ResultUI : NetworkBehaviour
//{
//    public TextMeshProUGUI resultText;
//
//    private void Start()
//    {
//        var view = MatchClientState.Instance;
//        if (view == null || !view.HasConfirmedResult)
//        {
//            resultText.text = "通信中断";
//            resultText.color = Color.white;
//            return;
//        }
//        bool won = view.Public.winner == view.LocalPlayer;
//        resultText.text = won ? "勝利！" : "敗北……";
//        resultText.color = won ? Color.red : Color.blue;
//    }
//}
//
////削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
////using TMPro;
////using Unity.Netcode;
////using UnityEngine;
////
////public class ResultUI : NetworkBehaviour
////{
////    public TextMeshProUGUI resultText;
////
////    void Start()
////    {
////        PlayerStatus[] allPlayers = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);
////
////        foreach (var ps in allPlayers)
////        {
////            // 「自分のデータ」かつ「ネットワーク経由で存在している」場合
////            if (ps.IsOwner)
////            {
////                if (ps.myResult.Value == Winner.Win)
////                {
////                    resultText.text = "勝利！";
////                    resultText.color = Color.red;
////                }
////                else
////                {
////                    resultText.text = "敗北……";
////                    resultText.color = Color.blue;
////                }
////            }
////        }
////    }
////}

//削除:T06の共通入力・ローカル接続へ移行する前の箇所を参照用に保存する。
//
//        view = MatchClientState.Instance;
