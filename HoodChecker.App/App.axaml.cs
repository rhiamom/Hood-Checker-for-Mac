/***************************************************************************
 *   Hood Checker for Mac — GPL v2 or later                                *
 ***************************************************************************/

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HoodChecker.App.ViewModels;
using HoodChecker.App.Views;

namespace HoodChecker.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.DataContext = new MainWindowViewModel(window);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
