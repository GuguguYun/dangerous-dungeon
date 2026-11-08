using UnityEngine;

/// A fading glowing copy of a skinned body, left behind by the light mech's boost.
public class Afterimage : MonoBehaviour
{
    const float Life = 0.3f;
    MeshRenderer mr;
    Mesh mesh;
    MaterialPropertyBlock mpb;
    Color color;
    float t;

    public static void Spawn(Transform body, Color c)
    {
        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!smr.enabled) continue;
            var go = new GameObject("afterimage");
            go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            var a = go.AddComponent<Afterimage>();
            a.mesh = new Mesh();
            smr.BakeMesh(a.mesh, true);
            go.AddComponent<MeshFilter>().sharedMesh = a.mesh;
            a.mr = go.AddComponent<MeshRenderer>();
            a.mr.sharedMaterial = Vfx.Fresnel(c, 1.6f, 0.15f, 0f);
            a.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            a.mpb = new MaterialPropertyBlock();
            a.color = c;
        }
    }

    void Update()
    {
        t += Time.deltaTime;
        // Additive: fading the colour to black fades the ghost out.
        float k = Mathf.Clamp01(1f - t / Life);
        mpb.SetColor("_Color", color * (k * k));
        mr.SetPropertyBlock(mpb);
        if (t >= Life) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (mesh) Destroy(mesh);
    }
}

/// The mech's melee arm made visible in melee mode: an energy blade out of the light mech's right
/// hand, glowing fists on the heavy one. Parented to the hand bones so it follows every swing.
public static class MechArm
{
    /// The pieces (one per hand) to show or hide together.
    public static GameObject[] Build(CharacterAnim body, MechDef d)
    {
        var parts = new System.Collections.Generic.List<GameObject>();
        if (!body) return parts.ToArray();
        if (!d.heavy)
        {
            var hand = body.Find("righthand");
            var fore = body.Find("rightforearm");
            if (!hand || !fore) return parts.ToArray();
            // The blade runs on along the forearm, out past the fist.
            Vector3 along = (hand.position - fore.position).normalized;
            float len = d.height * 0.62f;
            var holder = new GameObject("blade").transform;
            holder.SetParent(hand, false);
            holder.position = hand.position + along * 0.12f;
            holder.rotation = Quaternion.LookRotation(along, body.transform.up);
            Undo(holder);
            Part(holder, PrimitiveType.Cube, new Vector3(0f, 0f, len / 2f), new Vector3(0.05f, 0.11f, len), Palette.Get("bullet_player"));
            var sheath = Part(holder, PrimitiveType.Capsule, new Vector3(0f, 0f, len / 2f), new Vector3(0.2f, len / 2f, 0.2f), Vfx.Fresnel(d.color, 1.4f, 0.25f, 0f));
            sheath.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Glow(holder, new Vector3(0f, 0f, len * 0.5f), d.color, 3f, 2.5f);
            parts.Add(holder.gameObject);
        }
        else
        {
            foreach (var side in new[] { "lefthand", "righthand" })
            {
                var hand = body.Find(side);
                if (!hand) continue;
                var holder = new GameObject("fist_" + side).transform;
                holder.SetParent(hand, false);
                Undo(holder);
                Part(holder, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.42f, Vfx.Fresnel(d.color, 1.3f, 0.35f, 0f));
                Glow(holder, Vector3.zero, d.color, 2.5f, 2f);
                parts.Add(holder.gameObject);
            }
        }
        return parts.ToArray();
    }

    // Children of a scaled bone: cancel the bone's scale so sizes below are in metres.
    static void Undo(Transform t)
    {
        var s = t.parent.lossyScale;
        t.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
    }

    static GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.layer = Layers.Player;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static void Glow(Transform parent, Vector3 pos, Color c, float range, float intensity)
    {
        var l = new GameObject("arm_light").AddComponent<Light>();
        l.transform.SetParent(parent, false);
        l.transform.localPosition = pos;
        l.type = LightType.Point;
        l.color = c;
        l.range = range;
        l.intensity = intensity;
    }
}
