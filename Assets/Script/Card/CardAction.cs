//変更:ローカルの選択をSceneの共通管理へ集約する。
using Unity.Netcode;
using Waribasi.Presentation;
using Waribasi.Rules;

public enum TargetHandType
{
    Both,
    Left,
    Right
}

public class CardAction : NetworkBehaviour
{
    public NetworkVariable<bool> turnused = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public void ApplyConfirmed(GameState state, PlayerId player)
    {
        if (IsServer)
        {
            turnused.Value = state.CurrentPlayer == player && state.TurnPhase == TurnPhase.NormalActionOnly;
        }
    }

    //変更:札ID・版・対象・割り当ては共通選択窓口で保持する。
    public void OnCardSelected(int index)
    {
        if (IsOwner)
        {
            MatchInteractionController.Instance?.SelectCard(index);
        }
    }

    public void OnHandSelected(TargetHandType hand)
    {
        //変更:旧UIの左右選択も同じ対象選択へ渡す。
        if (IsOwner && hand != TargetHandType.Both)
        {
            MatchInteractionController.Instance?.SelectCardTarget(hand == TargetHandType.Left ? HandSide.Left : HandSide.Right);
        }
    }

    public void OnClickConfirm()
    {
        if (IsOwner)
        {
            MatchInteractionController.Instance?.ConfirmSelection();
        }
    }

    public void OnClickCancel()
    {
        if (IsOwner)
        {
            MatchInteractionController.Instance?.CancelSelection();
        }
    }
}

//削除:T04移行前の実装を参照用に保存する。
////変更:正式な更新を共通ルールと認証済み通信窓口へ集約する。
//using System.Collections;
//using Unity.Netcode;
//using UnityEngine;
//using Waribasi.Networking;
//using Waribasi.Rules;
//
//public enum TargetHandType
//{
//    Both,
//    Left,
//    Right
//}
//
//public class CardAction : NetworkBehaviour
//{
//    //変更:既存UI向けの公開コピー。使用可否は共通ルールが判定する。
//    public NetworkVariable<bool> turnused = new NetworkVariable<bool>(false,
//        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
//    private long selectedCardId;
//    private long selectedVersion;
//    private int selectedIndex;
//    private int confirmation;
//
//    public void ApplyConfirmed(GameState state, PlayerId player)
//    {
//        if (IsServer)
//        {
//            turnused.Value = state.CurrentPlayer == player && state.TurnPhase == TurnPhase.NormalActionOnly;
//        }
//    }
//
//    public void OnCardSelected(int index)
//    {
//        var view = MatchClientState.Instance;
//        if (!IsOwner || confirmation != 0 || view == null || !view.CanAct ||
//            view.Public.turnPhase != (int)TurnPhase.CardOrNormalAction || index < 0 || index >= view.OwnHand.Count)
//        {
//            return;
//        }
//        var card = view.OwnHand[index];
//        if (card.Definition.Kind == CardKind.Revival)
//        {
//            return;
//        }
//        selectedCardId = card.Id;
//        selectedVersion = view.Public.stateVersion;
//        selectedIndex = index;
//        confirmation = 1;
//        CardButtonDraw.Instance?.SelectCardAnim(index);
//        if (card.Definition.NeedsHand)
//        {
//            HandSelectUI.Instance?.ShowUI();
//        }
//        else
//        {
//            StartCoroutine(ConfirmCoroutine(TargetHandType.Both));
//        }
//    }
//
//    public void OnHandSelected(TargetHandType hand)
//    {
//        if (confirmation != 1)
//        {
//            return;
//        }
//        HandSelectUI.Instance?.DeleteUI();
//        StartCoroutine(ConfirmCoroutine(hand));
//    }
//
//    private IEnumerator ConfirmCoroutine(TargetHandType hand)
//    {
//        ConfirmCancelUI.Instance?.ShowUI();
//        yield return new WaitUntil(() => confirmation != 1 || !SelectionIsCurrent());
//        ConfirmCancelUI.Instance?.DeleteUI();
//        CardButtonDraw.Instance?.ResetCardAnim(selectedIndex);
//        bool confirmed = confirmation == 2 && SelectionIsCurrent();
//        confirmation = 0;
//        if (confirmed)
//        {
//            HandSide? side = hand == TargetHandType.Left ? HandSide.Left :
//                hand == TargetHandType.Right ? HandSide.Right : (HandSide?)null;
//            MatchNetworkController.Instance?.SendCard(selectedCardId, side, false, selectedVersion);
//        }
//    }
//
//    private bool SelectionIsCurrent()
//    {
//        var view = MatchClientState.Instance;
//        return view != null && view.CanAct && view.Public.stateVersion == selectedVersion;
//    }
//
//    private void Update()
//    {
//        if (confirmation == 1 && !SelectionIsCurrent())
//        {
//            confirmation = 3;
//            HandSelectUI.Instance?.DeleteUI();
//            ConfirmCancelUI.Instance?.DeleteUI();
//            //追加:片手選択中には確認コルーチンがまだないため、ここでも解除する。
//            StopAllCoroutines();
//            CardButtonDraw.Instance?.ResetCardAnim(selectedIndex);
//            confirmation = 0;
//        }
//    }
//
//    public void OnClickConfirm()
//    {
//        if (confirmation == 1)
//        {
//            confirmation = 2;
//        }
//    }
//
//    public void OnClickCancel()
//    {
//        if (confirmation == 1)
//        {
//            confirmation = 3;
//        }
//    }
//}

//削除:旧実装は参照用に保存し、RPC登録・直接更新・旧配布を実行しない。
//using Unity.Netcode;
//using UnityEngine;
//using System.Collections;
//
//public enum TargetHandType
//{
//    Both,  // 両手用
//    Left,  // 左手
//    Right  // 右手
//}
//
//public class CardAction : NetworkBehaviour
//{
//    CardCreate cardSyori;
//    int pendingCardIndex; //選択中のカードのインデックスを一時保存
//    TargetHandType pendingHandType;
//
//    private enum ConfirmState { None,Waiting, Confirmed, Canceled }
//    private ConfirmState confirmState;
//
//    //このターンでカードを使用したかどうか
//    public NetworkVariable<bool> turnused = new NetworkVariable<bool>(
//        false,
//        NetworkVariableReadPermission.Everyone,
//        NetworkVariableWritePermission.Server);
//
//    void Awake()
//    {
//        //同じプレイヤーオブジェクトについているCardCreateを取得しておく
//        cardSyori = GetComponent<CardCreate>();
//    }
//
//    //ターンが回ってきたら「1ターンに1回」の制限をリセットする
//    public override void OnNetworkSpawn()
//    {
//        if (IsServer)
//        {
//            var ps = GetComponent<PlayerStatus>();
//            //OnValueChangedがNGOの機能で、myturnの値が変わったときに自動で呼ばれる処理を登録できる
//            //ラムダ式で+=が変化が起きた時
//            //(oldValue, newValue)がNGOの機能で、値が変わる前と変わった後の値を受け取れる
//            //=>もラムダ式で、右の処理実行するというもの
//            //myturnの値が変わった時に自動で呼ばれる処理を登録
//            ps.myturn.OnValueChanged += (oldValue, newValue) =>
//            {
//                if (newValue == true) //自分のターンになったら
//                {
//                    turnused.Value = false; //制限をリセット
//                }
//            };
//        }
//    }
//
//    public void OnCardSelected(int selectedIndex)
//    {
//        if (!IsOwner) return; //自分以外の操作を弾く
//
//        //None以外の状態（Waitingなど）なら他のカードを押せないように弾く
//        if (confirmState != ConfirmState.None) return;
//
//        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
//        var ps = localPlayer.GetComponent<PlayerStatus>();
//
//        if (!ps.myturn.Value) { return; }//自分のターンのみ
//
//        //各ターン１回のみしか使用できないようにする
//        if (turnused.Value) { return; }
//
//        //まず、選ばれたカードの情報をローカルで確認する
//        CardCreate.Card selectedCard = cardSyori.myCards[selectedIndex];
//        //選ばれたカードが使用済みかどうか
//        if (selectedCard.used) { return; }
//
//        //選んだカードのインデックスを保存し、状態を待機中にする
//        pendingCardIndex = selectedIndex;
//        confirmState = ConfirmState.Waiting;
//
//        //選んだカードのUIを上に移動させる
//        CardButtonDraw.Instance.SelectCardAnim(selectedIndex);
//
//        //片手用なら直接「手を選ぶUI」を出し、両手用なら「確認UI」を出す
//        if (selectedCard.ryouhou == false)
//        {
//            HandSelectUI.Instance.ShowUI();
//        }
//        else
//        {
//            //両手用の場合は、両手を対象にしてコルーチンを呼ぶ
//            StartCoroutine(ConfirmCoroutine(TargetHandType.Both));
//        }
//    }
//
//    //HandSelectUIから呼ばれるメソッド
//    public void OnHandSelected(TargetHandType handType)
//    {
//        //手を選ぶUIを消す
//        HandSelectUI.Instance.DeleteUI();
//        //統合したコルーチンに引数として選んだ手を渡す
//        StartCoroutine(ConfirmCoroutine(handType));
//    }
//
//    //両手用・片手用で共通化した最終確認コルーチン
//    private IEnumerator ConfirmCoroutine(TargetHandType finalTargetHand)
//    {
//        //ここで「確定 / キャンセル」のボタンUI等を表示する処理を呼ぶ
//        ConfirmCancelUI.Instance.ShowUI();
//
//        //プレイヤーが確定かキャンセルを押すまで、ここで処理を待機
//        yield return new WaitUntil(() => confirmState != ConfirmState.Waiting);
//
//        //UIを消す
//        ConfirmCancelUI.Instance.DeleteUI();
//
//        //反応があった後の分岐
//        if (confirmState == ConfirmState.Canceled)
//        {
//            //キャンセルの場合：カードを元の位置に戻して終了
//            CardButtonDraw.Instance.ResetCardAnim(pendingCardIndex);
//            //状態をNoneに戻して、また別のカードをクリックできるようにする
//            confirmState = ConfirmState.None;
//            yield break; //コルーチンをここで終了
//        }
//
//        //確定の場合
//        CardButtonDraw.Instance.ResetCardAnim(pendingCardIndex); //一旦元の位置に戻す
//        confirmState = ConfirmState.None;
//
//        //サーバーに対して「何番目のカードを選んだか」と「どの手を変えるのか」しか送信できなくすることでチート対策を行う
//        UseCardServerRpc(pendingCardIndex, finalTargetHand);
//    }
//
//    //ServerRpcParams rpcParams = defaultで呼び出し元を特定できるようにする
//    [ServerRpc]
//    private void UseCardServerRpc(int index, TargetHandType targetHand, ServerRpcParams rpcParams = default)
//    {
//        ulong clientId = rpcParams.Receive.SenderClientId;
//
//        //サーバー側でも「すでに使われたカードじゃないか」チェック
//        if (cardSyori.myCards[index].used) return;
//        //サーバー側でも「すでにターンを消費していないか」チェック
//        if (turnused.Value) return;
//
//        //CardCreateから、サーバー側で保存されているカード情報を読み取る
//        CardCreate.Card usedCard = cardSyori.myCards[index];
//
//        var connectedClients = NetworkManager.Singleton.ConnectedClients;
//        //自分のステータスはプレイヤーオブジェクトから取得
//        PlayerStatus myStatus = connectedClients[clientId].PlayerObject.GetComponent<PlayerStatus>();
//        //ついでに自分のターンかどうかもサーバー側で確認する
//        if (!myStatus.myturn.Value) return;
//
//        //対戦相手のステータスは、接続されているクライアントの中から自分以外のプレイヤーオブジェクトを探して取得
//        PlayerStatus opponentStatus = null;
//        foreach (var client in connectedClients.Values)
//        {
//            if (client.ClientId != clientId)
//            {
//                opponentStatus = client.PlayerObject.GetComponent<PlayerStatus>();
//                break;
//            }
//        }
//
//        if (usedCard.forme)
//        {
//            //カードの内容を見て、ステータスを変更する
//            if (usedCard.ryouhou)
//            {
//                //どっちかが５ならキャンセル
//                if (myStatus.Lyubi.Value == 5 || myStatus.Ryubi.Value == 5) return;
//                //自分の手とカードの値が両方とも同じならキャンセル
//                if (myStatus.Lyubi.Value == usedCard.left && myStatus.Ryubi.Value == usedCard.right) return;
//
//                //両手の値を強制変更する
//                myStatus.Lyubi.Value = usedCard.left;
//                myStatus.Ryubi.Value = usedCard.right;
//            }
//            else
//            {
//                //対象が５ならキャンセル
//                if (targetHand == TargetHandType.Left && myStatus.Lyubi.Value == 5) return;
//                if (targetHand == TargetHandType.Right && myStatus.Ryubi.Value == 5) return;
//                //自分の手とカードの値が同じならキャンセル
//                if (targetHand == TargetHandType.Left && myStatus.Lyubi.Value == usedCard.hand) return;
//                if (targetHand == TargetHandType.Right && myStatus.Ryubi.Value == usedCard.hand) return;
//
//                //片手の値を強制変更する
//                if (targetHand == TargetHandType.Left) myStatus.Lyubi.Value = usedCard.hand;
//                if (targetHand == TargetHandType.Right) myStatus.Ryubi.Value = usedCard.hand;
//            }
//
//            //アニメーション（手の見た目の変更など）を全クライアントに反映させる
//            UpdateHandAnimationClientRpc(myStatus.OwnerClientId);
//
//            //最後にサーバー側で「使用済み」に変更する
//            cardSyori.myCards[index].used = true;
//            turnused.Value = true;
//
//            DisableCardUIClientRpc(index);
//        }
//        else
//        {
//            //カードの内容を見て、ステータスを変更する
//            if (usedCard.ryouhou)
//            {
//                //どっちかが５ならキャンセル
//                if (opponentStatus.Lyubi.Value == 5 || opponentStatus.Ryubi.Value == 5) return;
//
//                if (opponentStatus.Lyubi.Value == usedCard.left && opponentStatus.Ryubi.Value == usedCard.right) return;
//
//                //両手の値を強制変更する
//                opponentStatus.Lyubi.Value = usedCard.left;
//                opponentStatus.Ryubi.Value = usedCard.right;
//            }
//            else
//            {
//                //対象が５ならキャンセル
//                if (targetHand == TargetHandType.Left && opponentStatus.Lyubi.Value == 5) return;
//                if (targetHand == TargetHandType.Right && opponentStatus.Ryubi.Value == 5) return;
//
//                if (targetHand == TargetHandType.Left && opponentStatus.Lyubi.Value == usedCard.hand) return;
//                if (targetHand == TargetHandType.Right && opponentStatus.Ryubi.Value == usedCard.hand) return;
//
//                //片手の値を強制変更する
//                if (targetHand == TargetHandType.Left) opponentStatus.Lyubi.Value = usedCard.hand;
//                if (targetHand == TargetHandType.Right) opponentStatus.Ryubi.Value = usedCard.hand;
//            }
//
//            //アニメーション（手の見た目の変更など）を全クライアントに反映させる
//            UpdateHandAnimationClientRpc(opponentStatus.OwnerClientId);
//
//            //最後にサーバー側で「使用済み」に変更する
//            cardSyori.myCards[index].used = true;
//            turnused.Value = true;
//
//            DisableCardUIClientRpc(index);
//        }
//    }
//
//    [Rpc(SendTo.ClientsAndHost)] //全クライアント（ホスト含む）で手の見た目変更を実行
//    public void UpdateHandAnimationClientRpc(ulong targetClientId)
//    {
//        NetworkObject hand1 = null, hand2 = null;
//
//        //提示してくれたコードの検索処理をここで使う！
//        //ターゲットのプレイヤーの手を探し出す
//        foreach (var h in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
//        {
//            if (h.CompareTag("Hand") && h.OwnerClientId == targetClientId)
//            {
//                if (hand1 == null) { hand1 = h; }
//                else { hand2 = h; break; }
//            }
//        }
//
//        if (hand1 != null && hand2 != null)
//        {
//            ChangeHand.Instance.Anime(hand1, hand2);
//        }
//    }
//
//    [ClientRpc]
//    private void DisableCardUIClientRpc(int index)
//    {
//        // 他のプレイヤーの画面のUIまで消えないように、自分の画面の時だけ実行
//        if (!IsOwner) return;
//
//        // CardButtonDrawに用意した「半透明にするスクリプト」を呼び出す！
//        if (CardButtonDraw.Instance != null)
//        {
//            CardButtonDraw.Instance.DisableCardButton(index);
//        }
//    }
//
//    //画面上の「確定ボタン」「キャンセルボタン」からそれぞれ呼ばれるメソッド
//    public void OnClickConfirm()
//    {
//        //待機中の時だけ反応するようにする
//        if (confirmState == ConfirmState.Waiting)
//        {
//            confirmState = ConfirmState.Confirmed;
//        }
//    }
//
//    //キャンセルボタンのOnClickに設定するメソッド
//    public void OnClickCancel()
//    {
//        //待機中の時だけ反応するようにする
//        if (confirmState == ConfirmState.Waiting)
//        {
//            confirmState = ConfirmState.Canceled;
//        }
//    }
//}
