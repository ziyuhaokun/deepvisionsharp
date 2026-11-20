using System.Windows;
using System.Windows.Markup;

// Themes/Generic.xaml 是本程序集的默认样式入口（按 DefaultStyleKey 查找）。
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

// 使用方写法：xmlns:dvsui="http://schemas.deepvisionsharp.ui/xaml"，控件前缀 dvsui:
[assembly: XmlnsDefinition("http://schemas.deepvisionsharp.ui/xaml", "DeepVisionSharp.UI")]
[assembly: XmlnsDefinition("http://schemas.deepvisionsharp.ui/xaml", "DeepVisionSharp.UI.Controls")]
[assembly: XmlnsPrefix("http://schemas.deepvisionsharp.ui/xaml", "dvsui")]
