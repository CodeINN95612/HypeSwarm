using System;
using System.Collections.Generic;
using System.Globalization;

namespace HypeSwarm.Shared.Net
{
    /// <summary>
    /// What the command line asked this process to do. Pure: it takes a string array and returns a
    /// value, so the whole surface is testable without launching anything.
    /// </summary>
    /// <remarks>
    /// Launch arguments are how five instances get started for a test, how a dedicated server is
    /// configured (spec §23), and how a Steam invite eventually arrives. A misspelled flag that is
    /// silently ignored costs an afternoon of wondering why the second instance keeps hosting its
    /// own game, so every unrecognised or unusable argument lands in <see cref="Warnings"/> rather
    /// than disappearing.
    ///
    /// <para>The <c>hypeswarm-</c> prefix keeps these clear of Unity's own arguments, which are
    /// numerous and undocumented in places.</para>
    /// </remarks>
    public sealed class NetworkLaunchOptions
    {
        public const string TransportFlag = "-hypeswarm-transport";
        public const string ConnectFlag = "-hypeswarm-connect";
        public const string HostFlag = "-hypeswarm-host";
        public const string ServerFlag = "-hypeswarm-server";
        public const string OfflineFlag = "-hypeswarm-offline";
        public const string PortFlag = "-hypeswarm-port";
        public const string NameFlag = "-hypeswarm-name";

        public const ushort DefaultPort = 7777;

        /// <summary>Transport the launcher insisted on, or null to let <see cref="TransportSelector"/> decide.</summary>
        public TransportKind? RequestedTransport { get; private set; }

        public NetworkRole Role { get; private set; } = NetworkRole.Auto;

        /// <summary>Host to connect to. Empty unless <see cref="Role"/> is <see cref="NetworkRole.Client"/>.</summary>
        public string Address { get; private set; } = string.Empty;

        public ushort Port { get; private set; } = DefaultPort;

        /// <summary>Display name for the lobby roster, or empty to fall back to the device name.</summary>
        public string DisplayName { get; private set; } = string.Empty;

        /// <summary>
        /// No window, no input, no Steam client. Set by Unity's own <c>-batchmode</c> and
        /// <c>-nographics</c>, and by the dedicated server player itself.
        /// </summary>
        public bool Headless { get; private set; }

        /// <summary>Arguments that were understood as flags but could not be used. Never silently dropped.</summary>
        public IReadOnlyList<string> Warnings => warnings;

        readonly List<string> warnings = new List<string>();

        /// <summary>
        /// Resolves <see cref="NetworkRole.Auto"/> against the environment. A headless process has
        /// nobody sitting at it, so it serves rather than hosts.
        /// </summary>
        public NetworkRole ResolvedRole =>
            Role != NetworkRole.Auto ? Role : Headless ? NetworkRole.Server : NetworkRole.Host;

        public static NetworkLaunchOptions Parse(string[] args)
        {
            var options = new NetworkLaunchOptions();

            if (args == null)
            {
                return options;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var flag = args[i];

                if (string.IsNullOrEmpty(flag))
                {
                    continue;
                }

                switch (flag.ToLowerInvariant())
                {
                    case "-batchmode":
                    case "-nographics":
                        options.Headless = true;
                        break;

                    case HostFlag:
                        options.Role = NetworkRole.Host;
                        break;

                    case ServerFlag:
                        options.Role = NetworkRole.Server;
                        break;

                    case OfflineFlag:
                        options.Role = NetworkRole.Offline;
                        break;

                    case TransportFlag:
                        options.ReadTransport(Next(args, ref i, options, TransportFlag));
                        break;

                    case ConnectFlag:
                        options.ReadConnect(Next(args, ref i, options, ConnectFlag));
                        break;

                    case PortFlag:
                        options.ReadPort(Next(args, ref i, options, PortFlag), PortFlag);
                        break;

                    case NameFlag:
                        options.DisplayName = Next(args, ref i, options, NameFlag) ?? string.Empty;
                        break;
                }
            }

            return options;
        }

        /// <summary>
        /// Consumes the argument after a flag, or records a warning when there is not one. Returning
        /// null rather than throwing keeps one bad argument from stopping the process from starting
        /// at all — on a dedicated server that difference is a restart loop.
        /// </summary>
        static string Next(string[] args, ref int index, NetworkLaunchOptions options, string flag)
        {
            if (index + 1 >= args.Length)
            {
                options.warnings.Add($"{flag} was given without a value and has been ignored.");
                return null;
            }

            index++;
            return args[index];
        }

        void ReadTransport(string value)
        {
            if (value == null)
            {
                return;
            }

            if (Enum.TryParse<TransportKind>(value, ignoreCase: true, out var kind))
            {
                RequestedTransport = kind;
                return;
            }

            warnings.Add(
                $"{TransportFlag} '{value}' is not a transport. " +
                $"Expected {string.Join(" or ", Enum.GetNames(typeof(TransportKind)))}.");
        }

        /// <summary>
        /// Accepts <c>host</c> or <c>host:port</c>. The combined form is what people paste to each
        /// other, so refusing it would mean everyone hand-splitting an address they already have.
        /// </summary>
        void ReadConnect(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (value != null)
                {
                    warnings.Add($"{ConnectFlag} was given an empty address and has been ignored.");
                }

                return;
            }

            var separator = value.LastIndexOf(':');

            if (separator > 0 && separator < value.Length - 1)
            {
                ReadPort(value.Substring(separator + 1), ConnectFlag);
                value = value.Substring(0, separator);
            }

            Address = value.Trim();
            Role = NetworkRole.Client;
        }

        void ReadPort(string value, string flag)
        {
            if (value == null)
            {
                return;
            }

            if (ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port != 0)
            {
                Port = port;
                return;
            }

            warnings.Add($"{flag} port '{value}' is not a number between 1 and 65535. Using {Port}.");
        }
    }
}
