//変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
using Unity.Netcode;

public class Syouhai : NetworkBehaviour
{
    public static Syouhai Instance;

    private void Awake()
    {
        Instance = this;
    }

    //変更:勝敗とScene遷移は共通結果を受領する通信進行側が一度だけ処理する。
    public void Isfive()
    {
    }
}

//削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
//using Unity.Netcode;
//using UnityEngine;
//
//public class Syouhai : NetworkBehaviour
//{
//    public static Syouhai Instance;
//
//    // Start is called once before the first execution of Update after the MonoBehaviour is created
//    void Awake()
//    {
//        Instance = this;
//    }
//
//    public void Isfive()
//    {
//        PlayerStatus[] players = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);
//
//        //各プレイヤーのステータスを取得
//        foreach (var ps in players)
//        {
//            // 両手が5本になったら負け
//            if (ps.Ryubi.Value == 5 && ps.Lyubi.Value == 5)
//            {
//                GoEnd(ps); // 負けたプレイヤーを渡す
//                break;
//            }
//        }
//    }
//
//    private void GoEnd(PlayerStatus loser)
//    {
//        PlayerStatus[] players = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);
//
//        foreach (var ps in players)
//        {
//            // 負けた本人ならLose、それ以外ならWin
//            if (ps == loser)
//            {
//                ps.myResult.Value = Winner.Lose;
//                Debug.Log("負けたプレイヤー: " + ps.name);
//            }
//            else
//            {
//                ps.myResult.Value = Winner.Win;
//                Debug.Log("勝ったプレイヤー: " + ps.name);
//            }
//        }
//
//        if (IsServer)
//        {
//            NetworkManager.Singleton.SceneManager.LoadScene("End", UnityEngine.SceneManagement.LoadSceneMode.Single);
//        }
//    }
//}
