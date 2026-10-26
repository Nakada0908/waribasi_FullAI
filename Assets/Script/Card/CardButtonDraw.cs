using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class CardButtonDraw : MonoBehaviour
{
    public static CardButtonDraw Instance;

    public Button[] buttons;

    public Sprite c1, c2, c3, c4;
    public Sprite c11, c12, c13, c22, c14, c23, c24, c33, c34, c44;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        Instance = this;
    }

    //カード情報をもとにカードのボタンを描画する
    public void cardButtonDraw(int index, CardCreate.Card card)
    {
        //カードのTMPを取得
        TextMeshProUGUI cardText = buttons[index].GetComponentInChildren<TextMeshProUGUI>();
        //カードのTMPにカードの情報を表示
        // 「自分」か「相手」かを判定
        string target = card.forme ? "自分" : "相手";
        // 両手か片手かで文章を組み立てる
        if (card.ryouhou)
        {
            //$で変数を楽に文字列に埋め込める
            cardText.text = $"   {target}の手を{card.left}{card.right}にするよ";
        }
        else
        {
            cardText.text = $"{target}の手を片方{card.hand}にするよ";
        }

        //カードの画像の決定
        if (card.ryouhou)
        {
            if (card.left == 1 && card.right == 1)
            {
                buttons[index].image.sprite = c11;
            }
            if (card.left == 1 && card.right == 2)
            {
                buttons[index].image.sprite = c12;
            }
            if (card.left == 1 && card.right == 3)
            {
                buttons[index].image.sprite = c13;
            }
            if (card.left == 2 && card.right == 2)
            {
                buttons[index].image.sprite = c22;
            }
            if (card.left == 1 && card.right == 4)
            {
                buttons[index].image.sprite = c14;
            }
            if (card.left == 2 && card.right == 3)
            {
                buttons[index].image.sprite = c23;
            }
            if (card.left == 2 && card.right == 4)
            {
                buttons[index].image.sprite = c24;
            }
            if (card.left == 3 && card.right == 3)
            {
                buttons[index].image.sprite = c33;
            }
            if (card.left == 3 && card.right == 4)
            {
                buttons[index].image.sprite = c34;
            }
            if (card.left == 4 && card.right == 4)
            {
                buttons[index].image.sprite = c44;
            }
        }
        else
        {
            switch (card.hand)
            {
                case 1:
                    buttons[index].image.sprite = c1;
                    break;
                case 2:
                    buttons[index].image.sprite = c2;
                    break;
                case 3:
                    buttons[index].image.sprite = c3;
                    break;
                case 4:
                    buttons[index].image.sprite = c4;
                    break;
                default:
                    break;
            }
        }
    }

    //指定した番号(index)のカードボタンを半透明にして無効化する
    public void DisableCardButton(int index)
    {
        // ボタンを押せなくする
        buttons[index].interactable = false;
    }

    //カードを上に移動させる
    public void SelectCardAnim(int index)
    {
        buttons[index].transform.localPosition += new Vector3(0, 110f, 0);
    }

    //カードを元の位置に戻す
    public void ResetCardAnim(int index)
    {
        buttons[index].transform.localPosition -= new Vector3(0, 110f, 0);
    }
}
