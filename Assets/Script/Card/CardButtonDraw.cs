//変更:本人札を枚数に合わせて配置し、選択位置を絶対値で決める。
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Waribasi.Presentation;
using Waribasi.Rules;

public class CardButtonDraw : MonoBehaviour
{
    public static CardButtonDraw Instance;
    public Button[] buttons;
    public Sprite c1, c2, c3, c4;
    public Sprite c11, c12, c13, c22, c14, c23, c24, c33, c34, c44;
    //追加:Canvas基準の間隔と選択時の持ち上げ量。
    [SerializeField] private float spacing = 286;
    [SerializeField] private float selectedHeight = 45;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void DrawOwnHand(IReadOnlyList<HandCard> cards)
    {
        var interaction = MatchInteractionController.Instance;
        for (int i = 0; i < buttons.Length; i++)
        {
            bool occupied = i < cards.Count;
            buttons[i].gameObject.SetActive(occupied);
            if (!occupied)
            {
                continue;
            }
            var card = cards[i].Definition;
            bool selected = interaction?.SelectedCard?.Id == cards[i].Id;
            ((RectTransform)buttons[i].transform).anchoredPosition = new Vector2(
                (i - (cards.Count - 1) * 0.5f) * spacing, selected ? selectedHeight : 0);
            buttons[i].interactable = interaction != null && interaction.CanInteract && card.Kind != CardKind.Revival;
            var label = buttons[i].GetComponentInChildren<TMP_Text>();
            label.text = MatchText.Card(card);
            buttons[i].image.sprite = NumberSprite(card);
            buttons[i].image.color = card.IsNumber ? Color.white : card.Kind == CardKind.Revival
                ? new Color(0.88f, 0.84f, 1) : new Color(0.83f, 0.94f, 1);
        }
    }

    private Sprite NumberSprite(CardDefinition card)
    {
        if (!card.IsNumber)
        {
            return null;
        }
        if (!card.IsBoth)
        {
            return new[] { c1, c2, c3, c4 }[card.First - 1];
        }
        int value = card.First * 10 + card.Second;
        switch (value)
        {
            case 11: return c11;
            case 12: return c12;
            case 13: return c13;
            case 14: return c14;
            case 22: return c22;
            case 23: return c23;
            case 24: return c24;
            case 33: return c33;
            case 34: return c34;
            default: return c44;
        }
    }
}

//削除:T04移行前の実装を参照用に保存する。
//using TMPro;
//using Unity.VisualScripting;
//using UnityEngine;
//using UnityEngine.UI;
//
//public class CardButtonDraw : MonoBehaviour
//{
//    public static CardButtonDraw Instance;
//
//    public Button[] buttons;
//
//    public Sprite c1, c2, c3, c4;
//    public Sprite c11, c12, c13, c22, c14, c23, c24, c33, c34, c44;
//
//    // Start is called once before the first execution of Update after the MonoBehaviour is created
//    void Awake()
//    {
//        Instance = this;
//    }
//
//    //カード情報をもとにカードのボタンを描画する
//    public void cardButtonDraw(int index, CardCreate.Card card)
//    {
//        //カードのTMPを取得
//        TextMeshProUGUI cardText = buttons[index].GetComponentInChildren<TextMeshProUGUI>();
//        //カードのTMPにカードの情報を表示
//        // 「自分」か「相手」かを判定
//        string target = card.forme ? "自分" : "相手";
//        // 両手か片手かで文章を組み立てる
//        if (card.ryouhou)
//        {
//            //$で変数を楽に文字列に埋め込める
//            cardText.text = $"   {target}の手を{card.left}{card.right}にするよ";
//        }
//        else
//        {
//            cardText.text = $"{target}の手を片方{card.hand}にするよ";
//        }
//
//        //カードの画像の決定
//        if (card.ryouhou)
//        {
//            if (card.left == 1 && card.right == 1)
//            {
//                buttons[index].image.sprite = c11;
//            }
//            if (card.left == 1 && card.right == 2)
//            {
//                buttons[index].image.sprite = c12;
//            }
//            if (card.left == 1 && card.right == 3)
//            {
//                buttons[index].image.sprite = c13;
//            }
//            if (card.left == 2 && card.right == 2)
//            {
//                buttons[index].image.sprite = c22;
//            }
//            if (card.left == 1 && card.right == 4)
//            {
//                buttons[index].image.sprite = c14;
//            }
//            if (card.left == 2 && card.right == 3)
//            {
//                buttons[index].image.sprite = c23;
//            }
//            if (card.left == 2 && card.right == 4)
//            {
//                buttons[index].image.sprite = c24;
//            }
//            if (card.left == 3 && card.right == 3)
//            {
//                buttons[index].image.sprite = c33;
//            }
//            if (card.left == 3 && card.right == 4)
//            {
//                buttons[index].image.sprite = c34;
//            }
//            if (card.left == 4 && card.right == 4)
//            {
//                buttons[index].image.sprite = c44;
//            }
//        }
//        else
//        {
//            switch (card.hand)
//            {
//                case 1:
//                    buttons[index].image.sprite = c1;
//                    break;
//                case 2:
//                    buttons[index].image.sprite = c2;
//                    break;
//                case 3:
//                    buttons[index].image.sprite = c3;
//                    break;
//                case 4:
//                    buttons[index].image.sprite = c4;
//                    break;
//                default:
//                    break;
//            }
//        }
//    }
//
//    //指定した番号(index)のカードボタンを半透明にして無効化する
//    public void DisableCardButton(int index)
//    {
//        // ボタンを押せなくする
//        buttons[index].interactable = false;
//    }
//
//    //カードを上に移動させる
//    public void SelectCardAnim(int index)
//    {
//        buttons[index].transform.localPosition += new Vector3(0, 110f, 0);
//    }
//
//    //カードを元の位置に戻す
//    public void ResetCardAnim(int index)
//    {
//        buttons[index].transform.localPosition -= new Vector3(0, 110f, 0);
//    }
//
//    //追加:本人手札の変化を既存5スロットへ反映する最小互換表示。専用意匠はT04で扱う。
//    public void DrawOwnHand(System.Collections.Generic.IReadOnlyList<Waribasi.Rules.HandCard> cards)
//    {
//        for (int i = 0; i < buttons.Length; i++)
//        {
//            bool occupied = i < cards.Count;
//            buttons[i].gameObject.SetActive(occupied);
//            if (!occupied)
//            {
//                continue;
//            }
//            var card = cards[i].Definition;
//            buttons[i].interactable = card.Kind != Waribasi.Rules.CardKind.Revival;
//            if (card.IsNumber)
//            {
//                cardButtonDraw(i, new CardCreate.Card
//                {
//                    forme = card.IsSelf, ryouhou = card.IsBoth, left = card.First, right = card.Second, hand = card.First
//                });
//            }
//            else
//            {
//                string label = card.Kind == Waribasi.Rules.CardKind.Defense ? "片手防御" :
//                    card.Kind == Waribasi.Rules.CardKind.Seal ? "攻撃封じ" :
//                    card.Kind == Waribasi.Rules.CardKind.DrawTwo ? "2枚補充" :
//                    card.Kind == Waribasi.Rules.CardKind.DrawThree ? "3枚補充" :
//                    card.Kind == Waribasi.Rules.CardKind.DrawRandom ? "1～3枚補充" : "復活（自動）";
//                buttons[i].GetComponentInChildren<TextMeshProUGUI>().text = label;
//                buttons[i].image.sprite = null;
//            }
//        }
//    }
//}
