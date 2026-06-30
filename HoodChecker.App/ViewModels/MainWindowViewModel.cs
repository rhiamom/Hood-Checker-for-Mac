/***************************************************************************
 *   Hood Checker for Mac                                                  *
 *   Hood Checker © 2006 Andi8104, © 2007-2011 Mootilda                    *
 *   macOS port © 2026 GramzeSweatshop (rhiamom@mac.com)                   *
 *   GPL v2 or later. See Licences/GPL-LICENSE.txt                         *
 ***************************************************************************/

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using LotExpander;

namespace HoodChecker.App.ViewModels;

/// <summary>
/// Wizard shell: holds the page being shown plus the choices that carry across
/// pages (selected hood, fix-vs-check, show-all-memories), mirroring the
/// original PrimaryForm screen-state machine.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    public Window Window { get; }

    public MainWindowViewModel(Window window)
    {
        Window = window;
        ShowWelcome();
    }

    [ObservableProperty]
    private object? _currentPage;

    // Carried across the wizard
    public NeighborhoodInfo? SelectedHood { get; set; }
    public bool Fix { get; set; }
    public bool ShowAllMemories { get; set; }

    public void ShowWelcome()    => CurrentPage = new WelcomePageViewModel(this);
    public void ShowChooseHood() => CurrentPage = new ChooseHoodPageViewModel(this);
    public void ShowConfirm()    => CurrentPage = new ConfirmPageViewModel(this);

    public void ShowOptions(bool fix)
    {
        Fix = fix;
        CurrentPage = new OptionsPageViewModel(this);
    }

    public async Task RunCheckAsync()
    {
        var results = new ResultsPageViewModel(this);
        CurrentPage = results;
        await results.RunAsync();
    }

    public void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
