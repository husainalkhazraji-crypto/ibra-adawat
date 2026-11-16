using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Adawat.Core;

namespace Adawat.App;

/// <summary>
/// "My apps": adverts for Ibrahim's other apps, read from promo/apps.json in the GitHub repository, so a new advert is
/// published by editing that file, with no new IBMC version. The last list is kept for offline use.
/// </summary>
public static class Promo
{
    private const string FeedUrl = "https://raw.githubusercontent.com/ibrata2/ibra-adawat/main/promo/apps.json";
    private static readonly string CachePath = Path.Combine(Settings.Folder, "promo.json");

    public sealed record Item(string Name, string Ar, string En, string Link, string? Icon, string? Color, string? Badge)
    {
        public string Text => L10n.IsArabic ? Ar : En;
    }

    public static IReadOnlyList<Item> Items { get; private set; } = Parse(ReadCache());

    public static event Action? Changed;

    private static string ReadCache()
    {
        try { return File.Exists(CachePath) ? File.ReadAllText(CachePath) : ""; } catch (IOException) { return ""; }
    }

    private static IReadOnlyList<Item> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<Item>();
            foreach (var app in doc.RootElement.GetProperty("apps").EnumerateArray())
            {
                string? S(string key) => app.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var name = S("name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new Item(name, S("ar") ?? S("en") ?? "", S("en") ?? S("ar") ?? "", S("link") ?? "https://t.me/ibraprogram", S("icon"), S("color"), S("badge")));
            }
            return list;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return []; }
    }

    /// <summary>Fetches the newest list once at start; keeps the cached one when offline.</summary>
    public static async void Refresh()
    {
        try
        {
            var json = await Core.Ibra.IbraTools.Client.GetStringAsync(FeedUrl + "?t=" + DateTime.UtcNow.Ticks);
            var items = Parse(json);
            if (items.Count == 0) return;
            Items = items;
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(CachePath, json); } catch (IOException) { }
            Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
        }
        catch (Exception e) { Log.Write($"promo: {e.Message}"); }
    }

    public static void Open(Item item)
    {
        try { Process.Start(new ProcessStartInfo(item.Link) { UseShellExecute = true }); } catch (Exception) { }
    }

    public static Brush ColorOf(Item item)
    {
        try { if (item.Color is { Length: > 0 } c) return (Brush)new BrushConverter().ConvertFromString(c)!; } catch (FormatException) { }
        return new SolidColorBrush(Color.FromRgb(0xE3, 0x19, 0x2B));
    }

    /// <summary>The app's icon from its URL, or its first letters on its colour.</summary>
    public static FrameworkElement Badge(Item item, double size)
    {
        var box = new Grid { Width = size, Height = size };
        box.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.24), Background = ColorOf(item),
            Child = new TextBlock
            {
                Text = new string(item.Name.Replace("IBRA", "").Replace("iBRA", "").Trim().Where(char.IsLetterOrDigit).Take(2).ToArray()) is { Length: > 0 } t ? t.ToUpperInvariant() : "IB",
                Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = size * 0.36,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight,
            },
        });
        if (item.Icon is { Length: > 0 } icon && Uri.TryCreate(icon, UriKind.Absolute, out var uri))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = uri;
                image.DecodePixelWidth = (int)(size * 2);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                box.Children.Add(new Image { Source = image, Width = size, Height = size, Stretch = Stretch.UniformToFill,
                                             Clip = new RectangleGeometry(new Rect(0, 0, size, size), size * 0.24, size * 0.24) });
            }
            catch (Exception) { }
        }
        return box;
    }

    /// <summary>A strip that shows one advert at a time, changing every few seconds; hidden while there are none.</summary>
    public static Border Banner()
    {
        var index = 0;
        var host = new Border
        {
            Background = Theme.Card, CornerRadius = new CornerRadius(12), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 0, 0), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed,
        };
        var row = new DockPanel { LastChildFill = true };
        var label = UI.Text(L10n.IsArabic ? "من تطبيقاتي" : "My apps", 10.5, FontWeights.SemiBold, Theme.Muted);
        var all = UI.Button(L10n.IsArabic ? "كل التطبيقات" : "All apps", Present);
        DockPanel.SetDock(all, Dock.Right);
        row.Children.Add(all);
        var badge = new ContentControl { Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = UI.Text("", 13, FontWeights.SemiBold);
        var line = UI.Text("", 11.5, color: Theme.Muted);
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(label);
        text.Children.Add(name);
        text.Children.Add(line);
        row.Children.Add(text);
        host.Child = row;

        void Show()
        {
            var items = Items;
            host.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count == 0) return;
            var item = items[index % items.Count];
            badge.Content = Badge(item, 40);
            name.Text = item.Badge is { Length: > 0 } b ? $"{item.Name}  ·  {b}" : item.Name;
            line.Text = item.Text;
            host.Tag = item;
        }
        host.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not System.Windows.Controls.Primitives.ButtonBase && host.Tag is Item item) Open(item); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        timer.Tick += (_, _) => { index++; Show(); };
        timer.Start();
        Changed += Show;
        Show();
        return host;
    }

    private static ToolWindow? _window;

    /// <summary>All the adverts in one window.</summary>
    public static void Present()
    {
        if (_window != null) { _window.Activate(); return; }
        var window = new ToolWindow(L10n.IsArabic ? "تطبيقاتي" : "My apps", 520);
        var stack = new StackPanel();
        stack.Children.Add(UI.Caption(L10n.IsArabic ? "تطبيقات وألعاب من برمجة وتطوير ابراهيم الخزرجي" : "Apps and games by Ibrahim Al-Khazraji").Pad(0, 0));
        if (Items.Count == 0) stack.Children.Add(UI.Caption(L10n.IsArabic ? "تتحمّل القائمة لما يكون أكو إنترنت." : "The list loads when you are online.").Pad(0, 12));
        foreach (var item in Items)
        {
            var card = new DockPanel { LastChildFill = true };
            var open = UI.Button(L10n.IsArabic ? "فتح" : "Open", () => Open(item), primary: true);
            DockPanel.SetDock(open, Dock.Right);
            card.Children.Add(open);
            var badge = Badge(item, 46);
            badge.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(badge, Dock.Left);
            card.Children.Add(badge);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(UI.Text(item.Badge is { Length: > 0 } b ? $"{item.Name}  ·  {b}" : item.Name, 14, FontWeights.SemiBold));
            var line = UI.Text(item.Text, 12, color: Theme.Muted, wrap: true);
            text.Children.Add(line);
            card.Children.Add(text);
            stack.Children.Add(new Border { Child = card, Background = Theme.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 0) });
        }
        window.Body = new ScrollViewer { Content = stack, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Trailing(UI.Button("Telegram", () => Open(new Item("", "", "", "https://t.me/ibraprogram", null, null, null)), icon: ""));
        window.Closed += (_, _) => _window = null;
        _window = window;
        window.Present();
    }
}
