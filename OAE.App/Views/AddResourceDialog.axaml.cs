using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using OAE.Core.Resources;

namespace OAE.App.Views;

public sealed record AddResourceResult(string Key, string Guid, long FileId, int Type);

/// <summary>One of the common Unity asset types we let the user pick from.</summary>
public sealed record TypeChoice(string Label, int TypeId, long DefaultFileId);

public partial class AddResourceDialog : Window
{
    private static readonly Regex KeyPattern = new(@"^[a-z0-9_/@.]+$", RegexOptions.Compiled);

    private static readonly TypeChoice[] Types =
    {
        new("Sprite (PNG)",                          3, 21300000),
        new("Texture (other)",                       3, 2800000),
        new("ScriptableObject (.asset)",             2, 11400000),
        new("Prefab / GameObject",                   3, 100100000),
    };

    private TextBox _keyBox = null!;
    private TextBox _pathBox = null!;
    private TextBlock _guidText = null!;
    private ComboBox _typeBox = null!;
    private TextBlock _errorLine = null!;
    private Button _okButton = null!;

    private UnityMetaIndex? _meta;
    private ResourcesDbStore? _store;
    private string? _pickedPath;
    private string? _pickedGuid;

    public AddResourceDialog() { InitializeComponent(); }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _keyBox     = this.FindControl<TextBox>("KeyBox")!;
        _pathBox    = this.FindControl<TextBox>("PathBox")!;
        _guidText   = this.FindControl<TextBlock>("GuidText")!;
        _typeBox    = this.FindControl<ComboBox>("TypeBox")!;
        _errorLine  = this.FindControl<TextBlock>("ErrorLine")!;
        _okButton   = this.FindControl<Button>("OkButton")!;

        _typeBox.ItemsSource = Types;
        _typeBox.DisplayMemberBinding = new Avalonia.Data.Binding("Label");
        _typeBox.SelectedIndex = 0;

        _keyBox.TextChanged += (_, _) => Validate();
        _typeBox.SelectionChanged += (_, _) => Validate();
    }

    public void Configure(UnityMetaIndex meta, ResourcesDbStore store)
    {
        _meta = meta;
        _store = store;
    }

    private async void OnPickClick(object? sender, RoutedEventArgs e)
    {
        if (_meta is null) return;
        var pick = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick the asset file (a .png, .asset, etc. under Assets/)",
            AllowMultiple = false,
        });
        var file = pick.FirstOrDefault();
        if (file is null) return;
        _pickedPath = file.Path.LocalPath;

        // Read the guid from the sibling .meta file.
        var metaPath = _pickedPath + ".meta";
        if (!File.Exists(metaPath))
        {
            _guidText.Text = "(no .meta file alongside — Unity has not imported this asset yet)";
            _pickedGuid = null;
            _pathBox.Text = _pickedPath;
            Validate();
            return;
        }
        var match = Regex.Match(File.ReadAllText(metaPath), @"guid:\s*([a-f0-9]{32})", RegexOptions.IgnoreCase);
        _pickedGuid = match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        _guidText.Text = _pickedGuid is null
            ? "(could not parse guid from .meta)"
            : $"guid: {_pickedGuid}";
        _pathBox.Text = _pickedPath;
        Validate();
    }

    private void Validate()
    {
        var key = (_keyBox.Text ?? string.Empty).Trim();
        string? error = null;

        if (key.Length == 0)
        {
            // empty — disable OK silently
        }
        else if (!KeyPattern.IsMatch(key))
        {
            error = "Key must match [a-z0-9_/@.]+";
        }
        else if (_store?.Get(key) is not null)
        {
            error = $"‘{key}’ already exists";
        }
        else if (_pickedGuid is null)
        {
            // require a picked file with a parsed guid
        }

        _errorLine.Text = error ?? string.Empty;
        _errorLine.IsVisible = error is not null;
        _okButton.IsEnabled = key.Length > 0 && error is null && _pickedGuid is not null && _typeBox.SelectedItem is TypeChoice;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (_pickedGuid is null || _typeBox.SelectedItem is not TypeChoice t) return;
        var key = (_keyBox.Text ?? string.Empty).Trim();
        Close(new AddResourceResult(key, _pickedGuid, t.DefaultFileId, t.TypeId));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
