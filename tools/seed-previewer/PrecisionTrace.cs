#nullable disable // Shared with the net48 in-game probe; null denotes an absent mask or optional final callback.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BC = BetterContinents.BetterContinents;

namespace SeedPreviewerDiagnostics
{
    // Shared observer for CoreCLR, standalone Mono and the in-game probe. Never patches generation.
    public static class PrecisionTrace
    {
        public static readonly float[][] Points = {
            new[] {-5344f,3744f}, new[] {-5332f,3787f}, new[] {-5331f,3787f}, new[] {-5330f,3787f}, new[] {-5343f,3744f}, new[] {-5342f,3744f},
            new[] {31f,-16f}, new[] {30f,-11f}, new[] {27f,-10f}, new[] {23f,-13f}, new[] {-29f,-31f}, new[] {-26f,-31f},
            new[] {992f,-455f}, new[] {1003f,-424f}, new[] {1033f,-428f}, new[] {996f,-449f}, new[] {1009f,-480f}, new[] {1012f,-480f}
        };
        const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        public static object Float(float value) => new { value, bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("X8") };
        static object Field(object value, string name) => (value.GetType().GetField(name, Fields)
            ?? throw new MissingFieldException(value.GetType().FullName, name)).GetValue(value);

        public static object Capture(Func<float, float, float> actualBase)
        {
            if (BC.Settings.HasHeightMap) throw new NotSupportedException("Precision trace currently requires the shipped procedural preset without a heightmap.");
            var stack = BC.WorldGeneratorPatch.BaseHeightNoise;
            if (stack == null) throw new InvalidOperationException("World noise is not initialized.");
            var samples = new List<object>();
            foreach (var point in Points)
            {
                float x = point[0], z = point[1], input = 0;
                var layers = new List<object>();
                for (int index = 0; index < stack.layers.Count; index++)
                {
                    var layer = stack.layers[index];
                    float output = layer.Apply(x, z, input);
                    layers.Add(new {
                        index, input = Float(input), output = Float(output),
                        noise = Noise(layer.noise, x, z),
                        mask = layer.mask.HasValue ? Noise(layer.mask.Value, x, z) : null
                    });
                    input = output;
                }
                // These are observer stages, not a replacement for GetBaseHeightV3. The supplied callback records the real final result when available.
                float stackResult = stack.Apply(x, z, 0f);
                float minusOffset = stackResult - 0.15f;
                float plusSea = minusOffset + BC.Settings.SeaLevelAdjustment;
                samples.Add(new {
                    x = Float(x), z = Float(z), layers,
                    stackResult = Float(stackResult), minusOffset = Float(minusOffset), plusSea = Float(plusSea),
                    actualBase = actualBase == null ? null : Float(actualBase(x, z))
                });
            }
            var scalarFields = BC.Settings.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(float)).ToDictionary(f => f.Name, f => Float((float)f.GetValue(BC.Settings)));
            return new {
                format = "sp-precision/1", settingsVersion = BC.Settings.Version,
                totalSize = Float(BC.TotalSize), scalarFields, samples,
                note = "Observations use explicit float32 bit encodings. Intermediate calls add observation boundaries; actualBase is authoritative when present. No RNG mutation or terrain patch."
            };
        }

        static object Noise(BetterContinents.WarpedNoise noise, float x, float z)
        {
            float originalX = x;
            object boxed = noise;
            var settings = (BetterContinents.NoiseStackSettings.NoiseSettings)Field(boxed, "settings");
            float y = z * settings.Aspect;
            object warp = Field(boxed, "warp");
            var args = new object[] { x, y };
            if (warp != null)
            {
                warp.GetType().GetMethod("DomainWarp", new[] { typeof(float).MakeByRefType(), typeof(float).MakeByRefType() }).Invoke(warp, args);
                x = (float)args[0]; y = (float)args[1];
            }
            var generator = Field(boxed, "noise");
            float raw = (float)generator.GetType().GetMethod("GetNoise", new[] { typeof(float), typeof(float) }).Invoke(generator, new object[] { x, y });
            return new {
                min = Float((float)Field(boxed, "min")), max = Float((float)Field(boxed, "max")),
                add = Float((float)Field(boxed, "add")), mul = Float((float)Field(boxed, "mul")),
                warpedX = Float(x), warpedZ = Float(y), raw = Float(raw),
                filtered = Float(noise.GetNoise(originalX, z)),
                settingsAspect = Float(settings.Aspect)
            };
        }
    }
}
