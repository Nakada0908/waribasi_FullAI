//変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
using TMPro;
using Unity.Netcode;
using UnityEngine;
using Waribasi.Networking;

public class ResultUI : NetworkBehaviour
{
    public TextMeshProUGUI resultText;

    private void Start()
    {
        var view = MatchClientState.Instance;
        if (view == null || !view.HasConfirmedResult)
        {
            resultText.text = "通信中断";
            resultText.color = Color.white;
            return;
        }
        bool won = view.Public.winner == view.LocalPlayer;
        resultText.text = won ? "勝利！" : "敗北……";
        resultText.color = won ? Color.red : Color.blue;
    }
}

//削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
//using TMPro;
//using Unity.Netcode;
//using UnityEngine;
//
//public class ResultUI : NetworkBehaviour
//{
//    public TextMeshProUGUI resultText;
//
//    void Start()
//    {
//        PlayerStatus[] allPlayers = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);
//
//        foreach (var ps in allPlayers)
//        {
//            // 「自分のデータ」かつ「ネットワーク経由で存在している」場合
//            if (ps.IsOwner)
//            {
//                if (ps.myResult.Value == Winner.Win)
//                {
//                    resultText.text = "勝利！";
//                    resultText.color = Color.red;
//                }
//                else
//                {
//                    resultText.text = "敗北……";
//                    resultText.color = Color.blue;
//                }
//            }
//        }
//    }
//}
