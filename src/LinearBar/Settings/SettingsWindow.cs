using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LinearBar.Core.Config;
using LinearBar.Core.Dock;
using LinearBar.Plugins;
using LinearBar.Visuals;

namespace LinearBar.Settings;

/// <summary>
/// "Personalizar a barra": every setting of the bar in one squircle glass window. Changes
/// are saved and applied to the running bar immediately (debounced), so the bar itself is
/// the live preview.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static readonly (string Name, string Hex)[] AccentPresets =
    [
        ("Azul", "#5AA9FF"), ("Violeta", "#A78BFA"), ("Rosa", "#FF6FAE"), ("Laranja", "#FF9F45"),
        ("Âmbar", "#FFC54D"), ("Verde", "#4FD1A5"), ("Turquesa", "#2DD4E6"), ("Vermelho", "#FF5F57"),
    ];

    private static readonly (string Name, string Hex)[] GlassPresets =
    [
        ("Grafite", "#101016"), ("Noite", "#0B1530"), ("Ameixa", "#1E1030"), ("Floresta", "#0C2018"),
        ("Café", "#22160E"), ("Névoa", "#E8ECF4"),
    ];

    private static readonly (string Name, string Hex)[] TextPresets = [("Claro", "#F2F2F5"), ("Escuro", "#16161C")];

    private static readonly string[] Fonts = ["Segoe UI Variable Text, Segoe UI", "Segoe UI", "Inter, Segoe UI", "Bahnschrift", "Cascadia Code, Consolas"];

    private readonly SettingsStore _store;
    private readonly Action _apply;
    private readonly PluginHost _plugins;
    private readonly DispatcherTimer _debounce;
    private readonly ContentControl _page = new();
    private readonly StackPanel _nav = new();
    private string _currentPage = "appearance";

    public SettingsWindow(SettingsStore store, Action apply, PluginHost plugins)
    {
        _store = store;
        _apply = apply;
        _plugins = plugins;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _apply();
        };

        Title = "Personalizar o LinearBar";
        Width = 900;
        Height = 640;
        MinWidth = 720;
        MinHeight = 480;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "TextBrush");
        TextElement.SetFontSize(this, 12.5);

        Content = BuildShell();
        ShowPage(_currentPage);
    }

    /// <summary>Re-reads the settings (after the file was edited by hand).</summary>
    public void Reload() => ShowPage(_currentPage);

    private BarSettings Settings => _store.Current;

    // ---- Shell --------------------------------------------------------------------------

    private UIElement BuildShell()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Title bar: drag to move, squircle close button.
        var title = new Grid { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent };
        title.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new StartGlyph { Width = 18, Height = 18, Margin = new Thickness(4, 0, 10, 0) });
        ((StartGlyph)heading.Children[0]).SetResourceReference(StartGlyph.FillProperty, "AccentBrush");
        heading.Children.Add(new TextBlock { Text = "Personalizar o LinearBar", FontSize = 16, FontWeight = FontWeights.SemiBold });
        title.Children.Add(heading);

        var close = new Button { Content = new TextBlock { Text = "✕", FontSize = 13 }, Width = 34, Height = 30, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Fechar" };
        close.SetResourceReference(StyleProperty, "BarButton");
        close.Click += (_, _) => Close();
        title.Children.Add(close);
        Grid.SetColumnSpan(title, 2);
        root.Children.Add(title);

        // Navigation.
        foreach ((string id, string icon, string text) in new[]
        {
            ("appearance", "sparkle", "Aparência"),
            ("layout", "settings", "Disposição"),
            ("dock", "gauge", "Dock"),
            ("plugins", "plug", "Plugins"),
            ("widgets", "clock", "Relógio e gráficos"),
            ("general", "cpu", "Geral"),
        })
        {
            var entry = new StackPanel { Orientation = Orientation.Horizontal };
            var iconView = new IconView { Width = 16, Height = 16, Icon = icon, Margin = new Thickness(0, 0, 10, 0) };
            iconView.SetResourceReference(IconView.BrushProperty, "AccentBrush");
            entry.Children.Add(iconView);
            entry.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

            var radio = new RadioButton { Content = entry, GroupName = "nav", Tag = id, IsChecked = id == _currentPage };
            radio.SetResourceReference(StyleProperty, "NavRadio");
            radio.Checked += (_, _) => ShowPage(id);
            _nav.Children.Add(radio);
        }

        Grid.SetRow(_nav, 1);
        root.Children.Add(_nav);

        var scroll = new ScrollViewer { Content = _page, Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(0, 0, 8, 0) };
        scroll.SetResourceReference(StyleProperty, "GlassScrollViewer");
        Grid.SetRow(scroll, 1);
        Grid.SetColumn(scroll, 1);
        root.Children.Add(scroll);

        return new GlassPanel { Solid = true, Radius = 28, Padding = new Thickness(8), Margin = new Thickness(10), Child = root };
    }

    private void ShowPage(string id)
    {
        _currentPage = id;
        var page = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        switch (id)
        {
            case "appearance":
                BuildAppearance(page);
                break;
            case "layout":
                BuildLayout(page);
                break;
            case "dock":
                BuildDock(page);
                break;
            case "plugins":
                BuildPlugins(page);
                break;
            case "widgets":
                BuildWidgets(page);
                break;
            default:
                BuildGeneral(page);
                break;
        }

        _page.Content = page;
    }

    /// <summary>Changes a setting, saves it and applies it to the bar shortly after.</summary>
    private void Change(Action<BarSettings> change, bool rebuildPage = false)
    {
        _store.Update(change);
        _debounce.Stop();
        _debounce.Start();
        if (rebuildPage)
        {
            ShowPage(_currentPage);
        }
    }

    // ---- Pages ----------------------------------------------------------------------------

    private void BuildAppearance(StackPanel page)
    {
        AppearanceSettings a = Settings.Appearance;

        Section(page, "Barra", "Onde fica e que forma tem.");
        Row(page, "Posição", null, Choices(
            [("bottom", "Em baixo"), ("top", "Em cima")], Settings.Layout.Position, v => Change(s => s.Layout.Position = v)));
        Row(page, "Estilo", "Ilhas flutuantes ou uma barra contínua de lado a lado.", Choices(
            [("islands", "Ilhas"), ("bar", "Barra contínua")], Settings.Layout.Style, v => Change(s => s.Layout.Style = v)));
        Row(page, "Altura", null, SliderWith(Settings.BarHeight, 40, 110, 1, "0 px", v => Change(s => s.BarHeight = v)));
        Row(page, "Espaço à volta das ilhas", null, SliderWith(a.IslandMargin, 0, 16, 1, "0 px", v => Change(s => s.Appearance.IslandMargin = v)));

        Section(page, "Squircles", "A forma dos cantos de tudo na barra.");
        Row(page, "Arredondamento", "De cantos retos (0) a pílula (0,5).", SliderWith(a.CornerRoundness, 0, 0.5, 0.01, "0.00", v => Change(s => s.Appearance.CornerRoundness = v)));
        Row(page, "Suavização dos cantos", "0 = cantos circulares comuns · 0,6 = como a Apple · 1 = máxima.", SliderWith(a.CornerSmoothing, 0, 1, 0.05, "0.00", v => Change(s => s.Appearance.CornerSmoothing = v)));

        Section(page, "Vidro", null);
        Row(page, "Desfoque", "Vidro fosco: desfoca o que está por trás das ilhas.", Switch(a.Blur, v => Change(s => s.Appearance.Blur = v)));
        Row(page, "Opacidade do vidro", null, SliderWith(Settings.BarOpacity, 0.2, 1, 0.05, "0.00", v => Change(s => s.BarOpacity = v)));
        Row(page, "Cor do vidro", null, Swatches(GlassPresets, a.GlassColor, allowSystem: false, v => Change(s => s.Appearance.GlassColor = v, rebuildPage: true)));

        Section(page, "Cores e texto", null);
        Row(page, "Cor de destaque", null, Swatches(AccentPresets, a.Accent, allowSystem: true, v => Change(s => s.Appearance.Accent = v, rebuildPage: true)));
        Row(page, "Cor do texto", "Use texto escuro com vidro claro.", Swatches(TextPresets, a.TextColor, allowSystem: false, v => Change(s => s.Appearance.TextColor = v, rebuildPage: true)));
        Row(page, "Tipo de letra", null, Choices(
            Fonts.Select(f => (f, f.Split(',')[0])).ToArray(), a.FontFamily, v => Change(s => s.Appearance.FontFamily = v)));
    }

    private void BuildLayout(StackPanel page)
    {
        Section(page, "Disposição", "Escolha o que aparece em cada zona da barra e por que ordem. «Separador» divide uma zona em duas ilhas. O «Dock» pode ir para qualquer zona.");

        var zones = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < 3; i++)
        {
            zones.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        LayoutSettings layout = Settings.Layout;
        (string Key, string Title, List<string> Items)[] lists =
        [
            ("left", "Esquerda", layout.Left),
            ("center", "Centro", layout.Center),
            ("right", "Direita", layout.Right),
        ];

        for (int z = 0; z < lists.Length; z++)
        {
            int zoneIndex = z;
            var column = new StackPanel { Margin = new Thickness(z == 0 ? 0 : 6, 0, z == 2 ? 0 : 6, 0) };
            column.Children.Add(new TextBlock { Text = lists[z].Title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 8) });

            var box = new SquircleBorder { Radius = 16, Padding = new Thickness(6), MinHeight = 120, AllowDrop = true };
            box.SetResourceReference(SquircleBorder.BackgroundProperty, "FieldBrush");
            var stack = new StackPanel();
            box.Child = stack;

            List<string> items = lists[z].Items;
            for (int i = 0; i < items.Count; i++)
            {
                stack.Children.Add(LayoutChip(zoneIndex, i, items[i], items.Count));
            }

            if (items.Count == 0)
            {
                stack.Children.Add(new TextBlock { Text = "Vazio — arraste algo para aqui", Opacity = 0.5, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap });
            }

            box.Drop += (_, e) =>
            {
                if (e.Data.GetData(typeof(string)) is string payload)
                {
                    int insertAt = DropIndex(stack, e);
                    MoveItem(payload, zoneIndex, insertAt);
                }
            };
            box.DragOver += (_, e) => e.Effects = DragDropEffects.Move;

            column.Children.Add(box);
            Grid.SetColumn(column, z);
            zones.Children.Add(column);
        }

        page.Children.Add(zones);

        // Everything that is not placed yet.
        var placed = new HashSet<string>(layout.Left.Concat(layout.Center).Concat(layout.Right));
        List<string> available = LayoutSettings.CoreItems
            .Where(id => id == "gap" || !placed.Contains(id))
            .Concat(_plugins.Widgets.Select(w => w.Id).Where(id => !placed.Contains(id)))
            .Distinct()
            .ToList();

        Section(page, "Disponíveis", "Clique para acrescentar à zona da esquerda, do centro ou da direita.");
        var wrap = new WrapPanel();
        foreach (string id in available)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal };
            chip.Children.Add(new TextBlock { Text = ItemName(id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            foreach ((string label, int zone) in new[] { ("◀", 0), ("●", 1), ("▶", 2) })
            {
                string captured = id;
                var add = SmallButton(label, $"Acrescentar à {(zone == 0 ? "esquerda" : zone == 1 ? "zona central" : "direita")}", () => AddItem(captured, zone));
                chip.Children.Add(add);
            }

            var box = new SquircleBorder { Radius = 12, Padding = new Thickness(10, 4, 4, 4), Margin = new Thickness(0, 0, 8, 8), Child = chip };
            box.SetResourceReference(SquircleBorder.BackgroundProperty, "FieldBrush");
            wrap.Children.Add(box);
        }

        page.Children.Add(wrap);

        var reset = SmallButton("Repor disposição original", null, () => Change(s => s.Layout = new LayoutSettings(), rebuildPage: true));
        reset.Margin = new Thickness(0, 12, 0, 0);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(reset);
    }

    private FrameworkElement LayoutChip(int zone, int index, string id, int count)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock { Text = ItemName(id), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 6, 0) };
        if (id == "gap")
        {
            name.Opacity = 0.6;
        }

        grid.Children.Add(name);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (index > 0)
        {
            buttons.Children.Add(SmallButton("▲", "Subir", () => MoveItem(SlotKey(zone, index), zone, index - 1)));
        }

        if (index < count - 1)
        {
            buttons.Children.Add(SmallButton("▼", "Descer", () => MoveItem(SlotKey(zone, index), zone, index + 2)));
        }

        if (zone > 0)
        {
            buttons.Children.Add(SmallButton("◀", "Mover para a zona à esquerda", () => MoveItem(SlotKey(zone, index), zone - 1, int.MaxValue)));
        }

        if (zone < 2)
        {
            buttons.Children.Add(SmallButton("▶", "Mover para a zona à direita", () => MoveItem(SlotKey(zone, index), zone + 1, int.MaxValue)));
        }

        buttons.Children.Add(SmallButton("✕", "Retirar da barra", () => RemoveItem(zone, index)));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var chip = new SquircleBorder { Radius = 11, Padding = new Thickness(6, 3, 3, 3), Margin = new Thickness(0, 0, 0, 5), Child = grid, Cursor = Cursors.SizeAll };
        chip.Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255));
        chip.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not Button)
            {
                DragDrop.DoDragDrop(chip, SlotKey(zone, index), DragDropEffects.Move);
            }
        };
        return chip;
    }

    private static string SlotKey(int zone, int index) => zone.ToString(CultureInfo.InvariantCulture) + ":" + index.ToString(CultureInfo.InvariantCulture);

    private static int DropIndex(StackPanel stack, DragEventArgs e)
    {
        for (int i = 0; i < stack.Children.Count; i++)
        {
            if (stack.Children[i] is FrameworkElement child && e.GetPosition(child).Y < child.ActualHeight / 2)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private List<string> Zone(BarSettings s, int zone) => zone switch
    {
        0 => s.Layout.Left,
        1 => s.Layout.Center,
        _ => s.Layout.Right,
    };

    /// <summary>Moves the item at "zone:index" to another position (insert index in the target zone).</summary>
    private void MoveItem(string key, int targetZone, int targetIndex)
    {
        string[] parts = key.Split(':');
        int fromZone = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int fromIndex = int.Parse(parts[1], CultureInfo.InvariantCulture);

        Change(s =>
        {
            List<string> from = Zone(s, fromZone);
            if (fromIndex >= from.Count)
            {
                return;
            }

            string id = from[fromIndex];
            from.RemoveAt(fromIndex);

            List<string> to = Zone(s, targetZone);
            if (fromZone == targetZone && targetIndex > fromIndex)
            {
                targetIndex--;
            }

            to.Insert(Math.Clamp(targetIndex, 0, to.Count), id);
        }, rebuildPage: true);
    }

    private void AddItem(string id, int zone) => Change(s => Zone(s, zone).Add(id), rebuildPage: true);

    private void RemoveItem(int zone, int index) => Change(s =>
    {
        List<string> items = Zone(s, zone);
        if (index < items.Count)
        {
            items.RemoveAt(index);
        }
    }, rebuildPage: true);

    private string ItemName(string id) => id switch
    {
        "start" => "Iniciar",
        "dock" => "Dock",
        "cpu" => "Gráfico de CPU",
        "memory" => "Gráfico de RAM",
        "network" => "Gráfico de rede",
        "battery" => "Bateria",
        "tray" => "Ícones do sistema",
        "clock" => "Relógio",
        "gap" => "— separador —",
        _ => _plugins.Widgets.FirstOrDefault(w => w.Id == id)?.Name ?? id,
    };

    private void BuildDock(StackPanel page)
    {
        DockSettings d = Settings.Dock;
        double maxIcon = BarGeometry.MaxIconSize(Settings.BarHeight, Settings.Appearance.IslandMargin);

        Section(page, "Ícones", null);
        Row(page, "Tamanho dos ícones", $"Até {maxIcon:0} px com a altura atual da barra.", SliderWith(d.IconSize, 16, Math.Max(16, maxIcon), 1, "0 px", v => Change(s => s.Dock.IconSize = v)));
        Row(page, "Espaço entre ícones", null, SliderWith(d.Spacing, 0, 32, 1, "0 px", v => Change(s => s.Dock.Spacing = v)));
        Row(page, "Ícones em squircles", "Põe cada ícone num tile de vidro, para todos terem a mesma forma.", Switch(d.IconTiles, v => Change(s => s.Dock.IconTiles = v)));
        Row(page, "Nome ao passar o rato", null, Switch(d.ShowLabels, v => Change(s => s.Dock.ShowLabels = v)));

        Section(page, "Ampliação", "Os ícones crescem à volta do cursor, como no macOS.");
        Row(page, "Ampliar", null, Switch(d.Magnify, v => Change(s => s.Dock.Magnify = v)));
        Row(page, "Tamanho máximo", "Escala do ícone debaixo do cursor.", SliderWith(d.MaxScale, 1, 3, 0.05, "0.00×", v => Change(s => s.Dock.MaxScale = v)));
        Row(page, "Alcance", "Quantos ícones de cada lado também crescem.", SliderWith(d.LensRadius, 1, 6, 0.25, "0.0", v => Change(s => s.Dock.LensRadius = v)));
    }

    private void BuildPlugins(StackPanel page)
    {
        Section(page, "Plugins", "Ligue, desligue e configure cada plugin. Os seus próprios plugins vão em %APPDATA%\\LinearBar\\plugins.");

        IEnumerable<(string Id, string Name)> all = _plugins.Widgets.Select(w => (w.Id, w.Name))
            .Concat(Settings.Plugins.Where(p => !p.Value.Enabled).Select(p => (p.Key, PluginName(p.Key))))
            .DistinctBy(p => p.Item1);

        foreach ((string id, string name) in all)
        {
            PluginConfig config = Settings.Plugins.GetValueOrDefault(id) ?? new PluginConfig();
            bool enabled = _plugins.Widgets.Any(w => w.Id == id) && config.Enabled;

            var card = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new IconView { Width = 18, Height = 18, Icon = _plugins.Widgets.FirstOrDefault(w => w.Id == id)?.Icon ?? "plug", Margin = new Thickness(0, 0, 10, 0) };
            icon.SetResourceReference(IconView.BrushProperty, "AccentBrush");
            title.Children.Add(icon);
            title.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(title);
            ToggleButton toggle = Switch(enabled, v => Change(s => Config(s, id).Enabled = v));
            Grid.SetColumn(toggle, 1);
            head.Children.Add(toggle);
            card.Children.Add(head);

            foreach ((string option, string label, string? hint, bool secret) in PluginOptions(id))
            {
                card.Children.Add(OptionField(id, option, label, hint, secret, config.GetString(option)));
            }

            var box = new SquircleBorder { Radius = 16, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = card };
            box.SetResourceReference(SquircleBorder.BackgroundProperty, "FieldBrush");
            page.Children.Add(box);
        }

        var folder = SmallButton("Abrir pasta de plugins", null, () =>
        {
            System.IO.Directory.CreateDirectory(PluginHost.UserPluginsFolder);
            Process.Start(new ProcessStartInfo(PluginHost.UserPluginsFolder) { UseShellExecute = true });
        });
        folder.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(folder);
    }

    private static string PluginName(string id) => id switch
    {
        "spotify" => "Spotify",
        "nowplaying" => "A tocar",
        "calendar" => "Calendário",
        "github" => "GitHub",
        "weather" => "Meteorologia",
        "claude" => "Claude",
        "codex" => "Codex",
        "agy" => "Antigravity",
        "ping" => "Latência",
        _ => id,
    };

    private IEnumerable<(string Option, string Label, string? Hint, bool Secret)> PluginOptions(string id)
    {
        switch (id)
        {
            case "weather":
                yield return ("city", "Cidade", "Vazio = localização aproximada pelo IP", false);
                yield return ("units", "Unidades", "celsius ou fahrenheit", false);
                break;
            case "calendar":
                yield return ("icsUrl", "Endereço iCal", "Google: «Endereço secreto em formato iCal» · Outlook: calendário publicado (ICS). Vários: separe com espaços.", false);
                yield return ("days", "Dias a mostrar", "1 a 14", false);
                break;
            case "github":
                yield return ("token", "Token", "Vazio = usa o GitHub CLI (gh auth login) ou GH_TOKEN", true);
                break;
            case "claude":
                yield return ("blockTokenLimit", "Orçamento de tokens por 5 h", "Opcional: mostra a percentagem deste valor", false);
                break;
            case "ping":
                yield return ("host", "Servidor", null, false);
                break;
            case "agy":
                yield return ("command", "Comando", "Normalmente «agy»", false);
                break;
            default:
                // Script plugins: offer the options their manifest declares.
                if (_plugins.Widgets.FirstOrDefault(w => w.Id == id)?.Plugin is ScriptPlugin script)
                {
                    foreach (string option in script.Manifest.Options.Keys)
                    {
                        yield return (option, option, null, false);
                    }
                }

                break;
        }
    }

    private FrameworkElement OptionField(string plugin, string option, string label, string? hint, bool secret, string? value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(2, 0, 0, 4) });

        void Save(string text) => Change(s =>
        {
            PluginConfig config = Config(s, plugin);
            if (string.IsNullOrWhiteSpace(text))
            {
                config.Options.Remove(option);
            }
            else
            {
                config.Options[option] = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && option != "city"
                    ? JsonSerializer.SerializeToElement(number)
                    : JsonSerializer.SerializeToElement(text.Trim());
            }
        });

        if (secret)
        {
            var box = new PasswordBox { Password = value ?? string.Empty };
            box.SetResourceReference(StyleProperty, "GlassPasswordBox");
            box.LostKeyboardFocus += (_, _) => Save(box.Password);
            panel.Children.Add(box);
        }
        else
        {
            var box = new TextBox { Text = value ?? string.Empty };
            box.SetResourceReference(StyleProperty, "GlassTextBox");
            box.LostKeyboardFocus += (_, _) => Save(box.Text);
            box.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    Save(box.Text);
                }
            };
            panel.Children.Add(box);
        }

        if (hint is not null)
        {
            var hintText = new TextBlock { Text = hint, FontSize = 11, Margin = new Thickness(2, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            hintText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            panel.Children.Add(hintText);
        }

        return panel;
    }

    private static PluginConfig Config(BarSettings settings, string id)
    {
        if (!settings.Plugins.TryGetValue(id, out PluginConfig? config) || config is null)
        {
            config = new PluginConfig();
            settings.Plugins[id] = config;
        }

        return config;
    }

    private void BuildWidgets(StackPanel page)
    {
        ClockSettings c = Settings.Clock;
        MetricsSettings m = Settings.Metrics;

        Section(page, "Relógio", null);
        Row(page, "Mostrar data", null, Switch(c.ShowDate, v => Change(s => s.Clock.ShowDate = v)));
        Row(page, "Formato da hora", "Ex.: HH:mm · HH:mm:ss · h:mm tt", TextField(c.TimeFormat, v => Change(s => s.Clock.TimeFormat = v)));
        Row(page, "Formato da data", "Ex.: ddd, d MMM · dd/MM/yyyy", TextField(c.DateFormat, v => Change(s => s.Clock.DateFormat = v)));

        Section(page, "Gráficos do sistema", "Quais aparecem é escolhido em «Disposição».");
        Row(page, "Ativos", null, Switch(m.Enabled, v => Change(s => s.Metrics.Enabled = v)));
        Row(page, "Atualizar a cada", null, SliderWith(m.IntervalMs, 250, 5000, 250, "0 ms", v => Change(s => s.Metrics.IntervalMs = (int)v)));
        Row(page, "Histórico", "Pontos em cada gráfico.", SliderWith(m.HistoryLength, 10, 300, 10, "0", v => Change(s => s.Metrics.HistoryLength = (int)v)));
    }

    private void BuildGeneral(StackPanel page)
    {
        Section(page, "Geral", null);
        Row(page, "Esconder a taskbar do Windows", "É sempre reposta quando o LinearBar fecha.", Switch(Settings.HideWindowsTaskbar, v => Change(s => s.HideWindowsTaskbar = v)));
        Row(page, "Ícones do sistema (tray)", "Requer reiniciar o LinearBar.", Switch(Settings.ShowTray, v => Change(s => s.ShowTray = v)));

        Section(page, "Ficheiro de definições", _store.FilePath);
        var buttons = new WrapPanel();
        buttons.Children.Add(SmallButton("Abrir ficheiro", null, () => Process.Start(new ProcessStartInfo(_store.FilePath) { UseShellExecute = true })));
        buttons.Children.Add(SmallButton("Repor tudo", "Volta às definições de origem (mantém as apps fixadas)", () =>
        {
            if (MessageBox.Show(this, "Repor todas as definições de origem? As apps fixadas mantêm-se.", "LinearBar", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                Change(s =>
                {
                    var fresh = new BarSettings { Pinned = s.Pinned, ImportedTaskbarPins = true };
                    s.BarHeight = fresh.BarHeight;
                    s.BarOpacity = fresh.BarOpacity;
                    s.Appearance = fresh.Appearance;
                    s.Layout = fresh.Layout;
                    s.Dock = fresh.Dock;
                    s.Metrics = fresh.Metrics;
                    s.Clock = fresh.Clock;
                }, rebuildPage: true);
            }
        }));
        page.Children.Add(buttons);
    }

    // ---- Building blocks -------------------------------------------------------------------

    private static void Section(StackPanel page, string title, string? description)
    {
        page.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, page.Children.Count == 0 ? 0 : 18, 0, 4) });
        if (description is not null)
        {
            var text = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            page.Children.Add(text);
        }
    }

    private static void Row(StackPanel page, string label, string? hint, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        labels.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (hint is not null)
        {
            var text = new TextBlock { Text = hint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            labels.Children.Add(text);
        }

        grid.Children.Add(labels);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        page.Children.Add(grid);
    }

    private static ToggleButton Switch(bool value, Action<bool> changed)
    {
        var toggle = new ToggleButton { IsChecked = value, HorizontalAlignment = HorizontalAlignment.Left };
        toggle.SetResourceReference(StyleProperty, "SwitchToggle");
        toggle.Checked += (_, _) => changed(true);
        toggle.Unchecked += (_, _) => changed(false);
        return toggle;
    }

    private static FrameworkElement SliderWith(double value, double min, double max, double step, string format, Action<double> changed)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), SmallChange = step, LargeChange = step * 4, TickFrequency = step, IsSnapToTickEnabled = true };
        slider.SetResourceReference(StyleProperty, "GlassSlider");
        var label = new TextBlock { Text = slider.Value.ToString(format, CultureInfo.CurrentCulture), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            label.Text = e.NewValue.ToString(format, CultureInfo.CurrentCulture);
            changed(e.NewValue);
        };

        grid.Children.Add(slider);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        return grid;
    }

    private static FrameworkElement Choices((string Value, string Label)[] options, string current, Action<string> changed)
    {
        var wrap = new WrapPanel();
        string group = Guid.NewGuid().ToString("N");
        foreach ((string value, string label) in options)
        {
            var radio = new RadioButton { Content = label, GroupName = group, IsChecked = string.Equals(value, current, StringComparison.OrdinalIgnoreCase) };
            radio.SetResourceReference(StyleProperty, "ChipRadio");
            radio.Checked += (_, _) => changed(value);
            wrap.Children.Add(radio);
        }

        return wrap;
    }

    private static FrameworkElement Swatches((string Name, string Hex)[] presets, string current, bool allowSystem, Action<string> changed)
    {
        var wrap = new WrapPanel();

        void AddSwatch(string name, string value, Brush fill)
        {
            bool selected = string.Equals(value, current, StringComparison.OrdinalIgnoreCase);
            var swatch = new SquircleBorder
            {
                Width = 30,
                Height = 30,
                Radius = 9,
                Margin = new Thickness(0, 0, 8, 8),
                Background = fill,
                BorderBrush = selected ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
                Cursor = Cursors.Hand,
                ToolTip = name,
            };
            if (selected)
            {
                swatch.Child = new IconView { Icon = "check", Width = 14, Height = 14, Brush = IsLight(fill) ? Brushes.Black : Brushes.White };
            }

            swatch.MouseLeftButtonUp += (_, _) => changed(value);
            wrap.Children.Add(swatch);
        }

        if (allowSystem)
        {
            AddSwatch("Cor do Windows", "system", new SolidColorBrush(ThemeManager.SystemAccent() ?? ThemeManager.DefaultAccent));
        }

        foreach ((string name, string hex) in presets)
        {
            AddSwatch(name, hex, new SolidColorBrush(ThemeManager.Parse(hex) ?? Colors.Gray));
        }

        var custom = new TextBox { Text = current, Width = 96, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Cor personalizada: #RRGGBB" };
        custom.SetResourceReference(StyleProperty, "GlassTextBox");
        custom.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && ThemeManager.Parse(custom.Text) is not null)
            {
                changed(custom.Text.Trim());
            }
        };
        wrap.Children.Add(custom);
        return wrap;
    }

    private static bool IsLight(Brush brush) =>
        brush is SolidColorBrush { Color: var c } && (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 160;

    private static FrameworkElement TextField(string value, Action<string> changed)
    {
        var box = new TextBox { Text = value, MaxWidth = 280, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 180 };
        box.SetResourceReference(StyleProperty, "GlassTextBox");
        box.LostKeyboardFocus += (_, _) => changed(box.Text);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                changed(box.Text);
            }
        };
        return box;
    }

    private static Button SmallButton(string text, string? tooltip, Action click)
    {
        var button = new Button { Content = new TextBlock { Text = text, FontSize = 11.5 }, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2, 0, 2, 0), ToolTip = tooltip };
        button.SetResourceReference(StyleProperty, "ChipButton");
        button.Click += (_, _) => click();
        return button;
    }
}
