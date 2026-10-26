using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleSceneManager : MonoBehaviour
{
    //毎フレームの入力をチェックするメソッド
    void Update()
    {
        //PCのキー入力・マウスクリック(anyKeyDown)、またはスマホの画面タッチを検知したか判定
        if (Input.anyKeyDown || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
        {
            //StartGameシーンをロードする
            SceneManager.LoadScene("StartGame");
        }
    }
}