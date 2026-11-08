using UnityEngine;

/// The chest room's chest: opens when the player walks up and drops a weapon they don't have.
/// Now and then it is a mimic: it wakes when you come close and has to be beaten for its loot.
public class Chest : MonoBehaviour
{
    public const float MimicChance = 0.3f;
    public Dungeon dungeon;
    public int minRarity;
    public bool mimic;
    public Enemy woke; // the mimic it turned into (self-test)
    bool opened;

    void Update()
    {
        if (opened) return;
        var player = PlayerController.Current;
        if (!player || player.dead) return;
        Vector3 d = player.transform.position - transform.position;
        d.y = 0f;
        if (mimic && d.magnitude < 3.2f)
        {
            Wake(player);
            return;
        }
        if (d.magnitude > 2.2f) return;
        opened = true;
        var w = Weapons.RandomAtLeast(minRarity, player.weapons[0]?.id, player.weapons[1]?.id);
        Pickup.Spawn(PickupKind.Weapon, transform.position - d.normalized * 1.4f, 0, w);
        Pickup.SpawnEnergy(transform.position, 4);
        dungeon.ShowMessage($"宝箱：{w.name}（{w.type}，按 E 拾取）", 2f);
        transform.localScale = new Vector3(1.1f, 0.8f, 1.1f);
        Sfx.Play("chest", 0.9f, 0f);
        Sfx.Play("buff", 0.4f, 0f);
    }

    void Wake(PlayerController player)
    {
        opened = true;
        var go = new GameObject("mimic");
        go.transform.SetPositionAndRotation(transform.position, transform.rotation);
        var e = go.AddComponent<Enemy>();
        e.Setup(EnemyKind.Mimic, RunState.floor);
        e.target = player;
        e.Died += m =>
        {
            Pickup.DropWeapon(m.transform.position, Mathf.Max(2, minRarity));
            Pickup.SpawnGold(m.transform.position, 10);
        };
        woke = e;
        Vfx.Puff(transform.position + Vector3.up * 0.5f, new Color(0.45f, 0.3f, 0.2f, 0.7f), 1.2f, 12);
        Sfx.Play("chest", 1f, 0f, 0.6f);
        CameraShake.Add(0.2f);
        Tips.Show("mimic", "宝箱怪！打败它会掉稀有以上的武器");
        dungeon.ShowMessage("宝箱怪！", 1.5f);
        gameObject.SetActive(false);
    }
}
