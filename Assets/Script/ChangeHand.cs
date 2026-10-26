using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

public class ChangeHand : NetworkBehaviour
{
    public static ChangeHand Instance;

    void Awake()
    {
        Instance = this;
    }

    //指の本数変更後、見た目を更新
    public void Anime(NetworkObject myHand, NetworkObject enemyHand)
    {
        UpdateOneHand(myHand);
        UpdateOneHand(enemyHand);
    }

    void UpdateOneHand(NetworkObject hand)
    {
        //どっちの誰の手でステータスを取得
        var handRL = hand.GetComponentInParent<HandRL>();
        var owner = hand.GetComponent<HandOwner>();
        var player = FindPlayer(owner.OwnerClientId.Value);

        //自分の手の指の本数を取得
        int fingerCount;
        if (handRL.handType == HandType.Right)
        { 
            fingerCount = player.Ryubi.Value; 
        }
        else
        { 
            fingerCount = player.Lyubi.Value;
        }

        //手それぞれにアニメーションの設定が必要だからスクリプトを分けて、取得する
        var controller = hand.GetComponentInChildren<HandFingerController>();
        if (controller != null)
        {
            //見た目の変更を行う
            controller.SetFinger(fingerCount);
        }
    }

    PlayerStatus FindPlayer(ulong clientId)
    {
        foreach (var p in FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None))
        {
            if (p.OwnerClientId == clientId) return p;
        }
        return null;
    }
}
