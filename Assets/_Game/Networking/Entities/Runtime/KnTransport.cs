using System;
using System.IO;
using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.TLS;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>How one world's packets travel.</summary>
    public enum KnTransportMode
    {
        /// <summary>Direct UDP, not encrypted (a host test in the Editor, a LAN test, loopback). Netcode's own default driver choice (IPC in the same process, else UDP).</summary>
        Plain = 0,
        /// <summary>Direct UDP with DTLS: the server proves itself with a certificate our authority signed; the game accepts only that.</summary>
        Secure = 1,
        /// <summary>Through Unity Relay (the relay encrypts by itself when its data says secure).</summary>
        Relay = 2,
    }

    /// <summary>What a server decided about its transport at start (plain data; the secrets are NOT in it).</summary>
    public readonly struct KnServerTransportPlan
    {
        public readonly KnTransportMode Mode;
        /// <summary>False: the server must not listen (a reason is in <see cref="Problem"/>).</summary>
        public readonly bool CanStart;
        public readonly string Problem;
        /// <summary>The server starts, but something is off (it names what, never a secret).</summary>
        public readonly string Warning;
        public readonly string CertificatePath;
        public readonly string KeyPath;

        public KnServerTransportPlan(KnTransportMode mode, bool canStart, string problem, string warning, string certificatePath = null, string keyPath = null)
        {
            Mode = mode;
            CanStart = canStart;
            Problem = problem;
            Warning = warning;
            CertificatePath = certificatePath;
            KeyPath = keyPath;
        }
    }

    /// <summary>
    /// The rules for choosing a transport, without any Unity or network call, so they can be tested. A real server must be
    /// encrypted or on Relay; a development build or the Editor may run plain with a warning; a release server runs plain only
    /// when its settings say so by name (<c>plainTransport</c>), never by accident because a file went missing.
    /// </summary>
    public static class KnTransportPlanner
    {
        public const string RelayMode = "relay";
        public const string DirectMode = "direct";

        /// <param name="settings">The server's settings.</param>
        /// <param name="isRelease">A dedicated server build (not the Editor, not a development test).</param>
        /// <param name="relayDataReady">The Relay allocation exists (the host layer made it before the world's first update).</param>
        /// <param name="serverFolder">The folder of the server build, for the default certificate folder.</param>
        /// <param name="fileExists">Replaces <see cref="File.Exists"/> in tests; null = the real one.</param>
        public static KnServerTransportPlan PlanServer(KnServerSettings settings, bool isRelease, bool relayDataReady, string serverFolder,
            Func<string, bool> fileExists = null)
        {
            fileExists ??= File.Exists;
            if (string.Equals(settings.connectionMode, RelayMode, StringComparison.OrdinalIgnoreCase))
            {
                return relayDataReady
                    ? new KnServerTransportPlan(KnTransportMode.Relay, true, null, null)
                    : new KnServerTransportPlan(KnTransportMode.Relay, false, "Relay mode needs the Relay allocation before the server starts listening, and none was handed to the server.", null);
            }

            ServerCertificates.ResolvePaths(settings.certificateFile, settings.privateKeyFile, serverFolder, out string cert, out string key);
            bool namedOne = !string.IsNullOrWhiteSpace(settings.certificateFile) != !string.IsNullOrWhiteSpace(settings.privateKeyFile);
            bool present = fileExists(cert) && fileExists(key);

            if (present && !settings.plainTransport && !namedOne)
                return new KnServerTransportPlan(KnTransportMode.Secure, true, null, null, cert, key);

            string why = namedOne ? "only one of certificateFile and privateKeyFile is set; both are needed"
                : present ? "plainTransport is on" : $"no certificate and key at {cert} and {key}";
            if (settings.plainTransport)
                return new KnServerTransportPlan(KnTransportMode.Plain, true, null,
                    $"NOT ENCRYPTED: {why}. Games that insist on encryption (all release games) cannot join directly.", cert, key);
            if (isRelease)
                return new KnServerTransportPlan(KnTransportMode.Plain, false,
                    $"This server will not start without encryption: {why}. Put the certificate there (Server Guide, \"Encryption\"), or set plainTransport to run unencrypted on purpose.", null, cert, key);
            return new KnServerTransportPlan(KnTransportMode.Plain, true, null,
                $"Direct connections are NOT encrypted in this development run: {why}. A release server would refuse to start.", cert, key);
        }

        /// <summary>The mode a client uses: Relay when it has a Relay join, else DTLS for a dedicated server, else plain.</summary>
        public static KnTransportMode ChooseClient(bool hasRelay, bool encryptDirect) =>
            hasRelay ? KnTransportMode.Relay : encryptDirect ? KnTransportMode.Secure : KnTransportMode.Plain;
    }

    /// <summary>The transport choice of one world (kept beside the world; read when its driver is made).</summary>
    public sealed class KnWorldTransport
    {
        public KnTransportMode Mode;
        public RelayServerData Relay;
        /// <summary>Server only. Held as text for the driver; never printed (see <see cref="ToString"/>).</summary>
        public string Certificate;
        public string PrivateKey;

        public override string ToString() => Mode.ToString(); // never the certificate text or the key
    }

    /// <summary>
    /// Netcode for Entities driver constructor of Krytical Nova: direct UDP, DTLS and Unity Relay, chosen per world
    /// (<see cref="Assign"/>) before the world's first update, which is when Netcode asks for the driver. A world with nothing
    /// assigned gets <see cref="KnTransportMode.Plain"/>, which is Netcode's own default choice. Install it once with
    /// <see cref="Install"/> (<see cref="KnBootstrap"/> does). What only a built player can prove: see the manual test list in the
    /// migration plan, "M2b-2 notes".
    /// </summary>
    public sealed class KnDriverConstructor : INetworkStreamDriverConstructor
    {
        public void CreateClientDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
        {
            KnWorldTransport t = KnTransport.Of(world);
            switch (t.Mode)
            {
                case KnTransportMode.Relay:
                {
                    RelayServerData relay = t.Relay;
                    DefaultDriverBuilder.RegisterClientDriver(world, ref driverStore, netDebug, ref relay);
                    break;
                }
                case KnTransportMode.Secure:
                {
                    NetworkSettings settings = DefaultDriverBuilder.GetNetworkClientSettings();
                    // The game accepts only a server whose certificate our authority signed, under our one server name.
                    settings.WithSecureClientParameters(ServerCertificates.AuthorityCertificate, ServerCertificates.ServerName);
                    DefaultDriverBuilder.RegisterClientDriver(world, ref driverStore, netDebug, settings);
                    break;
                }
                default:
                    DefaultDriverBuilder.RegisterClientDriver(world, ref driverStore, netDebug);
                    break;
            }
        }

        public void CreateServerDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
        {
            KnWorldTransport t = KnTransport.Of(world);
            switch (t.Mode)
            {
                case KnTransportMode.Relay:
                {
                    RelayServerData relay = t.Relay;
                    DefaultDriverBuilder.RegisterServerDriver(world, ref driverStore, netDebug, ref relay);
                    break;
                }
                case KnTransportMode.Secure:
                {
                    // The in-process loopback driver (Editor only) stays plain; only the socket that faces the internet is DTLS.
                    NetworkSettings plain = DefaultDriverBuilder.GetNetworkServerSettings();
#if UNITY_EDITOR
                    if (!world.IsHost()) DefaultDriverBuilder.RegisterServerIpcDriver(world, ref driverStore, netDebug, plain);
#endif
                    NetworkSettings secure = DefaultDriverBuilder.GetNetworkServerSettings();
                    secure.WithSecureServerParameters(t.Certificate, t.PrivateKey);
                    DefaultDriverBuilder.RegisterServerUdpDriver(world, ref driverStore, netDebug, secure);
                    break;
                }
                default:
                    DefaultDriverBuilder.RegisterServerDriver(world, ref driverStore, netDebug);
                    break;
            }
        }
    }

    /// <summary>The per-world transport table and the one place the constructor is installed. Main thread.</summary>
    public static class KnTransport
    {
        private static readonly ConditionalWeakTable<World, KnWorldTransport> s_Worlds = new ConditionalWeakTable<World, KnWorldTransport>();
        private static readonly KnWorldTransport s_Plain = new KnWorldTransport { Mode = KnTransportMode.Plain };

        /// <summary>
        /// The server's Relay allocation, handed over by the host layer (which asked Unity Relay for a place) before the server
        /// world's first update. <see cref="KnServerEntry"/> reads it for a server whose <c>connectionMode</c> is relay.
        /// </summary>
        public static RelayServerData? PendingServerRelay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PendingServerRelay = null;

        /// <summary>Makes <see cref="KnDriverConstructor"/> Netcode's driver constructor.</summary>
        public static void Install() => NetworkStreamReceiveSystem.DriverConstructor = new KnDriverConstructor();

        /// <summary>Sets how <paramref name="world"/> connects or listens. Before the world's first update.</summary>
        public static void Assign(World world, KnWorldTransport transport)
        {
            s_Worlds.Remove(world);
            s_Worlds.Add(world, transport);
        }

        public static void AssignRelayClient(World world, RelayServerData relay) => Assign(world, new KnWorldTransport { Mode = KnTransportMode.Relay, Relay = relay });

        public static void AssignSecureClient(World world) => Assign(world, new KnWorldTransport { Mode = KnTransportMode.Secure });

        public static KnWorldTransport Of(World world) => s_Worlds.TryGetValue(world, out KnWorldTransport t) ? t : s_Plain;

        /// <summary>
        /// Server start: applies the plan to <paramref name="world"/>. Reads the certificate and key when the plan is Secure (a file
        /// that turns out unreadable downgrades to the plan's rules again: release refuses, development warns). Returns false with
        /// <paramref name="problem"/> when the server must not listen.
        /// </summary>
        public static bool ApplyServerPlan(World world, KnServerTransportPlan plan, bool isRelease, out string warning, out string problem)
        {
            warning = plan.Warning;
            problem = plan.Problem;
            if (!plan.CanStart) return false;
            switch (plan.Mode)
            {
                case KnTransportMode.Relay:
                    if (PendingServerRelay == null) { problem = "Relay allocation missing."; return false; }
                    Assign(world, new KnWorldTransport { Mode = KnTransportMode.Relay, Relay = PendingServerRelay.Value });
                    return true;
                case KnTransportMode.Secure:
                    if (!ServerCertificates.TryLoad(plan.CertificatePath, plan.KeyPath, out string cert, out string key, out string loadProblem))
                    {
                        if (isRelease) { problem = "This server will not start without encryption: " + loadProblem + "."; return false; }
                        warning = "Direct connections are NOT encrypted: " + loadProblem + ".";
                        Assign(world, new KnWorldTransport { Mode = KnTransportMode.Plain });
                        return true;
                    }
                    Assign(world, new KnWorldTransport { Mode = KnTransportMode.Secure, Certificate = cert, PrivateKey = key });
                    return true;
                default:
                    Assign(world, new KnWorldTransport { Mode = KnTransportMode.Plain });
                    return true;
            }
        }
    }
}
