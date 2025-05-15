using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Data_Logger.DLUtils;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Data_Logger.Services.Implementations;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Opc.Ua;
using Opc.Ua.Configuration;
using Serilog;

namespace DataLogger.Tests.IntegrationTests
{
    [TestFixture]
    [Category("IntegrationTest")]
    public class OpcUaServiceIntegrationTests
    {
        private const string ReferenceServerImage =
            "ghcr.io/opcfoundation/uanetstandard/refserver:latest";

        private const string DefaultLocalOpcUaEndpoint =
            "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
        private string _currentOpcUaEndpoint;

        private ILogger _testLogger;
        private ApplicationConfiguration _clientAppConfigForTest;
        private static bool _isCiEnvironment;

        static OpcUaServiceIntegrationTests()
        {
            _isCiEnvironment = Environment.GetEnvironmentVariable("CI_ENVIRONMENT") == "true";
        }

        [OneTimeSetUp]
        public void GlobalSetup()
        {
            if (_isCiEnvironment)
            {
                TestContext.Progress.WriteLine(
                    "CI Environment: Docker container wordt verondersteld beheerd te worden door GitHub Actions Service of extern."
                );
                TestContext.Progress.WriteLine(
                    "DockerTestHelper.cs zal NIET worden gebruikt om de container te starten/stoppen."
                );
                _currentOpcUaEndpoint = Environment.GetEnvironmentVariable(
                    "OPCUA_TEST_SERVER_ENDPOINT"
                );
                if (string.IsNullOrEmpty(_currentOpcUaEndpoint))
                {
                    TestContext.Progress.WriteLine(
                        "CI Environment WAARSCHUWING: OPCUA_TEST_SERVER_ENDPOINT omgevingsvariabele is niet gezet. Gebruik fallback (waarschijnlijk localhost, wat kan falen)."
                    );
                    _currentOpcUaEndpoint = DefaultLocalOpcUaEndpoint;
                }
                TestContext.Progress.WriteLine(
                    $"CI Environment: OPC UA Endpoint voor tests is: {_currentOpcUaEndpoint}"
                );

                TestContext.Progress.WriteLine(
                    "CI Environment: Wacht 10 seconden voor de service container om potentieel op te starten..."
                );
                Thread.Sleep(10);
            }
            else
            {
                TestContext.Progress.WriteLine(
                    "Local Environment: Starten van OPC UA Reference Server via DockerTestHelper..."
                );
                _currentOpcUaEndpoint = DefaultLocalOpcUaEndpoint;

                string hostSideVolumeBase = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "ReferenceServerFiles"
                );
                string hostSideOpcFoundationDir = Path.Combine(
                    hostSideVolumeBase,
                    "OPC Foundation"
                );
                Directory.CreateDirectory(hostSideOpcFoundationDir);
                string containerSidePkiPath = "/root/.local/share/OPC Foundation";

                string hostPathForVolume = Path.GetFullPath(hostSideOpcFoundationDir)
                    .Replace('\\', '/');
                string volumeMapping = $"{hostPathForVolume}:{containerSidePkiPath}";
                string serverArgs = "-a -c -s";

                DockerTestHelper.DockerContainerName =
                    $"local-test-refserver-{Guid.NewGuid().ToString().Substring(0, 8)}";
                TestContext.Progress.WriteLine(
                    $"Lokaal: Proberen container te starten: {DockerTestHelper.DockerContainerName} met volume: {volumeMapping}"
                );

                if (
                    !DockerTestHelper.StartReferenceServerContainer(
                        ReferenceServerImage,
                        "62541:62541",
                        volumeMapping,
                        serverArgs
                    )
                )
                {
                    DockerTestHelper.RunDockerCommand(
                        $"logs {DockerTestHelper.DockerContainerName}",
                        TimeSpan.FromSeconds(10),
                        true
                    );
                    ClassicAssert.Fail(
                        $"Lokaal: Kon DockerTestHelper OPC UA server ({DockerTestHelper.DockerContainerName}) niet starten. Controleer Docker logs en setup."
                    );
                }
                TestContext.Progress.WriteLine(
                    $"Lokaal: Docker container {DockerTestHelper.DockerContainerName} gestart en 'Server started.' gedetecteerd."
                );
            }
        }

        [OneTimeTearDown]
        public void GlobalTeardown()
        {
            if (_isCiEnvironment)
            {
                TestContext.Progress.WriteLine(
                    "CI Environment: Docker container wordt beheerd door GitHub Actions Service. Overslaan DockerTestHelper.StopAndRemoveReferenceServerContainer()."
                );
            }
            else
            {
                TestContext.Progress.WriteLine(
                    $"Local Environment: Stoppen van OPC UA Reference Server via DockerTestHelper ({DockerTestHelper.DockerContainerName})..."
                );
                DockerTestHelper.StopAndRemoveReferenceServerContainer();
            }
            Log.CloseAndFlush();
        }

        [SetUp]
        public void PerTestSetup()
        {
            TestContext.Progress.WriteLine(
                $"PerTestSetup: Beginnen met aanmaken _clientAppConfigForTest voor test: {TestContext.CurrentContext.Test.Name}"
            );
            string pkiPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                $"TestClientPki_{TestContext.CurrentContext.Test.ID}"
            );
            string logPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "TestLogs",
                TestContext.CurrentContext.Test.ID
            );

            Directory.CreateDirectory(pkiPath);
            Directory.CreateDirectory(logPath);

            _clientAppConfigForTest = OpcUaConfigurator.CreateClientConfiguration(
                applicationName: $"TestClient_{Guid.NewGuid().ToString().Substring(0, 6)}",
                applicationUriIdentifier: "TestMachineForOPCUA",
                pkiBaseStorePath: pkiPath,
                clientTraceLogDirectory: logPath,
                autoAcceptUntrustedCertificates: true,
                addAppCertToTrustedStore: true,
                createClientCertificateIfNeeded: true,
                certificateKeySize: 2048,
                certificateLifetimeInMonths: 1
            );

            if (_clientAppConfigForTest == null)
            {
                ClassicAssert.Fail(
                    "PerTestSetup: _clientAppConfigForTest is NULL na aanroep CreateClientConfiguration!"
                );
            }
            TestContext.Progress.WriteLine(
                $"PerTestSetup: _clientAppConfigForTest succesvol aangemaakt. ApplicationName: {_clientAppConfigForTest.ApplicationName}"
            );
        }

        private IOpcUaService CreateOpcUaService(OpcUaConnectionConfig connectionConfig)
        {
            connectionConfig.EndpointUrl = _currentOpcUaEndpoint;

            ClassicAssert.IsNotNull(
                _clientAppConfigForTest,
                "ClientAppConfig is niet geïnitialiseerd in PerTestSetup."
            );

            return new OpcUaService(_testLogger, connectionConfig, _clientAppConfigForTest);
        }

        [Test, Order(0)]
        public void Prerequisite_DockerAndServerShouldBeRunning()
        {
            if (!_isCiEnvironment)
            {
                ClassicAssert.IsTrue(
                    DockerTestHelper.IsDockerRunning(),
                    "Docker service zou moeten draaien (lokaal)."
                );
                ClassicAssert.IsTrue(
                    DockerTestHelper.CheckContainerIsRunning(),
                    $"Docker container '{DockerTestHelper.DockerContainerName}' zou moeten draaien (lokaal)."
                );
            }
            else
            {
                TestContext.Progress.WriteLine(
                    "CI Environment: Overslaan Docker running check, server wordt extern beheerd/verondersteld."
                );
            }
            TestContext.Progress.WriteLine(
                "Voorwaarde: Docker en/of Reference Server container verondersteld draaiend."
            );
        }

        [Test, Order(1)]
        public async Task ConnectAndDisconnect_ToRefServer_ShouldSucceed()
        {
            var connectionConfig = new OpcUaConnectionConfig
            {
                ConnectionName = "TestRefServerConnectDisconnect",

                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                IsEnabled = true,
            };
            var opcUaService = CreateOpcUaService(connectionConfig);

            bool connected = false;
            try
            {
                TestContext.Progress.WriteLine(
                    $"Proberen te verbinden met {_currentOpcUaEndpoint}..."
                );
                connected = await opcUaService.ConnectAsync();
                TestContext.Progress.WriteLine($"Verbindingsresultaat: {connected}");
            }
            catch (Exception ex)
            {
                ClassicAssert.Fail(
                    $"ConnectAsync gooide een exception: {ex.Message} - {ex.StackTrace}"
                );
            }

            ClassicAssert.IsTrue(connected, "Verbinding met reference server zou moeten slagen.");
            ClassicAssert.IsTrue(
                opcUaService.IsConnected,
                "IsConnected property zou true moeten zijn na succesvolle verbinding."
            );

            await opcUaService.DisconnectAsync();
            ClassicAssert.IsFalse(
                opcUaService.IsConnected,
                "IsConnected property zou false moeten zijn na disconnect."
            );
            opcUaService.Dispose();
        }

        [Test, Order(2)]
        public async Task BrowseRoot_AfterConnect_ShouldReturnKnownNodes()
        {
            var connectionConfig = new OpcUaConnectionConfig
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
            };
            var opcUaService = CreateOpcUaService(connectionConfig);
            bool connected = await opcUaService.ConnectAsync();
            Assume.That(connected, "Voorwaarde: Moet verbonden zijn om te browsen.");

            ReferenceDescriptionCollection rootNodes = null;
            try
            {
                rootNodes = await opcUaService.BrowseRootAsync();
            }
            catch (Exception ex)
            {
                ClassicAssert.Fail(
                    $"BrowseRootAsync gooide een exception: {ex.Message} - {ex.StackTrace}"
                );
            }

            ClassicAssert.IsNotNull(rootNodes, "Root nodes collectie mag niet null zijn.");
            ClassicAssert.IsTrue(rootNodes.Count > 0, "Zou items moeten vinden in de root.");

            ClassicAssert.IsTrue(
                rootNodes.Any(n => n.BrowseName.Name == "Server"),
                "Het 'Server' object zou aanwezig moeten zijn in de root browse."
            );

            await opcUaService.DisconnectAsync();
            opcUaService.Dispose();
        }

        [Test, Order(3)]
        public async Task MonitorKnownSimulatedTag_ShouldReceiveDataUpdates()
        {
            string simulatedSineNodeId = "ns=5;s=Sinusoid";

            if (_isCiEnvironment && _currentOpcUaEndpoint.Contains("opcfoundation.github.io"))
            {
                simulatedSineNodeId = "ns=2;s=SimulatedDouble";
            }

            string tagName = "TestSimulatedTag";

            var tagConfig = new OpcUaTagConfig
            {
                NodeId = simulatedSineNodeId,
                TagName = tagName,
                SamplingInterval = 200,
                IsActive = true,
            };
            var connectionConfig = new OpcUaConnectionConfig
            {
                ConnectionName = "TestMonitorConnection",

                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TagsToMonitor = new ObservableCollection<OpcUaTagConfig> { tagConfig },
            };
            var opcUaService = CreateOpcUaService(connectionConfig);

            var receivedData = new List<LoggedTagValue>();
            var dataReceivedEvent = new ManualResetEventSlim(false);
            int updatesReceivedCount = 0;
            const int expectedUpdates = 3;

            opcUaService.TagsDataReceived += (sender, data) =>
            {
                foreach (var val in data)
                {
                    if (val.TagName == tagName)
                    {
                        TestContext.Progress.WriteLine(
                            $"Integratietest ({TestContext.CurrentContext.Test.Name}): Data ontvangen voor {val.TagName}: {val.Value} (Kwaliteit: {val.IsGoodQuality}, Tijdstempel: {val.Timestamp:O})"
                        );
                        receivedData.Add(val);
                        Interlocked.Increment(ref updatesReceivedCount);
                        if (updatesReceivedCount >= expectedUpdates)
                        {
                            dataReceivedEvent.Set();
                        }
                    }
                }
            };

            await opcUaService.ConnectAsync();
            Assume.That(opcUaService.IsConnected, "Moet verbonden zijn voor monitoring.");

            await opcUaService.StartMonitoringTagsAsync();
            TestContext.Progress.WriteLine(
                $"Integratietest ({TestContext.CurrentContext.Test.Name}): Monitoring gestart voor {tagName} (NodeId: {simulatedSineNodeId}), wacht op {expectedUpdates} updates..."
            );

            bool eventTriggered = dataReceivedEvent.Wait(TimeSpan.FromSeconds(20));

            await opcUaService.StopMonitoringTagsAsync();
            await opcUaService.DisconnectAsync();
            opcUaService.Dispose();

            ClassicAssert.IsTrue(
                eventTriggered,
                $"Niet genoeg data updates ({updatesReceivedCount}/{expectedUpdates}) ontvangen binnen de timeout voor tag {tagName}. Endpoint: {_currentOpcUaEndpoint}, NodeId: {simulatedSineNodeId}."
            );
            ClassicAssert.GreaterOrEqual(
                receivedData.Count,
                expectedUpdates,
                "Zou het verwachte aantal data updates moeten hebben ontvangen."
            );
            ClassicAssert.IsTrue(
                receivedData.All(d => d.IsGoodQuality),
                "Alle ontvangen data zou goede kwaliteit moeten hebben."
            );

            if (receivedData.Count >= 2)
            {
                var values = receivedData
                    .Select(rd =>
                    {
                        try
                        {
                            return Convert.ToDouble(rd.Value, CultureInfo.InvariantCulture);
                        }
                        catch
                        {
                            return double.NaN;
                        }
                    })
                    .Where(v => !double.IsNaN(v))
                    .ToList();

                if (values.Count >= 2)
                {
                    ClassicAssert.AreNotEqual(
                        values.First(),
                        values.Last(),
                        $"Waarden zouden moeten veranderen voor een simulatietag. Eerste: {values.First()}, Laatste: {values.Last()}. Endpoint: {_currentOpcUaEndpoint}, NodeId: {simulatedSineNodeId}"
                    );
                }
                else
                {
                    TestContext.Progress.WriteLine(
                        "Integratietest ({TestContext.CurrentContext.Test.Name}): Niet genoeg numerieke waarden ontvangen om variatie te controleren."
                    );
                }
            }
        }

        [Test, Explicit("Lokaal testen van DockerTestHelper")]
        [Category("LocalDocker")]
        public void Local_TestDockerHelper_StartAndStop()
        {
            if (_isCiEnvironment)
            {
                ClassicAssert.Ignore("Deze test is alleen voor lokaal gebruik met Docker.");
            }

            TestContext.Progress.WriteLine(
                "Lokaal: Testen van DockerTestHelper.StartReferenceServerContainer..."
            );
            _currentOpcUaEndpoint = DefaultLocalOpcUaEndpoint;

            string hostSideVolumeBase = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "ReferenceServerFiles_LocalTest"
            );
            string hostSideOpcFoundationDir = Path.Combine(hostSideVolumeBase, "OPC Foundation");
            Directory.CreateDirectory(hostSideOpcFoundationDir);
            string containerSidePkiPath = "/root/.local/share/OPC Foundation";
            string hostPathForVolume = Path.GetFullPath(hostSideOpcFoundationDir)
                .Replace('\\', '/');
            string volumeMapping = $"{hostPathForVolume}:{containerSidePkiPath}";
            string serverArgs = "-a -c -s";

            DockerTestHelper.DockerContainerName =
                $"explicit-local-test-{Guid.NewGuid().ToString().Substring(0, 6)}";

            bool started = DockerTestHelper.StartReferenceServerContainer(
                ReferenceServerImage,
                "62541:62541",
                volumeMapping,
                serverArgs
            );
            ClassicAssert.IsTrue(
                started,
                $"Kon container {DockerTestHelper.DockerContainerName} niet starten met DockerTestHelper."
            );
            TestContext.Progress.WriteLine(
                $"Lokaal: Container {DockerTestHelper.DockerContainerName} gestart."
            );

            bool portInUse = System
                .Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(p => p.Port == 62541);
            ClassicAssert.IsTrue(
                portInUse,
                "Poort 62541 zou in gebruik moeten zijn na starten server."
            );

            TestContext.Progress.WriteLine(
                $"Lokaal: Stoppen van container {DockerTestHelper.DockerContainerName}..."
            );
            DockerTestHelper.StopAndRemoveReferenceServerContainer();
            TestContext.Progress.WriteLine(
                $"Lokaal: Container {DockerTestHelper.DockerContainerName} gestopt en verwijderd."
            );

            Thread.Sleep(2000);
            portInUse = System
                .Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(p => p.Port == 62541);
            ClassicAssert.IsFalse(
                portInUse,
                "Poort 62541 zou weer vrij moeten zijn na stoppen server."
            );
        }
    }
}
