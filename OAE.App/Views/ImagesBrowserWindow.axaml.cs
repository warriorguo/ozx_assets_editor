using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OAE.Core.Resources;

namespace OAE.App.Views;

public sealed class FolderNode
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public ObservableCollection<FolderNode> Children { get; } = new();
    public override string ToString() => Name;
}

public sealed record ImageFile(string Name, string FullPath, string Size);

public partial class ImagesBrowserWindow : Window
{
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    private TextBlock _statusText = null!;
    private TreeView _tree = null!;
    private ListBox _fileList = null!;
    private TextBlock _previewName = null!;
    private TextBlock _previewDims = null!;
    private TextBlock _previewSlice = null!;
    private Border _ppuBadge = null!;
    private TextBlock _ppuBadgeText = null!;
    private TextBlock _previewPath = null!;
    private Image _previewImage = null!;

    private string _root = string.Empty;
    private string _projectRoot = string.Empty;
    private readonly ObservableCollection<FolderNode> _rootNodes = new();
    private readonly ObservableCollection<ImageFile> _files = new();

    public ImagesBrowserWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _tree = this.FindControl<TreeView>("FolderTree")!;
        _fileList = this.FindControl<ListBox>("FileList")!;
        _previewName = this.FindControl<TextBlock>("PreviewName")!;
        _previewDims = this.FindControl<TextBlock>("PreviewDims")!;
        _previewSlice = this.FindControl<TextBlock>("PreviewSlice")!;
        _ppuBadge = this.FindControl<Border>("PpuBadge")!;
        _ppuBadgeText = this.FindControl<TextBlock>("PpuBadgeText")!;
        _previewPath = this.FindControl<TextBlock>("PreviewPath")!;
        _previewImage = this.FindControl<Image>("PreviewImage")!;

        _tree.ItemsSource = _rootNodes;
        _fileList.ItemsSource = _files;
        _tree.SelectionChanged += (_, _) => OnFolderSelected();
        _fileList.SelectionChanged += (_, _) => OnFileSelected();
    }

    public void Configure(string projectRoot)
    {
        _projectRoot = projectRoot;
        _root = Path.Combine(projectRoot, "Assets", "Images");
        _rootNodes.Clear();
        if (!Directory.Exists(_root))
        {
            _statusText.Text = $"Assets/Images not found under {projectRoot}";
            return;
        }
        var node = new FolderNode { Name = "Images", FullPath = _root };
        PopulateChildren(node);
        _rootNodes.Add(node);
        _statusText.Text = $"Browsing {_root}";
    }

    private static void PopulateChildren(FolderNode node)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(node.FullPath).OrderBy(d => d, StringComparer.Ordinal))
            {
                var child = new FolderNode { Name = Path.GetFileName(dir), FullPath = dir };
                PopulateChildren(child);
                node.Children.Add(child);
            }
        }
        catch { /* unreadable subdir — skip */ }
    }

    private void OnFolderSelected()
    {
        _files.Clear();
        if (_tree.SelectedItem is not FolderNode folder) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder.FullPath)
                                          .Where(p => Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                                          .OrderBy(p => p, StringComparer.Ordinal))
            {
                var info = new FileInfo(path);
                _files.Add(new ImageFile(
                    Name: Path.GetFileName(path),
                    FullPath: path,
                    Size: FormatSize(info.Length)));
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = $"List failed: {ex.Message}";
        }
        ClearPreview();
    }

    private void OnFileSelected()
    {
        if (_fileList.SelectedItem is not ImageFile file) { ClearPreview(); return; }

        _previewName.Text = file.Name;
        _previewPath.Text = file.FullPath;

        try { _previewImage.Source = new Bitmap(file.FullPath); }
        catch { _previewImage.Source = null; }

        var dims = PngReader.TryReadDimensions(file.FullPath);
        _previewDims.Text = dims is null ? "(dimensions unknown)" : $"{dims.Value.Width}×{dims.Value.Height}";

        var meta = SpriteMetaReader.Read(file.FullPath + ".meta");
        if (meta is null)
        {
            _previewSlice.Text = "(no .meta — Unity hasn't imported this asset)";
            _ppuBadge.IsVisible = false;
        }
        else
        {
            _previewSlice.Text = meta.IsMultiple
                ? $"spriteMode: Multiple · {meta.SpriteCount} sliced sprite(s) · PPU {meta.PixelsPerUnit}"
                : $"spriteMode: Single · PPU {meta.PixelsPerUnit}";

            var rel = Path.GetRelativePath(_projectRoot, file.FullPath);
            ApplyPpuBadge(PpuClassifier.Classify(rel, meta.PixelsPerUnit));
        }
    }

    private void ApplyPpuBadge(PpuVerdict v)
    {
        _ppuBadge.IsVisible = true;
        _ppuBadgeText.Text = $"{v.Severity} · {v.Reason}";
        switch (v.Severity)
        {
            case PpuSeverity.Ok:
                _ppuBadge.Background  = (IBrush)Resources["BadgeOkBg"]!;
                _ppuBadgeText.Foreground = (IBrush)Resources["BadgeOkFg"]!;
                break;
            case PpuSeverity.Preferred:
                _ppuBadge.Background  = (IBrush)Resources["BadgePreferredBg"]!;
                _ppuBadgeText.Foreground = (IBrush)Resources["BadgePreferredFg"]!;
                break;
            case PpuSeverity.Strict:
                _ppuBadge.Background  = (IBrush)Resources["BadgeStrictBg"]!;
                _ppuBadgeText.Foreground = (IBrush)Resources["BadgeStrictFg"]!;
                break;
            default:
                _ppuBadge.Background  = (IBrush)Resources["BadgeIgnoredBg"]!;
                _ppuBadgeText.Foreground = (IBrush)Resources["BadgeIgnoredFg"]!;
                break;
        }
    }

    private void ClearPreview()
    {
        _previewName.Text = "(select an image)";
        _previewDims.Text = string.Empty;
        _previewSlice.Text = string.Empty;
        _ppuBadge.IsVisible = false;
        _previewPath.Text = string.Empty;
        _previewImage.Source = null;
    }

    private async void OnScanPpusClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root)) return;

        var results = new List<PpuAuditRow>();
        foreach (var path in Directory.EnumerateFiles(_root, "*.meta", SearchOption.AllDirectories))
        {
            var image = path[..^".meta".Length];
            var meta = SpriteMetaReader.Read(path);
            if (meta is null) continue;
            var rel = Path.GetRelativePath(_projectRoot, image);
            var verdict = PpuClassifier.Classify(rel, meta.PixelsPerUnit);
            if (verdict.Severity is PpuSeverity.Strict or PpuSeverity.Preferred)
                results.Add(new PpuAuditRow(rel, meta.PixelsPerUnit, verdict.Severity, verdict.Reason));
        }
        results = results.OrderBy(r => r.Severity == PpuSeverity.Strict ? 0 : 1).ThenBy(r => r.RelativePath, StringComparer.Ordinal).ToList();

        var window = new PpuAuditResultsWindow();
        window.Configure(results);
        await window.ShowDialog(this);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }
}

public sealed record PpuAuditRow(string RelativePath, int Ppu, PpuSeverity Severity, string Reason);
