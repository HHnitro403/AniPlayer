using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Services;
using System;
using Aniplayer.Core.Constants;

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

            // HttpClient factory for MetadataService
            services.AddHttpClient("anilist", client =>
            {
                client.BaseAddress = new Uri(AppConstants.AniListEndpoint);
                client.Timeout = TimeSpan.FromSeconds(AppConstants.AniListTimeoutSeconds);
            });

            // Core services
            services.AddSingleton<IDatabaseService, DatabaseService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<ILibraryService, LibraryService>();
            services.AddSingleton<IWatchProgressService, WatchProgressService>();
            services.AddSingleton<IScannerService, ScannerService>();
            services.AddSingleton<IMetadataService, MetadataService>();
            services.AddSingleton<IFolderWatcherService, FolderWatcherService>();
            

            Services = services.BuildServiceProvider();

            // Initialize services and load settings before showing UI
            try
            {
                var db = Services.GetRequiredService<IDatabaseService>();
                await db.InitializeAsync();
                
                // Configure logger from saved settings
                var settings = Services.GetRequiredService<ISettingsService>();
                await ConfigureLoggerFromSettings(settings);

                Logger.Log("Database initialized successfully", LogRegion.General, force: true);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Database initialization failed", ex);
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async System.Threading.Tasks.Task ConfigureLoggerFromSettings(ISettingsService settings)
        {
            Logger.MasterLoggingEnabled = await settings.GetBoolAsync("logging_master_enabled", false);
            
            // Clear all regions first, then enable the ones that are set
            Logger.EnabledRegions = LogRegion.General; // General is always on

            if (await settings.GetBoolAsync("logging_region_scanner", false))
                Logger.EnabledRegions |= LogRegion.Scanner;
            
            if (await settings.GetBoolAsync("logging_region_parser", false))
                Logger.EnabledRegions |= LogRegion.Parser;
                
            // For UI and DB, default to ON if master is enabled, but only if they haven't been saved before
            // The GetBoolAsync with defaultValue handles this logic. If master is on, we want these to default to true.
            var uiDefault = Logger.MasterLoggingEnabled;
            var dbDefault = Logger.MasterLoggingEnabled;

            if (await settings.GetBoolAsync("logging_region_ui", uiDefault))
                Logger.EnabledRegions |= LogRegion.UI;
            
            if (await settings.GetBoolAsync("logging_region_db", dbDefault))
                Logger.EnabledRegions |= LogRegion.DB;
            
            if (await settings.GetBoolAsync("logging_region_progress", false))
                Logger.EnabledRegions |= LogRegion.Progress;
        }
    }
}
