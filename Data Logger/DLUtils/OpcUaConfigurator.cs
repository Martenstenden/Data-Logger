using System;
using System.IO;
using System.Security.Cryptography.X509Certificates; // Nodig voor X509StoreType
using Opc.Ua;
using Opc.Ua.Configuration;
using Serilog; // Zorg dat deze using aanwezig is voor Serilog.ILogger

namespace Data_Logger.DLUtils // Zorg dat de namespace overeenkomt met jouw projectstructuur
{
    /// <summary>
    /// Utility klasse voor het configureren van een OPC UA client applicatie.
    /// Bevat methoden om een <see cref="ApplicationConfiguration"/> object te creëren en te initialiseren.
    /// </summary>
    public static class OpcUaConfigurator
    {
        /// <summary>
        /// Creëert en configureert een <see cref="ApplicationConfiguration"/> voor een OPC UA client.
        /// Deze methode stelt de applicatienaam, URI, security configuraties (inclusief certificaatpaden),
        /// transportquota's, clientinstellingen en trace-configuraties in.
        /// Het zorgt ook voor het aanmaken van de benodigde PKI (Public Key Infrastructure) mappen
        /// en kan een clientcertificaat genereren indien nodig.
        /// </summary>
        /// <param name="applicationName">De naam van de OPC UA client applicatie.</param>
        /// <param name="applicationUriIdentifier">
        /// Een unieke identifier voor de applicatie URI, vaak de hostnaam (bijv. Dns.GetHostName()).
        /// Wordt gebruikt om de ApplicationUri te formatteren (urn:{applicationUriIdentifier}:{applicationName}).
        /// </param>
        /// <param name="pkiBaseStorePath">
        /// Het basispad waar de "CertificateStores" map moet komen (of al bestaat).
        /// Bijvoorbeeld: <c>AppDomain.CurrentDomain.BaseDirectory</c>.
        /// </param>
        /// <param name="clientTraceLogDirectory">
        /// De map waar de OPC UA client trace logs worden opgeslagen.
        /// Indien null of leeg, wordt een "Logs" submap in <paramref name="pkiBaseStorePath"/> gebruikt.
        /// </param>
        /// <param name="autoAcceptUntrustedCertificates">
        /// Indien true, worden niet-vertrouwde servercertificaten automatisch geaccepteerd.
        /// Dit is handig voor ontwikkelingsscenario's maar wordt afgeraden voor productie. Default is true.
        /// </param>
        /// <param name="addAppCertToTrustedStore">
        /// Indien true, wordt het eigen applicatiecertificaat toegevoegd aan de trusted store. Default is true.
        /// </param>
        /// <param name="createClientCertificateIfNeeded">
        /// Indien true, wordt er een nieuw clientcertificaat aangemaakt en opgeslagen als er nog geen geldig certificaat bestaat. Default is true.
        /// </param>
        /// <param name="logger">Optionele Serilog logger instantie voor diagnostische output. Gebruik <see cref="Serilog.ILogger"/>.</param>
        /// <returns>Een geconfigureerd <see cref="ApplicationConfiguration"/> object.</returns>
        /// <exception cref="ArgumentNullException">Als <paramref name="applicationName"/>, <paramref name="applicationUriIdentifier"/>, of <paramref name="pkiBaseStorePath"/> null of leeg is.</exception>
        /// <exception cref="Exception">Kan exceptions gooien van de OPC UA SDK tijdens validatie of certificaatoperaties.</exception>
        [Obsolete("Obsolete")]
        public static ApplicationConfiguration CreateClientConfiguration(
            string applicationName,
            string applicationUriIdentifier,
            string pkiBaseStorePath,
            string clientTraceLogDirectory,
            bool autoAcceptUntrustedCertificates = true,
            bool addAppCertToTrustedStore = true,
            bool createClientCertificateIfNeeded = true,
            ILogger logger = null
        )
        {
            // Valideer basisparameters
            if (string.IsNullOrEmpty(applicationName))
                throw new ArgumentNullException(nameof(applicationName));
            if (string.IsNullOrEmpty(applicationUriIdentifier))
                throw new ArgumentNullException(nameof(applicationUriIdentifier));
            if (string.IsNullOrEmpty(pkiBaseStorePath))
                throw new ArgumentNullException(nameof(pkiBaseStorePath));

            // Definieer paden voor certificaat stores
            string certStoresRoot = Path.Combine(pkiBaseStorePath, "CertificateStores");
            string ownCertStorePath = Path.Combine(certStoresRoot, "own"); // Eigen certificaat (.pfx) en private key
            string trustedIssuerStorePath = Path.Combine(certStoresRoot, "issuer"); // Map voor certificaten van vertrouwde CA's
            string trustedPeersStorePath = Path.Combine(certStoresRoot, "trusted"); // Map voor certificaten van vertrouwde peer applicaties
            string rejectedCertStorePath = Path.Combine(certStoresRoot, "rejected"); // Map voor afgewezen certificaten

            logger?.Information(
                "OPC UA Client PKI Root Path wordt ingesteld op: {Path}",
                certStoresRoot
            );

            // Zorg ervoor dat de PKI mappen bestaan.
            try
            {
                Directory.CreateDirectory(ownCertStorePath);
                Directory.CreateDirectory(trustedIssuerStorePath);
                Directory.CreateDirectory(trustedPeersStorePath);
                Directory.CreateDirectory(rejectedCertStorePath);
                logger?.Debug(
                    "PKI directory structuur gecontroleerd/aangemaakt in {CertStoresRoot}",
                    certStoresRoot
                );
            }
            catch (Exception ex)
            {
                logger?.Error(
                    ex,
                    "Fout bij het aanmaken van PKI directory structuur in {CertStoresRoot}",
                    certStoresRoot
                );
            }

            var config = new ApplicationConfiguration
            {
                ApplicationName = applicationName,
                ApplicationUri = Utils.Format($"urn:{applicationUriIdentifier}:{applicationName}"),
                ApplicationType = ApplicationType.Client,
                ProductUri = $"urn:{applicationUriIdentifier}:DataLogger:OpcUaClientProduct", // Unieke ProductUri

                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory, // Certificaat wordt uit een map geladen
                        StorePath = ownCertStorePath, // Pad naar de map met het .pfx bestand van de client
                        SubjectName = Utils.Format(
                            $"CN={applicationName}, DC={applicationUriIdentifier}"
                        ), // Onderwerp van het client certificaat
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = trustedIssuerStorePath,
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = trustedPeersStorePath,
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = rejectedCertStorePath,
                    },
                    AutoAcceptUntrustedCertificates = autoAcceptUntrustedCertificates,
                    AddAppCertToTrustedStore = addAppCertToTrustedStore,
                    RejectSHA1SignedCertificates = true,
                    MinimumCertificateKeySize = 2048,
                    NonceLength = 32,
                },

                TransportConfigurations = new TransportConfigurationCollection(), // Standaard transport configuraties
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 120000, // 2 minuten voor operaties (bijv. Read/Write/Browse)
                    MaxStringLength = 1048576, // 1MB
                    MaxByteStringLength = 4194304, // 4MB
                    MaxArrayLength = 65535,
                    MaxMessageSize = 4194304, // 4MB
                    MaxBufferSize = 65535,
                    ChannelLifetime = 300000, // 5 minuten
                    SecurityTokenLifetime = 3600000, // 1 uur
                },
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000, // 1 minuut
                    MinSubscriptionLifetime = 10000, // 10 seconden
                },

                TraceConfiguration = new TraceConfiguration
                {
                    DeleteOnLoad = false, // Verwijder oude logbestanden niet bij het laden
                    TraceMasks =
                        Utils.TraceMasks.Error
                        | Utils.TraceMasks.Security
                        | Utils.TraceMasks.StackTrace
                        | Utils.TraceMasks.Information, // Uitgebreidere logging
                },
            };

            // Configureer output pad voor trace logging
            string effectiveClientTraceLogDirectory;
            if (!string.IsNullOrEmpty(clientTraceLogDirectory))
            {
                effectiveClientTraceLogDirectory = clientTraceLogDirectory;
            }
            else
            {
                effectiveClientTraceLogDirectory = Path.Combine(pkiBaseStorePath, "Logs");
                logger?.Debug(
                    "Geen specifieke clientTraceLogDirectory opgegeven, gebruikt default: {DefaultLogDir}",
                    effectiveClientTraceLogDirectory
                );
            }

            try
            {
                Directory.CreateDirectory(effectiveClientTraceLogDirectory);
                config.TraceConfiguration.OutputFilePath = Path.Combine(
                    effectiveClientTraceLogDirectory,
                    $"{applicationName}.OpcUaClient.Trace.log.txt"
                );
                logger?.Information(
                    "OPC UA SDK Trace logging geconfigureerd naar: {TraceLogPath}",
                    config.TraceConfiguration.OutputFilePath
                );
            }
            catch (Exception ex)
            {
                logger?.Error(
                    ex,
                    "Fout bij het configureren van OPC UA SDK Trace logging pad: {TraceLogPath}",
                    config.TraceConfiguration.OutputFilePath
                );
                config.TraceConfiguration = null; // Schakel trace logging uit bij fout
            }

            // Valideer de gecreëerde configuratie. Dit gooit een exception bij problemen.
            try
            {
                config.Validate(ApplicationType.Client).GetAwaiter().GetResult();
                logger?.Debug("ApplicationConfiguration succesvol gevalideerd.");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Fout tijdens valideren van ApplicationConfiguration.");
                throw; // Configuratie is niet geldig
            }

            // Handler voor server certificaat validatie
            // Deze code wordt uitgevoerd wanneer de client verbinding maakt en het server certificaat valideert.
            if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                logger?.Warning(
                    "AutoAcceptUntrustedCertificates is ingeschakeld. DIT IS ONVEILIG VOOR PRODUCTIE!"
                );
            }
            // De CertificateValidator wordt gebruikt door de Session objecten die met deze configuratie worden aangemaakt.
            config.CertificateValidator = new CertificateValidator();
            config.CertificateValidator.CertificateValidation += (validator, certArgs) =>
            {
                var validationLogger = logger?.ForContext("CertificateValidationEvent", true);
                validationLogger?.Information(
                    "Validating Server Certificate: Subject='{SubjectName}', Thumbprint='{Thumbprint}', Error='{Status}' (Code: {StatusCode})",
                    certArgs.Certificate.SubjectName?.Name,
                    certArgs.Certificate.Thumbprint,
                    certArgs.Error.ToString(), // Volledige ServiceResult als string
                    certArgs.Error.StatusCode
                );

                if (
                    certArgs.Error.StatusCode == StatusCodes.BadCertificateUntrusted
                    || certArgs.Error.StatusCode == StatusCodes.BadCertificateChainIncomplete
                )
                {
                    if (autoAcceptUntrustedCertificates) // Gebruik de parameter van de methode
                    {
                        validationLogger?.Warning(
                            "ONVEILIG: Auto-accepting server certificate (Subject='{SubjectName}', Error='{Status}') vanwege AutoAcceptUntrustedCertificates=true.",
                            certArgs.Certificate.SubjectName?.Name,
                            certArgs.Error
                        );
                        certArgs.Accept = true;
                    }
                    else
                    {
                        validationLogger?.Warning(
                            "Server certificate (Subject='{SubjectName}') is niet vertrouwd (Error='{Status}') en AutoAcceptUntrustedCertificates=false. Certificaat NIET geaccepteerd.",
                            certArgs.Certificate.SubjectName?.Name,
                            certArgs.Error
                        );
                        certArgs.Accept = false;
                    }
                }
                else if (StatusCode.IsBad(certArgs.Error.StatusCode))
                {
                    validationLogger?.Warning(
                        "Server certificate (Subject='{SubjectName}') validatie mislukt met een andere fout: {Status}. Certificaat NIET geaccepteerd.",
                        certArgs.Certificate.SubjectName?.Name,
                        certArgs.Error
                    );
                    certArgs.Accept = false;
                }
                else // StatusCodes.Good
                {
                    validationLogger?.Information(
                        "Server certificate (Subject='{SubjectName}') succesvol gevalideerd.",
                        certArgs.Certificate.SubjectName?.Name
                    );
                    certArgs.Accept = true; // Expliciet accepteren bij Good status
                }
            };

            // Controleer en maak eventueel een client applicatiecertificaat aan
            if (createClientCertificateIfNeeded)
            {
                logger?.Information(
                    "Controle op aanwezigheid van client applicatiecertificaat voor '{AppName}' in Store '{StorePath}' met Subject '{Subject}' (KeySize: {KeySize}, Lifetime: ca. 24 maanden default)",
                    config.ApplicationName,
                    config.SecurityConfiguration.ApplicationCertificate.StorePath,
                    config.SecurityConfiguration.ApplicationCertificate.SubjectName,
                    config.SecurityConfiguration.MinimumCertificateKeySize
                );

                // De SDK ApplicationInstance is nodig om het certificaat te checken/maken.
                // Deze instantie wordt hier alleen tijdelijk gebruikt voor certificaatbeheer.
                ApplicationInstance application = new ApplicationInstance
                {
                    ApplicationName = config.ApplicationName,
                    ApplicationType = config.ApplicationType,
                    ApplicationConfiguration = config, // Koppel de zojuist gecreëerde config
                };

                try
                {
                    bool certOK = application
                        .CheckApplicationInstanceCertificate(
                            silent: true,
                            minimumKeySize: config.SecurityConfiguration.MinimumCertificateKeySize,
                            lifeTimeInMonths: 24 // Standaard levensduur in maanden
                        )
                        .GetAwaiter()
                        .GetResult();

                    if (certOK)
                    {
                        logger?.Information(
                            "Client applicatiecertificaat is aanwezig en geldig voor '{AppName}'.",
                            config.ApplicationName
                        );
                    }
                    else
                    {
                        // CheckApplicationInstanceCertificate zou true moeten retourneren als het een nieuw certificaat succesvol heeft aangemaakt.
                        // Als het false retourneert, is er iets misgegaan bij het aanmaken of valideren.
                        logger?.Warning(
                            "Client applicatiecertificaat voor '{AppName}' was niet aanwezig/geldig of kon niet aangemaakt/gevalideerd worden. Controleer SDK logs.",
                            config.ApplicationName
                        );
                    }
                }
                catch (Exception ex)
                {
                    logger?.Error(
                        ex,
                        "Fout tijdens controleren/aanmaken van client applicatiecertificaat voor '{AppName}'.",
                        config.ApplicationName
                    );
                }
            }
            else
            {
                logger?.Information(
                    "Automatisch aanmaken/controleren van client certificaat is overgeslagen (createClientCertificateIfNeeded=false)."
                );
            }

            return config;
        }
    }
}
