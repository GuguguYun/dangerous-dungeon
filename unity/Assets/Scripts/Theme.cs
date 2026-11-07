using UnityEngine;

/// Per-floor look: tints on the Meshy stone/wood textures, fog, ambient and sun colours, brazier
/// fire colour, sky tint and the ambient particles drifting around the player.
public class ThemeDef
{
    public string name;
    public Color stone = Color.white, wall = Color.white, trim = Color.white, wood = Color.white;
    public Color fog, ambient, sun, fire, sky;
    public float fogDensity = 0.012f;
    public string particles; // dust, embers, snow

    public static ThemeDef ForFloor(int floor) => floor switch
    {
        2 => new ThemeDef
        {
            name = "熔火深渊",
            stone = new Color(1f, 0.82f, 0.72f), wall = new Color(1f, 0.8f, 0.7f), trim = new Color(0.88f, 0.6f, 0.55f), wood = new Color(0.92f, 0.76f, 0.66f),
            fog = new Color(0.24f, 0.1f, 0.06f), fogDensity = 0.016f, ambient = new Color(0.7f, 0.55f, 0.5f),
            sun = new Color(1f, 0.7f, 0.5f), fire = new Color(1f, 0.45f, 0.15f), sky = new Color(0.6f, 0.2f, 0.12f), particles = "embers",
        },
        3 => new ThemeDef
        {
            name = "霜骨墓城",
            stone = new Color(0.86f, 0.94f, 1.05f), wall = new Color(0.88f, 0.95f, 1.06f), trim = new Color(0.78f, 0.87f, 1f), wood = new Color(0.85f, 0.88f, 0.95f),
            fog = new Color(0.12f, 0.17f, 0.24f), fogDensity = 0.015f, ambient = new Color(0.58f, 0.65f, 0.78f),
            sun = new Color(0.8f, 0.9f, 1.1f), fire = new Color(0.45f, 0.8f, 1f), sky = new Color(0.25f, 0.4f, 0.6f), particles = "snow",
        },
        4 => new ThemeDef
        {
            // The final floor: the warlord's blood-moon throne room.
            name = "血月王座",
            stone = new Color(0.95f, 0.78f, 0.78f), wall = new Color(0.92f, 0.72f, 0.74f), trim = new Color(0.75f, 0.45f, 0.5f), wood = new Color(0.8f, 0.6f, 0.6f),
            fog = new Color(0.22f, 0.04f, 0.06f), fogDensity = 0.018f, ambient = new Color(0.62f, 0.45f, 0.5f),
            sun = new Color(1f, 0.55f, 0.5f), fire = new Color(1f, 0.25f, 0.2f), sky = new Color(0.45f, 0.06f, 0.1f), particles = "embers",
        },
        _ => new ThemeDef
        {
            name = "青苔地宫",
            fog = new Color(0.10f, 0.08f, 0.15f), ambient = new Color(0.58f, 0.6f, 0.7f),
            sun = new Color(1f, 0.96f, 0.9f), fire = new Color(1f, 0.6f, 0.3f), sky = new Color(0.35f, 0.25f, 0.55f), particles = "dust",
        },
    };

    public Color TintFor(string material) => material switch
    {
        "stone_a" or "stone_b" or "grout" => stone,
        "wall" => wall,
        "trim" => trim,
        "frame" or "wood" or "wood_dark" => wood,
        _ => Color.white,
    };
}
