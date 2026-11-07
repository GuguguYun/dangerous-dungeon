using UnityEngine;

/// Makes a point light waver like a fire.
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
        float n = Mathf.PerlinNoise(seed, Time.time * 6f);
        l.intensity = baseIntensity * (0.75f + 0.5f * n);
    }
}
