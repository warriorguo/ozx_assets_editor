using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OAE.Core.Resources;

namespace OAE.App.Views;

/// <summary>
/// Renders a <see cref="TilemapDocument"/> to a small <see cref="WriteableBitmap"/>.
/// Each layer paints in a distinct color over the ground baseline, so the
/// layout reads at a glance without re-implementing ORT's tile rendering.
/// </summary>
internal static class TilemapPreviewRenderer
{
    private const int CellPx = 18;

    public static WriteableBitmap? Render(TilemapDocument doc)
    {
        var w = doc.Width;
        var h = doc.Height;
        if (w <= 0 || h <= 0 || doc.Ground is null) return null;

        var bmp = new WriteableBitmap(
            new PixelSize(w * CellPx, h * CellPx),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using var fb = bmp.Lock();
        unsafe
        {
            var ptr = (byte*)fb.Address;
            var stride = fb.RowBytes;

            Fill(ptr, stride, w * CellPx, h * CellPx, 0x14_19_22FF);

            PaintLayer(ptr, stride, w, h, doc.Ground,    0x2B_3A_4FFF, fillCell: true);
            PaintLayer(ptr, stride, w, h, doc.SoftEdge,  0x3C_50_70FF, fillCell: false);
            PaintLayer(ptr, stride, w, h, doc.Bridge,    0x8C_6B_3CFF, fillCell: true);
            PaintLayer(ptr, stride, w, h, doc.Rail,      0xB0_85_4FFF, fillCell: false);
            PaintLayer(ptr, stride, w, h, doc.Pipeline,  0x4F_7A_85FF, fillCell: false);
            PaintLayer(ptr, stride, w, h, doc.Static,    0x55_5C_6EFF, fillCell: true);
            PaintLayer(ptr, stride, w, h, doc.MainPath,  0x7E_AD_9CFF, fillCell: false);
            PaintLayer(ptr, stride, w, h, doc.Chaser,    0xE3_77_77FF, fillCell: false);
            PaintLayer(ptr, stride, w, h, doc.Zoner,     0xE3_B9_77FF, fillCell: false);
            PaintLayer(ptr, stride, w, h, doc.Dps,       0xE3_77_C8FF, fillCell: false);
            PaintLayer(ptr, stride, w, h, doc.MobAir,    0x9B_77_E3FF, fillCell: false);

            PaintDoors(ptr, stride, w, h, doc);
        }
        return bmp;
    }

    private static unsafe void Fill(byte* ptr, int stride, int wPx, int hPx, uint rgba)
    {
        var (b, g, r, a) = Unpack(rgba);
        for (var y = 0; y < hPx; y++)
        {
            var row = ptr + y * stride;
            for (var x = 0; x < wPx; x++)
            {
                row[x * 4 + 0] = b;
                row[x * 4 + 1] = g;
                row[x * 4 + 2] = r;
                row[x * 4 + 3] = a;
            }
        }
    }

    private static unsafe void PaintLayer(byte* ptr, int stride, int w, int h, int[][]? layer, uint rgba, bool fillCell)
    {
        if (layer is null) return;
        var (b, g, r, a) = Unpack(rgba);
        for (var y = 0; y < h; y++)
        {
            if (y >= layer.Length) break;
            var row = layer[y];
            for (var x = 0; x < w; x++)
            {
                if (x >= row.Length || row[x] <= 0) continue;
                if (fillCell)
                {
                    FillCell(ptr, stride, x, y, b, g, r, a);
                }
                else
                {
                    DotCell(ptr, stride, x, y, b, g, r, a);
                }
            }
        }
    }

    private static unsafe void FillCell(byte* ptr, int stride, int cellX, int cellY, byte b, byte g, byte r, byte a)
    {
        // Leave a 1-px gutter so the grid reads visually.
        var x0 = cellX * CellPx + 1;
        var y0 = cellY * CellPx + 1;
        var x1 = x0 + CellPx - 2;
        var y1 = y0 + CellPx - 2;
        for (var py = y0; py < y1; py++)
        {
            var row = ptr + py * stride;
            for (var px = x0; px < x1; px++)
            {
                row[px * 4 + 0] = b;
                row[px * 4 + 1] = g;
                row[px * 4 + 2] = r;
                row[px * 4 + 3] = a;
            }
        }
    }

    private static unsafe void DotCell(byte* ptr, int stride, int cellX, int cellY, byte b, byte g, byte r, byte a)
    {
        // Small centered square so the overlay reads on top of a filled cell.
        var size = CellPx / 3;
        var x0 = cellX * CellPx + (CellPx - size) / 2;
        var y0 = cellY * CellPx + (CellPx - size) / 2;
        for (var py = y0; py < y0 + size; py++)
        {
            var row = ptr + py * stride;
            for (var px = x0; px < x0 + size; px++)
            {
                row[px * 4 + 0] = b;
                row[px * 4 + 1] = g;
                row[px * 4 + 2] = r;
                row[px * 4 + 3] = a;
            }
        }
    }

    private static unsafe void PaintDoors(byte* ptr, int stride, int w, int h, TilemapDocument doc)
    {
        const uint doorColor = 0xFF_C8_3DFF;
        var (b, g, r, a) = Unpack(doorColor);
        var midX = w / 2;
        var midY = h / 2;
        if (doc.Doors.Top > 0) StripeCell(ptr, stride, midX, 0, b, g, r, a);
        if (doc.Doors.Bottom > 0) StripeCell(ptr, stride, midX, h - 1, b, g, r, a);
        if (doc.Doors.Left > 0) StripeCell(ptr, stride, 0, midY, b, g, r, a);
        if (doc.Doors.Right > 0) StripeCell(ptr, stride, w - 1, midY, b, g, r, a);
    }

    private static unsafe void StripeCell(byte* ptr, int stride, int cellX, int cellY, byte b, byte g, byte r, byte a)
    {
        var x0 = cellX * CellPx;
        var y0 = cellY * CellPx;
        for (var py = y0; py < y0 + CellPx; py++)
        {
            var row = ptr + py * stride;
            for (var px = x0; px < x0 + CellPx; px++)
            {
                if ((px + py) % 3 != 0) continue;
                row[px * 4 + 0] = b;
                row[px * 4 + 1] = g;
                row[px * 4 + 2] = r;
                row[px * 4 + 3] = a;
            }
        }
    }

    private static (byte b, byte g, byte r, byte a) Unpack(uint rgba)
    {
        var r = (byte)((rgba >> 24) & 0xFF);
        var g = (byte)((rgba >> 16) & 0xFF);
        var b = (byte)((rgba >> 8) & 0xFF);
        var a = (byte)(rgba & 0xFF);
        return (b, g, r, a);
    }
}
