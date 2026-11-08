using UnityEngine;

/// Makes a point light breathe gently like a fire. Slow and shallow on purpose: a fast ±25% waver
/// made whole rooms (and pale characters in them) pulse visibly, which brings on 3D sickness.
public class Flicker : MonoBehaviour
{
    Light l;
    float baseIntensity, seed;

    void Start()
    {
        l = GetComponent<Light>();
        baseIntensity = l.intensity;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * 0.8f);
        l.intensity = baseIntensity * (0.94f + 0.12f * n);
    }
}
