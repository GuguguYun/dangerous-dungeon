using UnityEngine;

/// A point light that fades out and removes itself.
public class FadeLight : MonoBehaviour
{
    public float duration = 0.1f;
    Light l;
    float start, t;

    void Start()
    {
        l = GetComponent<Light>();
        start = l.intensity;
    }

    void Update()
    {
        t += Time.deltaTime;
        l.intensity = Mathf.Lerp(start, 0f, t / duration);
        if (t >= duration) Destroy(gameObject);
    }
}
