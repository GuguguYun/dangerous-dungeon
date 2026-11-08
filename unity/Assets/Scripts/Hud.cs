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
        public Color? color;
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

    /// A word that floats up like a damage number (reaction names).
    public void AddText(Vector3 pos, string text, Color c) =>
        numbers.Add(new Number { pos = pos, text = text, crit = true, color = c });

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
        UiKit.Init(font);
        button = UiKit.Button;
    }

    /// Seconds of actual play on this floor; the controls line fades out after a while.
    float playT;

    static Color ClassColor(string id) => id switch
    {
        "knight" => new Color(0.45f, 0.7f, 1f),
        "assassin" => new Color(0.75f, 0.5f, 1f),
        "mage" => new Color(1f, 0.55f, 0.3f),
        "priest" => new Color(1f, 0.88f, 0.45f),
        _ => Color.white,
    };

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
        if (screen == Screen.None && player && !player.dead) playT += dt;
        IconStudio.I?.ShowClassPreviews(screen == Screen.ClassSelect);
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

    /// A button that clicks when pressed.
    /// A wooden menu button that clicks when pressed.
    static bool Btn(Rect r, string text, GUIStyle style)
    {
        bool over = r.Contains(Event.current.mousePosition);
        UiKit.DrawButton(r, over, over && Input.GetMouseButton(0));
        var ts = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, richText = true };
        ts.normal.background = ts.hover.background = ts.active.background = ts.focused.background = null;
        UiKit.Text(new Rect(r.x, r.y - 2, r.width, r.height), text, ts, over ? UiKit.Gold : UiKit.Paper);
        if (!GUI.Button(r, GUIContent.none, GUIStyle.none)) return false;
        Sfx.Play("ui_click", 0.6f, 0.03f);
        return true;
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
            UiKit.Text(new Rect(w / 2 - 100, h / 2 + 44, 200, 26), $"中毒 {player.poisoned:0.0}s", new GUIStyle(center) { fontSize = 18 }, new Color(0.55f, 1f, 0.45f));
        }
        if (GameSettings.flashes && perfect > 0f) Box(new Rect(0, 0, w, h), new Color(0.3f, 0.9f, 1f, perfect * 0.35f));
        if (GameSettings.showFps) DrawFps(w);

        if (player)
        {
            DrawStatus();
            // The companion drone's health, just above the skill card.
            var pet = Pet.Current;
            if (pet && pet.isActiveAndEnabled)
            {
                var pr = new Rect(92, h - 158, 236, 36);
                UiKit.DrawPanel(pr);
                UiKit.Text(new Rect(pr.x + 54, pr.y, 70, 36), "无人机", new GUIStyle(left) { fontSize = 14 });
                UiKit.Bar(new Rect(pr.x + 108, pr.y + 12, 74, 12), pet.hp, pet.maxHp, pet.offline ? new Color(0.4f, 0.4f, 0.45f) : new Color(0.3f, 0.8f, 0.95f));
                UiKit.Text(new Rect(pr.x + 188, pr.y, 50, 36), pet.offline ? "重启" : $"{pet.hp}/{pet.maxHp}", new GUIStyle(left) { fontSize = 13 });
            }
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

        DrawCrosshair(w, h);
        DrawEliteTags();
        DrawLootTags();
        DrawCombo(w, h);
        DrawNumbers();
        DrawBoss(w);
        DrawMinimap(w);

        if (screen == Screen.None) DrawPrompt(w, h);
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
        float hintA = Mathf.Clamp01((45f - playT) / 5f);
        if (screen == Screen.None && player && player.InMech)
        {
            var d = player.mechDef;
            Label(new Rect(0, h - 36, w, 28), $"WASD 移动 · 左键 {d.ranged} · 右键 {d.melee} · F {d.special} · Shift {d.dashName} · 空格 {d.jumpName} · E 离开机甲 · Esc 暂停", small);
        }
        else if (screen == Screen.None && hintA > 0f)
        {
            var hs = new GUIStyle(small);
            hs.normal.textColor = new Color(1f, 1f, 1f, 0.75f * hintA);
            Label(new Rect(0, h - 36, w, 28), "WASD 移动 · 鼠标 瞄准 · 左键 攻击 · 右键/F 技能 · Q 换武器 · E 拾取 · Shift 冲刺 · 空格 跳 · Esc 暂停", hs);
        }

        switch (screen)
        {
            case Screen.Buff: DrawBuffs(w, h); break;
            case Screen.Pause: DrawMenu(w, h, "已暂停", "继续", Resume); break;
            case Screen.Dead: DrawMenu(w, h, "你倒下了", "再来一局", Restart); break;
            case Screen.Won: DrawMenu(w, h, "通关！你打穿了地牢", "再来一局", Restart); break;
        }
        GUI.color = Color.white;
    }

    // -uiswatch: grey test patches for checking how IMGUI shows textures in this linear-space project.
    static readonly bool swatch = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-uiswatch") >= 0;
    static Texture2D[] swatches;

    static void DrawSwatches()
    {
        if (swatches == null)
        {
            Texture2D T(bool linear, float v)
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);
                var px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = new Color(v, v, v, 1f);
                t.SetPixels(px);
                t.Apply();
                return t;
            }
            swatches = new[] { T(false, 0.184f), T(true, 0.184f), T(true, 0.028f) };
        }
        for (int i = 0; i < 3; i++) GUI.DrawTexture(new Rect(20 + i * 90, 300, 80, 80), swatches[i]);
        var panel = UiKit.Art("panel");
        if (panel) GUI.DrawTextureWithTexCoords(new Rect(290, 300, 80, 80), panel, new Rect(0.45f, 0.45f, 0.1f, 0.1f));
        UiKit.Fill(new Rect(380, 300, 80, 80), new Color(0.184f, 0.184f, 0.184f));
        Debug.Log($"SWATCH panel format={(panel ? panel.format.ToString() : "none")} linear={(panel ? panel.isDataSRGB.ToString() : "-")} colorSpace={QualitySettings.activeColorSpace}");
    }

    void DrawTitle(float w, float h)
    {
        if (swatch) DrawSwatches();
        UiKit.Vignette(w, h);
        var t = new GUIStyle(title) { fontSize = 96 };
        UiKit.Text(new Rect(0, h * 0.12f, w, 110), "模境迷城", t, UiKit.Gold);
        UiKit.Text(new Rect(0, h * 0.12f + 104, w, 30), "M E S H Y   D U N G E O N", new GUIStyle(small) { fontSize = 18 }, new Color(0.85f, 0.85f, 0.95f, 0.85f));
        UiKit.Text(new Rect(0, h * 0.12f + 140, w, 30), "提示词铸成的迷城每晚都会重生 —— 带上武器，闯到最深处", new GUIStyle(small) { fontSize = 20 });
        float bw = 320, bx = w / 2 - bw / 2, by = h * 0.45f;
        if (Btn(new Rect(bx, by, bw, 66), "开始冒险", button)) Begin?.Invoke();
        if (Btn(new Rect(bx, by + 80, bw, 54), "设置 · 防晕 3D", button)) OpenSettings();
        if (Btn(new Rect(bx, by + 148, bw, 54), "退出游戏", button)) Quit?.Invoke();
        // Controls in a small panel at the bottom.
        var cr = new Rect(w / 2 - 430, h - 170, 860, 112);
        UiKit.DrawPanel(cr);
        var cs = new GUIStyle(left) { fontSize = 16, wordWrap = true, alignment = TextAnchor.UpperLeft };
        UiKit.Text(new Rect(cr.x + 22, cr.y + 12, 400, 90), "<color=#F7CC75>操作</color>\nWASD 移动 · 鼠标瞄准 · 左键攻击\nQ 换武器 · E 拾取/购买/搭乘 · 右键或 F 技能", cs);
        UiKit.Text(new Rect(cr.x + 440, cr.y + 12, 400, 90), "<color=#F7CC75>闪避</color>\n空格跳：时机对了子弹从脚下穿过\nShift 冲刺躲子弹 · 贴身躲开触发「完美闪避」", cs);
        if (PlayerPrefs.HasKey("best_floor"))
        {
            int bt = PlayerPrefs.GetInt("best_time");
            string result = PlayerPrefs.GetInt("best_won") == 1 ? "通关" : $"到达第 {PlayerPrefs.GetInt("best_floor")} 层";
            UiKit.Text(new Rect(0, by + 216, w, 28), $"<color=#F7CC75>最佳纪录</color>  {PlayerPrefs.GetString("best_class")} · {result} · 用时 {bt / 60}:{bt % 60:00} · 击杀 {PlayerPrefs.GetInt("best_kills")}",
                new GUIStyle(small) { fontSize = 18 });
        }
        UiKit.Text(new Rect(0, h - 40, w, 30), "角色、敌人、武器、道具：Meshy 生成 · 场景：Blender · 音效：Kenney 等（CC0）· 音乐：OpenGameArt（CC0）", small);
    }

    static readonly Color InkColor = new Color(0.24f, 0.14f, 0.07f);

    void DrawClassSelect(float w, float h)
    {
        UiKit.Vignette(w, h);
        UiKit.Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.02f, 0.05f, 0.55f));
        UiKit.DrawBanner(new Rect(w / 2 - 300, h * 0.012f, 600, 100), "选择你的英雄", new GUIStyle(title) { fontSize = 38 });
        int n = Classes.All.Count;
        float gap = 28f;
        float ch = Mathf.Min(h * 0.62f, 600f), cw = Mathf.Min(ch / 1.5f, (w - 80f - gap * (n - 1)) / n);
        ch = cw * 1.5f;
        float x0 = w / 2 - (cw * n + gap * (n - 1)) / 2, y = h * 0.135f;
        var mouse = Event.current.mousePosition;
        string hover = null;
        for (int i = 0; i < n; i++)
        {
            var c = Classes.All[i];
            var slot = new Rect(x0 + i * (cw + gap), y, cw, ch);
            bool over = slot.Contains(mouse);
            if (over) hover = c.id;
            var cc = ClassColor(c.id);
            // The hovered card lifts a little and glows in the class colour.
            var r = over ? new Rect(slot.x - cw * 0.03f, slot.y - 12f, cw * 1.06f, ch * 1.06f) : slot;
            if (over) UiKit.Halo(r, new Color(cc.r, cc.g, cc.b, 0.9f), 22f);
            DrawHeroCard(r, c, i + 1);
            // Under the card: dodges, speed, crit and the starting weapons.
            var sr = new Rect(slot.x, slot.yMax + 16, cw, 78);
            UiKit.DrawPanel(sr);
            var st = new GUIStyle(left) { fontSize = Mathf.RoundToInt(Mathf.Clamp(cw * 0.045f, 13f, 16f)) };
            UiKit.Text(new Rect(sr.x + 18, sr.y + 8, 50, 24), "闪避", st, UiKit.Dim);
            for (int k = 0; k < c.stamina; k++)
                UiKit.Fill(new Rect(sr.x + 58 + k * 15, sr.y + 15, 11, 11), new Color(0.45f, 0.95f, 1f));
            UiKit.Text(new Rect(sr.x + 58 + 5 * 15 + 8, sr.y + 8, cw, 24), $"移速 {c.speed:0.0} · 暴击 {c.crit * 100f:0}%", st, UiKit.Dim);
            float wx = sr.x + 16;
            foreach (var id in c.weapons)
            {
                var wd = Weapons.Get(id);
                var ic = IconStudio.I ? IconStudio.I.Weapon(id) : null;
                if (ic) GUI.DrawTexture(new Rect(wx, sr.y + 36, 52, 32), ic, ScaleMode.ScaleToFit);
                UiKit.Text(new Rect(wx + 54, sr.y + 36, cw / 2 - 70, 32), wd.name, st, Weapons.RarityColors[wd.rarity]);
                wx += (cw - 32) / 2;
            }
            if (Event.current.type == EventType.MouseDown && over)
            {
                Sfx.Play("ui_click", 0.7f, 0.02f);
                ChooseClass?.Invoke(c.id);
                Event.current.Use();
            }
        }
        if (IconStudio.I) IconStudio.I.hovered = hover;
        UiKit.Text(new Rect(0, y + ch + 104, w, 30), "点击卡牌或按 1 – 4 选择", small);
    }

    /// A painted hero card: portrait in the arch, name on the ribbon, trait and skill on the
    /// parchment, health / armour / energy as gems on the corners.
    void DrawHeroCard(Rect r, ClassDef c, int key)
    {
        string card = "card_" + c.id;
        var cc = ClassColor(c.id);
        var win = UiKit.Sub(r, UiKit.Layout(card, new Rect(0.16f, 0.13f, 0.67f, 0.4f)));
        UiKit.Fill(win, new Color(cc.r * 0.22f, cc.g * 0.22f, cc.b * 0.26f, 1f));
        UiKit.Spot(new Rect(win.x - win.width * 0.2f, win.y, win.width * 1.4f, win.height * 1.2f), new Color(cc.r, cc.g, cc.b, 0.55f));
        var view = IconStudio.I ? IconStudio.I.ClassView(c.id) : null;
        // The hero fills the arch (feet hidden by the ribbon); the frame drawn next covers the overflow.
        if (view) GUI.DrawTexture(new Rect(win.x - win.width * 0.2f, win.y - win.height * 0.08f, win.width * 1.4f, win.height * 1.4f), view, ScaleMode.ScaleToFit, true);
        var frame = UiKit.Art(card);
        if (frame) GUI.DrawTexture(r, frame, ScaleMode.StretchToFill, true);
        else GUI.Box(r, GUIContent.none, UiKit.Card);
        var text = UiKit.Sub(r, UiKit.Layout(card + "_text", new Rect(0.13f, 0.63f, 0.74f, 0.29f)));
        float ribbonY = (win.yMax + text.y) / 2f;
        float fs = r.width;
        var nameStyle = new GUIStyle(center) { fontSize = Mathf.RoundToInt(fs * 0.088f) };
        var nameRect = new Rect(r.x, ribbonY - fs * 0.07f, r.width, fs * 0.13f);
        if (c.id == "priest") Ink(nameRect, c.name, new GUIStyle(nameStyle) { alignment = TextAnchor.MiddleCenter }, new Color(0.35f, 0.22f, 0.08f)); // ivory ribbon
        else UiKit.Text(nameRect, c.name, nameStyle, UiKit.Paper);
        // Rules text on the parchment, in dark ink.
        var body = new Rect(text.x + text.width * 0.06f, text.y + text.height * 0.07f, text.width * 0.88f, text.height * 0.86f);
        var flavor = new GUIStyle(center) { fontSize = Mathf.RoundToInt(fs * 0.042f), fontStyle = FontStyle.Italic, wordWrap = true, alignment = TextAnchor.UpperCenter, richText = true };
        var rule = new GUIStyle(flavor) { fontStyle = FontStyle.Normal, fontSize = Mathf.RoundToInt(fs * 0.04f) };
        var fine = new GUIStyle(rule) { fontSize = Mathf.RoundToInt(fs * 0.036f) };
        float ty = body.y;
        ty = Ink(new Rect(body.x, ty, body.width, 200), c.desc, flavor, new Color(0.36f, 0.24f, 0.14f)) + 3f;
        ty = Ink(new Rect(body.x, ty, body.width, 200), $"<b><color=#8A2A10>特性</color></b> {c.trait}", rule, InkColor) + 3f;
        ty = Ink(new Rect(body.x, ty, body.width, 200), $"<b><color=#8A2A10>技能 · {c.skillName}</color></b>（{c.cooldown:0} 秒）", rule, InkColor) + 1f;
        Ink(new Rect(body.x, ty, body.width, body.yMax - ty), c.skillDesc, fine, InkColor);
        // Corner gems: energy top-left, health bottom-left, armour bottom-right.
        float g = r.width * 0.2f;
        var gemStyle = new GUIStyle(center) { fontSize = Mathf.RoundToInt(g * 0.38f) };
        Gem(new Rect(r.x - g * 0.12f, r.y + r.height * 0.02f, g * 0.8f, g), "glyph_energy", $"{c.maxEnergy}", new GUIStyle(gemStyle) { fontSize = Mathf.RoundToInt(g * 0.3f) });
        Gem(new Rect(r.x - g * 0.18f, r.yMax - g * 0.95f, g, g), "glyph_heart", $"{c.maxHp}", gemStyle);
        Gem(new Rect(r.xMax - g * 0.82f, r.yMax - g * 0.95f, g, g), "glyph_armor", $"{c.maxArmor}", gemStyle);
        // The hotkey on the ribbon's left end.
        var kr = new Rect(r.x + r.width * 0.06f, ribbonY - fs * 0.05f, fs * 0.1f, fs * 0.1f);
        GUI.DrawTexture(kr, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.06f, 0.05f, 0.04f, 0.9f).linear, 0f, kr.width / 2f);
        GUI.DrawTexture(kr, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, UiKit.Gold.linear, 2f, kr.width / 2f);
        UiKit.Text(kr, $"{key}", new GUIStyle(center) { fontSize = Mathf.RoundToInt(kr.width * 0.6f) }, UiKit.Gold);
    }

    /// A painted icon with a number on it (Hearthstone-style stat gems).
    static void Gem(Rect r, string glyph, string value, GUIStyle style)
    {
        UiKit.Icon(r, glyph);
        UiKit.Text(new Rect(r.x, r.y + r.height * 0.04f, r.width, r.height * 0.9f), value, style, Color.white);
    }

    /// Dark text on parchment (no drop shadow); returns where the next line starts.
    static float Ink(Rect r, string text, GUIStyle style, Color color)
    {
        var st = new GUIStyle(style);
        st.normal.textColor = color;
        float hgt = st.CalcHeight(new GUIContent(text), r.width);
        GUI.Label(new Rect(r.x, r.y, r.width, Mathf.Min(hgt, Mathf.Max(r.height, 1f))), text, st);
        return r.y + hgt;
    }

    /// The "press E" card above the weapon panel: a weapon on the floor (compared with what you hold),
    /// a shop ware, or a mech to board.
    void DrawPrompt(float w, float h)
    {
        if (!player) return;
        var r = new Rect(w / 2 - 300, h * 0.64f, 600, 96);
        if (player.nearbyWeapon)
        {
            var pw = player.nearbyWeapon.weapon;
            var cur = player.Weapon;
            UiKit.DrawPanel(r);
            var ic = IconStudio.I ? IconStudio.I.Weapon(pw.id) : null;
            if (ic) GUI.DrawTexture(new Rect(r.x + 14, r.y + 10, 120, 76), ic, ScaleMode.ScaleToFit);
            var rc = Weapons.RarityColors[pw.rarity];
            UiKit.Text(new Rect(r.x + 146, r.y + 6, 440, 34), $"<color=#FFD27A>E</color> 拾取  {pw.name}  <size=15>{pw.type} · {Weapons.RarityNames[pw.rarity]}</size>", new GUIStyle(left) { fontSize = 24 }, rc);
            UiKit.Text(new Rect(r.x + 146, r.y + 40, 440, 22), pw.desc, new GUIStyle(left) { fontSize = 15 });
            string Cmp(int a2, int b2) => b2 > a2 ? $"<color=#8CFF7A>{b2} ▲</color>" : b2 < a2 ? $"<color=#FF7A6A>{b2} ▼</color>" : $"{b2}";
            UiKit.Text(new Rect(r.x + 146, r.y + 64, 440, 22), $"伤害 {cur.damage} → {Cmp(cur.damage, pw.damage)}   耗能 {cur.energy} → {pw.energy}   会替换：{cur.name}", new GUIStyle(left) { fontSize = 14 }, UiKit.Dim);
        }
        else if (player.nearbyThing)
        {
            var it = player.nearbyThing;
            UiKit.DrawPanel(r);
            GUI.DrawTexture(new Rect(r.x + 44, r.y + 18, 60, 60), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, it.Tint.linear, 0f, 30f);
            UiKit.Text(new Rect(r.x + 146, r.y + 14, 440, 34), $"<color=#FFD27A>E</color>  {it.Title}", new GUIStyle(left) { fontSize = 24 }, it.Tint);
            UiKit.Text(new Rect(r.x + 146, r.y + 52, 440, 26), it.Desc, new GUIStyle(left) { fontSize = 16 });
        }
        else if (player.nearbyWare)
        {
            var it = player.nearbyWare;
            bool afford = RunState.gold >= it.price;
            UiKit.DrawPanel(r);
            var ic = it.kind == ShopItem.Kind.Weapon && IconStudio.I ? IconStudio.I.Weapon(it.weapon.id) : null;
            if (ic) GUI.DrawTexture(new Rect(r.x + 14, r.y + 10, 120, 76), ic, ScaleMode.ScaleToFit);
            else GUI.DrawTexture(new Rect(r.x + 44, r.y + 18, 60, 60), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, it.Tint.linear, 0f, 30f);
            UiKit.Text(new Rect(r.x + 146, r.y + 6, 440, 34), $"<color=#FFD27A>E</color> 购买  {it.Title}", new GUIStyle(left) { fontSize = 24 }, it.Tint);
            UiKit.Text(new Rect(r.x + 146, r.y + 40, 440, 22), it.Desc, new GUIStyle(left) { fontSize = 15 });
            UiKit.Text(new Rect(r.x + 146, r.y + 64, 440, 22), $"<color={(afford ? "#FFD45A" : "#FF6A5A")}>{it.price} 金币</color>   你有 {RunState.gold}", new GUIStyle(left) { fontSize = 15 });
        }
        else if (player.nearbyMech)
        {
            var m = player.nearbyMech;
            UiKit.DrawPanel(r);
            UiKit.Text(new Rect(r.x + 22, r.y + 6, 560, 34), $"<color=#FFD27A>E</color> 搭乘  {m.def.name}  <size=15>{(m.def.heavy ? "重装" : "轻装")}机甲 · 耐久 {m.hp}/{m.def.maxHp}</size>", new GUIStyle(left) { fontSize = 24 }, m.def.color);
            UiKit.Text(new Rect(r.x + 22, r.y + 40, 560, 22), $"{m.def.ranged} + {m.def.melee} · 技能 {m.def.special}", new GUIStyle(left) { fontSize = 15 });
            UiKit.Text(new Rect(r.x + 22, r.y + 64, 560, 22), m.def.desc, new GUIStyle(left) { fontSize = 14 }, UiKit.Dim);
        }
    }

    /// Left-column offset: the mech's durability bar pushes the rest down one row.
    float Off => player && player.InMech ? 30f : 0f;

    /// While piloting: the mech's weapons (bottom right) and its special (bottom left).
    void DrawMech(float w, float h)
    {
        var d = player.mechDef;
        var r = new Rect(w - 470, h - 142, 450, 118);
        UiKit.DrawPanel(r);
        var nameStyle = new GUIStyle(left) { fontSize = 24, richText = true };
        UiKit.Text(new Rect(r.x + 26, r.y + 8, 300, 34), $"{d.name}  <size=15>{(d.heavy ? "重装机甲" : "轻装机甲")}</size>", nameStyle, d.color);
        UiKit.Text(new Rect(r.x + 300, r.y + 8, 130, 34), "<color=#FFD27A>Q</color> 切换", new GUIStyle(left) { fontSize = 16, alignment = TextAnchor.MiddleRight });
        // The two modes side by side, the active one lit.
        void Mode(Rect m, string head, string name, bool on, string sub)
        {
            if (on) UiKit.Fill(m, new Color(d.color.r, d.color.g, d.color.b, 0.22f));
            UiKit.Text(new Rect(m.x + 10, m.y + 2, m.width - 16, 26), $"{head}  <b>{name}</b>", new GUIStyle(left) { fontSize = 17 }, on ? UiKit.Gold : UiKit.Dim);
            UiKit.Text(new Rect(m.x + 10, m.y + 28, m.width - 16, 22), sub, new GUIStyle(left) { fontSize = 13 }, on ? Color.white : UiKit.Dim);
        }
        string gun = player.overheated ? "<color=#FF7A5A>过热冷却中</color>" : player.overdriveT > 0f ? $"<color=#7FE8FF>超频 {player.overdriveT:0.0}s</color>" : d.heavy ? "爆炸炮弹 · 不耗能量" : "速射 · 会过热";
        string blade = d.heavy ? "三连拳，第三拳砸地" : "三连斩，第三斩突刺";
        Mode(new Rect(r.x + 20, r.y + 48, 200, 54), "射击", d.ranged, !player.mechBlade, gun);
        Mode(new Rect(r.x + 226, r.y + 48, 204, 54), "近战", d.melee, player.mechBlade, blade);

        var plate = new Rect(92, h - 116, 236, 76);
        UiKit.DrawPanel(plate);
        var medal = new Rect(10, h - 148, 132, 132);
        float ready = 1f - player.mechSpecialCd / d.specialCooldown;
        UiKit.DrawMedal(medal, UiKit.Art(d.heavy ? "skill_heavy" : "skill_light"), ready, new Color(d.color.r * 0.3f, d.color.g * 0.3f, d.color.b * 0.35f));
        if (ready < 1f) UiKit.Text(medal, $"{player.mechSpecialCd:0}", new GUIStyle(center) { fontSize = 34 }, Color.white);
        UiKit.Text(new Rect(plate.x + 56, plate.y + 6, 180, 34), d.special, new GUIStyle(left) { fontSize = 22 }, d.color);
        UiKit.Text(new Rect(plate.x + 56, plate.y + 38, 180, 26), $"<color=#FFD27A>F</color> · {(player.mechSpecialCd > 0f ? $"冷却 {player.mechSpecialCd:0.0}s" : "<color=#7FE8FF>就绪</color>")}", new GUIStyle(left) { fontSize = 15 });
    }

    void DrawSkill(float w, float h)
    {
        var s = player ? player.skill : null;
        if (s == null || s.def == null) return;
        float ready = s.def.cooldown <= 0f ? 1f : 1f - s.cooldownLeft / s.def.cooldown;
        var cc = ClassColor(s.def.id);
        var plate = new Rect(92, h - 116, 236, 76);
        UiKit.DrawPanel(plate);
        var medal = new Rect(10, h - 148, 132, 132);
        if (s.Active) UiKit.Halo(new Rect(medal.x + 20, medal.y + 20, medal.width - 40, medal.height - 40), new Color(1f, 0.85f, 0.35f, 0.9f), 18f);
        UiKit.DrawMedal(medal, UiKit.Art("skill_" + s.def.id), ready, new Color(cc.r * 0.3f, cc.g * 0.3f, cc.b * 0.35f));
        if (ready < 1f) UiKit.Text(medal, $"{s.cooldownLeft:0}", new GUIStyle(center) { fontSize = 34 }, Color.white);
        string state = s.Active ? $"<color=#FFD45A>生效中 {s.activeLeft:0.0}s</color>" : s.cooldownLeft > 0f ? $"冷却 {s.cooldownLeft:0.0}s" : "<color=#7FE8FF>就绪</color>";
        UiKit.Text(new Rect(plate.x + 56, plate.y + 6, 180, 34), s.def.skillName, new GUIStyle(left) { fontSize = 22 }, cc);
        UiKit.Text(new Rect(plate.x + 56, plate.y + 38, 180, 26), $"<color=#FFD27A>F</color> / 右键 · {state}", new GUIStyle(left) { fontSize = 15 });
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

    // Settings rows: the label on the left, the control from x + 270.
    void RowLabel(float y, float x, string label) =>
        UiKit.Text(new Rect(x, y, 260, 32), label, new GUIStyle(left) { fontSize = 19 }, UiKit.Paper);

    float SliderRow(float y, float x, string label, float value, float min, float max, string shown)
    {
        RowLabel(y, x, label);
        var r = new Rect(x + 270, y + 10, 250, 12);
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        var hit = new Rect(r.x - 10, r.y - 12, r.width + 20, r.height + 24);
        float At(float mx) => Mathf.Lerp(min, max, Mathf.InverseLerp(r.x, r.xMax, mx));
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && hit.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    value = At(e.mousePosition.x);
                    GUI.changed = true;
                    e.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    value = At(e.mousePosition.x);
                    GUI.changed = true;
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
            case EventType.Repaint:
                UiKit.DrawSlider(r, Mathf.InverseLerp(min, max, value), GUIUtility.hotControl == id || hit.Contains(e.mousePosition));
                break;
        }
        UiKit.Text(new Rect(x + 540, y, 120, 32), shown, new GUIStyle(left) { fontSize = 18 }, UiKit.Gold);
        return value;
    }

    bool ToggleRow(float y, float x, string label, bool value)
    {
        RowLabel(y, x, label);
        var r = new Rect(x + 270, y + 5, 58, 24);
        bool over = new Rect(r.x - 6, r.y - 4, r.width + 60, r.height + 8).Contains(Event.current.mousePosition);
        UiKit.DrawSwitch(r, value, over);
        UiKit.Text(new Rect(r.xMax + 12, y, 50, 32), value ? "开" : "关", new GUIStyle(left) { fontSize = 18 }, value ? new Color(0.6f, 1f, 0.8f) : UiKit.Dim);
        if (GUI.Button(new Rect(r.x - 6, r.y - 4, r.width + 60, r.height + 8), GUIContent.none, GUIStyle.none))
        {
            Sfx.Play("ui_click", 0.6f, 0.03f);
            return !value;
        }
        return value;
    }

    int ChoiceRow(float y, float x, string label, int value, string[] names)
    {
        RowLabel(y, x, label);
        bool Arrow(Rect r, bool right)
        {
            UiKit.DrawArrow(r, right, r.Contains(Event.current.mousePosition));
            if (!GUI.Button(r, GUIContent.none, GUIStyle.none)) return false;
            Sfx.Play("ui_click", 0.6f, 0.03f);
            return true;
        }
        if (Arrow(new Rect(x + 268, y + 3, 28, 28), false)) value = (value + names.Length - 1) % names.Length;
        UiKit.Text(new Rect(x + 300, y, 190, 32), names[value], new GUIStyle(center) { fontSize = 18 }, UiKit.Gold);
        if (Arrow(new Rect(x + 494, y + 3, 28, 28), true)) value = (value + 1) % names.Length;
        return value;
    }

    /// Comfort settings based on the motion-sickness checklist the user shared.
    void DrawSettings(float w, float h)
    {
        UiKit.Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.02f, 0.05f, 0.85f));
        var panel = new Rect(w / 2 - 370, 78, 740, h - 158);
        UiKit.DrawPanel(panel);
        UiKit.DrawBanner(new Rect(w / 2 - 300, 4, 600, 92), "设置 · 防晕 3D", new GUIStyle(title) { fontSize = 34 });
        // Sixteen rows plus the note; squeeze the rows on short windows.
        float x = w / 2 - 330, y = 104, row = Mathf.Clamp((panel.height - 120f) / 16f, 26f, 38f);
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
        GameSettings.music = SliderRow(y, x, "音乐音量", GameSettings.music, 0f, 1f, $"{GameSettings.music * 100f:0}%"); y += row;
        int win = ChoiceRow(y, x, "窗口大小", GameSettings.window, GameSettings.WindowNames);
        if (win != GameSettings.window) { GameSettings.window = win; GameSettings.ApplyWindow(); }
        y += row + 6;
        UiKit.Fill(new Rect(x, y, 660, 1.5f), new Color(0.85f, 0.65f, 0.3f, 0.5f));
        Label(new Rect(x, y + 6, 660, 64),
            "依据「解决晕 3D」：降延迟（关垂直同步、帧率 ≥ 80）、不用鼠标平滑、画面别太大（建议窗口宽度不超过观看距离的一半）、关运动模糊和景深、视野调大、减少闪烁和抖动。",
            new GUIStyle(small) { fontSize = 15, wordWrap = true });
        if (GUI.changed) SettingsChanged?.Invoke();
        if (Btn(new Rect(w / 2 - 110, h - 70, 220, 50), "返回", button) || Input.GetKeyDown(KeyCode.Escape))
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
        // Taller than its text so the plate's carved bottom edge stays clear of the last line.
        var r = new Rect(w - 470, h - 152, 450, 128);
        UiKit.DrawPanel(r);
        var rc = Weapons.RarityColors[cur.rarity];
        var icon = IconStudio.I ? IconStudio.I.Weapon(cur.id) : null;
        UiKit.Thumb(new Rect(r.x + 20, r.y + 20, 136, 88), icon, rc, ScaleMode.ScaleToFit);
        float tx = r.x + 170;
        string tag = cur.chargeable || cur.kind == WeaponKind.Charge ? "  <size=14><color=#FFD27A>可蓄力</color></size>" : "";
        UiKit.Text(new Rect(tx, r.y + 12, 260, 34), $"{cur.Title}{tag}", new GUIStyle(left) { fontSize = 26 }, rc);
        UiKit.Text(new Rect(tx, r.y + 46, 260, 20), $"{cur.type} · {Weapons.RarityNames[cur.rarity]}", new GUIStyle(left) { fontSize = 14 }, UiKit.Dim);
        UiKit.Text(new Rect(tx, r.y + 66, 260, 20), cur.desc, new GUIStyle(left) { fontSize = 14, clipping = TextClipping.Clip });
        string cost = cur.energyPerSec > 0f ? $"每秒 {cur.energyPerSec:0} 能量" : cur.energy > 0 ? $"每次 {cur.energy} 能量" : "不耗能量";
        UiKit.Text(new Rect(tx, r.y + 86, 260, 20), $"伤害 {cur.damage} · {cost}", new GUIStyle(left) { fontSize = 14 }, UiKit.Dim);
        if (other != null)
        {
            var q = new Rect(w - 320, h - 200, 300, 42);
            UiKit.DrawPanel(q);
            var qi = IconStudio.I ? IconStudio.I.Weapon(other.id) : null;
            if (qi) GUI.DrawTexture(new Rect(q.x + 10, q.y + 4, 56, 34), qi, ScaleMode.ScaleToFit);
            UiKit.Text(new Rect(q.x + 72, q.y, 220, 42), $"<color=#FFD27A>Q</color>  {other.name}  <size=13>{other.type}</size>",
                new GUIStyle(left) { fontSize = 17 }, Weapons.RarityColors[other.rarity]);
        }
    }

    /// Top left: class emblem, segmented health and armour, energy; then a strip with floor, kills, gold.
    /// Top left: the hero's portrait in a gold medallion, health / armour / energy with their
    /// painted icons on a stone plate; below, floor, kills and gold.
    void DrawStatus()
    {
        float y0 = 14f;
        if (player.InMech)
        {
            // The mech's durability is spent first, so it sits on top.
            var mr = new Rect(70, y0, 300, 42);
            UiKit.DrawPanel(mr);
            UiKit.Text(new Rect(mr.x + 18, mr.y, 110, 42), $"机甲 · {player.mechDef.name}", new GUIStyle(left) { fontSize = 16 }, player.mechDef.color);
            UiKit.Bar(new Rect(mr.x + 120, mr.y + 13, 120, 16), player.mechHp, player.mechDef.maxHp, player.mechDef.color * 0.85f);
            UiKit.Text(new Rect(mr.x + 246, mr.y, 60, 42), $"{player.mechHp}/{player.mechDef.maxHp}", new GUIStyle(left) { fontSize = 15 });
            y0 += 46f;
        }
        var cls = Classes.Get(RunState.classId);
        var cc = ClassColor(cls.id);
        var r = new Rect(70, y0 + 6, 300, 112);
        UiKit.DrawPanel(r);
        var medal = new Rect(8, y0, 124, 124);
        var portrait = IconStudio.I ? IconStudio.I.ClassView(cls.id) : null;
        UiKit.DrawMedal(medal, portrait, 1f, new Color(cc.r * 0.35f, cc.g * 0.35f, cc.b * 0.4f), new Rect(0.14f, 0.42f, 0.72f, 0.58f));
        float bx = r.x + 90, bw = 140;
        var lab = new GUIStyle(left) { fontSize = 15 };
        void Row(float yy, string glyph, float v, float max, Color c, int seg, string shown)
        {
            UiKit.Icon(new Rect(bx - 30, yy - 3, 26, 26), glyph);
            UiKit.Bar(new Rect(bx, yy, bw, 20), v, max, c, seg);
            UiKit.Text(new Rect(bx + bw + 8, yy - 4, 60, 28), shown, lab);
        }
        Row(r.y + 16, "glyph_heart", player.hp, player.maxHp, new Color(0.86f, 0.18f, 0.22f), player.maxHp <= 14 ? player.maxHp : 0, $"{player.hp}/{player.maxHp}");
        Row(r.y + 46, "glyph_armor", player.armor, player.maxArmor, new Color(0.66f, 0.72f, 0.84f), player.maxArmor <= 14 ? player.maxArmor : 0, $"{player.armor}/{player.maxArmor}");
        Row(r.y + 76, "glyph_energy", player.energy, player.maxEnergy, new Color(0.25f, 0.6f, 1f), 0, $"{player.energy}");
        // Floor · kills · gold, and how many are left in the room.
        var strip = new Rect(70, r.yMax + 6, 300, 38);
        UiKit.DrawPanel(strip);
        var ss = new GUIStyle(left) { fontSize = 15 };
        UiKit.Text(new Rect(strip.x + 18, strip.y, 150, 38), $"第 {RunState.floor}/{RunState.Floors} 层 · {themeName}", ss);
        UiKit.Icon(new Rect(strip.x + 170, strip.y + 8, 22, 22), "glyph_skull");
        UiKit.Text(new Rect(strip.x + 194, strip.y, 40, 38), $"{RunState.kills}", ss);
        UiKit.Icon(new Rect(strip.x + 230, strip.y + 8, 22, 22), "glyph_gold");
        UiKit.Text(new Rect(strip.x + 254, strip.y, 50, 38), $"{RunState.gold}", ss, new Color(1f, 0.85f, 0.35f));
        if (enemiesLeft >= 0)
        {
            var er = new Rect(70, strip.yMax + 6, 170, 36);
            UiKit.DrawPanel(er);
            UiKit.Text(new Rect(er.x + 18, er.y, 160, 36), $"<color=#FF7A6A>剩余敌人 {enemiesLeft}</color>", ss);
        }
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

    /// The hit streak on the right: count and damage bonus, fading as the 2 s window runs out.
    void DrawCombo(float w, float h)
    {
        if (screen != Screen.None || Combo.count < 3 || !Combo.Alive) return;
        float a = Mathf.Clamp01((2f - (Time.time - Combo.lastHit)) * 1.5f);
        var big = new GUIStyle(left) { fontSize = 34, alignment = TextAnchor.MiddleRight };
        UiKit.Text(new Rect(w - 360, h * 0.42f, 320, 44), $"连击 ×{Combo.count}", big, new Color(1f, 0.82f, 0.3f, a));
        UiKit.Text(new Rect(w - 360, h * 0.42f + 40, 320, 26), $"伤害 +{Combo.Bonus * 100f:0}%", new GUIStyle(left) { fontSize = 18, alignment = TextAnchor.MiddleRight }, new Color(1f, 1f, 1f, 0.85f * a));
    }

    /// Names over weapons lying on the floor nearby, in their rarity colour.
    void DrawLootTags()
    {
        var cam = Camera.main;
        if (!cam || !player || screen != Screen.None) return;
        var st = new GUIStyle(center) { fontSize = 16 };
        foreach (var p in Pickup.Live)
        {
            if (p.kind != PickupKind.Weapon || p.weapon == null) continue;
            if (Vector3.Distance(p.transform.position, player.transform.position) > 12f) continue;
            // White text just above the floating weapon (the beam behind is already the rarity colour).
            Vector3 sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 1.15f);
            if (sp.z < 0.5f) continue;
            UiKit.Text(new Rect(sp.x - 80, UnityEngine.Screen.height - sp.y - 12, 160, 24),
                $"<color=#{ColorUtility.ToHtmlStringRGB(Weapons.RarityColors[p.weapon.rarity])}>●</color> {p.weapon.name}", st, Color.white);
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
            // Outlined so the affix colour reads on any floor (blue on the frost floor was lost).
            UiKit.Text(new Rect(x - 110, y - 50, 220, 22), e.EliteName, tag, Color.Lerp(e.EliteColor, Color.white, 0.25f));
            UiKit.Text(new Rect(x - 110, y - 30, 220, 20), e.KindName, new GUIStyle(tag) { fontSize = 14 }, Color.white);
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
            // Crits pop in big and settle; everything fades out as it rises.
            float pop = n.crit ? 1f + 0.5f * Mathf.Clamp01(1f - n.t / 0.15f) : 1f;
            var st = new GUIStyle(number) { fontSize = Mathf.RoundToInt((n.crit ? 36 : 26) * pop) };
            var baseCol = n.color ?? (n.crit ? new Color(1f, 0.82f, 0.25f) : Color.white);
            UiKit.Text(new Rect(sp.x - 90, UnityEngine.Screen.height - sp.y - 24, 180, 48), n.text, st, new Color(baseCol.r, baseCol.g, baseCol.b, 1f - n.t / 0.8f));
        }
    }

    void DrawBoss(float w)
    {
        var b = Enemy.ActiveBoss;
        if (!b || b.dead) return;
        var panel = new Rect(w / 2 - 330, 14, 660, 64);
        UiKit.DrawPanel(panel);
        string state = b.Stunned ? "  <color=#7FE8FF>眩晕 · 伤害翻倍</color>" : b.Enraged ? "  <color=#FF6A5A>狂暴</color>" : "";
        UiKit.Text(new Rect(panel.x, panel.y + 4, panel.width, 28), b.displayName + state, new GUIStyle(center) { fontSize = 20 }, new Color(1f, 0.55f, 0.5f));
        UiKit.Bar(new Rect(panel.x + 20, panel.y + 36, panel.width - 40, 16), b.hp, b.maxHp, b.Enraged ? new Color(1f, 0.3f, 0.15f) : new Color(0.86f, 0.15f, 0.2f));
    }

    void DrawBuffs(float w, float h)
    {
        UiKit.Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.02f, 0.05f, 0.65f));
        UiKit.DrawBanner(new Rect(w / 2 - 340, h * 0.04f, 680, 104), $"第 {RunState.floor} 层 · 选择一份祝福", new GUIStyle(title) { fontSize = 36 });
        float ch = Mathf.Min(h * 0.6f, 520f), cw = ch / 1.5f, gap = 44f;
        float x0 = w / 2 - (cw * offer.Count + gap * (offer.Count - 1)) / 2, y = h * 0.2f;
        var mouse = Event.current.mousePosition;
        for (int i = 0; i < offer.Count; i++)
        {
            var b = offer[i];
            var slot = new Rect(x0 + i * (cw + gap), y, cw, ch);
            bool over = slot.Contains(mouse);
            var r = over ? new Rect(slot.x - cw * 0.03f, slot.y - 12f, cw * 1.06f, ch * 1.06f) : slot;
            if (over) UiKit.Halo(r, new Color(0.35f, 1f, 0.75f, 0.85f), 22f);
            var win = UiKit.Sub(r, UiKit.Layout("card_buff", new Rect(0.15f, 0.13f, 0.7f, 0.42f)));
            var pic = UiKit.Art("buff_" + b.id);
            if (pic) GUI.DrawTexture(win, pic, ScaleMode.ScaleAndCrop, true);
            else UiKit.Fill(win, new Color(0.1f, 0.12f, 0.14f));
            var frame = UiKit.Art("card_buff");
            if (frame) GUI.DrawTexture(r, frame, ScaleMode.StretchToFill, true);
            else GUI.Box(r, GUIContent.none, UiKit.Card);
            var text = UiKit.Sub(r, UiKit.Layout("card_buff_text", new Rect(0.13f, 0.65f, 0.74f, 0.29f)));
            float ribbonY = (win.yMax + text.y) / 2f;
            UiKit.Text(new Rect(r.x, ribbonY - cw * 0.07f, r.width, cw * 0.13f), b.name, new GUIStyle(center) { fontSize = Mathf.RoundToInt(cw * 0.1f) }, UiKit.Paper);
            var body = new Rect(text.x + text.width * 0.08f, text.y + text.height * 0.12f, text.width * 0.84f, text.height * 0.76f);
            var rule = new GUIStyle(center) { fontSize = Mathf.RoundToInt(cw * 0.055f), fontStyle = FontStyle.Normal, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            rule.normal.textColor = InkColor;
            GUI.Label(body, b.desc, rule);
            if (b.stackable)
            {
                var fine = new GUIStyle(rule) { fontSize = Mathf.RoundToInt(cw * 0.045f), alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Italic };
                fine.normal.textColor = new Color(0.4f, 0.28f, 0.16f);
                GUI.Label(new Rect(body.x, body.y, body.width, body.height + text.height * 0.08f), "可叠加", fine);
            }
            var kr = new Rect(r.x + r.width * 0.06f, ribbonY - cw * 0.05f, cw * 0.1f, cw * 0.1f);
            GUI.DrawTexture(kr, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.06f, 0.05f, 0.04f, 0.9f).linear, 0f, kr.width / 2f);
            GUI.DrawTexture(kr, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, UiKit.Gold.linear, 2f, kr.width / 2f);
            UiKit.Text(kr, $"{i + 1}", new GUIStyle(center) { fontSize = Mathf.RoundToInt(kr.width * 0.6f) }, UiKit.Gold);
            if (Event.current.type == EventType.MouseDown && over)
            {
                Sfx.Play("ui_click", 0.7f, 0.02f);
                PickBuff(i);
                Event.current.Use();
                return;
            }
        }
        UiKit.Text(new Rect(0, y + ch + 24, w, 30), "点击卡牌或按 1 / 2 / 3", small);
    }

    void DrawMenu(float w, float h, string heading, string primary, System.Action primaryAction)
    {
        UiKit.Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.02f, 0.05f, 0.62f));
        bool over = screen == Screen.Dead || screen == Screen.Won;
        float top = over ? h * 0.10f : h * 0.22f;
        UiKit.DrawBanner(new Rect(w / 2 - 330, top - 14, 660, 104), heading, new GUIStyle(title) { fontSize = 38 });
        float y = h * 0.48f;
        if (over) y = DrawSummary(w, top + 90f) + 20f;
        else if (player)
        {
            // What you are carrying: where you are, kills and gold; both weapons (with upgrades) and
            // the blessings so far as pictures.
            var pr = new Rect(w / 2 - 330, top + 84, 660, 172);
            UiKit.DrawPanel(pr);
            var ss = new GUIStyle(left) { fontSize = 17 };
            UiKit.Text(new Rect(pr.x + 30, pr.y + 16, 300, 28), $"第 {RunState.floor}/{RunState.Floors} 层 · {themeName}", ss, UiKit.Paper);
            UiKit.Icon(new Rect(pr.x + 400, pr.y + 18, 24, 24), "glyph_skull");
            UiKit.Text(new Rect(pr.x + 428, pr.y + 16, 60, 28), $"{RunState.kills}", ss, UiKit.Paper);
            UiKit.Icon(new Rect(pr.x + 490, pr.y + 18, 24, 24), "glyph_gold");
            UiKit.Text(new Rect(pr.x + 518, pr.y + 16, 80, 28), $"{RunState.gold}", ss, new Color(1f, 0.85f, 0.35f));
            var head = new GUIStyle(left) { fontSize = 14 };
            UiKit.Text(new Rect(pr.x + 30, pr.y + 46, 100, 20), "武器", head, UiKit.Gold);
            WeaponRow(pr.x + 30, pr.y + 68, 190, new List<WeaponDef> { player.weapons[0], player.weapons[1] });
            UiKit.Text(new Rect(pr.x + 240, pr.y + 46, 100, 20), "祝福", head, UiKit.Gold);
            BlessingRow(pr.x + 240, pr.y + 68, pr.width - 270, RunState.buffs);
            y = Mathf.Max(y, pr.yMax + 24);
        }
        if (Btn(new Rect(w / 2 - 140, y, 280, 56), primary, button)) primaryAction?.Invoke();
        if (screen == Screen.Pause && Btn(new Rect(w / 2 - 140, y + 70, 280, 56), "重新开始", button)) Restart?.Invoke();
        if (screen == Screen.Pause && Btn(new Rect(w / 2 - 140, y + 140, 280, 56), "设置 · 防晕 3D", button)) OpenSettings();
        if (Btn(new Rect(w / 2 - 140, y + (screen == Screen.Pause ? 210 : 70), 280, 56), "退出游戏", button)) Quit?.Invoke();
    }

    /// End-of-run card: time, progress, combat numbers, the weapons you carried and the buffs you chose.
    float DrawSummary(float w, float y)
    {
        var panel = new Rect(w / 2 - 390, y, 780, 408);
        UiKit.DrawPanel(panel);
        var cls = Classes.Get(RunState.classId);
        var cc = ClassColor(cls.id);
        // The hero in a medallion with the name under it.
        var medal = new Rect(panel.x + 26, y + 20, 124, 124);
        UiKit.DrawMedal(medal, IconStudio.I ? IconStudio.I.ClassView(cls.id) : null, 1f, new Color(cc.r * 0.35f, cc.g * 0.35f, cc.b * 0.4f), new Rect(0.14f, 0.42f, 0.72f, 0.58f));
        UiKit.Text(new Rect(medal.x - 10, medal.yMax - 4, medal.width + 20, 24), cls.name, new GUIStyle(center) { fontSize = 17 }, cc);
        // Ten numbers on two rows of tiles.
        int secs = Mathf.FloorToInt(RunState.runTime);
        var stats = new[]
        {
            ("用时", $"{secs / 60}:{secs % 60:00}"), ("到达", $"第 {RunState.floor} 层"), ("击杀", $"{RunState.kills}"),
            ("首领", $"{RunState.bossesKilled}"), ("金币", $"{RunState.gold}"),
            ("造成伤害", $"{RunState.damageDealt}"), ("受到伤害", $"{RunState.damageTaken}"), ("最高连击", $"{RunState.bestCombo}"),
            ("完美闪避", $"{RunState.perfectDodges}"), ("驾驶机甲", $"{RunState.mechsPiloted} 次"),
        };
        float gx = panel.x + 176, gw = panel.xMax - 30 - gx, tw = (gw - 4 * 8f) / 5f, th = 58f;
        var value = new GUIStyle(center) { fontSize = 22 };
        var label = new GUIStyle(center) { fontSize = 13, fontStyle = FontStyle.Normal };
        for (int i = 0; i < stats.Length; i++)
        {
            var t = new Rect(gx + (i % 5) * (tw + 8f), y + 24 + (i / 5) * (th + 8f), tw, th);
            UiKit.Inset(t, new Color(0.85f, 0.65f, 0.3f, 0.55f));
            UiKit.Text(new Rect(t.x, t.y + 4, t.width, 30), stats[i].Item2, value, UiKit.Gold);
            UiKit.Text(new Rect(t.x, t.y + 34, t.width, 20), stats[i].Item1, label, UiKit.Dim);
        }
        // What you fought with and what you were blessed with, as pictures.
        var head = new GUIStyle(left) { fontSize = 15 };
        float ry = y + 168;
        UiKit.Text(new Rect(panel.x + 30, ry, 200, 22), "用过的武器", head, UiKit.Gold);
        var seen = new List<WeaponDef>();
        foreach (var id in RunState.weaponsSeen) seen.Add(Weapons.Get(id));
        WeaponRow(panel.x + 30, ry + 26, panel.width - 60, seen);
        ry += 26 + 72 + 10;
        UiKit.Text(new Rect(panel.x + 30, ry, 200, 22), "选过的祝福", head, UiKit.Gold);
        BlessingRow(panel.x + 30, ry + 26, panel.width - 60, RunState.buffs);
        return panel.yMax;
    }

    /// Weapon thumbnails (icon in a rim of its rarity colour, name under it), left to right.
    void WeaponRow(float x, float y, float maxW, List<WeaponDef> list)
    {
        const float tw = 82f, th = 52f, gap = 10f;
        var name = new GUIStyle(center) { fontSize = 12, fontStyle = FontStyle.Normal, clipping = TextClipping.Clip };
        float cx = x;
        int shown = 0, total = 0;
        foreach (var d in list)
        {
            if (d == null) continue;
            total++;
            if (cx + tw > x + maxW) continue;
            var rc = Weapons.RarityColors[d.rarity];
            UiKit.Thumb(new Rect(cx, y, tw, th), IconStudio.I ? IconStudio.I.Weapon(d.id) : null, rc, ScaleMode.ScaleToFit);
            UiKit.Text(new Rect(cx - 6, y + th + 1, tw + 12, 18), d.Title, name, rc);
            cx += tw + gap;
            shown++;
        }
        if (total == 0) UiKit.Text(new Rect(x, y, 100, th), "—", name, UiKit.Dim);
        else if (shown < total) UiKit.Text(new Rect(cx - gap - tw, y, tw, th), $"+{total - shown}", new GUIStyle(center) { fontSize = 20 }, UiKit.Paper);
    }

    /// Blessing pictures with their names; a blessing taken several times shows once with a count.
    void BlessingRow(float x, float y, float maxW, List<string> ids)
    {
        var picked = new List<BuffDef>();
        var count = new Dictionary<BuffDef, int>();
        foreach (var id in ids)
        {
            var b = Buffs.All.Find(d => d.id == id);
            if (b == null) continue;
            if (count.ContainsKey(b)) count[b]++;
            else { count[b] = 1; picked.Add(b); }
        }
        const float s = 50f, gap = 16f;
        var name = new GUIStyle(center) { fontSize = 12, fontStyle = FontStyle.Normal, clipping = TextClipping.Clip };
        if (picked.Count == 0) { UiKit.Text(new Rect(x, y, 100, s), "—", name, UiKit.Dim); return; }
        float cx = x;
        foreach (var b in picked)
        {
            if (cx + s > x + maxW) break;
            UiKit.Thumb(new Rect(cx, y, s, s), UiKit.Art("buff_" + b.id), new Color(0.4f, 0.9f, 0.75f), ScaleMode.ScaleAndCrop);
            if (count[b] > 1) UiKit.Text(new Rect(cx + s - 30, y + s - 22, 32, 22), $"×{count[b]}", new GUIStyle(center) { fontSize = 15 }, UiKit.Gold);
            UiKit.Text(new Rect(cx - 8, y + s + 1, s + 16, 18), b.name, name, UiKit.Paper);
            cx += s + gap;
        }
    }

    /// Visited rooms in full, their neighbours dimmed, the player as a dot (Soul Knight style).
    void DrawMinimap(float screenW)
    {
        if (!dungeon || !player) return;
        const float scale = 1.9f; // pixels per metre: the room you are in and its neighbours fill the map
        const float size = 240f;
        var frame = new Rect(screenW - size - 28, 12, size + 16, size + 44);
        UiKit.DrawPanel(frame);
        UiKit.Text(new Rect(frame.x, frame.y + 8, frame.width, 26), themeName, new GUIStyle(center) { fontSize = 15 }, UiKit.Gold);
        var panel = new Rect(screenW - size - 20, 40, size, size);
        UiKit.Rect(panel, new Color(0f, 0f, 0f, 0.35f));
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
            if (r.width > 0 && r.height > 0) UiKit.Fill(r, new Color(0.55f, 0.52f, 0.5f, 0.55f));
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
                Dungeon.Kind.Altar => new Color(0.95f, 0.35f, 0.35f),
                Dungeon.Kind.Trial => new Color(1f, 0.6f, 0.25f),
                Dungeon.Kind.End => new Color(0.7f, 0.5f, 1f),
                Dungeon.Kind.Boss => new Color(1f, 0.35f, 0.3f),
                _ => new Color(0.75f, 0.75f, 0.85f),
            };
            if (!room.visited) col.a = 0.35f;
            if (room == dungeon.current) col = Color.Lerp(col, Color.white, 0.4f);
            // A rounded tile with a dark edge; the room you are in gets a gold rim.
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, (col * 0.8f).linear, 0f, 5f);
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, col.linear, 0f, 4f);
            if (room == dungeon.current)
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, UiKit.Gold.linear, 2f, 5f);
            // An icon on the rooms that matter: boss, chest, shop.
            string icon = room.kind switch
            {
                Dungeon.Kind.Boss => "<color=#FF5A4A>首</color>",
                Dungeon.Kind.Chest => "<color=#7A5A10>宝</color>",
                Dungeon.Kind.Shop => "<color=#0E5A48>商</color>",
                Dungeon.Kind.Altar => "<color=#5A0E0E>祭</color>",
                Dungeon.Kind.Trial => "<color=#5A2E08>试</color>",
                _ => null,
            };
            if (icon != null) Label(new Rect(r.center.x - 15, r.center.y - 14, 30, 28), icon, new GUIStyle(center) { fontSize = 20, richText = true });
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
        // You: an arrow pointing where the camera looks.
        float yaw = player.cam ? player.cam.transform.eulerAngles.y : player.transform.eulerAngles.y;
        var you = new Rect(panel.center.x - 9, panel.center.y - 9, 18, 18);
        var keep = GUI.matrix;
        GUIUtility.RotateAroundPivot(yaw - 90f, panel.center);
        UiKit.DrawArrow(you, true, false);
        GUI.matrix = keep;
    }
}
