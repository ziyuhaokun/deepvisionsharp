using DeepVisionSharp.Shell.Services.Dialogs;
using YuUI.Controls;
using DeepVisionSharp.Presentation.Services.Dialogs;

namespace DeepVisionSharp.Shell.Services.Hosting;

/// <summary>
/// 对话框宿主（由 <c>MainWindow</c> 实现）。
/// 取代 WPF-UI 的 <c>IContentDialogService</c>：<c>AppDialogService</c> 负责组装 yu:Dialog 的内容与按钮，
/// 宿主只负责模态呈现并把按钮结果回传。
/// </summary>
public interface IDialogHost
{
    bool IsAttached { get; }

    /// <summary>呈现对话框并等待结果；同一时刻只允许一个。</summary>
    Task<AppDialogResult> ShowAsync(Dialog dialog, CancellationToken cancellationToken);

    /// <summary>由对话框里的按钮调用，关闭当前对话框并把结果回传给 <see cref="ShowAsync"/>。</summary>
    void Close(AppDialogResult result);
}
