using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// The energy-shield dome for the knight's 誓约壁垒 and for shielded elites (Meshy/Shield): geodesic
/// hex cells on a sphere whose centre sits a little above the floor, so the dome is cut cleanly by the
/// floor with a glowing seam. Cells grow in when it goes up and dissolve when it drops; a struck spot
/// sends out a ripple. One material instance per shield (it carries the impact points).
public class ShieldFx : MonoBehaviour
{
    static Mesh fineDome, coarseDome;
    static Material baseMat;
    static readonly int FillId = Shader.PropertyToID("_Fill"), FloorId = Shader.PropertyToID("_FloorY"), NowId = Shader.PropertyToID("_Now");
    static readonly int[] HitIds = { Shader.PropertyToID("_Hit0"), Shader.PropertyToID("_Hit1"), Shader.PropertyToID("_Hit2"), Shader.PropertyToID("_Hit3") };

    public float radius;  // world radius of the sphere
    public float lift;    // how far the sphere's centre sits above the floor (this object sits on the floor)
    Transform dome;
    Material mat;
    MeshRenderer mr;
    Renderer pool;
    float fill, fillTarget, fillSpeed = 3f, strength = 1f;
    int nextHit;

    public bool Up => fillTarget > 0f;
    public Vector3 Centre => dome.position;

    /// fine: smaller cells (the knight's big dome); field/accent are sRGB hues times a linear intensity.
    /// The object sits on the floor (parent it at the owner's feet, or move it there each frame).
    public static ShieldFx Create(string name, Color field, float fieldIntensity, Color accent, float accentIntensity,
        float radius, float lift, bool fine, Transform parent)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        var fx = go.AddComponent<ShieldFx>();
        fx.radius = radius;
        fx.lift = lift;
        fx.dome = new GameObject("dome").transform;
        fx.dome.SetParent(go.transform, false);
        fx.dome.localPosition = Vector3.up * lift;
        fx.dome.localScale = Vector3.one * radius;
        fx.dome.gameObject.AddComponent<MeshFilter>().sharedMesh = fine ? (fineDome ? fineDome : fineDome = Dome(3)) : (coarseDome ? coarseDome : coarseDome = Dome(2));
        fx.mr = fx.dome.gameObject.AddComponent<MeshRenderer>();
        fx.mr.shadowCastingMode = ShadowCastingMode.Off;
        fx.mr.receiveShadows = false;
        fx.mr.lightProbeUsage = LightProbeUsage.Off;
        if (!baseMat) baseMat = Resources.Load<Material>("VFX/vfx_shield");
        fx.mat = baseMat ? new Material(baseMat) : new Material(Vfx.Additive);
        fx.mat.name = name;
        fx.mat.SetVector("_Color", (Vector4)(field.linear * fieldIntensity));
        fx.mat.SetVector("_Accent", (Vector4)(accent.linear * accentIntensity));
        for (int i = 0; i < 4; i++) fx.mat.SetVector(HitIds[i], new Vector4(0f, 0f, 0f, -100f));
        fx.mat.SetFloat(FillId, 0f);
        fx.mr.sharedMaterial = fx.mat;
        fx.mr.enabled = false;
        // A soft pool of the shield's light on the floor inside it.
        float footprint = Mathf.Sqrt(Mathf.Max(0.01f, radius * radius - lift * lift));
        fx.pool = LootFx.Glow(go.transform, field, 0.3f * fieldIntensity, footprint * 2.2f, 0f, 0f);
        fx.pool.enabled = false;
        return fx;
    }

    /// Raise the shield (cells grow in).
    public void Show(float seconds = 0.35f)
    {
        fillTarget = 1f;
        fillSpeed = 1f / Mathf.Max(0.05f, seconds);
        mr.enabled = pool.enabled = true;
    }

    /// Drop it (cells dissolve away).
    public void Hide(float seconds = 0.35f)
    {
        fillTarget = 0f;
        fillSpeed = 1f / Mathf.Max(0.05f, seconds);
    }

    /// Elites: a worn shield loses cells (the rim stays so its outline still reads).
    public void SetStrength(float k) => strength = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(k));

    /// A ripple and flare where something struck it (a world point; it is projected onto the dome).
    public void Hit(Vector3 world)
    {
        Vector3 local = dome.InverseTransformPoint(world);
        local = local.sqrMagnitude > 1e-6f ? local.normalized : Vector3.forward;
        mat.SetVector(HitIds[nextHit], new Vector4(local.x, local.y, local.z, Time.time));
        nextHit = (nextHit + 1) % HitIds.Length;
    }

    /// The point on the dome facing a world position (for hits without an exact impact point).
    public Vector3 SurfaceToward(Vector3 world)
    {
        Vector3 d = world - Centre;
        return Centre + (d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward) * radius;
    }

    void LateUpdate()
    {
        float target = fillTarget * strength;
        fill = Mathf.MoveTowards(fill, target, Time.deltaTime * fillSpeed);
        mat.SetFloat(FillId, fill);
        mat.SetFloat(FloorId, transform.position.y);
        mat.SetFloat(NowId, Time.time);
        bool on = fill > 0.001f || fillTarget > 0f;
        if (mr.enabled != on) mr.enabled = pool.enabled = on;
    }

    void OnDestroy()
    {
        if (mat) Destroy(mat);
    }

    // ---------- the dome mesh ----------

    /// A unit icosphere drawn as hex cells: every triangle gets its own three vertices with barycentric
    /// colours, plus the three corners' random values (one per cell) in UV0. Triangles wholly below
    /// the floor (more than half the radius down) are left out.
    static Mesh Dome(int subdivisions)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        // A pentagon cell right at the top, so the dome looks symmetric.
        var up = Quaternion.FromToRotation(v[0].normalized, Vector3.up);
        for (int i = 0; i < v.Count; i++) v[i] = up * v[i].normalized;
        var f = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        for (int s = 0; s < subdivisions; s++)
        {
            var mids = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mids.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                mids[key] = v.Count - 1;
                return v.Count - 1;
            }
            var next = new List<int>(f.Count * 4);
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = next;
        }
        var pos = new List<Vector3>();
        var col = new List<Color>();
        var rnd = new List<Vector3>();
        var tri = new List<int>();
        for (int i = 0; i < f.Count; i += 3)
        {
            int a = f[i], b = f[i + 1], c = f[i + 2];
            if (v[a].y < -0.5f && v[b].y < -0.5f && v[c].y < -0.5f) continue;
            // Outward winding (Unity: front face where Cross(b-a, c-a) points).
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), v[a] + v[b] + v[c]) < 0f) (b, c) = (c, b);
            var r = new Vector3(Rand(a), Rand(b), Rand(c));
            int n = pos.Count;
            pos.Add(v[a]); pos.Add(v[b]); pos.Add(v[c]);
            col.Add(new Color(1, 0, 0)); col.Add(new Color(0, 1, 0)); col.Add(new Color(0, 0, 1));
            rnd.Add(r); rnd.Add(r); rnd.Add(r);
            tri.Add(n); tri.Add(n + 1); tri.Add(n + 2);
        }
        var mesh = new Mesh { name = "shield_dome" + subdivisions };
        mesh.SetVertices(pos);
        mesh.SetNormals(pos);
        mesh.SetColors(col);
        mesh.SetUVs(0, rnd);
        mesh.SetTriangles(tri, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        return mesh;
    }

    static float Rand(int i) => Mathf.Repeat(Mathf.Sin(i * 12.9898f + 78.233f) * 43758.5453f, 1f);
}
