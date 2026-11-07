using UnityEngine;

/// Shrinks an effect to nothing, then removes it.
public class Shrink : MonoBehaviour
{
    public float duration = 0.15f;
    float t;
    Vector3 start;

    void Start() => start = transform.localScale;

    void Update()
    {
        t += Time.deltaTime;
        transform.localScale = Vector3.Lerp(start, Vector3.zero, t / duration);
        if (t >= duration) Destroy(gameObject);
    }
}
