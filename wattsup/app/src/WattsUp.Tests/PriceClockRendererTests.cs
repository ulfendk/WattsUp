using System.Buffers.Binary;
using System.IO.Compression;
using WattsUp.Services.Pricing;

namespace WattsUp.Tests;

public class PriceClockRendererTests
{
    private const int Size = 200;

    [Fact]
    public void RenderPng_GreysOutPassedHoursAndColoursTheRestByPrice()
    {
        var prices = Enumerable.Range(0, 24).ToDictionary(h => h, h => h == 18 ? 5m : h == 3 ? 1m : 2m);

        var pixels = Decode(PriceClockRenderer.RenderPng(prices, currentHour: 12, currentPrice: 3.21m, Size));

        Assert.Equal((0xCF, 0xCF, 0xCF, 255), SliceColour(pixels, hour: 3)); // passed, even though cheapest
        var expensive = SliceColour(pixels, hour: 18);
        Assert.Equal((0xF4, 0x43, 0x36, 255), expensive); // the day's maximum is the red end
        var ordinary = SliceColour(pixels, hour: 20);
        Assert.True(ordinary.G > ordinary.R, "an hour near the day's minimum should lean green");
        Assert.Equal(0, pixels[0, 0].A); // transparent outside the face
    }

    [Fact]
    public void RenderPng_WithoutPricesStillDrawsAClock()
    {
        var pixels = Decode(PriceClockRenderer.RenderPng(new Dictionary<int, decimal>(), currentHour: 0, currentPrice: null, Size));

        Assert.Equal((0xEE, 0xEE, 0xEE, 255), SliceColour(pixels, hour: 9));
    }

    /// <summary>The colour in the middle of an hour's slice.</summary>
    private static (int R, int G, int B, int A) SliceColour((int R, int G, int B, int A)[,] pixels, int hour)
    {
        var angle = (hour + 0.5) / 24 * 2 * Math.PI;
        var radius = Size * 0.35;
        var x = (int)(Size / 2.0 + Math.Sin(angle) * radius);
        var y = (int)(Size / 2.0 - Math.Cos(angle) * radius);
        return pixels[x, y];
    }

    /// <summary>Just enough of a PNG reader for the renderer's own output: 8-bit RGBA, no filters.</summary>
    private static (int R, int G, int B, int A)[,] Decode(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        Assert.Equal((Size, Size), (width, height));

        using var idat = new MemoryStream();
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            if (System.Text.Encoding.ASCII.GetString(png, offset + 4, 4) == "IDAT")
            {
                idat.Write(png, offset + 8, length);
            }
            offset += 12 + length;
        }
        idat.Position = 0;
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.CopyTo(raw);
        }

        var bytes = raw.ToArray();
        var pixels = new (int, int, int, int)[width, height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 4) + 1;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                pixels[x, y] = (bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]);
            }
        }
        return pixels;
    }
}
