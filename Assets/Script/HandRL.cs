using Unity.Netcode;
using UnityEngine;

public enum HandType
{
    Right,
    Left
}

public class HandRL : MonoBehaviour
{
    //手のオブジェクト自体に持たせる
    public HandType handType;
}