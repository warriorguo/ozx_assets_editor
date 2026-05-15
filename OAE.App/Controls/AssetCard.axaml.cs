using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OAE.Core.Importer;
using OAE.Core.Resources;

namespace OAE.App.Controls;

/// <summary>
/// Card surface for the Images tab: shows a thumbnail of one
/// <see cref="ResolvedAsset"/>, accepts a file drop to replace it, and
/// optionally resizes the dropped file to the current asset's dimensions
/// before passing through to <c>import_asset.py</c>.
/// </summary>
public partial class AssetCard : UserControl
{
    private Border _card = null!;
    private Image _thumb = null!;
    private TextBlock _placeholder = null!;
    private TextBlock _keyText = null!;
    private TextBlock _pipelineBadge = null!;
    private TextBlock _dimsText = null!;
    private TextBlock _pathText = null!;
    private ContentControl _statePane = null!;

    private ResolvedAsset? _asset;
    private AssetImporter? _importer;
    private IImageResizer? _resizer;
    private string? _projectRoot;
    private string? _entityId;

    public event Action? ImportCompleted;

    public AssetCard() { InitializeComponent(); }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _card           = this.FindControl<Border>("Card")!;
        _thumb          = this.FindControl<Image>("Thumb")!;
        _placeholder    = this.FindControl<TextBlock>("ThumbPlaceholder")!;
        _keyText        = this.FindControl<TextBlock>("KeyText")!;
        _pipelineBadge  = this.FindControl<TextBlock>("PipelineBadge")!;
        _dimsText       = this.FindControl<TextBlock>("DimsText")!;
        _pathText       = this.FindControl<TextBlock>("PathText")!;
        _statePane      = this.FindControl<ContentControl>("StatePane")!;

        DragDrop.SetAllowDrop(_card, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void Configure(
        ResolvedAsset asset,
        AssetImporter importer,
        IImageResizer resizer,
        string projectRoot,
        string entityId)
    {
        _asset = asset;
        _importer = importer;
        _resizer = resizer;
        _projectRoot = projectRoot;
        _entityId = entityId;

        _keyText.Text = asset.AssetKey;
        _pipelineBadge.Text = asset.Pipeline;
        _dimsText.Text = BuildDimsLine(asset);
        _pathText.Text = ShortPath(asset.ImagePath ?? asset.DirectAssetPath ?? "(unresolved)", projectRoot);

        if (asset.ImagePath is not null)
        {
            try
            {
                _thumb.Source = new Bitmap(asset.ImagePath);
                _thumb.IsVisible = true;
                _placeholder.IsVisible = false;
            }
            catch
            {
                _thumb.IsVisible = false;
                _placeholder.IsVisible = true;
                _placeholder.Text = "(image unreadable)";
            }
        }
        else
        {
            _thumb.IsVisible = false;
            _placeholder.IsVisible = true;
            _placeholder.Text = asset.DirectAssetPath is null
                ? "(unresolved key)"
                : "(no image — opens an .asset)";
        }

        ShowIdle();
    }

    private static string BuildDimsLine(ResolvedAsset a)
    {
        if (a.Width is null || a.Height is null) return "dimensions unknown";
        var hop = a.Hops > 0 ? $" · resolved via {a.Hops} hop(s)" : string.Empty;
        return $"{a.Width}×{a.Height}{hop}";
    }

    private static string ShortPath(string full, string? projectRoot)
    {
        if (projectRoot is not null && full.StartsWith(projectRoot, StringComparison.Ordinal))
            return full[projectRoot.Length..].TrimStart('/', '\\');
        return full;
    }

    // ── drag/drop ────────────────────────────────────────────────────────

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (CanAccept(e)) { _card.Classes.Add("dragOver"); e.DragEffects = DragDropEffects.Copy; e.Handled = true; }
    }
    private void OnDragLeave(object? sender, DragEventArgs e) => _card.Classes.Remove("dragOver");
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = CanAccept(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private bool CanAccept(DragEventArgs e)
    {
        if (_importer?.IsAvailable != true) return false;
        var files = e.DataTransfer?.TryGetFiles();
        return files is { Length: 1 };
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _card.Classes.Remove("dragOver");
        if (!CanAccept(e)) return;
        var path = e.DataTransfer.TryGetFiles()![0].Path.LocalPath;
        if (string.IsNullOrEmpty(path)) return;
        ShowConfirm(path);
    }

    // ── state pane content ───────────────────────────────────────────────

    private void ShowIdle()
    {
        if (_importer?.IsAvailable != true)
        {
            _statePane.Content = new TextBlock
            {
                Text = "import-asset script not found — drops disabled.",
                Foreground = new SolidColorBrush(Color.Parse("#A3ADBA")),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
            };
            return;
        }
        _statePane.Content = new TextBlock
        {
            Text = "Drop a file to replace.",
            Foreground = new SolidColorBrush(Color.Parse("#6E7785")),
            FontSize = 10.5,
        };
    }

    private void ShowConfirm(string source)
    {
        var stack = new StackPanel { Spacing = 6 };

        stack.Children.Add(new TextBlock
        {
            Text = $"Replace with {Path.GetFileName(source)}?",
            Foreground = new SolidColorBrush(Color.Parse("#D8DCE4")),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
        });

        // Resize affordance — only when both old and new dims are known and differ.
        var newDims = PngReader.TryReadDimensions(source);
        CheckBox? resizeBox = null;
        if (_asset?.Width is { } cw && _asset.Height is { } ch && newDims is { } nd && (cw != nd.Width || ch != nd.Height))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"current {cw}×{ch}  ·  new {nd.Width}×{nd.Height}",
                Foreground = new SolidColorBrush(Color.Parse("#7A8290")),
                FontSize = 10.5,
            });
            resizeBox = new CheckBox
            {
                Content = $"Resize to {cw}×{ch} first",
                IsChecked = _resizer?.IsAvailable == true,
                IsEnabled = _resizer?.IsAvailable == true,
                FontSize = 11,
            };
            stack.Children.Add(resizeBox);
        }

        var run = new Button
        {
            Content = "Run import",
            Background = new SolidColorBrush(Color.Parse("#2C6BE6")),
            Foreground = Brushes.White,
            Padding = new Thickness(10, 4),
        };
        run.Click += async (_, _) => await StartRun(source, resizeBox?.IsChecked == true);

        var cancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(10, 4),
            Margin = new Thickness(6, 0, 0, 0),
        };
        cancel.Click += (_, _) => ShowIdle();

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        btnRow.Children.Add(run);
        btnRow.Children.Add(cancel);
        stack.Children.Add(btnRow);

        _statePane.Content = stack;
    }

    private async Task StartRun(string source, bool resizeFirst)
    {
        if (_importer is null || _projectRoot is null || _entityId is null || _asset is null) return;
        var pipeline = _asset.Pipeline;

        var actualSource = source;
        string? tempForCleanup = null;

        ShowMessage(resizeFirst ? "Resizing…" : $"Running {pipeline}…");

        try
        {
            if (resizeFirst && _asset.Width is { } w && _asset.Height is { } h && _resizer?.IsAvailable == true)
            {
                actualSource = await _resizer.ResizeAsync(source, w, h);
                tempForCleanup = actualSource;
                ShowMessage($"Running {pipeline}…");
            }

            var result = await _importer.RunAsync(pipeline, actualSource, _projectRoot, _entityId);
            if (result.IsSuccess)
            {
                ShowSuccess(result);
                Dispatcher.UIThread.Post(() => ImportCompleted?.Invoke());
            }
            else
            {
                ShowFailure($"Exit {result.ExitCode} after {result.DurationMs}ms\n\nstderr:\n{result.StdErr}");
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex.Message);
        }
        finally
        {
            if (tempForCleanup is not null)
                try { File.Delete(tempForCleanup); } catch { /* best effort */ }
        }
    }

    private void ShowMessage(string text)
    {
        _statePane.Content = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#A3ADBA")),
            FontSize = 11,
        };
    }

    private void ShowSuccess(ImportResult r)
    {
        _statePane.Content = new TextBlock
        {
            Text = $"✓ Done in {r.DurationMs} ms",
            Foreground = new SolidColorBrush(Color.Parse("#9BE3B5")),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
        };
    }

    private void ShowFailure(string message)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = "✗ Import failed",
            Foreground = new SolidColorBrush(Color.Parse("#E39B9B")),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
        });
        stack.Children.Add(new TextBox
        {
            Text = message,
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
            FontSize = 10,
            Background = new SolidColorBrush(Color.Parse("#1A0F12")),
            BorderBrush = new SolidColorBrush(Color.Parse("#5A2A30")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            MaxHeight = 180,
        });
        _statePane.Content = stack;
    }
}
