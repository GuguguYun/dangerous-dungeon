using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Pictures for the UI, made from the game's own models far below the level: a small icon for every
/// weapon (rendered once at startup), and live turntable previews of the four classes for the
/// class-select cards (only rendered while that screen is open).
public class IconStudio : MonoBehaviour
{
    public static IconStudio I;
    const int Layer = 30;
    static readonly Vector3 Origin = new Vector3(0f, -600f, 0f);
    readonly Dictionary<string, Texture2D> weaponIcons = new Dictionary<string, Texture2D>();
    readonly Dictionary<string, RenderTexture> classTargets = new Dictionary<string, RenderTexture>();
    readonly Dictionary<string, Texture2D> classViews = new Dictionary<string, Texture2D>();
    float copyT;
    readonly Dictionary<string, CharacterAnim> classBodies = new Dictionary<string, CharacterAnim>();
    readonly List<Camera> classCams = new List<Camera>();
    readonly List<Transform> turntables = new List<Transform>();
    public bool previewsOn;
    public string hovered;
    string lastHovered;

    public static IconStudio Create()
    {
        if (I) return I; // kept across floors: the icons are rendered once
        var go = new GameObject("icon_studio");
        DontDestroyOnLoad(go);
        I = go.AddComponent<IconStudio>();
        I.StartCoroutine(I.BuildWeaponIcons());
        return I;
    }

    public Texture2D Weapon(string id) => id != null && weaponIcons.TryGetValue(id, out var t) ? t : null;
    public Texture ClassView(string id) => classViews.TryGetValue(id, out var t) ? t : null;

    Camera MakeCamera(RenderTexture rt, Vector3 at, Vector3 look)
    {
        var cam = new GameObject("studio_cam").AddComponent<Camera>();
        cam.transform.SetParent(transform, false);
        cam.transform.position = at;
        cam.transform.LookAt(look);
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.targetTexture = rt;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 30f;
        cam.depth = -10;
        return cam;
    }

    void Light(Vector3 at, float range, float intensity, Color c)
    {
        var l = new GameObject("studio_light").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.position = at;
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = c;
    }

    IEnumerator BuildWeaponIcons()
    {
        var rt = new RenderTexture(160, 96, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Vector3 spot = Origin + new Vector3(-50f, 0f, 0f);
        var cam = MakeCamera(rt, spot + new Vector3(0f, 0.2f, -3f), spot);
        cam.orthographic = true;
        Light(spot + new Vector3(-1.5f, 2f, -2.5f), 8f, 6f, new Color(1f, 0.95f, 0.85f));
        Light(spot + new Vector3(2f, -0.5f, -1.5f), 6f, 2.5f, new Color(0.6f, 0.75f, 1f));
        var holder = new GameObject("icon_holder").transform;
        holder.SetParent(transform, false);
        holder.position = spot;
        foreach (var w in Weapons.All)
        {
            var v = Weapons.MakeVisual(w, holder, Layer);
            // Lay it on its side, muzzle to the right, a little tilted towards the camera.
            holder.rotation = Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(-15f, 0f, 0f);
            if (w.grip == WeaponGrip.Staff || w.id == "spear") holder.rotation *= Quaternion.Euler(-35f, 0f, 0f);
            var b = new Bounds(spot, Vector3.zero);
            bool any = false;
            foreach (var r in v.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            v.transform.position += spot - b.center;
            cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x * rt.height / rt.width) * 1.15f;
            cam.enabled = true;
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            weaponIcons[w.id] = tex;
            Destroy(v);
            yield return null;
        }
        cam.enabled = false;
        Debug.Log($"ICONS {weaponIcons.Count}");
    }

    IEnumerator CopyViews()
    {
        yield return new WaitForEndOfFrame();
        var prev = RenderTexture.active;
        foreach (var kv in classTargets)
        {
            RenderTexture.active = kv.Value;
            var tex = classViews[kv.Key];
            tex.ReadPixels(new Rect(0, 0, kv.Value.width, kv.Value.height), 0, 0);
            tex.Apply();
        }
        RenderTexture.active = prev;
    }

    /// Turntables for the class cards (built on first use).
    public void ShowClassPreviews(bool on)
    {
        if (on && turntables.Count == 0) BuildClassPreviews();
        previewsOn = on;
        foreach (var c in classCams) if (c) c.enabled = on;
    }

    void BuildClassPreviews()
    {
        int i = 0;
        foreach (var cls in Classes.All)
        {
            Vector3 spot = Origin + new Vector3(i * 12f, 0f, 0f);
            var table = new GameObject("turntable_" + cls.id).transform;
            table.SetParent(transform, false);
            table.position = spot;
            table.rotation = Quaternion.Euler(0f, 200f, 0f);
            var body = CharacterAnim.Spawn(cls.model, table, Layer);
            if (body) classBodies[cls.id] = body;
            var rt = new RenderTexture(360, 420, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            classTargets[cls.id] = rt;
            // Drawn from a copied texture (like the weapon icons): drawing the render target directly
            // came out washed out and see-through in IMGUI.
            classViews[cls.id] = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var cam = MakeCamera(rt, spot + new Vector3(0f, 1.0f, 3.2f), spot + Vector3.up * 0.8f);
            cam.fieldOfView = 34f;
            // An opaque backdrop in the class colour, so the picture is drawn without alpha
            // (some Meshy textures carry alpha that would make the hero look see-through).
            // Dark backdrops (the project is linear: these read much lighter than the numbers suggest).
            var tint = cls.id switch { "knight" => new Color(0.012f, 0.02f, 0.045f), "assassin" => new Color(0.025f, 0.012f, 0.04f), "mage" => new Color(0.045f, 0.015f, 0.01f), _ => new Color(0.04f, 0.035f, 0.012f) };
            cam.backgroundColor = tint;
            // Same tone mapping as the game camera, or the heroes come out flat and washed out.
            var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (!data) data = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            classCams.Add(cam);
            // Gentle key and rim lights: the sun and ambient light already reach down here, and strong
            // close point lights overexposed the heroes into a pale, washed-out look.
            // A warm key from the front and above, and a strong cool rim from behind for the silhouette.
            Light(spot + new Vector3(-1.4f, 2.6f, 2.4f), 8f, 2.4f, new Color(1f, 0.92f, 0.8f));
            Light(spot + new Vector3(0.8f, 2.2f, -1.6f), 5f, 4f, new Color(0.5f, 0.75f, 1f));
            turntables.Add(table);
            i++;
        }
    }

    void Update()
    {
        if (!previewsOn) return;
        copyT -= Time.unscaledDeltaTime;
        if (copyT <= 0f)
        {
            copyT = 1f / 15f; // the turntables only need ~15 updates a second
            StartCoroutine(CopyViews());
        }
        foreach (var t in turntables) t.Rotate(Vector3.up, 18f * Time.unscaledDeltaTime, Space.World);
        if (hovered != lastHovered)
        {
            lastHovered = hovered;
            // The hovered hero shows off.
            if (hovered != null && classBodies.TryGetValue(hovered, out var b))
                b.Trigger(b.Has("cast") ? "cast" : b.Has("slash") ? "slash" : "shoot", 1f, 0.5f);
        }
    }
}
