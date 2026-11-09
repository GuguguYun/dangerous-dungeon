using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Comfort and display settings (3D motion-sickness checklist), kept in PlayerPrefs.
/// Defaults follow the checklist: no v-sync latency, frame cap at the display rate, crisp MSAA,
/// wider FOV, raw mouse input, moderate shake.
public static class GameSettings
{
    /// The browser build: the page owns the window size and paces the frames, and there is nothing to quit to.
    public static readonly bool Web = Application.platform == RuntimePlatform.WebGLPlayer;

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
    public static float uiScale = 1.2f; // on top of fitting the UI to the window height
    public static int window; // index into Windows

    public static readonly int[] FpsCaps = { 0, 60, 90, 120, 144, -1 };
    public static readonly string[] FpsCapNames = { "跟随显示器", "60", "90", "120", "144", "不限" };
    // Window sizes as a share of the screen (fixed pixel sizes come out a quarter of the screen on a
    // Retina Mac, where a point is two pixels); 0 = full screen. The big window leaves room for the
    // menu bar and the Dock.
    public static readonly Vector2[] Windows = { new Vector2(0.96f, 0.86f), Vector2.zero, new Vector2(0.8f, 0.75f), new Vector2(0.62f, 0.6f) };
    public static readonly string[] WindowNames = { "大窗口（推荐）", "全屏", "中窗口", "小窗口" };

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
        window = PlayerPrefs.GetInt("window2", window); // "window" held the old pixel-size list
        music = PlayerPrefs.GetFloat("music", music);
        uiScale = PlayerPrefs.GetFloat("ui", uiScale);
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
        PlayerPrefs.SetInt("window2", window);
        PlayerPrefs.SetFloat("music", music);
        PlayerPrefs.SetFloat("ui", uiScale);
        PlayerPrefs.Save();
    }

    /// Frame pacing, quality and the camera/post settings that live outside the scene objects.
    public static void ApplyDisplay(Camera cam, Volume post, bool scripted)
    {
        if (Web)
        {
            // The browser draws once per display refresh; a frame cap there only adds stutter.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
        else if (!scripted)
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
        if (Web) return;
        var w = Windows[Mathf.Clamp(window, 0, Windows.Length - 1)];
        var screen = Screen.currentResolution;
        if (w == Vector2.zero)
            Screen.SetResolution(screen.width, screen.height, FullScreenMode.FullScreenWindow);
        else
        {
            int ww = Mathf.RoundToInt(screen.width * w.x), wh = Mathf.RoundToInt(screen.height * w.y);
            Screen.SetResolution(ww, wh, FullScreenMode.Windowed);
            // The player grows the window about the old one's centre, which can push it off the screen
            // edge (measured: 27 points off the left); put it in the middle, just under the menu bar.
            var d = Screen.mainWindowDisplayInfo;
            if (d.width > 0)
                Screen.MoveMainWindowTo(d, new Vector2Int(Mathf.Max(0, (d.width - ww) / 2), Mathf.Max(0, (d.height - wh) / 4)));
        }
    }
}
