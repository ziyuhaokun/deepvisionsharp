namespace DeepVisionSharp.Shell.Avalonia.ViewModels.Dialogs;

/// <summary>对话框下拉项：只用于 ItemTemplate 显示一行文字。</summary>
public sealed record SampleDisplayTextItem(string DisplayText);

/// <summary>对话框下拉项：显示名（设备、模型一类的 DisplayName 绑定）。</summary>
public sealed record SampleDisplayNameItem(string DisplayName);

/// <summary>对话框下拉项：SelectedValue + Label（选项字典一类的绑定）。</summary>
public sealed record SampleValueLabelItem(string Value, string Label);
