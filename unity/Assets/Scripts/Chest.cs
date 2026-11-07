using UnityEngine;

/// The chest room's chest: opens when the player walks up and drops a weapon they don't have.
public class Chest : MonoBehaviour
{
    public Dungeon dungeon;
    public int minRarity;
    bool opened;

    void Update()
    {
        if (opened) return;
        var player = PlayerController.Current;
        if (!player || player.dead) return;
        Vector3 d = player.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude > 2.2f) return;
        opened = true;
        var w = Weapons.RandomExcept(player.weapons[0]?.id, player.weapons[1]?.id);
        for (int i = 0; i < 20 && w.rarity < minRarity; i++) w = Weapons.RandomExcept(player.weapons[0]?.id, player.weapons[1]?.id);
        Pickup.Spawn(PickupKind.Weapon, transform.position - d.normalized * 1.4f, 0, w);
        Pickup.SpawnEnergy(transform.position, 4);
        dungeon.ShowMessage($"宝箱：{w.name}（按 E 拾取）", 2f);
        transform.localScale = new Vector3(1.1f, 0.8f, 1.1f);
        Sfx.Play("buff", 0.6f, 0f);
    }
}
