using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// Long robes (priest, mage): Meshy's auto-rig binds the skirt of the robe to the leg bones, so a run
/// tears it open between the legs and the hem pops in and out several times a second, which reads as
/// flicker and brings on 3D sickness (measured with -flicker / scripts/flicker.py: the priest's running
/// frames flickered twice as much as the knight's). Here the skirt is re-weighted to hang mostly from
/// the hips with a little of both thighs, so it sways as one piece; the legs themselves (close to the
/// leg bones) and the feet keep their weights. Saved as Chars/<id>/robe.asset, which CharacterAnim
/// uses in place of the imported mesh.
public static class RobeFix
{
    static readonly string[] Robed = { "priest", "mage" };

    public static void Run()
    {
        foreach (var id in Robed)
        {
            string dir = $"Assets/Resources/Models/Chars/{id}";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/rigged.fbx");
            var smr = model ? model.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (!smr || !smr.sharedMesh) continue;
            var mesh = Object.Instantiate(smr.sharedMesh);
            mesh.name = id + "_robe";
            int changed = Fix(mesh, smr.bones);
            AssetDatabase.DeleteAsset($"{dir}/robe.asset");
            if (changed == 0) continue;
            AssetDatabase.CreateAsset(mesh, $"{dir}/robe.asset");
            Debug.Log($"SETUP robe {id}: {changed} of {mesh.vertexCount} vertices now hang from the hips");
        }
    }

    static int Fix(Mesh mesh, Transform[] bones)
    {
        int Index(string n) => System.Array.FindIndex(bones, b => b && b.name == n);
        int hips = Index("Hips"), head = Index("Head");
        int lu = Index("LeftUpLeg"), ll = Index("LeftLeg"), lf = Index("LeftFoot");
        int ru = Index("RightUpLeg"), rl = Index("RightLeg"), rf = Index("RightFoot");
        if (hips < 0 || head < 0 || lu < 0 || ll < 0 || lf < 0 || ru < 0 || rl < 0 || rf < 0) return 0;
        var bp = mesh.bindposes;
        Vector3 P(int b) => bp[b].inverse.GetColumn(3); // the bone's position in mesh space
        Vector3 up = (P(head) - P(hips)).normalized;
        float hipY = Vector3.Dot(P(hips), up);
        float footY = (Vector3.Dot(P(lf), up) + Vector3.Dot(P(rf), up)) / 2f;
        float width = Vector3.Distance(P(lu), P(ru));
        // Leg flesh lies within legR of a leg bone; cloth further out turns over to the hips across blend.
        float legR = width * 0.38f, blend = width * 0.3f;
        float bottom = footY + (hipY - footY) * 0.12f; // below this are the feet and ankles
        bool IsLeg(int b) => b == lu || b == ll || b == lf || b == ru || b == rl || b == rf;

        var verts = mesh.vertices;
        var weights = mesh.boneWeights;
        int changed = 0;
        var mix = new Dictionary<int, float>();
        for (int i = 0; i < verts.Length; i++)
        {
            float y = Vector3.Dot(verts[i], up);
            if (y > hipY || y < bottom) continue;
            var w = weights[i];
            float legShare = (IsLeg(w.boneIndex0) ? w.weight0 : 0f) + (IsLeg(w.boneIndex1) ? w.weight1 : 0f)
                           + (IsLeg(w.boneIndex2) ? w.weight2 : 0f) + (IsLeg(w.boneIndex3) ? w.weight3 : 0f);
            if (legShare < 0.3f) continue;
            float d = Mathf.Min(Mathf.Min(Seg(verts[i], P(lu), P(ll)), Seg(verts[i], P(ll), P(lf))),
                                Mathf.Min(Seg(verts[i], P(ru), P(rl)), Seg(verts[i], P(rl), P(rf))));
            float k = Mathf.SmoothStep(0f, 1f, (d - legR) / blend);
            if (k < 0.001f) continue;
            mix.Clear();
            void Add(int b, float v) { if (v > 0f) mix[b] = (mix.TryGetValue(b, out var o) ? o : 0f) + v; }
            Add(w.boneIndex0, w.weight0 * (1f - k));
            Add(w.boneIndex1, w.weight1 * (1f - k));
            Add(w.boneIndex2, w.weight2 * (1f - k));
            Add(w.boneIndex3, w.weight3 * (1f - k));
            Add(hips, 0.6f * k);
            Add(lu, 0.2f * k);
            Add(ru, 0.2f * k);
            weights[i] = Top4(mix);
            changed++;
        }
        mesh.boneWeights = weights;
        return changed;
    }

    static float Seg(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
        return Vector3.Distance(p, a + ab * t);
    }

    static BoneWeight Top4(Dictionary<int, float> mix)
    {
        var list = new List<KeyValuePair<int, float>>(mix);
        list.Sort((x, y) => y.Value.CompareTo(x.Value));
        float sum = 0f;
        for (int i = 0; i < Mathf.Min(4, list.Count); i++) sum += list[i].Value;
        var bw = new BoneWeight();
        for (int i = 0; i < Mathf.Min(4, list.Count); i++)
        {
            float v = list[i].Value / sum;
            switch (i)
            {
                case 0: bw.boneIndex0 = list[i].Key; bw.weight0 = v; break;
                case 1: bw.boneIndex1 = list[i].Key; bw.weight1 = v; break;
                case 2: bw.boneIndex2 = list[i].Key; bw.weight2 = v; break;
                default: bw.boneIndex3 = list[i].Key; bw.weight3 = v; break;
            }
        }
        return bw;
    }
}
