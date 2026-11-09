using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// 青苔地宫 (floor 1): moss hanging from the wall tops, clumps at the wall feet and a few carpets on
/// combat-room floors. Cut-out pictures (Meshy, background removed) on quads, no colliders and no
/// shadows; the floor and wall textures on that floor are the mossy versions too (Kit.Load,
/// ThemeDef.mossy). Moss burns: whatever sets poison gas off (flames, burning enemies, blasts, fire
/// pits) catches the moss it reaches, fire creeps on to moss close by, and moss burning on the floor
/// sets alight whoever stands in it, you as well as them, like a fire pit.
public static class Moss
{
    public const string Hang = "moss_hang", Patch = "moss_patch";
    public static int Hanging, Patches, Burnt; // placed on this floor / burnt out (self-test)

    /// A lit, alpha-clipped, two-sided material for one picture. BuildScript saves it under
    /// Resources/Kit so the clipped Lit variant ships in the build.
    public static Material CreateMaterial(Texture tex)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = tex.name };
        m.SetTexture("_BaseMap", tex);
        m.mainTexture = tex;
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.45f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", (float)CullMode.Off);
        m.SetFloat("_Smoothness", 0.05f);
        m.SetOverrideTag("RenderType", "TransparentCutout");
        m.renderQueue = (int)RenderQueue.AlphaTest;
        return m;
    }

    static Material hang, patch;

    static bool Ready()
    {
        if (!hang) hang = Resources.Load<Material>("Kit/" + Hang);
        if (!patch) patch = Resources.Load<Material>("Kit/" + Patch);
        return hang && patch;
    }

    /// Dresses the walls (centre of each 4 m wall piece and the way into the room or corridor):
    /// about every other wall gets a curtain of moss under its top ledge, about one in three a clump
    /// at its foot, half tucked under the skirting. Its own random numbers, so the map and the rest
    /// of the decoration come out as before for the same seed.
    public static void Dress(List<(Vector3 pos, Vector3 inward)> walls, System.Random rng, Transform parent)
    {
        Hanging = Patches = Burnt = 0;
        if (!Ready()) return;
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        foreach (var (pos, inward) in walls)
        {
            var side = Vector3.Cross(Vector3.up, inward);
            if (rng.NextDouble() < 0.5)
            {
                // The wall face is 0.30 m out from the piece's centre, its top ledge at 4.0 m; the
                // corner pillars take the last half metre at each end.
                float w = R(2.2f, 3f), h = w * Aspect(hang);
                Make(true, hang, pos + inward * 0.315f + Vector3.up * (3.98f - h / 2f) + side * R(-1f, 1f) * (1.45f - w / 2f),
                    Quaternion.LookRotation(-inward), new Vector2(rng.NextDouble() < 0.5 ? -w : w, h), parent);
            }
            if (rng.NextDouble() < 0.33)
                Carpet(pos + inward * 0.38f + side * R(-1.1f, 1.1f), R(0.9f, 1.4f), inward, (float)rng.NextDouble() * 360f, parent);
        }
    }

    /// A clump of moss on the floor, size metres across. With tuck set, it starts at that point
    /// and grows away from the wall, its back edge hidden under the skirting.
    public static MossPiece Carpet(Vector3 at, float size, Vector3? tuck, float yaw, Transform parent)
    {
        if (!Ready()) return null;
        if (tuck is Vector3 inward) at += inward * size * 0.3f;
        // A hair above the floor, each clump at its own height so overlapping ones never flicker.
        at.y = 0.012f + 0.002f * (Patches % 5);
        return Make(false, patch, at, Quaternion.Euler(90f, yaw, 0f), new Vector2(size, size * Aspect(patch)), parent);
    }

    static float Aspect(Material m) => m.mainTexture ? (float)m.mainTexture.height / m.mainTexture.width : 1f;

    static Mesh quad;

    static MossPiece Make(bool hanging, Material m, Vector3 at, Quaternion rot, Vector2 size, Transform parent)
    {
        if (!quad)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);
        }
        // The picture is a scaled child; fire and sound go on the unscaled parent.
        var root = new GameObject(m.name);
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(at, rot);
        var pic = new GameObject("picture");
        pic.transform.SetParent(root.transform, false);
        pic.transform.localScale = new Vector3(size.x, size.y, 1f);
        pic.AddComponent<MeshFilter>().sharedMesh = quad;
        var r = pic.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        var piece = root.AddComponent<MossPiece>();
        piece.hanging = hanging;
        piece.size = new Vector2(Mathf.Abs(size.x), size.y);
        piece.pic = pic.transform;
        piece.look = r;
        if (hanging) Hanging++; else Patches++;
        return piece;
    }

    /// Fire at a point, reach metres around it: catches every piece of moss it gets to.
    public static void Fire(Vector3 at, float reach)
    {
        for (int i = MossPiece.Live.Count - 1; i >= 0; i--)
        {
            var m = MossPiece.Live[i];
            if (m.state == MossPiece.State.Green && m.Reaches(at, reach)) m.Ignite();
        }
    }
}

public class MossPiece : MonoBehaviour
{
    public static readonly List<MossPiece> Live = new List<MossPiece>();
    public enum State { Green, Burning, Ash }
    public State state;
    public bool hanging;
    public Vector2 size;
    public Transform pic;
    public MeshRenderer look;

    // Hanging moss is gone in 2.2 s; a floor clump burns 3 s and leaves a patch of ash.
    float Duration => hanging ? 2.2f : 3f;
    float t, tick, spread;
    ParticleSystem flames;
    AudioSource roar;
    MaterialPropertyBlock mpb;

    void OnEnable() => Live.Add(this);
    void OnDisable() => Live.Remove(this);

    /// Hanging moss catches from fire on the floor below it (flames climb the wall) or touching it;
    /// floor moss from fire on or right over it.
    public bool Reaches(Vector3 at, float reach)
    {
        var p = transform.InverseTransformPoint(at);
        if (hanging)
            return Mathf.Abs(p.x) < size.x / 2f + reach + 0.3f && p.y > -size.y / 2f - 2f && p.y < size.y / 2f + 0.3f
                && p.z > -(reach + 1.2f) && p.z < 0.5f;
        // Lying flat: the picture's x and y are along the floor, z is up (negative).
        return new Vector2(p.x, p.y).magnitude < Mathf.Max(size.x, size.y) / 2f + reach && at.y < 1.6f;
    }

    public void Ignite()
    {
        if (state != State.Green) return;
        state = State.Burning;
        Tips.Show("moss", "青苔一点就着：用火把它点燃，站在上面的怪会着火，你自己也别站进去");
        // Bigger flames than a burning goblin's: moss goes up all at once.
        flames = Vfx.Trail(transform, "burn");
        var main = flames.main;
        main.startSizeMultiplier *= hanging ? 1.7f : 1.4f;
        var sh = flames.shape;
        var em = flames.emission;
        if (hanging)
        {
            // A box shape throws along its +z, which here is into the wall: turn it to face the room and
            // set it just in front of the moss so no flame starts behind the picture.
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(size.x * 0.85f, size.y * 0.6f, 0.05f);
            sh.rotation = new Vector3(0f, 180f, 0f);
            sh.position = new Vector3(0f, 0f, -0.12f);
            em.rateOverTime = 32f * size.x;
        }
        else
        {
            sh.radius = size.x * 0.38f;
            em.rateOverTime = 26f * size.x * size.x;
        }
        roar = Props.FireSound(gameObject, 0.55f);
    }

    void Update()
    {
        if (state != State.Burning) return;
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / Duration);
        mpb ??= new MaterialPropertyBlock();
        // Green, then glowing orange as it burns, then grey-brown ash (not black: that reads as a hole).
        var ember = new Color(0.95f, 0.5f, 0.22f);
        var c = k < 0.5f ? Color.Lerp(Color.white, ember, k / 0.5f) : Color.Lerp(ember, new Color(0.26f, 0.23f, 0.2f), (k - 0.5f) / 0.5f);
        mpb.SetColor("_BaseColor", c);
        look.SetPropertyBlock(mpb);
        if (hanging)
        {
            // Burns from the bottom up: the top stays under the ledge.
            float left = 1f - 0.85f * k;
            pic.localScale = new Vector3(pic.localScale.x, size.y * left, 1f);
            pic.localPosition = Vector3.up * size.y * (1f - left) / 2f;
        }
        else pic.localScale = new Vector3(size.x, size.y, 1f) * (1f - 0.15f * k);
        // It spreads to moss close by, once, after a moment.
        spread += Time.deltaTime;
        if (spread > 0.5f && spread < 1f)
        {
            spread = 1f;
            var side = transform.right * size.x / 2f;
            var at = hanging ? transform.position : transform.position + Vector3.up * 0.8f;
            Moss.Fire(at, 1.2f);
            if (hanging) { Moss.Fire(at + side, 1.2f); Moss.Fire(at - side, 1.2f); }
        }
        Reactions.CheckClouds(transform.position, 0.3f);
        // On the floor it burns whoever stands in it, every half second, like a fire pit.
        tick -= Time.deltaTime;
        if (!hanging && tick <= 0f)
        {
            tick = 0.5f;
            float r = size.x / 2f + 0.2f;
            var p = PlayerController.Current;
            if (p && !p.dead && Flat(p.transform.position - transform.position) < r && p.FeetHeight < 1f) p.TakeShockwave(1);
            foreach (var col in Physics.OverlapSphere(transform.position + Vector3.up * 0.5f, r, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
            {
                var e = col.GetComponentInParent<Enemy>();
                if (!e || e.dead) continue;
                e.TakeDamage(Balance.FireTrap, Vector3.up, false, 1f);
                e.ApplyBurn(2f);
            }
        }
        if (k >= 1f)
        {
            state = State.Ash;
            Moss.Burnt++;
            Vfx.Detach(flames);
            if (roar) Destroy(roar);
            if (hanging)
            {
                Vfx.Sparks(transform.position, new Color(1f, 0.55f, 0.2f), 10, 2f);
                Destroy(gameObject);
            }
        }
    }

    static float Flat(Vector3 d) => new Vector2(d.x, d.z).magnitude;
}
