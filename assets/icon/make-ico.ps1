param([string]$Source, [string]$OutIco, [string]$OutPng, [string]$PreviewDir)
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconMaker {
  // Crop to the opaque tile, make its inside fully opaque, centre it on a square with a margin.
  public static Bitmap Master(string path, int size, double margin) {
    using (var src = new Bitmap(path)) {
      int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
      for (int y = 0; y < src.Height; y++)
        for (int x = 0; x < src.Width; x++)
          if (src.GetPixel(x, y).A > 128) {
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
          }
      int w = maxX - minX + 1, h = maxY - minY + 1;
      var tile = new Bitmap(w, h, PixelFormat.Format32bppArgb);
      for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++) {
          var c = src.GetPixel(minX + x, minY + y);
          int a = c.A >= 240 ? 255 : c.A;
          tile.SetPixel(x, y, Color.FromArgb(a, c.R, c.G, c.B));
        }
      var master = new Bitmap(size, size, PixelFormat.Format32bppArgb);
      using (var g = Graphics.FromImage(master)) {
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        double inner = size * (1 - 2 * margin);
        double scale = inner / Math.Max(w, h);
        double dw = w * scale, dh = h * scale;
        g.DrawImage(tile, new RectangleF((float)((size - dw) / 2), (float)((size - dh) / 2), (float)dw, (float)dh));
      }
      tile.Dispose();
      return master;
    }
  }

  // Downscale in halving steps, so thin strokes don't alias at small sizes.
  public static Bitmap Scale(Bitmap master, int size) {
    Bitmap current = master;
    bool owned = false;
    while (current.Width / 2 >= size * 2) {
      var half = Resize(current, current.Width / 2);
      if (owned) current.Dispose();
      current = half; owned = true;
    }
    var result = Resize(current, size);
    if (owned) current.Dispose();
    return result;
  }

  static Bitmap Resize(Bitmap src, int size) {
    var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(dst)) {
      g.Clear(Color.Transparent);
      g.InterpolationMode = InterpolationMode.HighQualityBicubic;
      g.PixelOffsetMode = PixelOffsetMode.HighQuality;
      g.CompositingQuality = CompositingQuality.HighQuality;
      using (var attrs = new ImageAttributes()) {
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
      }
    }
    return dst;
  }

  static byte[] Dib(Bitmap bmp) {
    int s = bmp.Width;
    using (var ms = new MemoryStream())
    using (var bw = new BinaryWriter(ms)) {
      bw.Write(40); bw.Write(s); bw.Write(s * 2); bw.Write((short)1); bw.Write((short)32);
      bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
      for (int y = s - 1; y >= 0; y--)
        for (int x = 0; x < s; x++) {
          var c = bmp.GetPixel(x, y);
          bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
        }
      int stride = ((s + 31) / 32) * 4;
      for (int y = 0; y < s; y++)
        for (int i = 0; i < stride; i++) bw.Write((byte)0);
      return ms.ToArray();
    }
  }

  static byte[] Png(Bitmap bmp) {
    using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
  }

  public static void Write(Bitmap master, int[] sizes, string icoPath, string previewDir) {
    var images = new List<byte[]>();
    foreach (int s in sizes) {
      string over = previewDir == null ? null : Path.Combine(previewDir, "override-" + s + ".png");
      using (var b = over != null && File.Exists(over) ? new Bitmap(over) : Scale(master, s)) {
        images.Add(s <= 48 ? Dib(b) : Png(b));
        if (previewDir != null) b.Save(Path.Combine(previewDir, "final-" + s + ".png"), ImageFormat.Png);
      }
    }
    using (var fs = File.Create(icoPath))
    using (var bw = new BinaryWriter(fs)) {
      bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
      int offset = 6 + 16 * sizes.Length;
      for (int i = 0; i < sizes.Length; i++) {
        int s = sizes[i];
        bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)(s >= 256 ? 0 : s));
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
        bw.Write(images[i].Length); bw.Write(offset);
        offset += images[i].Length;
      }
      foreach (var img in images) bw.Write(img);
    }
  }
}
'@
$master = [IconMaker]::Master($Source, 1024, 0.04)
$master.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
[IconMaker]::Write($master, [int[]](16,20,24,32,40,48,64,96,128,256), $OutIco, $PreviewDir)
$master.Dispose()
"ico {0} bytes, png {1} bytes" -f (Get-Item $OutIco).Length, (Get-Item $OutPng).Length
