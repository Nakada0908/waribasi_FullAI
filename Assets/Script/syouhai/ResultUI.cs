using TMPro;
using Unity.Netcode;
using UnityEngine;

public class ResultUI : NetworkBehaviour
{
    public TextMeshProUGUI resultText;

    void Start()
    {
        PlayerStatus[] allPlayers = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);

        foreach (var ps in allPlayers)
        {
            // 「自分のデータ」かつ「ネットワーク経由で存在している」場合
            if (ps.IsOwner)
            {
                if (ps.myResult.Value == Winner.Win)
                {
                    resultText.text = "勝利！";
                    resultText.color = Color.red;
                }
                else
                {
                    resultText.text = "敗北……";
                    resultText.color = Color.blue;
                }
            }
        }
    }
}