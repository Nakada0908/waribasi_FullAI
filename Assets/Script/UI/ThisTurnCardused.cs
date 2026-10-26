using TMPro;
using Unity.Netcode;
using UnityEngine;
using static Unity.Burst.Intrinsics.X86.Avx;

public class ThisTurnCardused : MonoBehaviour
{
    CardAction cardAction;
    TextMeshProUGUI tmp;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        cardAction = localPlayer.GetComponent<CardAction>();

        tmp = GetComponent<TextMeshProUGUI>();
    }

    // Update is called once per frame
    void Update()
    {
        if(cardAction.turnused.Value == true)
        {
            tmp.text = "カードを使ったよ";
        }
        else
        {
            tmp.text = "カードを使ってないよ";
        }
    }
}
