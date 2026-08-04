using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NLog;

namespace LSModManager.Controls;

/// <summary>
/// Attached-Property, das async ein Bild aus einer URL nachlädt und dem
/// <see cref="Image"/> als Source zuweist. Vermeidet ein weiteres NuGet-Paket
/// (AsyncImageLoader), reicht für die Katalog-Vorschau-Cards.
/// </summary>
public static class UrlImageBehavior
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient Http = CreateClient();

    public static readonly AttachedProperty<string?> UrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Url", typeof(UrlImageBehavior));

    static UrlImageBehavior()
    {
        UrlProperty.Changed.AddClassHandler<Image>((img, _args) =>
        {
            _ = LoadAsync(img, GetUrl(img));
        });
    }

    public static void SetUrl(Image target, string? value) => target.SetValue(UrlProperty, value);
    public static string? GetUrl(Image target) => target.GetValue(UrlProperty);

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("LSModManager (+https://github.com/Kroste/LS-ModManager)");
        // GIANTS-CDN gibt Assets nur mit korrektem Referer frei — sonst HTTP 403.
        c.DefaultRequestHeaders.Referrer = new Uri("https://www.farming-simulator.com/");
        return c;
    }

    private static async Task LoadAsync(Image image, string? url)
    {
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (GetUrl(image) != url) return; // Recycler-Falle: Item wurde inzwischen anders belegt
                using var ms = new MemoryStream(bytes);
                image.Source = new Bitmap(ms);
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Bild-Load fehlgeschlagen: {url}", url);
        }
    }
}
