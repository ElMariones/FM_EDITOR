using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.UiTests;

/// <summary>Settings → Currency: amounts everywhere follow the choice, and edits are still stored in pounds.</summary>
public class CurrencyTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Choosing_a_currency_converts_rows_and_the_editor_and_stores_pounds()
    {
        var settings = AppServices.Settings;
        try
        {
            var vm = new MainWindowViewModel();
            var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
            w.Show();
            vm.OpenDemoCommand.Execute(null);
            Pump();
            var players = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
            players.SetSelection([players.Rows[0]]);
            Pump();
            var entity = players.Rows[0].Entity;
            var pounds = (long)vm.Session!.Read(entity, "Value")!;
            Assert.True(pounds > 0);
            Assert.Equal(Money.Pound.Id, Money.Current.Id);
            Assert.StartsWith("£", players.Rows[0]["Value"]);

            // Euro from the quick three.
            var euro = vm.Settings.CommonCurrencies.Single(c => c.Currency.Id == Money.Euro.Id);
            vm.Settings.SelectedCommon = euro;
            Pump();
            Assert.Equal(Money.Euro.Id, settings.CurrencyId);
            Assert.StartsWith("€", players.Rows[0]["Value"]);
            Assert.Equal(entity, players.Editor!.Entity);
            var value = players.Editor.Groups.SelectMany(g => g.Editors).OfType<NumberEditorViewModel>().Single(e => e.Def.Key == "Value");
            Assert.Equal((decimal)Math.Round(pounds * euro.Currency.Rate), value.Value);
            // Nothing was edited by switching.
            Assert.Equal(0, vm.PendingCount);

            // Typing an amount in euros stores the pounds it is worth.
            value.Value = 2_320_000;
            Assert.Equal((long)Math.Round(2_320_000 / euro.Currency.Rate), vm.Session.Read(entity, "Value"));
            vm.DiscardCommand.Execute(null);
            w.Close();
        }
        finally
        {
            settings.CurrencyId = Money.Pound.Id;
            Money.Current = Money.Pound;
        }
    }
}
