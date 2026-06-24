using Avalonia;
using Avalonia.ReactiveUI; // ← this is the correct namespace for v11
using System;
using Velopack;

namespace AvaloniaTestApp;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        
        VelopackApp.Build().Run();
        
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}