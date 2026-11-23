using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Adawat.Core;

namespace Adawat.App;

/// <summary>
/// A slideshow of the newest programs and games on IBRA STORE (ibraprogram.netlify.app/api/apps), not the news.
/// It takes the app's own font, text colour and surfaces from <see cref="Theme"/>; the main colour is the app's accent,
/// or <see cref="ACCENT"/> when that is set. It stays hidden until the list arrives and stays hidden if the request fails.
/// </summary>
public static class StoreSlides
{
    /// <summary>Main colour of the slideshow, as "#RRGGBB". Empty: use the app's accent colour.</summary>
    public const string ACCENT = "";

    private const string Site = "https://ibraprogram.netlify.app";
    private const int Count = 10;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

    private sealed record Slide(string Name, string Slug, string Tagline, string Category, string Icon, string Background, DateTime Created);

    private static Brush AccentBrush
    {
        get
        {
            if (ACCENT.Length > 0)
            {
                try { var b = (Brush)new BrushConverter().ConvertFromString(ACCENT)!; b.Freeze(); return b; } catch (FormatException) { }
            }
            return Theme.Accent;
        }
    }

    private static string Absolute(string url) =>
        string.IsNullOrWhiteSpace(url) ? "" : url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : Site + (url.StartsWith('/') ? url : "/" + url);

    private static async Task<List<Slide>> Load()
    {
        var json = await Core.Ibra.IbraTools.Client.GetStringAsync($"{Site}/api/apps");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
                 : doc.RootElement.TryGetProperty("apps", out var a) ? a : default;
        var list = new List<Slide>();
        if (root.ValueKind != JsonValueKind.Array) return list;
        foreach (var app in root.EnumerateArray())
        {
            string S(string key) => app.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var name = S("name");
            var slug = S("slug");
            if (name.Length == 0 || slug.Length == 0) continue;
            var category = app.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array && cats.GetArrayLength() > 0
                ? cats[0].GetString() ?? "" : S("category");
            var shot = app.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array && shots.GetArrayLength() > 0
                ? shots[0].GetString() ?? "" : "";
            var icon = Absolute(S("icon"));
            DateTime.TryParse(S("createdAt"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var created);
            list.Add(new Slide(name, slug, S("tagline"), category, icon, shot.Length > 0 ? Absolute(shot) : icon, created));
        }
        return list.OrderByDescending(s => s.Created).Take(Count).ToList();
    }

    private static readonly Dictionary<string, Task<BitmapSource?>> Images = [];

    /// <summary>Downloads a picture once. The store keeps WebP, so it is decoded with ImageMagick rather than relying on a Windows codec.</summary>
    private static Task<BitmapSource?> Picture(string url, int width)
    {
        if (url.Length == 0) return Task.FromResult<BitmapSource?>(null);
        var key = url + "|" + width;
        if (Images.TryGetValue(key, out var cached)) return cached;
        var task = Task.Run<BitmapSource?>(async () =>
        {
            try
            {
                var bytes = await Core.Ibra.IbraTools.Client.GetByteArrayAsync(url);
                using var image = new ImageMagick.MagickImage(bytes);
                if (image.Width > width) image.Resize((uint)width, 0);
                var png = image.ToByteArray(ImageMagick.MagickFormat.Png);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(png);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception) { return null; }
        });
        Images[key] = task;
        return task;
    }

    private static void Open(Slide slide)
    {
        try { Process.Start(new ProcessStartInfo($"{Site}/app/{Uri.EscapeDataString(slide.Slug)}") { UseShellExecute = true }); } catch (Exception) { }
    }

    /// <summary>The slideshow box. Collapsed until it has something to show.</summary>
    public static Border Make()
    {
        var accent = AccentBrush;
        var host = new Border
        {
            Height = 196, CornerRadius = new CornerRadius(14), ClipToBounds = true, Margin = new Thickness(0, 8, 0, 0),
            Background = Theme.Card, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed,
        };
        var stage = new Grid();
        host.Child = stage;

        // Background picture with a shade over it so the text stays readable on any screenshot.
        var picture = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stage.Children.Add(picture);
        var shadeStart = Theme.IsDark ? Color.FromArgb(0xF0, 0x10, 0x10, 0x12) : Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);
        var shadeEnd = Theme.IsDark ? Color.FromArgb(0x40, 0x10, 0x10, 0x12) : Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF);
        stage.Children.Add(new Border
        {
            Background = new LinearGradientBrush(shadeStart, shadeEnd, Theme.Direction == FlowDirection.RightToLeft ? 0 : 180),
        });

        // Content: icon, category, name, tagline, button.
        var content = new Grid { Margin = new Thickness(18, 14, 18, 30), FlowDirection = Theme.Direction };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var iconBox = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(14), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Background = Theme.ButtonFill };
        var icon = new Image { Stretch = Stretch.UniformToFill };
        iconBox.Child = icon;
        content.Children.Add(iconBox);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        var category = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 2), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        var categoryText = new TextBlock { FontFamily = Theme.Font, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
        category.Child = categoryText;
        var name = new TextBlock { FontFamily = Theme.Display, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Theme.Ink, TextTrimming = TextTrimming.CharacterEllipsis };
        var tagline = new TextBlock { FontFamily = Theme.Font, FontSize = 12.5, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, MaxHeight = 36, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 8) };
        // A pill in the slideshow's main colour; clicking it (or anywhere on the slide) opens the program's page.
        var view = new Border
        {
            Background = accent, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 5, 14, 6), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = L10n.IsArabic ? "عرض البرنامج ←" : "View app →", FontFamily = Theme.Font, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
        };
        view.MouseEnter += (_, _) => view.Opacity = 0.88;
        view.MouseLeave += (_, _) => view.Opacity = 1;
        text.Children.Add(category);
        text.Children.Add(name);
        text.Children.Add(tagline);
        text.Children.Add(view);
        content.Children.Add(text);
        stage.Children.Add(content);

        // Arrows and dots.
        Button Arrow(string glyph, string help, HorizontalAlignment side)
        {
            var b = UI.IconButton(glyph, () => { }, help, 30);
            b.HorizontalAlignment = side;
            b.VerticalAlignment = VerticalAlignment.Bottom;
            b.Margin = new Thickness(8, 0, 8, 6);
            return b;
        }
        var previous = Arrow("", L10n.IsArabic ? "السابق" : "Previous", HorizontalAlignment.Left);
        var next = Arrow("", L10n.IsArabic ? "التالي" : "Next", HorizontalAlignment.Right);
        var arrows = new Grid { FlowDirection = FlowDirection.LeftToRight };
        arrows.Children.Add(previous);
        arrows.Children.Add(next);
        stage.Children.Add(arrows);
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12), FlowDirection = FlowDirection.LeftToRight };
        stage.Children.Add(dots);

        var slides = new List<Slide>();
        var index = 0;
        var paused = false;
        var timer = new DispatcherTimer { Interval = Interval };

        async void Show(int to, bool animate)
        {
            if (slides.Count == 0) return;
            index = (to % slides.Count + slides.Count) % slides.Count;
            var slide = slides[index];
            var mine = index;
            if (animate) content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(380)));
            categoryText.Text = slide.Category;
            category.Visibility = slide.Category.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            name.Text = slide.Name;
            tagline.Text = slide.Tagline;
            tagline.Visibility = slide.Tagline.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            host.ToolTip = slide.Name;
            for (var i = 0; i < dots.Children.Count; i++)
            {
                var dot = (Border)dots.Children[i];
                dot.Width = i == index ? 18 : 7;
                dot.Background = i == index ? accent : Theme.Muted;
                dot.Opacity = i == index ? 1 : 0.5;
            }
            icon.Source = null;
            picture.Source = null;
            var iconTask = Picture(slide.Icon, 160);
            var backTask = Picture(slide.Background, 900);
            var iconImage = await iconTask;
            if (mine != index) return;
            icon.Source = iconImage;
            iconBox.Visibility = iconImage != null ? Visibility.Visible : Visibility.Collapsed;
            var back = await backTask;
            if (mine != index) return;
            picture.Source = back;
            if (animate && back != null) picture.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
            // Load the next one early so the change is smooth.
            if (slides.Count > 1) { _ = Picture(slides[(index + 1) % slides.Count].Background, 900); _ = Picture(slides[(index + 1) % slides.Count].Icon, 160); }
        }

        void Restart()
        {
            timer.Stop();
            if (!paused && slides.Count > 1) timer.Start();
        }

        timer.Tick += (_, _) => Show(index + 1, true);
        previous.Click += (_, _) => { Show(index - 1, true); Restart(); };
        next.Click += (_, _) => { Show(index + 1, true); Restart(); };
        host.MouseEnter += (_, _) => { paused = true; timer.Stop(); };
        host.MouseLeave += (_, _) => { paused = false; Restart(); };
        host.Cursor = Cursors.Hand;
        host.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && FindButton(source) != null) return;
            if (slides.Count > 0) Open(slides[index]);
        };

        host.Loaded += async (_, _) =>
        {
            if (slides.Count > 0) return;
            try
            {
                slides = await Load();
            }
            catch (Exception e)
            {
                Log.Write($"store slides: {e.Message}");
                return; // stays hidden
            }
            if (slides.Count == 0) return;
            dots.Children.Clear();
            for (var i = 0; i < slides.Count; i++)
            {
                var at = i;
                var dot = new Border { Height = 7, Width = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand };
                dot.MouseLeftButtonUp += (_, e) => { e.Handled = true; Show(at, true); Restart(); };
                dots.Children.Add(dot);
            }
            var single = slides.Count < 2 ? Visibility.Collapsed : Visibility.Visible;
            previous.Visibility = next.Visibility = dots.Visibility = single;
            host.Visibility = Visibility.Visible;
            Show(0, false);
            Restart();
        };
        host.Unloaded += (_, _) => timer.Stop();
        return host;
    }

    private static System.Windows.Controls.Primitives.ButtonBase? FindButton(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase button) return button;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
}
