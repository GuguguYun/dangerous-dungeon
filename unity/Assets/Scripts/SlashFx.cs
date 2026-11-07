using UnityEngine;

/// A glowing blade arc for melee swings: a fan mesh that can be tilted (diagonal slashes),
/// stood upright (overhead smashes) or narrowed to a streak (thrusts), tinted per weapon.
public class SlashFx : MonoBehaviour
{
    MeshRenderer mr;
    MaterialPropertyBlock mpb;
    Color color;
    float t, duration;

    /// yaw: facing; tilt: roll around the facing (diagonal swings); pitch: 90 stands the fan upright.
    public static void Spawn(Vector3 origin, float yaw, float reach, float arc, Color color,
        float tilt = 0f, float pitch = 0f, float duration = 0.18f, float inner = 0.6f)
    {
        var go = new GameObject("slash");
        go.transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-pitch, 0f, tilt));
        go.AddComponent<MeshFilter>().sharedMesh = Fan(Mathf.Min(inner, reach * 0.8f), reach, arc);
        var fx = go.AddComponent<SlashFx>();
        fx.mr = go.AddComponent<MeshRenderer>();
        fx.mr.sharedMaterial = Palette.Trail("slash");
        fx.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fx.mpb = new MaterialPropertyBlock();
        fx.color = color;
        fx.duration = duration;
    }

    // Old call sites: the plain cyan sweep.
    public static void Spawn(Vector3 origin, float yaw, float reach, float arc) =>
        Spawn(origin, yaw, reach, arc, Palette.ColorOf("slash"));

    static Mesh Fan(float inner, float outer, float arc)
    {
        int seg = Mathf.Clamp(Mathf.CeilToInt(arc / 8f), 4, 48);
        var verts = new Vector3[(seg + 1) * 2];
        var cols = new Color[verts.Length];
        var tris = new int[seg * 6];
        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.Deg2Rad * (-arc / 2f + arc * i / seg);
            var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            verts[i * 2] = d * inner;
            verts[i * 2 + 1] = d * outer;
            // Brightest along the leading edge of the swing, fading behind it.
            float lead = i / (float)seg;
            cols[i * 2] = new Color(1f, 1f, 1f, 0f);
            cols[i * 2 + 1] = new Color(1f, 1f, 1f, 0.2f + 0.8f * lead);
        }
        for (int i = 0; i < seg; i++)
        {
            int v = i * 2;
            tris[i * 6 + 0] = v;
            tris[i * 6 + 1] = v + 1;
            tris[i * 6 + 2] = v + 3;
            tris[i * 6 + 3] = v;
            tris[i * 6 + 4] = v + 3;
            tris[i * 6 + 5] = v + 2;
        }
        var m = new Mesh { vertices = verts, colors = cols, triangles = tris };
        m.RecalculateBounds();
        return m;
    }

    void Update()
    {
        t += Time.deltaTime;
        var c = color * 2.2f;
        c.a = 1f - t / duration;
        mpb.SetColor("_Color", c);
        mpb.SetColor("_BaseColor", c);
        mr.SetPropertyBlock(mpb);
        transform.localScale = Vector3.one * (0.85f + 0.2f * t / duration);
        if (t >= duration) Destroy(gameObject);
    }
}
