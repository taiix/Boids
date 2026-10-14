using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame
{
    /// <summary>
    /// The ocean itself pushed about where a body broke through it (WaterFx.Splash keeps a few): a crater that
    /// opens as a body dives in and rebounds as the water rushes back - or a mound dragged up by a body leaping
    /// out, collapsing behind it - and a ring wave spreading out across the surface. Drawn as HDRP water decals
    /// (deformation), so the real water moves: its waves, reflections and foam ride over it.
    /// </summary>
    public class SurfaceDisplacement : MonoBehaviour
    {
        WaterDecal _hole, _ring;
        float _time, _size, _pace;
        bool _diving;

        public bool Playing => gameObject.activeSelf;

        /// <summary>Make one: two water decals drawn with the crater and ring shapes.</summary>
        public static SurfaceDisplacement Create(Transform parent, Material crater, Material ring)
        {
            var go = new GameObject("Displacement");
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            var d = go.AddComponent<SurfaceDisplacement>();
            d._hole = Decal(go.transform, "Crater", crater);
            d._ring = Decal(go.transform, "Ring", ring);
            return d;
        }

        static WaterDecal Decal(Transform parent, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var decal = go.AddComponent<WaterDecal>();
            decal.scaleMode = DecalScaleMode.ScaleInvariant;
            decal.material = material;
            decal.resolution = new Vector2Int(128, 128);
            decal.updateMode = CustomRenderTextureUpdateMode.OnLoad;
            decal.amplitude = 0f;
            return decal;
        }

        /// <summary>Displace the water at <paramref name="at"/> for a body about <paramref name="size"/> metres
        /// across going in (<paramref name="diving"/>) or out.</summary>
        public void Play(Vector3 at, float size, bool diving)
        {
            transform.SetPositionAndRotation(new Vector3(at.x, at.y, at.z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            _size = Mathf.Max(0.3f, size);
            _diving = diving;
            // Bigger disturbances take longer (like the splashes: with the square root of their size).
            _pace = 1f / Mathf.Sqrt(Mathf.Max(0.5f, _size));
            _time = 0f;
            Step();
            gameObject.SetActive(true);
        }

        void Update()
        {
            _time += Time.deltaTime;
            if (!Step()) gameObject.SetActive(false);
        }

        // Shape the water for the time so far; false once it has settled.
        bool Step()
        {
            float t = _time * _pace;     // seconds for a body about a metre across
            float s = _size;

            // The hole (or mound) right where it went through: about the body's width.
            float hole;
            if (_diving)
            {
                // Punched down fast, the water rushing back in overshoots into a hump, then settles.
                hole = t < 0.12f ? -Mathf.SmoothStep(0f, 1f, t / 0.12f)
                     : t < 0.5f ? Mathf.Lerp(-1f, 0.35f, Mathf.SmoothStep(0f, 1f, (t - 0.12f) / 0.38f))
                     : t < 1.3f ? Mathf.Lerp(0.35f, 0f, Mathf.SmoothStep(0f, 1f, (t - 0.5f) / 0.8f))
                     : 0f;
                hole *= 0.32f * s;
            }
            else
            {
                // Dragged up with the body, then falling back below the level and settling.
                hole = t < 0.15f ? Mathf.SmoothStep(0f, 1f, t / 0.15f)
                     : t < 0.55f ? Mathf.Lerp(1f, -0.45f, Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.4f))
                     : t < 1.3f ? Mathf.Lerp(-0.45f, 0f, Mathf.SmoothStep(0f, 1f, (t - 0.55f) / 0.75f))
                     : 0f;
                hole *= 0.22f * s;
            }
            _hole.amplitude = hole;
            _hole.regionSize = Vector2.one * (2.4f * s);

            // The ring wave racing outward, flattening as it spreads.
            const float RingTime = 1.9f;
            float r = Mathf.Clamp01(t / RingTime);
            float radius = Mathf.Lerp(0.7f, 6f, 1f - (1f - r) * (1f - r)) * s;
            float strength = (_diving ? 0.16f : 0.11f) * s * Mathf.SmoothStep(0f, 1f, t / 0.1f) * (1f - r) * (1f - r)
                             / Mathf.Sqrt(radius / s);
            _ring.amplitude = strength;
            // The ring's crest sits at 60% of the decal's half-size.
            _ring.regionSize = Vector2.one * (2f * radius / 0.6f);

            return t < Mathf.Max(1.3f, RingTime);
        }
    }
}
