using System;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OAE.Core.References;

namespace OAE.App.Controls;

/// <summary>
/// Replaces a plain <c>TextBox</c> for fields with
/// <see cref="OAE.Core.Schema.EditorMeta.RefTarget"/> set. Drives an
/// <see cref="AutoCompleteBox"/> populated from the open project's id list
/// for the target type, with a jump arrow and an inline 'not found' warning.
/// </summary>
public partial class ReferencePicker : UserControl
{
    private AutoCompleteBox _picker = null!;
    private Button _jump = null!;
    private TextBlock _broken = null!;

    private JsonObject? _parent;
    private string? _key;
    private string? _targetType;
    private ReferenceIndex? _index;
    private Action<string, string>? _onJump;
    private Action? _onMutated;
    private bool _suppressMutation;

    public ReferencePicker() { InitializeComponent(); }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _picker = this.FindControl<AutoCompleteBox>("Picker")!;
        _jump   = this.FindControl<Button>("JumpButton")!;
        _broken = this.FindControl<TextBlock>("BrokenLine")!;

        _picker.PropertyChanged += (_, e) =>
        {
            if (e.Property == AutoCompleteBox.TextProperty) OnTextChanged();
        };
        _jump.Click += OnJumpClick;
    }

    public void Configure(
        JsonObject parent,
        string key,
        string targetType,
        ReferenceIndex index,
        Action<string, string> onJump,
        Action onMutated)
    {
        _parent = parent;
        _key = key;
        _targetType = targetType;
        _index = index;
        _onJump = onJump;
        _onMutated = onMutated;

        _picker.ItemsSource = index.IdsOf(targetType);
        _suppressMutation = true;
        _picker.Text = parent[key]?.GetValue<string?>() ?? string.Empty;
        _suppressMutation = false;

        UpdateBrokenLine();
    }

    private void OnTextChanged()
    {
        if (_suppressMutation || _parent is null || _key is null) return;
        var value = _picker.Text ?? string.Empty;
        _parent[_key] = string.IsNullOrEmpty(value) ? null : JsonValue.Create(value);
        _onMutated?.Invoke();
        UpdateBrokenLine();
    }

    private void OnJumpClick(object? sender, RoutedEventArgs e)
    {
        var value = _picker.Text;
        if (string.IsNullOrEmpty(value) || _targetType is null) return;
        _onJump?.Invoke(_targetType, value);
    }

    private void UpdateBrokenLine()
    {
        if (_index is null || _targetType is null || _picker.Text is not { Length: > 0 } v)
        {
            _broken.IsVisible = false;
            return;
        }
        if (_index.Contains(_targetType, v))
        {
            _broken.IsVisible = false;
        }
        else
        {
            _broken.IsVisible = true;
            _broken.Text = $"‘{v}’ not found in {_targetType}";
        }
    }
}
