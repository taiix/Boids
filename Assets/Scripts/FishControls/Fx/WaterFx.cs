using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame
{
    /// <summary>
    /// Shared underwater effects, each drawn by one world-space system for the whole scene: bubbles,
    /// the refraction shockwave of a violent bite, and splashes where a body breaks the surface. Purely
    /// visual and local - every machine plays them from what it sees, nothing is networked.
    /// The looks come from <see cref="WaterFxLibrary"/>; without it these calls do nothing.
    /// </summary>
    public static class WaterFx
    {
        const int MaxBubbles = 4000;
        const int MaxShockwaves = 64;
        const int MaxDroplets = 1500;
        const int MaxRipples = 32;
        const int SeabedLayers = 1 << 3; // Terrain

        static Transform s_root;
        static ParticleSystem s_bubbles, s_shockwaves, s_droplets, s_ripples, s_mist;

        // The simulated splashes (Blender/WaterSplash) are water about half a metre across hitting or leaving
        // the surface (WaterFxLibrary.splashFps meshes a second), cut just above the still water (3 cm, at that
        // size).
        const float SplashBodySize = 0.9f, SplashCut = 0.03f;
        const int SplashPool = 8;
        static readonly List<MeshSplash> s_splashes = new List<MeshSplash>();
        static readonly List<SurfaceDisplacement> s_displacements = new List<SurfaceDisplacement>();
        static Transform s_deformAnchor;
        static Camera s_viewCamera;
        static float s_nextCameraLookup;
        static ParticleSystem.EmitParams s_emit;
        static WaterSurface s_water;
        static float s_nextWaterLookup;
        static Mesh s_bubbleMesh;

        // ------------------------------------------------------------------ queries

        /// <summary>Height of the water surface (its resting level; waves are small next to a splash).</summary>
        public static float SurfaceY
        {
            get
            {
                if (s_water == null && Time.unscaledTime >= s_nextWaterLookup)
                {
                    s_nextWaterLookup = Time.unscaledTime + 2f;
                    s_water = Object.FindAnyObjectByType<WaterSurface>();
                }
                return s_water != null ? s_water.transform.position.y : 0f;
            }
        }

        /// <summary>Height of the water at <paramref name="p"/>, waves included (the resting level if the water
        /// can't be asked).</summary>
        public static float WaterHeightAt(Vector3 p) => WaterHeightAt(p, out _);

        /// <summary>Height of the water at <paramref name="p"/>, waves included, and which way the surface faces
        /// there (straight up if the water can't be asked). <paramref name="withDisplacement"/>: include where
        /// splashes have pushed it about (off = the sea as it would be without them).</summary>
        public static float WaterHeightAt(Vector3 p, out Vector3 normal, bool withDisplacement = true)
        {
            float flat = SurfaceY;
            normal = Vector3.up;
            if (s_water == null) return flat;
            if (!s_water.scriptInteractions) s_water.scriptInteractions = true;
            var at = new Vector3(p.x, flat, p.z);
            var search = new WaterSearchParameters
            {
                startPositionWS = at,
                targetPositionWS = at,
                error = 0.02f,
                maxIterations = 6,
                includeDeformation = withDisplacement,
                outputNormal = true,
            };
            if (!s_water.ProjectPointOnWaterSurface(search, out WaterSearchResult hit)) return flat;
            Vector3 n = hit.normalWS;
            if (n.y > 0.2f) normal = n.normalized;
            return hit.projectedPositionWS.y;
        }

        /// <summary>Top of the seabed straight below/above <paramref name="p"/>, if there is seabed there.</summary>
        public static bool SeabedHeight(Vector3 p, out float y)
        {
            bool found = SeabedAt(p, out Vector3 point, out _);
            y = point.y;
            return found;
        }

        /// <summary>The seabed straight below/above <paramref name="p"/>: where, and which way it faces.</summary>
        public static bool SeabedAt(Vector3 p, out Vector3 point, out Vector3 normal)
        {
            // From well above both the point and the water, so even a body that sank far under the seabed
            // finds its top (the surface only faces up: a ray from beneath passes straight through).
            float top = Mathf.Max(p.y + 60f, SurfaceY + 20f);
            if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out RaycastHit hit, top - p.y + 200f,
                                SeabedLayers, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            point = new Vector3(p.x, 0f, p.z);
            normal = Vector3.up;
            return false;
        }

        /// <summary>Is there seabed somewhere below <paramref name="p"/>? Always, unless it's beneath the seabed:
        /// the surface only faces up, so from under it a ray down finds nothing.</summary>
        public static bool GroundBelow(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 0.1f, Vector3.down, 500f, SeabedLayers, QueryTriggerInteraction.Ignore);

        // ------------------------------------------------------------------ bubbles

        /// <summary>One bubble: drifts with <paramref name="velocity"/> (soon slowed by the water), then rises.</summary>
        public static void Bubble(Vector3 position, Vector3 velocity, float size, float life)
        {
            if (!EnsureSystems() || position.y > SurfaceY) return;
            s_emit.position = position;
            s_emit.velocity = velocity;
            s_emit.startSize = size;
            s_emit.startLifetime = life;
            s_emit.rotation = Random.Range(0f, 360f);
            s_bubbles.Emit(s_emit, 1);
        }

        /// <summary>A burst of bubbles blown out from <paramref name="center"/>.</summary>
        public static void BubbleBurst(Vector3 center, int count, float radius, float speed, Vector2 size, Vector3 push = default)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                Bubble(center + dir * (radius * Random.value), dir * (speed * Random.Range(0.3f, 1f)) + push,
                       Random.Range(size.x, size.y), Random.Range(1.4f, 2.6f));
            }
        }

        /// <summary>The water itself buckling: a ring of refraction bursting out from <paramref name="center"/>.</summary>
        public static void Shockwave(Vector3 center, float size, float life = 0.45f)
        {
            if (!EnsureSystems() || center.y > SurfaceY) return;
            var p = new ParticleSystem.EmitParams
            {
                position = center,
                velocity = Vector3.zero,
                startSize = size,
                startLifetime = life,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            };
            s_shockwaves.Emit(p, 1);
        }

        // ------------------------------------------------------------------ splashes

        /// <summary>
        /// A body broke the surface at <paramref name="at"/> moving with <paramref name="velocity"/>: real 3D water
        /// thrown up (the crown around a dive, the column dragged up by a leap - simulated in Blender), fine
        /// streaking droplets, a ring spreading across the surface, and - diving in - a little fizz of air dragged
        /// under. <paramref name="size"/> is about the body's girth in metres, scaled up for a harder hit.
        /// </summary>
        public static void Splash(Vector3 at, Vector3 velocity, float size)
        {
            if (!EnsureSystems()) return;
            at.y = WaterHeightAt(at, out _, withDisplacement: false);
            bool diving = velocity.y < 0f;
            float speed = Mathf.Abs(velocity.y);
            float k = Mathf.Sqrt(Mathf.Max(0.3f, size));
            Vector3 drift = new Vector3(velocity.x, 0f, velocity.z) * 0.25f;

            // The simulated water itself, in 3D: the crown of a dive, or the column dragged up by a leap. Scaled
            // to the body; a bigger splash also plays slower (splashes take time in proportion to the square
            // root of their size). Its cut base sits just under the waves.
            var lib = WaterFxLibrary.Get();
            var frames = diving ? lib.entrySplash : lib.leapSplash;
            var player = frames != null && frames.Length > 0 ? FreeSplash(lib) : null;
            if (player != null)
            {
                float scale = Mathf.Clamp(size / SplashBodySize, 0.6f, 4f);
                player.Play(frames, at, scale, frames.Length / Mathf.Max(1f, lib.splashFps) * Mathf.Sqrt(scale), (SplashCut + 0.06f) * scale);
            }

            // The sea itself pushed about: a hollow (or a mound) and a ring wave spreading from it.
            var displacement = FreeDisplacement(lib);
            if (displacement != null)
            {
                KeepDeformationAround();
                displacement.Play(at, Mathf.Clamp(size, 0.5f, 4f), diving);
            }

            if (s_droplets != null)
            {
                int drops = Mathf.RoundToInt(Mathf.Lerp(40f, 140f, Mathf.Clamp01(size / 4f)));
                for (int i = 0; i < drops; i++)
                {
                    Vector2 ring = Random.insideUnitCircle;
                    Vector3 v = diving
                        // A crown: out and up all round the hole.
                        ? new Vector3(ring.x, 0f, ring.y).normalized * Random.Range(1.5f, 4f) * k + Vector3.up * Random.Range(2.5f, 6f) * k
                        // A column trailing the leap: mostly up, a little spread.
                        : new Vector3(ring.x, 0f, ring.y) * 1.8f * k + Vector3.up * Random.Range(0.35f, 0.9f) * Mathf.Max(4f, speed);
                    s_emit.position = at + new Vector3(ring.x, 0f, ring.y) * (size * 0.45f);
                    s_emit.velocity = v + drift;
                    s_emit.startSize = Random.Range(0.018f, 0.05f) * k;
                    s_emit.startLifetime = Random.Range(0.8f, 1.5f);
                    s_emit.rotation = 0f;
                    s_droplets.Emit(s_emit, 1);
                }
            }

            // Fine spray left hanging over it.
            if (s_mist != null)
            {
                int puffs = Mathf.RoundToInt(Mathf.Lerp(3f, 8f, Mathf.Clamp01(size / 4f)));
                for (int i = 0; i < puffs; i++)
                {
                    Vector2 ring = Random.insideUnitCircle * (size * 0.6f);
                    s_emit.position = at + new Vector3(ring.x, Random.Range(0.1f, 0.6f) * k, ring.y);
                    s_emit.velocity = Vector3.up * Random.Range(0.5f, 1.8f) * k + drift * 0.5f
                                      + (diving ? new Vector3(ring.x, 0f, ring.y) : Vector3.zero);
                    s_emit.startSize = Random.Range(0.8f, 1.8f) * k;
                    s_emit.startLifetime = Random.Range(0.6f, 1.2f);
                    s_emit.rotation = Random.Range(0f, 360f);
                    s_mist.Emit(s_emit, 1);
                }
            }

            if (s_ripples != null)
            {
                var p = new ParticleSystem.EmitParams { applyShapeToPosition = false, position = at + Vector3.up * 0.05f };
                p.startSize = size * 2.5f;
                p.startLifetime = 1.3f;
                s_ripples.Emit(p, 1);
                p.startSize = size * 4f;
                p.startLifetime = 1.9f;
                s_ripples.Emit(p, 1);
            }

            // Diving in drags a little fizz of air under with it.
            if (diving)
                BubbleBurst(at + Vector3.down * (size * 0.6f), Mathf.RoundToInt(Mathf.Lerp(20f, 50f, Mathf.Clamp01(size / 4f))),
                            size * 0.5f, 1f, new Vector2(0.015f, 0.05f) * k, velocity * 0.15f);
        }

        // ------------------------------------------------------------------ setup

        /// <summary>Bubbles are drawn as small spheres: an 80-triangle icosphere (radius 0.5) keeps thousands cheap.</summary>
        public static Mesh BubbleMesh
        {
            get
            {
                if (s_bubbleMesh == null) s_bubbleMesh = BuildIcosphere();
                return s_bubbleMesh;
            }
        }

        static bool EnsureSystems()
        {
            if (s_root != null) return s_bubbles != null;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
            var lib = WaterFxLibrary.Get();
            if (lib == null || lib.bubble == null) return false;

            s_root = new GameObject("Water FX").transform; // in the scene: goes with it
            s_bubbles = BuildBubbles(s_root, lib.bubble);
            s_shockwaves = lib.shockwave != null ? BuildFlipbook(s_root, "Shockwaves", lib.shockwave, MaxShockwaves, ParticleSystemRenderMode.Billboard) : null;
            s_ripples = lib.ripple != null ? BuildFlipbook(s_root, "Ripples", lib.ripple, MaxRipples, ParticleSystemRenderMode.HorizontalBillboard) : null;
            s_droplets = lib.droplet != null ? BuildDroplets(s_root, lib.droplet) : null;
            s_mist = lib.mist != null ? BuildMist(s_root, lib.mist) : null;
            s_splashes.Clear();
            s_displacements.Clear();
            s_emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            return true;
        }

        static ParticleSystem BuildBubbles(Transform parent, Material material)
        {
            var go = new GameObject("Bubbles");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxBubbles;
            main.startSpeed = 0f;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            // The water stops a thrown bubble quickly; then it floats up, wobbling.
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 100f;
            drag.drag = 2.2f;
            var rise = ps.velocityOverLifetime;
            rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.World;
            // x/y/z must share one curve mode (Unity errors otherwise), so x/z are 0..0 ranges too
            rise.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            rise.y = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            rise.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var wobble = ps.noise;
            wobble.enabled = true;
            wobble.strength = 0.35f;
            wobble.frequency = 1.4f;
            wobble.scrollSpeed = 0.6f;

            // Swell a little as they leave, then pop at the end.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(0.15f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));

            // Small sprites: a thin bright rim and a glint, nearly clear inside - silvery, like real ones.
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // Water thrown into the air: streaks along its flight, falls back under gravity, shrinks as it lands.
        static ParticleSystem BuildDroplets(Transform parent, Material material)
        {
            var go = new GameObject("Droplets");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxDroplets;
            main.startSpeed = 0f;
            main.gravityModifier = 1f;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.7f, 0.85f), new Keyframe(1f, 0f)));

            // One of the 16 rendered drops each.
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 15.99f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.025f;
            r.lengthScale = 1f;
            r.cameraVelocityScale = 0f;
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // A displacement of the sea that isn't playing (or any, when all are), from a small pool.
        static SurfaceDisplacement FreeDisplacement(WaterFxLibrary lib)
        {
            if (lib.waterCrater == null || lib.waterRing == null || s_root == null || s_water == null || !s_water.deformation)
                return null;
            foreach (var d in s_displacements)
                if (d != null && !d.Playing) return d;
            if (s_displacements.Count < SplashPool)
            {
                var d = SurfaceDisplacement.Create(s_root, lib.waterCrater, lib.waterRing);
                s_displacements.Add(d);
                return d;
            }
            return s_displacements[Random.Range(0, s_displacements.Count)];
        }

        /// <summary>The sea can only be pushed about within an area around one point (HDRP's decal region); keep
        /// it on this machine's view - by default it follows Camera.main, which the fish's camera isn't.</summary>
        public static void KeepDeformationAround()
        {
            if (s_water == null || s_root == null) return;
            if (s_deformAnchor == null)
            {
                s_deformAnchor = new GameObject("Deformation Area").transform;
                s_deformAnchor.SetParent(s_root, false);
                s_deformAnchor.gameObject.AddComponent<FollowView>();
            }
            if (s_water.decalRegionAnchor != s_deformAnchor) s_water.decalRegionAnchor = s_deformAnchor;
        }

        /// <summary>The camera drawing this machine's view: the enabled on-screen camera drawn last.</summary>
        public static Camera ViewCamera
        {
            get
            {
                if ((s_viewCamera == null || !s_viewCamera.isActiveAndEnabled) || Time.unscaledTime >= s_nextCameraLookup)
                {
                    s_nextCameraLookup = Time.unscaledTime + 1f;
                    Camera best = null;
                    foreach (var c in Camera.allCameras)
                        if (c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
                    s_viewCamera = best;
                }
                return s_viewCamera;
            }
        }

        // Keeps the sea's deformation area on the view.
        class FollowView : MonoBehaviour
        {
            void LateUpdate()
            {
                var cam = ViewCamera;
                if (cam != null) transform.position = cam.transform.position;
            }
        }

        // A splash that isn't playing (or the one that has played longest), from a small pool of 3D splashes.
        static MeshSplash FreeSplash(WaterFxLibrary lib)
        {
            if (lib.splashWater == null || s_root == null) return null;
            foreach (var sp in s_splashes)
                if (sp != null && !sp.Playing) return sp;
            if (s_splashes.Count < SplashPool)
            {
                var go = new GameObject("Splash");
                go.transform.SetParent(s_root, false);
                go.SetActive(false);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = lib.splashWater;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var sp = go.AddComponent<MeshSplash>();
                s_splashes.Add(sp);
                return sp;
            }
            return s_splashes[Random.Range(0, s_splashes.Count)];
        }

        // Spray hanging over a splash: billows out a little, slows, and thins away.
        static ParticleSystem BuildMist(Transform parent, Material material)
        {
            var go = new GameObject("Spray");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.startSpeed = 0f;
            main.gravityModifier = 0.15f;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 100f;
            drag.drag = 2.5f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // A ring played as a 4x4 flipbook over each particle's life: the bite's shockwave (facing the camera)
        // and the splash's ripple (lying on the surface).
        static ParticleSystem BuildFlipbook(Transform parent, string name, Material material, int max, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startSpeed = 0f;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            // The flipbook carries it: the ring grows across the frames while its pull fades out.
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.cycleCount = 1;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static Mesh BuildIcosphere()
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var mids = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mids.TryGetValue(key, out int i)) return i;
                v.Add((v[a] + v[b]) * 0.5f);
                mids[key] = v.Count - 1;
                return v.Count - 1;
            }
            var tris = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            var normals = new Vector3[v.Count];
            var uvs = new Vector2[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                normals[i] = v[i].normalized;
                v[i] = normals[i] * 0.5f;
                uvs[i] = new Vector2(0.5f + Mathf.Atan2(normals[i].z, normals[i].x) / (2f * Mathf.PI), 0.5f + Mathf.Asin(normals[i].y) / Mathf.PI);
            }
            var mesh = new Mesh { name = "Bubble" };
            mesh.SetVertices(v);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
