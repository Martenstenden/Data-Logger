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
using NUnit.Framework.Legacy;
using Opc.Ua;
using Opc.Ua.Configuration;
using Serilog;

namespace DataLogger.Tests.IntegrationTests
{
    using NUnit.Framework;

    [TestFixture]
    [Category("IntegrationTest")]
    public class OpcUaServiceIntegrationTests
    {
        private const string ReferenceServerImage =
            "ghcr.io/opcfoundation/uanetstandard/refserver:latest";
        private const string DefaultLocalOpcUaEndpoint =
            "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
        
        private ILogger _testLogger;

        private ApplicationConfiguration _clientAppConfigForTest;

        [OneTimeSetUp]
public void GlobalSetup()
{
    if (Environment.GetEnvironmentVariable("CI_ENVIRONMENT") == "true")
    {
        TestContext.Progress.WriteLine("CI Environment: Docker container wordt beheerd door GitHub Actions Service. Overslaan DockerTestHelper.StartReferenceServerContainer().");
        // Wacht eventueel even om de service container de tijd te geven volledig op te starten.
        // Dit is soms nodig, hoewel GitHub Actions probeert te wachten tot poorten beschikbaar zijn.
        Thread.Sleep(15000); // 15 seconden, pas aan indien nodig of maak dit intelligenter.
    }
    else
    {
        TestContext.Progress.WriteLine("Local Environment: Starten van OPC UA Reference Server via DockerTestHelper...");
        // ... (je bestaande DockerTestHelper.StartReferenceServerContainer() logica) ...
        // Zorg ervoor dat DockerTestHelper.DockerContainerName hier uniek is per testrun als je lokaal draait.
        // En dat TestOpcUaEndpoint hier ook lokaal correct is.
        string hostSideVolumeBase = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ReferenceServerFiles");
        string hostSideOpcFoundationDir = Path.Combine(hostSideVolumeBase, "OPC Foundation");
        Directory.CreateDirectory(hostSideOpcFoundationDir);
        string containerSidePkiPath = "/root/.local/share/OPC Foundation";
        string hostPathForVolume = Path.GetFullPath(hostSideOpcFoundationDir).Replace('\\', '/');
        string volumeMapping = $"{hostPathForVolume}:{containerSidePkiPath}";
        string serverArgs = "-a -c -s"; // Je bestaande argumenten

        // De image naam en poort mapping zijn hier hardcoded, overweeg deze ook configureerbaar te maken.
        if (!DockerTestHelper.StartReferenceServerContainer(
                "ghcr.io/opcfoundation/uanetstandard/refserver:latest", // Image naam
                "62541:62541",      // Port mapping
                volumeMapping,
                serverArgs))
        {
            Assert.Fail("Kon DockerTestHelper OPC UA server niet starten voor lokale testrun.");
        }
    }
}

        [Test]
        public void SimplestPossibleTest()
        {
            ClassicAssert.IsTrue(true, "This basic test should always pass and run.");
            TestContext.Progress.WriteLine("SimplestPossibleTest executed!");
        }

        [OneTimeTearDown]
        public void GlobalTeardown()
        {
            if (Environment.GetEnvironmentVariable("CI_ENVIRONMENT") == "true")
            {
                TestContext.Progress.WriteLine("CI Environment: Docker container wordt beheerd door GitHub Actions Service. Overslaan DockerTestHelper.StopAndRemoveReferenceServerContainer().");
            }
            else
            {
                TestContext.Progress.WriteLine("Local Environment: Stoppen van OPC UA Reference Server via DockerTestHelper...");
                DockerTestHelper.StopAndRemoveReferenceServerContainer();
            }
        }

        [SetUp]
        public void PerTestSetup()
        {
            var mockLoggerImpl = new Mock<ILogger>();
            mockLoggerImpl
                .Setup(l => l.ForContext(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<bool>()))
                .Returns(mockLoggerImpl.Object);
            mockLoggerImpl.Setup(l => l.ForContext<It.IsAnyType>()).Returns(mockLoggerImpl.Object);
            _testLogger = mockLoggerImpl.Object;

            TestContext.Progress.WriteLine(
                "PerTestSetup: Beginnen met aanmaken _clientAppConfigForTest."
            );

            _clientAppConfigForTest = OpcUaConfigurator.CreateClientConfiguration(
                applicationName: $"TestClient_{Guid.NewGuid().ToString().Substring(0, 6)}",
                applicationUriIdentifier: "TestMachineForOPCUA",
                pkiBaseStorePath: Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "TestClientPki_" + Guid.NewGuid().ToString().Substring(0, 6)
                ),
                clientTraceLogDirectory: Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "TestLogs",
                    Guid.NewGuid().ToString().Substring(0, 6)
                ),
                autoAcceptUntrustedCertificates: true,
                addAppCertToTrustedStore: true,
                createClientCertificateIfNeeded: true,
                certificateKeySize: 2048,
                certificateLifetimeInMonths: 1,
                logger: _testLogger
            );

            if (_clientAppConfigForTest == null)
            {
                TestContext.Progress.WriteLine(
                    "PerTestSetup: _clientAppConfigForTest is NULL na aanroep CreateClientConfiguration!"
                );
            }
            else
            {
                TestContext.Progress.WriteLine(
                    $"PerTestSetup: _clientAppConfigForTest succesvol aangemaakt. ApplicationName: {_clientAppConfigForTest.ApplicationName}"
                );
            }
        }

        private IOpcUaService CreateOpcUaService(OpcUaConnectionConfig connectionConfig)
        {
            ClassicAssert.IsNotNull(
                _clientAppConfigForTest,
                "ClientAppConfig is niet geïnitialiseerd in PerTestSetup."
            );
            return new OpcUaService(_testLogger, connectionConfig, _clientAppConfigForTest);
        }

        [Test, Order(0)]
        public void Prerequisite_DockerAndServerShouldBeRunning()
        {
            ClassicAssert.IsTrue(
                DockerTestHelper.IsDockerRunning(),
                "Docker service zou moeten draaien."
            );
            ClassicAssert.IsTrue(
                DockerTestHelper.CheckContainerIsRunning(),
                $"Docker container '{DockerTestHelper.DockerContainerName}' zou moeten draaien."
            );
            TestContext.Progress.WriteLine(
                "Voorwaarde: Docker en Reference Server container draaien."
            );
        }
        
        private string GetTestOpcUaEndpoint()
        {
            string ciEndpoint = Environment.GetEnvironmentVariable("OPCUA_TEST_SERVER_ENDPOINT");
            if (!string.IsNullOrEmpty(ciEndpoint) && Environment.GetEnvironmentVariable("CI_ENVIRONMENT") == "true")
            {
                TestContext.Progress.WriteLine($"CI Environment: Gebruik OPC UA endpoint van env var: {ciEndpoint}");
                return ciEndpoint;
            }
            TestContext.Progress.WriteLine($"Local Environment: Gebruik hardcoded OPC UA endpoint: {DefaultLocalOpcUaEndpoint}");
            return DefaultLocalOpcUaEndpoint; // Je bestaande lokale endpoint URL
        }


        [Test, Order(1)]
        public async Task ConnectAndDisconnect_ToRefServer_ShouldSucceed()
        {
            Assume.That(
                DockerTestHelper.IsDockerRunning() && DockerTestHelper.CheckContainerIsRunning(),
                "Referentie server (Docker) moet draaien voor deze test."
            );

            var connectionConfig = new OpcUaConnectionConfig
            {
                ConnectionName = "TestRefServerConnectDisconnect",
                EndpointUrl = GetTestOpcUaEndpoint(),
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                IsEnabled = true,
            };
            var opcUaService = CreateOpcUaService(connectionConfig);

            bool connected = false;
            try
            {
                TestContext.Progress.WriteLine($"Proberen te verbinden met {GetTestOpcUaEndpoint()}...");
                connected = await opcUaService.ConnectAsync();
                TestContext.Progress.WriteLine($"Verbindingsresultaat: {connected}");
            }
            catch (Exception ex)
            {
                Assert.Fail($"ConnectAsync gooide een exception: {ex}");
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
            Assume.That(
                DockerTestHelper.IsDockerRunning() && DockerTestHelper.CheckContainerIsRunning(),
                "Referentie server (Docker) moet draaien voor deze test."
            );

            var connectionConfig = new OpcUaConnectionConfig
            {
                EndpointUrl = GetTestOpcUaEndpoint(),
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
                Assert.Fail($"BrowseRootAsync gooide een exception: {ex}");
            }

            ClassicAssert.IsNotNull(rootNodes, "Root nodes collectie mag niet null zijn.");
            ClassicAssert.IsTrue(rootNodes.Count > 0, "Zou items moeten vinden in de root.");

            ClassicAssert.IsTrue(
                rootNodes.Any(n => n.BrowseName.Name == "Server"),
                "De 'Objects' folder zou aanwezig moeten zijn."
            );
            // ClassicAssert.IsTrue(
            //     rootNodes.Any(n => n.BrowseName.Name == "Types"),
            //     "De 'Types' folder zou aanwezig moeten zijn."
            // );
            // ClassicAssert.IsTrue(
            //     rootNodes.Any(n => n.BrowseName.Name == "Views"),
            //     "De 'Views' folder zou aanwezig moeten zijn."
            // );

            await opcUaService.DisconnectAsync();
            opcUaService.Dispose();
        }

        [Test, Order(3)]
        public async Task MonitorKnownSimulatedTag_ShouldReceiveDataUpdates()
        {
            Assume.That(
                DockerTestHelper.IsDockerRunning() && DockerTestHelper.CheckContainerIsRunning(),
                "Referentie server (Docker) moet draaien voor deze test."
            );

            string simulatedSineNodeId = "ns=5;i=1242";
            string tagName = "Pipe1001Output";

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
                EndpointUrl = GetTestOpcUaEndpoint(),
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TagsToMonitor = new ObservableCollection<OpcUaTagConfig> { tagConfig },
            };
            var opcUaService = CreateOpcUaService(connectionConfig);

            var receivedData = new List<LoggedTagValue>();
            var dataReceivedEvent = new ManualResetEventSlim(false);
            int updatesReceivedCount = 0;
            const int expectedUpdates = 5;

            opcUaService.TagsDataReceived += (sender, data) =>
            {
                foreach (var val in data)
                {
                    if (val.TagName == tagName)
                    {
                        TestContext.Progress.WriteLine(
                            $"Integratietest: Data ontvangen voor {val.TagName}: {val.Value} (Kwaliteit: {val.IsGoodQuality})"
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
                $"Integratietest: Monitoring gestart voor {tagName}, wacht op {expectedUpdates} updates..."
            );

            bool eventTriggered = dataReceivedEvent.Wait(TimeSpan.FromSeconds(15));

            await opcUaService.StopMonitoringTagsAsync();
            await opcUaService.DisconnectAsync();
            opcUaService.Dispose();

            ClassicAssert.IsTrue(
                eventTriggered,
                $"Niet genoeg data updates ({updatesReceivedCount}/{expectedUpdates}) ontvangen binnen de timeout voor tag {tagName}."
            );
            ClassicAssert.GreaterOrEqual(
                receivedData.Count,
                expectedUpdates,
                "Zou meerdere data updates moeten hebben ontvangen."
            );
            ClassicAssert.IsTrue(
                receivedData.All(d => d.IsGoodQuality),
                "Alle ontvangen data zou goede kwaliteit moeten hebben."
            );

            if (receivedData.Count >= 2)
            {
                var values = receivedData
                    .Select(rd => Convert.ToDouble(rd.Value, CultureInfo.InvariantCulture))
                    .ToList();
                ClassicAssert.AreNotEqual(
                    values.First(),
                    values.Last(),
                    $"Waarden zouden moeten veranderen voor een simulatietag zoals Sine1. Eerste: {values.First()}, Laatste: {values.Last()}"
                );
            }
        }
    }
}
