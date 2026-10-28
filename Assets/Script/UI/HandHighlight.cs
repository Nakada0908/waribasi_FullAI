//変更:公開の防御・封印と、本人だけの選択表示を組み合わせる。
using Unity.Netcode;
using UnityEngine;
using Waribasi.Networking;
using Waribasi.Presentation;

public class HandHighlight : MonoBehaviour
{
    public Material selectMaterial;
    //追加:状態用素材と、手の表示とは別のマーカー。
    [SerializeField] private Material stateOpaque;
    [SerializeField] private Material stateTransparent;
    [SerializeField] private GameObject selectionMarker;
    [SerializeField] private GameObject sealMarker;
    [SerializeField, Range(0.15f, 0.9f)] private float sealOpacity = 0.45f;
    [SerializeField, Range(0, 2)] private float rainbowSpeed = 0.3f;
    [SerializeField] private Vector3 markerOffset = new Vector3(0, 0.22f, 0);
    private Renderer myRenderer;
    private Material[] originalMaterials;
    private Material opaqueInstance;
    private Material transparentInstance;
    private MaterialPropertyBlock properties;
    //変更:状態表示の対象を共通の手識別で保持する。
    private MatchHandView hand;
    private HandRL side;
    //追加:マーカーは手札に隠れる腕中央ではなく、既存の接触位置を基準にする。
    private Transform markerAnchor;
    private MatchClientState view;
    private MatchInteractionController interaction;
    private Camera localCamera;
    private bool legacySelection;
    //追加:公開イベントの変化対象を短時間強調し、選択解除後も結果を確認できるようにする。
    [SerializeField, Min(0.1f)] private float resultHighlightSeconds = 0.8f;
    private string resultMatch;
    private long resultVersion = -1;
    private float resultUntil;
    private bool resultVisible;

    private void Awake()
    {
        myRenderer = GetComponent<Renderer>();
        if (myRenderer == null)
        {
            return;
        }
        originalMaterials = myRenderer.sharedMaterials;
        properties = new MaterialPropertyBlock();
        //変更:ローカル手にはNetworkObjectを要求しない。
        side = GetComponentInParent<HandRL>();
        hand = side != null ? side.GetComponent<MatchHandView>() ?? side.gameObject.AddComponent<MatchHandView>() : null;
        markerAnchor = hand != null ? hand.transform.Find("targetpos") : null;
        if (stateOpaque != null)
        {
            opaqueInstance = new Material(stateOpaque);
            transparentInstance = new Material(stateTransparent);
        }
        Apply();
    }

    private void Update()
    {
        //変更:モードの表示状態を再購読し、古い状態へ購読を残さない。
        if (view != MatchSessionAccess.State)
        {
            if (view != null)
            {
                view.Changed -= Apply;
            }
            view = MatchSessionAccess.State;
            if (view != null)
            {
                view.Changed += Apply;
            }
            Apply();
        }
        if (interaction == null && MatchInteractionController.Instance != null)
        {
            interaction = MatchInteractionController.Instance;
            interaction.Changed += Apply;
            Apply();
        }
        //追加:状態通知のない間も結果の強調を期限で終了する。
        if (resultVisible && Time.unscaledTime >= resultUntil)
        {
            Apply();
        }
        if (localCamera == null)
        {
            localCamera = Camera.main;
        }
        if (localCamera != null && myRenderer != null)
        {
            //変更:指先付近へマーカーを配置し、手札との重なりを避ける。
            Vector3 position = (markerAnchor != null ? markerAnchor.position : myRenderer.bounds.center) + markerOffset;
            if (selectionMarker != null)
            {
                selectionMarker.transform.position = position;
                selectionMarker.transform.rotation = localCamera.transform.rotation;
            }
            if (sealMarker != null)
            {
                sealMarker.transform.position = position + localCamera.transform.right * 0.45f;
                sealMarker.transform.rotation = localCamera.transform.rotation;
            }
        }
    }

    public void SetHighlight(bool selected)
    {
        //変更:既存呼出しも公開状態を消さずに選択だけ変更する。
        legacySelection = selected;
        Apply();
    }

    private void Apply()
    {
        if (myRenderer == null || originalMaterials == null)
        {
            return;
        }
        //変更:選択と確定済み結果を同じ既存マーカーで示す。
        //削除:bool selected = interaction != null ? interaction.IsMatchHandSelected(hand) : legacySelection;
        bool selected = interaction != null ? interaction.IsMatchHandSelected(hand) : legacySelection;
        bool defended = false;
        bool sealedHand = false;
        bool alive = true;
        //変更:公開盤面の席と左右を使い、相手の非公開情報を参照しない。
        if (view?.Public != null && hand != null && hand.ResolvePlayer(view, out var player))
        {
            //追加:同じ公開版の再配信で強調時間を延長しない。
            if (resultMatch != view.Public.matchId || resultVersion != view.Public.stateVersion)
            {
                resultMatch = view.Public.matchId;
                resultVersion = view.Public.stateVersion;
                resultUntil = MatchHudDisplay.ChangedHand(view.Public, player, hand.Side)
                    ? Time.unscaledTime + resultHighlightSeconds : 0;
            }
            int index = (int)player * 2 + (int)hand.Side;
            var state = view.Public.hands[index];
            alive = state.count != 0;
            defended = state.defenseUntil != 0;
            sealedHand = state.sealUntil != 0;
        }
        //追加:切断や終了では古い結果を点灯し続けない。
        resultVisible = view?.Public != null && !view.CommunicationInterrupted &&
            view.Public.matchPhase == (int)Waribasi.Rules.MatchPhase.Active && Time.unscaledTime < resultUntil;
        selected = (selected || resultVisible) && alive;
        //削除:selected &= alive;
        if (selectionMarker != null)
        {
            selectionMarker.SetActive(selected);
        }
        if (sealMarker != null)
        {
            sealMarker.SetActive(sealedHand && alive);
        }
        if ((!selected && !defended && !sealedHand) || opaqueInstance == null)
        {
            myRenderer.sharedMaterials = originalMaterials;
            myRenderer.SetPropertyBlock(null);
            return;
        }
        var materials = new Material[originalMaterials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = sealedHand ? transparentInstance : opaqueInstance;
        }
        myRenderer.sharedMaterials = materials;
        Material original = originalMaterials.Length > 0 ? originalMaterials[0] : null;
        properties.Clear();
        if (original != null)
        {
            properties.SetColor("_BaseColor", original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") :
                original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white);
            Texture texture = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : original.mainTexture;
            if (texture != null)
            {
                properties.SetTexture("_BaseMap", texture);
            }
        }
        properties.SetFloat("_Opacity", sealedHand ? sealOpacity : 1);
        properties.SetFloat("_Rainbow", defended ? 1 : 0);
        properties.SetFloat("_Selected", selected && !defended ? 1 : 0);
        properties.SetFloat("_RainbowSpeed", rainbowSpeed);
        myRenderer.SetPropertyBlock(properties);
    }

    private void OnDestroy()
    {
        if (view != null)
        {
            view.Changed -= Apply;
        }
        if (interaction != null)
        {
            interaction.Changed -= Apply;
        }
        if (opaqueInstance != null)
        {
            Destroy(opaqueInstance);
            Destroy(transparentInstance);
        }
    }
}

//削除:T04移行前の実装を参照用に保存する。
//using UnityEngine;
//
//public class HandHighlight : MonoBehaviour
//{
//    //SkinnedMeshRendererも取得できるように、親クラスである汎用的な「Renderer」を使用する
//    Renderer myRenderer;
//    Material[] originalMaterials; //元のマテリアル配列を保存
//
//    [Header("選択時のマテリアル")]
//    public Material selectMaterial;
//
//    void Awake()
//    {
//        //Rendererを取得するように変更
//        myRenderer = GetComponent<Renderer>();
//
//        //エラー防止：もしアタッチし間違えても落ちないようにする
//        if (myRenderer == null) return;
//
//        //元のマテリアルをすべて取得して保存
//        originalMaterials = myRenderer.materials;
//    }
//
//    //ハイライトをオン・オフする
//    public void SetHighlight(bool on)
//    {
//        //エラー回避：マテリアルがセットされていなければ何もしない
//        if (myRenderer == null || selectMaterial == null) return;
//
//        if (on)
//        {
//            //マテリアルをすべて「選択用マテリアル」に完全置換する
//            Material[] selectedMats = new Material[originalMaterials.Length];
//            for (int i = 0; i < originalMaterials.Length; i++)
//            {
//                selectedMats[i] = selectMaterial;
//            }
//            myRenderer.materials = selectedMats;
//        }
//        else
//        {
//            //元のマテリアル配列に戻す
//            myRenderer.materials = originalMaterials;
//        }
//    }
//}

//削除:T06の共通入力・ローカル接続へ移行する前の箇所を参照用に保存する。
//    private NetworkObject hand;
//        hand = GetComponentInParent<NetworkObject>();
//        side = hand != null ? hand.GetComponent<HandRL>() : null;
//        if (view == null && MatchClientState.Instance != null)
//        {
//            view = MatchClientState.Instance;
//            view.Changed += Apply;
//            Apply();
//        }
//interaction.IsHandSelected(hand)
//        if (view?.Public != null && hand != null && side != null)
//        {
//            int player = hand.OwnerClientId == view.Public.playerAClientId ? 0 : 1;
//            int index = player * 2 + (int)MatchClientState.ToSide(side.handType);
