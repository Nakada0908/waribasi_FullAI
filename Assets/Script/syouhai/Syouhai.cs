using Unity.Netcode;
using UnityEngine;

public class Syouhai : NetworkBehaviour
{
    public static Syouhai Instance;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        Instance = this;
    }

    public void Isfive()
    {
        PlayerStatus[] players = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);

        //各プレイヤーのステータスを取得
        foreach (var ps in players)
        {
            // 両手が5本になったら負け
            if (ps.Ryubi.Value == 5 && ps.Lyubi.Value == 5)
            {
                GoEnd(ps); // 負けたプレイヤーを渡す
                break;
            }
        }
    }

    private void GoEnd(PlayerStatus loser)
    {
        PlayerStatus[] players = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);

        foreach (var ps in players)
        {
            // 負けた本人ならLose、それ以外ならWin
            if (ps == loser)
            {
                ps.myResult.Value = Winner.Lose;
                Debug.Log("負けたプレイヤー: " + ps.name);
            }
            else
            {
                ps.myResult.Value = Winner.Win;
                Debug.Log("勝ったプレイヤー: " + ps.name);
            }
        }

        if (IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene("End", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}
