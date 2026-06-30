/***************************************************************************
 *   Hood Checker for Mac                                                  *
 *   Hood Checker © 2006 Andi8104, © 2007-2011 Mootilda                    *
 *   macOS port © 2026 GramzeSweatshop (rhiamom@mac.com)                   *
 *   GPL v2 or later. See Licences/GPL-LICENSE.txt                         *
 ***************************************************************************/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoodChecker.App.Views;
using LotExpander;

namespace HoodChecker.App.ViewModels;

// ---- 1. Welcome ----
public partial class WelcomePageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    public WelcomePageViewModel(MainWindowViewModel main) => _main = main;

    public string Blurb =>
        "Sims 2 Neighborhood Corruption Detector.\n\n" +
        "This program will check your neighborhood to see whether it can find " +
        "any signs of corruption, and will optionally attempt to fix those problems.\n\n" +
        "The program may not be able to handle neighborhoods which were saved with " +
        "Expansion or Stuff Packs released after The Sims™ 2 Mansion and Garden Stuff.\n\n" +
        "Press the Start button to begin.";

    [RelayCommand] private void Start() => _main.ShowChooseHood();
    [RelayCommand] private void Exit() => _main.Exit();
}

// ---- 2. Choose a Neighbourhood ----
public partial class ChooseHoodPageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public ChooseHoodPageViewModel(MainWindowViewModel main)
    {
        _main = main;
        foreach (NeighborhoodInfo nb in NeighborhoodCatalog.List())
            Hoods.Add(nb);
        SelectedHood = _main.SelectedHood;
    }

    public ObservableCollection<NeighborhoodInfo> Hoods { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private NeighborhoodInfo? _selectedHood;

    public bool HasHoods => Hoods.Count > 0;

    private bool CanNext() => SelectedHood != null;

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next()
    {
        _main.SelectedHood = SelectedHood;
        _main.ShowConfirm();
    }

    [RelayCommand] private void Back() => _main.ShowWelcome();

    [RelayCommand]
    private async Task Browse()
    {
        var files = await _main.Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a neighborhood main package",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Sims 2 Neighborhood")
                {
                    Patterns = new[] { "*_Neighborhood.package" }
                }
            }
        });
        if (files.Count == 0) return;

        string path = files[0].Path.LocalPath;
        string code = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        var info = new NeighborhoodInfo { Code = code, Name = code, MainPackagePath = path };
        _main.SelectedHood = info;
        _main.ShowConfirm();
    }
}

// ---- 3. Check vs Remove ----
public partial class ConfirmPageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    public ConfirmPageViewModel(MainWindowViewModel main) => _main = main;

    public string HoodName => _main.SelectedHood?.Name ?? "";

    public string Blurb =>
        "Click on Check to check this neighborhood for signs of corruption.\n\n" +
        "Click on Remove to attempt to fix this neighborhood by removing invalid references.";

    [RelayCommand] private void Check() => _main.ShowOptions(fix: false);
    [RelayCommand] private void Remove() => _main.ShowOptions(fix: true);
    [RelayCommand] private void Back() => _main.ShowChooseHood();
}

// ---- 4. Options ----
public partial class OptionsPageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    public OptionsPageViewModel(MainWindowViewModel main)
    {
        _main = main;
        ShowAllMemories = _main.ShowAllMemories;
    }

    public string HoodName => _main.SelectedHood?.Name ?? "";
    public bool IsFix => _main.Fix;

    [ObservableProperty] private bool _showAllMemories;

    [RelayCommand]
    private async Task Finish()
    {
        _main.ShowAllMemories = ShowAllMemories;
        await _main.RunCheckAsync();
    }

    [RelayCommand] private void Back() => _main.ShowConfirm();
}

// ---- 5. Results ----
public partial class ResultsPageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    public ResultsPageViewModel(MainWindowViewModel main) => _main = main;

    public string HoodName => _main.SelectedHood?.Name ?? "";
    public ObservableCollection<string> ReportLines { get; } = new();

    [ObservableProperty] private bool _isRunning;

    public async Task RunAsync()
    {
        IsRunning = true;
        string path = _main.SelectedHood!.MainPackagePath;
        bool fix = _main.Fix;
        bool all = _main.ShowAllMemories;

        var run = new HoodCheckRun { GraveyardPolicy = ResolveGraveyard };
        List<string> lines = await Task.Run(() => run.Run(path, fix, all));

        foreach (string line in lines)
            ReportLines.Add(line);
        IsRunning = false;
    }

    // Runs on the background check thread; marshal to the UI thread to prompt.
    private GraveyardChoice ResolveGraveyard(uint hoodId, List<R_DESC> lots)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
            await GraveyardDialog.PromptAsync(_main.Window, hoodId, lots)
        ).GetAwaiter().GetResult() ?? GraveyardChoice.Keep();
    }

    [RelayCommand] private void Restart() => _main.ShowChooseHood();
    [RelayCommand] private void Exit() => _main.Exit();

    [RelayCommand]
    private async Task Save()
    {
        var file = await _main.Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save report",
            SuggestedFileName = (_main.SelectedHood?.Code ?? "neighborhood") + "_HoodCheck.txt",
            DefaultExtension = "txt"
        });
        if (file == null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        foreach (string line in ReportLines)
            await writer.WriteLineAsync(line);
    }
}
