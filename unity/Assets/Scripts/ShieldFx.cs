using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// The energy-shield dome for the knight's 誓约壁垒 and for shielded elites (Meshy/Shield): geodesic
/// hex cells on a sphere whose centre sits a little above the floor, so the dome is cut cleanly by the
/// floor with a glowing seam (lifted clear of the floor it is a whole bubble). Cells grow in when it goes
/// up and dissolve when it drops; a struck spot sends out a ripple. One material instance per shield
/// (it carries the impact points).
public class ShieldFx : MonoBehaviour
{
    static Mesh fineDome, coarseDome;
    static Material baseMat;
    static readonly int FillId = Shader.PropertyToID("_Fill"), FloorId = Shader.PropertyToID("_FloorY"), NowId = Shader.PropertyToID("_Now");
    static readonly int[] HitIds = { Shader.PropertyToID("_Hit0"), Shader.PropertyToID("_Hit1"), Shader.PropertyToID("_Hit2"), Shader.PropertyToID("_Hit3") };

    public float radius;  // radius of the sphere (world, at the parent's scale 1)
    float lift;           // how far the sphere's centre sits above the floor (this object sits on the floor)
    Transform dome;
    Material mat;
    MeshRenderer mr;
    Renderer pool;
    float fill, fillTarget, fillSpeed = 3f, strength = 1f, poolSize;
    int nextHit;

    public bool Up => fillTarget > 0f;
    public Vector3 Centre => dome.position;
    public float WorldRadius => radius * transform.lossyScale.x; // an enemy pops in from a tiny scale

    /// The centre's height above the floor. Raise it and the floor cuts off less of the sphere; above
    /// one radius the dome floats as a whole bubble. The light pool on the floor shrinks to match.
    public float Lift
    {
        get => lift;
        set
        {
            lift = value;
            dome.localPosition = Vector3.up * lift;
            float footprint = Mathf.Sqrt(Mathf.Max(0f, radius * radius - lift * lift));
            pool.transform.localScale = new Vector3(footprint * poolSize, 1f, footprint * poolSize);
        }
    }

    /// fine: smaller cells (the knight's big dome); field/accent are sRGB hues times a linear intensity.
    /// The object sits on the floor (parent it at the owner's feet, or move it there each frame).
    public static ShieldFx Create(string name, Color field, float fieldIntensity, Color accent, float accentIntensity,
        float radius, float lift, bool fine, Transform parent)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        var fx = go.AddComponent<ShieldFx>();
        fx.radius = radius;
        fx.dome = new GameObject("dome").transform;
        fx.dome.SetParent(go.transform, false);
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
        fx.pool = LootFx.Glow(go.transform, field, 0.3f * fieldIntensity, 1f, 0f, 0f);
        fx.pool.enabled = false;
        fx.poolSize = 2.2f;
        fx.Lift = lift;
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
        return Centre + (d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward) * WorldRadius;
    }

    /// Where a blow travelling along dir that ended at point (on the body inside) came through the dome:
    /// the path traced back to the sphere.
    public Vector3 Entry(Vector3 point, Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-6f) return SurfaceToward(point);
        dir.Normalize();
        float r = WorldRadius;
        Vector3 m = point - Centre;
        // |m - t dir| = r, furthest back along the path (t >= 0 when the point is inside).
        float b = Vector3.Dot(m, dir);
        float disc = b * b - m.sqrMagnitude + r * r;
        if (disc < 0f) return SurfaceToward(point - dir); // the path never crossed the dome
        return point - dir * (b + Mathf.Sqrt(disc));
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
    /// colours, plus the three corners' random values (one per cell) in UV0. The whole sphere: the
    /// shader clips what is under the floor, wherever the dome is lifted to.
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
