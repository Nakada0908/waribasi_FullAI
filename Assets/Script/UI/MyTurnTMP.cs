using TMPro;
using UnityEngine;
using System.Collections;

public class MyTurnTMP : MonoBehaviour
{
    private PlayerStatus ps;
    TextMeshProUGUI tmp;

    //スポーンして見つかるまで続けられるようにする
    IEnumerator Start()
    {
        tmp = GetComponent<TextMeshProUGUI>();

        //自分がオーナーの PlayerStatus を探す
        while (ps == null)
        {
            foreach (var status in FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None))
            {
                if (status.IsOwner)
                {
                    ps = status;
                    break;
                }
            }
            yield return null;
        }

    }

    void Update()
    {
        if (ps == null) return;

        if (ps.myturn.Value)
        {
            tmp.text = "自分のターン";
        }
        else
        {
            tmp.text = "相手のターン";
        }
    }
}