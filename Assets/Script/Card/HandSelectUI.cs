using UnityEngine;
using Unity.Netcode;

public class HandSelectUI : MonoBehaviour
{
    public static HandSelectUI Instance;

    PlayerStatus ps;

    private void Awake()
    {
        //ゲーム開始時に自分自身を登録して、一旦非表示にする
        Instance = this;
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (ps == null)
        {
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.LocalClient != null &&
                NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                ps = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerStatus>();
            }
            return; //取得できるまで何もしない
        }

        if (!ps.myturn.Value)
        {
            DeleteUI();
        }
    }

    //UIを表示する
    public void ShowUI()
    {
        gameObject.SetActive(true);
    }
    //UIを消す
    public void DeleteUI()
    {
        gameObject.SetActive(false);
    }

    public void OnClickLeftButton()
    {
        SendHandChoiceToServer(TargetHandType.Left);
    }

    public void OnClickRightButton()
    {
        SendHandChoiceToServer(TargetHandType.Right);
    }

    private void SendHandChoiceToServer(TargetHandType handType)
    {
        // NetworkManagerを使って「ローカルプレイヤー（自分）」のオブジェクトを取得
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        // 自分のプレイヤースクリプト（CardManagerなど）を取得
        var myPlayerScript = localPlayer.GetComponent<CardAction>();

        //直接サーバーに送るのではなく、CardAction側に「手が選ばれた」ことを伝えて確認待ちにする
        myPlayerScript.OnHandSelected(handType);
    }
}