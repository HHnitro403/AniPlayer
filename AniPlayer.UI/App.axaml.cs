using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Services;
using System;

namespace AniPlayer.UI
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            // Build DI container
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddConsole());

            // Core services
            services.AddSingleton<IDatabaseService, DatabaseService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<ILibraryService, LibraryService>();
            services.AddSingleton<IWatchProgressService, WatchProgressService>();
            services.AddSingleton<IScannerService, ScannerService>();
            services.AddSingleton<IMetadataService, MetadataService>();
            services.AddSingleton<IFolderWatcherService, FolderWatcherService>();
            services.AddSingleton<IPlayerService, PlayerService>();

            Services = services.BuildServiceProvider();

            // Initialize database (creates file + tables) before showing UI
            try
            {
                var db = Services.GetRequiredService<IDatabaseService>();
                await db.InitializeAsync();
                Logger.Log("Database initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Log($"Database initialization failed: {ex.Message}");
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
