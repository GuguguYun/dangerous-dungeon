using System.Collections.Generic;
using UnityEngine;

/// Named colours shared by the Blender kit's material slots and the code-made placeholders.
/// BuildScript turns each entry into a material asset under Resources/Palette, so the
/// Standard shader's emission variant and the trail shader survive build stripping.
public static class Palette
{
    public struct Entry
    {
        public Color color;
        public float emission;
        public Entry(float r, float g, float b, float e = 0f) { color = new Color(r, g, b); emission = e; }
    }

    public static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>
    {
        // Blender kit material slots
        { "grout", new Entry(0.18f, 0.18f, 0.22f) },
        { "stone_a", new Entry(0.55f, 0.57f, 0.63f) },
        { "stone_b", new Entry(0.48f, 0.50f, 0.57f) },
        { "wall", new Entry(0.58f, 0.54f, 0.66f) },
        { "trim", new Entry(0.36f, 0.33f, 0.45f) },
        { "frame", new Entry(0.62f, 0.45f, 0.30f) },
        { "gate", new Entry(1.00f, 0.35f, 0.20f, 1.5f) },
        { "wood", new Entry(0.72f, 0.52f, 0.33f) },
        { "wood_dark", new Entry(0.48f, 0.33f, 0.20f) },
        { "portal_ring", new Entry(0.70f, 0.50f, 1.00f, 2.5f) },
        { "portal_core", new Entry(0.55f, 0.35f, 1.00f, 1.5f) },
        // placeholders made in code
        { "player_body", new Entry(0.30f, 0.55f, 1.00f) },
        { "player_visor", new Entry(0.40f, 1.00f, 1.00f, 2f) },
        { "gun", new Entry(0.20f, 0.20f, 0.24f) },
        { "gun_green", new Entry(0.25f, 0.55f, 0.35f, 0.6f) },
        { "gun_red", new Entry(0.60f, 0.20f, 0.18f, 0.4f) },
        { "blade", new Entry(0.80f, 0.90f, 1.00f, 1.2f) },
        { "enemy_body", new Entry(0.85f, 0.30f, 0.25f) },
        { "enemy_charger", new Entry(0.60f, 0.30f, 0.80f) },
        { "enemy_spreader", new Entry(0.35f, 0.70f, 0.35f) },
        { "enemy_boss", new Entry(0.45f, 0.12f, 0.15f) },
        { "enemy_eye", new Entry(1.00f, 0.90f, 0.35f, 2f) },
        { "bullet_player", new Entry(0.40f, 0.95f, 1.00f, 3f) },
        { "bullet_heavy", new Entry(1.00f, 0.85f, 0.40f, 3f) },
        { "bullet_laser", new Entry(0.45f, 1.00f, 0.50f, 3f) },
        { "bullet_rocket", new Entry(1.00f, 0.40f, 0.20f, 3f) },
        { "bullet_enemy", new Entry(1.00f, 0.55f, 0.15f, 3f) },
        { "bullet_boss", new Entry(1.00f, 0.25f, 0.55f, 3f) },
        { "bullet_low", new Entry(0.45f, 1.00f, 0.35f, 3f) },
        { "bullet_gold", new Entry(1.00f, 0.78f, 0.25f, 2.5f) },
        { "bullet_rock", new Entry(0.50f, 0.38f, 0.28f, 0.3f) },
        { "bullet_axe", new Entry(1.00f, 0.22f, 0.15f, 2.5f) },
        { "bullet_arrow", new Entry(1.00f, 0.95f, 0.70f, 3f) },
        { "bullet_frost", new Entry(0.60f, 0.90f, 1.00f, 3f) },
        { "bullet_fire", new Entry(1.00f, 0.50f, 0.10f, 4f) },
        { "bullet_missile", new Entry(1.00f, 0.30f, 0.20f, 3f) },
        { "bullet_grenade", new Entry(0.50f, 0.60f, 0.25f, 1f) },
        { "beam", new Entry(1.00f, 0.25f, 0.25f, 4f) },
        { "lightning", new Entry(0.60f, 0.85f, 1.00f, 4f) },
        { "shockwave", new Entry(1.00f, 0.55f, 0.20f) },
        { "iron", new Entry(0.20f, 0.20f, 0.24f) },
        { "embers", new Entry(1.00f, 0.40f, 0.10f, 2.5f) },
        { "steel", new Entry(0.48f, 0.50f, 0.56f) },
        { "ice", new Entry(0.72f, 0.88f, 1.00f, 0.15f) },
        { "pickup_energy", new Entry(0.30f, 0.60f, 1.00f, 2.5f) },
        { "pickup_health", new Entry(1.00f, 0.30f, 0.35f, 1.5f) },
        { "pickup_gold", new Entry(1.00f, 0.80f, 0.25f, 1.6f) },
        { "slash", new Entry(0.70f, 0.95f, 1.00f) },
    };

    public static readonly string[] TrailNames =
        { "bullet_player", "bullet_heavy", "bullet_laser", "bullet_rocket", "bullet_enemy", "bullet_boss", "bullet_low", "bullet_arrow", "bullet_frost", "bullet_fire",
          "bullet_missile", "bullet_grenade", "beam", "lightning", "slash", "shockwave", "bullet_gold", "bullet_rock", "bullet_axe" };

    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    public static Color ColorOf(string name) =>
        Entries.TryGetValue(name, out var e) ? e.color : Color.magenta;

    public static Material Get(string name)
    {
        if (cache.TryGetValue(name, out var m)) return m;
        m = Resources.Load<Material>("Palette/" + name);
        if (m == null) m = Create(name);
        cache[name] = m;
        return m;
    }

    public static Material Trail(string name)
    {
        string key = "trail_" + name;
        if (cache.TryGetValue(key, out var m)) return m;
        m = Resources.Load<Material>("Palette/" + key);
        if (m == null) m = CreateTrail(name);
        cache[key] = m;
        return m;
    }

    public static Material Create(string name)
    {
        var e = Entries.TryGetValue(name, out var found) ? found : new Entry(1f, 0f, 1f);
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { name = name };
        m.color = e.color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", e.color);
        m.SetFloat("_Glossiness", 0.15f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        if (e.emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", e.color * e.emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return m;
    }

    public static Material CreateTrail(string name)
    {
        var m = new Material(Shader.Find("Sprites/Default")) { name = "trail_" + name };
        m.color = ColorOf(name);
        return m;
    }
}
