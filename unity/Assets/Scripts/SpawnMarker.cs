using UnityEngine;

/// A pulsing red circle where an enemy is about to appear (Soul Knight style warning).
public class SpawnMarker : MonoBehaviour
{
    LineRenderer ring;
    float t;

    public static SpawnMarker Create(Vector3 at)
    {
        var go = new GameObject("spawn_marker");
        go.transform.position = new Vector3(at.x, 0.07f, at.z);
        var m = go.AddComponent<SpawnMarker>();
        m.ring = go.AddComponent<LineRenderer>();
        m.ring.loop = true;
        m.ring.useWorldSpace = false;
        m.ring.positionCount = 32;
        m.ring.widthMultiplier = 0.12f;
        m.ring.sharedMaterial = Palette.Trail("beam");
        m.ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return m;
    }

    void Update()
    {
        t += Time.deltaTime;
        float r = Mathf.Lerp(1.4f, 0.5f, Mathf.Repeat(t * 1.6f, 1f));
        for (int i = 0; i < 32; i++)
        {
            float a = i * Mathf.PI * 2f / 32f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
        var c = new Color(1f, 0.2f, 0.5f, 0.9f);
        ring.startColor = ring.endColor = c * 2f;
    }
}
