using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class ConfirmCancelUI : MonoBehaviour
{
    public static ConfirmCancelUI Instance;

    PlayerStatus ps;

    [Header("確定とキャンセルのUI全体(パネルなど)")]
    public GameObject uiPanel;

    [Header("それぞれのボタン")]
    public Button confirmButton;
    public Button cancelButton;

    void Awake()
    {
        Instance = this;

        //最初は非表示にしておく
        uiPanel.SetActive(false);

        //ボタンにメソッドを登録(インスペクターでの設定忘れ防止)
        confirmButton.onClick.AddListener(OnConfirmPressed);
        cancelButton.onClick.AddListener(OnCancelPressed);
    }

    private void Update()
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
        uiPanel.SetActive(true);
    }

    //UIを非表示にする
    public void DeleteUI()
    {
        uiPanel.SetActive(false);
    }

    //確定ボタンが押された時の処理
    private void OnConfirmPressed()
    {
        //ネットワーク上の「自分のプレイヤーオブジェクト」を探す
        if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
            var cardAction = localPlayer.GetComponent<CardAction>();

            //プレイヤーのCardActionに「確定したよ」と伝える
            cardAction.OnClickConfirm();
        }
    }

    //キャンセルボタンが押された時の処理
    private void OnCancelPressed()
    {
        //ネットワーク上の「自分のプレイヤーオブジェクト」を探す
        if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
            var cardAction = localPlayer.GetComponent<CardAction>();

            //プレイヤーのCardActionに「キャンセルしたよ」と伝える
            cardAction.OnClickCancel();
        }
    }
}