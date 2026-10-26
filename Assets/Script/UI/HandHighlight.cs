using UnityEngine;

public class HandHighlight : MonoBehaviour
{
    //SkinnedMeshRendererも取得できるように、親クラスである汎用的な「Renderer」を使用する
    Renderer myRenderer;
    Material[] originalMaterials; //元のマテリアル配列を保存

    [Header("選択時のマテリアル")]
    public Material selectMaterial;

    void Awake()
    {
        //Rendererを取得するように変更
        myRenderer = GetComponent<Renderer>();

        //エラー防止：もしアタッチし間違えても落ちないようにする
        if (myRenderer == null) return;

        //元のマテリアルをすべて取得して保存
        originalMaterials = myRenderer.materials;
    }

    //ハイライトをオン・オフする
    public void SetHighlight(bool on)
    {
        //エラー回避：マテリアルがセットされていなければ何もしない
        if (myRenderer == null || selectMaterial == null) return;

        if (on)
        {
            //マテリアルをすべて「選択用マテリアル」に完全置換する
            Material[] selectedMats = new Material[originalMaterials.Length];
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                selectedMats[i] = selectMaterial;
            }
            myRenderer.materials = selectedMats;
        }
        else
        {
            //元のマテリアル配列に戻す
            myRenderer.materials = originalMaterials;
        }
    }
}