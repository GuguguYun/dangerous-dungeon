using System.Collections.Generic;
using UnityEngine;

/// The Blender-made dungeon kit: one FBX with one mesh object per piece, placed by code.
public static class Kit
{
    class Piece
    {
        public Mesh mesh;
        public Material[] materials;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    static Dictionary<string, Piece> pieces;

    public static int Count => pieces == null ? 0 : pieces.Count;

    public static bool Load(ThemeDef theme = null)
    {
        pieces = new Dictionary<string, Piece>();
        var root = Resources.Load<GameObject>("Kit/dungeon_kit");
        if (root == null)
        {
            Debug.LogError("KIT missing Resources/Kit/dungeon_kit");
            return false;
        }
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            var mats = new Material[mr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                // Stone and wood slots carry Meshy textures from Blender; the rest use the palette.
                var src = mr.sharedMaterials[i];
                if (src && src.mainTexture)
                {
                    // An instance per floor so the theme tint never touches the imported asset.
                    var m = new Material(src);
                    var tint = theme != null ? theme.TintFor(CleanName(src.name)) : Color.white;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
                    m.color = tint;
                    mats[i] = m;
                }
                else mats[i] = Palette.Get(CleanName(src ? src.name : "missing"));
            }
            // Pose relative to the FBX root, so any import axis/unit conversion is kept.
            Matrix4x4 rel = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            pieces[mf.name] = new Piece
            {
                mesh = mf.sharedMesh,
                materials = mats,
                position = rel.GetColumn(3),
                rotation = rel.rotation,
                scale = rel.lossyScale,
            };
        }
        return true;
    }

    static string CleanName(string n)
    {
        n = n.Replace(" (Instance)", "");
        int dot = n.IndexOf('.');
        return dot > 0 ? n.Substring(0, dot) : n;
    }

    /// World-space size of a piece as placed with identity rotation.
    public static Vector3 Size(string name)
    {
        if (!pieces.TryGetValue(name, out var p)) return Vector3.zero;
        var m = Matrix4x4.TRS(p.position, p.rotation, p.scale);
        var b = p.mesh.bounds;
        var min = Vector3.positiveInfinity;
        var max = Vector3.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var w = m.MultiplyPoint3x4(c);
            min = Vector3.Min(min, w);
            max = Vector3.Max(max, w);
        }
        return max - min;
    }

    public static GameObject Spawn(string name, Vector3 pos, Quaternion rot, Transform parent, bool collide = true)
    {
        if (!pieces.TryGetValue(name, out var p))
        {
            Debug.LogError("KIT piece missing: " + name);
            return null;
        }
        var go = new GameObject(name) { layer = Layers.World };
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var child = new GameObject("mesh") { layer = Layers.World };
        child.transform.SetParent(go.transform, false);
        child.transform.localPosition = p.position;
        child.transform.localRotation = p.rotation;
        child.transform.localScale = p.scale;
        child.AddComponent<MeshFilter>().sharedMesh = p.mesh;
        child.AddComponent<MeshRenderer>().sharedMaterials = p.materials;
        if (collide) child.AddComponent<MeshCollider>().sharedMesh = p.mesh;
        return go;
    }
}
