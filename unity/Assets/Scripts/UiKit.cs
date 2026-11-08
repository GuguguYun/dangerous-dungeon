using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// The game's UI look: painted Hearthstone-style art (stone-and-gold plates, wooden buttons, ribbon
/// banners, hero and blessing cards, a gold skill medallion) from Resources/UI, made by
/// scripts/ui_art.py and blender/ui_process.py. Plates and buttons are drawn nine-sliced so their
/// ornate corners keep their shape at any size. The older runtime-made rounded panels remain as a
/// fallback when the art is missing.
public static class UiKit
{
    public static readonly Color Gold = new Color(0.97f, 0.80f, 0.46f);
    // Fairly opaque: the project blends in linear space, where a translucent dark panel over a bright
    // scene comes out much lighter than its alpha suggests.
    public static readonly Color Ink = new Color(0.04f, 0.05f, 0.08f, 0.95f);
    public static readonly Color Paper = new Color(0.95f, 0.93f, 0.88f);
    public static readonly Color Dim = new Color(0.70f, 0.71f, 0.78f);
    public const int Radius = 12;

    public static GUIStyle Panel, Button, Card, CardHover, Track, Pill;
    static Texture2D white, vignette, glow, track, spot;
    static Font font;

    public static void Init(Font f)
    {
        if (Panel != null) return;
        font = f;
        var panelTex = Round(48, Radius, Ink, new Color(Gold.r, Gold.g, Gold.b, 0.55f), 1.6f);
        var buttonTex = Round(48, Radius, new Color(0.07f, 0.08f, 0.13f, 0.96f), new Color(Gold.r, Gold.g, Gold.b, 0.6f), 1.6f);
        var buttonHover = Round(48, Radius, new Color(0.18f, 0.16f, 0.12f, 0.95f), Gold, 2.4f);
        var buttonDown = Round(48, Radius, new Color(0.30f, 0.24f, 0.12f, 0.95f), Gold, 2.4f);
        var cardTex = Round(48, Radius, new Color(0.04f, 0.05f, 0.09f, 0.96f), new Color(1f, 1f, 1f, 0.18f), 1.4f);
        var cardHover = Round(48, Radius, new Color(0.08f, 0.08f, 0.13f, 0.97f), Gold, 2.6f);
        var trackTex = Round(24, 6, new Color(0.03f, 0.025f, 0.02f, 0.95f), new Color(0.75f, 0.58f, 0.3f, 0.9f), 1.6f);
        track = trackTex;
        glow = Glow(64, 22);
        var pillTex = Round(32, 14, new Color(0.03f, 0.04f, 0.07f, 0.9f), new Color(1f, 1f, 1f, 0.10f), 1f);
        white = Texture2D.whiteTexture;

        Panel = Style(panelTex, Radius);
        Card = Style(cardTex, Radius);
        CardHover = Style(cardHover, Radius);
        Track = Style(trackTex, 6);
        Pill = Style(pillTex, 14);
        Button = Style(buttonTex, Radius);
        Button.hover.background = buttonHover;
        Button.active.background = buttonDown;
        Button.focused.background = buttonTex;
        Button.font = font;
        Button.fontSize = 24;
        Button.alignment = TextAnchor.MiddleCenter;
        Button.normal.textColor = Paper;
        Button.hover.textColor = Gold;
        Button.active.textColor = Gold;
        Button.richText = true;
        Button.wordWrap = true;
    }

    static GUIStyle Style(Texture2D bg, int border)
    {
        var s = new GUIStyle { border = new RectOffset(border, border, border, border), padding = new RectOffset(12, 12, 8, 8) };
        s.normal.background = bg;
        s.font = font;
        return s;
    }

    /// A rounded rectangle (signed distance, antialiased), filled, with an inner border. Colours are
    /// stored linear: an sRGB texture drawn by IMGUI comes out encoded once more (see UiArtImporter).
    public static Texture2D Round(int size, int radius, Color fill, Color border, float borderWidth)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - radius; // <0 inside
                float inside = Mathf.Clamp01(0.5f - d);
                float rim = borderWidth > 0f ? Mathf.Clamp01(d + borderWidth + 0.5f) : 0f;
                var c = Lin(Color.Lerp(fill, border, rim));
                c.a *= inside;
                px[y * size + x] = c;
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    /// A capsule (a circle when w == h) along the longer side, filled, with an inner border; antialiased
    /// and mipmapped (drawn a few pixels wide it would otherwise come out as a diamond), stored linear like
    /// Round. Drawn white-filled and tinted (Tinted), the border stays dark.
    public static Texture2D Capsule(int w, int h, Color fill, Color border, float borderWidth)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var px = new Color[w * h];
        float r = Mathf.Min(w, h) / 2f;
        Vector2 a = w < h ? new Vector2(w / 2f, r) : new Vector2(r, h / 2f);
        Vector2 b = w < h ? new Vector2(w / 2f, h - r) : new Vector2(w - r, h / 2f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float k = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                float d = Vector2.Distance(p, a + (b - a) * k) - r; // <0 inside
                float inside = Mathf.Clamp01(0.5f - d);
                float rim = Mathf.Clamp01(d + borderWidth + 0.5f);
                var c = Lin(Color.Lerp(fill, border, rim));
                c.a *= inside;
                px[y * w + x] = c;
            }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    /// A ring whose outer edge touches the texture's edge, `thickness` pixels wide, filled, with a border
    /// on both edges; antialiased and mipmapped (it is drawn smaller than it is made). Stored linear like Round.
    public static Texture2D Ring(int size, float thickness, Color fill, Color border, float borderWidth)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var px = new Color[size * size];
        float half = size / 2f, mid = half - 1f - thickness / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
                float d = Mathf.Abs(r - mid) - thickness / 2f; // <0 inside the band
                float inside = Mathf.Clamp01(0.5f - d);
                float rim = Mathf.Clamp01(d + borderWidth + 0.5f);
                var c = Lin(Color.Lerp(fill, border, rim));
                c.a *= inside;
                px[y * size + x] = c;
            }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    /// A texture drawn tinted by `c` (as authored: converted for the linear project).
    public static void Tinted(Rect r, Texture t, Color c)
    {
        GUI.color = Lin(c);
        GUI.DrawTexture(r, t);
        GUI.color = Color.white;
    }

    // ---- painted art ----
    static readonly Dictionary<string, Texture2D> art = new Dictionary<string, Texture2D>();
    static Dictionary<string, Rect> layout;

    public static Texture2D Art(string name)
    {
        if (!art.TryGetValue(name, out var t)) art[name] = t = Resources.Load<Texture2D>("UI/" + name);
        return t;
    }

    /// A box measured on a painted card ("card_knight" = its picture window, "card_knight_text" = its
    /// parchment), as fractions of the card from the top-left.
    public static Rect Layout(string name, Rect fallback)
    {
        if (layout == null)
        {
            layout = new Dictionary<string, Rect>();
            var ta = Resources.Load<TextAsset>("UI/ui_layout");
            if (ta)
                foreach (var line in ta.text.Split('\n'))
                {
                    var p = line.Trim().Split(' ');
                    if (p.Length != 5) continue;
                    float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
                    layout[p[0]] = new Rect(F(1), F(2), F(3), F(4));
                }
        }
        // A failed measurement comes out tiny; use the typical spot instead.
        return layout.TryGetValue(name, out var r) && r.width > 0.3f && r.height > 0.15f ? r : fallback;
    }

    /// A part of r given as fractions of it.
    public static Rect Sub(Rect r, Rect f) => new Rect(r.x + f.x * r.width, r.y + f.y * r.height, f.width * r.width, f.height * r.height);

    /// Nine-slice: the corners (borders in texture pixels) keep their shape, scaled by `scale`;
    /// the edges and the middle stretch.
    public static void Nine(Rect r, Texture2D t, float bl, float bt, float br, float bb, float scale)
    {
        if (!t) return;
        scale = Mathf.Min(scale, r.width / (bl + br), r.height / (bt + bb));
        float tw = t.width, th = t.height;
        float[] xs = { r.x, r.x + bl * scale, r.xMax - br * scale, r.xMax };
        float[] ys = { r.y, r.y + bt * scale, r.yMax - bb * scale, r.yMax };
        // Seams on whole screen pixels: under the HUD's scale matrix a seam on a fraction of a pixel left a
        // one-pixel gap between two slices (a thin line of the scene across the panel).
        var m = GUI.matrix;
        for (int i = 0; i < 4; i++)
        {
            xs[i] = (Mathf.Round(xs[i] * m.m00 + m.m03) - m.m03) / m.m00;
            ys[i] = (Mathf.Round(ys[i] * m.m11 + m.m13) - m.m13) / m.m11;
        }
        float[] us = { 0f, bl / tw, 1f - br / tw, 1f };
        float[] vs = { 1f, 1f - bt / th, bb / th, 0f };
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var sr = UnityEngine.Rect.MinMaxRect(xs[i], ys[j], xs[i + 1], ys[j + 1]);
                if (sr.width < 0.5f || sr.height < 0.5f) continue;
                GUI.DrawTextureWithTexCoords(sr, t, UnityEngine.Rect.MinMaxRect(us[i], vs[j + 1], us[i + 1], vs[j]), true);
            }
    }

    /// The stone plate with gold corners behind every HUD block.
    public static void DrawPanel(Rect r)
    {
        var t = Art("panel");
        if (!t) { GUI.Box(r, GUIContent.none, Panel); return; }
        float bx = t.width * 0.18f, by = t.height * 0.34f;
        Nine(r, t, bx, by, bx, by, Mathf.Min(38f, r.height * 0.42f) / by);
    }

    /// A soft coloured halo just outside r (hovered cards and buttons).
    public static void Halo(Rect r, Color c, float spread = 16f)
    {
        var prev = GUI.color;
        GUI.color = Lin(c);
        var o = new Rect(r.x - spread, r.y - spread, r.width + spread * 2f, r.height + spread * 2f);
        Nine(o, glow, 22f, 22f, 22f, 22f, spread / 22f * 1.6f);
        GUI.color = prev;
    }

    /// The wooden menu button with gold end caps; lit up when hovered.
    public static void DrawButton(Rect r, bool over, bool down)
    {
        var t = Art("button");
        if (!t) { GUI.Box(r, GUIContent.none, Button); return; }
        if (over) Halo(r, new Color(1f, 0.8f, 0.35f, down ? 0.5f : 0.85f), 12f);
        var prev = GUI.color;
        if (down) GUI.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        float cap = t.width * 0.14f, half = t.height * 0.5f;
        Nine(r, t, cap, half, cap, half, r.height / t.height);
        GUI.color = prev;
    }

    /// Big gold lettering in the Hearthstone manner: a dark outline and drop shadow, the fill light
    /// gold at the top, deeper gold in the middle and amber at the bottom. Drawn unscaled at the real
    /// pixel size (see Label), the bands clipped in screen space.
    public static void Title(Rect r, string text, GUIStyle style, Color? tint = null)
    {
        var keep = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        var sr = new Rect(r.x * Scale, r.y * Scale, r.width * Scale, r.height * Scale);
        var s = new GUIStyle(style) { richText = false, fontSize = Mathf.RoundToInt((style.fontSize > 0 ? style.fontSize : 13) * Scale) };
        string plain = StripColor(text);
        var dark = new GUIStyle(s);
        dark.normal.textColor = new Color(0.16f, 0.07f, 0.02f, 0.95f);
        float o = Mathf.Max(1.5f, s.fontSize * 0.045f);
        GUI.Label(new Rect(sr.x + o * 0.6f, sr.y + o * 2.2f, sr.width, sr.height), plain, dark); // drop shadow
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            GUI.Label(new Rect(sr.x + Mathf.Cos(a) * o, sr.y + Mathf.Sin(a) * o, sr.width, sr.height), plain, dark);
        }
        Color t = tint ?? new Color(1f, 0.82f, 0.42f);
        var bands = new[]
        {
            (0f, 0.47f, Color.Lerp(t, Color.white, 0.55f)),
            (0.47f, 0.6f, t),
            (0.6f, 1f, new Color(t.r * 0.92f, t.g * 0.68f, t.b * 0.45f)),
        };
        // The glyphs sit around the middle of the rect; the bands split that middle stretch.
        float gy = sr.center.y - s.fontSize * 0.55f, gh = s.fontSize * 1.1f;
        foreach (var (from, to, c) in bands)
        {
            float y0 = from == 0f ? sr.y : gy + gh * from, y1 = to >= 1f ? sr.yMax : gy + gh * to;
            GUI.BeginGroup(new Rect(sr.x, y0, sr.width, y1 - y0));
            var f = new GUIStyle(s);
            f.normal.textColor = c;
            GUI.Label(new Rect(0f, sr.y - y0, sr.width, sr.height), plain, f);
            GUI.EndGroup();
        }
        GUI.matrix = keep;
    }

    /// A red ribbon banner with a title on it.
    public static void DrawBanner(Rect r, string text, GUIStyle style)
    {
        var t = Art("banner");
        if (t)
        {
            float cap = t.width * 0.2f, half = t.height * 0.5f;
            Nine(r, t, cap, half, cap, half, r.height / t.height);
            Title(new Rect(r.x, r.y + r.height * 0.06f, r.width, r.height * 0.62f), text, style);
        }
        else Text(r, text, style, Gold);
    }

    /// The gold medallion (skill, mech special, hero portrait): a picture in the round window, a
    /// darkening sweep while it recharges, the frame on top.
    public static void DrawMedal(Rect r, Texture picture, float ready, Color backdrop, Rect? uv = null)
    {
        var frame = Art("medal");
        var win = Sub(r, Layout("medal", new Rect(0.22f, 0.22f, 0.56f, 0.56f)));
        var inner = new Rect(win.x - 2, win.y - 2, win.width + 4, win.height + 4);
        GUI.DrawTexture(inner, white, ScaleMode.StretchToFill, true, 0f, Lin(backdrop), 0f, inner.width / 2f);
        Spot(inner, new Color(backdrop.r * 2.5f, backdrop.g * 2.5f, backdrop.b * 2.5f, 0.8f));
        if (picture && uv.HasValue) GUI.DrawTextureWithTexCoords(inner, picture, uv.Value, true);
        else if (picture) GUI.DrawTexture(inner, picture, ScaleMode.ScaleAndCrop, true);
        if (ready < 1f)
        {
            // Recharging: dark from the top down to how much is left.
            float k = 1f - Mathf.Clamp01(ready);
            GUI.DrawTexture(inner, white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(0f, 0f, 0f, 0.35f)), 0f, inner.width / 2f);
            var cover = new Rect(inner.x, inner.y, inner.width, inner.height * k);
            GUI.BeginGroup(cover);
            GUI.DrawTexture(new Rect(0, 0, inner.width, inner.height), white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(0f, 0f, 0f, 0.45f)), 0f, inner.width / 2f);
            GUI.EndGroup();
        }
        if (frame) GUI.DrawTexture(r, frame, ScaleMode.StretchToFill, true);
        else GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, Gold.linear, 3f, r.width / 2f);
    }

    /// A soft round light (behind portraits).
    public static void Spot(Rect r, Color c)
    {
        if (!spot)
        {
            const int n = 64;
            spot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    spot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            spot.Apply();
        }
        var prev = GUI.color;
        GUI.color = Lin(c);
        GUI.DrawTexture(r, spot, ScaleMode.StretchToFill, true);
        GUI.color = prev;
    }

    /// One of the small painted icons (glyph_heart, glyph_armor, glyph_energy, glyph_gold, glyph_skull).
    public static void Icon(Rect r, string name)
    {
        var t = Art(name);
        if (t) GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true);
    }

    // ---- settings controls and thumbnails (drawn here; Hud does the clicking) ----

    /// A dark inset with a thin coloured rim (stat tiles, thumbnails).
    public static void Inset(Rect r, Color rim, float rimWidth = 1.5f)
    {
        float rad = Mathf.Min(8f, r.height / 2f);
        GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(0.03f, 0.03f, 0.05f, 0.85f)), 0f, rad);
        GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, Lin(rim), rimWidth, rad);
    }

    /// A picture with rounded corners in a rimmed inset (weapon icons, blessing art).
    public static void Thumb(Rect r, Texture pic, Color rim, ScaleMode mode)
    {
        Inset(r, rim, 2f);
        if (!pic) return;
        var p = new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6);
        GUI.DrawTexture(p, pic, mode, true, 0f, Color.white, 0f, Mathf.Min(6f, p.height / 2f));
    }

    /// A gold-rimmed groove filled up to k, with a round gold knob.
    public static void DrawSlider(Rect r, float k, bool hot)
    {
        k = Mathf.Clamp01(k);
        float rad = r.height / 2f;
        GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(0.03f, 0.025f, 0.02f, 0.95f)), 0f, rad);
        var fill = new Rect(r.x + 2, r.y + 2, Mathf.Max(r.height - 4, (r.width - 4) * k), r.height - 4);
        GUI.DrawTexture(fill, white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(0.85f, 0.62f, 0.25f)), 0f, fill.height / 2f);
        GUI.DrawTexture(new Rect(fill.x + 2, fill.y + 1, fill.width - 4, fill.height * 0.4f), white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(1f, 1f, 1f, 0.3f)), 0f, fill.height * 0.2f);
        GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(0.75f, 0.58f, 0.3f, 0.9f)), 1.5f, rad);
        Knob(new Vector2(r.x + r.width * k, r.center.y), r.height + 10f, hot);
    }

    /// A pill switch: the knob sits right and the groove lights up when on.
    public static void DrawSwitch(Rect r, bool on, bool hot)
    {
        float rad = r.height / 2f;
        var groove = on ? new Color(0.22f, 0.55f, 0.42f, 0.95f) : new Color(0.05f, 0.045f, 0.04f, 0.95f);
        GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, Lin(groove), 0f, rad);
        if (on) GUI.DrawTexture(new Rect(r.x + 4, r.y + 2, r.width - 8, r.height * 0.38f), white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(1f, 1f, 1f, 0.22f)), 0f, r.height * 0.19f);
        GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(0.75f, 0.58f, 0.3f, 0.9f)), 1.5f, rad);
        Knob(new Vector2(on ? r.xMax - rad : r.x + rad, r.center.y), r.height - 2f, hot);
    }

    /// A round gold knob with a dark rim and a highlight; brighter when hovered or held.
    static void Knob(Vector2 c, float d, bool hot)
    {
        var o = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
        if (hot) Spot(new Rect(o.x - d * 0.5f, o.y - d * 0.5f, d * 2f, d * 2f), new Color(1f, 0.8f, 0.35f, 0.7f));
        GUI.DrawTexture(o, white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(0.18f, 0.11f, 0.05f)), 0f, d / 2f);
        var i = new Rect(o.x + 2.5f, o.y + 2.5f, d - 5f, d - 5f);
        GUI.DrawTexture(i, white, ScaleMode.StretchToFill, true, 0f, Lin(hot ? new Color(1f, 0.86f, 0.5f) : new Color(0.9f, 0.7f, 0.35f)), 0f, i.width / 2f);
        var hi = new Rect(i.x + i.width * 0.22f, i.y + i.height * 0.12f, i.width * 0.56f, i.height * 0.36f);
        GUI.DrawTexture(hi, white, ScaleMode.StretchToFill, true, 0f, Lin(new Color(1f, 1f, 1f, 0.45f)), 0f, hi.height / 2f);
    }

    static Texture2D arrow;

    /// A chunky gold arrow (left or right), for stepping through choices.
    public static void DrawArrow(Rect r, bool right, bool hot)
    {
        if (!arrow)
        {
            // A rounded triangle pointing right, antialiased by its distance to the edges.
            const int n = 48;
            arrow = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var a = new Vector2(10, 6); var b = new Vector2(42, 24); var c = new Vector2(10, 42);
            float Edge(Vector2 p, Vector2 u, Vector2 v) { var e = (v - u).normalized; return (p.x - u.x) * e.y - (p.y - u.y) * e.x; }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Mathf.Min(Edge(p, b, a), Mathf.Min(Edge(p, c, b), Edge(p, a, c))); // >0 inside
                    float body = Mathf.Clamp01(d + 0.5f), core = Mathf.Clamp01(d - 3f + 0.5f);
                    // Dark outline, gold inside, lighter towards the top.
                    var col = Color.Lerp(new Color(0.18f, 0.11f, 0.05f), Color.Lerp(new Color(0.85f, 0.6f, 0.25f), new Color(1f, 0.9f, 0.6f), y / (float)n), core);
                    col.a = body;
                    arrow.SetPixel(x, y, Lin(col));
                }
            arrow.Apply();
        }
        if (hot) Spot(new Rect(r.x - r.width * 0.4f, r.y - r.height * 0.4f, r.width * 1.8f, r.height * 1.8f), new Color(1f, 0.8f, 0.35f, 0.6f));
        GUI.DrawTextureWithTexCoords(r, arrow, right ? new Rect(0f, 0f, 1f, 1f) : new Rect(1f, 0f, -1f, 1f), true);
    }

    /// A soft rounded-rectangle halo, opaque inside and fading out over `fade` pixels.
    static Texture2D Glow(int size, int fade)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float half = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - fade), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - fade), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) / fade;
                float a = Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        t.Apply();
        return t;
    }

    static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

    /// A rounded bar; with segments > 0 it is split into that many cells (hearts / armour plates).
    public static void Bar(Rect r, float value, float max, Color color, int segments = 0)
    {
        GUI.Box(r, GUIContent.none, Track);
        float k = max <= 0f ? 0f : Mathf.Clamp01(value / max);
        var inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
        if (segments <= 0)
        {
            if (k > 0f) Gloss(new Rect(inner.x, inner.y, Mathf.Max(inner.height, inner.width * k), inner.height), color);
            return;
        }
        float gap = 3f, cw = (inner.width - gap * (segments - 1)) / segments;
        for (int i = 0; i < segments; i++)
        {
            float part = Mathf.Clamp01(value - i);
            var cell = new Rect(inner.x + i * (cw + gap), inner.y, cw, inner.height);
            Fill(cell, new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 0.6f));
            if (part > 0f) Gloss(new Rect(cell.x, cell.y, cell.width * part, cell.height), color);
        }
    }

    /// A filled bar with a light sheen along its top (reads as a glass tube rather than flat paint).
    static void Gloss(Rect r, Color c)
    {
        Fill(r, c);
        var hi = new Rect(r.x + 1, r.y + 1, r.width - 2, Mathf.Max(1f, r.height * 0.38f));
        Fill(hi, new Color(1f, 1f, 1f, 0.28f));
        Fill(new Rect(r.x, r.yMax - Mathf.Max(1f, r.height * 0.25f), r.width, Mathf.Max(1f, r.height * 0.25f)), new Color(0f, 0f, 0f, 0.18f));
    }

    public static void Fill(Rect r, Color c) =>
        GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, Lin(c), 0f, Mathf.Min(5f, r.height / 2f));

    public static void Rect(Rect r, Color c)
    {
        GUI.color = Lin(c);
        GUI.DrawTexture(r, white);
        GUI.color = Color.white;
    }

    /// Dark at the top and bottom of the screen so titles and footers read over the 3D scene.
    public static void Vignette(float w, float h)
    {
        if (!vignette)
        {
            vignette = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
            {
                float v = y / 63f;
                float a = Mathf.Max(Mathf.Pow(1f - v, 2.2f), Mathf.Pow(v, 2.6f)) * 0.85f;
                vignette.SetPixel(0, y, Lin(new Color(0.02f, 0.02f, 0.05f, a)));
            }
            vignette.Apply();
        }
        GUI.DrawTexture(new Rect(0, 0, w, h), vignette);
    }

    /// How much the UI is scaled up (Hud sets it with GUI.matrix each frame). Text is not stretched
    /// with the matrix (it would blur): Label draws it unscaled at the real pixel size instead.
    public static float Scale = 1f;

    public static void Label(Rect r, string text, GUIStyle style)
    {
        if (Mathf.Abs(Scale - 1f) < 0.01f) { GUI.Label(r, text, style); return; }
        var keep = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        var s = new GUIStyle(style) { fontSize = Mathf.RoundToInt((style.fontSize > 0 ? style.fontSize : 13) * Scale) };
        GUI.Label(new Rect(r.x * Scale, r.y * Scale, r.width * Scale, r.height * Scale), ScaleSizeTags(text), s);
        GUI.matrix = keep;
    }

    // <size=N> in rich text is in pixels: scale it with the rest (remembered, texts repeat every frame).
    static readonly Dictionary<string, string> sized = new Dictionary<string, string>();
    static float sizedFor;

    static string ScaleSizeTags(string t)
    {
        if (t == null || t.IndexOf("<size=", System.StringComparison.Ordinal) < 0) return t;
        if (sizedFor != Scale || sized.Count > 2000) { sized.Clear(); sizedFor = Scale; }
        if (sized.TryGetValue(t, out var s)) return s;
        return sized[t] = System.Text.RegularExpressions.Regex.Replace(t, @"<size=(\d+)>",
            m => $"<size={Mathf.RoundToInt(int.Parse(m.Groups[1].Value) * Scale)}>");
    }

    static Texture2D edge;

    /// A soft coloured glow around the screen's edges, clear in the middle (hurt and dodge cues).
    public static void EdgeGlow(float w, float h, Color c)
    {
        if (!edge)
        {
            const int n = 64;
            edge = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Distance from the centre of an ellipse filling the screen: 0 middle, 1 at the edges.
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx * 0.7f + dy * dy * 0.7f);
                    float a = Mathf.SmoothStep(0f, 1f, (d - 0.55f) / 0.45f);
                    edge.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            edge.Apply();
        }
        var prev = GUI.color;
        GUI.color = Lin(c);
        GUI.DrawTexture(new Rect(0, 0, w, h), edge, ScaleMode.StretchToFill, true);
        GUI.color = prev;
    }

    /// A label with a soft dark outline (reads on any background).
    public static void Text(Rect r, string text, GUIStyle style, Color? color = null)
    {
        var s = new GUIStyle(style) { richText = true };
        var shadow = new GUIStyle(s);
        shadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f * (color.HasValue ? color.Value.a : 1f)); // fades with the text
        string plain = StripColor(text);
        Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), plain, shadow);
        Label(new Rect(r.x - 1, r.y + 1, r.width, r.height), plain, shadow);
        if (color.HasValue) s.normal.textColor = color.Value;
        Label(r, text, s);
    }

    // The shadow copy has no colour tags; texts repeat every frame, so remember the stripped versions.
    static readonly System.Collections.Generic.Dictionary<string, string> stripped = new System.Collections.Generic.Dictionary<string, string>();

    static string StripColor(string t)
    {
        if (t.IndexOf('<') < 0) return t;
        if (stripped.TryGetValue(t, out var s)) return s;
        if (stripped.Count > 2000) stripped.Clear();
        return stripped[t] = System.Text.RegularExpressions.Regex.Replace(t, "</?color[^>]*>", "");
    }
}
