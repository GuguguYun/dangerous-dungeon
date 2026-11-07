using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Comfort and display settings (3D motion-sickness checklist), kept in PlayerPrefs.
/// Defaults follow the checklist: no v-sync latency, frame cap at the display rate, crisp MSAA,
/// wider FOV, raw mouse input, moderate shake.
public static class GameSettings
{
    public static float fov = 68f;
    public static float sensitivity = 1f;
    public static bool invertX, invertY;
    public static float shake = 0.5f;
    public static bool flashes = true;
    public static bool vsync;
    public static int fpsCap; // 0 = follow the display refresh rate, -1 = unlimited
    public static float renderScale = 1f;
    public static bool smaa;
    public static bool bloom = true, vignette = true;
    public static bool showFps;
    public static float music = 0.45f;
    public static int window = 1; // index into Windows

    public static readonly int[] FpsCaps = { 0, 60, 90, 120, 144, -1 };
    public static readonly string[] FpsCapNames = { "跟随显示器", "60", "90", "120", "144", "不限" };
    public static readonly Vector2Int[] Windows =
        { new Vector2Int(1280, 800), new Vector2Int(1440, 900), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), Vector2Int.zero };
    public static readonly string[] WindowNames = { "1280×800", "1440×900（推荐）", "1600×900", "1920×1080", "全屏" };

    static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        fov = PlayerPrefs.GetFloat("fov", fov);
        sensitivity = PlayerPrefs.GetFloat("sens", sensitivity);
        invertX = PlayerPrefs.GetInt("invX", 0) == 1;
        invertY = PlayerPrefs.GetInt("invY", 0) == 1;
        shake = PlayerPrefs.GetFloat("shake", shake);
        flashes = PlayerPrefs.GetInt("flashes", 1) == 1;
        vsync = PlayerPrefs.GetInt("vsync", 0) == 1;
        fpsCap = PlayerPrefs.GetInt("fpsCap", 0);
        renderScale = PlayerPrefs.GetFloat("scale", renderScale);
        smaa = PlayerPrefs.GetInt("smaa", 0) == 1;
        bloom = PlayerPrefs.GetInt("bloom", 1) == 1;
        vignette = PlayerPrefs.GetInt("vignette", 1) == 1;
        showFps = PlayerPrefs.GetInt("fps", 0) == 1;
        window = PlayerPrefs.GetInt("window", window);
        music = PlayerPrefs.GetFloat("music", music);
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("fov", fov);
        PlayerPrefs.SetFloat("sens", sensitivity);
        PlayerPrefs.SetInt("invX", invertX ? 1 : 0);
        PlayerPrefs.SetInt("invY", invertY ? 1 : 0);
        PlayerPrefs.SetFloat("shake", shake);
        PlayerPrefs.SetInt("flashes", flashes ? 1 : 0);
        PlayerPrefs.SetInt("vsync", vsync ? 1 : 0);
        PlayerPrefs.SetInt("fpsCap", fpsCap);
        PlayerPrefs.SetFloat("scale", renderScale);
        PlayerPrefs.SetInt("smaa", smaa ? 1 : 0);
        PlayerPrefs.SetInt("bloom", bloom ? 1 : 0);
        PlayerPrefs.SetInt("vignette", vignette ? 1 : 0);
        PlayerPrefs.SetInt("fps", showFps ? 1 : 0);
        PlayerPrefs.SetInt("window", window);
        PlayerPrefs.SetFloat("music", music);
        PlayerPrefs.Save();
    }

    /// Frame pacing, quality and the camera/post settings that live outside the scene objects.
    public static void ApplyDisplay(Camera cam, Volume post, bool scripted)
    {
        if (!scripted)
        {
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            int refresh = Mathf.Max(60, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));
            Application.targetFrameRate = vsync ? -1 : fpsCap == 0 ? refresh : fpsCap;
        }
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            urp.renderScale = renderScale;
        if (cam)
        {
            cam.fieldOfView = fov;
            var data = cam.GetUniversalAdditionalCameraData();
            data.antialiasing = smaa ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
        }
        if (post && post.profile)
        {
            if (post.profile.TryGet<Bloom>(out var b)) b.active = bloom;
            if (post.profile.TryGet<Vignette>(out var v)) v.active = vignette;
        }
    }

    public static void ApplyWindow()
    {
        var w = Windows[Mathf.Clamp(window, 0, Windows.Length - 1)];
        if (w == Vector2Int.zero)
            Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
        else
            Screen.SetResolution(w.x, w.y, FullScreenMode.Windowed);
    }
}
