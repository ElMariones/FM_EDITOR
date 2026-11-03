using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(FMOpenEditor.UiTests.TestAppBuilder))]

namespace FMOpenEditor.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FMOpenEditor.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
