using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
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
using Opc.Ua.Configuration;
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

            // _opcUaAppConfig = CreateOpcUaApplicationConfiguration();

            string appNameForOpcUa = "DataLoggerApp"; // Of houd "DataLogger"
            string pkiRootForApp = AppDomain.CurrentDomain.BaseDirectory; // Certificaten komen in bin\Debug\CertificateStores
            string clientTraceLogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

            _opcUaAppConfig = OpcUaConfigurator.CreateClientConfiguration(
                applicationName: appNameForOpcUa,
                applicationUriIdentifier: Dns.GetHostName(), // Standaard host
                pkiBaseStorePath: pkiRootForApp,
                clientTraceLogDirectory: clientTraceLogDir,
                autoAcceptUntrustedCertificates: true, // Jouw instelling
                addAppCertToTrustedStore: true, // Jouw instelling
                createClientCertificateIfNeeded: true, // Was impliciet door CheckApplicationInstanceCertificates
                logger: Log.Logger.ForContext<App>() // Geef de Serilog logger mee
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
            services.AddSingleton<ILogger>(Log.Logger);
            services.AddSingleton<ILoggingHostService>(_theActualLoggingHostService);

            services.AddSingleton<IStatusService, StatusService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IDataLoggingService, DataLoggingService>();

            services.AddSingleton<ApplicationConfiguration>(_opcUaAppConfig);

            services.AddSingleton<LogViewModel>(serviceProvider => new LogViewModel(
                serviceProvider.GetRequiredService<ILoggingHostService>(),
                serviceProvider.GetRequiredService<ILogger>()
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

            services.AddSingleton<MainViewModel>(serviceProvider => new MainViewModel(
                serviceProvider.GetRequiredService<ILogger>(),
                serviceProvider.GetRequiredService<LogViewModel>(),
                serviceProvider.GetRequiredService<IStatusService>(),
                serviceProvider.GetRequiredService<ISettingsService>(),
                serviceProvider.GetRequiredService<Func<Action, SettingsViewModel>>(),
                serviceProvider.GetRequiredService<
                    Func<ModbusTcpConnectionConfig, IModbusService>
                >(),
                serviceProvider.GetRequiredService<Func<OpcUaConnectionConfig, IOpcUaService>>(),
                serviceProvider.GetRequiredService<IDataLoggingService>()
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

        // private ApplicationConfiguration CreateOpcUaApplicationConfiguration()
        // {
        //     var localLogger = Serilog.Log.Logger.ForContext<App>();
        //
        //     string applicationName = "DataLogger";
        //     string hostName = Dns.GetHostName();
        //
        //     string executableLocation = Assembly.GetExecutingAssembly().Location;
        //     string applicationDirectory = Path.GetDirectoryName(executableLocation);
        //
        //     string certStoresBasePath = Path.Combine(applicationDirectory, "CertificateStores");
        //     string ownCertStorePath = Path.Combine(certStoresBasePath, "own");
        //     string trustedPeersStorePath = Path.Combine(certStoresBasePath, "trusted", "certs");
        //     string trustedIssuerStorePath = Path.Combine(certStoresBasePath, "issuer", "certs");
        //     string rejectedCertStorePath = Path.Combine(certStoresBasePath, "rejected", "certs");
        //
        //     string trustedPeersCrlPath = Path.Combine(certStoresBasePath, "trusted", "crl");
        //     string trustedIssuerCrlPath = Path.Combine(certStoresBasePath, "issuer", "crl");
        //
        //     localLogger.Information(
        //         "OPC UA Client Cert Store Base Path: {Path}",
        //         certStoresBasePath
        //     );
        //
        //     var config = new ApplicationConfiguration
        //     {
        //         ApplicationName = applicationName,
        //         ApplicationUri = Utils.Format(@"urn:{0}:{1}", hostName, applicationName),
        //         ApplicationType = ApplicationType.Client,
        //         ProductUri = "urn:DataLogger:OpcUaClient",
        //         SecurityConfiguration = new SecurityConfiguration
        //         {
        //             ApplicationCertificate = new CertificateIdentifier
        //             {
        //                 StoreType = CertificateStoreType.Directory,
        //                 StorePath = ownCertStorePath,
        //                 SubjectName = Utils.Format(@"CN={0}, DC={1}", applicationName, hostName),
        //             },
        //             TrustedIssuerCertificates = new CertificateTrustList
        //             {
        //                 StoreType = CertificateStoreType.Directory,
        //                 StorePath = trustedIssuerStorePath,
        //             },
        //             TrustedPeerCertificates = new CertificateTrustList
        //             {
        //                 StoreType = CertificateStoreType.Directory,
        //                 StorePath = trustedPeersStorePath,
        //             },
        //             RejectedCertificateStore = new CertificateTrustList
        //             {
        //                 StoreType = CertificateStoreType.Directory,
        //                 StorePath = rejectedCertStorePath,
        //             },
        //             AutoAcceptUntrustedCertificates = true,
        //             AddAppCertToTrustedStore = true,
        //             RejectSHA1SignedCertificates = false,
        //             MinimumCertificateKeySize = 2048,
        //         },
        //         TransportConfigurations = new TransportConfigurationCollection(),
        //         TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
        //         ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
        //         TraceConfiguration = new TraceConfiguration
        //         {
        //             OutputFilePath = Path.Combine(
        //                 applicationDirectory,
        //                 "Logs",
        //                 $"{applicationName}.OpcUaClient.log.txt"
        //             ),
        //             DeleteOnLoad = true,
        //             TraceMasks =
        //                 Utils.TraceMasks.Error
        //                 | Utils.TraceMasks.Security
        //                 | Utils.TraceMasks.StackTrace,
        //         },
        //     };
        //     config.Validate(ApplicationType.Client).GetAwaiter().GetResult();
        //
        //     if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
        //     {
        //         config.CertificateValidator.CertificateValidation += (s, e) =>
        //         {
        //             e.Accept = (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted);
        //         };
        //     }
        //
        //     var application = new ApplicationInstance
        //     {
        //         ApplicationName = "DataLogger",
        //         ApplicationType = ApplicationType.Client,
        //         ApplicationConfiguration = config,
        //     };
        //
        //     application.CheckApplicationInstanceCertificates(false, 24).GetAwaiter().GetResult();
        //
        //     return config;
        // }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
