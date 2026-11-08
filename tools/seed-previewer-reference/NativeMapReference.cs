using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;
using BC = BetterContinents.BetterContinents;

namespace SeedPreviewerReference
{
    internal static class NativeMapReference
    {
        const string TestWorld = "SPF04C1";
        const string TestSeed = "AbC123";
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static IEnumerator Capture(Action finished, Action<string> error)
        {
            try
            {
                yield return new WaitForEndOfFrame();
                try
                {
                    string id = CaptureFrame();
                    global::Console.instance?.Print("Native map reference captured: " + id);
                }
                catch (Exception ex)
                {
                    error(ex.ToString());
                    global::Console.instance?.Print("Native map capture failed: " + ex.Message);
                }
            }
            finally
            {
                finished();
            }
        }

        static string CaptureFrame()
        {
            var map = Minimap.instance;
            var generator = WorldGenerator.instance;
            var world = generator?.m_world;
            if (map == null || world == null || world.m_name != TestWorld ||
                world.m_seedName != TestSeed || !BC.Settings.ShapesWorld)
                throw new InvalidOperationException("Load the disposable SPF04C1 world with seed AbC123.");
            if (!BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException("The height export requires little-endian floats.");

            var view = ReadField<RawImage>(map, "m_mapImageLarge");
            if (view == null || !view.isActiveAndEnabled || view.canvasRenderer.materialCount == 0)
                throw new InvalidOperationException("Open the large map with M before running this command.");
            var material = view.canvasRenderer.GetMaterial(0);
            if (material == null || material.shader.name != "Custom/mapshader" || !material.shader.isSupported ||
                material.passCount != 1)
                throw new InvalidOperationException("The expected supported single-pass map material is not bound.");

            var main = GetLayer(map, material, "m_mapTexture", "_MainTex");
            var mask = GetLayer(map, material, "m_forestMaskTexture", "_MaskTex");
            var height = GetLayer(map, material, "m_heightTexture", "_HeightTex");
            var fog = GetLayer(map, material, "m_fogTexture", "_FogTex");
            int size = ReadField<int>(map, "m_textureSize");
            float metresPerTexel = ReadField<float>(map, "m_pixelSize");
            if (size < 64 || size > 4096 || metresPerTexel <= 0 ||
                float.IsNaN(metresPerTexel) || float.IsInfinity(metresPerTexel))
                throw new InvalidDataException("Unsupported map texture geometry.");
            foreach (var layer in new[] { main, mask, height, fog })
                if (layer.width != size || layer.height != size || !layer.isReadable)
                    throw new InvalidDataException("Map inputs must be readable and match the map grid.");

            string id = Guid.NewGuid().ToString("N");
            string root = Path.Combine(Paths.BepInExRootPath, "seed-previewer-native-map");
            string stage = Path.Combine(root, "." + id + ".partial");
            Directory.CreateDirectory(stage);
            var outputFiles = new Dictionary<string, string>();
            var clock = new
            {
                frame = Time.frameCount,
                time = Time.time,
                timeSinceLevelLoad = Time.timeSinceLevelLoad,
                deltaTime = Time.deltaTime,
                smoothDeltaTime = Time.smoothDeltaTime
            };
            var uniforms = ReadUniforms(material);
            var uv = view.uvRect;
            var mapTextures = new Dictionary<string, object>();
            foreach (string name in new[]
            {
                "_MainTex", "_MaskTex", "_HeightTex", "_FogTex", "_BackgroundTex", "_FogLayerTex",
                "_WaterTex", "_MountainTex", "_ForestTex", "_lavaTex", "_SpaceTex", "_CloudTex"
            })
            {
                var texture = material.GetTexture(name) as Texture2D;
                if (texture == null)
                    throw new InvalidDataException("Required map texture is unassigned: " + name);
                mapTextures.Add(name, TextureInfo(texture));
            }

            var clone = new Material(material);
            object render;
            int renderSize = Math.Min(size * 2, 4096);
            try
            {
                clone.SetTextureScale("_MainTex", Vector2.one);
                clone.SetTextureOffset("_MainTex", Vector2.zero);
                clone.SetFloat("_StencilComp", (float)CompareFunction.Always);
                render = RenderPair(stage, main, clone, renderSize);
            }
            finally
            {
                UnityEngine.Object.Destroy(clone);
            }

            WriteRgba(Path.Combine(stage, "main.rgba8"), main);
            WriteRgba(Path.Combine(stage, "mask.rgba8"), mask);
            WriteRgba(Path.Combine(stage, "fog.rgba8"), fog);
            WriteHeight(Path.Combine(stage, "height.f32"), height);
            var settings = new List<string>();
            BC.Settings.Dump(settings.Add);
            File.WriteAllLines(Path.Combine(stage, "effective-settings.txt"), settings);
            foreach (string file in new[]
            {
                "native-with-fog.png", "native-terrain.png", "main.rgba8", "mask.rgba8",
                "fog.rgba8", "height.f32", "effective-settings.txt"
            })
                outputFiles.Add(file, Hash(Path.Combine(stage, file)));

            File.WriteAllText(Path.Combine(stage, "manifest.json"), ReferenceJson.Serialize(new
            {
                format = "sp-native-map-reference/1",
                status = "complete",
                seed = TestSeed,
                caseId = "Continents-AbC123",
                gameSha256 = Hash(typeof(World).Assembly.Location),
                coreSha256 = Hash(typeof(Color).Assembly.Location),
                modSha256 = Hash(typeof(BC).Assembly.Location),
                probeSha256 = Hash(typeof(NativeMapReference).Assembly.Location),
                gameVersion = Application.version,
                unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsUVStartsAtTop = SystemInfo.graphicsUVStartsAtTop,
                colorSpace = QualitySettings.activeColorSpace.ToString(),
                shader = material.shader.name,
                keywords = material.shaderKeywords,
                size,
                metresPerTexel,
                spanMetres = size * (double)metresPerTexel,
                centre = new[] { 0f, 0f },
                layerOrder = "south-first texel centres; east +X, north +Z",
                renderSize,
                render,
                liveUiUv = new[] { uv.x, uv.y, uv.width, uv.height },
                clock,
                uniforms,
                mapTextures,
                files = outputFiles
            }));
            Directory.Move(stage, Path.Combine(root, id));
            return id;
        }

        static T ReadField<T>(Minimap map, string name)
        {
            var field = typeof(Minimap).GetField(name, Fields) ?? throw new MissingFieldException(name);
            return (T)field.GetValue(map);
        }

        static Texture2D GetLayer(Minimap map, Material material, string field, string property)
        {
            var texture = ReadField<Texture2D>(map, field);
            if (texture == null || material.GetTexture(property) != texture)
                throw new InvalidDataException("Bound material does not use the expected map layer: " + property);
            return texture;
        }

        static object TextureInfo(Texture2D texture) => new
        {
            width = texture.width,
            height = texture.height,
            format = texture.format.ToString(),
            graphicsFormat = texture.graphicsFormat.ToString(),
            srgb = GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat),
            filter = texture.filterMode.ToString(),
            wrapU = texture.wrapModeU.ToString(),
            wrapV = texture.wrapModeV.ToString(),
            mipCount = texture.mipmapCount,
            mipBias = texture.mipMapBias,
            aniso = texture.anisoLevel
        };

        static object ReadUniforms(Material material)
        {
            var values = new Dictionary<string, object>();
            foreach (string name in new[]
            {
                "_zoom", "_pixelSize", "_normalWidth", "_normalIntensity", "_SharedFade"
            })
                values.Add(name, new
                {
                    material = material.HasFloat(name) ? (float?)material.GetFloat(name) : null,
                    globalApiRead = Shader.GetGlobalFloat(name)
                });
            foreach (string name in new[]
            {
                "_mapCenter", "_CloudOffset", "_SunDir", "_SunColor", "_SunFogColor",
                "_AmbientColor", "_Time", "_lightColor", "_ambientLightColor",
                "_ForestColor", "_WaterColor", "_WaterColorDeep", "_WaterColorAshlands",
                "_WaterColorAshlandsDeep", "_lavaColor1", "_lavaColor2"
            })
                values.Add(name, new
                {
                    material = material.HasColor(name) ? Components(material.GetColor(name))
                        : material.HasVector(name) ? Components(material.GetVector(name)) : null,
                    globalApiRead = Components(Shader.GetGlobalVector(name))
                });
            // Global API reads may return defaults for uniforms managed only by the engine.
            return values;
        }

        static float[] Components(Vector4 v) => new[] { v.x, v.y, v.z, v.w };

        static object RenderPair(string stage, Texture2D main, Material clone, int size)
        {
            var previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var mode = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
            var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, mode);
            Texture2D pixels = null;
            try
            {
                pixels = new Texture2D(size, size, TextureFormat.RGBA32, false, !target.sRGB);
                RenderToPng(Path.Combine(stage, "native-with-fog.png"), main, clone, target, pixels);
                clone.SetTexture("_FogTex", Texture2D.blackTexture);
                RenderToPng(Path.Combine(stage, "native-terrain.png"), main, clone, target, pixels);
                return new
                {
                    kind = "native offscreen material pass 0",
                    uv = "full map, identity MainTex scale and offset",
                    stencil = "Always on clone for the offscreen target",
                    terrainFogOverride = "black texture on clone only; zero RG exploration mask",
                    graphicsFormat = target.graphicsFormat.ToString(),
                    srgb = target.sRGB,
                    readback = "RGBA32 PNG via ReadPixels, no manual gamma transform"
                };
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                RenderTexture.ReleaseTemporary(target);
                if (pixels != null)
                    UnityEngine.Object.Destroy(pixels);
            }
        }

        static void RenderToPng(string file, Texture main, Material clone, RenderTexture target, Texture2D pixels)
        {
            RenderTexture.active = target;
            GL.sRGBWrite = target.sRGB;
            GL.Clear(false, true, Color.clear);
            Graphics.Blit(main, target, clone, 0);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            var centre = pixels.GetPixel(target.width / 2, target.height / 2);
            var corner = pixels.GetPixel(target.width / 8, target.height / 8);
            if (centre.maxColorComponent == 0 && corner.maxColorComponent == 0 &&
                pixels.GetPixel(target.width / 2, target.height * 3 / 4).maxColorComponent == 0)
                throw new InvalidDataException("The isolated map render appears blank.");
            var png = ImageConversion.EncodeToPNG(pixels);
            if (png == null || png.Length == 0)
                throw new IOException("PNG encoding failed.");
            File.WriteAllBytes(file, png);
        }

        static void WriteRgba(string file, Texture2D texture)
        {
            var pixels = texture.GetPixels32(0);
            var bytes = new byte[checked(pixels.Length * 4)];
            for (int i = 0, j = 0; i < pixels.Length; i++)
            {
                bytes[j++] = pixels[i].r;
                bytes[j++] = pixels[i].g;
                bytes[j++] = pixels[i].b;
                bytes[j++] = pixels[i].a;
            }
            File.WriteAllBytes(file, bytes);
        }

        static void WriteHeight(string file, Texture2D texture)
        {
            var pixels = texture.GetPixels(0);
            var heights = new float[pixels.Length];
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            for (int i = 0; i < heights.Length; i++)
            {
                float value = pixels[i].r;
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidDataException("The map height layer contains non-finite samples.");
                heights[i] = value;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
            if (minimum >= 29.5f || maximum <= 70f)
                throw new InvalidDataException("The map height layer lacks the expected coastal and mountain range.");
            var bytes = new byte[checked(heights.Length * sizeof(float))];
            Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(file, bytes);
        }

        static string Hash(string file)
        {
            using (var stream = File.OpenRead(file))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
