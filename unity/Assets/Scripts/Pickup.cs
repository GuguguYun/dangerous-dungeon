using System.Collections.Generic;
using UnityEngine;

public enum PickupKind { Energy, Health, Weapon, Gold }

/// Things on the floor: energy orbs fly to the player, health potions heal on touch,
/// weapons wait for the player to press E under a rarity-coloured light pillar.
public class Pickup : MonoBehaviour
{
    public static readonly List<Pickup> Live = new List<Pickup>();

    public PickupKind kind;
    public int amount;
    public WeaponDef weapon;
    public Rigidbody body;
    Transform visual, beam;
    float age, beamWidth, beamHeight;
    Vector3 basePos;

    public static Pickup Spawn(PickupKind kind, Vector3 pos, int amount = 0, WeaponDef weapon = null)
    {
        var go = new GameObject("pickup_" + kind);
        go.transform.position = new Vector3(pos.x, kind == PickupKind.Weapon ? 0f : 0.6f, pos.z);
        var p = go.AddComponent<Pickup>();
        p.kind = kind;
        p.amount = amount;
        p.weapon = weapon;
        p.basePos = go.transform.position;
        if (kind != PickupKind.Weapon)
        {
            // Drops pop out and bounce around before they home in on the player.
            go.layer = Layers.Debris;
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.25f;
            col.center = Vector3.up * 0.0f;
            p.body = go.AddComponent<Rigidbody>();
            p.body.mass = 0.2f;
            p.body.linearDamping = 0.4f;
            p.body.AddForce(new Vector3(Random.Range(-2f, 2f), Random.Range(3f, 5f), Random.Range(-2f, 2f)), ForceMode.VelocityChange);
        }

        var holder = new GameObject("visual").transform;
        holder.SetParent(go.transform, false);
        p.visual = holder;
        switch (kind)
        {
            case PickupKind.Energy:
                Ball(holder, 0.22f, "pickup_energy");
                break;
            case PickupKind.Health:
                Ball(holder, 0.4f, "pickup_health");
                break;
            case PickupKind.Gold:
            {
                // A spinning gold coin.
                var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                DestroyImmediate(coin.GetComponent<Collider>());
                coin.transform.SetParent(holder, false);
                coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                coin.transform.localScale = new Vector3(0.32f, 0.03f, 0.32f);
                var mr = coin.GetComponent<Renderer>();
                mr.sharedMaterial = Palette.Get("pickup_gold");
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                break;
            }
            case PickupKind.Weapon:
                var v = Weapons.MakeVisual(weapon, holder, 0);
                v.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var l = new GameObject("glow").AddComponent<Light>();
                l.transform.SetParent(go.transform, false);
                l.transform.localPosition = Vector3.up * 0.8f;
                l.type = LightType.Point;
                l.color = Weapons.RarityColors[weapon.rarity];
                l.range = 3f + 0.3f * weapon.rarity;
                l.intensity = 1.6f + 0.7f * weapon.rarity;
                l.shadows = LightShadows.None;
                p.beam = LootFx.LootBeam(go.transform, weapon.rarity);
                p.beamWidth = p.beam.localScale.x;
                p.beamHeight = p.beam.localScale.y;
                p.beam.localScale = new Vector3(p.beamWidth, 0.01f, p.beamWidth);
                break;
        }
        return p;
    }

    /// A weapon for a kill or a room: at least minRarity, rarer on deeper floors. Shines and chimes.
    public static Pickup DropWeapon(Vector3 at, int minRarity = 0)
    {
        var p = PlayerController.Current;
        var w = Weapons.RandomAtLeast(minRarity, p ? p.weapons[0]?.id : null, p ? p.weapons[1]?.id : null);
        RunState.weaponPity = 0;
        var drop = Spawn(PickupKind.Weapon, at, 0, w);
        Sfx.Play(w.rarity >= 3 ? "gong" : "buff", w.rarity >= 3 ? 0.5f : 0.35f, 0f);
        return drop;
    }

    static void Ball(Transform parent, float r, string mat)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(s.GetComponent<Collider>());
        s.transform.SetParent(parent, false);
        s.transform.localScale = Vector3.one * r * 2f;
        var mr = s.GetComponent<Renderer>();
        mr.sharedMaterial = Palette.Get(mat);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// Coins worth one gold each; they bounce out and get pulled in like energy.
    public static void SpawnGold(Vector3 at, int coins)
    {
        for (int i = 0; i < coins; i++)
        {
            var off = Random.insideUnitCircle * 1.2f;
            Spawn(PickupKind.Gold, at + new Vector3(off.x, 0f, off.y), 1);
        }
    }

    public static void SpawnEnergy(Vector3 at, int orbs)
    {
        for (int i = 0; i < orbs; i++)
        {
            var off = Random.insideUnitCircle * 1.2f;
            Spawn(PickupKind.Energy, at + new Vector3(off.x, 0f, off.y), 3);
        }
    }

    void OnEnable() => Live.Add(this);
    void OnDisable() => Live.Remove(this);

    void Update()
    {
        age += Time.deltaTime;
        // Weapons pop up and bounce down when they drop, then hover.
        float pop = kind == PickupKind.Weapon && age < 0.6f ? Mathf.Abs(Mathf.Cos(age * 9f)) * (0.6f - age) * 2.2f : 0f;
        visual.localPosition = body ? Vector3.zero : Vector3.up * (0.7f + pop + Mathf.Sin(age * 3f) * 0.12f);
        visual.localRotation = Quaternion.Euler(0f, age * 90f, 0f);
        if (beam && age < 0.9f)
        {
            // The pillar grows up out of the floor as the weapon lands.
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / 0.9f), 3f);
            beam.localScale = new Vector3(beamWidth, Mathf.Max(0.01f, beamHeight * k), beamWidth);
        }

        var player = PlayerController.Current;
        if (!player || player.dead || kind == PickupKind.Weapon) return;
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if ((kind == PickupKind.Energy || kind == PickupKind.Gold) && dist < 6f && age > 0.6f)
        {
            if (body && !body.isKinematic) body.isKinematic = true; // physics hands over to the magnet
            Vector3 aim = player.transform.position + Vector3.up * 0.6f - transform.position;
            transform.position += aim.normalized * Mathf.Min(aim.magnitude, 14f * Time.deltaTime);
        }
        if (dist < 1.0f)
        {
            if (kind == PickupKind.Energy) player.AddEnergy(amount);
            else if (kind == PickupKind.Gold)
            {
                RunState.gold += amount;
                Tips.Show("gold", "金币能在商店花掉（小地图上标「商」的房间）");
            }
            else player.Heal(amount);
            if (kind == PickupKind.Gold) Sfx.Play("coin", 0.3f, 0.15f, 1.15f);
            else Sfx.Play("pickup", kind == PickupKind.Health ? 0.6f : 0.25f, 0.15f);
            Destroy(gameObject);
        }
    }
}
