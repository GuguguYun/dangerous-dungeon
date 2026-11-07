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
    static bool Btn(Rect r, string text, GUIStyle style)
    {
        if (!GUI.Button(r, text, style)) return false;
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
                var pr = new Rect(20, h - 158, 280, 30);
                GUI.Box(pr, GUIContent.none, UiKit.Pill);
                UiKit.Text(new Rect(pr.x + 14, pr.y, 90, 30), "无人机", new GUIStyle(left) { fontSize = 15 });
                UiKit.Bar(new Rect(pr.x + 78, pr.y + 9, 140, 12), pet.hp, pet.maxHp, pet.offline ? new Color(0.4f, 0.4f, 0.45f) : new Color(0.3f, 0.8f, 0.95f));
                UiKit.Text(new Rect(pr.x + 224, pr.y, 60, 30), pet.offline ? "重启中" : $"{pet.hp}/{pet.maxHp}", new GUIStyle(left) { fontSize = 14 });
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

    void DrawTitle(float w, float h)
    {
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

    void DrawClassSelect(float w, float h)
    {
        UiKit.Vignette(w, h);
        UiKit.Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.02f, 0.05f, 0.45f));
        UiKit.Text(new Rect(0, h * 0.04f, w, 70), "选择你的英雄", new GUIStyle(title) { fontSize = 48 }, UiKit.Gold);
        int n = Classes.All.Count;
        float gap = 18f, cw = Mathf.Min(340f, (w - 60f - gap * (n - 1)) / n), ch = Mathf.Min(h * 0.8f, 700f);
        float x0 = w / 2 - (cw * n + gap * (n - 1)) / 2, y = h * 0.13f;
        var mouse = Event.current.mousePosition;
        string hover = null;
        for (int i = 0; i < n; i++)
        {
            var c = Classes.All[i];
            var r = new Rect(x0 + i * (cw + gap), y, cw, ch);
            bool over = r.Contains(mouse);
            if (over) hover = c.id;
            var cc = ClassColor(c.id);
            GUI.Box(r, GUIContent.none, over ? UiKit.CardHover : UiKit.Card);
            // Live 3D preview of the hero, turning slowly.
            var view = IconStudio.I ? IconStudio.I.ClassView(c.id) : null;
            var vr = new Rect(r.x + 10, r.y + 10, r.width - 20, ch * 0.42f);
            UiKit.Fill(vr, new Color(cc.r * 0.18f, cc.g * 0.18f, cc.b * 0.22f, 0.9f));
            if (view) GUI.DrawTexture(vr, view, ScaleMode.ScaleToFit, true);
            UiKit.Text(new Rect(r.x + 16, vr.y + 6, 60, 28), $"{i + 1}", new GUIStyle(left) { fontSize = 20 }, UiKit.Gold);
            float ty = vr.yMax + 8;
            UiKit.Text(new Rect(r.x, ty, r.width, 40), c.name, new GUIStyle(center) { fontSize = 30 }, cc);
            UiKit.Text(new Rect(r.x, ty + 38, r.width, 24), c.desc, new GUIStyle(small) { fontSize = 16 });
            ty += 70;
            // Stats as short bars.
            float sx = r.x + 18, sw = r.width - 36;
            void Stat(string name, float v, float max, string shown, Color col)
            {
                UiKit.Text(new Rect(sx, ty, 60, 20), name, new GUIStyle(left) { fontSize = 14 }, UiKit.Dim);
                UiKit.Bar(new Rect(sx + 52, ty + 4, sw - 100, 12), v, max, col);
                UiKit.Text(new Rect(sx + sw - 44, ty, 50, 20), shown, new GUIStyle(left) { fontSize = 14 });
                ty += 21;
            }
            Stat("生命", c.maxHp, 8, $"{c.maxHp}", new Color(0.86f, 0.2f, 0.25f));
            Stat("护甲", c.maxArmor, 6, $"{c.maxArmor}", new Color(0.62f, 0.68f, 0.8f));
            Stat("能量", c.maxEnergy, 260, $"{c.maxEnergy}", new Color(0.25f, 0.55f, 1f));
            Stat("闪避", c.stamina, 5, $"{c.stamina}", new Color(0.45f, 0.95f, 1f));
            Stat("移速", c.speed, 7f, $"{c.speed:0.0}", new Color(0.6f, 0.9f, 0.5f));
            Stat("暴击", c.crit, 0.2f, $"{c.crit * 100f:0}%", new Color(1f, 0.8f, 0.3f));
            ty += 4;
            UiKit.Text(new Rect(sx, ty, sw, 40), $"<color=#F7CC75>特性</color>  {c.trait}", new GUIStyle(left) { fontSize = 14, wordWrap = true, alignment = TextAnchor.UpperLeft });
            ty += 40;
            UiKit.Text(new Rect(sx, ty, sw, 24), $"<color=#F7CC75>技能</color>  {c.skillName}（冷却 {c.cooldown:0} 秒）", new GUIStyle(left) { fontSize = 15 }, cc);
            UiKit.Text(new Rect(sx, ty + 24, sw, 58), c.skillDesc, new GUIStyle(left) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft }, UiKit.Dim);
            ty += 84;
            // Starting weapons with their icons.
            float wx = sx;
            foreach (var id in c.weapons)
            {
                var wd = Weapons.Get(id);
                var ic = IconStudio.I ? IconStudio.I.Weapon(id) : null;
                if (ic) GUI.DrawTexture(new Rect(wx, ty, 54, 32), ic, ScaleMode.ScaleToFit);
                UiKit.Text(new Rect(wx + 56, ty, 90, 32), wd.name, new GUIStyle(left) { fontSize = 14 }, Weapons.RarityColors[wd.rarity]);
                wx += sw / 2;
            }
            if (Event.current.type == EventType.MouseDown && over)
            {
                Sfx.Play("ui_click", 0.7f, 0.02f);
                ChooseClass?.Invoke(c.id);
                Event.current.Use();
            }
        }
        if (IconStudio.I) IconStudio.I.hovered = hover;
        UiKit.Text(new Rect(0, y + ch + 10, w, 30), "点击卡片或按 1 – 4 选择", small);
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
        var r = new Rect(w - 470, h - 142, 450, 112);
        UiKit.DrawPanel(r);
        UiKit.Fill(new Rect(r.x + 10, r.y + 12, 4, r.height - 24), d.color);
        var nameStyle = new GUIStyle(left) { fontSize = 28, richText = true };
        nameStyle.normal.textColor = d.color;
        Label(new Rect(r.x + 24, r.y + 8, 360, 36), $"{d.name}  <size=16>{(d.heavy ? "重装机甲" : "轻装机甲")}</size>", nameStyle);
        Label(new Rect(r.x + 24, r.y + 46, 360, 26), $"左键 {d.ranged} · 右键 {d.melee}", new GUIStyle(left) { fontSize = 16 });
        string gun = player.overheated ? "<color=#FF7A5A>过热冷却中</color>" : player.overdriveT > 0f ? $"<color=#7FE8FF>超频 {player.overdriveT:0.0}s</color>" : "不耗能量";
        Label(new Rect(r.x + 24, r.y + 74, 360, 26), gun, new GUIStyle(left) { fontSize = 16, richText = true });

        var sr = new Rect(20, h - 120, 280, 70);
        UiKit.DrawPanel(sr);
        float ready = 1f - player.mechSpecialCd / d.specialCooldown;
        UiKit.Bar(new Rect(sr.x + 14, sr.yMax - 16, sr.width - 28, 8), ready, 1f, ready >= 1f ? d.color : new Color(0.5f, 0.5f, 0.6f));
        Label(new Rect(sr.x + 14, sr.y + 4, sr.width - 20, 30), $"{d.special}  <size=15>F</size>", new GUIStyle(left) { fontSize = 22, richText = true });
        Label(new Rect(sr.x + 14, sr.y + 32, sr.width - 20, 24), player.mechSpecialCd > 0f ? $"冷却 {player.mechSpecialCd:0.0}s" : "就绪", new GUIStyle(left) { fontSize = 16 });
    }

    void DrawSkill(float w, float h)
    {
        var s = player ? player.skill : null;
        if (s == null || s.def == null) return;
        var r = new Rect(20, h - 120, 280, 90);
        UiKit.DrawPanel(r);
        float ready = s.def.cooldown <= 0f ? 1f : 1f - s.cooldownLeft / s.def.cooldown;
        var cc = ClassColor(s.def.id);
        // A key badge that fills up as the skill comes back.
        var key = new Rect(r.x + 12, r.y + 14, 62, 62);
        GUI.DrawTexture(key, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, (ready >= 1f ? cc * 0.6f : new Color(0.2f, 0.2f, 0.25f)).linear, 0f, 12f);
        if (ready < 1f) UiKit.Fill(new Rect(key.x, key.yMax - key.height * ready, key.width, key.height * ready), cc * 0.45f);
        GUI.DrawTexture(key, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, (s.Active ? new Color(1f, 0.85f, 0.3f) : UiKit.Gold).linear, 2f, 12f);
        UiKit.Text(key, ready >= 1f ? "F" : $"{s.cooldownLeft:0}", new GUIStyle(center) { fontSize = 26 });
        string state = s.Active ? $"<color=#FFD45A>生效中 {s.activeLeft:0.0}s</color>" : s.cooldownLeft > 0f ? $"冷却 {s.cooldownLeft:0.0}s" : "<color=#7FE8FF>就绪</color>";
        UiKit.Text(new Rect(r.x + 86, r.y + 10, 190, 34), s.def.skillName, new GUIStyle(left) { fontSize = 24 }, cc);
        UiKit.Text(new Rect(r.x + 86, r.y + 44, 190, 22), $"右键 / F · {state}", new GUIStyle(left) { fontSize = 15 });
        UiKit.Text(new Rect(r.x + 86, r.y + 64, 190, 20), $"冷却 {s.def.cooldown:0} 秒", new GUIStyle(left) { fontSize = 13 }, UiKit.Dim);
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
        if (Btn(new Rect(x + 250, y + 2, 110, 28), value ? "开" : "关", button)) return !value;
        return value;
    }

    int ChoiceRow(float y, float x, string label, int value, string[] names)
    {
        Label(new Rect(x, y, 240, 32), label, new GUIStyle(left) { fontSize = 19 });
        if (Btn(new Rect(x + 250, y + 2, 34, 28), "<", button)) value = (value + names.Length - 1) % names.Length;
        Label(new Rect(x + 290, y, 190, 32), names[value], new GUIStyle(center) { fontSize = 18 });
        if (Btn(new Rect(x + 486, y + 2, 34, 28), ">", button)) value = (value + 1) % names.Length;
        return value;
    }

    /// Comfort settings based on the motion-sickness checklist the user shared.
    void DrawSettings(float w, float h)
    {
        UiKit.Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.02f, 0.05f, 0.85f));
        UiKit.DrawPanel(new Rect(w / 2 - 370, 70, 740, h - 150));
        UiKit.Text(new Rect(0, 18, w, 56), "设置 · 防晕 3D", new GUIStyle(title) { fontSize = 40 }, UiKit.Gold);
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
        GameSettings.music = SliderRow(y, x, "音乐音量", GameSettings.music, 0f, 1f, $"{GameSettings.music * 100f:0}%"); y += row;
        int win = ChoiceRow(y, x, "窗口大小", GameSettings.window, GameSettings.WindowNames);
        if (win != GameSettings.window) { GameSettings.window = win; GameSettings.ApplyWindow(); }
        y += row + 8;
        Label(new Rect(x - 40, y, 740, 64),
            "依据「解决晕 3D」：降延迟（关垂直同步、帧率 ≥ 80）、不用鼠标平滑、画面别太大（建议窗口宽度不超过观看距离的一半）、关运动模糊和景深、视野调大、减少闪烁和抖动。",
            new GUIStyle(small) { fontSize = 16, wordWrap = true });
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
        var r = new Rect(w - 470, h - 142, 450, 112);
        UiKit.DrawPanel(r);
        var rc = Weapons.RarityColors[cur.rarity];
        UiKit.Fill(new Rect(r.x + 10, r.y + 12, 4, r.height - 24), rc);
        var icon = IconStudio.I ? IconStudio.I.Weapon(cur.id) : null;
        if (icon) GUI.DrawTexture(new Rect(r.x + 18, r.y + 14, 140, 84), icon, ScaleMode.ScaleToFit);
        float tx = r.x + 168;
        string tag = cur.chargeable || cur.kind == WeaponKind.Charge ? "  <size=14><color=#FFD27A>可蓄力</color></size>" : "";
        UiKit.Text(new Rect(tx, r.y + 6, 270, 38), $"{cur.Title}{tag}", new GUIStyle(left) { fontSize = 28 }, rc);
        UiKit.Text(new Rect(tx, r.y + 42, 270, 22), $"{cur.type} · {Weapons.RarityNames[cur.rarity]}", new GUIStyle(left) { fontSize = 15 }, UiKit.Dim);
        UiKit.Text(new Rect(tx, r.y + 62, 274, 22), cur.desc, new GUIStyle(left) { fontSize = 14, clipping = TextClipping.Clip });
        string cost = cur.energyPerSec > 0f ? $"每秒 {cur.energyPerSec:0} 能量" : cur.energy > 0 ? $"每次 {cur.energy} 能量" : "不耗能量";
        UiKit.Text(new Rect(tx, r.y + 82, 270, 22), $"伤害 {cur.damage} · {cost}", new GUIStyle(left) { fontSize = 15 }, UiKit.Dim);
        if (other != null)
        {
            var q = new Rect(w - 320, h - 190, 300, 42);
            GUI.Box(q, GUIContent.none, UiKit.Pill);
            var qi = IconStudio.I ? IconStudio.I.Weapon(other.id) : null;
            if (qi) GUI.DrawTexture(new Rect(q.x + 10, q.y + 4, 56, 34), qi, ScaleMode.ScaleToFit);
            UiKit.Text(new Rect(q.x + 72, q.y, 220, 42), $"<color=#FFD27A>Q</color>  {other.name}  <size=13>{other.type}</size>",
                new GUIStyle(left) { fontSize = 17 }, Weapons.RarityColors[other.rarity]);
        }
    }

    /// Top left: class emblem, segmented health and armour, energy; then a strip with floor, kills, gold.
    void DrawStatus()
    {
        float y0 = 16f;
        if (player.InMech)
        {
            // The mech's durability is spent first, so it sits on top.
            var mr = new Rect(16, y0, 340, 38);
            UiKit.DrawPanel(mr);
            UiKit.Text(new Rect(mr.x + 14, mr.y, 110, 38), $"机甲 · {player.mechDef.name}", new GUIStyle(left) { fontSize = 16 }, player.mechDef.color);
            UiKit.Bar(new Rect(mr.x + 116, mr.y + 11, 156, 16), player.mechHp, player.mechDef.maxHp, player.mechDef.color * 0.85f);
            UiKit.Text(new Rect(mr.x + 280, mr.y, 60, 38), $"{player.mechHp}/{player.mechDef.maxHp}", new GUIStyle(left) { fontSize = 15 });
            y0 += 44f;
        }
        var r = new Rect(16, y0, 340, 108);
        UiKit.DrawPanel(r);
        // Class emblem: a coloured disc with the class's key character.
        var cls = Classes.Get(RunState.classId);
        var cc = ClassColor(cls.id);
        var disc = new Rect(r.x + 12, r.y + 19, 70, 70);
        GUI.DrawTexture(disc, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, (cc * 0.55f).linear, 0f, 35f);
        GUI.DrawTexture(disc, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, UiKit.Gold.linear, 2.5f, 35f);
        UiKit.Text(disc, cls.name.Substring(cls.name.Length - 2, 1), new GUIStyle(center) { fontSize = 32 });
        float bx = r.x + 94, bw = 180;
        var lab = new GUIStyle(left) { fontSize = 14 };
        UiKit.Bar(new Rect(bx, r.y + 14, bw, 20), player.hp, player.maxHp, new Color(0.86f, 0.2f, 0.25f), player.maxHp <= 14 ? player.maxHp : 0);
        UiKit.Text(new Rect(bx + bw + 8, r.y + 10, 60, 28), $"{player.hp}/{player.maxHp}", lab);
        UiKit.Bar(new Rect(bx, r.y + 44, bw, 20), player.armor, player.maxArmor, new Color(0.62f, 0.68f, 0.8f), player.maxArmor <= 14 ? player.maxArmor : 0);
        UiKit.Text(new Rect(bx + bw + 8, r.y + 40, 60, 28), $"{player.armor}/{player.maxArmor}", lab);
        UiKit.Bar(new Rect(bx, r.y + 74, bw, 20), player.energy, player.maxEnergy, new Color(0.25f, 0.55f, 1f));
        UiKit.Text(new Rect(bx + bw + 8, r.y + 70, 60, 28), $"{player.energy}", lab);
        // Floor · kills · gold, and how many are left in the room.
        var strip = new Rect(16, r.yMax + 6, 340, 32);
        GUI.Box(strip, GUIContent.none, UiKit.Pill);
        UiKit.Text(new Rect(strip.x + 14, strip.y, 330, 32),
            $"第 {RunState.floor}/{RunState.Floors} 层 · {themeName}   击杀 {RunState.kills}   <color=#FFD45A>金币 {RunState.gold}</color>", new GUIStyle(left) { fontSize = 15 });
        if (enemiesLeft >= 0)
        {
            var er = new Rect(16, strip.yMax + 6, 170, 30);
            GUI.Box(er, GUIContent.none, UiKit.Pill);
            UiKit.Text(new Rect(er.x + 14, er.y, 160, 30), $"<color=#FF7A6A>剩余敌人 {enemiesLeft}</color>", new GUIStyle(left) { fontSize = 15 });
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
            Label(new Rect(x - 110, y - 50, 220, 22), $"<color=#{ColorUtility.ToHtmlStringRGB(e.EliteColor)}>{e.EliteName}</color>", tag);
            Label(new Rect(x - 110, y - 30, 220, 20), e.KindName, new GUIStyle(tag) { fontSize = 14 });
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
        UiKit.Text(new Rect(0, h * 0.16f, w, 70), $"第 {RunState.floor} 层 · 选择一份祝福", new GUIStyle(title) { fontSize = 46 }, UiKit.Gold);
        float cw = 300, ch = 240, gap = 30;
        float x0 = w / 2 - (cw * offer.Count + gap * (offer.Count - 1)) / 2;
        var mouse = Event.current.mousePosition;
        for (int i = 0; i < offer.Count; i++)
        {
            var r = new Rect(x0 + i * (cw + gap), h * 0.34f, cw, ch);
            bool over = r.Contains(mouse);
            GUI.Box(r, GUIContent.none, over ? UiKit.CardHover : UiKit.Card);
            UiKit.Text(new Rect(r.x, r.y + 18, r.width, 30), $"{i + 1}", new GUIStyle(center) { fontSize = 22 }, UiKit.Dim);
            UiKit.Text(new Rect(r.x, r.y + 50, r.width, 50), offer[i].name, new GUIStyle(center) { fontSize = 36 }, UiKit.Gold);
            UiKit.Text(new Rect(r.x + 24, r.y + 112, r.width - 48, 100), offer[i].desc, new GUIStyle(center) { fontSize = 18, wordWrap = true, fontStyle = FontStyle.Normal });
            if (offer[i].stackable) UiKit.Text(new Rect(r.x, r.yMax - 36, r.width, 24), "可叠加", new GUIStyle(small) { fontSize = 14 }, UiKit.Dim);
            if (Event.current.type == EventType.MouseDown && over)
            {
                Sfx.Play("ui_click", 0.7f, 0.02f);
                PickBuff(i);
                Event.current.Use();
                return;
            }
        }
        UiKit.Text(new Rect(0, h * 0.34f + ch + 20, w, 30), "点击卡片或按 1 / 2 / 3", small);
    }

    void DrawMenu(float w, float h, string heading, string primary, System.Action primaryAction)
    {
        UiKit.Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.02f, 0.05f, 0.62f));
        bool over = screen == Screen.Dead || screen == Screen.Won;
        float top = over ? h * 0.10f : h * 0.22f;
        UiKit.Text(new Rect(0, top, w, 80), heading, new GUIStyle(title) { fontSize = 58 }, screen == Screen.Dead ? new Color(1f, 0.45f, 0.4f) : UiKit.Gold);
        float y = h * 0.48f;
        if (over) y = DrawSummary(w, top + 90f) + 20f;
        else if (player)
        {
            // What you are carrying: both weapons (with upgrades), gold, the blessings so far.
            var pr = new Rect(w / 2 - 300, top + 84, 600, 92);
            UiKit.DrawPanel(pr);
            string Wpn(WeaponDef d) => d == null ? "—" : $"<color=#{ColorUtility.ToHtmlStringRGB(Weapons.RarityColors[d.rarity])}>{d.Title}</color>";
            UiKit.Text(new Rect(pr.x + 20, pr.y + 8, 560, 26), $"第 {RunState.floor}/{RunState.Floors} 层 · 击杀 {RunState.kills} · <color=#FFD45A>金币 {RunState.gold}</color> · 武器 {Wpn(player.weapons[0])}、{Wpn(player.weapons[1])}", new GUIStyle(left) { fontSize = 16 });
            var names = new List<string>();
            foreach (var id in RunState.buffs) { var b = Buffs.All.Find(d => d.id == id); names.Add(b != null ? b.name : id); }
            UiKit.Text(new Rect(pr.x + 20, pr.y + 40, 560, 44), "祝福：" + (names.Count > 0 ? string.Join("、", names) : "—"), new GUIStyle(left) { fontSize = 16, wordWrap = true, alignment = TextAnchor.UpperLeft });
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
        var panel = new Rect(w / 2 - 340, y, 680, 250);
        UiKit.DrawPanel(panel);
        int secs = Mathf.FloorToInt(RunState.runTime);
        var cls = Classes.Get(RunState.classId);
        var big = new GUIStyle(left) { fontSize = 20, richText = true };
        float x = panel.x + 24, lh = 32;
        Label(new Rect(x, y + 14, 620, lh), $"{cls.name} · 用时 {secs / 60}:{secs % 60:00} · 到达第 {RunState.floor} 层 · 击杀 {RunState.kills}（首领 {RunState.bossesKilled}）", big);
        Label(new Rect(x, y + 14 + lh, 640, lh), $"造成伤害 {RunState.damageDealt} · 受到伤害 {RunState.damageTaken} · 最高连击 {RunState.bestCombo} · 完美闪避 {RunState.perfectDodges} · 机甲 {RunState.mechsPiloted} 次", big);
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
        var frame = new Rect(screenW - size - 28, 12, size + 16, size + 44);
        UiKit.DrawPanel(frame);
        UiKit.Text(new Rect(frame.x, frame.y + 4, frame.width, 26), themeName, new GUIStyle(center) { fontSize = 15 }, UiKit.Gold);
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
                Dungeon.Kind.Altar => new Color(0.95f, 0.35f, 0.35f),
                Dungeon.Kind.Trial => new Color(1f, 0.6f, 0.25f),
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
                Dungeon.Kind.Altar => "<color=#5A0E0E>祭</color>",
                Dungeon.Kind.Trial => "<color=#5A2E08>试</color>",
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
