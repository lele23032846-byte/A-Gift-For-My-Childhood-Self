using UnityEngine;

public class ScaleControl : MonoBehaviour
{
    public Transform scaleTarget;

    private Animator anim;
    private Vector3 originalScale;

    void Start()
    {
        anim = GetComponent<Animator>();
        originalScale = scaleTarget.localScale;
    }

    void Update()
    {
        float scale = anim.GetFloat("scaleTarget");

        if (scaleTarget != null)
        {
            scale = Mathf.Clamp(scale, 0, 1);
            scaleTarget.localScale = originalScale * scale;
        }
    }
}