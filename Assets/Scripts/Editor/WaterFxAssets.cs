using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame.EditorTools
{
    /// <summary>
    /// Builds the assets the water effects use (FishGame > Build Water FX Assets) and fills
    /// Resources/WaterFxLibrary with them:
    ///   * ShockwaveAtlas - refraction map (flow vectors in RG, blur in B), generated here.
    ///   * ShockwaveRings - faint pale shimmer drawn with it (refraction alone vanishes against
    ///     open water: with nothing behind it there's nothing to bend).
    ///   * FxShockwave - HDRP/Unlit, additive shimmer plus distortion: the water itself warping (a bite).
    ///   * FxAllyGlow - additive, drawn through everything and unaffected by fog (fish teammates).
    ///   * FxBubble - small realistic bubbles (BubbleSprite: thin bright rim, a glint, clear middle).
    ///   * FxBubbleFar - the mesh bubble shader without fog (the exit's bubble curtain).
    ///   * FxDroplet, FxSpray, FxRipple - a splash's flying water (stretched along its flight), the fine spray
    ///     hanging over it, and the ring it spreads across the surface.
    ///   * FxSplashWater + SplashMeshes - the splashes themselves: real 3D water, one mesh per frame of a
    ///     Mantaflow liquid simulation (Blender/WaterSplash: make_splash.py, export_meshes.py), drawn with a
    ///     refractive water material. The drops are rendered there too (Cycles) and copied in.
    ///   * FxWaterCrater, FxWaterRing - HDRP water decals (deformation) that push the ocean itself about where a
    ///     body breaks through: a smooth hollow and a ring wave (WaterCraterShape / WaterRingShape: heights).
    /// Real assets rather than materials made at runtime, so their shader variants are in builds. Safe to run
    /// again: it updates what's there.
    /// </summary>
    public static class WaterFxAssets
    {
        const string Root = "Assets/VFX";
        const string Textures = Root + "/Textures";
        const string Materials = Root + "/Materials";
        const string LibraryPath = "Assets/Resources/WaterFxLibrary.asset";
        const string BubblePath = "Assets/Particles/Bubbles/Bubble.mat";
        const string BlenderRenders = "Blender/WaterSplash";   // next to Assets, outside the project's assets
        const string Meshes = Root + "/Meshes";
        // The splash meshes: simulated at 60 fps (make_splash.py dive dive60 160 mesh 60, then export_meshes.py).
        const string SplashMeshesSource = BlenderRenders + "/dive60/SplashMeshes.fbx";
        const float SplashMeshesFps = 60f;
        // HDRP's water decal graph that can deform the water by a texture (what its old Texture deformer became).
        const string WaterDecalShader =
            "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/ShaderGraph/Water Decal Sample.shadergraph";

        [MenuItem("FishGame/Build Water FX Assets")]
        public static void Build()
        {
            Folder("Assets", "VFX");
            Folder(Root, "Textures");
            Folder(Root, "Materials");
            Folder(Root, "Meshes");

            var ringMap = SaveTexture(Textures + "/ShockwaveAtlas.png", ShockwaveAtlas(128, colour: false), linear: true);
            var ringLook = SaveTexture(Textures + "/ShockwaveRings.png", ShockwaveAtlas(128, colour: true), linear: false);

            var shockwave = Distortion(Materials + "/FxShockwave.mat", ringMap, ringLook, new Color(0.6f, 0.85f, 0.9f) * 0.45f, strength: 1.6f, blur: 0.5f);
            var glow = AllyGlow(Materials + "/FxAllyGlow.mat");
            var bubbleSprite = SaveTexture(Textures + "/BubbleSprite.png", BubbleSprite(64), linear: false);
            var dropSprite = SaveTexture(Textures + "/DropletSprite.png", DropletSprite(32), linear: false);
            var spraySprite = SaveTexture(Textures + "/SpraySprite.png", SpraySprite(64), linear: false);

            var bubble = Bubble(Materials + "/FxBubble.mat", bubbleSprite);
            // Rendered water from Blender where there is some; the drawn droplet otherwise.
            var dropRender = CopyIn(BlenderRenders + "/droplets/Droplets.png", Textures + "/WaterDroplets.png");
            var droplet = dropRender != null
                ? Rendered(Materials + "/FxDroplet.mat", dropRender, 1.3f)
                : LitSprite(Materials + "/FxDroplet.mat", dropSprite, new Color(0.88f, 0.96f, 1f, 0.9f), 0.95f);
            var splashWater = SplashWater(Materials + "/FxSplashWater.mat");
            var splashMeshes = CopyInMeshes(SplashMeshesSource, Meshes + "/SplashMeshes.fbx");
            var spray = LitSprite(Materials + "/FxSpray.mat", spraySprite, new Color(0.95f, 0.98f, 1f, 0.16f), 0.3f);
            var ripple = Ripple(Materials + "/FxRipple.mat", ringLook);
            var craterShape = SaveHeights(Textures + "/WaterCraterShape.asset", 128, CraterHeight);
            var ringShape = SaveHeights(Textures + "/WaterRingShape.asset", 256, RingHeight);
            var waterCrater = WaterDecalMaterial(Materials + "/FxWaterCrater.mat", craterShape);
            var waterRing = WaterDecalMaterial(Materials + "/FxWaterRing.mat", ringShape);

            var meshBubble = AssetDatabase.LoadAssetAtPath<Material>(BubblePath);
            Material bubbleFar = null;
            if (meshBubble != null)
            {
                bubbleFar = Upsert(Materials + "/FxBubbleFar.mat", meshBubble.shader);
                bubbleFar.CopyPropertiesFromMaterial(meshBubble);
                bubbleFar.SetFloat("_EnableFogOnTransparent", 0f);
                HDMaterial.ValidateMaterial(bubbleFar);
                EditorUtility.SetDirty(bubbleFar);
            }

            var lib = AssetDatabase.LoadAssetAtPath<WaterFxLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<WaterFxLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            lib.bubble = bubble;
            lib.bubbleFar = bubbleFar;
            lib.shockwave = shockwave;
            lib.allyGlow = glow;
            lib.droplet = droplet;
            lib.splashWater = splashWater;
            lib.entrySplash = Frames(splashMeshes, "Entry_");
            lib.leapSplash = Frames(splashMeshes, "Leap_");
            lib.splashFps = SplashMeshesFps;
            lib.waterCrater = waterCrater;
            lib.waterRing = waterRing;
            lib.mist = spray;
            lib.ripple = ripple;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WaterFx] Built water FX assets (curtain bubble {(meshBubble != null)}, splash meshes " +
                      $"{lib.entrySplash.Length} in / {lib.leapSplash.Length} out).");
        }

        // ------------------------------------------------------------------ materials

        static Material Distortion(string path, Texture2D map, Texture2D look, Color tint, float strength, float blur)
        {
            var m = Upsert(path, Shader.Find("HDRP/Unlit"));
            m.SetFloat("_SurfaceType", 1f);               // transparent
            m.SetFloat("_BlendMode", 1f);                 // additive: a pale shimmer, never a dark smear
            m.SetColor("_UnlitColor", tint);
            m.SetTexture("_UnlitColorMap", look);
            m.SetFloat("_DistortionEnable", 1f);
            m.SetFloat("_DistortionOnly", 0f);
            m.SetFloat("_DistortionDepthTest", 1f);
            m.SetFloat("_DistortionBlendMode", 0f);       // add
            m.SetTexture("_DistortionVectorMap", map);
            m.SetFloat("_DistortionVectorScale", 2f);     // stored 0..1 -> -1..1
            m.SetFloat("_DistortionVectorBias", -1f);
            m.SetFloat("_DistortionScale", strength);
            m.SetFloat("_DistortionBlurScale", blur);
            m.SetFloat("_DistortionBlurRemapMin", 0f);
            m.SetFloat("_DistortionBlurRemapMax", 1f);
            m.SetFloat("_EnableFogOnTransparent", 1f);   // the shimmer fades into the murk like everything else
            m.SetFloat("_CullMode", 0f);                  // both sides
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material AllyGlow(string path)
        {
            var m = Upsert(path, Shader.Find("HDRP/Unlit"));
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 1f);                 // additive: a glow, never a dark shape
            m.SetColor("_UnlitColor", new Color(0.12f, 0.75f, 0.62f, 1f) * 0.65f);
            m.SetFloat("_ZTestTransparent", (float)CompareFunction.Always); // through rocks and other fish
            m.SetFloat("_TransparentZWrite", 0f);
            m.SetFloat("_EnableFogOnTransparent", 0f);   // through the murk
            m.SetFloat("_DistortionEnable", 0f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Underwater bubbles catch the light like little mirrors: silvery, unlit (they'd go dark in the deep
        // otherwise), faded by the murk with distance like everything else.
        static Material Bubble(string path, Texture2D sprite)
        {
            var m = Upsert(path, Shader.Find("HDRP/Unlit"));
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 0f);                 // alpha: the sprite's shape
            m.SetColor("_UnlitColor", new Color(0.95f, 1.05f, 1.15f, 1f));
            m.SetTexture("_UnlitColorMap", sprite);
            m.SetFloat("_DistortionEnable", 0f);
            m.SetFloat("_EnableFogOnTransparent", 1f);
            m.SetFloat("_CullMode", 0f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Water rendered in Blender: its light is already in the picture (the sky bent through it, the sun's
        // glints), so it's drawn as it is, brightened to sit in the day-lit scene.
        static Material Rendered(string path, Texture2D render, float brightness)
        {
            var m = Upsert(path, Shader.Find("HDRP/Unlit"));
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 0f);                 // alpha: clear water stays see-through
            m.SetColor("_UnlitColor", new Color(brightness, brightness, brightness, 1f));
            m.SetTexture("_UnlitColorMap", render);
            m.SetFloat("_DistortionEnable", 0f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_CullMode", 0f);
            m.SetFloat("_EnableFogOnTransparent", 1f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Thrown-up water: mostly clear, with the sky and sun glinting off it at full strength (specular is kept
        // whatever the alpha). No refraction: HDRP's refraction can't see the ocean surface, so it showed the
        // fog under it - a bright cyan shape.
        static Material SplashWater(string path)
        {
            var m = Upsert(path, Shader.Find("HDRP/Lit"));
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 0f);
            m.SetColor("_BaseColor", new Color(0.85f, 0.94f, 0.97f, 0.13f));
            m.SetFloat("_Smoothness", 0.96f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_RefractionModel", 0f);
            m.SetFloat("_EnableBlendModePreserveSpecularLighting", 1f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_CullMode", 0f);
            m.SetFloat("_EnableFogOnTransparent", 1f);
            m.SetFloat("_TransparentZWrite", 0f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // A water decal pushing the ocean up by shape (heights in metres for an amplitude of 1; the decal's
        // amplitude and size are animated in game). Deformation only, no foam.
        static Material WaterDecalMaterial(string path, Texture2D shape)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(WaterDecalShader);
            if (shader == null)
            {
                Debug.LogWarning("[WaterFx] HDRP's water decal graph wasn't found: no water displacement.");
                return null;
            }
            var m = Upsert(path, shader);
            m.SetFloat("_TYPE", 4f);                       // Texture
            foreach (var k in new[] { "_TYPE_SPHERE", "_TYPE_BOX", "_TYPE_BOW_WAVE", "_TYPE_SHORE_WAVE" })
                m.DisableKeyword(k);
            m.EnableKeyword("_TYPE_TEXTURE");
            m.SetTexture("_Deformation_Texture", shape);
            m.SetFloat("_Remap_Min", 0f);                  // the heights as they are (signed)
            m.SetFloat("_Remap_Max", 1f);
            m.SetFloat("_AffectDeformation", 1f);
            m.SetFloat("_AffectFoam", 0f);
            MaterialEditor.ApplyMaterialPropertyDrawers(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // A smooth hollow (or hump): 1 in the middle easing to 0 at the edge.
        static float CraterHeight(float r) => r >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(Mathf.PI * r);

        // A ring wave: a crest at 60% out with a trough just inside it and a smaller one outside, nothing in
        // the middle or at the edge.
        static float RingHeight(float r)
        {
            float Bell(float at, float width) => Mathf.Exp(-(r - at) * (r - at) / (width * width));
            float h = Bell(0.6f, 0.09f) - 0.55f * Bell(0.43f, 0.08f) - 0.25f * Bell(0.76f, 0.06f);
            return h * (1f - Mathf.SmoothStep(0f, 1f, (r - 0.85f) / 0.15f));
        }

        // Heights by distance from the middle (0..1 at the edge) as a float texture, so they can go below zero
        // exactly. Kept as the same asset when rebuilt.
        static Texture2D SaveHeights(string path, int n, System.Func<float, float> height)
        {
            var tex = new Texture2D(n, n, TextureFormat.RHalf, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = Path.GetFileNameWithoutExtension(path),
            };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    tex.SetPixel(x, y, new Color(height(Mathf.Sqrt(u * u + v * v)), 0f, 0f, 1f));
                }
            tex.Apply();
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(tex, path);
                return tex;
            }
            EditorUtility.CopySerialized(tex, existing);
            Object.DestroyImmediate(tex);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // The simulated splash meshes copied into the project (the copy already there if they haven't been
        // exported here): just the shapes - no materials, cameras or animation - with the normals the simulation
        // exported.
        static GameObject CopyInMeshes(string source, string destination)
        {
            string from = Path.GetFullPath(Path.Combine(Application.dataPath, "..", source));
            if (!File.Exists(from)) return AssetDatabase.LoadAssetAtPath<GameObject>(destination);
            File.Copy(from, destination, overwrite: true);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(destination);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true;                  // the game moves their vertices between frames
            importer.addCollider = false;
            importer.bakeAxisConversion = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(destination);
        }

        // The meshes of the parts named prefix00, prefix01, ... in order.
        static Mesh[] Frames(GameObject model, string prefix)
        {
            if (model == null) return new Mesh[0];
            var frames = new System.Collections.Generic.List<(string, Mesh)>();
            foreach (var f in model.GetComponentsInChildren<MeshFilter>(true))
                if (f.name.StartsWith(prefix) && f.sharedMesh != null) frames.Add((f.name, f.sharedMesh));
            frames.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return frames.ConvertAll(f => f.Item2).ToArray();
        }

        // A render from Blender/WaterSplash copied into the project as a texture (null if it hasn't been made).
        static Texture2D CopyIn(string source, string destination)
        {
            string from = Path.GetFullPath(Path.Combine(Application.dataPath, "..", source));
            if (!File.Exists(from)) return null;
            File.Copy(from, destination, overwrite: true);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(destination);
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(destination);
        }

        // Water in the air: lit by the sky and sun like the surface it came from, shaped by the sprite's alpha.
        // Its reflections fade with the alpha too: kept at full strength, the sky glinting off the whole quad
        // showed each sprite as a flat square turning to face the camera.
        static Material LitSprite(string path, Texture2D sprite, Color colour, float smoothness)
        {
            var m = Upsert(path, Shader.Find("HDRP/Lit"));
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 0f);
            m.SetFloat("_EnableBlendModePreserveSpecularLighting", 0f);
            m.SetColor("_BaseColor", colour);
            m.SetTexture("_BaseColorMap", sprite);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_EnableFogOnTransparent", 1f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // The shockwave's pale ring, laid flat on the water and seen from above or below.
        static Material Ripple(string path, Texture2D rings)
        {
            var m = Upsert(path, Shader.Find("HDRP/Unlit"));
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 1f);                 // additive
            m.SetColor("_UnlitColor", new Color(0.85f, 0.95f, 1f) * 0.25f);
            m.SetTexture("_UnlitColorMap", rings);
            m.SetFloat("_DistortionEnable", 0f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_CullMode", 0f);
            m.SetFloat("_EnableFogOnTransparent", 1f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Upsert(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        // ------------------------------------------------------------------ refraction maps

        // A small bubble seen up close: a thin bright rim (the light bending round its edge), a glint where it
        // catches the light, a second fainter one opposite, and a nearly clear middle.
        static Texture2D BubbleSprite(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false);
            var glint = new Vector2(-0.38f, 0.38f);
            var back = new Vector2(0.32f, -0.4f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f) * 2f;
                    float d = p.magnitude;
                    float inside = Mathf.Clamp01((1f - d) / 0.08f);
                    float rim = Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.1f, 2f));
                    float body = 0.06f + 0.12f * d * d;
                    float g1 = Mathf.Exp(-(p - glint).sqrMagnitude / 0.012f);
                    float g2 = 0.35f * Mathf.Exp(-(p - back).sqrMagnitude / 0.008f);
                    float a = Mathf.Clamp01(rim * 0.8f + body + g1 + g2) * inside;
                    float white = Mathf.Clamp01(g1 + g2 + rim * 0.4f);
                    tex.SetPixel(x, y, new Color(Mathf.Lerp(0.8f, 1f, white), Mathf.Lerp(0.92f, 1f, white), 1f, a));
                }
            tex.Apply();
            return tex;
        }

        // A drop of water: a soft disc with a bright core; stretched along its flight it reads as a streak.
        static Texture2D DropletSprite(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f) * 2f;
                    float d = p.magnitude;
                    float a = Mathf.Clamp01(Mathf.SmoothStep(1f, 0.45f, d) * 0.85f + Mathf.Exp(-d * d / 0.05f) * 0.3f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return tex;
        }

        // A wisp of spray: soft and uneven, thin at the edges.
        static Texture2D SpraySprite(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f) * 2f;
                    float d = p.magnitude;
                    float grain = 0.7f + 0.3f * Mathf.PerlinNoise(x * 0.18f, y * 0.18f);
                    float a = Mathf.Clamp01(Mathf.Exp(-d * d * 5f) * grain * (1f - Mathf.SmoothStep(0.8f, 1f, d)));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return tex;
        }

        // 4x4 frames (the first top-left): a ring of warped water racing outward and fading as it goes. The
        // profile pushes outward past the ring and inward inside it, which reads as a lens of bent water.
        // colour = true draws the same ring as a pale glow instead of flow vectors.
        static Texture2D ShockwaveAtlas(int frame, bool colour)
        {
            int size = frame * 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !colour);
            for (int k = 0; k < 16; k++)
            {
                float f = k / 15f;
                float radius = 0.12f + 0.78f * f, width = 0.10f + 0.12f * f, amp = Mathf.Pow(1f - f, 1.6f);
                int col = k % 4, row = k / 4;
                int ox = col * frame, oy = (3 - row) * frame;
                for (int y = 0; y < frame; y++)
                    for (int x = 0; x < frame; x++)
                    {
                        var p = new Vector2((x + 0.5f) / frame - 0.5f, (y + 0.5f) / frame - 0.5f) * 2f; // -1..1
                        float d = p.magnitude;
                        Vector2 dir = d > 1e-4f ? p / d : Vector2.zero;
                        float s = (d - radius) / width;
                        float bell = Mathf.Exp(-s * s);
                        float push = amp * s * bell * 2.3f;
                        float edge = Mathf.Clamp01((1f - d) / 0.1f); // nothing at the quad's border
                        Vector2 vec = dir * push * edge;
                        float glow = amp * bell * edge;
                        tex.SetPixel(ox + x, oy + y, colour
                            ? new Color(glow, glow, glow, 1f)
                            : new Color(0.5f + 0.5f * vec.x, 0.5f + 0.5f * vec.y, amp * bell * 0.4f * edge, 1f));
                    }
            }
            tex.Apply();
            return tex;
        }

        static Texture2D SaveTexture(string path, Texture2D tex, bool linear)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = !linear;               // flow vectors are data; the shimmer is a colour
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            // Flow maps carry no alpha; the sprites (bubbles, drops, spray) are nothing but their alpha.
            importer.alphaSource = linear ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = !linear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
