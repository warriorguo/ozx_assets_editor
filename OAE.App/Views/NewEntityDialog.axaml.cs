using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OAE.Core.References;
using OAE.Core.Templates;

namespace OAE.App.Views;

/// <summary>
/// What <see cref="NewEntityDialog"/> returns to the caller on confirm.
/// <c>null</c> means the user cancelled.
/// </summary>
public sealed record NewEntityResult(string TemplateId, string NewId, string? DisplayName);

public partial class NewEntityDialog : Window
{
    private static readonly Regex IdPattern = new("^[a-z0-9_]+$", RegexOptions.Compiled);

    private TextBlock _typeText = null!;
    private ComboBox _templateBox = null!;
    private TextBlock _templateDescription = null!;
    private TextBox _idBox = null!;
    private TextBox _displayBox = null!;
    private TextBlock _errorLine = null!;
    private Button _okButton = null!;

    private string _typeId = string.Empty;
    private ReferenceIndex? _references;

    public NewEntityDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _typeText = this.FindControl<TextBlock>("TypeText")!;
        _templateBox = this.FindControl<ComboBox>("TemplateBox")!;
        _templateDescription = this.FindControl<TextBlock>("TemplateDescription")!;
        _idBox = this.FindControl<TextBox>("IdBox")!;
        _displayBox = this.FindControl<TextBox>("DisplayNameBox")!;
        _errorLine = this.FindControl<TextBlock>("ErrorLine")!;
        _okButton = this.FindControl<Button>("OkButton")!;

        _templateBox.SelectionChanged += (_, _) => OnTemplateChanged();
        _idBox.TextChanged += (_, _) => Validate();
    }

    public void Configure(string typeId, ReferenceIndex references)
    {
        _typeId = typeId;
        _references = references;
        _typeText.Text = typeId;

        var templates = TemplateLoader.For(typeId);
        _templateBox.ItemsSource = templates;
        _templateBox.DisplayMemberBinding = new Avalonia.Data.Binding("Label");
        if (templates.Count > 0) _templateBox.SelectedIndex = 0;

        Validate();
    }

    private void OnTemplateChanged()
    {
        if (_templateBox.SelectedItem is TemplateDescriptor t)
            _templateDescription.Text = t.Description ?? string.Empty;
        else
            _templateDescription.Text = string.Empty;
        Validate();
    }

    private void Validate()
    {
        var id = (_idBox.Text ?? string.Empty).Trim();
        string? error = null;
        if (id.Length == 0)
        {
            // empty id — disable OK silently
        }
        else if (!IdPattern.IsMatch(id))
        {
            error = "Id must match [a-z0-9_]+";
        }
        else if (_references is not null && _references.Contains(_typeId, id))
        {
            error = $"‘{id}’ already exists in {_typeId}";
        }

        _errorLine.Text = error ?? string.Empty;
        _errorLine.IsVisible = error is not null;
        _okButton.IsEnabled = id.Length > 0 && error is null && _templateBox.SelectedItem is TemplateDescriptor;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (_templateBox.SelectedItem is not TemplateDescriptor t) return;
        var id = (_idBox.Text ?? string.Empty).Trim();
        var display = (_displayBox.Text ?? string.Empty).Trim();
        var result = new NewEntityResult(t.Id, id, display.Length > 0 ? display : null);
        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
