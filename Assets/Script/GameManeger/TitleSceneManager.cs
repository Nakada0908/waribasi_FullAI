//変更:キーはInput System、クリックとタッチは同じUIイベントで開始する。
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Waribasi.Presentation;

public class TitleSceneManager : MonoBehaviour
{
    private bool loading;

    private void Update()
    {
        if (GameInputRouter.StartPressed && !GameInputRouter.EscapePressed)
        {
            Begin();
        }
    }

    //追加:タイトル全体のButtonから呼ぶ。一度のScene遷移だけ受け付ける。
    public void Begin()
    {
        if (!loading && !SceneFade.InputBlocked)
        {
            loading = true;
            //変更:T20の暗転完了後に1回だけ設定画面へ移る。
            //削除:SceneManager.LoadScene("StartGame");
            BeginWithFade().Forget();
        }
    }
    //追加:元のSceneが破棄されても、常駐フェード側が新画面の表示と入力解除を行う。
    private async UniTask BeginWithFade()
    {
        try
        {
            await SceneFade.LoadLocalAsync("StartGame", destroyCancellationToken);
        }
        finally
        {
            if (this != null)
            {
                loading = false;
            }
        }
    }
}

//削除:T04移行前の実装を参照用に保存する。
//using UnityEngine;
//using UnityEngine.SceneManagement;
//
//public class TitleSceneManager : MonoBehaviour
//{
//    //毎フレームの入力をチェックするメソッド
//    void Update()
//    {
//        //PCのキー入力・マウスクリック(anyKeyDown)、またはスマホの画面タッチを検知したか判定
//        if (Input.anyKeyDown || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
//        {
//            //StartGameシーンをロードする
//            SceneManager.LoadScene("StartGame");
//        }
//    }
//}
