using System.Collections.Generic;
using UnityEngine;

/// Something in a room you walk up to and press E on (the HUD shows its title and what it does).
public abstract class Interactable : MonoBehaviour
{
    public static readonly List<Interactable> Live = new List<Interactable>();
    public abstract string Title { get; }
    public abstract string Desc { get; }
    public abstract Color Tint { get; }
    public virtual bool Available => true;
    public abstract void Use(PlayerController p);

    protected virtual void OnEnable() => Live.Add(this);
    protected virtual void OnDisable() => Live.Remove(this);

    protected static Light Glow(Transform parent, Vector3 local, Color c, float range, float intensity)
    {
        var l = new GameObject("glow").AddComponent<Light>();
        l.transform.SetParent(parent, false);
        l.transform.localPosition = local;
        l.type = LightType.Point;
        l.color = c;
        l.range = range;
        l.intensity = intensity;
        return l;
    }
}

/// The blood altar: give up 2 max health, then pick one of three blessings.
public class Altar : Interactable
{
    bool used;
    Transform orb;
    Light glow;

    public override string Title => "血之祭坛";
    public override string Desc => used ? "祭坛已经沉寂" : "献出 2 点生命上限，从三份祝福中选一份";
    public override Color Tint => new Color(1f, 0.3f, 0.3f);
    public override bool Available => !used;

    public static Altar Create(Vector3 at, Transform parent)
    {
        var go = new GameObject("altar");
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        var stone = Kit.Spawn("pillar", at, Quaternion.Euler(0f, 45f, 0f), go.transform);
        stone.transform.localScale = new Vector3(1.6f, 0.33f, 1.6f);
        var a = go.AddComponent<Altar>();
        var o = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(o.GetComponent<Collider>());
        o.transform.SetParent(go.transform, false);
        o.transform.localPosition = Vector3.up * 1.7f;
        o.transform.localScale = Vector3.one * 0.5f;
        o.GetComponent<Renderer>().sharedMaterial = Palette.Get("pickup_health");
        a.orb = o.transform;
        a.glow = Glow(go.transform, Vector3.up * 1.8f, new Color(1f, 0.25f, 0.25f), 6f, 4f);
        return a;
    }

    void Update()
    {
        if (orb) orb.localPosition = Vector3.up * (1.7f + Mathf.Sin(Time.time * 2f) * 0.12f);
    }

    public override void Use(PlayerController p)
    {
        if (used) return;
        if (p.maxHp <= 3)
        {
            Hud.I?.Show("你的生命上限太低，祭坛不收", 1.5f);
            return;
        }
        used = true;
        p.maxHp -= 2;
        RunState.maxHp = p.maxHp;
        p.hp = Mathf.Min(p.hp, p.maxHp);
        Vfx.Ring(transform.position, 2.5f, Tint);
        Vfx.Sparks(transform.position + Vector3.up * 1.7f, Tint, 20, 5f);
        Sfx.Play("gong", 0.6f, 0f);
        if (orb) Destroy(orb.gameObject);
        if (glow) glow.intensity = 0.8f;
        TimeControl.paused = true;
        PlayerController.LockCursor(false);
        Hud.I?.OfferBuffs(Buffs.Offer(3), b =>
        {
            Buffs.Take(b, p);
            TimeControl.paused = false;
            PlayerController.LockCursor(true);
            Hud.I?.Show($"血之祭坛赐予你：{b.name}", 2f);
        });
    }
}

/// The trial statue: wake it to lock the room and fight three waves of elites; win an epic chest.
public class TrialStatue : Interactable
{
    public Room room;
    bool started;

    public override string Title => "试炼石像";
    public override string Desc => started ? "试炼进行中" : "开启试炼：三波精英怪，胜利后得到史诗宝箱";
    public override Color Tint => new Color(1f, 0.6f, 0.2f);
    public override bool Available => !started;

    public static TrialStatue Create(Vector3 at, Room room, Transform parent)
    {
        var go = new GameObject("trial_statue");
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        if (Props.Has("broken_pillar")) Props.Spawn("broken_pillar", at, Quaternion.identity, go.transform);
        else Kit.Spawn("pillar", at, Quaternion.identity, go.transform);
        var s = go.AddComponent<TrialStatue>();
        s.room = room;
        Glow(go.transform, Vector3.up * 2.2f, new Color(1f, 0.55f, 0.2f), 6f, 3f);
        return s;
    }

    public override void Use(PlayerController p)
    {
        if (started || !room) return;
        started = true;
        Sfx.Play("gong", 0.7f, 0f);
        Vfx.Ring(transform.position, 3f, Tint);
        Hud.I?.Show("试炼开始！击败三波精英", 2f);
        room.Begin();
    }
}
