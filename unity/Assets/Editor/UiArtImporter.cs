using UnityEditor;
using UnityEngine;

/// The painted UI art (Resources/UI) is drawn at many sizes and nine-sliced: keep it at its own size,
/// clamped at the edges, with mipmaps so it stays smooth when drawn small.
///
/// Colour: in this linear-space project IMGUI's texture path encodes to sRGB twice (measured with
/// the -uiswatch grey patches: a texture holding linear 0.028 shows as 0.41, a vertex colour of
/// linear 0.028 as 0.17), which washes the art out (dark stone 0.18 came out 0.48). So the art is
/// stored decoded twice, as half floats so the dark tones keep their gradations, and comes out on
/// screen as painted.
public class UiArtImporter : AssetPostprocessor
{
    // Bumped whenever the rules below change, so Unity re-imports the art.
    public override uint GetVersion() => 3;

    static bool IsUiArt(string path) => path.StartsWith("Assets/Resources/UI/") && path.EndsWith(".png");

    void OnPreprocessTexture()
    {
        if (!IsUiArt(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Trilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 2048;
        var ps = ti.GetPlatformTextureSettings("Standalone");
        ps.overridden = true;
        ps.format = TextureImporterFormat.RGBAHalf;
        ps.maxTextureSize = 2048;
        ti.SetPlatformTextureSettings(ps);
    }

    static float Twice(float v) => Mathf.GammaToLinearSpace(Mathf.GammaToLinearSpace(v));

    void OnPostprocessTexture(Texture2D tex)
    {
        if (!IsUiArt(assetPath)) return;
        for (int m = 0; m < tex.mipmapCount; m++)
        {
            var px = tex.GetPixels(m);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color(Twice(c.r), Twice(c.g), Twice(c.b), c.a);
            }
            tex.SetPixels(px, m);
        }
        tex.Apply(false);
    }
}
