using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

static class MakeIcon
{
    private static Bitmap DrawFrame(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float scale = size / 32f;
            int b = (int)Math.Round(1.5f * scale);
            int rad = (int)Math.Round(6.0f * scale);
            int x0 = b, y0 = b, x1 = size - 1 - b, y1 = size - 1 - b;
            int d = Math.Max(1, rad);
            using (var path = new GraphicsPath())
            {
                path.AddArc(x0, y0, d, d, 180, 90);
                path.AddArc(x1 - d, y0, d, d, 270, 90);
                path.AddArc(x1 - d, y1 - d, d, d, 0, 90);
                path.AddArc(x0, y1 - d, d, d, 90, 90);
                path.CloseFigure();
                using (var fill = new SolidBrush(Color.FromArgb(255, 255, 105, 0)))
                    g.FillPath(fill, path);
            }
            int[][] src =
            {
                new[] { 18, 6 }, new[] { 11, 18 }, new[] { 15, 18 },
                new[] { 13, 26 }, new[] { 21, 13 }, new[] { 17, 13 }
            };
            var pts = new PointF[src.Length];
            for (int i = 0; i < src.Length; i++)
                pts[i] = new PointF((src[i][0]) * scale, (src[i][1]) * scale);
            using (var bolt = new SolidBrush(Color.White))
                g.FillPolygon(bolt, pts);
        }
        return bmp;
    }

    // 手编 32bpp DIB + AND mask（单帧 ico 数据段）
    private static byte[] DIBFrame(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        var raw = new byte[stride * h];
        Marshal.Copy(data.Scan0, raw, 0, raw.Length);
        bmp.UnlockBits(data);

        int rows = h * 2; // XOR + AND
        int maskStride = (w + 31) / 32 * 4;
        var img = new List<byte>((40) + stride * h + maskStride * h);
        img.AddRange(BitConverter.GetBytes(40));                    // biSize
        img.AddRange(BitConverter.GetBytes(w));                     // biWidth
        img.AddRange(BitConverter.GetBytes(rows));                  // biHeight (incl AND)
        img.AddRange(BitConverter.GetBytes((ushort)1));             // planes
        img.AddRange(BitConverter.GetBytes((ushort)32));            // bpp
        img.AddRange(BitConverter.GetBytes(0));                     // compression
        img.AddRange(BitConverter.GetBytes(stride * h * 2 + maskStride * h)); // biSizeImage
        img.AddRange(new byte[16]);                                 // xppm/ypm/clrUsed/clrImportant
        for (int r = h - 1; r >= 0; r--)                            // XOR rows bottom-up
            for (int c = 0; c < stride; c++)
                img.Add(raw[r * stride + c]);
        img.AddRange(new byte[maskStride * h]);                     // AND mask (zeros)
        return img.ToArray();
    }

    private static byte[] SingleIco(int size, byte[] dib)
    {
        var b = new List<byte>(6 + 16 + dib.Length);
        b.AddRange(new byte[] { 0, 0, 1, 0 });
        b.Add(1); b.Add(0); // count
        byte bw = size >= 256 ? (byte)0 : (byte)size;
        b.AddRange(new byte[] { bw, bw, 0, 0 });
        b.AddRange(BitConverter.GetBytes((ushort)1));
        b.AddRange(BitConverter.GetBytes((ushort)32));
        b.AddRange(BitConverter.GetBytes((uint)dib.Length));
        b.AddRange(BitConverter.GetBytes(22u));
        b.AddRange(dib);
        return b.ToArray();
    }

    private static bool ValidIco(byte[] ico, Bitmap reference)
    {
        int w = reference.Width, h = reference.Height;
        try
        {
            using (var ms = new MemoryStream(ico))
            using (var icon = new Icon(ms, w, h))
            using (var bmp = icon.ToBitmap())
            {
                if (bmp.Width != w || bmp.Height != h) return false;
                int checks = 0, match = 0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var a = reference.GetPixel(x, y);
                        var b = bmp.GetPixel(x, y);
                        if (a.A < 30) continue; // skip transparent ref areas (anti-alias edges)
                        checks++;
                        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B, da = a.A - b.A;
                        if (Math.Abs(dr) <= 24 && Math.Abs(dg) <= 24 && Math.Abs(db) <= 24 && Math.Abs(da) <= 40)
                            match++;
                    }
                }
                if (checks == 0) return false;
                return (double)match / checks >= 0.85;
            }
        }
        catch { return false; }
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: MakeIcon <out.ico> ..."); return 1; }

        int[] sizes = { 256, 64, 48, 32, 24, 16 };
        var frames = new List<(byte[] Data, int Size)>();
        foreach (int s in sizes)
        {
            byte[] dib;
            using (var bmp = DrawFrame(s))
            {
                dib = DIBFrame(bmp);
                var single = SingleIco(s, dib);
                if (!ValidIco(single, bmp))
                {
                    Console.WriteLine("skip size " + s + " (GDI rejects frame)");
                    continue;
                }
            }
            frames.Add((dib, s));
        }
        if (frames.Count == 0) { Console.WriteLine("no valid frames"); return 2; }

        var full = new List<byte>();
        ushort count = (ushort)frames.Count;
        full.AddRange(new byte[] { 0, 0, 1, 0 });
        full.Add((byte)count); full.Add(0);
        int offset = 6 + 16 * count;
        foreach (var f in frames)
        {
            byte bw = f.Size >= 256 ? (byte)0 : (byte)f.Size;
            full.AddRange(new byte[] { bw, bw, 0, 0 });
            full.AddRange(BitConverter.GetBytes((ushort)1));
            full.AddRange(BitConverter.GetBytes((ushort)32));
            full.AddRange(BitConverter.GetBytes((uint)f.Data.Length));
            full.AddRange(BitConverter.GetBytes((uint)offset));
            offset += f.Data.Length;
        }
        foreach (var f in frames)
            full.AddRange(f.Data);

        byte[] final = full.ToArray();
        using (var ref64 = DrawFrame(64))
        {
            if (!ValidIco(final, ref64))
            {
                Console.WriteLine("FAIL: combined icon rejected by GDI");
                return 3;
            }
        }

        foreach (string t in args)
        {
            File.WriteAllBytes(t, final);
            Console.WriteLine("wrote " + t + " (" + final.Length + " bytes, " + count + " frames)");
        }
        return 0;
    }
}