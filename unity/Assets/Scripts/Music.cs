using UnityEngine;

/// Background music with a slow crossfade between tracks: the dungeon ambience while exploring and on
/// the menus, the battle theme while a room is locked (and for bosses). CC0 tracks from OpenGameArt:
/// "Dungeon Ambience" and "Battle Theme B for RPG".
public class Music : MonoBehaviour
{
    public static Music I;
    AudioSource a, b;
    string current;
    float fade = 1f;
    const float FadeTime = 1.5f;

    public static void Init(GameObject host)
    {
        if (I) return;
        var go = new GameObject("music");
        DontDestroyOnLoad(go);
        I = go.AddComponent<Music>();
        I.a = I.Source();
        I.b = I.Source();
    }

    AudioSource Source()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.volume = 0f;
        return s;
    }

    public void Play(string track)
    {
        if (track == current) return;
        var clip = Resources.Load<AudioClip>("Music/" + track);
        if (!clip) return;
        current = track;
        Debug.Log("MUSIC " + track);
        // The old track fades out on a while the new one fades in on b.
        (a, b) = (b, a);
        a.clip = clip;
        a.Play();
        fade = 0f;
    }

    void Update()
    {
        fade = Mathf.Min(1f, fade + Time.unscaledDeltaTime / FadeTime);
        float v = GameSettings.music;
        a.volume = v * fade;
        b.volume = v * (1f - fade);
        if (fade >= 1f && b.isPlaying) b.Stop();
    }
}
