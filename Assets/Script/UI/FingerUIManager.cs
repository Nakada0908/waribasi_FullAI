using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class FingerUIManager : MonoBehaviour
{
    public TMP_Text myYubi;//自分の指の数を表示するテキスト
    PlayerStatus myPlayer;
    public TMP_Text enemyYubi;//相手の指の数を表示するテキスト
    PlayerStatus enemyPlayer;

    IEnumerator Start()
    {
        while (myPlayer == null || enemyPlayer == null)
        {
            foreach (var status in FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None))
            {
                //自分自身（IsOwner）のスクリプトを捕獲する
                if (status.IsOwner)
                {
                    myPlayer = status;
                }
                else if (!status.IsOwner)
                {
                    enemyPlayer = status;
                }
            }
            yield return null;
        }
    }

    void Update()
    {
        myYubi.text = $"自分の指の数: " + myPlayer.Lyubi.Value + myPlayer.Ryubi.Value;
        enemyYubi.text = $"相手の指の数: " + enemyPlayer.Lyubi.Value + enemyPlayer.Ryubi.Value;
    }
}