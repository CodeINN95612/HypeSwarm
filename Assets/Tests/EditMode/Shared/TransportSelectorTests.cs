using HypeSwarm.Shared.Net;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// Spec §13.4 in test form. The rule these protect is not "pick a transport" — it is that the
    /// process always ends up with one, and that the one case where the caller must be overruled
    /// actually overrules them.
    /// </summary>
    [TestFixture]
    public sealed class TransportSelectorTests
    {
        static TransportSelection Select(
            TransportKind? requested = null,
            bool steamAvailable = false,
            bool headless = false)
        {
            var availability = steamAvailable
                ? TransportAvailability.WithSteam()
                : TransportAvailability.DirectOnly("Steam is not running");

            return TransportSelector.Select(requested, availability, headless);
        }

        [Test]
        public void WithSteamRunning_AndNoPreference_SteamIsChosen()
        {
            Assert.That(Select(steamAvailable: true).Kind, Is.EqualTo(TransportKind.Steam));
        }

        [Test]
        public void WithoutSteam_AndNoPreference_DirectIsChosen()
        {
            Assert.That(Select().Kind, Is.EqualTo(TransportKind.Direct));
        }

        [Test]
        public void RequestingSteam_WhenItIsAvailable_IsHonoured()
        {
            var selection = Select(TransportKind.Steam, steamAvailable: true);

            Assert.That(selection.Kind, Is.EqualTo(TransportKind.Steam));
            Assert.That(selection.IsFallback, Is.False);
        }

        [Test]
        public void RequestingDirect_IsHonouredEvenWithSteamRunning()
        {
            Assert.That(
                Select(TransportKind.Direct, steamAvailable: true).Kind,
                Is.EqualTo(TransportKind.Direct));
        }

        /// <summary>
        /// The reason this is a rule and not a condition at the call site: a dedicated server has no
        /// Steam client attached, so Fizzy cannot serve it (§13.4). A server launched with the wrong
        /// flag must start anyway — refusing to run is worse than running on the other transport.
        /// </summary>
        [Test]
        public void AHeadlessProcess_NeverUsesSteam_EvenWhenAskedTo()
        {
            var selection = TransportSelector.Select(
                TransportKind.Steam, TransportAvailability.WithSteam(), headless: true);

            Assert.That(selection.Kind, Is.EqualTo(TransportKind.Direct));
            Assert.That(selection.IsFallback, Is.True);
        }

        [Test]
        public void SteamRequestedButUnavailable_FallsBackAndSaysWhy()
        {
            var availability = TransportAvailability.DirectOnly("no Steam client is running");
            var selection = TransportSelector.Select(TransportKind.Steam, availability, headless: false);

            Assert.That(selection.Kind, Is.EqualTo(TransportKind.Direct));
            Assert.That(selection.IsFallback, Is.True);
            Assert.That(selection.Reason, Does.Contain("no Steam client is running"));
        }

        /// <summary>
        /// Every path produces a sentence. A transport chosen silently is one nobody notices is
        /// wrong until five people are on the wrong one.
        /// </summary>
        [TestCase(null, false, false)]
        [TestCase(null, true, false)]
        [TestCase(TransportKind.Steam, false, false)]
        [TestCase(TransportKind.Steam, true, true)]
        [TestCase(TransportKind.Direct, false, true)]
        public void EverySelection_ExplainsItself(TransportKind? requested, bool steam, bool headless)
        {
            Assert.That(Select(requested, steam, headless).Reason, Is.Not.Empty);
        }

        [Test]
        public void LaunchOptions_AreReadForBothTheRequestAndTheHeadlessFlag()
        {
            var options = NetworkLaunchOptions.Parse(
                new[] { "-batchmode", "-hypeswarm-transport", "steam" });

            var selection = TransportSelector.Select(options, TransportAvailability.WithSteam());

            Assert.That(selection.Kind, Is.EqualTo(TransportKind.Direct));
        }

        [Test]
        public void AvailabilityWithSteam_CarriesNoFailureReason()
        {
            Assert.That(TransportAvailability.WithSteam().SteamUnavailableReason, Is.Empty);
        }
    }
}
