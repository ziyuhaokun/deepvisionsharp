using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace YuUI.Controls;

/// <summary>带上下限、步进与单位的数字输入框，对应 WPF 版 YuUI.Controls.NumericBox。</summary>
public class NumericBox : TemplatedControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<NumericBox, double>(nameof(Value), 0d, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<NumericBox, double>(nameof(Minimum), 0d);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<NumericBox, double>(nameof(Maximum), 1000d);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<NumericBox, double>(nameof(Step), 1d);

    public static readonly StyledProperty<int> DecimalPlacesProperty =
        AvaloniaProperty.Register<NumericBox, int>(nameof(DecimalPlaces), 2);

    public static readonly StyledProperty<bool> FixedDecimalPlacesProperty =
        AvaloniaProperty.Register<NumericBox, bool>(nameof(FixedDecimalPlaces), false);

    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<NumericBox, string?>(nameof(Unit));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<NumericBox, bool>(nameof(IsReadOnly));

    private TextBox? _textBox;
    private bool _syncing;

    protected override Type StyleKeyOverride => typeof(NumericBox);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public int DecimalPlaces { get => GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }
    public bool FixedDecimalPlaces { get => GetValue(FixedDecimalPlacesProperty); set => SetValue(FixedDecimalPlacesProperty, value); }
    public string? Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_textBox is not null)
        {
            _textBox.LostFocus -= OnTextBoxLostFocus;
        }

        _textBox = e.NameScope.Find<TextBox>("PART_TextBox");
        if (_textBox is not null)
        {
            _textBox.LostFocus += OnTextBoxLostFocus;
        }

        if (e.NameScope.Find<Button>("PART_UpButton") is { } up)
        {
            up.Click += (_, _) => StepBy(Step);
        }

        if (e.NameScope.Find<Button>("PART_DownButton") is { } down)
        {
            down.Click += (_, _) => StepBy(-Step);
        }

        SyncText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            SyncText();
        }
    }

    private void StepBy(double delta)
    {
        var next = Math.Clamp(Value + delta, Minimum, Maximum);
        Value = Math.Round(next, Math.Max(0, DecimalPlaces));
    }

    private void OnTextBoxLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_textBox is null || _syncing)
        {
            return;
        }

        if (double.TryParse(_textBox.Text, out var parsed))
        {
            Value = Math.Round(Math.Clamp(parsed, Minimum, Maximum), Math.Max(0, DecimalPlaces));
        }

        SyncText();
    }

    private void SyncText()
    {
        if (_textBox is null)
        {
            return;
        }

        _syncing = true;
        _textBox.Text = FixedDecimalPlaces
            ? Value.ToString("F" + Math.Max(0, DecimalPlaces))
            : Value.ToString("0." + new string('#', Math.Max(0, DecimalPlaces)));
        _syncing = false;
    }
}
