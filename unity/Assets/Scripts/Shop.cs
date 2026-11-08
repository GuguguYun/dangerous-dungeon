using System.Collections.Generic;
using UnityEngine;

/// One thing for sale on a shop pedestal: walk up, press E, pay in gold.
public class ShopItem : MonoBehaviour
{
    public enum Kind { Weapon, Potion, Energy, Buff, Upgrade, Mech }

    public static readonly List<ShopItem> Live = new List<ShopItem>();
    public const string UpgradeModel = "smith_anvil", BlessingModel = "blessing_scroll";
    public Kind kind;
    public int price;
    public WeaponDef weapon;
    public BuffDef buff;
    public MechDef mech;      // Mech: which one the beacon calls in (its figurine turns on the pedestal)
    public Vector3 dropAt;    // Mech: where it lands
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
        Kind.Upgrade => "武器强化",
        Kind.Mech => $"机甲信标：{mech.name}",
        _ => "增益：" + buff.name,
    };

    public string Desc => kind switch
    {
        Kind.Weapon => $"{Weapons.RarityNames[weapon.rarity]} · {weapon.desc}",
        Kind.Potion => "回复 3 点生命（在机甲里则修理机甲）",
        Kind.Energy => "回复 100 点能量",
        Kind.Upgrade => PlayerController.Current ? $"手上的「{PlayerController.Current.Weapon.Title}」伤害 +1（这一局都有效，可叠加）" : "手上的武器伤害 +1",
        Kind.Mech => $"呼叫{(mech.heavy ? "重装" : "轻装")}机甲「{mech.name}」空投到商店门口",
        _ => buff.desc,
    };

    public Color Tint => kind switch
    {
        Kind.Weapon => Weapons.RarityColors[weapon.rarity],
        Kind.Potion => new Color(1f, 0.35f, 0.4f),
        Kind.Energy => new Color(0.35f, 0.6f, 1f),
        Kind.Upgrade => new Color(1f, 0.55f, 0.25f),
        Kind.Mech => mech.color,
        _ => new Color(1f, 0.85f, 0.35f),
    };

    public static ShopItem Create(Kind kind, Vector3 at, Transform parent, WeaponDef w = null, BuffDef b = null, MechDef m = null)
    {
        // A carved stone pedestal (the dungeon's pillar, cut down) with the ware floating and turning above it.
        var stand = Kit.Spawn("pillar", at, Quaternion.identity, parent);
        stand.name = "pedestal";
        stand.transform.localScale = new Vector3(0.8f / 1.1f, 0.9f / 4.85f, 0.8f / 1.1f);

        var go = new GameObject("shop_" + kind);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        var item = go.AddComponent<ShopItem>();
        item.kind = kind;
        item.weapon = w;
        item.buff = b;
        item.mech = m;
        item.price = kind switch
        {
            Kind.Weapon => new[] { 12, 20, 32, 48, 70 }[w.rarity],
            Kind.Potion => 12,
            Kind.Energy => 8,
            Kind.Upgrade => 22,
            Kind.Mech => 45,
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
            // The same Meshy potion and energy crystal that drop on the floor, a size up on the pedestal.
            case Kind.Potion: if (!LootFx.PickupModel(LootFx.PotionModel, item.visual, 1.3f, true)) Orb(item.visual, 0.42f, "pickup_health"); break;
            case Kind.Energy: if (!LootFx.PickupModel(LootFx.CrystalModel, item.visual, 1.75f, true)) Orb(item.visual, 0.32f, "pickup_energy"); break;
            case Kind.Mech:
            {
                // A little figurine of the mech it calls in.
                var fig = new GameObject("figurine").transform;
                fig.SetParent(item.visual, false);
                Mechs.Visual(m, fig, Layers.World, out _);
                fig.localScale = Vector3.one * 0.3f;
                fig.localPosition = Vector3.down * 0.35f;
                break;
            }
            // A smith's anvil for the weapon upgrade, a blessing scroll for the random blessing.
            case Kind.Upgrade: if (!LootFx.PickupModel(UpgradeModel, item.visual, 1.2f, true)) Orb(item.visual, 0.3f, "embers"); break;
            default:
                // The scroll is modelled lying open; stand it up so its face turns towards you.
                var upright = new GameObject("upright").transform;
                upright.SetParent(item.visual, false);
                upright.localRotation = Quaternion.Euler(90f, 0f, 0f);
                if (!LootFx.PickupModel(BlessingModel, upright, 1.25f, true)) Orb(item.visual, 0.3f, "pickup_gold");
                break;
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
            case Kind.Upgrade: p.UpgradeWeapon(); break;
            case Kind.Mech: ParkedMech.Drop(mech, dropAt, Random.Range(0f, 360f)); break;
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

        // Six wares in a row between the merchant and the door (the shop and chests are the only
        // places to get weapons and mechs).
        var owned = new[] { PlayerController.Current?.weapons[0]?.id, PlayerController.Current?.weapons[1]?.id };
        var w = Weapons.RandomExcept(owned);
        var offers = Buffs.Offer(1);
        Vector3 row = center - toDoor * 0.6f;
        ShopItem.Create(ShopItem.Kind.Weapon, row - side * 3.5f, parent, w);
        ShopItem.Create(ShopItem.Kind.Potion, row - side * 2.1f, parent);
        ShopItem.Create(ShopItem.Kind.Energy, row - side * 0.7f, parent);
        ShopItem.Create(ShopItem.Kind.Upgrade, row + side * 0.7f, parent);
        if (offers.Count > 0) ShopItem.Create(ShopItem.Kind.Buff, row + side * 2.1f, parent, null, offers[0]);
        else ShopItem.Create(ShopItem.Kind.Potion, row + side * 2.1f, parent);
        var beacon = ShopItem.Create(ShopItem.Kind.Mech, row + side * 3.5f, parent, null, null, Mechs.Random());
        beacon.dropAt = center + toDoor * 2.6f;
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
