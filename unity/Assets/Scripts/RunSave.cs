using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

/// 继续冒险: the run as it stood when this floor began (after its blessing). Closing the game mid-run
/// and coming back starts the same floor again, on the same map, with the hero as they arrived; as in
/// Soul Knight, what the floor gave since is not kept. Every public RunState field is saved, so a new
/// one comes along without touching this file. Dying or winning clears it.
public static class RunSave
{
    const string Key = "run_save";
    static readonly string[] Skip = { nameof(RunState.testPass), nameof(RunState.testFail), nameof(RunState.SaveProgress), nameof(RunState.won) };

    static IEnumerable<FieldInfo> Fields()
    {
        foreach (var f in typeof(RunState).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (!f.IsLiteral && !f.IsInitOnly && System.Array.IndexOf(Skip, f.Name) < 0) yield return f;
    }

    public static bool Has => Showcase != null || (RunState.SaveProgress && PlayerPrefs.HasKey(Key));

    /// Screenshots: show the title's continue button for this run without a save.
    public static (int floor, string classId)? Showcase;

    /// The floor and class of the saved run, for the title button.
    public static (int floor, string classId) Peek()
    {
        if (Showcase != null) return Showcase.Value;
        var v = Parse(PlayerPrefs.GetString(Key, ""));
        int.TryParse(v.TryGetValue(nameof(RunState.floor), out var f) ? f : "1", out int floor);
        return (floor, v.TryGetValue(nameof(RunState.classId), out var c) ? c : "knight");
    }

    public static void Write(PlayerController p)
    {
        if (!RunState.SaveProgress) return;
        RunState.Keep(p);
        PlayerPrefs.SetString(Key, Capture());
        PlayerPrefs.Save();
    }

    public static void Clear()
    {
        if (!RunState.SaveProgress) return;
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }

    public static bool Load() => Has && Restore(PlayerPrefs.GetString(Key));

    public static string Capture()
    {
        var sb = new StringBuilder();
        foreach (var f in Fields()) sb.Append(f.Name).Append('=').Append(Encode(f.GetValue(null))).Append('\n');
        return sb.ToString();
    }

    /// Put a captured run back. Nothing changes unless every saved field reads back (an old or broken
    /// save is refused rather than half applied).
    public static bool Restore(string text)
    {
        var v = Parse(text);
        var decoded = new List<(FieldInfo, object)>();
        foreach (var f in Fields())
        {
            if (!v.TryGetValue(f.Name, out var s) || !TryDecode(s, f.FieldType, out var value)) return false;
            decoded.Add((f, value));
        }
        foreach (var (f, value) in decoded) f.SetValue(null, value);
        return true;
    }

    static Dictionary<string, string> Parse(string text)
    {
        var v = new Dictionary<string, string>();
        foreach (var line in text.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq > 0) v[line.Substring(0, eq)] = line.Substring(eq + 1);
        }
        return v;
    }

    // Ids never hold ',', ':' or '~'; '~' stands for a missing one (an empty second weapon slot).
    static string Id(string s) => s ?? "~";
    static string Unid(string s) => s == "~" ? null : s;

    static string Encode(object o) => o switch
    {
        null => "~",
        float x => x.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        string s => Id(s),
        string[] a => string.Join(",", System.Array.ConvertAll(a, Id)),
        List<string> l => string.Join(",", l.ConvertAll(Id)),
        Dictionary<string, int> d => string.Join(",", new List<KeyValuePair<string, int>>(d).ConvertAll(kv => $"{kv.Key}:{kv.Value}")),
        _ => System.Convert.ToString(o, CultureInfo.InvariantCulture),
    };

    static bool TryDecode(string s, System.Type t, out object value)
    {
        value = null;
        var parts = s.Length == 0 ? new string[0] : s.Split(',');
        if (t == typeof(int) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) value = i;
        else if (t == typeof(float) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) value = x;
        else if (t == typeof(bool) && (s == "0" || s == "1")) value = s == "1";
        else if (t == typeof(string)) value = Unid(s);
        else if (t == typeof(string[])) value = System.Array.ConvertAll(parts, Unid);
        else if (t == typeof(List<string>)) value = new List<string>(System.Array.ConvertAll(parts, Unid));
        else if (t == typeof(Dictionary<string, int>))
        {
            var d = new Dictionary<string, int>();
            foreach (var kv in parts)
            {
                int colon = kv.LastIndexOf(':');
                if (colon <= 0 || !int.TryParse(kv.Substring(colon + 1), out int n)) return false;
                d[kv.Substring(0, colon)] = n;
            }
            value = d;
        }
        else return false;
        return true;
    }
}
