using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DeepVisionSharp.UI.Controls;

/// <summary>
/// 带上下限、步进与单位的数字输入框。行为逐条照抄源 NumericBox.cs：
/// 失焦时解析文本并按 [Minimum, Maximum] 夹取、步进按钮加减 Step、按 DecimalPlaces/FixedDecimalPlaces 回写格式化文本。
/// </summary>
public class NumericBox : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(NumericBox),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericBox), new FrameworkPropertyMetadata(0d));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericBox), new FrameworkPropertyMetadata(1000d));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumericBox), new FrameworkPropertyMetadata(1d));

    public static readonly DependencyProperty DecimalPlacesProperty =
        DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(NumericBox), new FrameworkPropertyMetadata(2));

    public static readonly DependencyProperty FixedDecimalPlacesProperty =
        DependencyProperty.Register(nameof(FixedDecimalPlaces), typeof(bool), typeof(NumericBox), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(NumericBox), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(NumericBox), new FrameworkPropertyMetadata(false));

    private TextBox? _textBox;
    private Button? _upButton;
    private Button? _downButton;
    private bool _syncing;

    static NumericBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NumericBox), new FrameworkPropertyMetadata(typeof(NumericBox)));
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public int DecimalPlaces
    {
        get => (int)GetValue(DecimalPlacesProperty);
        set => SetValue(DecimalPlacesProperty, value);
    }

    public bool FixedDecimalPlaces
    {
        get => (bool)GetValue(FixedDecimalPlacesProperty);
        set => SetValue(FixedDecimalPlacesProperty, value);
    }

    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_textBox is not null)
        {
            _textBox.LostFocus -= OnTextBoxLostFocus;
        }

        if (_upButton is not null)
        {
            _upButton.Click -= OnUpButtonClick;
        }

        if (_downButton is not null)
        {
            _downButton.Click -= OnDownButtonClick;
        }

        _textBox = GetTemplateChild("PART_TextBox") as TextBox;
        if (_textBox is not null)
        {
            _textBox.LostFocus += OnTextBoxLostFocus;
        }

        _upButton = GetTemplateChild("PART_UpButton") as Button;
        if (_upButton is not null)
        {
            _upButton.Click += OnUpButtonClick;
        }

        _downButton = GetTemplateChild("PART_DownButton") as Button;
        if (_downButton is not null)
        {
            _downButton.Click += OnDownButtonClick;
        }

        SyncText();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((NumericBox)d).SyncText();
    }

    private void OnUpButtonClick(object sender, RoutedEventArgs e)
    {
        StepBy(Step);
    }

    private void OnDownButtonClick(object sender, RoutedEventArgs e)
    {
        StepBy(-Step);
    }

    private void StepBy(double delta)
    {
        var next = Math.Clamp(Value + delta, Minimum, Maximum);
        Value = Math.Round(next, Math.Max(0, DecimalPlaces));
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_textBox is null || _syncing)
        {
            return;
        }

        if (double.TryParse(
                _textBox.Text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture,
                out var parsed))
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
            ? Value.ToString("F" + Math.Max(0, DecimalPlaces), CultureInfo.CurrentCulture)
            : Value.ToString("0." + new string('#', Math.Max(0, DecimalPlaces)), CultureInfo.CurrentCulture);
        _syncing = false;
    }
}
