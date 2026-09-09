using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HypeSwarm.Shared.Net;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HypeSwarm.Editor
{
    /// <summary>
    /// Builds one player and launches copies of it pointed at the Editor, so a five-player session
    /// can be tested by one person on one machine.
    /// </summary>
    /// <remarks>
    /// The exit criterion for this step is five machines in one lobby (spec, Phase 1 step 3), and
    /// five machines is not a thing anyone has on a Tuesday. Five windows against a host running in
    /// the Editor exercises everything except the network itself: spawning, ownership, the roster,
    /// join and leave, and whether five champions moving at once still looks right.
    ///
    /// <para>It does not exercise latency — everything here is on loopback, where the round trip is
    /// under a millisecond. Mirror's <c>LatencySimulation</c> transport is the tool for that, and the
    /// real answer is still five machines before Phase 2 is finished.</para>
    ///
    /// <para>Clients are launched with <c>-hypeswarm-connect</c>, which
    /// <see cref="NetworkLaunchOptions"/> reads. The arguments are the whole configuration — nothing
    /// in the build is aware it is a test.</para>
    ///
    /// <para>The one thing that does differ is the scripting backend: this builds with Mono, while
    /// the game ships IL2CPP (§13.5). A test player is rebuilt after every code change, and an
    /// IL2CPP build takes minutes where Mono takes seconds — a loop nobody runs is worse than a loop
    /// that does not measure IL2CPP's performance, which this was never going to measure anyway. The
    /// project setting is restored afterwards.</para>
    /// </remarks>
    public static class NetworkTestLauncher
    {
        const string ScenePath = "Assets/Scenes/Prototype.unity";
        const string BuildFolder = "Builds/NetworkTest";
        const string ExecutableName = "HypeSwarm.exe";

        /// <summary>Small enough that several fit on one screen at once.</summary>
        const int WindowWidth = 860;
        const int WindowHeight = 484;

        const string RunningClientsKey = "HypeSwarm.NetworkTest.Clients";

        static string ExecutablePath =>
            Path.GetFullPath(Path.Combine(BuildFolder, ExecutableName));

        [MenuItem("Hype Swarm/Network/Build Test Player", priority = 100)]
        public static void BuildTestPlayer()
        {
            Directory.CreateDirectory(BuildFolder);

            var options = new BuildPlayerOptions
            {
                // Only the prototype. The template's SampleScene is still first in the build
                // settings, and a test player that opens it would show an empty room.
                scenes = new[] { ScenePath },
                locationPathName = ExecutablePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            var standalone = NamedBuildTarget.Standalone;
            var shipping = PlayerSettings.GetScriptingBackend(standalone);
            BuildReport report;

            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);

            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                // In a finally block because a build that throws must not leave the project on the
                // wrong backend — that is a setting nobody thinks to check and it would quietly
                // change what every later build measures. Saved immediately rather than left for
                // Unity's own schedule, or the file on disk keeps saying Mono until something else
                // happens to flush it, and that file is what gets committed.
                PlayerSettings.SetScriptingBackend(standalone, shipping);
                AssetDatabase.SaveAssets();
            }

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log(
                    $"Test player built at {ExecutablePath} in {report.summary.totalTime.TotalSeconds:0}s. " +
                    "Press Play in the Editor to host, then launch clients.");
                return;
            }

            Debug.LogError(
                $"Test player build {report.summary.result} with {report.summary.totalErrors} error(s).");
        }

        [MenuItem("Hype Swarm/Network/Launch 1 Test Client", priority = 120)]
        public static void LaunchOne() => Launch(1);

        [MenuItem("Hype Swarm/Network/Launch 2 Test Clients", priority = 121)]
        public static void LaunchTwo() => Launch(2);

        /// <summary>Four clients plus the Editor is the designed party size (§1).</summary>
        [MenuItem("Hype Swarm/Network/Launch 4 Test Clients (full lobby)", priority = 122)]
        public static void LaunchFull() => Launch(LobbyRoster.MaxPlayers - 1);

        /// <summary>
        /// One more than the lobby holds. The sixth is meant to be refused with a message, and that
        /// is a behaviour worth being able to watch rather than infer.
        /// </summary>
        [MenuItem("Hype Swarm/Network/Launch 5 Test Clients (one too many)", priority = 123)]
        public static void LaunchOneTooMany() => Launch(LobbyRoster.MaxPlayers);

        [MenuItem("Hype Swarm/Network/Stop Test Clients", priority = 140)]
        public static void StopClients()
        {
            var stopped = 0;

            foreach (var process in RunningClients())
            {
                try
                {
                    process.Kill();
                    stopped++;
                }
                catch (System.Exception error)
                {
                    Debug.LogWarning($"Could not stop client {process.Id}: {error.Message}");
                }
            }

            EditorPrefs.DeleteKey(RunningClientsKey);
            Debug.Log($"Stopped {stopped} test client(s).");
        }

        [MenuItem("Hype Swarm/Network/Stop Test Clients", true)]
        static bool CanStopClients() => RunningClients().Any();

        public static void Launch(int count, string address = "localhost")
        {
            if (!File.Exists(ExecutablePath))
            {
                Debug.LogError(
                    $"No test player at {ExecutablePath}. Run Hype Swarm → Network → Build Test Player first.");
                return;
            }

            var launched = new List<int>(RunningClients().Select(process => process.Id));

            for (var i = 0; i < count; i++)
            {
                var name = $"Client {launched.Count + 1}";

                var arguments = string.Join(" ",
                    $"{NetworkLaunchOptions.ConnectFlag} {address}:{NetworkLaunchOptions.DefaultPort}",
                    $"{NetworkLaunchOptions.NameFlag} \"{name}\"",
                    $"-screen-width {WindowWidth}",
                    $"-screen-height {WindowHeight}",
                    "-screen-fullscreen 0",
                    // Each instance needs its own log or they fight over the same file and the
                    // interesting one is whichever wrote last.
                    $"-logFile \"{Path.GetFullPath(Path.Combine(BuildFolder, $"client-{launched.Count + 1}.log"))}\"");

                var process = Process.Start(new ProcessStartInfo(ExecutablePath, arguments)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(ExecutablePath)
                });

                if (process != null)
                {
                    launched.Add(process.Id);
                }
            }

            EditorPrefs.SetString(RunningClientsKey, string.Join(",", launched));

            Debug.Log(
                $"Launched {count} client(s) against {address}. " +
                "Press Play in the Editor first if it is not already hosting.");
        }

        /// <summary>
        /// Process ids survive a domain reload in EditorPrefs; the processes themselves may not have
        /// survived at all, so every one is re-checked rather than trusted.
        /// </summary>
        static IEnumerable<Process> RunningClients()
        {
            var stored = EditorPrefs.GetString(RunningClientsKey, string.Empty);

            if (string.IsNullOrEmpty(stored))
            {
                yield break;
            }

            foreach (var part in stored.Split(','))
            {
                if (!int.TryParse(part, out var id))
                {
                    continue;
                }

                Process process = null;

                try
                {
                    process = Process.GetProcessById(id);

                    if (process.HasExited)
                    {
                        process = null;
                    }
                }
                catch (System.ArgumentException)
                {
                    // Already gone. Not worth a warning; closing a test window is the normal way to
                    // stop one.
                }

                if (process != null)
                {
                    yield return process;
                }
            }
        }
    }
}
