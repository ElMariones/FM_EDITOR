using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using FMOpenEditor.App.Controls;

namespace FMOpenEditor.UiTests;

/// <summary>
/// The mascot is drawn in code (<see cref="Mascot"/>), so the app icon and logo files are rendered
/// from it rather than kept as hand-edited bitmaps. Run with BRAND=1 to regenerate
/// src/FMOpenEditor.App/Assets/icon.ico and the PNGs in docs/brand after changing the artwork.
/// </summary>
public class BrandAssetTests
{
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    private static byte[] Render(int size, bool badge, double warmth = 0)
    {
        var mascot = new Mascot { Badge = badge, Warmth = warmth, Width = size, Height = size, FollowPointer = false };
        mascot.Measure(new Size(size, size));
        mascot.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        bitmap.Render(mascot);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    /// <summary>An .ico whose entries are PNG-compressed, which Windows has supported since Vista.</summary>
    private static byte[] BuildIco(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write((uint)png.Length);
            w.Write((uint)offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) w.Write(png);
        w.Flush();
        return stream.ToArray();
    }

    [AvaloniaFact]
    public void Mascot_renders_at_every_icon_size()
    {
        var images = IconSizes.Select(s => (s, Render(s, badge: true))).ToList();
        Assert.All(images, i => Assert.True(i.Item2.Length > 100));
        var ico = BuildIco(images);
        Assert.Equal(IconSizes.Length, BitConverter.ToUInt16(ico, 4));

        if (Environment.GetEnvironmentVariable("BRAND") != "1") return;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs"))) dir = dir.Parent;
        var root = dir!.FullName;

        var assets = Path.Combine(root, "src", "FMOpenEditor.App", "Assets");
        Directory.CreateDirectory(assets);
        File.WriteAllBytes(Path.Combine(assets, "icon.ico"), ico);

        var brand = Path.Combine(root, "docs", "brand");
        Directory.CreateDirectory(brand);
        File.WriteAllBytes(Path.Combine(brand, "icon-512.png"), Render(512, badge: true));
        File.WriteAllBytes(Path.Combine(brand, "icon-32.png"), Render(32, badge: true));
        File.WriteAllBytes(Path.Combine(brand, "tiki-chroma.png"), Render(512, badge: false));
        File.WriteAllBytes(Path.Combine(brand, "tiki-sunset.png"), Render(512, badge: false, warmth: 1));
    }
}
