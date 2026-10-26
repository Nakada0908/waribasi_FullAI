using UnityEngine;

public class HandFingerController : MonoBehaviour
{
    //プライベートだけどインスペクターから設定できるようにする
    [SerializeField] Animator animator;

    void Awake()
    {
        //if (animator == null)
        animator = GetComponent<Animator>();
    }
    public void SetFinger(int fingerCount)
    {
        animator.SetInteger("FingerCount", fingerCount);
    }
}
