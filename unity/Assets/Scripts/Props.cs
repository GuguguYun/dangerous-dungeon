using UnityEngine;

/// Meshy-made props (Resources/Models/Props/<name>, prepared in Blender with the origin at the bottom
/// centre). Each gets a box collider from its bounds; a missing model falls back to the kit crate.
public static class Props
{
    public static bool Has(string name) => Resources.Load<GameObject>("Models/Props/" + name) != null;

    public static GameObject Spawn(string name, Vector3 pos, Quaternion rot, Transform parent, bool collide = true)
    {
        var prefab = Resources.Load<GameObject>("Models/Props/" + name);
        if (!prefab) return Kit.Spawn("crate", pos, rot, parent, collide);
        var go = Object.Instantiate(prefab, parent, false);
        go.name = name;
        go.transform.SetPositionAndRotation(pos, rot);
        foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.World;
        if (collide)
        {
            var b = new Bounds(pos, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            box.size = Quaternion.Inverse(rot) * b.size;
            box.size = new Vector3(Mathf.Abs(box.size.x), Mathf.Abs(box.size.y), Mathf.Abs(box.size.z));
        }
        return go;
    }

    /// A brazier with a flickering fire light on top.
    public static Color FireColor = new Color(1f, 0.6f, 0.3f);

    public static void Brazier(Vector3 pos, Transform parent)
    {
        var go = Spawn("brazier", pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), parent);
        var l = new GameObject("fire").AddComponent<Light>();
        l.transform.SetParent(go.transform, false);
        l.transform.localPosition = Vector3.up * 1.5f;
        l.type = LightType.Point;
        l.color = FireColor;
        l.range = 10f;
        l.intensity = 5f;
        l.gameObject.AddComponent<Flicker>();
        FireSound(l.gameObject, 0.5f);
    }

    /// The sound of a fire burning: a CC0 fireplace recording (PagDev, OpenGameArt; low and soft, not a
    /// hiss of synthesised crackles, which the user found piercing), heard only up close. Each starts at
    /// its own point in the loop so two side by side don't crackle together.
    public static AudioSource FireSound(GameObject on, float volume)
    {
        var clip = Sfx.Clip("fire");
        if (!clip) return null;
        var s = on.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = 1.5f;
        s.maxDistance = 9f;
        s.dopplerLevel = 0f;
        s.volume = volume;
        s.Play();
        s.time = Random.Range(0f, clip.length);
        return s;
    }
}
