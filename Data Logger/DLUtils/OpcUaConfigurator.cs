using System.IO;
using Opc.Ua;
using Opc.Ua.Configuration;
using Serilog;

namespace Data_Logger.DLUtils;

public static class OpcUaConfigurator
    {
        public static ApplicationConfiguration CreateClientConfiguration(
            string applicationName,
            string applicationUriIdentifier, // Bijv. Dns.GetHostName() of een test-specifieke string
            string pkiBaseStorePath,         // Het basispad waar de "CertificateStores" map moet komen
            string clientTraceLogDirectory,  // Map voor de OPC UA client trace log
            bool autoAcceptUntrustedCertificates = true,
            bool addAppCertToTrustedStore = true,
            bool createClientCertificateIfNeeded = true,
            ushort certificateKeySize = 2048,
            ushort certificateLifetimeInMonths = 24,
            ILogger logger = null) // Optionele Serilog logger
        {
            // logger = logger ?? Serilog.Log.Logger.None; // Gebruik een NOP logger als er geen is meegegeven

            // --- Paden voor certificaat stores ---
            // De structuur komt overeen met wat je in App.xaml.cs had, maar nu relatief aan pkiBaseStorePath
            string certStoresRoot = Path.Combine(pkiBaseStorePath, "CertificateStores");
            string ownCertStorePath = Path.Combine(certStoresRoot, "own"); // Oude code gebruikte ...own\certs, OPC SDK zoekt vaak in ...own
            string trustedIssuerStorePath = Path.Combine(certStoresRoot, "issuer", "certs");
            string trustedPeersStorePath = Path.Combine(certStoresRoot, "trusted", "certs");
            string rejectedCertStorePath = Path.Combine(certStoresRoot, "rejected", "certs");
            // CRLs (indien nodig)
            // string trustedPeersCrlPath = Path.Combine(certStoresRoot, "trusted", "crl");
            // string trustedIssuerCrlPath = Path.Combine(certStoresRoot, "issuer", "crl");

            // Zorg dat de PKI mappen bestaan (cruciaal voor zowel app als tests)
            // De SDK maakt deze vaak zelf aan, maar expliciet kan geen kwaad.
            Directory.CreateDirectory(ownCertStorePath); // Pad naar de certs zelf
            Directory.CreateDirectory(Path.GetDirectoryName(trustedIssuerStorePath)); // Pad naar "issuer"
            Directory.CreateDirectory(Path.GetDirectoryName(trustedPeersStorePath));  // Pad naar "trusted"
            Directory.CreateDirectory(Path.GetDirectoryName(rejectedCertStorePath)); // Pad naar "rejected"

            logger.Information("OPC UA Client PKI Root Path: {Path}", certStoresRoot);

            var config = new ApplicationConfiguration
            {
                ApplicationName = applicationName,
                ApplicationUri = Utils.Format($"urn:{applicationUriIdentifier}:{applicationName}"),
                ApplicationType = ApplicationType.Client,
                ProductUri = $"urn:{applicationUriIdentifier}:DataLogger:OpcUaClient", // Unieker maken
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = ownCertStorePath, // Directory waar het certificaat .pfx bestand staat/komt
                        SubjectName = Utils.Format($"CN={applicationName}, DC={applicationUriIdentifier}"),
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = trustedIssuerStorePath, // Pad naar de map met vertrouwde CA certs
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = trustedPeersStorePath,  // Pad naar de map met vertrouwde server/client certs
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = rejectedCertStorePath, // Pad naar de map met afgewezen certs
                    },
                    AutoAcceptUntrustedCertificates = autoAcceptUntrustedCertificates,
                    AddAppCertToTrustedStore = addAppCertToTrustedStore,
                    RejectSHA1SignedCertificates = false, // Jouw setting was false
                    MinimumCertificateKeySize = 2048      // Jouw setting
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
                TraceConfiguration = new TraceConfiguration
                {
                    DeleteOnLoad = true, // Standaard setting
                    // TraceMasks uit jouw App.xaml.cs (Error | Security | StackTrace)
                    TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security | Utils.TraceMasks.StackTrace,
                }
            };

            if (!string.IsNullOrEmpty(clientTraceLogDirectory))
            {
                Directory.CreateDirectory(clientTraceLogDirectory);
                config.TraceConfiguration.OutputFilePath = Path.Combine(clientTraceLogDirectory, $"{applicationName}.OpcUaClient.Trace.log.txt");
            }
            else // Fallback
            {
                string defaultLogDir = Path.Combine(pkiBaseStorePath, "Logs");
                Directory.CreateDirectory(defaultLogDir);
                config.TraceConfiguration.OutputFilePath = Path.Combine(defaultLogDir, $"{applicationName}.OpcUaClient.Trace.log.txt");
            }

            // Valideer de configuratie (gooit exception bij problemen)
            config.Validate(ApplicationType.Client).GetAwaiter().GetResult();

            // Handler voor server certificaat validatie
            if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateValidator.CertificateValidation += (validator, certArgs) =>
                {
                    logger.Debug("Validating Server Certificate: Subject='{SubjectName}', Error='{Status}'",
                        certArgs.Certificate.SubjectName.Name, certArgs.Error);

                    if (certArgs.Error.StatusCode == StatusCodes.BadCertificateUntrusted || certArgs.Error.StatusCode == StatusCodes.BadCertificateChainIncomplete)
                    {
                        logger.Information("Auto-accepting untrusted certificate: Subject='{SubjectName}', Thumbprint='{Thumbprint}'", certArgs.Certificate.SubjectName.Name, certArgs.Certificate.Thumbprint);
                        certArgs.Accept = true;
                    }
                    else if (certArgs.Error.StatusCode != StatusCodes.Good)
                    {
                        logger.Warning("Certificate Error for Subject='{SubjectName}': {Status}", certArgs.Certificate.SubjectName.Name, certArgs.Error);
                    }
                };
            }

            if (createClientCertificateIfNeeded)
            {
                logger.Information("Checking application instance certificate for '{AppName}' in Store '{StorePath}' with Subject '{Subject}' (KeySize: {KeySize}, Lifetime: {LifetimeMonths}m)...",
                    config.ApplicationName,
                    config.SecurityConfiguration.ApplicationCertificate.StorePath,
                    config.SecurityConfiguration.ApplicationCertificate.SubjectName,
                    config.SecurityConfiguration.MinimumCertificateKeySize, // Haal uit config
                    certificateLifetimeInMonths);

                // *** GEBRUIK DE "OUDE" IMPLEMENTATIE ZOALS IN App.xaml.cs ***
                var application = new ApplicationInstance(config) // Geef de config direct mee aan de constructor
                {
                    ApplicationName = config.ApplicationName, // Redundant als het via constructor gaat, maar voor de zekerheid
                    ApplicationType = ApplicationType.Client
                    // CertificatePasswordProvider kan hier ook worden ingesteld als je die had
                };

                // De false hier is voor 'silent', de tweede parameter is 'defaultLifeTimeInMonths'
                // De minimumKeySize wordt intern gehaald uit config.SecurityConfiguration.MinimumCertificateKeySize
                application.CheckApplicationInstanceCertificates(silent: true)
                    .GetAwaiter().GetResult();

                logger.Information("Application instance certificate checked/ensured for '{AppName}'.", config.ApplicationName);
            }

            return config;
        }
    }