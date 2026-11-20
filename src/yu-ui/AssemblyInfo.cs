using System.Windows;
using System.Windows.Markup;

// Themes/Generic.xaml 是本程序集的默认样式入口（按 DefaultStyleKey 查找）。
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

// 使用方写法：xmlns:yu="http://schemas.yu-ui/xaml"，控件前缀 yu:
[assembly: XmlnsDefinition("http://schemas.yu-ui/xaml", "YuUI")]
[assembly: XmlnsDefinition("http://schemas.yu-ui/xaml", "YuUI.Controls")]
[assembly: XmlnsPrefix("http://schemas.yu-ui/xaml", "yu")]
