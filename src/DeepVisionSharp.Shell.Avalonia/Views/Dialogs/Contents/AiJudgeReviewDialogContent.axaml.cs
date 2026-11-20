using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Views.Dialogs.Contents;

/// <summary>
/// AI 建议审阅内容。deepvision 版在这里挂 Dialog.Closing 拦截写入期间的关闭，
/// 并订阅状态对象的 CloseRequested；审阅状态对象属于标注 lane，本波只保留布局。
/// </summary>
public partial class AiJudgeReviewDialogContent : UserControl
{
    public AiJudgeReviewDialogContent() => InitializeComponent();
}
