/***************************************************************************
 *   Hood Checker for Mac                                                  *
 *   Hood Checker © 2006 Andi8104, © 2007-2011 Mootilda                    *
 *   macOS port © 2026 GramzeSweatshop (rhiamom@mac.com)                   *
 *   GPL v2 or later. See Licences/GPL-LICENSE.txt                         *
 *                                                                         *
 *   Replaces Mootilda's SelectGraveyardDialog. Cancel keeps invalid       *
 *   urnstones (non-destructive); the destructive "remove all" path is not *
 *   exposed here.                                                         *
 ***************************************************************************/

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LotExpander;

namespace HoodChecker.App.Views;

public partial class GraveyardDialog : Window
{
    public GraveyardDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public GraveyardDialog(uint hoodId, List<R_DESC> lots) : this()
    {
        this.FindControl<TextBlock>("Prompt")!.Text =
            $"Choose graveyard for invalid Neighborhood ID {hoodId}:";
        this.FindControl<ListBox>("LotList")!.ItemsSource = lots;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("LotList")!;
        var lot = list.SelectedItem as R_DESC;
        if (lot == null) return; // require a selection for OK

        Close(new GraveyardChoice
        {
            SelectedLot = lot,
            UseForAll = this.FindControl<CheckBox>("UseForAll")!.IsChecked == true,
        });
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    /// <summary>Show modally and return the choice (null = keep invalid urnstones).</summary>
    public static Task<GraveyardChoice?> PromptAsync(Window owner, uint hoodId, List<R_DESC> lots)
        => new GraveyardDialog(hoodId, lots).ShowDialog<GraveyardChoice?>(owner);
}
