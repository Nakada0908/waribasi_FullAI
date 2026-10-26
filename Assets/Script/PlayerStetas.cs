using Unity.Netcode;
using UnityEngine;

public enum Winner
{
    Non, Win, Lose
}
public class PlayerStatus : NetworkBehaviour
{
    //右手の指の本数
    public NetworkVariable<int> Ryubi =
        new NetworkVariable<int>(1,                 // 初期値
        NetworkVariableReadPermission.Everyone,     // 読み取り権限
        NetworkVariableWritePermission.Server        // 書き込み権限
    );
    //左手の指の本数
    public NetworkVariable<int> Lyubi =
        new NetworkVariable<int>(1,                  // 初期値
        NetworkVariableReadPermission.Everyone,     // 読み取り権限
        NetworkVariableWritePermission.Server        // 書き込み権限
    );
    //現在の操作権限
    public NetworkVariable<bool> myturn =
        new NetworkVariable<bool>(false,              // 初期値
        NetworkVariableReadPermission.Everyone,     // 読み取り権限
        NetworkVariableWritePermission.Server        // 書き込み権限
    );
    // 各クライアントが「自分の結果」を見る用
    public NetworkVariable<Winner> myResult =
        new NetworkVariable<Winner>(Winner.Non,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    //マルチでアップデートは重いのでSumyubiを呼び足すときに足した結果を読み取る
    //両手の指の合計
    public int Sumyubi => Ryubi.Value + Lyubi.Value;
}