using System.Windows;
using System.Windows.Controls;

namespace DeepVisionSharp.Shell.Controls;

/// <summary>
/// 让 WPF 原生 <see cref="PasswordBox"/> 的明文可以像依赖属性一样绑定。
/// <see cref="PasswordBox.Password"/> 不是依赖属性（没有 PasswordBox.PasswordProperty，绑不上），
/// 这里用附加属性在 PasswordChanged 与绑定源之间双向中转。
/// </summary>
public static class PasswordBoxAssist
{
    /// <summary>
    /// 密码明文，默认绑定方向 TwoWay。
    /// 默认值取 <c>null</c> 而不是空串：绑定安装时若初值与属性默认值相等，WPF 不会触发变更回调（实测），
    /// 而 PasswordChanged 的订阅只能挂在回调里，默认 null 才能保证「初值是空串」的绑定同样挂上订阅。
    /// </summary>
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password",
        typeof(string),
        typeof(PasswordBoxAssist),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnPasswordPropertyChanged));

    /// <summary>占位文本：密码为空时由视图里叠在 <see cref="PasswordBox"/> 上的 TextBlock 显示。</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder",
        typeof(string),
        typeof(PasswordBoxAssist),
        new FrameworkPropertyMetadata(null));

    /// <summary>防重入标记：回写控件与回写附加属性互斥，避免两侧互相触发成环。</summary>
    private static readonly DependencyProperty IsSynchronizingProperty = DependencyProperty.RegisterAttached(
        "IsSynchronizing",
        typeof(bool),
        typeof(PasswordBoxAssist),
        new PropertyMetadata(false));

    public static string? GetPassword(DependencyObject d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return (string?)d.GetValue(PasswordProperty);
    }

    public static void SetPassword(DependencyObject d, string? value)
    {
        ArgumentNullException.ThrowIfNull(d);
        d.SetValue(PasswordProperty, value);
    }

    public static string? GetPlaceholder(DependencyObject d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return (string?)d.GetValue(PlaceholderProperty);
    }

    public static void SetPlaceholder(DependencyObject d, string? value)
    {
        ArgumentNullException.ThrowIfNull(d);
        d.SetValue(PlaceholderProperty, value);
    }

    private static void OnPasswordPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox passwordBox)
        {
            return;
        }

        // 附加属性只对 PasswordBox 生效；订阅幂等，先从控件回写附加属性的路径要保证始终在线。
        passwordBox.PasswordChanged -= OnPasswordBoxPasswordChanged;
        passwordBox.PasswordChanged += OnPasswordBoxPasswordChanged;

        if (GetIsSynchronizing(passwordBox))
        {
            return;
        }

        var password = e.NewValue as string ?? string.Empty;
        if (passwordBox.Password == password)
        {
            return;
        }

        SetIsSynchronizing(passwordBox, true);
        try
        {
            passwordBox.Password = password;
        }
        finally
        {
            SetIsSynchronizing(passwordBox, false);
        }
    }

    private static void OnPasswordBoxPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox || GetIsSynchronizing(passwordBox))
        {
            return;
        }

        SetIsSynchronizing(passwordBox, true);
        try
        {
            // TwoWay 绑定在这一步把输入回写给绑定源。
            SetPassword(passwordBox, passwordBox.Password);
        }
        finally
        {
            SetIsSynchronizing(passwordBox, false);
        }
    }

    private static bool GetIsSynchronizing(DependencyObject d) => (bool)d.GetValue(IsSynchronizingProperty);

    private static void SetIsSynchronizing(DependencyObject d, bool value)
        => d.SetValue(IsSynchronizingProperty, value);
}
