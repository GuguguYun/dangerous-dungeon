using UnityEngine;

/// The game's UI look ("dark dungeon, gold rim"): rounded translucent panels with a thin gold border,
/// buttons that light up on hover, rounded bars. Textures are made at runtime as 9-slice images.
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
    static Texture2D white, vignette;
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
        var trackTex = Round(24, 6, new Color(0f, 0f, 0f, 0.8f), new Color(1f, 1f, 1f, 0.12f), 1f);
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

    /// A rounded rectangle (signed distance, antialiased), filled, with an inner border.
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
                var c = Color.Lerp(fill, border, rim);
                c.a *= inside;
                px[y * size + x] = c;
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    public static void DrawPanel(Rect r) => GUI.Box(r, GUIContent.none, Panel);

    static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

    /// A rounded bar; with segments > 0 it is split into that many cells (hearts / armour plates).
    public static void Bar(Rect r, float value, float max, Color color, int segments = 0)
    {
        GUI.Box(r, GUIContent.none, Track);
        float k = max <= 0f ? 0f : Mathf.Clamp01(value / max);
        var inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
        if (segments <= 0)
        {
            if (k > 0f) Fill(new Rect(inner.x, inner.y, Mathf.Max(inner.height, inner.width * k), inner.height), color);
            return;
        }
        float gap = 3f, cw = (inner.width - gap * (segments - 1)) / segments;
        for (int i = 0; i < segments; i++)
        {
            float part = Mathf.Clamp01(value - i);
            var cell = new Rect(inner.x + i * (cw + gap), inner.y, cw, inner.height);
            Fill(cell, new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 0.6f));
            if (part > 0f) Fill(new Rect(cell.x, cell.y, cell.width * part, cell.height), color);
        }
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
                vignette.SetPixel(0, y, new Color(0.02f, 0.02f, 0.05f, a));
            }
            vignette.Apply();
        }
        GUI.DrawTexture(new Rect(0, 0, w, h), vignette);
    }

    /// A label with a soft dark outline (reads on any background).
    public static void Text(Rect r, string text, GUIStyle style, Color? color = null)
    {
        var s = new GUIStyle(style) { richText = true };
        var shadow = new GUIStyle(s);
        shadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
        foreach (var o in new[] { new Vector2(2, 2), new Vector2(-1, 1), new Vector2(1, -1) })
            GUI.Label(new Rect(r.x + o.x, r.y + o.y, r.width, r.height), StripColor(text), shadow);
        if (color.HasValue) s.normal.textColor = color.Value;
        GUI.Label(r, text, s);
    }

    static string StripColor(string t) => System.Text.RegularExpressions.Regex.Replace(t, "</?color[^>]*>", "");
}
