using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OAE.Core.Schema;

namespace OAE.App.Forms;

/// <summary>
/// Walks a <see cref="SchemaModel"/> and builds an Avalonia control tree
/// bound to a backing <see cref="JsonObject"/>. Edits mutate the JSON in
/// place; the caller handles save/dirty bookkeeping via
/// <paramref name="onMutated"/>.
/// </summary>
public static class EntityFormBuilder
{
    public static Control Build(SchemaModel schema, JsonObject root, Action onMutated)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var field in schema.Fields)
            panel.Children.Add(BuildFieldRow(field, root, onMutated));
        return panel;
    }

    // ── per-field row ────────────────────────────────────────────────────

    private static Control BuildFieldRow(FieldDescriptor field, JsonObject parent, Action onMutated)
    {
        var label = new TextBlock
        {
            Text = Humanise(field.Name),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5,
            Foreground = LabelBrush,
        };

        var hint = BuildHintLine(field);
        var editor = BuildEditor(field, parent, onMutated);

        var labelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        labelRow.Children.Add(label);
        if (hint is not null) labelRow.Children.Add(hint);

        var row = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };
        row.Children.Add(labelRow);
        row.Children.Add(editor);
        return row;
    }

    private static TextBlock? BuildHintLine(FieldDescriptor f)
    {
        var bits = new List<string>();
        bits.Add(f.Kind.ToString().ToLowerInvariant());
        if (f.Meta?.RefTarget is { } r) bits.Add($"ref:{r}");
        if (f.Meta?.AssetKey is { } a) bits.Add($"asset:{a}");
        if (f.Meta?.Description is { } d) bits.Add(d);
        return new TextBlock
        {
            Text = string.Join(" · ", bits),
            FontSize = 10.5,
            Foreground = HintBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ── editor controls per FieldKind ────────────────────────────────────

    private static Control BuildEditor(FieldDescriptor f, JsonObject parent, Action onMutated)
    {
        return f.Kind switch
        {
            FieldKind.String => BuildString(f, parent, onMutated),
            FieldKind.Int    => BuildInteger(f, parent, onMutated),
            FieldKind.Long   => BuildInteger(f, parent, onMutated),
            FieldKind.Float  => BuildDecimal(f, parent, onMutated, increment: 0.1m),
            FieldKind.Double => BuildDecimal(f, parent, onMutated, increment: 0.1m),
            FieldKind.Bool   => BuildBool(f, parent, onMutated),
            FieldKind.Enum   => BuildEnum(f, parent, onMutated),
            FieldKind.Object => BuildObject(f, parent, onMutated),
            FieldKind.Array  => BuildArrayPlaceholder(f, parent),
            _                => new TextBlock { Text = $"(unsupported kind: {f.Kind})", Opacity = 0.6 },
        };
    }

    private static Control BuildString(FieldDescriptor f, JsonObject parent, Action onMutated)
    {
        var box = new TextBox
        {
            Text = parent[f.Name]?.GetValue<string?>() ?? string.Empty,
            FontFamily = MonoFont,
            FontSize = 12,
        };
        box.TextChanged += (_, _) =>
        {
            parent[f.Name] = JsonValue.Create(box.Text ?? string.Empty);
            onMutated();
        };
        return box;
    }

    private static Control BuildInteger(FieldDescriptor f, JsonObject parent, Action onMutated)
    {
        var nud = new NumericUpDown
        {
            Increment = 1,
            FormatString = "0",
            Value = TryGetDecimal(parent, f.Name),
        };
        nud.ValueChanged += (_, _) =>
        {
            if (nud.Value is null) parent[f.Name] = null;
            else parent[f.Name] = f.Kind == FieldKind.Long
                ? JsonValue.Create((long)nud.Value.Value)
                : JsonValue.Create((int)nud.Value.Value);
            onMutated();
        };
        return nud;
    }

    private static Control BuildDecimal(FieldDescriptor f, JsonObject parent, Action onMutated, decimal increment)
    {
        var nud = new NumericUpDown
        {
            Increment = increment,
            FormatString = "0.###",
            Value = TryGetDecimal(parent, f.Name),
        };
        nud.ValueChanged += (_, _) =>
        {
            if (nud.Value is null) parent[f.Name] = null;
            else parent[f.Name] = f.Kind == FieldKind.Double
                ? JsonValue.Create((double)nud.Value.Value)
                : JsonValue.Create((float)nud.Value.Value);
            onMutated();
        };
        return nud;
    }

    private static Control BuildBool(FieldDescriptor f, JsonObject parent, Action onMutated)
    {
        var cb = new CheckBox
        {
            IsChecked = parent[f.Name]?.GetValue<bool?>() ?? false,
        };
        cb.IsCheckedChanged += (_, _) =>
        {
            parent[f.Name] = JsonValue.Create(cb.IsChecked ?? false);
            onMutated();
        };
        return cb;
    }

    private static Control BuildEnum(FieldDescriptor f, JsonObject parent, Action onMutated)
    {
        var combo = new ComboBox
        {
            ItemsSource = f.Enum,
            SelectedItem = parent[f.Name]?.GetValue<string?>(),
        };
        combo.SelectionChanged += (_, _) =>
        {
            parent[f.Name] = combo.SelectedItem is string s ? JsonValue.Create(s) : null;
            onMutated();
        };
        return combo;
    }

    private static Control BuildObject(FieldDescriptor f, JsonObject parent, Action onMutated)
    {
        // Ensure the nested object exists so child editors have something to mutate.
        if (parent[f.Name] is not JsonObject nested)
        {
            nested = new JsonObject();
            parent[f.Name] = nested;
        }

        var border = new Border
        {
            BorderBrush = NestedBorderBrush,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(14, 2, 0, 2),
            Margin = new Thickness(0, 2, 0, 2),
        };
        var children = new StackPanel { Spacing = 0 };
        if (f.Nested is not null)
            foreach (var sub in f.Nested.Fields)
                children.Children.Add(BuildFieldRow(sub, nested, onMutated));
        border.Child = children;
        return border;
    }

    private static Control BuildArrayPlaceholder(FieldDescriptor f, JsonObject parent)
    {
        var arr = parent[f.Name] as JsonArray;
        var count = arr?.Count ?? 0;
        return new TextBlock
        {
            Text = count == 0
                ? "(empty — array editor lands as a follow-up; edit via raw JSON for now)"
                : $"{count} item(s) — array editor lands as a follow-up; edit via raw JSON for now",
            Foreground = HintBrush,
            FontStyle = FontStyle.Italic,
            FontSize = 11.5,
        };
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static decimal? TryGetDecimal(JsonObject parent, string key)
    {
        var node = parent[key];
        if (node is null) return null;
        try
        {
            // JsonValue can hold double / long / int — try double first for widest range.
            if (node.GetValue<double?>() is { } d) return (decimal)d;
        }
        catch { /* fall through */ }
        try { return node.GetValue<decimal>(); }
        catch { return null; }
    }

    private static string Humanise(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length + 4);
        sb.Append(char.ToUpper(s[0], CultureInfo.InvariantCulture));
        for (var i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsUpper(c) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static readonly FontFamily MonoFont = new("Menlo,Monaco,Consolas");
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#D8DCE4"));
    private static readonly IBrush HintBrush = new SolidColorBrush(Color.Parse("#6E7785"));
    private static readonly IBrush NestedBorderBrush = new SolidColorBrush(Color.Parse("#2C3340"));
}
