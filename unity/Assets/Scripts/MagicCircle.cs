using UnityEngine;

/// A spell's magic circle on the floor (the mage's meteor storm and star staff, the priest's sanctuary):
/// a ring of runes turning slowly one way and a star turning the other inside it over a pool of light,
/// each a flat quad (Meshy/MagicCircle) with a texture drawn here once, and a low curtain of light rising
/// off the rim. The circle draws itself in round its edge, the star spins up out of the middle, the
/// curtain rises, sparks drift up while it lasts, Pulse() swells it when the spell lands and End() lets it
/// fade. Its rim is the spell's reach.
public class MagicCircle : MonoBehaviour
{
    public enum Style { Fire, Holy }

    const int Size = 1024;
    const float FadeTime = 0.45f;
    static Texture2D runeTex, fireTex, holyTex, curtainTex;
    static Material baseMat, curtainMat;
    static Mesh quad, tube;

    Transform outer, inner, curtain;
    Renderer outerR, innerR, curtainR;
    MaterialPropertyBlock block;
    Light glow;
    ParticleSystem sparks;
    Vector4 colour;
    Color hue;
    float radius, drawIn, age, pulse, endAt = float.PositiveInfinity, maxLife;

    /// Circles on the floor now (self-tests: they all go once their spell is over).
    public static int Live;

    public static Color Hue(Style s) => s == Style.Fire ? new Color(1f, 0.42f, 0.1f) : new Color(1f, 0.8f, 0.36f);

    /// A circle of `radius` metres at the centre's floor, drawn in over `drawIn` seconds. It goes by
    /// itself after `maxLife` seconds in case whoever made it stops before ending it.
    public static MagicCircle Spawn(Vector3 centre, float radius, Style style, float drawIn = 0.35f, float maxLife = 10f)
    {
        var go = new GameObject("magic_circle");
        go.transform.position = new Vector3(centre.x, centre.y + 0.03f, centre.z);
        var c = go.AddComponent<MagicCircle>();
        Live++;
        c.radius = radius;
        c.drawIn = drawIn;
        c.maxLife = maxLife;
        var hue = c.hue = Hue(style);
        var lin = hue.linear * (style == Style.Fire ? 3.2f : 2.1f);
        c.colour = new Vector4(lin.r, lin.g, lin.b, 1f);
        c.block = new MaterialPropertyBlock();
        c.outer = c.Layer("runes", Runes(), out c.outerR);
        c.inner = c.Layer("star", style == Style.Fire ? FireStar() : HolyStar(), out c.innerR);
        c.inner.localPosition = Vector3.up * 0.005f;
        c.outer.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        c.curtain = c.Curtain(out c.curtainR);
        c.glow = new GameObject("circle_light").AddComponent<Light>();
        c.glow.transform.SetParent(go.transform, false);
        c.glow.transform.localPosition = Vector3.up * 1.2f;
        c.glow.type = LightType.Point;
        c.glow.color = hue;
        c.glow.range = radius * 2.2f;
        c.glow.intensity = 0f;
        c.glow.shadows = LightShadows.None;
        c.sparks = Vfx.CircleSparks(go.transform, radius, style == Style.Fire);
        Vfx.Ring(centre, radius * 1.15f, hue * 1.5f);
        c.Apply(0f, 0f, 0f);
        return c;
    }

    /// Mage and priest players draw theirs on the first cast: make the textures before the fight instead.
    public static void Warm(Style style)
    {
        Runes();
        CurtainTexture();
        if (style == Style.Fire) FireStar();
        else HolyStar();
    }

    /// The spell lands: the circle swells brighter for a moment (once, not a strobe).
    public void Pulse()
    {
        pulse = GameSettings.flashes ? 1f : 0.4f;
        Vfx.Ring(transform.position, radius * 1.1f, hue * 1.5f);
    }

    /// Fade out now (and stop the sparks).
    public void End()
    {
        if (endAt <= age) return;
        endAt = age;
        Vfx.Detach(sparks, 1.4f);
        sparks = null;
    }

    Transform Layer(string name, Texture2D tex, out Renderer r)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = CircleMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        block.Clear();
        block.SetTexture("_MainTex", tex);
        mr.SetPropertyBlock(block);
        r = mr;
        return go.transform;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (age >= maxLife) End();
        pulse = Mathf.MoveTowards(pulse, 0f, dt * 2.5f);
        float into = Mathf.Clamp01(age / drawIn);
        float starIn = Mathf.Clamp01((age - drawIn * 0.3f) / drawIn);
        float fade = Mathf.Clamp01(1f - (age - endAt) / FadeTime);
        Apply(Out(into), Out(starIn), fade);
        // The runes turn slowly; the star spins up fast and settles into a slow turn the other way.
        outer.Rotate(0f, 12f * dt, 0f, Space.Self);
        curtain.Rotate(0f, -18f * dt, 0f, Space.Self);
        inner.Rotate(0f, -(20f + 400f * Mathf.Exp(-age * 4f)) * dt, 0f, Space.Self);
        if (fade <= 0f)
        {
            Vfx.Detach(sparks, 1.4f);
            Destroy(gameObject);
        }
    }

    void OnDestroy() => Live--;

    Transform Curtain(out Renderer r)
    {
        var go = new GameObject("curtain");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = Tube();
        var mr = go.AddComponent<MeshRenderer>();
        if (!curtainMat)
        {
            curtainMat = new Material(Vfx.Additive) { name = "circle_curtain" };
            curtainMat.SetTexture("_BaseMap", CurtainTexture());
            curtainMat.mainTexture = curtainMat.GetTexture("_BaseMap");
            if (curtainMat.HasProperty("_Cull")) curtainMat.SetFloat("_Cull", 0f); // both sides
        }
        mr.sharedMaterial = curtainMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        r = mr;
        return go.transform;
    }

    static float Out(float k) => 1f - (1f - k) * (1f - k) * (1f - k);

    void Apply(float runesIn, float starIn, float fade)
    {
        // A slow breath (0.5 Hz, a few percent): steady light, nothing that flickers.
        float breathe = 1f + 0.06f * Mathf.Sin(age * Mathf.PI);
        float bright = (1f + pulse * 1.3f) * breathe;
        float d = radius * 2f;
        outer.localScale = new Vector3(d, 1f, d) * (1f + 0.12f * (1f - runesIn) + 0.06f * (1f - fade));
        inner.localScale = new Vector3(d, 1f, d) * (0.55f + 0.45f * starIn);
        Set(outerR, runesIn, fade, bright);
        Set(innerR, starIn, fade, bright);
        // The curtain stands as tall as a quarter of the radius (at most 1.2 m), rising as the runes draw in.
        float h = Mathf.Min(1.2f, radius * 0.25f) * runesIn * (0.6f + 0.4f * fade);
        curtain.localScale = new Vector3(radius * 0.985f, Mathf.Max(h, 0.001f), radius * 0.985f);
        curtainR.GetPropertyBlock(block);
        var lit = colour * (0.8f * bright * fade);
        lit.w = 1f; // the curtain's own alpha shapes it; this only tints
        block.SetVector("_BaseColor", lit);
        curtainR.SetPropertyBlock(block);
        glow.intensity = 3f * runesIn * fade * bright;
    }

    void Set(Renderer r, float reveal, float fade, float bright)
    {
        r.GetPropertyBlock(block);
        block.SetVector("_Color", colour * bright);
        block.SetFloat("_Reveal", reveal);
        block.SetFloat("_Fade", fade);
        r.SetPropertyBlock(block);
    }

    static Material CircleMaterial()
    {
        if (baseMat) return baseMat;
        var m = Resources.Load<Material>("VFX/vfx_circle");
        baseMat = m ? m : new Material(Vfx.Additive);
        return baseMat;
    }

    static Mesh Quad()
    {
        if (quad) return quad;
        quad = new Mesh
        {
            name = "circle_quad",
            vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) },
            uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
            colors = new[] { Color.white, Color.white, Color.white, Color.white },
            triangles = new[] { 0, 2, 1, 1, 2, 3 },
        };
        quad.RecalculateNormals();
        quad.RecalculateBounds();
        return quad;
    }

    /// An open cylinder, radius and height 1, its texture wrapped four times round.
    static Mesh Tube()
    {
        if (tube) return tube;
        const int n = 64;
        var v = new Vector3[(n + 1) * 2];
        var uv = new Vector2[v.Length];
        var col = new Color[v.Length];
        var tri = new int[n * 6];
        for (int i = 0; i <= n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v[i * 2] = p;
            v[i * 2 + 1] = p + Vector3.up;
            uv[i * 2] = new Vector2(i * 4f / n, 0f);
            uv[i * 2 + 1] = new Vector2(i * 4f / n, 1f);
            col[i * 2] = col[i * 2 + 1] = Color.white;
        }
        for (int i = 0; i < n; i++)
        {
            int k = i * 2;
            tri[i * 6] = k; tri[i * 6 + 1] = k + 1; tri[i * 6 + 2] = k + 2;
            tri[i * 6 + 3] = k + 2; tri[i * 6 + 4] = k + 1; tri[i * 6 + 5] = k + 3;
        }
        tube = new Mesh { name = "circle_tube", vertices = v, uv = uv, colors = col, triangles = tri };
        tube.RecalculateNormals();
        tube.RecalculateBounds();
        return tube;
    }

    /// Light rising off the rim: bright at the floor, gone at the top, broken into soft rays (which
    /// repeat round the circle, so the seam doesn't show).
    static Texture2D CurtainTexture()
    {
        if (curtainTex) return curtainTex;
        const int w = 256, h = 64;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
            float rays = 0.5f + 0.5f * (0.5f * Mathf.Sin(Mathf.PI * 2f * 3f * u + 1f) + 0.3f * Mathf.Sin(Mathf.PI * 2f * 7f * u + 2f) + 0.2f * Mathf.Sin(Mathf.PI * 2f * 13f * u + 4f));
            float a = Mathf.Pow(1f - v, 2f) * (0.3f + 0.7f * rays * rays) + 0.5f * Mathf.Exp(-(v / 0.05f) * (v / 0.05f));
            px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
        }
        // Round the circle it repeats; up the curtain it must not, or the top edge picks up the bright foot.
        curtainTex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "circle_curtain", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
        curtainTex.SetPixels32(px);
        curtainTex.Apply(true, true);
        return curtainTex;
    }

    // ---------- the textures ----------

    /// The outer ring: a heavy rim, a band of runes between two rings, a dial of ticks.
    static Texture2D Runes()
    {
        if (runeTex) return runeTex;
        var s = new Sheet(Size);
        s.Circle(0.985f, 0.012f);
        s.Circle(0.952f, 0.005f);
        const int glyphs = 40;
        var rng = new System.Random(7);
        for (int i = 0; i < glyphs; i++)
        {
            float a = i * Mathf.PI * 2f / glyphs;
            s.Rune(Glyphs[rng.Next(Glyphs.Length)], 0.913f, a, 0.046f, 0.056f, 0.0075f);
            float between = a + Mathf.PI / glyphs; // a dot between neighbours
            s.Line(Polar(0.911f, between), Polar(0.915f, between), 0.011f);
        }
        s.Circle(0.874f, 0.005f);
        for (int i = 0; i < 72; i++)
        {
            float a = i * Mathf.PI * 2f / 72f;
            s.Line(Polar(i % 6 == 0 ? 0.80f : 0.83f, a), Polar(0.862f, a), i % 6 == 0 ? 0.006f : 0.004f);
        }
        s.Circle(0.80f, 0.004f);
        return runeTex = s.Bake("circle_runes", 0f);
    }

    /// The mage's star: an eight-pointed star with a ring on each point, a triangle in the middle.
    static Texture2D FireStar()
    {
        if (fireTex) return fireTex;
        var s = new Sheet(Size);
        s.Circle(0.74f, 0.010f);
        s.Circle(0.70f, 0.004f);
        s.Star(8, 3, 0.70f, 0.007f, Mathf.PI / 8f);
        for (int i = 0; i < 8; i++)
        {
            var at = Polar(0.70f, Mathf.PI / 8f + i * Mathf.PI / 4f);
            s.Circle(0.05f, 0.005f, at);
        }
        s.Circle(0.27f, 0.006f);
        s.Star(3, 1, 0.27f, 0.006f, Mathf.PI / 2f);
        s.Star(3, 1, 0.27f, 0.004f, -Mathf.PI / 2f);
        s.Circle(0.07f, 0.006f);
        return fireTex = s.Bake("circle_fire", 0.3f);
    }

    /// The priest's star: a twelve-pointed star, a ring of sun rays, a small star of light in the middle.
    static Texture2D HolyStar()
    {
        if (holyTex) return holyTex;
        var s = new Sheet(Size);
        s.Circle(0.74f, 0.010f);
        s.Circle(0.70f, 0.004f);
        s.Star(12, 5, 0.70f, 0.005f, 0f);
        s.Circle(0.40f, 0.006f);
        for (int i = 0; i < 24; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / 24f;
            s.Line(Polar(0.43f, a), Polar(i % 2 == 0 ? 0.53f : 0.48f, a), 0.006f);
        }
        s.Circle(0.17f, 0.005f);
        s.Star(8, 3, 0.15f, 0.004f, 0f);
        return holyTex = s.Bake("circle_holy", 0.18f);
    }

    static Vector2 Polar(float r, float a) => new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);

    /// Runes after the elder futhark, as strokes in a box -0.5..0.5 (x across, y up = outward on the circle).
    static readonly float[][] Glyphs =
    {
        new[] { 0f, -.5f, 0f, .5f, 0f, .1f, .35f, .4f, 0f, -.15f, .35f, .15f },                     // fehu
        new[] { -.3f, -.5f, -.3f, .5f, -.3f, .5f, .3f, .2f, .3f, .2f, .3f, -.5f },                   // uruz
        new[] { 0f, -.5f, 0f, .5f, 0f, .25f, .3f, 0f, .3f, 0f, 0f, -.25f },                          // thurisaz
        new[] { 0f, -.5f, 0f, .5f, 0f, .5f, .3f, .25f, 0f, .2f, .3f, -.05f },                        // ansuz
        new[] { 0f, -.5f, 0f, .5f, 0f, .5f, .3f, .25f, .3f, .25f, 0f, 0f, 0f, 0f, .3f, -.5f },        // raido
        new[] { .25f, .4f, -.2f, 0f, -.2f, 0f, .25f, -.4f },                                         // kaunan
        new[] { -.3f, .5f, .3f, -.5f, -.3f, -.5f, .3f, .5f },                                        // gebo
        new[] { 0f, -.5f, 0f, .5f, 0f, .5f, .3f, .25f, .3f, .25f, 0f, 0f },                          // wunjo
        new[] { -.25f, -.5f, -.25f, .5f, .25f, -.5f, .25f, .5f, -.25f, .2f, .25f, -.2f },            // hagalaz
        new[] { 0f, -.5f, 0f, .5f, -.25f, .2f, .25f, -.15f },                                        // naudiz
        new[] { 0f, -.5f, 0f, .5f, 0f, .5f, .25f, .3f, 0f, -.5f, -.25f, -.3f },                      // eihwaz
        new[] { 0f, -.5f, 0f, .5f, 0f, .1f, -.3f, .45f, 0f, .1f, .3f, .45f },                        // algiz
        new[] { .2f, .5f, -.2f, .15f, -.2f, .15f, .2f, -.15f, .2f, -.15f, -.2f, -.5f },              // sowilo
        new[] { 0f, -.5f, 0f, .5f, 0f, .5f, -.3f, .2f, 0f, .5f, .3f, .2f },                          // tiwaz
        new[] { 0f, -.5f, 0f, .5f, 0f, .5f, .3f, .25f, .3f, .25f, 0f, 0f, 0f, 0f, .3f, -.25f, .3f, -.25f, 0f, -.5f }, // berkanan
        new[] { -.3f, -.5f, -.3f, .5f, .3f, -.5f, .3f, .5f, -.3f, .5f, .3f, .1f, .3f, .5f, -.3f, .1f }, // mannaz
        new[] { 0f, .5f, .3f, .15f, .3f, .15f, 0f, -.2f, 0f, -.2f, -.3f, .15f, -.3f, .15f, 0f, .5f, 0f, -.2f, .3f, -.5f, 0f, -.2f, -.3f, -.5f }, // othala
        new[] { -.3f, -.5f, -.3f, .5f, .3f, -.5f, .3f, .5f, -.3f, .5f, .3f, -.5f, -.3f, -.5f, .3f, .5f }, // dagaz
    };

    /// Lines drawn into a square of n pixels, in coordinates -1..1 across it (1 = the circle's rim).
    sealed class Sheet
    {
        readonly int n;
        readonly float[] ink;

        public Sheet(int n)
        {
            this.n = n;
            ink = new float[n * n];
        }

        Vector2 Px(Vector2 p) => new Vector2((p.x + 1f) * 0.5f * n - 0.5f, (p.y + 1f) * 0.5f * n - 0.5f);

        /// A stroke `width` wide (in the same units), with a pixel of soft edge.
        public void Line(Vector2 a, Vector2 b, float width)
        {
            float hw = width * 0.25f * n;
            Vector2 pa = Px(a), pb = Px(b), ab = pb - pa;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, pb.x) - hw - 2f)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(pa.x, pb.x) + hw + 2f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, pb.y) - hw - 2f)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(pa.y, pb.y) + hw + 2f));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x - pa.x, py = y - pa.y;
                float t = Mathf.Clamp01((px * ab.x + py * ab.y) / len2);
                float dx = px - ab.x * t, dy = py - ab.y * t;
                float v = Mathf.Clamp01(hw + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                int i = y * n + x;
                if (v > ink[i]) ink[i] = v;
            }
        }

        public void Circle(float r, float width, Vector2 centre = default)
        {
            int segs = Mathf.Max(24, Mathf.CeilToInt(r * n * 0.5f));
            for (int i = 0; i < segs; i++)
                Line(centre + Polar(r, i * Mathf.PI * 2f / segs), centre + Polar(r, (i + 1) * Mathf.PI * 2f / segs), width);
        }

        /// A star polygon {points/step} on a circle of radius r, its first point at angle `turn`.
        public void Star(int points, int step, float r, float width, float turn)
        {
            for (int i = 0; i < points; i++)
                Line(Polar(r, turn + i * Mathf.PI * 2f / points), Polar(r, turn + (i + step) * Mathf.PI * 2f / points), width);
        }

        /// A rune standing on the circle at radius r and angle a, `w` across and `h` tall.
        public void Rune(float[] strokes, float r, float a, float w, float h, float width)
        {
            Vector2 Map(float x, float y) => Polar(r + y * h, a - x * w / r);
            for (int k = 0; k + 3 < strokes.Length; k += 4)
                Line(Map(strokes[k], strokes[k + 1]), Map(strokes[k + 2], strokes[k + 3]), width);
        }

        /// Lines in red, their soft glow (and a pool of light in the middle, `pool` strong) in green.
        public Texture2D Bake(string name, float pool)
        {
            var glow = (float[])ink.Clone();
            var tmp = new float[n * n];
            int blur = n / 96;
            for (int pass = 0; pass < 3; pass++)
            {
                Box(glow, tmp, blur, true);
                Box(tmp, glow, blur, false);
            }
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float light = pool * Mathf.Pow(Mathf.Clamp01(1f - r / 0.74f), 1.6f);
                float g = Mathf.Clamp01(glow[i] * 2.2f + light);
                px[i] = new Color32((byte)(ink[i] * 255f), (byte)(g * 255f), 0, 255);
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// One box-blur pass along rows (or columns), `r` pixels each side.
        void Box(float[] src, float[] dst, int r, bool rows)
        {
            float norm = 1f / (2 * r + 1);
            for (int line = 0; line < n; line++)
            {
                int Idx(int k) => rows ? line * n + k : k * n + line;
                float acc = 0f;
                for (int k = 0; k <= r && k < n; k++) acc += src[Idx(k)];
                for (int k = 0; k < n; k++)
                {
                    dst[Idx(k)] = acc * norm;
                    if (k + r + 1 < n) acc += src[Idx(k + r + 1)];
                    if (k - r >= 0) acc -= src[Idx(k - r)];
                }
            }
        }
    }
}
