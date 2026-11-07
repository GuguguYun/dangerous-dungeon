using System.Collections.Generic;
using UnityEngine;

/// One thing for sale on a shop pedestal: walk up, press E, pay in gold.
public class ShopItem : MonoBehaviour
{
    public enum Kind { Weapon, Potion, Energy, Buff }

    public static readonly List<ShopItem> Live = new List<ShopItem>();
    public Kind kind;
    public int price;
    public WeaponDef weapon;
    public BuffDef buff;
    public bool sold;
    Transform visual;
    Light glow;

    void OnEnable() => Live.Add(this);
    void OnDisable() => Live.Remove(this);

    public string Title => kind switch
    {
        Kind.Weapon => $"{weapon.name}（{weapon.type}）",
        Kind.Potion => "赤红药剂",
        Kind.Energy => "蓝晶电池",
        _ => "增益：" + buff.name,
    };

    public string Desc => kind switch
    {
        Kind.Weapon => $"{Weapons.RarityNames[weapon.rarity]} · {weapon.desc}",
        Kind.Potion => "回复 3 点生命（在机甲里则修理机甲）",
        Kind.Energy => "回复 100 点能量",
        _ => buff.desc,
    };

    public Color Tint => kind switch
    {
        Kind.Weapon => Weapons.RarityColors[weapon.rarity],
        Kind.Potion => new Color(1f, 0.35f, 0.4f),
        Kind.Energy => new Color(0.35f, 0.6f, 1f),
        _ => new Color(1f, 0.85f, 0.35f),
    };

    public static ShopItem Create(Kind kind, Vector3 at, Transform parent, WeaponDef w = null, BuffDef b = null)
    {
        // A stone pedestal with the ware floating and turning above it.
        var stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stand.name = "pedestal";
        stand.layer = Layers.World;
        stand.transform.SetParent(parent, false);
        stand.transform.position = at + Vector3.up * 0.45f;
        stand.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        stand.GetComponent<Renderer>().sharedMaterial = Palette.Get("trim");

        var go = new GameObject("shop_" + kind);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        var item = go.AddComponent<ShopItem>();
        item.kind = kind;
        item.weapon = w;
        item.buff = b;
        item.price = kind switch
        {
            Kind.Weapon => new[] { 12, 20, 32, 48, 70 }[w.rarity],
            Kind.Potion => 12,
            Kind.Energy => 8,
            _ => 30,
        };
        item.visual = new GameObject("ware").transform;
        item.visual.SetParent(go.transform, false);
        item.visual.localPosition = Vector3.up * 1.35f;
        switch (kind)
        {
            case Kind.Weapon:
                var v = Weapons.MakeVisual(w, item.visual, 0);
                v.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                break;
            case Kind.Potion: Orb(item.visual, 0.42f, "pickup_health"); break;
            case Kind.Energy: Orb(item.visual, 0.32f, "pickup_energy"); break;
            default: Orb(item.visual, 0.3f, "pickup_gold"); break;
        }
        item.glow = new GameObject("glow").AddComponent<Light>();
        item.glow.transform.SetParent(go.transform, false);
        item.glow.transform.localPosition = Vector3.up * 1.6f;
        item.glow.type = LightType.Point;
        item.glow.color = item.Tint;
        item.glow.range = 3f;
        item.glow.intensity = 2.5f;
        return item;
    }

    static void Orb(Transform parent, float r, string mat)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(s.GetComponent<Collider>());
        s.transform.SetParent(parent, false);
        s.transform.localScale = Vector3.one * r * 2f;
        s.GetComponent<Renderer>().sharedMaterial = Palette.Get(mat);
    }

    void Update()
    {
        if (visual) visual.localRotation = Quaternion.Euler(0f, Time.time * 60f, 0f);
    }

    /// Pay and take it. False (and a message) when gold is short.
    public bool Buy(PlayerController p)
    {
        if (sold) return false;
        if (RunState.gold < price)
        {
            Hud.I?.Show($"金币不够（还差 {price - RunState.gold}）", 1.2f);
            Sfx.Play("hit", 0.3f, 0.1f);
            return false;
        }
        RunState.gold -= price;
        sold = true;
        switch (kind)
        {
            case Kind.Weapon: Pickup.Spawn(PickupKind.Weapon, transform.position + Vector3.back * 0.01f, 0, weapon); break;
            case Kind.Potion: p.Heal(3); break;
            case Kind.Energy: p.AddEnergy(100); break;
            default: Buffs.Take(buff, p); break;
        }
        Vfx.Sparks(transform.position + Vector3.up * 1.35f, Tint, 14, 4f);
        Sfx.Play("coin", 0.9f, 0.05f);
        Sfx.Play("buff", 0.4f, 0f);
        Hud.I?.Show($"买下了 {Title}", 1.2f);
        if (visual) Destroy(visual.gameObject);
        if (glow) Destroy(glow.gameObject);
        RunState.shopBuys++;
        return true;
    }
}

/// The floor's shop room: a goblin merchant behind four pedestals (a weapon, a potion, energy, a buff).
public static class Shop
{
    public static void Build(Vector3 center, Vector3 entrance, Transform parent)
    {
        Vector3 toDoor = entrance - center;
        toDoor.y = 0f;
        toDoor = toDoor.sqrMagnitude > 0.01f ? toDoor.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, toDoor);
        var face = Quaternion.LookRotation(toDoor);

        // The merchant stands at the back, facing the door.
        Vector3 back = center - toDoor * 2.6f;
        var keeper = new GameObject("merchant").transform;
        keeper.SetParent(parent, false);
        keeper.SetPositionAndRotation(back, face);
        var anim = CharacterAnim.Spawn("merchant", keeper, Layers.World);
        if (anim) keeper.gameObject.AddComponent<Merchant>().anim = anim;
        else
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(keeper, false);
            body.transform.localPosition = Vector3.up * 0.65f;
            body.transform.localScale = new Vector3(0.7f, 0.65f, 0.7f);
            body.GetComponent<Renderer>().sharedMaterial = Palette.Get("enemy_spreader");
        }
        var col = keeper.gameObject.AddComponent<CapsuleCollider>();
        col.radius = 0.5f;
        col.height = 1.4f;
        col.center = Vector3.up * 0.7f;
        keeper.gameObject.layer = Layers.World;

        // Four wares in a row between the merchant and the door.
        var owned = new[] { PlayerController.Current?.weapons[0]?.id, PlayerController.Current?.weapons[1]?.id };
        var w = Weapons.RandomExcept(owned);
        var offers = Buffs.Offer(1);
        Vector3 row = center - toDoor * 0.6f;
        ShopItem.Create(ShopItem.Kind.Weapon, row - side * 2.4f, parent, w);
        ShopItem.Create(ShopItem.Kind.Potion, row - side * 0.8f, parent);
        ShopItem.Create(ShopItem.Kind.Energy, row + side * 0.8f, parent);
        if (offers.Count > 0) ShopItem.Create(ShopItem.Kind.Buff, row + side * 2.4f, parent, null, offers[0]);
        else ShopItem.Create(ShopItem.Kind.Potion, row + side * 2.4f, parent);
    }
}

/// The shopkeeper idles, waves when you come in and cheers when you buy.
public class Merchant : MonoBehaviour
{
    public CharacterAnim anim;
    bool greeted;
    int buys;

    void Update()
    {
        var p = PlayerController.Current;
        if (!p || !anim) return;
        anim.SetBase("idle");
        float d = Vector3.Distance(p.transform.position, transform.position);
        if (!greeted && d < 7f)
        {
            greeted = true;
            anim.Trigger("taunt", 1f, 0.5f);
            Hud.I?.Show("商人金牙：欢迎光临！走到货架前按 E 购买", 2.5f);
        }
        if (RunState.shopBuys != buys)
        {
            buys = RunState.shopBuys;
            anim.Trigger("cast", 1.2f, 0.3f);
        }
    }
}
