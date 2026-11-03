using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

internal static class BaseImage
{
    internal static int Run(string input, string resolution, string output)
    {
        if (!int.TryParse(resolution, out int size) || size < 64 || size > 2048) throw new ArgumentException("Resolution must be 64..2048.");
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Image output must be new.");
        if (new FileInfo(input).Length != size * size * 4L) throw new InvalidDataException("Unexpected float grid length.");
        var values = new float[size * size];
        using (var reader = new BinaryReader(File.OpenRead(input))) for (int i = 0; i < values.Length; i++) {
            values[i] = reader.ReadSingle(); if (!float.IsFinite(values[i])) throw new InvalidDataException("Non-finite image input.");
        }
        float min = values.Min(), max = values.Max();
        using var image = new Image<L16>(size, size);
        for (int row = 0; row < size; row++) for (int col = 0; col < size; col++)
            image[col, row] = new L16(max == min ? (ushort)0 : (ushort)Math.Clamp((int)((values[row * size + col] - min) / (max - min) * 65535f), 0, 65535));
        image.SaveAsPng(output);
        return 0;
    }
}
