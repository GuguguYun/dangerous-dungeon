using UnityEngine;

/// One-time hints for new players: each shows the first time its situation comes up, then never again
/// on this machine. Scripted runs (self-test, screenshots) neither show nor record them.
public static class Tips
{
    public static bool Enabled = true;

    public static void Show(string id, string text)
    {
        if (!Enabled || PlayerPrefs.GetInt("tip_" + id, 0) == 1) return;
        PlayerPrefs.SetInt("tip_" + id, 1);
        PlayerPrefs.Save();
        Hud.I?.Tip(text);
    }
}
