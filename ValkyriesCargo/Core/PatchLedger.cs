using System;
using System.Collections.Generic;
using System.Text;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The record of what happened when the Harmony patches were APPLIED, one class at a time — and the
    /// one decision that hangs off it: does this mod run at all. PURE, so the decision is provable.
    ///
    /// Why this exists (issue #31): a bare <c>Harmony.PatchAll()</c> is one call, and if any single patch
    /// fails to apply — a renamed method, a changed signature, a type that moved — the call throws,
    /// <c>Awake</c> unwinds, and the ENTIRE mod fails to load: not the failing patch, all of it, with a
    /// stack trace in the log and an empty <c>cargo</c> prefix. House rule 3 (every patch BODY in its own
    /// try/catch) never sees it, because application happens once, before any body runs. Valheim 1.0
    /// lands 2026-09-09; a release that renames a method is exactly the event this is for.
    ///
    /// The shape: every patch class is applied on its own (<c>Patching.cs</c>), each outcome is a row here,
    /// and a failure is logged BY NAME and counted rather than thrown. A degraded mod says it is degraded:
    /// the boot line prints <c>patches applied/expected</c> and <c>cargo status</c> prints the failures.
    ///
    /// The one exception is <see cref="IsLoadBearing"/>: the vendored ServerSync's patches are the version
    /// gate and the config lock. Without them a client on another build joins, and a locked config is not
    /// locked, and every other feature runs on a lie. So a load-bearing failure REFUSES the mod — nothing
    /// ticks, nothing registers, the console still answers <c>cargo status</c> with the reason. That set is
    /// deliberately the smallest one that is true; widening it is the owner's decision (issue #31).
    /// </summary>
    public sealed class PatchLedger
    {
        public sealed class Row
        {
            /// <summary>The patch class's short name (`Patch_Valkyrie_Awake`, `ConfigSync+RegisterRPCPatch`).</summary>
            public string Name;
            /// <summary>Its full type name, which is what decides LoadBearing.</summary>
            public string FullName;
            public bool Applied;
            /// <summary>How many methods the class patched. 0 with Applied is a container that patched nothing.</summary>
            public int Methods;
            /// <summary>The exception's message when it did not apply; "" otherwise.</summary>
            public string Error = "";
            public bool LoadBearing;
        }

        private readonly List<Row> _rows = new List<Row>();

        public IReadOnlyList<Row> Rows => _rows;
        public int Applied { get; private set; }
        public int Failed { get; private set; }
        public int Expected => _rows.Count;

        /// <summary>True when a load-bearing patch did not apply: the mod must not run.</summary>
        public bool Refused { get; private set; }

        /// <summary>
        /// The load-bearing set, by full type name. ServerSync's patch classes are nested in
        /// <c>ServerSync.ConfigSync</c> and <c>ServerSync.VersionCheck</c> (the namespace is `ServerSync`);
        /// they carry the version gate (`ZNet.RPC_PeerInfo`), the RPC registration (`ZNet.Awake`,
        /// `OnNewConnection`) and the config lock (`ConfigEntryBase.Get/SetSerializedValue`).
        /// </summary>
        public static bool IsLoadBearing(string fullTypeName) =>
            fullTypeName != null && fullTypeName.StartsWith("ServerSync.", StringComparison.Ordinal);

        public Row Record(string name, string fullTypeName, bool applied, int methods, string error)
        {
            Row r = new Row
            {
                Name = name ?? "?",
                FullName = fullTypeName ?? "",
                Applied = applied,
                Methods = applied ? Math.Max(0, methods) : 0,
                Error = applied ? "" : FirstLine(error),
                LoadBearing = IsLoadBearing(fullTypeName),
            };
            _rows.Add(r);
            if (applied) Applied++; else Failed++;
            if (!applied && r.LoadBearing) Refused = true;
            return r;
        }

        /// <summary>One line for the boot log and `cargo status`: the count, then every failure by name.</summary>
        public string StatusLine()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("patches ").Append(Applied).Append('/').Append(Expected).Append(" applied");
            if (Failed == 0) return sb.ToString();
            sb.Append("; FAILED: ");
            bool first = true;
            foreach (Row r in _rows)
            {
                if (r.Applied) continue;
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(r.Name);
                if (r.LoadBearing) sb.Append(" [LOAD-BEARING]");
            }
            if (Refused) sb.Append("; the mod REFUSED to run (a load-bearing patch did not apply)");
            else sb.Append("; running degraded");
            return sb.ToString();
        }

        /// <summary>One line per failure, with the error, for `cargo status`.</summary>
        public IEnumerable<string> Report()
        {
            foreach (Row r in _rows)
                if (!r.Applied)
                    yield return r.Name + (r.LoadBearing ? " [LOAD-BEARING]" : "") + ": " + (r.Error.Length > 0 ? r.Error : "did not apply");
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int n = s.IndexOfAny(new[] { '\r', '\n' });
            string line = n < 0 ? s : s.Substring(0, n);
            return line.Length > 200 ? line.Substring(0, 200) : line;
        }
    }
}
