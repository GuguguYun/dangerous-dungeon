using System.Collections.Generic;
using UnityEngine;

/// The smear a swung blade leaves for a moment: a soft ribbon between the hilt and the tip that fades
/// within a tenth of a second. It follows the real blade through the animation, so it reads as motion
/// blur rather than a separate arc of light (the old glowing fan was disliked). Alpha-blended, not
/// additive: a pale wake, never a flash.
[DefaultExecutionOrder(100)] // after the animation and the hand IK have placed the blade
public class BladeSmear : MonoBehaviour
{
    const float Life = 0.11f;
    const int Steps = 3; // in-between points per frame, so fast swings stay curved

    struct Sample { public Vector3 hilt, tip; public float t; }

    readonly List<Sample> samples = new List<Sample>();
    Transform pivot;
    float hilt, tip; // distances along the pivot's Z (the blade's direction)
    Color color;
    float activeUntil;
    Mesh mesh;
    static Material mat;

    /// A smear for the blade held by pivot, from `hiltAt` to `tipAt` metres along its Z axis.
    public static BladeSmear Attach(Transform pivot, float hiltAt, float tipAt)
    {
        var go = new GameObject("blade_smear");
        var s = go.AddComponent<BladeSmear>();
        s.pivot = pivot;
        s.hilt = hiltAt;
        s.tip = tipAt;
        s.mesh = new Mesh { name = "smear" };
        s.mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = s.mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Material();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return s;
    }

    /// Leave a smear for the next `seconds` (one swing), in this tint.
    public void Swing(float seconds, Color c)
    {
        activeUntil = Time.time + seconds;
        color = c;
    }

    static Material Material()
    {
        if (mat) return mat;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        mat = new Material(shader) { name = "blade_smear" };
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f); // both sides
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        return mat;
    }

    void LateUpdate()
    {
        if (!pivot)
        {
            Destroy(gameObject);
            return;
        }
        float now = Time.time;
        if (now < activeUntil && pivot.gameObject.activeInHierarchy)
        {
            var s = new Sample { hilt = pivot.TransformPoint(0f, 0f, hilt), tip = pivot.TransformPoint(0f, 0f, tip), t = now };
            if (samples.Count > 0)
            {
                // In-between points: arc the tip around the hilt so a quick swing stays a curve.
                var last = samples[samples.Count - 1];
                for (int i = 1; i < Steps; i++)
                {
                    float k = i / (float)Steps;
                    Vector3 h = Vector3.Lerp(last.hilt, s.hilt, k);
                    Vector3 a = last.tip - last.hilt, b = s.tip - s.hilt;
                    samples.Add(new Sample { hilt = h, tip = h + Vector3.Slerp(a, b, k), t = Mathf.Lerp(last.t, now, k) });
                }
            }
            samples.Add(s);
        }
        samples.RemoveAll(x => now - x.t > Life);
        Build(now);
    }

    void Build(float now)
    {
        mesh.Clear();
        if (samples.Count < 2) return;
        int n = samples.Count;
        var v = new Vector3[n * 2];
        var c = new Color[n * 2];
        var tris = new int[(n - 1) * 6];
        for (int i = 0; i < n; i++)
        {
            float age = Mathf.Clamp01((now - samples[i].t) / Life);
            float a = (1f - age) * (1f - age);
            v[i * 2] = samples[i].hilt;
            v[i * 2 + 1] = samples[i].tip;
            c[i * 2] = new Color(color.r, color.g, color.b, color.a * a * 0.15f);  // faint at the hilt
            c[i * 2 + 1] = new Color(color.r, color.g, color.b, color.a * a);     // strongest at the tip
            if (i == n - 1) continue;
            int q = i * 6, o = i * 2;
            tris[q] = o; tris[q + 1] = o + 1; tris[q + 2] = o + 3;
            tris[q + 3] = o; tris[q + 4] = o + 3; tris[q + 5] = o + 2;
        }
        mesh.vertices = v;
        mesh.colors = c;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
    }
}
