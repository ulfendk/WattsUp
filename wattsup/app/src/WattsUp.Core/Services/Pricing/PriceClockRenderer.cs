using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace WattsUp.Services.Pricing;

/// <summary>
/// Draws today's prices as a 24-hour clock: one ring slice per local hour, midnight at the top and
/// running clockwise, coloured on the same green → orange → red scale over the day's range as the
/// Dashboard's hourly list. Hours that have passed are greyed out and the current hour sticks out,
/// with the current price in the middle. Rendered straight to PNG (no imaging dependency) so it can
/// be published as a Home Assistant image entity and attached to phone notifications.
/// </summary>
public static class PriceClockRenderer
{
    // MudBlazor's default Success / Warning / Error, which the Dashboard's range bars blend between.
    private static readonly Rgb Green = new(0x00, 0xC8, 0x53);
    private static readonly Rgb Orange = new(0xFF, 0x98, 0x00);
    private static readonly Rgb Red = new(0xF4, 0x43, 0x36);
    private static readonly Rgb Passed = new(0xCF, 0xCF, 0xCF);
    private static readonly Rgb Unknown = new(0xEE, 0xEE, 0xEE);
    private static readonly Rgb Face = new(0xFF, 0xFF, 0xFF);
    private static readonly Rgb Rim = new(0xE0, 0xE0, 0xE0);
    private static readonly Rgb Ink = new(0x30, 0x30, 0x30);
    private static readonly Rgb MutedInk = new(0x8A, 0x8A, 0x8A);

    /// <param name="hourlyAverages">Average price per local hour of the day (0–23); hours without
    /// a known price are left out and drawn as empty slices.</param>
    /// <param name="currentHour">The local hour now; earlier hours are greyed out.</param>
    /// <param name="currentPrice">Shown in the middle, or nothing when null.</param>
    public static byte[] RenderPng(
        IReadOnlyDictionary<int, decimal> hourlyAverages, int currentHour, decimal? currentPrice, int size = 512)
    {
        var colours = new Rgb?[24];
        if (hourlyAverages.Count > 0)
        {
            var min = hourlyAverages.Values.Min();
            var span = hourlyAverages.Values.Max() - min;
            foreach (var (hour, average) in hourlyAverages)
            {
                if (hour is < 0 or > 23)
                {
                    continue;
                }
                colours[hour] = hour < currentHour
                    ? Passed
                    : Scale(span <= 0m ? 0.5 : (double)((average - min) / span));
            }
        }

        var canvas = new Canvas(size);
        var c = size / 2.0;
        var faceRadius = size * 0.49;
        canvas.Fill(Rim, (x, y) => faceRadius - Distance(x, y, c, c));
        canvas.Fill(Face, (x, y) => faceRadius - 2 - Distance(x, y, c, c));

        var inner = size * 0.27;
        var outer = size * 0.43;
        var currentOuter = size * 0.475;
        const double gap = 1.5; // half the 3px surface gap between neighbouring slices
        for (var hour = 0; hour < 24; hour++)
        {
            var slice = hour;
            var outerRadius = hour == currentHour ? currentOuter : outer;
            canvas.Fill(colours[hour] ?? Unknown, (x, y) =>
            {
                var r = Distance(x, y, c, c);
                // Clockwise angle from 12 o'clock, in hours (0–24).
                var angle = (Math.Atan2(x - c, c - y) / (2 * Math.PI) * 24 + 24) % 24;
                var fromStart = AngularDistance(angle, slice) * r * 2 * Math.PI / 24;
                var toEnd = AngularDistance(slice + 1, angle) * r * 2 * Math.PI / 24;
                return Math.Min(Math.Min(r - inner, outerRadius - r), Math.Min(fromStart, toEnd) - gap);
            });
        }

        // Glyphs are 6 grid steps tall, so a step of size/150 gives labels 4 % of the image high.
        var labelUnit = size / 150.0;
        foreach (var hour in new[] { 0, 6, 12, 18 })
        {
            var a = hour / 24.0 * 2 * Math.PI;
            var r = inner - labelUnit * 7;
            DrawText(canvas, hour.ToString("00"), c + Math.Sin(a) * r, c - Math.Cos(a) * r, labelUnit, MutedInk);
        }

        if (currentPrice is { } price)
        {
            DrawText(canvas, price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ','),
                c, c, size / 60.0, Ink);
        }

        return canvas.ToPng();
    }

    private static Rgb Scale(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? Rgb.Lerp(Green, Orange, t * 2) : Rgb.Lerp(Orange, Red, (t - 0.5) * 2);
    }

    private static double Distance(double x, double y, double cx, double cy) =>
        Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

    /// <summary>How far (in hours, 0–24) <paramref name="to"/> lies clockwise past <paramref name="from"/>.</summary>
    private static double AngularDistance(double to, double from)
    {
        var d = ((to - from) % 24 + 24) % 24;
        return d > 12 ? d - 24 : d;
    }

    // --- text: digits and a comma drawn as rounded strokes on a 4×6 grid -------------------------

    private static readonly Dictionary<char, (double X, double Y)[][]> Glyphs = new()
    {
        ['0'] = [[(0, 0), (4, 0), (4, 6), (0, 6), (0, 0)]],
        ['1'] = [[(1, 1), (2.5, 0), (2.5, 6)]],
        ['2'] = [[(0, 0), (4, 0), (4, 3), (0, 3), (0, 6), (4, 6)]],
        ['3'] = [[(0, 0), (4, 0), (4, 6), (0, 6)], [(1, 3), (4, 3)]],
        ['4'] = [[(0, 0), (0, 3), (4, 3)], [(4, 0), (4, 6)]],
        ['5'] = [[(4, 0), (0, 0), (0, 3), (4, 3), (4, 6), (0, 6)]],
        ['6'] = [[(4, 0), (0, 0), (0, 6), (4, 6), (4, 3), (0, 3)]],
        ['7'] = [[(0, 0), (4, 0), (4, 6)]],
        ['8'] = [[(0, 0), (4, 0), (4, 6), (0, 6), (0, 0)], [(0, 3), (4, 3)]],
        ['9'] = [[(4, 3), (0, 3), (0, 0), (4, 0), (4, 6), (0, 6)]],
        [','] = [[(0.6, 5.6), (0, 7.2)]],
        ['-'] = [[(0.5, 3), (3.5, 3)]],
    };

    private static double Advance(char ch) => ch == ',' ? 2.2 : 6;

    /// <summary>Draws <paramref name="text"/> centred on (<paramref name="cx"/>, <paramref name="cy"/>);
    /// <paramref name="unit"/> is the size of one grid step in pixels.</summary>
    private static void DrawText(Canvas canvas, string text, double cx, double cy, double unit, Rgb colour)
    {
        var segments = new List<(double X1, double Y1, double X2, double Y2)>();
        var width = text.Sum(Advance) - 2;
        var x0 = cx - width * unit / 2;
        var y0 = cy - 3 * unit;
        var pen = 0.0;
        foreach (var ch in text)
        {
            if (Glyphs.TryGetValue(ch, out var strokes))
            {
                foreach (var stroke in strokes)
                {
                    for (var i = 1; i < stroke.Length; i++)
                    {
                        segments.Add((x0 + (pen + stroke[i - 1].X) * unit, y0 + stroke[i - 1].Y * unit,
                            x0 + (pen + stroke[i].X) * unit, y0 + stroke[i].Y * unit));
                    }
                }
            }
            pen += Advance(ch);
        }

        var halfWidth = unit * 0.5;
        canvas.Fill(colour, (x, y) =>
        {
            var best = double.MaxValue;
            foreach (var s in segments)
            {
                best = Math.Min(best, SegmentDistance(x, y, s.X1, s.Y1, s.X2, s.Y2));
            }
            return halfWidth - best;
        });
    }

    private static double SegmentDistance(double px, double py, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lengthSquared, 0, 1);
        return Distance(px, py, x1 + t * dx, y1 + t * dy);
    }

    // --- raster + PNG ------------------------------------------------------------------------------

    private readonly record struct Rgb(byte R, byte G, byte B)
    {
        public static Rgb Lerp(Rgb a, Rgb b, double t) => new(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>A transparent RGBA canvas. Shapes are given as signed distance functions (positive
    /// inside, in pixels), which gives one pixel of anti-aliasing at every edge for free.</summary>
    private sealed class Canvas(int size)
    {
        private readonly double[] _pixels = new double[size * size * 4];

        public void Fill(Rgb colour, Func<double, double, double> insideDistance)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var coverage = Math.Clamp(insideDistance(x + 0.5, y + 0.5) + 0.5, 0, 1);
                    if (coverage <= 0)
                    {
                        continue;
                    }
                    var i = (y * size + x) * 4;
                    var alpha = _pixels[i + 3];
                    _pixels[i] = _pixels[i] * (1 - coverage) + colour.R * coverage;
                    _pixels[i + 1] = _pixels[i + 1] * (1 - coverage) + colour.G * coverage;
                    _pixels[i + 2] = _pixels[i + 2] * (1 - coverage) + colour.B * coverage;
                    _pixels[i + 3] = alpha + (255 - alpha) * coverage;
                }
            }
        }

        public byte[] ToPng()
        {
            using var raw = new MemoryStream();
            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                var row = new byte[1 + size * 4];
                for (var y = 0; y < size; y++)
                {
                    row[0] = 0; // no filter
                    for (var x = 0; x < size * 4; x++)
                    {
                        var i = y * size * 4 + x;
                        var alpha = _pixels[i - x % 4 + 3];
                        // Colours were blended over transparent black; un-premultiply for PNG.
                        var value = x % 4 == 3 ? alpha : alpha > 0 ? _pixels[i] * 255 / alpha : 0;
                        row[1 + x] = (byte)Math.Clamp(Math.Round(value), 0, 255);
                    }
                    zlib.Write(row);
                }
            }

            using var png = new MemoryStream();
            png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, size);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size);
            header[8] = 8; // bit depth
            header[9] = 6; // RGBA
            WriteChunk(png, "IHDR", header);
            WriteChunk(png, "IDAT", raw.ToArray());
            WriteChunk(png, "IEND", []);
            return png.ToArray();
        }

        private static void WriteChunk(Stream png, string type, byte[] data)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
            png.Write(buffer);
            var typeBytes = Encoding.ASCII.GetBytes(type);
            png.Write(typeBytes);
            png.Write(data);
            BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32(typeBytes, data));
            png.Write(buffer);
        }

        private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
        {
            var c = (uint)n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return c;
        }).ToArray();

        private static uint Crc32(byte[] type, byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in type.Concat(data))
            {
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
