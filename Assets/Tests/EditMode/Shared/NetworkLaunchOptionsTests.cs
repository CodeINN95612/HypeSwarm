using HypeSwarm.Shared.Net;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// Launch arguments are how five instances get told what to be, and a misparsed one produces a
    /// process that starts perfectly and does the wrong thing. Every case here is one where the
    /// wrong answer looks like a working game.
    /// </summary>
    [TestFixture]
    public sealed class NetworkLaunchOptionsTests
    {
        static NetworkLaunchOptions Parse(params string[] args) => NetworkLaunchOptions.Parse(args);

        [Test]
        public void NoArguments_HostsOnTheDefaultPortWithNoTransportPreference()
        {
            var options = Parse();

            Assert.That(options.Role, Is.EqualTo(NetworkRole.Auto));
            Assert.That(options.ResolvedRole, Is.EqualTo(NetworkRole.Host));
            Assert.That(options.RequestedTransport, Is.Null);
            Assert.That(options.Port, Is.EqualTo(NetworkLaunchOptions.DefaultPort));
            Assert.That(options.Headless, Is.False);
            Assert.That(options.Warnings, Is.Empty);
        }

        [Test]
        public void NullArguments_AreTreatedAsNone()
        {
            Assert.That(NetworkLaunchOptions.Parse(null).ResolvedRole, Is.EqualTo(NetworkRole.Host));
        }

        [Test]
        public void UnrelatedArguments_AreIgnored()
        {
            var options = Parse("HypeSwarm.exe", "-logFile", "-", "-screen-width", "1920");

            Assert.That(options.ResolvedRole, Is.EqualTo(NetworkRole.Host));
            Assert.That(options.Warnings, Is.Empty, "Unity's own arguments are not our business");
        }

        // --- Role --------------------------------------------------------------------------

        /// <summary>
        /// The rule that makes a dedicated server possible without a second build configuration
        /// (spec §23): nobody is sitting at a headless process, so it serves rather than hosts.
        /// </summary>
        [Test]
        public void Batchmode_ResolvesToAServerRatherThanAHost()
        {
            var options = Parse("-batchmode", "-nographics");

            Assert.That(options.Headless, Is.True);
            Assert.That(options.ResolvedRole, Is.EqualTo(NetworkRole.Server));
        }

        [Test]
        public void AnExplicitRole_SurvivesHeadlessResolution()
        {
            Assert.That(Parse("-batchmode", "-hypeswarm-host").ResolvedRole, Is.EqualTo(NetworkRole.Host));
        }

        [Test]
        public void OfflineFlag_StartsNothing()
        {
            Assert.That(Parse("-hypeswarm-offline").ResolvedRole, Is.EqualTo(NetworkRole.Offline));
        }

        // --- Connecting --------------------------------------------------------------------

        [Test]
        public void ConnectFlag_MakesThisAClientAtThatAddress()
        {
            var options = Parse("-hypeswarm-connect", "10.0.0.5");

            Assert.That(options.ResolvedRole, Is.EqualTo(NetworkRole.Client));
            Assert.That(options.Address, Is.EqualTo("10.0.0.5"));
            Assert.That(options.Port, Is.EqualTo(NetworkLaunchOptions.DefaultPort));
        }

        /// <summary>
        /// The combined form is what one player pastes to another, so refusing it would mean five
        /// people hand-splitting an address they already have in one piece.
        /// </summary>
        [Test]
        public void ConnectFlag_AcceptsAnAddressWithThePortAttached()
        {
            var options = Parse("-hypeswarm-connect", "10.0.0.5:7100");

            Assert.That(options.Address, Is.EqualTo("10.0.0.5"));
            Assert.That(options.Port, Is.EqualTo(7100));
        }

        [Test]
        public void PortFlag_OverridesTheDefault()
        {
            Assert.That(Parse("-hypeswarm-port", "7005").Port, Is.EqualTo(7005));
        }

        [TestCase("0")]
        [TestCase("70000")]
        [TestCase("-1")]
        [TestCase("seven")]
        public void AnUnusablePort_KeepsTheDefaultAndSaysSo(string port)
        {
            var options = Parse("-hypeswarm-port", port);

            Assert.That(options.Port, Is.EqualTo(NetworkLaunchOptions.DefaultPort));
            Assert.That(options.Warnings, Has.Exactly(1).Contains(port));
        }

        // --- Transport ---------------------------------------------------------------------

        [TestCase("steam", TransportKind.Steam)]
        [TestCase("Steam", TransportKind.Steam)]
        [TestCase("DIRECT", TransportKind.Direct)]
        public void TransportFlag_IsCaseInsensitive(string value, TransportKind expected)
        {
            Assert.That(Parse("-hypeswarm-transport", value).RequestedTransport, Is.EqualTo(expected));
        }

        [Test]
        public void AnUnknownTransport_IsRefusedRatherThanGuessedAt()
        {
            var options = Parse("-hypeswarm-transport", "carrier-pigeon");

            Assert.That(options.RequestedTransport, Is.Null);
            Assert.That(options.Warnings, Has.Exactly(1).Contains("carrier-pigeon"));
        }

        // --- Malformed input ---------------------------------------------------------------

        /// <summary>
        /// A flag typed as the last argument with its value forgotten. Reading past the end would
        /// take down a dedicated server at startup, which is where nobody is watching.
        /// </summary>
        [Test]
        public void AFlagMissingItsValue_IsWarnedAboutRatherThanRead()
        {
            var options = Parse("-hypeswarm-connect");

            Assert.That(options.Address, Is.Empty);
            Assert.That(options.ResolvedRole, Is.EqualTo(NetworkRole.Host));
            Assert.That(options.Warnings, Has.Exactly(1).Contains(NetworkLaunchOptions.ConnectFlag));
        }

        [Test]
        public void AnEmptyAddress_DoesNotMakeThisAClientWithNowhereToGo()
        {
            var options = Parse("-hypeswarm-connect", "   ");

            Assert.That(options.ResolvedRole, Is.EqualTo(NetworkRole.Host));
            Assert.That(options.Warnings, Is.Not.Empty);
        }

        [Test]
        public void TheLastOccurrenceOfAFlagWins()
        {
            Assert.That(Parse("-hypeswarm-port", "7001", "-hypeswarm-port", "7002").Port, Is.EqualTo(7002));
        }

        [Test]
        public void DisplayName_IsCarriedThrough()
        {
            Assert.That(Parse("-hypeswarm-name", "Instance 3").DisplayName, Is.EqualTo("Instance 3"));
        }
    }
}
