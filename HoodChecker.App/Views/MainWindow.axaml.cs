/***************************************************************************
 *   Hood Checker for Mac — GPL v2 or later                                *
 ***************************************************************************/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HoodChecker.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
