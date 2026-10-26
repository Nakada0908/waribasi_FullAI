using UnityEngine;

public class GameQuitManager : MonoBehaviour
{
    //ゲーム起動時（最初のシーンが読み込まれる直前）に自動で呼ばれるアタッチしなくていい！すご！
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        //空のゲームオブジェクトを自動生成し、自分自身をアタッチする
        GameObject manager = new GameObject("GameQuitManager");
        manager.AddComponent<GameQuitManager>();

        //シーンが切り替わってもこのオブジェクトが破壊されないようにする
        DontDestroyOnLoad(manager);
    }

    void Update()
    {
        //Escキーが押されたら終了処理を呼ぶ、Androidの戻るボタン（KeyCode.Escape）にも対応
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
        }
    }
}