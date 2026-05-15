using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using OAE.Core.Importer;

namespace OAE.App.Controls;

/// <summary>
/// Drop target for an asset field. Holds onto the backing JSON node so the
/// current asset key stays editable as text while drops trigger the
/// import-asset pipeline. Caller wires <see cref="ImportCompleted"/> so the
/// host form can refresh the entity.
/// </summary>
public partial class AssetDropZone : UserControl
{
    private TextBox _keyBox = null!;
    private TextBlock _pipelineBadge = null!;
    private ContentControl _statePane = null!;
    private Border _zone = null!;

    private JsonObject? _parent;
    private string? _key;
    private AssetImporter? _importer;
    private string? _projectRoot;
    private string? _entityId;
    private string? _pipeline;
    private string? _pendingSource;
    private CancellationTokenSource? _runCts;

    /// <summary>Raised on successful import so the form can re-load the entity.</summary>
    public event Action? ImportCompleted;

    public AssetDropZone()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _zone = this.FindControl<Border>("DropZone")!;
        _keyBox = this.FindControl<TextBox>("KeyBox")!;
        _pipelineBadge = this.FindControl<TextBlock>("PipelineBadge")!;
        _statePane = this.FindControl<ContentControl>("StatePane")!;

        _keyBox.TextChanged += OnKeyTextChanged;
        DragDrop.SetAllowDrop(_zone, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>
    /// One-shot configuration from the form builder.
    /// </summary>
    public void Configure(
        JsonObject parent,
        string key,
        string pipeline,
        AssetImporter importer,
        string projectRoot,
        string entityId)
    {
        _parent = parent;
        _key = key;
        _pipeline = pipeline;
        _importer = importer;
        _projectRoot = projectRoot;
        _entityId = entityId;

        _keyBox.Text = parent[key]?.GetValue<string?>() ?? string.Empty;
        _pipelineBadge.Text = $"asset · {pipeline}";

        if (!importer.IsAvailable)
        {
            _zone.Classes.Add("disabled");
            ShowMessage("import-asset script not found at ~/.claude/skills/import-asset/scripts/import_asset.py — drops disabled. Set ImportAssetSkillPath in config to override.");
        }
        else
        {
            ShowIdleHint();
        }
    }

    // ── editing the key text directly (when no drop is active) ───────────

    private void OnKeyTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_parent is null || _key is null) return;
        _parent[_key] = JsonValue.Create(_keyBox.Text ?? string.Empty);
        // The caller wires its onMutated via JsonObject mutation, but we need
        // to bubble dirty too — fire ImportCompleted? No, that would refresh.
        // The form-host listens to JsonNode mutations through this same path
        // anyway via the DataContext binding chain — see EntityFormBuilder.
    }

    // ── drag/drop wiring ─────────────────────────────────────────────────

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (_importer?.IsAvailable == true && HasOneFile(e))
        {
            _zone.Classes.Add("dragOver");
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _zone.Classes.Remove("dragOver");
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (_importer?.IsAvailable == true && HasOneFile(e))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _zone.Classes.Remove("dragOver");
        if (_importer?.IsAvailable != true) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null || files.Length != 1) return;
        var path = files[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path)) return;
        _pendingSource = path;
        ShowConfirmPrompt(path);
    }

    private static bool HasOneFile(DragEventArgs e)
    {
        var files = e.DataTransfer?.TryGetFiles();
        return files is { Length: 1 };
    }

    // ── state-pane content ───────────────────────────────────────────────

    private void ShowIdleHint()
    {
        _statePane.Content = new TextBlock
        {
            Text = $"Drop a file here to run the “{_pipeline}” pipeline.",
            Foreground = new SolidColorBrush(Color.Parse("#6E7785")),
            FontSize = 11,
        };
    }

    private void ShowMessage(string text)
    {
        _statePane.Content = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#A3ADBA")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private void ShowConfirmPrompt(string source)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = $"Run “{_pipeline}” on {Path.GetFileName(source)}?",
            Foreground = new SolidColorBrush(Color.Parse("#D8DCE4")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = source,
            Foreground = new SolidColorBrush(Color.Parse("#6E7785")),
            FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
        });

        var confirm = new Button
        {
            Content = "Run import",
            Background = new SolidColorBrush(Color.Parse("#2C6BE6")),
            Foreground = Brushes.White,
            Padding = new Thickness(12, 5),
        };
        confirm.Click += (_, _) => StartRun(source);

        var cancel = new Button { Content = "Cancel", Padding = new Thickness(10, 5), Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { _pendingSource = null; ShowIdleHint(); };

        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        _statePane.Content = stack;
    }

    private async void StartRun(string source)
    {
        if (_importer is null || _projectRoot is null || _entityId is null || _pipeline is null) return;

        _runCts = new CancellationTokenSource();
        ShowMessage($"Running {_pipeline}…");

        ImportResult result;
        try
        {
            result = await _importer.RunAsync(_pipeline, source, _projectRoot, _entityId, extraArgs: null, _runCts.Token);
        }
        catch (Exception ex)
        {
            ShowFailure($"Import threw: {ex.Message}");
            return;
        }

        if (result.IsSuccess)
        {
            ShowSuccess(result);
            // Hand control back to the host so it can re-Get the entity (the
            // pipeline may have rewritten the JSON we have in memory).
            Dispatcher.UIThread.Post(() => ImportCompleted?.Invoke());
        }
        else
        {
            ShowFailure($"Exit {result.ExitCode} after {result.DurationMs}ms\n\nstderr:\n{result.StdErr}");
        }
    }

    private void ShowSuccess(ImportResult r)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = $"✓ Import succeeded ({r.DurationMs} ms)",
            Foreground = new SolidColorBrush(Color.Parse("#9BE3B5")),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
        });
        if (!string.IsNullOrWhiteSpace(r.StdOut))
        {
            stack.Children.Add(new TextBlock
            {
                Text = TailLines(r.StdOut, 6),
                Foreground = new SolidColorBrush(Color.Parse("#7A8290")),
                FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        _statePane.Content = stack;
    }

    private void ShowFailure(string message)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = "✗ Import failed",
            Foreground = new SolidColorBrush(Color.Parse("#E39B9B")),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
        });
        stack.Children.Add(new TextBox
        {
            Text = message,
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
            FontSize = 10.5,
            Background = new SolidColorBrush(Color.Parse("#1A0F12")),
            BorderBrush = new SolidColorBrush(Color.Parse("#5A2A30")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            MaxHeight = 220,
        });
        _statePane.Content = stack;
    }

    private static string TailLines(string text, int n)
    {
        var lines = text.Split('\n');
        if (lines.Length <= n) return text.TrimEnd();
        return string.Join('\n', lines[^n..]).TrimEnd();
    }
}
