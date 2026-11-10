using Avalonia;
using Avalonia.Headless;
using FMOpenEditor.App.Services;

[assembly: AvaloniaTestApplication(typeof(FMOpenEditor.UiTests.TestAppBuilder))]

namespace FMOpenEditor.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // Tests never touch the user's real preferences, and run without decorative animation
        // so captured frames are deterministic.
        AppServices.Settings = AppSettings.Load(null);
        AppServices.Settings.TourHomeSeen = AppServices.Settings.TourWorkspaceSeen = true;
        Motion.Enabled = false;
        return AppBuilder.Configure<FMOpenEditor.App.App>()
            .UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
