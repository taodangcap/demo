using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShowCuePlayer.AudioEngine;
using ShowCuePlayer.Database;
using ShowCuePlayer.Database.Repositories;
using ShowCuePlayer.Services;
using ShowCuePlayer.ViewModels;
using ShowCuePlayer.Views;
using System.Windows;

namespace ShowCuePlayer;

/// <summary>
/// Application entry point. Configures DI host and launches MainWindow.
/// All services are registered here following the Composition Root pattern.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShowCuePlayer", "Logs");

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Global exception handling
        DispatcherUnhandledException += (s, args) =>
        {
            // Only recover from the known WebView shutdown race. Other UI
            // exceptions may leave the live-show state inconsistent.
            var recoverable = args.Exception is ObjectDisposedException disposed &&
                disposed.ObjectName?.Contains("WebView", StringComparison.OrdinalIgnoreCase) == true;
            LogCrash(args.Exception, showDialog: !recoverable);
            args.Handled = recoverable;
        };
        System.AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is System.Exception ex)
                LogCrash(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash(args.Exception, showDialog: false);
            args.SetObserved();
        };

        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddProvider(new FileLoggerProvider(LogDirectory));
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices(RegisterServices)
            .Build();

        await _host.StartAsync();

        // Initialize database schema
        var db = _host.Services.GetRequiredService<IDatabaseService>();
        await db.InitializeAsync();

        // Show main window
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void RegisterServices(HostBuilderContext ctx, IServiceCollection services)
    {
        // ── Database ──────────────────────────────────────────────
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();

        // ── Audio Engine ──────────────────────────────────────────
        services.AddSingleton<IAudioEngine, BassAudioEngine>();
        services.AddSingleton<IFadeEngine, FadeEngine>();
        services.AddSingleton<ICrossfadeEngine, CrossfadeEngine>();
        services.AddSingleton<IPreloadManager, PreloadManager>();

        // ── Application Services ──────────────────────────────────
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<IImportService, ImportService>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IVideoPlayerService, VideoPlayerService>();
        services.AddSingleton<IGitHubUpdateService, GitHubUpdateService>();

        // ── ViewModels ────────────────────────────────────────────
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();

        // ── Views ─────────────────────────────────────────────────
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsView>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                var audio = _host.Services.GetService<IAudioEngine>();
                audio?.Dispose();
                var hotkeys = _host.Services.GetService<IHotkeyService>();
                hotkeys?.Dispose();
            }
            catch (Exception ex) { LogCrash(ex, showDialog: false); }

            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private static void LogCrash(System.Exception ex, bool showDialog = true)
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
        string text = $"[{DateTimeOffset.Now:O}] Version={version}; OS={Environment.OSVersion}; " +
            $"Exception={ex.GetType().FullName}; Message={ex.Message}\n{ex}\n\n";
        try
        {
            var logDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShowCuePlayer", "Logs");
            System.IO.Directory.CreateDirectory(logDirectory);
            System.IO.File.AppendAllText(System.IO.Path.Combine(logDirectory, "crash.log"), text);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (showDialog)
            MessageBox.Show("Show Cue Player encountered an unexpected error. Details were saved in the Logs folder.",
                "Show Cue Player Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
