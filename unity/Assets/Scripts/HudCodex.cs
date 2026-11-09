using System.Collections.Generic;
using UnityEngine;

/// The weapon handbook (武器图鉴), after Soul Knight's: every weapon as a card, the ones you have held in
/// any run in colour and the rest as dark silhouettes; picking one shows what it does and its numbers in
/// the terms of the combat power system (Balance): damage per second against one enemy, energy a second,
/// and how that number comes from its rarity band and its strengths.
public partial class Hud
{
    Screen codexReturn;
    static readonly Dictionary<string, Texture2D> silhouettes = new Dictionary<string, Texture2D>();

    /// A weapon not yet found: its shape in flat grey-blue. (Tinting the icon dark doesn't work here: in
    /// this linear-space project IMGUI shows a 0.2 tint at about half brightness, colours and all.)
    static Texture2D Silhouette(string id)
    {
        if (silhouettes.TryGetValue(id, out var s)) return s;
        var icon = IconStudio.I ? IconStudio.I.Weapon(id) : null;
        if (!icon || !icon.isReadable) return null;
        var px = icon.GetPixels();
        // Stored the way IMGUI wants runtime pictures (see IconStudio.ForMedal): decoded once.
        var c = new Color(Mathf.GammaToLinearSpace(0.27f), Mathf.GammaToLinearSpace(0.3f), Mathf.GammaToLinearSpace(0.4f));
        for (int i = 0; i < px.Length; i++) px[i] = new Color(c.r, c.g, c.b, px[i].a);
        s = new Texture2D(icon.width, icon.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        s.SetPixels(px);
        s.Apply();
        silhouettes[id] = s;
        return s;
    }
    string codexPick;

    public void OpenCodex()
    {
        codexReturn = screen;
        screen = Screen.Codex;
        codexPick = null;
    }

    static List<WeaponDef> CodexOrder()
    {
        var list = new List<WeaponDef>(Weapons.All);
        // Stable by rarity, keeping the table's order within one rarity.
        var order = new Dictionary<WeaponDef, int>();
        for (int i = 0; i < list.Count; i++) order[list[i]] = i;
        list.Sort((a, b) => a.rarity != b.rarity ? a.rarity.CompareTo(b.rarity) : order[a].CompareTo(order[b]));
        return list;
    }

    void DrawCodex(float w, float h)
    {
        UiKit.Vignette(w, h);
        UiKit.Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.02f, 0.05f, 0.86f));
        UiKit.DrawBanner(new Rect(w / 2 - 300, 8, 600, 96), "武器图鉴", new GUIStyle(title) { fontSize = 36 });
        var weapons = CodexOrder();
        int known = weapons.FindAll(x => RunState.Known(x.id)).Count;
        UiKit.Text(new Rect(0, 100, w, 26), $"已收集 {known}/{weapons.Count} · 秒伤：持续打一个敌人每秒的伤害（不含祝福、暴击、连击）",
            new GUIStyle(small) { fontSize = 16 }, UiKit.Dim);

        float gridX = 40f, gridY = 138f, gridW = Mathf.Max(520f, w * 0.6f - 40f);
        const int cols = 6;
        const float gap = 12f;
        float cw = (gridW - gap * (cols - 1)) / cols, iconH = cw * 0.62f, ch = iconH + 26f;
        var mouse = Event.current.mousePosition;
        string hover = null;

        void Cell(WeaponDef wd, float x, float y, bool isKnown)
        {
            var r = new Rect(x, y, cw, iconH);
            bool over = new Rect(x, y, cw, ch).Contains(mouse);
            if (over) hover = wd.id;
            bool picked = codexPick == wd.id;
            var rim = isKnown ? Weapons.RarityColors[wd.rarity] : new Color(0.35f, 0.36f, 0.42f);
            if (over || picked) rim = Color.Lerp(rim, Color.white, 0.5f);
            var icon = IconStudio.I ? IconStudio.I.Weapon(wd.id) : null;
            UiKit.Thumb(r, isKnown ? icon : Silhouette(wd.id), rim, ScaleMode.ScaleToFit); // unknown: you can tell its shape, not what it is
            UiKit.Text(new Rect(x, y + iconH + 2, cw, 22), isKnown ? wd.name : "？？？", new GUIStyle(center) { fontSize = 15 },
                isKnown ? Weapons.RarityColors[wd.rarity] : UiKit.Dim);
            if (over && Event.current.type == EventType.MouseDown) codexPick = wd.id;
        }

        for (int i = 0; i < weapons.Count; i++)
            Cell(weapons[i], gridX + (i % cols) * (cw + gap), gridY + (i / cols) * (ch + gap), RunState.Known(weapons[i].id));
        float skillY = gridY + ((weapons.Count + cols - 1) / cols) * (ch + gap) + 4f;
        UiKit.Text(new Rect(gridX, skillY, gridW, 24), "职业专属武器", new GUIStyle(left) { fontSize = 15 }, UiKit.Gold);
        for (int i = 0; i < Weapons.SkillWeapons.Count; i++)
            Cell(Weapons.SkillWeapons[i], gridX + i * (cw + gap), skillY + 28f, true);

        // The card on the right: the weapon under the mouse, else the one clicked, else the first known.
        string id = hover ?? codexPick ?? weapons.Find(x => RunState.Known(x.id))?.id ?? weapons[0].id;
        var wsel = Weapons.Get(id);
        bool selKnown = wsel.IsSkillWeapon || RunState.Known(id);
        var panel = new Rect(gridX + gridW + 30f, gridY, w - (gridX + gridW + 30f) - 40f, skillY + 28f + ch - gridY);
        UiKit.DrawPanel(panel);
        float px = panel.x + 24, pw = panel.width - 48, py = panel.y + 18;
        var pic = IconStudio.I ? IconStudio.I.Weapon(wsel.id) : null;
        var big = new Rect(px, py, pw, Mathf.Min(150f, pw * 0.55f));
        UiKit.Thumb(big, selKnown ? pic : Silhouette(wsel.id), selKnown ? Weapons.RarityColors[wsel.rarity] : UiKit.Dim, ScaleMode.ScaleToFit);
        py = big.yMax + 12;
        if (!selKnown)
        {
            UiKit.Text(new Rect(px, py, pw, 34), "？？？", new GUIStyle(left) { fontSize = 26 }, UiKit.Dim);
            UiKit.Text(new Rect(px, py + 40, pw, 60), $"{Weapons.RarityNames[wsel.rarity]}品质 · 还没拿到过。宝箱和商店里会出现，越深的层越容易出好武器。",
                new GUIStyle(left) { fontSize = 15, wordWrap = true }, UiKit.Dim);
            CodexBack(w, h);
            return;
        }
        var rc = Weapons.RarityColors[wsel.rarity];
        UiKit.Text(new Rect(px, py, pw, 34), wsel.name, new GUIStyle(left) { fontSize = 26 }, rc);
        UiKit.Text(new Rect(px, py + 34, pw, 22), wsel.IsSkillWeapon ? $"{wsel.type} · 技能期间" : $"{wsel.type} · {Weapons.RarityNames[wsel.rarity]}",
            new GUIStyle(left) { fontSize = 15 }, UiKit.Dim);
        UiKit.Text(new Rect(px, py + 58, pw, 44), wsel.desc, new GUIStyle(left) { fontSize = 15, wordWrap = true });
        py += 106;

        // Numbers as bars against the strongest weapon in the game, so one glance compares two weapons.
        float topDps = 0f;
        foreach (var x in Weapons.All) topDps = Mathf.Max(topDps, x.Dps);
        foreach (var x in Weapons.SkillWeapons) topDps = Mathf.Max(topDps, x.Dps);
        var rowStyle = new GUIStyle(left) { fontSize = 16 };
        UiKit.Text(new Rect(px, py, 90, 24), "秒伤", rowStyle, UiKit.Gold);
        UiKit.Bar(new Rect(px + 90, py + 6, pw - 150, 12), wsel.Dps, topDps, rc);
        UiKit.Text(new Rect(px + pw - 52, py, 52, 24), $"{wsel.Dps:0}", rowStyle);
        py += 32;
        UiKit.Text(new Rect(px, py, 90, 24), "每秒耗能", rowStyle, UiKit.Gold);
        if (wsel.EnergyRate > 0f)
        {
            UiKit.Bar(new Rect(px + 90, py + 6, pw - 150, 12), wsel.EnergyRate, 5f, new Color(0.35f, 0.7f, 1f));
            UiKit.Text(new Rect(px + pw - 52, py, 52, 24), EnergyText(wsel), rowStyle);
        }
        else UiKit.Text(new Rect(px + 90, py, pw - 90, 24), "不耗能量", rowStyle, UiKit.Dim);
        py += 36;
        // Where the number comes from (Balance): the rarity's band times what the weapon's strengths cost.
        string why = wsel.IsSkillWeapon
            ? "职业技能的爆发：约为传说近战武器的 1.4 倍，持续几秒"
            : $"{Weapons.RarityNames[wsel.rarity]}档 {Balance.RarityDps[wsel.rarity]:0.#} × {Balance.RoleName(wsel)} {Balance.RoleMul(wsel):0.##} ≈ {Balance.TargetDps(wsel):0}";
        UiKit.Text(new Rect(px, py, pw, 44), $"<color=#F7CC75>怎么算的</color>  {why}", new GUIStyle(left) { fontSize = 14, wordWrap = true }, UiKit.Dim);
        CodexBack(w, h);
    }

    void CodexBack(float w, float h)
    {
        if (Btn(new Rect(w / 2 - 110, h - 64, 220, 50), "返回", button) || Input.GetKeyDown(KeyCode.Escape)) screen = codexReturn;
    }
}
