using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using BidParser.Application.Output;
using BidParser.Desktop.Mvvm;
using BidParser.Desktop.Services;
using BidParser.Domain.Models;
using BidParser.Output;

namespace BidParser.Desktop.ViewModels;

public sealed class LinePricingRowViewModel : ObservableObject, IDataErrorInfo
{
    private readonly LinePricingLine line;
    private readonly bool imPercentRequired;
    private readonly bool rowImEditable;
    private readonly Action onChanged;

    private string imPercentText = string.Empty;
    private string upliftText = string.Empty;
    private bool isImOverridden;
    private bool isImEditedAfterApply;
    private bool isUpliftEditedAfterApply;

    private decimal? parsedImPercent;
    private decimal? parsedUplift;
    private string? imErrorMessage;
    private string? upliftErrorMessage;

    private decimal? unitCost;
    private decimal? extCost;
    private decimal? unitPrice;
    private decimal? extPrice;

    public LinePricingRowViewModel(
        LinePricingLine line,
        bool imPercentEditable,
        bool imPercentRequired,
        decimal? defaultImPercent,
        decimal? defaultMargin,
        Action onChanged)
    {
        this.line = line;
        this.imPercentRequired = imPercentRequired;
        this.rowImEditable = !line.IsCancelled && imPercentEditable && line.Msrp is > 0m;
        this.onChanged = onChanged;

        if (!line.IsCancelled)
        {
            if (imPercentRequired && this.rowImEditable)
            {
                isImOverridden = true;
                imPercentText = defaultImPercent?.ToString("F2", CultureInfo.CurrentCulture) ?? string.Empty;
            }
            else if (line.DerivedImPercent is { } derived)
            {
                isImOverridden = false;
                imPercentText = derived.ToString("F2", CultureInfo.CurrentCulture);
            }

            upliftText = defaultMargin?.ToString("F2", CultureInfo.CurrentCulture) ?? string.Empty;
        }

        Recalculate(notifyParent: false);
    }

    public int Index => line.Index;
    public string LineNumber => line.LineNumber;
    public string Vpn => line.Vpn;
    public string Description => line.Description ?? string.Empty;
    public int Qty => line.Qty;
    public decimal? Msrp => line.Msrp;
    public decimal? SourceCost => line.SourceCost;
    public decimal? DerivedImPercent => line.DerivedImPercent;
    public bool IsCancelled => line.IsCancelled;
    public bool IsImEditable => rowImEditable;
    public bool IsUpliftEditable => !line.IsCancelled;

    public string MsrpDisplay => FormatMoney(line.Msrp);
    public string SourceCostDisplay => FormatMoney(line.SourceCost);

    public bool IsImOverridden
    {
        get => isImOverridden;
        private set
        {
            if (SetProperty(ref isImOverridden, value))
            {
                OnPropertyChanged(nameof(IsImHighlighted));
            }
        }
    }

    public bool IsImEditedAfterApply
    {
        get => isImEditedAfterApply;
        private set
        {
            if (SetProperty(ref isImEditedAfterApply, value))
            {
                OnPropertyChanged(nameof(IsImHighlighted));
            }
        }
    }

    public bool IsImHighlighted => IsImEditedAfterApply || (!imPercentRequired && IsImOverridden);

    public bool IsUpliftEditedAfterApply
    {
        get => isUpliftEditedAfterApply;
        private set => SetProperty(ref isUpliftEditedAfterApply, value);
    }

    public string ImPercent
    {
        get => imPercentText;
        set
        {
            if (!rowImEditable)
            {
                return;
            }

            var next = value ?? string.Empty;
            var changed = SetProperty(ref imPercentText, next);

            if (!imPercentRequired && string.IsNullOrWhiteSpace(next))
            {
                if (!changed && !IsImOverridden && !IsImEditedAfterApply)
                {
                    return;
                }

                IsImOverridden = false;
                IsImEditedAfterApply = false;
            }
            else
            {
                if (!changed && IsImOverridden && IsImEditedAfterApply)
                {
                    return;
                }

                IsImOverridden = true;
                IsImEditedAfterApply = true;
            }

            Recalculate(notifyParent: true);
        }
    }

    public string Uplift
    {
        get => upliftText;
        set
        {
            if (line.IsCancelled)
            {
                return;
            }

            var next = value ?? string.Empty;
            if (!SetProperty(ref upliftText, next))
            {
                return;
            }

            IsUpliftEditedAfterApply = true;
            Recalculate(notifyParent: true);
        }
    }

    public decimal? UnitCost => unitCost;
    public decimal? ExtCost => extCost;
    public decimal? UnitPrice => unitPrice;
    public decimal? ExtPrice => extPrice;

    public string UnitCostDisplay => FormatMoney(unitCost is { } u ? LinePricingMath.Round2(u) : null);
    public string ExtCostDisplay => FormatMoney(extCost);
    public string UnitPriceDisplay => FormatMoney(unitPrice);
    public string ExtPriceDisplay => FormatMoney(extPrice);
    public bool IsUnitCostNegative => unitCost is < 0m;

    public string? ImErrorMessage => imErrorMessage;
    public string? UpliftErrorMessage => upliftErrorMessage;
    public string? RowErrorMessage => imErrorMessage ?? upliftErrorMessage;
    public bool IsValid => imErrorMessage is null && upliftErrorMessage is null;

    string IDataErrorInfo.Error => RowErrorMessage ?? string.Empty;

    string IDataErrorInfo.this[string columnName] => columnName switch
    {
        nameof(ImPercent) => imErrorMessage ?? string.Empty,
        nameof(Uplift) => upliftErrorMessage ?? string.Empty,
        _ => string.Empty
    };

    internal void ApplyHeaderImPercent(decimal value)
    {
        if (!rowImEditable)
        {
            return;
        }

        imPercentText = value.ToString("F2", CultureInfo.CurrentCulture);
        IsImOverridden = true;
        IsImEditedAfterApply = false;
        OnPropertyChanged(nameof(ImPercent));
        Recalculate(notifyParent: true);
    }

    internal void ApplyHeaderUplift(decimal value)
    {
        if (line.IsCancelled)
        {
            return;
        }

        upliftText = value.ToString("F2", CultureInfo.CurrentCulture);
        IsUpliftEditedAfterApply = false;
        OnPropertyChanged(nameof(Uplift));
        Recalculate(notifyParent: true);
    }

    public LinePricingEntry ToEntry()
    {
        var effectiveMargin = line.IsCancelled ? 0m : parsedUplift ?? 0m;
        decimal? effectiveIm = rowImEditable && (imPercentRequired || IsImOverridden)
            ? parsedImPercent
            : null;

        return new LinePricingEntry(line.Index, line.Vpn, effectiveMargin, effectiveIm);
    }

    private void Recalculate(bool notifyParent)
    {
        if (line.IsCancelled)
        {
            parsedImPercent = null;
            parsedUplift = 0m;
            imErrorMessage = null;
            upliftErrorMessage = null;
            unitCost = null;
            extCost = null;
            unitPrice = null;
            extPrice = null;
            NotifyCalculatedProperties();
            if (notifyParent) onChanged();
            return;
        }

        // Validate IM%
        imErrorMessage = null;
        parsedImPercent = null;
        if (rowImEditable)
        {
            var rawIm = imPercentText.Trim();
            if (rawIm.Length == 0)
            {
                if (imPercentRequired)
                {
                    imErrorMessage = $"Line {line.LineNumber}: IM% is required.";
                }
            }
            else if (!TryParseDecimal(rawIm, out var imVal))
            {
                imErrorMessage = $"Line {line.LineNumber}: IM% must be a number.";
            }
            else if (imPercentRequired || IsImOverridden)
            {
                if (imVal < 0m)
                {
                    imErrorMessage = $"Line {line.LineNumber}: IM% cannot be negative.";
                }
                else if (imVal > 100m)
                {
                    // Above 100 the discount exceeds MSRP and the cost would be negative.
                    imErrorMessage = $"Line {line.LineNumber}: IM% cannot be more than 100.";
                }
                else if (imVal != Math.Round(imVal, 2))
                {
                    imErrorMessage = $"Line {line.LineNumber}: IM% must have at most 2 decimal places.";
                }
                else
                {
                    parsedImPercent = Math.Round(imVal, 2, MidpointRounding.AwayFromZero);
                }
            }
        }

        // Validate Uplift
        upliftErrorMessage = null;
        parsedUplift = null;
        var rawUplift = upliftText.Trim();
        if (rawUplift.Length == 0)
        {
            upliftErrorMessage = $"Line {line.LineNumber}: Uplift is required.";
        }
        else if (!TryParseDecimal(rawUplift, out var upliftVal))
        {
            upliftErrorMessage = $"Line {line.LineNumber}: Uplift must be a number.";
        }
        else if (upliftVal < 0m || upliftVal >= 100m)
        {
            upliftErrorMessage = $"Line {line.LineNumber}: Uplift must be at least 0 and less than 100.";
        }
        else if (upliftVal != Math.Round(upliftVal, 2))
        {
            upliftErrorMessage = $"Line {line.LineNumber}: Uplift must have at most 2 decimal places.";
        }
        else
        {
            parsedUplift = Math.Round(upliftVal, 2, MidpointRounding.AwayFromZero);
        }

        if (imErrorMessage is not null)
        {
            unitCost = null;
            extCost = null;
            unitPrice = null;
            extPrice = null;
            NotifyCalculatedProperties();
            if (notifyParent) onChanged();
            return;
        }

        var item = new LineItem
        {
            Vpn = line.Vpn,
            Description = line.Description,
            Qty = line.Qty,
            Msrp = line.Msrp,
            Cost = line.SourceCost ?? 0m,
            ImPercent = rowImEditable && (imPercentRequired || IsImOverridden)
                ? parsedImPercent ?? 0m
                : null
        };

        var uCost = LinePricingMath.EffectiveUnitCost(item, imPercentRequired, headerImPercent: null);
        var eCost = LinePricingMath.ExtCost(line.Qty, uCost);
        unitCost = uCost;
        extCost = eCost;

        if (upliftErrorMessage is not null || parsedUplift is not { } validUplift)
        {
            unitPrice = null;
            extPrice = null;
        }
        else
        {
            var uPrice = LinePricingMath.UnitPrice(uCost, validUplift);
            var ePrice = LinePricingMath.ExtPrice(line.Qty, uPrice);
            unitPrice = uPrice;
            extPrice = ePrice;
        }

        NotifyCalculatedProperties();
        if (notifyParent) onChanged();
    }

    private void NotifyCalculatedProperties()
    {
        OnPropertyChanged(nameof(UnitCost));
        OnPropertyChanged(nameof(ExtCost));
        OnPropertyChanged(nameof(UnitPrice));
        OnPropertyChanged(nameof(ExtPrice));
        OnPropertyChanged(nameof(UnitCostDisplay));
        OnPropertyChanged(nameof(ExtCostDisplay));
        OnPropertyChanged(nameof(UnitPriceDisplay));
        OnPropertyChanged(nameof(ExtPriceDisplay));
        OnPropertyChanged(nameof(IsUnitCostNegative));
        OnPropertyChanged(nameof(ImErrorMessage));
        OnPropertyChanged(nameof(UpliftErrorMessage));
        OnPropertyChanged(nameof(RowErrorMessage));
        OnPropertyChanged(nameof(IsValid));
    }

    private static bool TryParseDecimal(string raw, out decimal parsed) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed)
        || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);

    private static string FormatMoney(decimal? value) =>
        value is null ? "—" : value.Value.ToString("N2", CultureInfo.CurrentCulture);
}

public sealed class LinePricingViewModel : ObservableObject
{
    private readonly Action onNext;
    private readonly Action onCancel;
    private LinePricingTotals totals = new(0m, 0m, 0m, null);
    private double dialogWidth = double.NaN;
    private double dialogHeight = double.NaN;

    public LinePricingViewModel(
        LinePricingPreview preview,
        string sourceFilename,
        VendorDefaults? defaults = null,
        Action? onNext = null,
        Action? onCancel = null)
    {
        Preview = preview ?? throw new ArgumentNullException(nameof(preview));
        SourceFilename = sourceFilename;
        this.onNext = onNext ?? (() => { });
        this.onCancel = onCancel ?? (() => { });

        FxRate = new NumericFieldViewModel("Exchange rate", "USD to AUD", "4 d.p.", decimals: 4, mustBePositive: true)
        {
            IsRequired = preview.UsesFxRate,
            RejectExcessPrecision = true
        };
        HeaderImPercent = new NumericFieldViewModel("IM%", "%", "Discount off MSRP", decimals: 2)
        {
            IsRequired = false,
            RejectExcessPrecision = true,
            MaxInclusiveValue = 100m
        };
        HeaderUplift = new NumericFieldViewModel("Uplift", "%", "margin on sale price", decimals: 2)
        {
            IsRequired = false,
            RejectExcessPrecision = true,
            MaxExclusiveValue = 100m
        };

        FxRate.ApplyDefault(defaults?.FxRate);
        HeaderImPercent.ApplyDefault(defaults?.ImPercent);
        HeaderUplift.ApplyDefault(defaults?.Margin);

        Rows = new ObservableCollection<LinePricingRowViewModel>(
            preview.Lines.Select(line => new LinePricingRowViewModel(
                line,
                preview.ImPercentEditable,
                preview.ImPercentRequired,
                defaults?.ImPercent,
                defaults?.Margin,
                OnRowsOrHeaderChanged)));

        FxRate.PropertyChanged += (_, _) => OnRowsOrHeaderChanged();
        HeaderImPercent.PropertyChanged += (_, _) => OnRowsOrHeaderChanged();
        HeaderUplift.PropertyChanged += (_, _) => OnRowsOrHeaderChanged();

        ApplyImPercentCommand = new RelayCommand(
            ApplyImPercentToAll,
            () => preview.ImPercentEditable && HeaderImPercent.Value is not null && !HeaderImPercent.HasError);
        ApplyUpliftCommand = new RelayCommand(
            ApplyUpliftToAll,
            () => HeaderUplift.Value is not null && !HeaderUplift.HasError);
        NextCommand = new RelayCommand(() =>
        {
            if (IsValid)
            {
                this.onNext();
            }
        }, () => IsValid);
        CancelCommand = new RelayCommand(() => this.onCancel());

        RecalculateTotals();
    }

    public LinePricingPreview Preview { get; }
    public string SourceFilename { get; }
    public string Subtitle => $"{SourceFilename} · {Preview.ParserDisplayName} · {Preview.Lines.Count} {(Preview.Lines.Count == 1 ? "line" : "lines")}";
    public string Currency => string.IsNullOrWhiteSpace(Preview.Currency) ? "USD" : Preview.Currency;
    public string CurrencyBadgeText => $"All values {Currency}";
    public bool UsesFxRate => Preview.UsesFxRate;
    public bool ImPercentEditable => Preview.ImPercentEditable;
    public bool ImPercentRequired => Preview.ImPercentRequired;
    public string ImColumnHeader => Preview.ImPercentEditable ? "IM%*" : "IM%";

    /// <summary>
    /// The review's user-dragged size in DIPs. <see cref="double.NaN"/> (the default) lets the
    /// review fill the window. Held on the view model so <c>Edit pricing</c> reopens it at the same
    /// size; it lives only as long as this review and is never persisted.
    /// </summary>
    public double DialogWidth
    {
        get => dialogWidth;
        set => SetProperty(ref dialogWidth, value);
    }

    /// <inheritdoc cref="DialogWidth" />
    public double DialogHeight
    {
        get => dialogHeight;
        set => SetProperty(ref dialogHeight, value);
    }

    public NumericFieldViewModel FxRate { get; }
    public NumericFieldViewModel HeaderImPercent { get; }
    public NumericFieldViewModel HeaderUplift { get; }

    public ObservableCollection<LinePricingRowViewModel> Rows { get; }

    public RelayCommand ApplyImPercentCommand { get; }
    public RelayCommand ApplyUpliftCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand CancelCommand { get; }

    public decimal TotalCost => totals.TotalCost;
    public decimal TotalPrice => totals.TotalPrice;
    public decimal TotalMarginAmount => totals.MarginAmount;
    public decimal? TotalMarginPercent => totals.MarginPercent;

    /// <summary>
    /// Totals are shown only when every line is complete and valid; a partial sum would leave out
    /// some lines' prices while keeping their costs and misstate the margin.
    /// </summary>
    public bool AreTotalsAvailable => Rows.All(row => row.IsCancelled || row.IsValid);

    // Totals carry no currency prefix: the header badge already states the currency once.
    public string TotalCostDisplay => AreTotalsAvailable
        ? TotalCost.ToString("N2", CultureInfo.CurrentCulture)
        : "—";
    public string TotalPriceDisplay => AreTotalsAvailable
        ? TotalPrice.ToString("N2", CultureInfo.CurrentCulture)
        : "—";
    public string TotalMarginAmountDisplay => AreTotalsAvailable
        ? TotalMarginAmount.ToString("N2", CultureInfo.CurrentCulture)
        : "—";
    public string TotalMarginPercentDisplay => AreTotalsAvailable && TotalMarginPercent is { } pct
        ? $"{pct.ToString("F2", CultureInfo.CurrentCulture)}%"
        : "—";

    public bool IsValid =>
        (!Preview.UsesFxRate || (FxRate.Value is > 0m && !FxRate.HasError))
        && Rows.All(row => row.IsValid);

    public IReadOnlyList<LinePricingEntry> ToEntries() =>
        Rows.Select(row => row.ToEntry()).ToList();

    private void ApplyImPercentToAll()
    {
        if (!Preview.ImPercentEditable || HeaderImPercent.Value is not { } value || HeaderImPercent.HasError)
        {
            return;
        }

        foreach (var row in Rows)
        {
            row.ApplyHeaderImPercent(value);
        }
    }

    private void ApplyUpliftToAll()
    {
        if (HeaderUplift.Value is not { } value || HeaderUplift.HasError)
        {
            return;
        }

        foreach (var row in Rows)
        {
            row.ApplyHeaderUplift(value);
        }
    }

    private void OnRowsOrHeaderChanged()
    {
        RecalculateTotals();
        OnPropertyChanged(nameof(IsValid));
        ApplyImPercentCommand.NotifyCanExecuteChanged();
        ApplyUpliftCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    private void RecalculateTotals()
    {
        var totalCost = 0m;
        var totalPrice = 0m;
        foreach (var row in Rows)
        {
            if (row.IsCancelled)
            {
                continue;
            }

            totalCost += row.ExtCost ?? 0m;
            totalPrice += row.ExtPrice ?? 0m;
        }

        var marginAmount = totalPrice - totalCost;
        decimal? marginPercent = totalPrice > 0m
            ? LinePricingMath.Round2(marginAmount / totalPrice * 100m)
            : null;
        totals = new LinePricingTotals(totalCost, totalPrice, marginAmount, marginPercent);

        OnPropertyChanged(nameof(TotalCost));
        OnPropertyChanged(nameof(TotalPrice));
        OnPropertyChanged(nameof(TotalMarginAmount));
        OnPropertyChanged(nameof(TotalMarginPercent));
        OnPropertyChanged(nameof(AreTotalsAvailable));
        OnPropertyChanged(nameof(TotalCostDisplay));
        OnPropertyChanged(nameof(TotalPriceDisplay));
        OnPropertyChanged(nameof(TotalMarginAmountDisplay));
        OnPropertyChanged(nameof(TotalMarginPercentDisplay));
    }
}
