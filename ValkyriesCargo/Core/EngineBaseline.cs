using System.Globalization;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>Which way the game moved against the build this DLL was written on.</summary>
    public enum VersionDrift
    {
        /// <summary>The same three numbers.</summary>
        Same = 0,
        /// <summary>The game is ahead of us. The usual case for a player who updated first.</summary>
        Newer = 1,
        /// <summary>The game is behind us. Somebody rolled back, or is on an old branch.</summary>
        Older = 2,
        /// <summary>Nothing readable came back. Not the same as "unchanged".</summary>
        Unreadable = 3,
    }

    /// <summary>
    /// What `EngineBaseline.Compare` found: the four numbers side by side and one line of words.
    /// PURE - no Unity types, no reflection, no logging. The game side (`EngineCheck`) reads the live
    /// numbers and hands them here; this only judges them.
    /// </summary>
    public sealed class EngineComparison
    {
        public string ActualGame = "";
        public int ActualNetwork;
        public int ActualPlayer;
        public int ActualWorld;

        public VersionDrift Game = VersionDrift.Same;
        public bool NetworkMoved;
        public bool PlayerMoved;
        public bool WorldMoved;

        /// <summary>One line, for the boot log and `cargo status`. Deterministic; the harness pins it.</summary>
        public string Verdict = "";

        /// <summary>Nothing moved at all.</summary>
        public bool Same => Game == VersionDrift.Same && !NetworkMoved && !PlayerMoved && !WorldMoved;

        /// <summary>
        /// The network version is the handshake and the packet layout. A move here is the one that can
        /// take the deal wire out from under us without a single exception, so it gets its own flag.
        /// </summary>
        public bool WireAtRisk => NetworkMoved;

        /// <summary>The player and world versions are the save formats: a move puts the sidecar's siblings at risk.</summary>
        public bool SavesAtRisk => PlayerMoved || WorldMoved;

        public override string ToString() => Verdict;
    }

    /// <summary>
    /// The build this DLL was compiled against, as constants, and the comparison against what is
    /// actually running (P10b, design 0 and the brief's "say what you were built for").
    ///
    /// Everything the mod knows about Valheim it knows from ONE build. The dependency is not the API
    /// surface - a rename fails the build - but roughly seventy method BODIES, which can change without
    /// touching a signature. These numbers are the identity of the build those bodies were read from,
    /// and they are the first thing anyone should see in a log when a bug report arrives.
    ///
    /// Why the numbers are copied here rather than read from `Version`: the game's `Version` type is
    /// INTERNAL and its three numbers are `const`. A direct reference would be inlined AT OUR COMPILE
    /// TIME and would cheerfully report our own baseline back to us as if it were the live value - the
    /// comparison would be `36 == 36` for ever, on every build of Valheim ever released. `EngineCheck`
    /// reads the live ones by reflection over the loaded metadata; this file is the other half.
    ///
    /// PURE: no Unity types, harness-tested.
    /// </summary>
    public static class EngineBaseline
    {
        /// <summary>`Version.CurrentVersion.ToString()` on the build this was written against.</summary>
        public const string GameVersion = "0.221.12";
        public const int GameMajor = 0;
        public const int GameMinor = 221;
        public const int GamePatch = 12;

        /// <summary>`Version.m_networkVersion` (a `uint` const in the real assembly). The handshake and the wire.</summary>
        public const int NetworkVersion = 36;
        /// <summary>`Version.m_playerVersion`. The character save format.</summary>
        public const int PlayerVersion = 43;
        /// <summary>`Version.m_worldVersion`. The world save format the sidecar sits beside.</summary>
        public const int WorldVersion = 37;

        /// <summary>Steam build id of app 892970 (the client) as installed when the bodies were read.</summary>
        public const int ClientBuildId = 21981559;
        /// <summary>Steam build id of app 896660 (the dedicated server).</summary>
        public const int ServerBuildId = 21981590;

        /// <summary>When the method bodies this mod depends on were read. A branch re-push keeps the version and changes the bodies, so this date matters as much as the numbers.</summary>
        public const string ReadOn = "2026-09-06";

        /// <summary>The baseline in one line, for the boot log.</summary>
        public static string Describe() =>
            "built against Valheim " + GameVersion + " (network " + Wire.Int(NetworkVersion) +
            ", player " + Wire.Int(PlayerVersion) + ", world " + Wire.Int(WorldVersion) +
            "; Steam build " + Wire.Int(ClientBuildId) + " client / " + Wire.Int(ServerBuildId) +
            " server; bodies read " + ReadOn + ")";

        /// <summary>
        /// Judge the four live numbers against the four compiled-in ones. Never throws; an unreadable
        /// game version is its own verdict and is never mistaken for a match.
        /// </summary>
        public static EngineComparison Compare(string actualGame, int actualNetwork, int actualPlayer, int actualWorld)
        {
            var c = new EngineComparison
            {
                ActualGame = actualGame ?? "",
                ActualNetwork = actualNetwork,
                ActualPlayer = actualPlayer,
                ActualWorld = actualWorld,
                NetworkMoved = actualNetwork != NetworkVersion,
                PlayerMoved = actualPlayer != PlayerVersion,
                WorldMoved = actualWorld != WorldVersion,
            };

            int major, minor, patch;
            if (!TryParse(c.ActualGame, out major, out minor, out patch)) c.Game = VersionDrift.Unreadable;
            else
            {
                int order = Order(major, minor, patch);
                c.Game = order > 0 ? VersionDrift.Newer : order < 0 ? VersionDrift.Older : VersionDrift.Same;
            }

            c.Verdict = Verdict(c);
            return c;
        }

        /// <summary>-1 older than the baseline, 0 the same, +1 newer. `rc` builds sort below their release (patch is negative).</summary>
        public static int Order(int major, int minor, int patch)
        {
            if (major != GameMajor) return major > GameMajor ? 1 : -1;
            if (minor != GameMinor) return minor > GameMinor ? 1 : -1;
            if (patch != GamePatch) return patch > GamePatch ? 1 : -1;
            return 0;
        }

        /// <summary>
        /// `GameVersion.ToString()`, in every shape it can take: "0.221.12", "0.221" (patch 0),
        /// "0.221.rc3" (a release candidate, which sorts BELOW 0.221.0 - vanilla stores it as a negative
        /// patch), and with a platform prefix ("dw-0.221.12") in case a caller hands us
        /// `GetVersionString()` instead. "" (an invalid GameVersion) is a failure, not a zero.
        /// </summary>
        public static bool TryParse(string s, out int major, out int minor, out int patch)
        {
            major = 0; minor = 0; patch = 0;
            if (string.IsNullOrEmpty(s)) return false;

            // GetVersionString(true) appends the mercurial hash on a second line.
            int nl = s.IndexOf('\n');
            if (nl >= 0) s = s.Substring(0, nl);
            s = s.Trim();

            // A platform prefix ("dw-", "l-", "ms-") sits ahead of the numbers.
            int dash = s.LastIndexOf('-');
            if (dash >= 0) s = s.Substring(dash + 1);
            if (s.Length == 0) return false;

            string[] parts = s.Split('.');
            if (parts.Length < 2 || parts.Length > 3) return false;
            if (!Num(parts[0], out major) || !Num(parts[1], out minor)) return false;
            if (parts.Length == 2) { patch = 0; return true; }

            string p = parts[2];
            if (p.StartsWith("rc", System.StringComparison.OrdinalIgnoreCase))
            {
                int rc;
                if (!Num(p.Substring(2), out rc) || rc <= 0) return false;
                patch = -rc;
                return true;
            }
            return Num(p, out patch);
        }

        private static bool Num(string s, out int v) =>
            int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);

        private static string Verdict(EngineComparison c)
        {
            if (c.Same)
                return "same build " + GameVersion + " (net " + Wire.Int(NetworkVersion) + ", player " +
                       Wire.Int(PlayerVersion) + ", world " + Wire.Int(WorldVersion) + ")";

            string line = "";
            switch (c.Game)
            {
                case VersionDrift.Newer:
                    line = "newer game version (" + c.ActualGame + " vs " + GameVersion + ")"; break;
                case VersionDrift.Older:
                    line = "older game version (" + c.ActualGame + " vs " + GameVersion + ")"; break;
                case VersionDrift.Unreadable:
                    line = "game version unreadable ('" + c.ActualGame + "' vs " + GameVersion + ")"; break;
            }
            if (c.NetworkMoved) line = Add(line, "network version moved (" + Wire.Int(c.ActualNetwork) + " vs " + Wire.Int(NetworkVersion) + ")");
            if (c.PlayerMoved) line = Add(line, "player version moved (" + Wire.Int(c.ActualPlayer) + " vs " + Wire.Int(PlayerVersion) + ")");
            if (c.WorldMoved) line = Add(line, "world version moved (" + Wire.Int(c.ActualWorld) + " vs " + Wire.Int(WorldVersion) + ")");
            return line;
        }

        private static string Add(string line, string part) => line.Length == 0 ? part : line + "; " + part;
    }
}
