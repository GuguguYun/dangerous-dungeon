using System.Collections.Generic;
using UnityEngine;

/// Everything on screen, drawn with IMGUI: bars, weapon panel, crosshair and hit marker,
/// damage numbers, minimap, boss bar, messages, and the modal screens (buff pick, pause, death, win).
public class Hud : MonoBehaviour
{
    public static Hud I;

    public enum Screen { None, Title, ClassSelect, Buff, Pause, Dead, Won, Settings }

    public PlayerController player;
    public Dungeon dungeon;
    public int enemiesLeft = -1;
    public string themeName = "";
    public Screen screen = Screen.None;
    public System.Action Resume, Restart, Quit, Begin, SettingsChanged;
    public System.Action<string> ChooseClass;
    Screen settingsReturn;
    readonly float[] frameTimes = new float[240];
    int frameIndex;

    struct Number
    {
        public Vector3 pos;
        public string text;
        public bool crit;
        public float t;
    }

    readonly List<Number> numbers = new List<Number>();
    List<BuffDef> offer;
    System.Action<BuffDef> onPick;
    string message;
    float messageUntil;
    string tip;
    float tipUntil;

    /// A beginner hint on its own line (lower middle), so it doesn't fight with room messages.
    public void Tip(string text)
    {
        tip = text;
        tipUntil = Time.unscaledTime + 5f;
        Debug.Log("TIP " + text);
    }
    float hitMarker, hurt, perfect, bannerT;
    string bannerName, bannerSub;
    Font font;
    GUIStyle left, center, small, title, button, number;

    void Awake() => I = this;

    /// seconds <= 0 keeps the message until replaced.
    public void Show(string text, float seconds)
    {
        message = text;
        messageUntil = seconds > 0f ? Time.unscaledTime + seconds : float.MaxValue;
        Debug.Log("HUD " + text);
    }

    public void AddDamage(Vector3 pos, int amount, bool crit) =>
        numbers.Add(new Number { pos = pos + Random.insideUnitSphere * 0.3f, text = crit ? amount + "!" : amount.ToString(), crit = crit });

    public void HitMarker() => hitMarker = 0.12f;
    public void Hurt() => hurt = 0.35f;

    public void BossBanner(string name, string subtitle)
    {
        bannerName = name;
        bannerSub = subtitle;
        bannerT = 2.8f;
        Debug.Log("HUD boss " + name);
    }

    public void PerfectDodge()
    {
        perfect = 0.6f;
        Show("完美闪避！", 0.8f);
    }

    public void OfferBuffs(List<BuffDef> options, System.Action<BuffDef> pick)
    {
        offer = options;
        onPick = pick;
        screen = Screen.Buff;
    }

    public void PickBuff(int i)
    {
        if (screen != Screen.Buff || offer == null || i >= offer.Count) return;
        screen = Screen.None;
        var b = offer[i];
        offer = null;
        onPick?.Invoke(b);
    }

    void Init()
    {
        // Bundled Noto Sans SC (SIL OFL); Unity cannot resolve macOS system CJK fonts by name.
        font = Resources.Load<Font>("Fonts/NotoSansSC-Regular");
        left = new GUIStyle { font = font, fontSize = 22, alignment = TextAnchor.MiddleLeft };
        left.normal.textColor = Color.white;
        center = new GUIStyle(left) { fontSize = 40, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        small = new GUIStyle(left) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
        small.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
        title = new GUIStyle(center) { fontSize = 56 };
        number = new GUIStyle(center) { fontSize = 26 };
        button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 22, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        button.normal.textColor = Color.white;
        button.hover.textColor = new Color(1f, 0.9f, 0.5f);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        frameTimes[frameIndex] = dt;
        frameIndex = (frameIndex + 1) % frameTimes.Length;
        hitMarker -= dt;
        hurt -= dt;
        perfect -= dt;
        bannerT -= dt;
        for (int i = numbers.Count - 1; i >= 0; i--)
        {
            var n = numbers[i];
            n.t += dt;
            n.pos += Vector3.up * dt * 1.2f;
            numbers[i] = n;
            if (n.t > 0.8f) numbers.RemoveAt(i);
        }
        if (screen == Screen.ClassSelect)
        {
            for (int i = 0; i < Classes.All.Count; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) ChooseClass?.Invoke(Classes.All[i].id);
        }
        if (screen == Screen.Buff)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) PickBuff(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) PickBuff(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) PickBuff(2);
        }
    }

    static void Box(Rect r, Color c)
    {
        // The project renders in linear space, which brightens IMGUI colours; convert so they read as authored.
        GUI.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void Label(Rect r, string text, GUIStyle style)
    {
        var s = new GUIStyle(style);
        s.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, s);
        GUI.Label(r, text, style);
    }

    void Bar(float y, string name, int v, int max, Color c)
    {
        Box(new Rect(20, y, 260, 26), new Color(0f, 0f, 0f, 0.5f));
        Box(new Rect(22, y + 2, 256f * Mathf.Clamp01(max == 0 ? 0 : v / (float)max), 22), c);
        Label(new Rect(30, y, 250, 26), $"{name}  {v} / {max}", new GUIStyle(left) { fontSize = 17 });
    }

    void OnGUI()
    {
        if (font == null) Init();
        float w = UnityEngine.Screen.width, h = UnityEngine.Screen.height;
        if (screen == Screen.Title)
        {
            DrawTitle(w, h);
            return;
        }
        if (screen == Screen.Settings)
        {
            DrawSettings(w, h);
            return;
        }
        if (screen == Screen.ClassSelect)
        {
            DrawClassSelect(w, h);
            return;
        }

        if (GameSettings.flashes && hurt > 0f) Box(new Rect(0, 0, w, h), new Color(0.8f, 0f, 0f, hurt * 0.5f));
        if (player && player.poisoned > 0f)
        {
            Box(new Rect(0, 0, w, 10), new Color(0.3f, 0.9f, 0.2f, 0.5f));
            Box(new Rect(0, h - 10, w, 10), new Color(0.3f, 0.9f, 0.2f, 0.5f));
            Label(new Rect(22, 172 + Off, 300, 30), $"中毒 {player.poisoned:0.0}s", new GUIStyle(left) { fontSize = 20, normal = { textColor = new Color(0.5f, 1f, 0.4f) } });
        }
        if (GameSettings.flashes && perfect > 0f) Box(new Rect(0, 0, w, h), new Color(0.3f, 0.9f, 1f, perfect * 0.35f));
        if (GameSettings.showFps) DrawFps(w);

        if (player)
        {
            // In a mech its durability bar sits on top: it is spent before your own.
            if (player.InMech) Bar(20, player.mechDef.name, player.mechHp, player.mechDef.maxHp, player.mechDef.color * 0.8f);
            Bar(20 + Off, "生命", player.hp, player.maxHp, new Color(0.78f, 0.16f, 0.22f));
            Bar(50 + Off, "护甲", player.armor, player.maxArmor, new Color(0.40f, 0.45f, 0.56f));
            Bar(80 + Off, "能量", player.energy, player.maxEnergy, new Color(0.18f, 0.38f, 0.88f));
            if (player.InMech)
            {
                DrawMech(w, h);
            }
            else
            {
                DrawWeapons(w, h);
                DrawSkill(w, h);
            }
        }
        Label(new Rect(22, 112 + Off, 560, 30), $"第 {RunState.floor} / {RunState.Floors} 层 · {themeName}    击杀 {RunState.kills}    <color=#FFD45A>金币 {RunState.gold}</color>", new GUIStyle(left) { richText = true });
        if (enemiesLeft >= 0) Label(new Rect(22, 142 + Off, 400, 30), $"剩余敌人 {enemiesLeft}", left);

        DrawCrosshair(w, h);
        DrawEliteTags();
        DrawNumbers();
        DrawBoss(w);
        DrawMinimap(w);

        if (player && player.nearbyWeapon && screen == Screen.None)
        {
            var pw = player.nearbyWeapon.weapon;
            var ps = new GUIStyle(center) { fontSize = 26, richText = true };
            Label(new Rect(0, h * 0.62f, w, 40), $"按 E 拾取 <color=#{ColorUtility.ToHtmlStringRGB(Weapons.RarityColors[pw.rarity])}>{pw.name}</color>（{Weapons.RarityNames[pw.rarity]}）", ps);
            Label(new Rect(0, h * 0.62f + 36, w, 30), pw.desc, new GUIStyle(small) { fontSize = 18 });
        }
        else if (player && player.nearbyWare && screen == Screen.None)
        {
            var it = player.nearbyWare;
            bool afford = RunState.gold >= it.price;
            var ps = new GUIStyle(center) { fontSize = 26, richText = true };
            Label(new Rect(0, h * 0.62f, w, 40), $"按 E 购买 <color=#{ColorUtility.ToHtmlStringRGB(it.Tint)}>{it.Title}</color>  <color={(afford ? "#FFD45A" : "#FF6A5A")}>{it.price} 金币</color>", ps);
            Label(new Rect(0, h * 0.62f + 36, w, 30), it.Desc, new GUIStyle(small) { fontSize = 18 });
        }
        else if (player && player.nearbyMech && screen == Screen.None)
        {
            var m = player.nearbyMech;
            var ps = new GUIStyle(center) { fontSize = 26, richText = true };
            Label(new Rect(0, h * 0.62f, w, 40), $"按 E 搭乘 <color=#{ColorUtility.ToHtmlStringRGB(m.def.color)}>{m.def.name}</color>（{(m.def.heavy ? "重装" : "轻装")} · 耐久 {m.hp}/{m.def.maxHp}）", ps);
            Label(new Rect(0, h * 0.62f + 36, w, 30), $"{m.def.ranged} + {m.def.melee}：{m.def.desc}", new GUIStyle(small) { fontSize = 18 });
        }
        if (bannerT > 0f && screen == Screen.None)
        {
            float a = Mathf.Clamp01(bannerT * 2f) * Mathf.Clamp01((2.8f - bannerT) * 4f);
            Box(new Rect(0, h * 0.3f, w, 130), new Color(0f, 0f, 0f, 0.55f * a));
            var bs = new GUIStyle(title) { fontSize = 64 };
            bs.normal.textColor = new Color(1f, 0.35f, 0.3f, a);
            Label(new Rect(0, h * 0.3f + 8, w, 80), bannerName, bs);
            var ss = new GUIStyle(small) { fontSize = 22 };
            ss.normal.textColor = new Color(1f, 1f, 1f, 0.85f * a);
            Label(new Rect(0, h * 0.3f + 88, w, 32), bannerSub, ss);
        }
        if (message != null && Time.unscaledTime < messageUntil && screen == Screen.None)
            Label(new Rect(0, h * 0.2f, w, 60), message, center);
        if (tip != null && Time.unscaledTime < tipUntil && screen == Screen.None)
        {
            float a = Mathf.Clamp01((tipUntil - Time.unscaledTime) * 2f);
            var ts = new GUIStyle(center) { fontSize = 21 };
            ts.normal.textColor = new Color(1f, 0.92f, 0.6f, a);
            Box(new Rect(w / 2 - 380, h * 0.74f, 760, 40), new Color(0f, 0f, 0f, 0.5f * a));
            Label(new Rect(w / 2 - 380, h * 0.74f + 4, 760, 32), "提示：" + tip, ts);
        }
        if (screen == Screen.None && player && player.InMech)
        {
            var d = player.mechDef;
            Label(new Rect(0, h - 36, w, 28), $"WASD 移动 · 左键 {d.ranged} · 右键 {d.melee} · F {d.special} · Shift {d.dashName} · 空格 {d.jumpName} · E 离开机甲 · Esc 暂停", small);
        }
        else if (screen == Screen.None)
            Label(new Rect(0, h - 36, w, 28), "WASD 移动 · 鼠标 瞄准 · 左键 攻击 · 右键/F 技能 · Q 换武器 · E 拾取 · Shift 冲刺 · 空格 跳 · Esc 暂停", small);

        switch (screen)
        {
            case Screen.Buff: DrawBuffs(w, h); break;
            case Screen.Pause: DrawMenu(w, h, "已暂停", "继续", Resume); break;
            case Screen.Dead: DrawMenu(w, h, "你倒下了", "再来一局", Restart); break;
            case Screen.Won: DrawMenu(w, h, "通关！你打穿了地牢", "再来一局", Restart); break;
        }
        GUI.color = Color.white;
    }

    void DrawTitle(float w, float h)
    {
        Box(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.45f));
        Label(new Rect(0, h * 0.16f, w, 90), "Meshy 地牢", new GUIStyle(title) { fontSize = 84 });
        Label(new Rect(0, h * 0.16f + 96, w, 36), "第三人称 · 随机地牢 · 射击与近战 · 三层通关", new GUIStyle(small) { fontSize = 22 });
        if (GUI.Button(new Rect(w / 2 - 150, h * 0.45f, 300, 64), "开始游戏", button)) Begin?.Invoke();
        if (GUI.Button(new Rect(w / 2 - 150, h * 0.45f + 76, 300, 52), "设置 · 防晕 3D", button)) OpenSettings();
        if (GUI.Button(new Rect(w / 2 - 150, h * 0.45f + 140, 300, 52), "退出游戏", button)) Quit?.Invoke();
        Label(new Rect(0, h * 0.72f, w, 120),
            "WASD 移动 · 鼠标 瞄准 · 左键 攻击 · Q 换武器 · E 拾取 · Esc 暂停\n空格 跳：起跳时机对了，子弹会从脚下穿过；地面冲击波只能跳过\nShift 冲刺：短暂无敌躲子弹 · 跳跃和冲刺都消耗体力（准星下方）\n贴身躲开攻击触发「完美闪避」：子弹时间 + 下一击必暴击",
            new GUIStyle(small) { fontSize = 20, wordWrap = true });
        Label(new Rect(0, h - 40, w, 30), "角色、敌人、武器、道具由 Meshy 生成 · 场景拼块由 Blender 制作", small);
    }

    void DrawClassSelect(float w, float h)
    {
        Box(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.6f));
        Label(new Rect(0, h * 0.08f, w, 70), "选择角色", title);
        int n = Classes.All.Count;
        float cw = Mathf.Min(330f, (w - 80f) / n - 20f), ch = 430f, gap = 20f;
        float x0 = w / 2 - (cw * n + gap * (n - 1)) / 2, y = h * 0.2f;
        for (int i = 0; i < n; i++)
        {
            var c = Classes.All[i];
            var r = new Rect(x0 + i * (cw + gap), y, cw, ch);
            if (GUI.Button(r, "", button)) ChooseClass?.Invoke(c.id);
            Label(new Rect(r.x, r.y + 14, r.width, 50), $"{i + 1}. {c.name}", new GUIStyle(center) { fontSize = 36 });
            Label(new Rect(r.x, r.y + 62, r.width, 28), c.desc, new GUIStyle(small) { fontSize = 18 });
            string weapons = string.Join(" + ", System.Array.ConvertAll(c.weapons, id => Weapons.Get(id).name));
            Label(new Rect(r.x + 20, r.y + 96, r.width - 40, 130),
                $"生命 {c.maxHp}   护甲 {c.maxArmor}   能量 {c.maxEnergy}\n闪避 {c.stamina} 次   移速 {c.speed:0.0}   暴击 {c.crit * 100f:0}%\n特性：{c.trait}\n初始武器：{weapons}",
                new GUIStyle(left) { fontSize = 17, wordWrap = true, alignment = TextAnchor.UpperLeft });
            Label(new Rect(r.x + 20, r.y + 232, r.width - 40, 36), $"技能（右键 / F）：{c.skillName}", new GUIStyle(left) { fontSize = 20, fontStyle = FontStyle.Bold });
            Label(new Rect(r.x + 20, r.y + 268, r.width - 40, 150), $"{c.skillDesc}\n冷却 {c.cooldown:0} 秒",
                new GUIStyle(left) { fontSize = 17, wordWrap = true, alignment = TextAnchor.UpperLeft });
        }
        Label(new Rect(0, y + ch + 24, w, 30), "点击卡片或按 1 – 4", small);
    }

    /// Left-column offset: the mech's durability bar pushes the rest down one row.
    float Off => player && player.InMech ? 30f : 0f;

    /// While piloting: the mech's weapons (bottom right) and its special (bottom left).
    void DrawMech(float w, float h)
    {
        var d = player.mechDef;
        var r = new Rect(w - 330, h - 150, 310, 100);
        Box(r, new Color(0f, 0f, 0f, 0.45f));
        Box(new Rect(r.x, r.y, 5, r.height), d.color);
        var nameStyle = new GUIStyle(left) { fontSize = 28, richText = true };
        nameStyle.normal.textColor = d.color;
        Label(new Rect(r.x + 16, r.y + 6, 290, 36), $"{d.name}  <size=16>{(d.heavy ? "重装" : "轻装")}</size>", nameStyle);
        Label(new Rect(r.x + 16, r.y + 42, 290, 26), $"左键 {d.ranged} · 右键 {d.melee}", new GUIStyle(left) { fontSize = 16 });
        string gun = player.overheated ? "<color=#FF7A5A>过热冷却中</color>" : player.overdriveT > 0f ? $"<color=#7FE8FF>过载 {player.overdriveT:0.0}s</color>" : "不耗能量";
        Label(new Rect(r.x + 16, r.y + 68, 290, 26), gun, new GUIStyle(left) { fontSize = 16, richText = true });

        var sr = new Rect(20, h - 118, 260, 62);
        Box(sr, new Color(0f, 0f, 0f, 0.45f));
        float ready = 1f - player.mechSpecialCd / d.specialCooldown;
        Box(new Rect(sr.x, sr.yMax - 6, sr.width * ready, 6), ready >= 1f ? d.color : new Color(0.5f, 0.5f, 0.6f));
        Label(new Rect(sr.x + 14, sr.y + 4, sr.width - 20, 30), $"{d.special}  <size=15>F</size>", new GUIStyle(left) { fontSize = 22, richText = true });
        Label(new Rect(sr.x + 14, sr.y + 32, sr.width - 20, 24), player.mechSpecialCd > 0f ? $"冷却 {player.mechSpecialCd:0.0}s" : "就绪", new GUIStyle(left) { fontSize = 16 });
    }

    void DrawSkill(float w, float h)
    {
        var s = player ? player.skill : null;
        if (s == null || s.def == null) return;
        var r = new Rect(20, h - 118, 260, 62);
        Box(r, new Color(0f, 0f, 0f, 0.45f));
        float ready = s.def.cooldown <= 0f ? 1f : 1f - s.cooldownLeft / s.def.cooldown;
        Box(new Rect(r.x, r.yMax - 6, r.width * ready, 6), s.Active ? new Color(1f, 0.85f, 0.3f) : ready >= 1f ? new Color(0.45f, 0.95f, 1f) : new Color(0.5f, 0.5f, 0.6f));
        string state = s.Active ? $"生效中 {s.activeLeft:0.0}s" : s.cooldownLeft > 0f ? $"冷却 {s.cooldownLeft:0.0}s" : "就绪";
        Label(new Rect(r.x + 14, r.y + 4, r.width - 20, 30), $"{s.def.skillName}  <size=15>右键 / F</size>", new GUIStyle(left) { fontSize = 22, richText = true });
        Label(new Rect(r.x + 14, r.y + 32, r.width - 20, 24), state, new GUIStyle(left) { fontSize = 16 });
    }

    public void OpenSettings()
    {
        settingsReturn = screen;
        screen = Screen.Settings;
    }

    void DrawFps(float w)
    {
        float sum = 0f;
        var sorted = (float[])frameTimes.Clone();
        System.Array.Sort(sorted);
        foreach (var t in frameTimes) sum += t;
        float avg = sum / frameTimes.Length, worst = sorted[(int)(sorted.Length * 0.99f)];
        if (avg <= 0f) return;
        var st = new GUIStyle(small) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = 1f / avg >= 80f ? new Color(0.6f, 1f, 0.6f) : 1f / avg >= 60f ? Color.yellow : new Color(1f, 0.5f, 0.5f);
        Label(new Rect(w / 2 - 150, 4, 300, 22), $"FPS {1f / avg:0}  ·  1% 低帧 {1f / worst:0}", st);
    }

    float SliderRow(float y, float x, string label, float value, float min, float max, string shown)
    {
        Label(new Rect(x, y, 240, 32), label, new GUIStyle(left) { fontSize = 19 });
        float v = GUI.HorizontalSlider(new Rect(x + 250, y + 12, 260, 20), value, min, max);
        Label(new Rect(x + 525, y, 120, 32), shown, new GUIStyle(left) { fontSize = 18 });
        return v;
    }

    bool ToggleRow(float y, float x, string label, bool value)
    {
        Label(new Rect(x, y, 240, 32), label, new GUIStyle(left) { fontSize = 19 });
        if (GUI.Button(new Rect(x + 250, y + 2, 110, 28), value ? "开" : "关", button)) return !value;
        return value;
    }

    int ChoiceRow(float y, float x, string label, int value, string[] names)
    {
        Label(new Rect(x, y, 240, 32), label, new GUIStyle(left) { fontSize = 19 });
        if (GUI.Button(new Rect(x + 250, y + 2, 34, 28), "<", button)) value = (value + names.Length - 1) % names.Length;
        Label(new Rect(x + 290, y, 190, 32), names[value], new GUIStyle(center) { fontSize = 18 });
        if (GUI.Button(new Rect(x + 486, y + 2, 34, 28), ">", button)) value = (value + 1) % names.Length;
        return value;
    }

    /// Comfort settings based on the motion-sickness checklist the user shared.
    void DrawSettings(float w, float h)
    {
        Box(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.82f));
        Label(new Rect(0, 18, w, 56), "设置 · 防晕 3D", new GUIStyle(title) { fontSize = 40 });
        float x = w / 2 - 330, y = 86, row = 38;
        GameSettings.fov = Mathf.Round(SliderRow(y, x, "视野 FOV（大一些更不晕）", GameSettings.fov, 55f, 90f, $"{GameSettings.fov:0}°")); y += row;
        GameSettings.sensitivity = SliderRow(y, x, "鼠标灵敏度", GameSettings.sensitivity, 0.3f, 3f, $"{GameSettings.sensitivity:0.0}×"); y += row;
        GameSettings.invertX = ToggleRow(y, x, "水平反转（像拖动画布）", GameSettings.invertX); y += row;
        GameSettings.invertY = ToggleRow(y, x, "垂直反转", GameSettings.invertY); y += row;
        GameSettings.shake = SliderRow(y, x, "屏幕震动", GameSettings.shake, 0f, 1f, $"{GameSettings.shake * 100f:0}%"); y += row;
        GameSettings.flashes = ToggleRow(y, x, "受伤 / 闪避闪屏", GameSettings.flashes); y += row;
        GameSettings.vsync = ToggleRow(y, x, "垂直同步（会增加延迟）", GameSettings.vsync); y += row;
        int capIndex = Mathf.Max(0, System.Array.IndexOf(GameSettings.FpsCaps, GameSettings.fpsCap));
        GameSettings.fpsCap = GameSettings.FpsCaps[ChoiceRow(y, x, "帧率上限", capIndex, GameSettings.FpsCapNames)]; y += row;
        GameSettings.renderScale = Mathf.Round(SliderRow(y, x, "渲染分辨率（低了帧数高）", GameSettings.renderScale, 0.6f, 1f, $"{GameSettings.renderScale * 100f:0}%") * 20f) / 20f; y += row;
        GameSettings.smaa = ToggleRow(y, x, "SMAA 抗锯齿（更柔，更糊）", GameSettings.smaa); y += row;
        GameSettings.bloom = ToggleRow(y, x, "泛光", GameSettings.bloom); y += row;
        GameSettings.vignette = ToggleRow(y, x, "暗角", GameSettings.vignette); y += row;
        GameSettings.showFps = ToggleRow(y, x, "显示帧率（目标 ≥ 80）", GameSettings.showFps); y += row;
        int win = ChoiceRow(y, x, "窗口大小", GameSettings.window, GameSettings.WindowNames);
        if (win != GameSettings.window) { GameSettings.window = win; GameSettings.ApplyWindow(); }
        y += row + 8;
        Label(new Rect(x - 40, y, 740, 64),
            "依据「解决晕 3D」：降延迟（关垂直同步、帧率 ≥ 80）、不用鼠标平滑、画面别太大（建议窗口宽度不超过观看距离的一半）、关运动模糊和景深、视野调大、减少闪烁和抖动。",
            new GUIStyle(small) { fontSize = 16, wordWrap = true });
        if (GUI.changed) SettingsChanged?.Invoke();
        if (GUI.Button(new Rect(w / 2 - 110, h - 70, 220, 50), "返回", button) || Input.GetKeyDown(KeyCode.Escape))
        {
            GameSettings.Save();
            SettingsChanged?.Invoke();
            screen = settingsReturn;
        }
    }

    void DrawWeapons(float w, float h)
    {
        var cur = player.Weapon;
        var other = player.weapons[1 - player.slot];
        var r = new Rect(w - 330, h - 150, 310, 100);
        Box(r, new Color(0f, 0f, 0f, 0.45f));
        Box(new Rect(r.x, r.y, 5, r.height), Weapons.RarityColors[cur.rarity]);
        var nameStyle = new GUIStyle(left) { fontSize = 28 };
        nameStyle.normal.textColor = Weapons.RarityColors[cur.rarity];
        string tag = cur.chargeable || cur.kind == WeaponKind.Charge ? "  <size=15><color=#FFD27A>可蓄力</color></size>" : "";
        Label(new Rect(r.x + 16, r.y + 6, 290, 36), $"{cur.name}  <size=16>{Weapons.RarityNames[cur.rarity]}</size>{tag}", new GUIStyle(nameStyle) { richText = true });
        string cost = cur.energyPerSec > 0f ? $"每秒 {cur.energyPerSec:0} 能量" : cur.energy > 0 ? $"每次 {cur.energy} 能量" : "不耗能量";
        Label(new Rect(r.x + 16, r.y + 42, 290, 26), cur.desc, new GUIStyle(left) { fontSize = 16 });
        Label(new Rect(r.x + 16, r.y + 68, 290, 26), cost, new GUIStyle(left) { fontSize = 16 });
        if (other != null) Label(new Rect(w - 330, h - 186, 310, 30), $"Q 切换：{other.name}", new GUIStyle(left) { fontSize = 18 });
    }

    void DrawCrosshair(float w, float h)
    {
        var c = hitMarker > 0f ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 1f, 1f, 0.9f);
        float s = hitMarker > 0f ? 14f : 10f;
        Box(new Rect(w / 2 - s, h / 2 - 1, s * 2, 2), c);
        Box(new Rect(w / 2 - 1, h / 2 - s, 2, s * 2), c);
        if (!player) return;
        if (player.Charge > 0f)
        {
            // Bow charge: a bar that fills above the stamina pips, gold when full.
            Box(new Rect(w / 2 - 40, h / 2 + 16, 80, 6), new Color(0f, 0f, 0f, 0.5f));
            Box(new Rect(w / 2 - 40, h / 2 + 16, 80 * player.Charge, 6), player.Charge >= 0.95f ? new Color(1f, 0.8f, 0.2f) : Color.white);
        }
        // Light mech: the cannon's heat under the crosshair, red when it has overheated.
        if (player.InMech && !player.mechDef.heavy && (player.heat > 0.01f || player.overheated))
        {
            Box(new Rect(w / 2 - 40, h / 2 + 16, 80, 6), new Color(0f, 0f, 0f, 0.5f));
            Box(new Rect(w / 2 - 40, h / 2 + 16, 80 * player.heat, 6), player.overheated ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.8f, 0.4f));
        }
        // Stamina pips under the crosshair: each jump or dash spends one (a mech's boost charges).
        int n = player.maxStamina;
        float pw = 22f, gap = 5f, x0 = w / 2 - (n * pw + (n - 1) * gap) / 2, y = h / 2 + 28;
        for (int i = 0; i < n; i++)
        {
            float fill = Mathf.Clamp01(player.stamina - i);
            Box(new Rect(x0 + i * (pw + gap), y, pw, 6), new Color(0f, 0f, 0f, 0.45f));
            Box(new Rect(x0 + i * (pw + gap), y, pw * fill, 6), fill >= 1f ? new Color(0.45f, 0.95f, 1f, 0.95f) : new Color(0.45f, 0.95f, 1f, 0.45f));
        }
    }

    /// Name tags (and the shield gauge) over elite enemies that are on screen.
    void DrawEliteTags()
    {
        var cam = Camera.main;
        if (!cam) return;
        var tag = new GUIStyle(center) { fontSize = 17, richText = true };
        foreach (var e in Enemy.Elites)
        {
            if (!e || e.dead) continue;
            Vector3 head = e.transform.position + Vector3.up * (e.AimPoint.y - e.transform.position.y) * 2.1f;
            Vector3 sp = cam.WorldToScreenPoint(head);
            if (sp.z < 0.5f || sp.z > 40f) continue;
            float x = sp.x, y = UnityEngine.Screen.height - sp.y;
            Label(new Rect(x - 90, y - 34, 180, 24), $"<color=#{ColorUtility.ToHtmlStringRGB(e.EliteColor)}>{e.EliteName}</color>", tag);
            Box(new Rect(x - 40, y - 8, 80, 5), new Color(0f, 0f, 0f, 0.5f));
            Box(new Rect(x - 40, y - 8, 80f * e.hp / Mathf.Max(1, e.maxHp), 5), new Color(0.9f, 0.25f, 0.25f));
            if (e.maxShield > 0 && e.shield > 0)
                Box(new Rect(x - 40, y - 2, 80f * e.shield / e.maxShield, 4), new Color(0.45f, 0.85f, 1f));
        }
    }

    void DrawNumbers()
    {
        var cam = player ? player.cam : null;
        if (!cam) return;
        foreach (var n in numbers)
        {
            var sp = cam.WorldToScreenPoint(n.pos);
            if (sp.z < 0f) continue;
            var st = new GUIStyle(number) { fontSize = n.crit ? 34 : 26 };
            st.normal.textColor = n.crit ? new Color(1f, 0.8f, 0.2f, 1f - n.t / 0.8f) : new Color(1f, 1f, 1f, 1f - n.t / 0.8f);
            Label(new Rect(sp.x - 60, UnityEngine.Screen.height - sp.y - 20, 120, 40), n.text, st);
        }
    }

    void DrawBoss(float w)
    {
        var b = Enemy.ActiveBoss;
        if (!b || b.dead) return;
        var r = new Rect(w / 2 - 300, 24, 600, 22);
        Box(r, new Color(0f, 0f, 0f, 0.6f));
        Box(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(b.hp / (float)b.maxHp), r.height - 4), new Color(0.85f, 0.15f, 0.2f));
        string state = b.Stunned ? " · 眩晕（伤害翻倍）" : b.Enraged ? " · 狂暴" : "";
        Label(new Rect(r.x, r.y + 22, r.width, 30), b.displayName + state, new GUIStyle(center) { fontSize = 20 });
    }

    void DrawBuffs(float w, float h)
    {
        Box(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.6f));
        Label(new Rect(0, h * 0.18f, w, 70), $"第 {RunState.floor} 层 · 选择一个增益", title);
        float cw = 300, ch = 220, gap = 30;
        float x0 = w / 2 - (cw * offer.Count + gap * (offer.Count - 1)) / 2;
        for (int i = 0; i < offer.Count; i++)
        {
            var r = new Rect(x0 + i * (cw + gap), h * 0.36f, cw, ch);
            if (GUI.Button(r, $"{i + 1}. {offer[i].name}\n\n{offer[i].desc}", button)) PickBuff(i);
        }
        Label(new Rect(0, h * 0.36f + ch + 20, w, 30), "点击卡片或按 1 / 2 / 3", small);
    }

    void DrawMenu(float w, float h, string heading, string primary, System.Action primaryAction)
    {
        Box(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.6f));
        bool over = screen == Screen.Dead || screen == Screen.Won;
        float top = over ? h * 0.12f : h * 0.25f;
        Label(new Rect(0, top, w, 80), heading, title);
        float y = h * 0.48f;
        if (over) y = DrawSummary(w, top + 90f) + 20f;
        else Label(new Rect(0, top + 80, w, 30), $"到达第 {RunState.floor} 层 · 击杀 {RunState.kills}", small);
        if (GUI.Button(new Rect(w / 2 - 140, y, 280, 56), primary, button)) primaryAction?.Invoke();
        if (screen == Screen.Pause && GUI.Button(new Rect(w / 2 - 140, y + 70, 280, 56), "重新开始", button)) Restart?.Invoke();
        if (screen == Screen.Pause && GUI.Button(new Rect(w / 2 - 140, y + 140, 280, 56), "设置 · 防晕 3D", button)) OpenSettings();
        if (GUI.Button(new Rect(w / 2 - 140, y + (screen == Screen.Pause ? 210 : 70), 280, 56), "退出游戏", button)) Quit?.Invoke();
    }

    /// End-of-run card: time, progress, combat numbers, the weapons you carried and the buffs you chose.
    float DrawSummary(float w, float y)
    {
        var panel = new Rect(w / 2 - 330, y, 660, 250);
        Box(panel, new Color(0.08f, 0.08f, 0.12f, 0.85f));
        int secs = Mathf.FloorToInt(RunState.runTime);
        var cls = Classes.Get(RunState.classId);
        var big = new GUIStyle(left) { fontSize = 20, richText = true };
        float x = panel.x + 24, lh = 32;
        Label(new Rect(x, y + 14, 620, lh), $"{cls.name} · 用时 {secs / 60}:{secs % 60:00} · 到达第 {RunState.floor} 层 · 击杀 {RunState.kills}（首领 {RunState.bossesKilled}）", big);
        Label(new Rect(x, y + 14 + lh, 620, lh), $"造成伤害 {RunState.damageDealt} · 受到伤害 {RunState.damageTaken} · 完美闪避 {RunState.perfectDodges} · 驾驶机甲 {RunState.mechsPiloted} 次", big);
        var names = new List<string>();
        foreach (var id in RunState.weaponsSeen)
        {
            var wd = Weapons.Get(id);
            if (wd != null) names.Add($"<color=#{ColorUtility.ToHtmlStringRGB(Weapons.RarityColors[wd.rarity])}>{wd.name}</color>");
        }
        Label(new Rect(x, y + 24 + lh * 2, 620, lh * 2), "用过的武器：" + (names.Count > 0 ? string.Join("、", names) : "—"), new GUIStyle(big) { wordWrap = true });
        var buffNames = new List<string>();
        foreach (var id in RunState.buffs)
        {
            var b = Buffs.All.Find(d => d.id == id);
            buffNames.Add(b != null ? b.name : id);
        }
        Label(new Rect(x, y + 34 + lh * 4, 620, lh * 2), "选过的增益：" + (buffNames.Count > 0 ? string.Join("、", buffNames) : "—"), new GUIStyle(big) { wordWrap = true });
        return panel.yMax;
    }

    /// Visited rooms in full, their neighbours dimmed, the player as a dot (Soul Knight style).
    void DrawMinimap(float screenW)
    {
        if (!dungeon || !player) return;
        const float scale = 0.9f; // pixels per metre
        const float size = 240f;
        var panel = new Rect(screenW - size - 20, 20, size, size);
        Box(panel, new Color(0f, 0f, 0f, 0.35f));
        Vector3 c = player.transform.position;

        Rect ToMap(Vector3 min, Vector3 max)
        {
            float x0 = panel.center.x + (min.x - c.x) * scale;
            float x1 = panel.center.x + (max.x - c.x) * scale;
            float y0 = panel.center.y - (max.z - c.z) * scale;
            float y1 = panel.center.y - (min.z - c.z) * scale;
            return Rect.MinMaxRect(Mathf.Max(x0, panel.xMin), Mathf.Max(y0, panel.yMin), Mathf.Min(x1, panel.xMax), Mathf.Min(y1, panel.yMax));
        }

        foreach (var (a, b) in dungeon.links)
        {
            var ra = dungeon.rooms.Find(r => r.cell == a);
            var rb = dungeon.rooms.Find(r => r.cell == b);
            if (!ra.visited && !rb.visited) continue;
            Vector3 p = ra.Center, q = rb.Center;
            var lo = Vector3.Min(p, q) - new Vector3(1.5f, 0f, 1.5f);
            var hi = Vector3.Max(p, q) + new Vector3(1.5f, 0f, 1.5f);
            var r = ToMap(lo, hi);
            if (r.width > 0 && r.height > 0) Box(r, new Color(0.6f, 0.6f, 0.7f, 0.6f));
        }
        foreach (var room in dungeon.rooms)
        {
            bool known = room.visited || dungeon.links.Exists(l =>
                (l.a == room.cell && dungeon.rooms.Find(r => r.cell == l.b).visited) ||
                (l.b == room.cell && dungeon.rooms.Find(r => r.cell == l.a).visited));
            if (!known) continue;
            var r = ToMap(room.min, room.max);
            if (r.width <= 0 || r.height <= 0) continue;
            Color col = room.kind switch
            {
                Dungeon.Kind.Start => new Color(0.5f, 0.8f, 0.5f),
                Dungeon.Kind.Chest => new Color(0.95f, 0.8f, 0.3f),
                Dungeon.Kind.Shop => new Color(0.35f, 0.9f, 0.75f),
                Dungeon.Kind.End => new Color(0.7f, 0.5f, 1f),
                Dungeon.Kind.Boss => new Color(1f, 0.35f, 0.3f),
                _ => new Color(0.75f, 0.75f, 0.85f),
            };
            if (!room.visited) col.a = 0.35f;
            if (room == dungeon.current) col = Color.Lerp(col, Color.white, 0.4f);
            Box(r, col);
            // An icon on the rooms that matter: boss, chest, shop.
            string icon = room.kind switch
            {
                Dungeon.Kind.Boss => "<color=#FF5A4A>首</color>",
                Dungeon.Kind.Chest => "<color=#7A5A10>宝</color>",
                Dungeon.Kind.Shop => "<color=#0E5A48>商</color>",
                _ => null,
            };
            if (icon != null) Label(new Rect(r.center.x - 15, r.center.y - 14, 30, 28), icon, new GUIStyle(center) { fontSize = 18, richText = true });
        }
        // The portal once it is open, and parked mechs.
        if (dungeon.portal)
        {
            var pr = ToMap(dungeon.portal.transform.position - Vector3.one * 2.5f, dungeon.portal.transform.position + Vector3.one * 2.5f);
            if (pr.width > 0 && pr.height > 0) Label(new Rect(pr.center.x - 15, pr.center.y - 14, 30, 28), "<color=#C08CFF>◎</color>", new GUIStyle(center) { fontSize = 20, richText = true });
        }
        foreach (var m in ParkedMech.Live)
        {
            var mr = ToMap(m.transform.position - Vector3.one * 2f, m.transform.position + Vector3.one * 2f);
            if (mr.width > 0 && mr.height > 0) Label(new Rect(mr.center.x - 15, mr.center.y - 13, 30, 26), $"<color=#{ColorUtility.ToHtmlStringRGB(m.def.color)}>机</color>", new GUIStyle(center) { fontSize = 15, richText = true });
        }
        Box(new Rect(panel.center.x - 4, panel.center.y - 4, 8, 8), new Color(0.3f, 0.8f, 1f));
    }
}
