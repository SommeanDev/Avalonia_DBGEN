using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using AvaloniaTestApp.ViewModels;
using AvaloniaTestApp.Views;
using Velopack;
using Velopack.Sources;

namespace AvaloniaTestApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            _ = CheckForUpdatesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(
                "https://github.com/SommeanDev/Avalonia_DBGEN",
                null, // null = public repo, no token needed
                false // false = use latest stable release, not pre-release
            ));
            
            if (!mgr.IsInstalled) return; // skip when running in dev/debug

            var update = await mgr.CheckForUpdatesAsync();
            if (update == null) return; // already up to date

            // Download in background, then prompt user
            await mgr.DownloadUpdatesAsync(update);

            // TODO: show a nicer Avalonia dialog here instead of restarting silently
            mgr.ApplyUpdatesAndRestart(update);
        }
        catch
        {
            // Never crash the app over a failed update check
        }
    }
}