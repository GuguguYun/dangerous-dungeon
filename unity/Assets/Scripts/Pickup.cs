using System.Collections.Generic;
using UnityEngine;

public enum PickupKind { Energy, Health, Weapon }

/// Things on the floor: energy orbs fly to the player, health potions heal on touch,
/// weapons wait for the player to press E.
public class Pickup : MonoBehaviour
{
    public static readonly List<Pickup> Live = new List<Pickup>();

    public PickupKind kind;
    public int amount;
    public WeaponDef weapon;
    public Rigidbody body;
    Transform visual;
    float age;
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
            case PickupKind.Weapon:
                var v = Weapons.MakeVisual(weapon, holder, 0);
                v.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var l = new GameObject("glow").AddComponent<Light>();
                l.transform.SetParent(go.transform, false);
                l.transform.localPosition = Vector3.up;
                l.type = LightType.Point;
                l.color = Weapons.RarityColors[weapon.rarity];
                l.range = 3.5f;
                l.intensity = 3f + weapon.rarity;
                break;
        }
        return p;
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
        visual.localPosition = body ? Vector3.zero : Vector3.up * (0.7f + Mathf.Sin(age * 3f) * 0.12f);
        visual.localRotation = Quaternion.Euler(0f, age * 90f, 0f);

        var player = PlayerController.Current;
        if (!player || player.dead || kind == PickupKind.Weapon) return;
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (kind == PickupKind.Energy && dist < 6f && age > 0.6f)
        {
            if (body && !body.isKinematic) body.isKinematic = true; // physics hands over to the magnet
            Vector3 aim = player.transform.position + Vector3.up * 0.6f - transform.position;
            transform.position += aim.normalized * Mathf.Min(aim.magnitude, 14f * Time.deltaTime);
        }
        if (dist < 1.0f)
        {
            if (kind == PickupKind.Energy) player.AddEnergy(amount);
            else player.Heal(amount);
            Sfx.Play("pickup", kind == PickupKind.Energy ? 0.25f : 0.6f, 0.15f);
            Destroy(gameObject);
        }
    }
}
