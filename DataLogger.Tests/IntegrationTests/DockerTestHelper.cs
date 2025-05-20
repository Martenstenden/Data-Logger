using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DataLogger.Tests.IntegrationTests
{
    public static class DockerTestHelper
    {
        public static string DockerContainerName { get; set; } = "test-refserver-instance";

        public static bool IsDockerRunning()
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "docker";
                    process.StartInfo.Arguments = "ps";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;

                    TestContext.Progress.WriteLine(
                        "DockerTestHelper.IsDockerRunning: Probeert 'docker ps' uit te voeren..."
                    );
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);

                    TestContext.Progress.WriteLine(
                        $"DockerTestHelper.IsDockerRunning: 'docker ps' ExitCode={process.ExitCode}"
                    );
                    if (!string.IsNullOrWhiteSpace(output))
                        TestContext.Progress.WriteLine(
                            $"DockerTestHelper.IsDockerRunning: Output='{output.Trim()}'"
                        );
                    if (!string.IsNullOrWhiteSpace(error))
                        TestContext.Progress.WriteLine(
                            $"DockerTestHelper.IsDockerRunning: Error='{error.Trim()}'"
                        );

                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine(
                    $"DockerTestHelper.IsDockerRunning: Exception bij uitvoeren 'docker ps': {ex.Message} - {ex.StackTrace}"
                );
                return false;
            }
        }

        public static bool StartReferenceServerContainer(
            string imageName,
            string portMapping,
            string volumeMapping,
            string additionalArgs
        )
        {
            TestContext.Progress.WriteLine(
                $"Docker: Probeert container '{DockerContainerName}' te starten van image '{imageName}'..."
            );
            RunDockerCommand(
                $"stop {DockerContainerName}",
                TimeSpan.FromSeconds(10),
                ignoreErrors: true
            );
            RunDockerCommand(
                $"rm {DockerContainerName}",
                TimeSpan.FromSeconds(10),
                ignoreErrors: true
            );

            string volumeArg = string.IsNullOrEmpty(volumeMapping) ? "" : $"-v \"{volumeMapping}\"";
            string portArg = string.IsNullOrEmpty(portMapping) ? "" : $"-p {portMapping}";

            string arguments =
                $"run -d --name {DockerContainerName} {portArg} {volumeArg} {imageName} {additionalArgs}";
            bool success = RunDockerCommand(arguments, TimeSpan.FromSeconds(15));

            if (success)
            {
                TestContext.Progress.WriteLine(
                    $"Docker: Container '{DockerContainerName}' gestart. Wachten tot server 'Server started.' logt..."
                );
                return WaitForServerToLogStarted(TimeSpan.FromSeconds(360));
            }
            TestContext.Progress.WriteLine(
                $"Docker: Starten van container '{DockerContainerName}' is MISLUKT initieel (RunDockerCommand gaf false)."
            );
            return false;
        }

        private static bool WaitForServerToLogStarted(TimeSpan timeout)
        {
            DateTime startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                if (!CheckContainerIsRunning())
                {
                    TestContext.Progress.WriteLine(
                        "Docker: Container draait niet meer tijdens wachten op 'Server started.' log."
                    );
                    return false;
                }

                using (var process = new Process())
                {
                    process.StartInfo.FileName = "docker";
                    process.StartInfo.Arguments = $"logs {DockerContainerName}";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;

                    process.Start();
                    string logs = process.StandardOutput.ReadToEnd();
                    string errLogs = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(errLogs))
                    {
                        TestContext.Progress.WriteLine(
                            $"Docker logs (stderr) voor '{DockerContainerName}': {errLogs.Trim()}"
                        );
                    }
                    if (!string.IsNullOrWhiteSpace(logs))
                    {
                        TestContext.Progress.WriteLine(
                            $"Docker logs (stdout) voor '{DockerContainerName}' (laatste check): {logs.Trim()}"
                        );

                        if (
                            Regex.IsMatch(
                                logs,
                                @"Server\s+started\.(?:\s+Press\s+Ctrl-C\s+to\s+exit\.\.\.)?",
                                RegexOptions.IgnoreCase
                            )
                        )
                        {
                            TestContext.Progress.WriteLine(
                                "Docker: 'Server started.' bericht gevonden in container logs."
                            );
                            return true;
                        }
                    }
                }
                Thread.Sleep(2000);
            }

            TestContext.Progress.WriteLine(
                $"Docker: Timeout ({timeout.TotalSeconds}s) bereikt tijdens wachten op 'Server started.' bericht in logs voor container '{DockerContainerName}'."
            );
            return false;
        }

        public static bool CheckContainerIsRunning()
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "docker";
                    process.StartInfo.Arguments = $"ps -q -f name=^{DockerContainerName}$";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);
                    return !string.IsNullOrWhiteSpace(output.Trim());
                }
            }
            catch
            {
                return false;
            }
        }

        public static void StopAndRemoveReferenceServerContainer()
        {
            TestContext.Progress.WriteLine(
                $"Docker: Probeert container '{DockerContainerName}' te stoppen en te verwijderen..."
            );
            RunDockerCommand(
                $"stop {DockerContainerName}",
                TimeSpan.FromSeconds(10),
                ignoreErrors: true
            );
            RunDockerCommand(
                $"rm {DockerContainerName}",
                TimeSpan.FromSeconds(5),
                ignoreErrors: true
            );
            TestContext.Progress.WriteLine(
                $"Docker: Container '{DockerContainerName}' gestopt en verwijderd (als het bestond)."
            );
        }

        public static bool RunDockerCommand(
            string arguments,
            TimeSpan timeout,
            bool ignoreErrors = false
        )
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "docker";
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;

                    TestContext.Progress.WriteLine($"Docker Executing: docker {arguments}");
                    process.Start();

                    string output = "";
                    string error = "";
                    var outputTask = process
                        .StandardOutput.ReadToEndAsync()
                        .ContinueWith(t => output = t.Result);
                    var errorTask = process
                        .StandardError.ReadToEndAsync()
                        .ContinueWith(t => error = t.Result);

                    bool exited = process.WaitForExit((int)timeout.TotalMilliseconds);
                    Task.WaitAll(outputTask, errorTask);

                    if (!exited)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        { /* Negeer als al gestopt */
                        }
                        TestContext.Progress.WriteLine(
                            $"Docker: Commando '{arguments}' timed out na {timeout.TotalSeconds}s."
                        );
                        if (!ignoreErrors)
                            Assert.Fail($"Docker commando timed out: docker {arguments}");
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(output))
                        TestContext.Progress.WriteLine(
                            $"Docker Output for 'docker {arguments}':\n{output.Trim()}"
                        );
                    if (!string.IsNullOrWhiteSpace(error))
                        TestContext.Progress.WriteLine(
                            $"Docker Error for 'docker {arguments}':\n{error.Trim()}"
                        );

                    if (process.ExitCode != 0 && !ignoreErrors)
                    {
                        Assert.Fail(
                            $"Docker commando mislukt met exit code {process.ExitCode}: docker {arguments}\nError: {error}"
                        );
                        return false;
                    }
                    return process.ExitCode == 0 || ignoreErrors;
                }
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine(
                    $"Docker: Fout bij uitvoeren commando 'docker {arguments}': {ex.Message}"
                );
                if (!ignoreErrors)
                    Assert.Fail($"Exception bij uitvoeren docker commando: {ex.Message}");
                return false;
            }
        }
    }
}
