using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class CardCreate : NetworkBehaviour
{
    // カードの構造体
    //System.って書いて、下のランダムと差別化する
    [System.Serializable]//カードの構造体インスペクターで表示できるようにするため
    public struct Card
    {
        public bool forme;      // 効果対象
        public bool ryouhou;    // 両手かどうか
        public int left;        // 両手用 左
        public int right;       // 両手用 右
        public int hand;        // 片手用
        public bool used;       // 使用済みかどうか
    }

    //5枚分のカードを保持
    public Card[] myCards = new Card[5];

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            CreateCard();
        }
    }

    void CreateCard()
    {
        for (int i = 0; i < 5; i++)
        {
            Card newCard = new Card();

            // 効果対象を決定
            int taisyou = Random.Range(0, 2);
            if (taisyou == 0)
            {
                newCard.forme = true;
            }
            else
            {
                newCard.forme = false;
            }

            // 両手か片手か決定
            int ryouhoukana = Random.Range(0, 2);
            if (ryouhoukana == 0)
            {
                newCard.ryouhou = true;

                newCard.right = Random.Range(1, 5);
                //左<=右になるようにする
                newCard.left = Random.Range(1, newCard.right + 1);
            }
            else
            {
                newCard.ryouhou = false;

                newCard.hand = Random.Range(1, 5);
            }

            //サーバー側に保存
            myCards[i] = newCard;

            //クライアントへ送信
            SendCardClientRpc(
                i,
                newCard.forme,
                newCard.ryouhou,
                newCard.left,
                newCard.right,
                newCard.hand
            );
        }
    }

    [ClientRpc]
    void SendCardClientRpc(int index, bool f, bool r, int l, int rt, int h)
    {
        //毎回サーバーに確認しなくてもいいようにクライアント用のコピーを作る
        Card copyCard = new Card();

        copyCard.forme = f;
        copyCard.ryouhou = r;
        copyCard.left = l;
        copyCard.right = rt;
        copyCard.hand = h;

        //手札配列に保存（これで各プレイヤーのオブジェクトに手札が保存）
        myCards[index] = copyCard;

        //IsOwnerを使うことで、このオブジェクトが自分のキャラクターの時だけUIを表示
        //これで相手の手札が自分の画面に表示されるのを防ぐ
        if (IsOwner)
        {
            //ボタン表示処理
            //CardButtonDraw.Instance.cardButtonDraw(index, myCards[index]);
            StartCoroutine(WaitAndDrawUI(index));
        }
    }

    // UIの準備を待ってから描画（Draw）だけを行うコルーチン
    private IEnumerator WaitAndDrawUI(int index)
    {
        //UIマネージャー (CardButtonDraw.Instance) がシーンに出現するまで待機
        while (CardButtonDraw.Instance == null)
        {
            yield return null; // 1フレーム待機して様子を見る
        }

        //Updateを使わなくても好きな秒数だけ待機できる
        //さらに念のため待っておいて完全にUIの準備ができるようにする
        yield return new WaitForSeconds(0.1f);

        //準備ができたら、すでに保存してある myCards のデータを使って描画！
        CardButtonDraw.Instance.cardButtonDraw(index, myCards[index]);
    }
}
