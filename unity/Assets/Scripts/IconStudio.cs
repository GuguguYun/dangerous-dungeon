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
    public bool Ready => portraits.Count == Classes.All.Count && faces.Count == Classes.All.Count;

    /// Save every icon and portrait as PNG (-icons <dir>: checking them without a window on screen).
    public void Dump(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        foreach (var kv in weaponIcons) System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"weapon_{kv.Key}.png"), kv.Value.EncodeToPNG());
        foreach (var kv in portraits) System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"portrait_{kv.Key}.png"), kv.Value.EncodeToPNG());
        foreach (var kv in facesRaw) System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"face_{kv.Key}.png"), kv.Value.EncodeToPNG());
    }
    // Still portraits made exactly like the weapon icons (that path renders true colours).
    readonly Dictionary<string, Texture2D> portraits = new Dictionary<string, Texture2D>();
    public Texture ClassView(string id) => portraits.TryGetValue(id, out var p) ? p : classViews.TryGetValue(id, out var t) ? t : null;

    // Head-and-shoulders close-ups for the round medallions (HUD, run summary), cut to a circle.
    readonly Dictionary<string, Texture2D> faces = new Dictionary<string, Texture2D>();
    readonly Dictionary<string, Texture2D> facesRaw = new Dictionary<string, Texture2D>(); // as rendered, for -icons
    public Texture2D Face(string id) => faces.TryGetValue(id, out var f) ? f : null;

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

    Light Light(Vector3 at, float range, float intensity, Color c)
    {
        var l = new GameObject("studio_light").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.position = at;
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = c;
        return l;
    }

    IEnumerator BuildWeaponIcons()
    {
        var rt = new RenderTexture(160, 96, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Vector3 spot = Origin + new Vector3(-50f, 0f, 0f);
        var cam = MakeCamera(rt, spot + new Vector3(0f, 0.2f, -3f), spot);
        cam.orthographic = true;
        cam.enabled = false; // rendered on demand
        var key = Light(spot + new Vector3(-1.5f, 2f, -2.5f), 8f, 6f, new Color(1f, 0.95f, 0.85f));
        var fill = Light(spot + new Vector3(2f, -0.5f, -1.5f), 6f, 2.5f, new Color(0.6f, 0.75f, 1f));
        var holder = new GameObject("icon_holder").transform;
        holder.SetParent(transform, false);
        holder.position = spot;
        var icons = new List<WeaponDef>(Weapons.All);
        icons.AddRange(Weapons.SkillWeapons); // the class skills' own weapons show on the HUD too
        foreach (var w in icons)
        {
            var v = Weapons.MakeVisual(w, holder, Layer);
            // Lay it on its side, muzzle to the right, a little tilted towards the camera.
            holder.rotation = Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(-15f, 0f, 0f);
            if (w.grip == WeaponGrip.Staff || w.id == "spear") holder.rotation *= Quaternion.Euler(-35f, 0f, 0f);
            // The kunai lies flat (its blade faces up, the way it flies): roll it so the icon shows the
            // blade, ring and feathers, not a thin silver edge.
            if (w.id == "raven_darts") holder.rotation *= Quaternion.Euler(0f, 0f, 90f);
            var b = new Bounds(spot, Vector3.zero);
            bool any = false;
            foreach (var r in v.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            v.transform.position += spot - b.center;
            cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x * rt.height / rt.width) * 1.15f;
            cam.Render(); // explicit: also works without a window (batch mode never renders cameras by itself)
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
        // Hero portraits, the same way: orthographic, no post-processing, clear background. Softer lights
        // than for the shiny weapons: at full strength the cloth and skin wash out (dark outfits most).
        var prt = new RenderTexture(360, 420, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = prt;
        key.intensity = 2.2f;
        fill.intensity = 1.0f;
        foreach (var cls in Classes.All)
        {
            var body = CharacterAnim.Spawn(cls.model, holder, Layer);
            if (!body) continue;
            holder.rotation = Quaternion.Euler(0f, 160f, 0f); // facing the camera, turned a little
            yield return null; // let the idle pose apply
            var b = new Bounds(spot, Vector3.zero);
            bool any = false;
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            body.transform.position += spot - b.center;
            cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x * prt.height / prt.width) * 1.08f;
            cam.Render(); // explicit: also works without a window (batch mode never renders cameras by itself)
            var prev = RenderTexture.active;
            RenderTexture.active = prt;
            var tex = new Texture2D(prt.width, prt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, prt.width, prt.height), 0, 0);
            Grade(tex);
            RenderTexture.active = prev;
            portraits[cls.id] = tex;
            Destroy(body.gameObject);
            yield return null;
        }
        // Faces: the full portrait cropped to the head and blown up to a medallion on a big screen came out
        // blurred and grey. A 512 px close-up of head and shoulders instead, lit warm from the front-left,
        // filled cool from the right and rimmed from behind.
        var frt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = frt;
        key.intensity = 3.2f;
        fill.intensity = 1.4f;
        var rim = Light(spot + new Vector3(0.6f, 1.6f, 1.6f), 6f, 3f, new Color(0.75f, 0.85f, 1f));
        foreach (var cls in Classes.All)
        {
            var body = CharacterAnim.Spawn(cls.model, holder, Layer);
            if (!body) continue;
            holder.rotation = Quaternion.Euler(0f, 165f, 0f);
            yield return null; // let the idle pose apply
            // Frame the head from a first, wider picture: the topmost opaque block of it is the head
            // (with hair, hat, halo). Guessing from bones or skinned vertices left the faces off-centre.
            var b = new Bounds(body.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float h = b.size.y;
            body.transform.position += spot - new Vector3(b.center.x, b.max.y - h * 0.25f, b.center.z);
            float wide = h * 0.3f;
            cam.orthographicSize = wide;
            cam.Render();
            var prev0 = RenderTexture.active;
            RenderTexture.active = frt;
            var probe = new Texture2D(frt.width, frt.height, TextureFormat.RGBA32, false);
            probe.ReadPixels(new Rect(0, 0, frt.width, frt.height), 0, 0);
            RenderTexture.active = prev0;
            var px = probe.GetPixels32();
            int n = frt.width, top = -1;
            for (int y = n - 1; y >= 0 && top < 0; y--)
                for (int x = 0; x < n; x++) if (px[y * n + x].a > 20) { top = y; break; }
            float centreX = 0.5f, centreY = 0.5f, span = 0.6f;
            if (top >= 0)
            {
                // The head of these chibi heroes is about the top 36% of their height.
                int headPx = Mathf.RoundToInt(h * 0.36f / (2f * wide) * n), bottom = Mathf.Max(0, top - headPx);
                int minX = n, maxX = -1;
                for (int y = bottom; y <= top; y++)
                    for (int x = 0; x < n; x++)
                        if (px[y * n + x].a > 20) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
                if (maxX >= minX)
                {
                    span = Mathf.Max(maxX - minX, top - bottom) / (float)n;
                    centreX = (minX + maxX) * 0.5f / n;
                    // A little below the block's middle: hats and halos sit on top, the face lower down.
                    centreY = (bottom + top) * 0.5f / n - span * 0.12f;
                }
            }
            Destroy(probe);
            // Move the head to the middle (in the camera's plane) and fill most of the circle with it.
            body.transform.position -= cam.transform.right * (centreX - 0.5f) * 2f * wide + cam.transform.up * (centreY - 0.5f) * 2f * wide;
            cam.orthographicSize = Mathf.Max(0.05f, span * wide * 1.15f);
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = frt;
            var raw = new Texture2D(frt.width, frt.height, TextureFormat.RGBA32, false);
            raw.ReadPixels(new Rect(0, 0, frt.width, frt.height), 0, 0);
            raw.Apply();
            RenderTexture.active = prev;
            facesRaw[cls.id] = raw;
            faces[cls.id] = ForMedal(raw);
            Destroy(body.gameObject);
            yield return null;
        }
        Destroy(rim.gameObject);
        Debug.Log($"ICONS {weaponIcons.Count} portraits {portraits.Count} faces {faces.Count}");
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

    /// A light stand-in for the game camera's tone mapping: a little more contrast and saturation.
    /// A copy cut to a soft-edged circle, its colours decoded once more: IMGUI shows textures encoded
    /// twice in this linear project (see UiArtImporter), which is what made the faces grey and pale.
    static Texture2D ForMedal(Texture2D raw)
    {
        int w = raw.width, hgt = raw.height;
        var px = raw.GetPixels();
        float r0 = Mathf.Min(w, hgt) * 0.5f;
        for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var c = px[i];
                float d = new Vector2(x + 0.5f - w * 0.5f, y + 0.5f - hgt * 0.5f).magnitude;
                float edge = Mathf.Clamp01((r0 - d) / 2f);
                px[i] = new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a * edge);
            }
        var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    static void Grade(Texture2D tex)
    {
        var px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.a <= 0.01f) continue;
            float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            Color sat = Color.LerpUnclamped(new Color(l, l, l), c, 1.3f);
            Color con = new Color((sat.r - 0.5f) * 1.12f + 0.47f, (sat.g - 0.5f) * 1.12f + 0.47f, (sat.b - 0.5f) * 1.12f + 0.47f);
            px[i] = new Color(Mathf.Clamp01(con.r), Mathf.Clamp01(con.g), Mathf.Clamp01(con.b), c.a);
        }
        tex.SetPixels(px);
        tex.Apply();
    }

    /// Turntables for the class cards (built on first use).
    public void ShowClassPreviews(bool on)
    {
        if (portraits.Count > 0) on = false; // portraits replace the live turntables
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
