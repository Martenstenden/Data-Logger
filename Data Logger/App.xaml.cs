using System;
using System.IO;
using System.Net;
using System.Windows;
using Data_Logger.DLUtils;
using Data_Logger.Models;
using Data_Logger.Services;
using Data_Logger.Services.Abstractions;
using Data_Logger.Services.Implementations;
using Data_Logger.ViewModels;
using Data_Logger.Views;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Serilog;

namespace Data_Logger
{
    public partial class App
    {
        public IServiceProvider ServiceProvider { get; private set; }

        private ILoggingHostService _theActualLoggingHostService;

        private ApplicationConfiguration _opcUaAppConfig;

        public App()
        {
            _theActualLoggingHostService = new LoggingHostService();

            string logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logsDirectory))
                Directory.CreateDirectory(logsDirectory);
            string logFilePath = Path.Combine(logsDirectory, "DataLoggerApp_.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console()
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.Sink(new UiLogSink(_theActualLoggingHostService))
                .CreateLogger();

            string appNameForOpcUa = "DataLoggerApp";
            string pkiRootForApp = AppDomain.CurrentDomain.BaseDirectory;
            string clientTraceLogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

            _opcUaAppConfig = OpcUaConfigurator.CreateClientConfiguration(
                applicationName: appNameForOpcUa,
                applicationUriIdentifier: Dns.GetHostName(),
                pkiBaseStorePath: pkiRootForApp,
                clientTraceLogDirectory: clientTraceLogDir,
                autoAcceptUntrustedCertificates: true,
                addAppCertToTrustedStore: true,
                createClientCertificateIfNeeded: true,
                logger: Log.Logger.ForContext<App>()
            );

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            ServiceProvider = serviceCollection.BuildServiceProvider();

            var loggerFromDI = ServiceProvider.GetService<ILogger>();
            loggerFromDI?.Information(
                "Applicatie initialisatie voltooid in App constructor (ServiceProvider is gebouwd)."
            );
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(Log.Logger);
            services.AddSingleton(_theActualLoggingHostService);

            services.AddSingleton<IStatusService, StatusService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IDataLoggingService, DataLoggingService>();

            services.AddSingleton(_opcUaAppConfig);

            services.AddSingleton(serviceProvider => new LogViewModel(
                serviceProvider.GetRequiredService<ILoggingHostService>()
            ));

            services.AddTransient<Func<ModbusTcpConnectionConfig, IModbusService>>(
                serviceProvider =>
                    config => new ModbusService(
                        serviceProvider.GetRequiredService<ILogger>(),
                        config
                    )
            );

            services.AddTransient<Func<OpcUaConnectionConfig, IOpcUaService>>(serviceProvider =>
                config => new OpcUaService(
                    serviceProvider.GetRequiredService<ILogger>(),
                    config,
                    serviceProvider.GetRequiredService<ApplicationConfiguration>()
                )
            );

            services.AddSingleton(serviceProvider => new MainViewModel(
                serviceProvider.GetRequiredService<LogViewModel>(),
                serviceProvider.GetRequiredService<ILogger>(),
                serviceProvider.GetRequiredService<IStatusService>(),
                serviceProvider.GetRequiredService<ISettingsService>(),
                serviceProvider.GetRequiredService<IDataLoggingService>(),
                serviceProvider.GetRequiredService<
                    Func<ModbusTcpConnectionConfig, IModbusService>
                >(),
                serviceProvider.GetRequiredService<Func<OpcUaConnectionConfig, IOpcUaService>>(),
                serviceProvider.GetRequiredService<Func<Action, SettingsViewModel>>()
            ));

            services.AddTransient<Func<Action, SettingsViewModel>>(serviceProvider =>
                closeAction => new SettingsViewModel(
                    serviceProvider.GetRequiredService<ISettingsService>(),
                    serviceProvider.GetRequiredService<IStatusService>(),
                    serviceProvider.GetRequiredService<ILogger>(),
                    closeAction
                )
            );
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            Log.Information("App.OnStartup: Begin van OnStartup.");

            var mainWindow = new MainWindow
            {
                DataContext = ServiceProvider.GetRequiredService<MainViewModel>(),
            };
            mainWindow.Show();

            Log.Debug("App.OnStartup: MainWindow getoond.");

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
