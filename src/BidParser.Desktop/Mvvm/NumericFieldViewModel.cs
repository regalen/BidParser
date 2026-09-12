using System.ComponentModel;
using System.Globalization;

namespace BidParser.Desktop.Mvvm;

/// <summary>
/// One pricing input. Validates as the user types, normalises to the field's scale on focus loss,
/// and reports its own validation message. <see cref="IsDirty"/> records that the user typed here,
/// so a later default (bundled or remote) never overwrites an edit.
/// </summary>
public sealed class NumericFieldViewModel(
    string label,
    string unit,
    string helper,
    int decimals,
    bool mustBePositive = false) : ObservableObject, IDataErrorInfo
{
    private string text = string.Empty;
    private bool isRequired;

    public string Label { get; } = label;

    /// <summary>Suffix rendered inside the input (e.g. "%"); never part of the parsed value.</summary>
    public string Unit { get; } = unit;

    public string Helper { get; } = helper;

    /// <summary>True once the user has typed in this field during the current vendor session.</summary>
    public bool IsDirty { get; private set; }

    public decimal? Value { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;

    public bool IsRequired
    {
        get => isRequired;
        set
        {
            if (SetProperty(ref isRequired, value))
            {
                Validate();
            }
        }
    }

    public string Text
    {
        get => text;
        set
        {
            if (!SetProperty(ref text, value ?? string.Empty))
            {
                return;
            }

            IsDirty = true;
            Validate();
        }
    }

    string IDataErrorInfo.Error => ErrorMessage ?? string.Empty;

    string IDataErrorInfo.this[string columnName] =>
        columnName == nameof(Text) ? ErrorMessage ?? string.Empty : string.Empty;

    /// <summary>Applies a vendor default, leaving a field the user has edited untouched.</summary>
    public void ApplyDefault(decimal? value)
    {
        if (IsDirty)
        {
            return;
        }

        SetTextWithoutDirty(value is null ? string.Empty : Format(value.Value));
    }

    /// <summary>Clears the value and the edit history — used on Reset and on a vendor change.</summary>
    public void Clear()
    {
        IsDirty = false;
        SetTextWithoutDirty(string.Empty);
    }

    /// <summary>Formats a valid value to the field's configured scale after editing finishes.</summary>
    public void Normalize()
    {
        if (Value is not { } value)
        {
            return;
        }

        SetTextWithoutDirty(Format(value));
    }

    private void SetTextWithoutDirty(string value)
    {
        if (SetProperty(ref text, value, nameof(Text)))
        {
            Validate();
        }
    }

    private void Validate()
    {
        var raw = text.Trim();

        if (raw.Length == 0)
        {
            Value = null;
            ErrorMessage = IsRequired ? $"{Label} is required." : null;
        }
        else if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
        {
            Value = null;
            ErrorMessage = $"{Label} must be a number.";
        }
        else if (parsed < 0m)
        {
            Value = null;
            ErrorMessage = $"{Label} cannot be negative.";
        }
        else if (mustBePositive && parsed == 0m)
        {
            Value = null;
            ErrorMessage = $"{Label} must be greater than 0.";
        }
        else
        {
            Value = Math.Round(parsed, decimals, MidpointRounding.AwayFromZero);
            ErrorMessage = null;
        }

        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
    }

    private string Format(decimal value)
        => value.ToString($"F{decimals}", CultureInfo.CurrentCulture);
}
