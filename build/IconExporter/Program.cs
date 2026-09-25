using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string assets = Path.Combine(root, "assets", "branding");
        var icons = JsonSerializer.Deserialize<Dictionary<string, Layer[]>>(File.ReadAllText(Path.Combine(assets, "icon-pack.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var xaml = new StringBuilder("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n");
        foreach (var (name, layers) in icons)
        {
            int size = name == "app" ? 64 : 24;
            var group = new DrawingGroup();
            xaml.AppendLine($"  <DrawingImage x:Key=\"Icon.{name}\"><DrawingImage.Drawing><DrawingGroup>");
            // Transparent bounds keep every icon aligned to the same canvas.
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size))));
            xaml.AppendLine($"    <GeometryDrawing Brush=\"Transparent\" Geometry=\"M0,0 H{size} V{size} H0 Z\"/>");
            var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" fill=\"none\"><title>Sterling {name}</title>\n");
            foreach (var layer in layers)
            {
                string stroke = layer.Stroke ?? (name == "app" ? "none" : "#177F85");
                string fill = layer.Fill ?? "none";
                var brush = fill == "none" ? null : (Brush)new BrushConverter().ConvertFromString(fill)!;
                Pen? pen = stroke == "none" ? null : new Pen((Brush)new BrushConverter().ConvertFromString(stroke)!, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                group.Children.Add(new GeometryDrawing(brush, pen, Geometry.Parse(layer.Path)));
                svg.AppendLine($"<path d=\"{layer.Path}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                xaml.Append($"    <GeometryDrawing Geometry=\"{layer.Path}\"");
                if (fill != "none") xaml.Append($" Brush=\"{fill}\"");
                if (stroke == "none") xaml.AppendLine("/>");
                else xaml.AppendLine($"><GeometryDrawing.Pen><Pen Brush=\"{stroke}\" Thickness=\"1.8\" StartLineCap=\"Round\" EndLineCap=\"Round\" LineJoin=\"Round\"/></GeometryDrawing.Pen></GeometryDrawing>");
            }
            svg.AppendLine("</svg>"); File.WriteAllText(Path.Combine(assets, name + ".svg"), svg.ToString());
            xaml.AppendLine("  </DrawingGroup></DrawingImage.Drawing></DrawingImage>");
            byte[] Render(int pixels)
            {
                var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { drawing.PushTransform(new ScaleTransform((double)pixels / size, (double)pixels / size)); drawing.DrawDrawing(group); }
                var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); png.Save(stream); return stream.ToArray();
            }
            File.WriteAllBytes(Path.Combine(assets, name + ".png"), Render(name == "app" ? 256 : 48));
            if (name == "app")
            {
                int[] sizes = [16, 24, 32, 48, 64, 128, 256]; var images = sizes.Select(Render).ToArray();
                using var file = File.Create(Path.Combine(assets, "SterlingSoftwareCentre.ico")); using var writer = new BinaryWriter(file);
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++) { writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length; }
                foreach (var bytes in images) writer.Write(bytes);
            }
        }
        xaml.AppendLine("</ResourceDictionary>"); File.WriteAllText(Path.Combine(assets, "Icons.xaml"), xaml.ToString());
        Console.WriteLine("Generated original Sterling SVG, PNG, WPF resources and seven-size ICO.");
    }
    public record Layer(string Path, string? Fill, string? Stroke);
}
