using System.Collections.Generic;
using UnityEngine;

/// Sound effects: recorded CC0 clips from Resources/Sfx (Kenney's RPG and UI packs, the "100 CC0 SFX"
/// pack; variants name_1, name_2 ... are picked at random), else a short sound synthesised at startup.
public static class Sfx
{
    const int Rate = 44100;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static readonly Dictionary<string, AudioClip[]> files = new Dictionary<string, AudioClip[]>();

    static AudioClip[] Files(string name)
    {
        if (files.TryGetValue(name, out var found)) return found;
        var list = new List<AudioClip>();
        var one = Resources.Load<AudioClip>("Sfx/" + name);
        if (one) list.Add(one);
        for (int i = 1; i <= 8; i++)
        {
            var c = Resources.Load<AudioClip>($"Sfx/{name}_{i}");
            if (c) list.Add(c);
        }
        return files[name] = list.ToArray();
    }
    static AudioSource[] pool;
    static int next;
    static System.Random rng = new System.Random(7);

    public static void Init(GameObject host)
    {
        pool = new AudioSource[16];
        for (int i = 0; i < pool.Length; i++)
        {
            pool[i] = host.AddComponent<AudioSource>();
            pool[i].playOnAwake = false;
            pool[i].spatialBlend = 0f;
        }
        if (clips.Count > 0) return;
        Make("shoot", 0.09f, t => Noise() * Env(t, 0.09f, 40f) * 0.5f + Sine(t, 900f - 4000f * t) * Env(t, 0.09f, 30f) * 0.5f);
        Make("shotgun", 0.25f, t => Noise() * Env(t, 0.25f, 14f) * 0.9f + Sine(t, 110f) * Env(t, 0.25f, 20f) * 0.5f);
        Make("laser", 0.16f, t => Square(t, 1600f - 7000f * t) * Env(t, 0.16f, 12f) * 0.35f);
        Make("rocket", 0.3f, t => Noise() * Env(t, 0.3f, 6f) * 0.5f + Sine(t, 180f - 200f * t) * Env(t, 0.3f, 8f) * 0.4f);
        Make("slash", 0.18f, t => Noise() * Mathf.Sin(Mathf.PI * t / 0.18f) * 0.45f);
        Make("hit", 0.06f, t => Sine(t, 320f) * Env(t, 0.06f, 50f) * 0.7f + Noise() * Env(t, 0.06f, 80f) * 0.3f);
        Make("enemy_shot", 0.14f, t => Sine(t, 520f - 1400f * t) * Env(t, 0.14f, 18f) * 0.4f);
        Make("hurt", 0.28f, t => Square(t, 140f - 200f * t) * Env(t, 0.28f, 9f) * 0.45f);
        Make("explode", 0.6f, t => Lowpass(Noise(), 0.08f) * Env(t, 0.6f, 5f) * 1.6f);
        Make("pickup", 0.12f, t => Sine(t, 1200f + 5000f * t) * Env(t, 0.12f, 20f) * 0.35f);
        Make("door", 0.35f, t => Sine(t, 70f) * Env(t, 0.35f, 9f) * 0.8f + Lowpass(Noise(), 0.1f) * Env(t, 0.35f, 12f));
        Make("die", 0.3f, t => Square(t, 400f - 1000f * t) * Env(t, 0.3f, 8f) * 0.3f);
        Make("buff", 0.5f, t => Sine(t, t < 0.12f ? 660f : t < 0.24f ? 880f : 1320f) * Env(t, 0.5f, 4f) * 0.4f);
        Make("boss", 1.0f, t => (Sine(t, 55f) + Sine(t, 58f)) * Env(t, 1f, 2.5f) * 0.6f);
    }

    /// A clip for a looping source of its own (fire): the recording if there is one, else the synthesised one.
    public static AudioClip Clip(string name)
    {
        var recorded = Files(name);
        return recorded.Length > 0 ? recorded[0] : clips.TryGetValue(name, out var c) ? c : null;
    }

    public static void Play(string name, float volume = 1f, float pitchJitter = 0.08f, float pitch = 1f)
    {
        if (pool == null) return;
        var recorded = Files(name);
        AudioClip clip = recorded.Length > 0 ? recorded[Random.Range(0, recorded.Length)] : clips.TryGetValue(name, out var synth) ? synth : null;
        if (!clip) return;
        var s = pool[next];
        next = (next + 1) % pool.Length;
        s.pitch = pitch * (1f + Random.Range(-pitchJitter, pitchJitter));
        s.PlayOneShot(clip, volume);
    }

    static float lp;
    static float Lowpass(float x, float a) => lp += (x - lp) * a;
    static float Noise() => (float)(rng.NextDouble() * 2 - 1);
    static float Sine(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);
    static float Square(float t, float f) => Mathf.Sign(Sine(t, f));
    static float Env(float t, float len, float decay) => Mathf.Exp(-decay * t) * Mathf.Clamp01((len - t) * 40f) * Mathf.Clamp01(t * 400f);

    static void Make(string name, float seconds, System.Func<float, float> f)
    {
        int n = (int)(seconds * Rate);
        var data = new float[n];
        lp = 0f;
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        clips[name] = clip;
    }
}
