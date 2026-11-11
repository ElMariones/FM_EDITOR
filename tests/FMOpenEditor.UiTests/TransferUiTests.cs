using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.UiTests;

/// <summary>The Transfer button on a player opens the window, and a transfer from it is staged.</summary>
public class TransferUiTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Transfer_button_opens_the_window_and_moves_the_player()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
        w.Show();
        vm.OpenDemoCommand.Execute(null);
        Pump();

        vm.SelectedNav = vm.Nav.First(n => n.Title == PropertyCatalog.Plural(EntityKind.Player));
        Pump();
        var page = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        page.SetSelection(page.Rows.Take(1));
        Pump();
        Assert.True(page.CanTransfer);

        var button = w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TransferButton");
        Assert.True(button.IsEffectivelyVisible);

        TransferWindow? opened = null;
        Window.WindowOpenedEvent.AddClassHandler<TransferWindow>((t, _) => opened = t);
        button.Command!.Execute(null);
        Pump();
        Assert.NotNull(opened);
        var transfer = Assert.IsType<TransferViewModel>(opened!.DataContext);
        var session = vm.Session!;
        var player = transfer.Player;
        var from = session.Read(player, "Contract.Club");

        // Search, pick a club that isn't the current one, set a fee.
        var target = session.GetAll(EntityKind.Club).First(c => !Equals(c.Id, from));
        var name = session.NameOf(EntityKind.Club, target.Id);
        Control Named(string n) => opened.GetVisualDescendants().OfType<Control>().Single(c => c.Name == n);
        Assert.True(Named("PickPanel").IsEffectivelyVisible);
        Assert.False(Named("ChosenCard").IsEffectivelyVisible);

        transfer.ClubSearch = name;
        Pump();
        transfer.ChooseCommand.Execute(transfer.Results.First(r => r.Club == target));
        Pump();

        // Choosing folds the search away and shows the club as a card.
        Assert.Equal(target, transfer.SelectedClub!.Club);
        Assert.False(Named("PickPanel").IsEffectivelyVisible);
        Assert.True(Named("ChosenCard").IsEffectivelyVisible);

        // Clicking the card searches again; backing out keeps the choice.
        ((Button)Named("ChosenCard")).Command!.Execute(null);
        Pump();
        Assert.True(Named("PickPanel").IsEffectivelyVisible);
        Assert.True(transfer.ShowKeepChoice);
        transfer.CancelPickingCommand.Execute(null);
        Pump();
        Assert.True(Named("ChosenCard").IsEffectivelyVisible);
        Assert.Equal(target, transfer.SelectedClub!.Club);

        transfer.Fee = 3_000_000;
        Pump();
        Assert.False(transfer.HasProblem);
        Assert.Contains(name, transfer.Summary);
        Assert.True(transfer.ConfirmCommand.CanExecute(null));
        var confirm = opened.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmButton");
        Assert.True(confirm.IsEffectivelyEnabled);
        Pump();

        transfer.ConfirmCommand.Execute(null);
        Pump();
        Assert.False(opened.IsVisible);
        Assert.Equal(target.Id, session.Read(player, "Contract.Club"));
        Assert.True(session.Changes.Count > 0);
        w.Close();
    }
}
