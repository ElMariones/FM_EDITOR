using Avalonia.Controls;
using FMOpenEditor.App.Services;

namespace FMOpenEditor.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // The feature cards follow the hero in, one after another.
        AttachedToVisualTree += (_, _) =>
        {
            Motion.Enter(HeroArt, offsetY: 24, milliseconds: 520);
            var delay = 120;
            foreach (var card in Features.Children)
            {
                Motion.Enter(card, offsetY: 18, milliseconds: 380, delay: delay);
                delay += 70;
            }
        };
    }
}
