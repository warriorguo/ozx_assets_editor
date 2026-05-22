using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OAE.Core.Resources;

namespace OAE.App.Views;

public sealed record TilemapThemeRow(string Name, int Count);

public sealed class TilemapRow
{
    public string Stem { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public TilemapEntry Entry { get; init; } = null!;
}

public partial class TilemapBrowserWindow : Window
{
    private const string AnyToken = "(any)";

    private TextBlock _statusText = null!;
    private TextBlock _resultCount = null!;
    private TextBlock _previewName = null!;
    private TextBlock _previewKey = null!;
    private Image _previewImage = null!;
    private StackPanel _detailHost = null!;
    private ListBox _themeList = null!;
    private ListBox _fileList = null!;
    private ComboBox _shapeFilter = null!;
    private ComboBox _categoryFilter = null!;
    private ComboBox _stageFilter = null!;
    private ComboBox _doorsFilter = null!;
    private TextBox _nameFilter = null!;

    private readonly ObservableCollection<TilemapThemeRow> _themes = new();
    private readonly ObservableCollection<TilemapRow> _files = new();

    private TilemapIndex? _index;
    private string? _selectedTheme;
    private List<TilemapRow> _allRowsForTheme = new();
    // Doc cache so we re-parse each file once per Configure().
    private readonly Dictionary<string, TilemapDocument> _docCache = new();

    public TilemapBrowserWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _resultCount = this.FindControl<TextBlock>("ResultCount")!;
        _previewName = this.FindControl<TextBlock>("PreviewName")!;
        _previewKey = this.FindControl<TextBlock>("PreviewKey")!;
        _previewImage = this.FindControl<Image>("PreviewImage")!;
        _detailHost = this.FindControl<StackPanel>("DetailHost")!;
        _themeList = this.FindControl<ListBox>("ThemeList")!;
        _fileList = this.FindControl<ListBox>("FileList")!;
        _shapeFilter = this.FindControl<ComboBox>("ShapeFilter")!;
        _categoryFilter = this.FindControl<ComboBox>("CategoryFilter")!;
        _stageFilter = this.FindControl<ComboBox>("StageFilter")!;
        _doorsFilter = this.FindControl<ComboBox>("DoorsFilter")!;
        _nameFilter = this.FindControl<TextBox>("NameFilter")!;

        _themeList.ItemsSource = _themes;
        _fileList.ItemsSource = _files;

        _themeList.SelectionChanged += (_, _) => OnThemeSelected();
        _fileList.SelectionChanged += (_, _) => OnFileSelected();
        _shapeFilter.SelectionChanged += (_, _) => ApplyFilters();
        _categoryFilter.SelectionChanged += (_, _) => ApplyFilters();
        _stageFilter.SelectionChanged += (_, _) => ApplyFilters();
        _doorsFilter.SelectionChanged += (_, _) => ApplyFilters();
        _nameFilter.TextChanged += (_, _) => ApplyFilters();

        ClearDetail();
    }

    public void Configure(string projectRoot)
    {
        _index = new TilemapIndex(projectRoot);
        _docCache.Clear();

        _themes.Clear();
        foreach (var theme in TilemapIndex.KnownThemes)
        {
            var count = _index.ByTheme(theme).Count();
            _themes.Add(new TilemapThemeRow(theme, count));
        }

        var total = _index.Entries.Count;
        _statusText.Text = total == 0
            ? $"No tilemaps found under {_index.TilemapDataRoot}"
            : $"{total} tilemap(s) across {_themes.Count(t => t.Count > 0)} theme(s) · {Path.GetFileName(projectRoot)}";

        // Default-select the largest populated theme.
        var defaultTheme = _themes.OrderByDescending(t => t.Count).FirstOrDefault(t => t.Count > 0);
        if (defaultTheme is not null)
            _themeList.SelectedItem = defaultTheme;
    }

    private void OnThemeSelected()
    {
        if (_index is null || _themeList.SelectedItem is not TilemapThemeRow row)
        {
            _selectedTheme = null;
            _allRowsForTheme = new();
            RebuildFilterOptions(Array.Empty<TilemapDocument>());
            ApplyFilters();
            return;
        }
        _selectedTheme = row.Name;
        var entries = _index.ByTheme(_selectedTheme).ToList();

        // Eagerly read docs for this theme so filter dropdowns reflect real values.
        var docs = new List<TilemapDocument>(entries.Count);
        _allRowsForTheme = new List<TilemapRow>(entries.Count);
        foreach (var e in entries)
        {
            TilemapDocument doc;
            try
            {
                if (!_docCache.TryGetValue(e.FullPath, out doc!))
                {
                    doc = TilemapReader.Read(e.FullPath);
                    _docCache[e.FullPath] = doc;
                }
            }
            catch
            {
                // Skip malformed files; surface in summary.
                continue;
            }
            docs.Add(doc);
            _allRowsForTheme.Add(new TilemapRow
            {
                Stem = e.Stem,
                Summary = SummarizeDoc(doc),
                Entry = e,
            });
        }
        RebuildFilterOptions(docs);
        ApplyFilters();
    }

    private void RebuildFilterOptions(IReadOnlyList<TilemapDocument> docs)
    {
        FillCombo(_shapeFilter, "shape", docs.Select(d => d.RoomShape));
        FillCombo(_categoryFilter, "category", docs.Select(d => d.RoomCategory));
        FillCombo(_stageFilter, "stage", docs.Select(d => d.StageType));
        FillCombo(_doorsFilter, "doors", docs.Select(d => d.OpenDoors.ToString()));
    }

    private static void FillCombo(ComboBox box, string label, IEnumerable<string?> values)
    {
        var distinct = values
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        var items = new List<string> { $"{label}: {AnyToken}" };
        items.AddRange(distinct.Select(v => $"{label}: {v}"));
        box.ItemsSource = items;
        box.SelectedIndex = 0;
    }

    private void ApplyFilters()
    {
        _files.Clear();
        var shape = TokenOf(_shapeFilter);
        var category = TokenOf(_categoryFilter);
        var stage = TokenOf(_stageFilter);
        var doors = TokenOf(_doorsFilter);
        var name = (_nameFilter.Text ?? string.Empty).Trim();

        var matched = 0;
        foreach (var row in _allRowsForTheme)
        {
            var doc = _docCache[row.Entry.FullPath];
            if (shape is not null && !string.Equals(doc.RoomShape, shape, StringComparison.Ordinal)) continue;
            if (category is not null && !string.Equals(doc.RoomCategory, category, StringComparison.Ordinal)) continue;
            if (stage is not null && !string.Equals(doc.StageType, stage, StringComparison.Ordinal)) continue;
            if (doors is not null && doc.OpenDoors.ToString() != doors) continue;
            if (name.Length > 0 && !row.Stem.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
            _files.Add(row);
            matched++;
        }
        _resultCount.Text = $"{matched} / {_allRowsForTheme.Count}";
    }

    private static string? TokenOf(ComboBox box)
    {
        if (box.SelectedItem is not string s) return null;
        var colon = s.IndexOf(':');
        if (colon < 0) return null;
        var val = s[(colon + 1)..].Trim();
        return val == AnyToken ? null : val;
    }

    private void OnFileSelected()
    {
        if (_fileList.SelectedItem is not TilemapRow row)
        {
            ClearDetail();
            _previewImage.Source = null;
            _previewName.Text = "(select a tilemap)";
            _previewKey.Text = string.Empty;
            return;
        }
        var doc = _docCache[row.Entry.FullPath];
        _previewName.Text = $"{row.Entry.Theme}/{row.Entry.Stem}";
        _previewKey.Text = $"{row.Entry.FullPath}  ·  {row.Entry.SizeBytes:n0} bytes";
        _previewImage.Source = TilemapPreviewRenderer.Render(doc);
        BuildDetail(row.Entry, doc);
    }

    private void ClearDetail()
    {
        _detailHost.Children.Clear();
        _detailHost.Children.Add(new TextBlock
        {
            Text = "(click a tilemap to see metadata + reverse refs)",
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x82, 0x90)),
            FontStyle = FontStyle.Italic,
        });
    }

    private void BuildDetail(TilemapEntry entry, TilemapDocument doc)
    {
        _detailHost.Children.Clear();

        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock { Text = "TILEMAP", Classes = { "label" } });
        header.Children.Add(new TextBlock
        {
            Text = entry.Stem,
            FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
        });
        _detailHost.Children.Add(header);

        var kv = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("110,*"),
            RowSpacing = 4,
        };
        var rowIdx = 0;
        void AddRow(string key, string value)
        {
            kv.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var k = new TextBlock { Text = key, Classes = { "detailKey" }, VerticalAlignment = VerticalAlignment.Top };
            var v = new TextBlock { Text = value, Classes = { "detailVal" }, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(k, rowIdx);
            Grid.SetColumn(k, 0);
            Grid.SetRow(v, rowIdx);
            Grid.SetColumn(v, 1);
            kv.Children.Add(k);
            kv.Children.Add(v);
            rowIdx++;
        }
        AddRow("Theme", entry.Theme);
        AddRow("Shape", doc.RoomShape ?? "—");
        AddRow("Category", doc.RoomCategory ?? "—");
        AddRow("Stage", doc.StageType ?? "—");
        AddRow("Open doors", doc.OpenDoors.ToString());
        AddRow("Size", $"{doc.Width}×{doc.Height}");
        AddRow("Doors", $"L={doc.Doors.Left} T={doc.Doors.Top} R={doc.Doors.Right} B={doc.Doors.Bottom}");
        if (!string.IsNullOrEmpty(doc.Meta.Name)) AddRow("Meta name", doc.Meta.Name!);
        if (doc.Meta.Version > 0) AddRow("Version", doc.Meta.Version.ToString());
        _detailHost.Children.Add(kv);

        // Reverse refs (against levels/*.json templateId).
        var refsPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
        refsPanel.Children.Add(new TextBlock { Text = "REVERSE REFS", Classes = { "label" } });
        var refs = _index!.FindReverseRefs(entry.Stem);
        if (refs.Count == 0)
        {
            refsPanel.Children.Add(new TextBlock
            {
                Text = "(no level references this tilemap by templateId)",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x65, 0x73)),
                FontStyle = FontStyle.Italic,
                FontSize = 11,
            });
        }
        else
        {
            refsPanel.Children.Add(new TextBlock
            {
                Text = $"Referenced by {refs.Count} level(s):",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA3, 0xAD, 0xBA)),
                FontSize = 11,
            });
            foreach (var levelId in refs)
            {
                refsPanel.Children.Add(new TextBlock
                {
                    Text = levelId,
                    FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE4)),
                });
            }
        }
        _detailHost.Children.Add(refsPanel);
    }

    private void OnCopyKeyClick(object? sender, RoutedEventArgs e)
    {
        if (_fileList.SelectedItem is not TilemapRow row) return;
        var key = row.Entry.Key;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new ProcessStartInfo("pbcopy") { RedirectStandardInput = true, UseShellExecute = false };
                using var proc = Process.Start(psi)!;
                proc.StandardInput.Write(key);
                proc.StandardInput.Close();
                proc.WaitForExit(1000);
                _previewKey.Text = $"copied to clipboard: {key}";
            }
            else
            {
                _previewKey.Text = $"key (copy manually): {key}";
            }
        }
        catch (Exception ex)
        {
            _previewKey.Text = $"copy failed: {ex.Message}";
        }
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        if (_fileList.SelectedItem is not TilemapRow row) return;
        if (!File.Exists(row.Entry.FullPath)) return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", new[] { "-R", row.Entry.FullPath });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(row.Entry.FullPath)!,
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            _previewKey.Text = $"reveal failed: {ex.Message}";
        }
    }

    private static string SummarizeDoc(TilemapDocument doc)
    {
        var bits = new List<string>();
        if (!string.IsNullOrEmpty(doc.RoomShape)) bits.Add(doc.RoomShape!);
        if (!string.IsNullOrEmpty(doc.StageType)) bits.Add(doc.StageType!);
        if (doc.OpenDoors > 0) bits.Add($"{doc.OpenDoors} door" + (doc.OpenDoors == 1 ? "" : "s"));
        bits.Add($"{doc.Width}×{doc.Height}");
        return string.Join(" · ", bits);
    }
}
