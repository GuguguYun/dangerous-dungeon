using System.Collections.Generic;
using UnityEngine;

/// The light at a gun's muzzle: each shot swells it and it dies away smoothly, so rapid fire keeps a
/// steady glow. (A separate 0.06 s flash per shot strobed the room at the fire rate, 13 times a
/// second for the SMG, which brings on 3D sickness.) One glow per gun position, reused while it fires.
public class MuzzleGlow : MonoBehaviour
{
    const float Peak = 2f, Kick = 0.9f, Decay = 0.35f; // decay: seconds to fall to about a third
    static readonly List<MuzzleGlow> live = new List<MuzzleGlow>();
    Light l;

    public static void Pulse(Vector3 at, Color c)
    {
        MuzzleGlow g = null;
        foreach (var m in live)
            if (m && (m.transform.position - at).sqrMagnitude < 1.5f * 1.5f) { g = m; break; }
        if (!g)
        {
            var go = new GameObject("muzzle_glow");
            g = go.AddComponent<MuzzleGlow>();
            g.l = go.AddComponent<Light>();
            g.l.type = LightType.Point;
            g.l.range = 4f;
            g.l.intensity = 0f;
            g.l.shadows = LightShadows.None;
        }
        g.transform.position = at;
        g.l.color = c;
        g.l.intensity = Mathf.Min(Peak, g.l.intensity + Kick);
    }

    void OnEnable() => live.Add(this);
    void OnDisable() => live.Remove(this);

    void Update()
    {
        l.intensity *= Mathf.Exp(-Time.deltaTime / Decay);
        if (l.intensity < 0.03f) Destroy(gameObject);
    }
}
