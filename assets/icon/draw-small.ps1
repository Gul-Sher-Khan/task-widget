param([string]$OutDir)
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class SmallIcon {
  // The master's geometry, as fractions of the tile: ring centre (0.5, 0.5), radius 0.31; check through
  // (0.342, 0.506), (0.448, 0.607), (0.658, 0.401); dot at (0.785, 0.220), radius 0.05.
  public static Bitmap Draw(int s, bool ring) {
    var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(b)) {
      g.Clear(Color.Transparent);
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.PixelOffsetMode = PixelOffsetMode.HighQuality;
      float m = s <= 16 ? 0.5f : 1f;
      var tile = new RectangleF(m, m, s - 2 * m, s - 2 * m);
      float r = tile.Width * 0.22f;
      using (var path = new GraphicsPath()) {
        path.AddArc(tile.X, tile.Y, 2 * r, 2 * r, 180, 90);
        path.AddArc(tile.Right - 2 * r, tile.Y, 2 * r, 2 * r, 270, 90);
        path.AddArc(tile.Right - 2 * r, tile.Bottom - 2 * r, 2 * r, 2 * r, 0, 90);
        path.AddArc(tile.X, tile.Bottom - 2 * r, 2 * r, 2 * r, 90, 90);
        path.CloseFigure();
        using (var fill = new LinearGradientBrush(new PointF(0, tile.Y), new PointF(0, tile.Bottom),
            Color.FromArgb(0x3B, 0xB7, 0xFC), Color.FromArgb(0x01, 0x60, 0xC8)))
          g.FillPath(fill, path);
      }
      float w = tile.Width;
      Func<float, float, PointF> at = (fx, fy) => new PointF(tile.X + fx * w, tile.Y + fy * w);
      if (ring) {
        float stroke = Math.Max(1.25f, w * 0.075f);
        float rad = w * 0.31f;
        using (var pen = new Pen(Color.White, stroke)) {
          var c = at(0.5f, 0.5f);
          g.DrawEllipse(pen, c.X - rad, c.Y - rad, 2 * rad, 2 * rad);
        }
        using (var pen = new Pen(Color.White, Math.Max(1.5f, w * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
          g.DrawLines(pen, new[] { at(0.36f, 0.51f), at(0.455f, 0.605f), at(0.645f, 0.415f) });
        if (s >= 20) {
          var d = at(0.80f, 0.20f);
          float dr = Math.Max(1.1f, w * 0.065f);
          g.FillEllipse(Brushes.White, d.X - dr, d.Y - dr, 2 * dr, 2 * dr);
        }
      } else {
        using (var pen = new Pen(Color.White, Math.Max(2f, w * 0.15f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
          g.DrawLines(pen, new[] { at(0.26f, 0.52f), at(0.43f, 0.69f), at(0.75f, 0.35f) });
      }
    }
    return b;
  }
}
'@
foreach ($s in 16,20,24) {
  foreach ($v in @(@('a', $true), @('b', $false))) {
    $bmp = [SmallIcon]::Draw($s, $v[1]); $bmp.Save("$OutDir\small-$($v[0])-$s.png"); $bmp.Dispose()
  }
}
