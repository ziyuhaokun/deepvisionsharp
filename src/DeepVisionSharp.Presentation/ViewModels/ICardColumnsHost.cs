namespace DeepVisionSharp.Presentation.ViewModels;

/// <summary>
/// 卡片墙响应式列数的承载者。页面按可用宽度算出 1–4 栏后回写，
/// 由宿主页面在真实 ViewModel 与设计时兜底上下文之间统一处理。
/// </summary>
public interface ICardColumnsHost
{
    int CardColumns { get; set; }
}
