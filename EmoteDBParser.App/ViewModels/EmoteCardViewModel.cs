using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmoteDBParser;

namespace EmoteDBParser.App.ViewModels;

public sealed class AttributeEntry
{
    public string Label { get; }
    public string Value { get; }

    public AttributeEntry(string label, string? value)
    {
        Label = label;
        Value = string.IsNullOrWhiteSpace(value) ? "—" : value;
    }
}

public partial class EmoteCardViewModel : ObservableObject
{
    public EmoteRow Row { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Row.Name) ? Row.Row : Row.Name!;

    public string Initials
    {
        get
        {
            var name = DisplayName.Trim();

            if (name.Length == 0)
                return "?";

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();

            return name[..Math.Min(2, name.Length)].ToUpperInvariant();
        }
    }

    public Bitmap? Icon { get; }

    public bool HasIcon => Icon != null;

    [ObservableProperty]
    private bool _isSelected;

    public IReadOnlyList<AttributeEntry> Attributes { get; }

    public EmoteCardViewModel(EmoteRow row)
    {
        Row = row;

        if (!string.IsNullOrEmpty(row.IconImagePath) && File.Exists(row.IconImagePath))
        {
            try
            {
                Icon = new Bitmap(row.IconImagePath);
            }
            catch
            {
                Icon = null;
            }
        }

        Attributes = BuildAttributes(row);
    }

    private static IReadOnlyList<AttributeEntry> BuildAttributes(EmoteRow row) =>
        new[]
        {
            new AttributeEntry("ID", row.Id.ToString()),
            new AttributeEntry("Row", row.Row),
            new AttributeEntry("Name", row.Name),
            new AttributeEntry("Name key", row.NameKey),
            new AttributeEntry("Name from", row.NameFrom),
            new AttributeEntry("Name table", row.NameTable),
            new AttributeEntry("Play type", row.PlayType),
            new AttributeEntry("Move type", row.MoveType),
            new AttributeEntry("Max move speed", row.MaxMoveSpeed.ToString("0.##")),
            new AttributeEntry("Collision radius", row.CollisionRadius.ToString("0.##")),
            new AttributeEntry("Duration (s)", row.DurationSeconds?.ToString("0.##")),
            new AttributeEntry("Skeleton", row.Skeleton),
            new AttributeEntry("Icon path", row.Icon),
            new AttributeEntry("Montage", row.Montage),
            new AttributeEntry("Montage found", row.MontageFound.ToString()),
            new AttributeEntry("Unused", row.Unused.ToString()),
            new AttributeEntry("Sequences", row.Sequences is { Length: > 0 } ? string.Join(", ", row.Sequences) : null),
            new AttributeEntry("Localization", row.Localization),
        };
}
