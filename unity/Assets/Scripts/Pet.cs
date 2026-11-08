using UnityEngine;

/// The companion drone. It floats at chest height on the player's left, shoots the nearest enemy it can
/// see, and gets in the way of shots on that side (it takes the hit). At 0 HP it drops and reboots after
/// a while. The priest's Sanctuary heals it.
public class Pet : MonoBehaviour
{
    public static Pet Current;
    public int hp, maxHp;
    public bool offline;
    public int shots;
    PlayerController owner;
    Transform visual;
    Vector3 vel;
    float fireCd = 1f, rebootT, bob;
    Light eyeLight;
    ParticleSystem smoke;

    public static Pet Spawn(PlayerController p, int floor)
    {
        var go = new GameObject("pet_drone");
        go.layer = Layers.Player;
        go.transform.position = p.transform.position + Vector3.up * 1.1f - p.transform.right * 1f;
        var pet = go.AddComponent<Pet>();
        pet.owner = p;
        pet.maxHp = pet.hp = 6 + floor * 2;
        // Solid to enemy shots (Player layer) but not to the player's own controller.
        var col = go.AddComponent<SphereCollider>();
        col.radius = 0.35f;
        col.isTrigger = false;
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        Physics.IgnoreCollision(col, p.GetComponent<CharacterController>());
        pet.visual = new GameObject("visual").transform;
        pet.visual.SetParent(go.transform, false);
        if (Props.Has("pet_drone"))
        {
            var m = Props.Spawn("pet_drone", go.transform.position, Quaternion.identity, pet.visual, false);
            m.transform.localPosition = Vector3.down * 0.3f; // props are pivoted at the bottom
            foreach (var t in m.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Player;
            foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        else
        {
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(shell.GetComponent<Collider>());
            shell.transform.SetParent(pet.visual, false);
            shell.transform.localScale = Vector3.one * 0.5f;
            shell.GetComponent<Renderer>().sharedMaterial = Palette.Get("steel");
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(eye.GetComponent<Collider>());
            eye.transform.SetParent(pet.visual, false);
            eye.transform.localPosition = new Vector3(0f, 0.05f, 0.2f);
            eye.transform.localScale = Vector3.one * 0.18f;
            eye.GetComponent<Renderer>().sharedMaterial = Palette.Get("player_visor");
        }
        pet.eyeLight = new GameObject("eye_light").AddComponent<Light>();
        pet.eyeLight.transform.SetParent(go.transform, false);
        pet.eyeLight.type = LightType.Point;
        pet.eyeLight.color = new Color(0.4f, 0.95f, 1f);
        pet.eyeLight.range = 2.5f;
        pet.eyeLight.intensity = 1.5f;
        Current = pet;
        return pet;
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    void Update()
    {
        if (!owner) { Destroy(gameObject); return; }
        float dt = Time.deltaTime;
        if (offline)
        {
            rebootT -= dt;
            if (rebootT <= 0f) Reboot();
            return;
        }
        // Follow: chest height on the player's left, a little behind; a gentle bob.
        bob += dt;
        Vector3 fwd = Quaternion.Euler(0f, owner.yaw, 0f) * Vector3.forward;
        Vector3 left = -Vector3.Cross(Vector3.up, fwd);
        float height = owner.InMech ? owner.mechDef.height * 0.7f : 1.15f;
        float sideDist = owner.InMech ? owner.mechDef.radius + 0.8f : 1.0f;
        Vector3 goal = owner.transform.position + left * sideDist - fwd * 0.3f + Vector3.up * (height + Mathf.Sin(bob * 2.4f) * 0.06f);
        transform.position = Vector3.SmoothDamp(transform.position, goal, ref vel, 0.18f);

        // Look at and shoot the nearest enemy in sight.
        Enemy best = null;
        float bestD = 12f;
        foreach (var e in Enemy.Live)
        {
            if (!e || e.dead) continue;
            float d = Vector3.Distance(e.AimPoint, transform.position);
            if (d < bestD && !Physics.Linecast(transform.position, e.AimPoint, Layers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                bestD = d;
                best = e;
            }
        }
        Vector3 look = best ? best.AimPoint - transform.position : fwd;
        look.y *= 0.5f;
        if (look.sqrMagnitude > 0.01f) visual.rotation = Quaternion.Slerp(visual.rotation, Quaternion.LookRotation(look), 1f - Mathf.Exp(-10f * dt));
        fireCd -= dt;
        if (best && fireCd <= 0f && !owner.dead)
        {
            fireCd = 1.1f;
            shots++;
            Vector3 from = transform.position + visual.forward * 0.3f;
            Vector3 dir = (best.AimPoint - from).normalized;
            Bullet.Fire(from, dir * 40f, true, 0.07f, "bullet_player", 1.5f, 1 + RunState.floor / 2);
            Vfx.MuzzleFlash(from, dir, new Color(0.4f, 0.95f, 1f));
            Sfx.Play("laser", 0.25f, 0.2f);
        }
        eyeLight.intensity = 1.5f + (fireCd > 0.9f ? 3f : 0f);
    }

    /// An enemy shot hit the drone instead of the player.
    public void TakeDamage(int amount)
    {
        if (offline) return;
        hp = Mathf.Max(0, hp - amount);
        Vfx.Sparks(transform.position, new Color(0.6f, 0.9f, 1f), 8, 4f);
        Sfx.Play("hit", 0.4f, 0.2f);
        if (hp == 0)
        {
            offline = true;
            rebootT = 12f;
            smoke = Vfx.Trail(transform, "smoke");
            eyeLight.intensity = 0f;
            // Drop to the floor, dark.
            var p = transform.position;
            transform.position = new Vector3(p.x, 0.3f, p.z);
            Hud.I?.Show("无人机被击落：12 秒后重启", 1.5f);
        }
    }

    public void Heal(int amount)
    {
        if (offline) return;
        hp = Mathf.Min(maxHp, hp + amount);
    }

    void Reboot()
    {
        offline = false;
        hp = maxHp;
        Vfx.Detach(smoke);
        smoke = null;
        Vfx.Ring(transform.position, 1f, new Color(0.4f, 0.95f, 1f));
        Sfx.Play("buff", 0.4f, 0f);
    }
}
