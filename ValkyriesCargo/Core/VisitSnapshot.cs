using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    public enum VisitPhase
    {
        None,
        Flying,
        Dropped,
        Approaching,
        Trading,
        Leaving,
    }

    /// <summary>
    /// The VisitState channel, parsed. "v1;visitId;phase;pilotUid;birdZdo;merchantZdo;dropX;dropY;dropZ;endWorldTime;purse;seed[;terminalsOpen]".
    /// The server writes it at every phase change; every client reads it (the terminal for its
    /// countdown and its close-on-leaving rule, the merchant for its state, the pilot for the
    /// drop point). ZDO ids travel as "userId:id" text so this file stays game-free. PURE.
    /// The 13th field (2026-09-08, issue #59) is OPTIONAL on the wire: a 12-field string from a side
    /// that is behind still parses, with the count read as 0, and the format version does not move.
    /// </summary>
    public sealed class VisitSnapshot
    {
        public const int FormatVersion = 1;

        public int VisitId;
        public VisitPhase Phase = VisitPhase.None;
        public long PilotUid;
        public string BirdZdo = "";
        public string MerchantZdo = "";
        public float DropX, DropY, DropZ;
        public double EndWorldTime;
        public int Purse;
        public int Seed;
        /// <summary>
        /// How many terminals are open on him right now, counted by the server on the deal wire
        /// (`VCargo_open` / `VCargo_close`, one per peer). The merchant's OWNER reads it: while it is
        /// above zero he never walks (the trading leash holds), because the player at the terminal may
        /// be one the owner's client has not even instanced. Never persisted.
        /// </summary>
        public int TerminalsOpen;

        public bool Active => Phase != VisitPhase.None;

        /// <summary>Seconds left on the visit clock at the given world time; never negative.</summary>
        public double Remaining(double worldTime) => Math.Max(0.0, EndWorldTime - worldTime);

        public string Encode() =>
            "v" + Wire.Int(FormatVersion) + Wire.Field + Wire.Int(VisitId) + Wire.Field + Phase + Wire.Field +
            Wire.Long(PilotUid) + Wire.Field + (BirdZdo ?? "") + Wire.Field + (MerchantZdo ?? "") + Wire.Field +
            Wire.Float(DropX) + Wire.Field + Wire.Float(DropY) + Wire.Field + Wire.Float(DropZ) + Wire.Field +
            Wire.Double(EndWorldTime) + Wire.Field + Wire.Int(Purse) + Wire.Field + Wire.Int(Seed) + Wire.Field +
            Wire.Int(Math.Max(0, TerminalsOpen));

        /// <summary>Never throws. Empty string = no visit (Phase None) with no problems.</summary>
        public static VisitSnapshot Parse(string s, List<string> problems)
        {
            var v = new VisitSnapshot();
            if (string.IsNullOrEmpty(s)) return v;
            string[] f = Wire.Fields(s, 13);
            if (f.Length != 12 && f.Length != 13) { Wire.Report(problems, "visit: expected 12 or 13 fields, found " + f.Length); return v; }
            if (f[0] != "v" + Wire.Int(FormatVersion)) { Wire.Report(problems, "visit: format " + f[0] + " is not v" + FormatVersion + "; update the side that is behind"); return v; }

            var parsed = new VisitSnapshot();
            VisitPhase phase;
            if (!Wire.TryInt(f[1], out parsed.VisitId)) { Wire.Report(problems, "visit: visitId did not parse"); return v; }
            if (!TryPhase(f[2], out phase)) { Wire.Report(problems, "visit: unknown phase '" + f[2] + "'"); return v; }
            parsed.Phase = phase;
            if (!Wire.TryLong(f[3], out parsed.PilotUid)) { Wire.Report(problems, "visit: pilotUid did not parse"); return v; }
            parsed.BirdZdo = f[4] ?? "";
            parsed.MerchantZdo = f[5] ?? "";
            if (!Wire.TryFloat(f[6], out parsed.DropX) || !Wire.TryFloat(f[7], out parsed.DropY) || !Wire.TryFloat(f[8], out parsed.DropZ))
            { Wire.Report(problems, "visit: drop point did not parse"); return v; }
            if (!Wire.TryDouble(f[9], out parsed.EndWorldTime)) { Wire.Report(problems, "visit: endWorldTime did not parse"); return v; }
            if (!Wire.TryInt(f[10], out parsed.Purse) || parsed.Purse < 0) { Wire.Report(problems, "visit: purse did not parse"); return v; }
            if (!Wire.TryInt(f[11], out parsed.Seed)) { Wire.Report(problems, "visit: seed did not parse"); return v; }
            if (f.Length == 13 && (!Wire.TryInt(f[12], out parsed.TerminalsOpen) || parsed.TerminalsOpen < 0))
            { Wire.Report(problems, "visit: terminalsOpen did not parse"); return v; }
            return parsed;
        }

        private static bool TryPhase(string s, out VisitPhase phase)
        {
            foreach (VisitPhase p in Enum.GetValues(typeof(VisitPhase)))
            {
                if (string.Equals(p.ToString(), (s ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) { phase = p; return true; }
            }
            phase = VisitPhase.None;
            return false;
        }

        /// <summary>A visit in the Trading phase with 300 s left at world time 0: what `cargo terminal demo` uses.</summary>
        public static readonly string Demo = new VisitSnapshot
        {
            VisitId = 1, Phase = VisitPhase.Trading, PilotUid = 1, BirdZdo = "", MerchantZdo = "1:1",
            DropX = 0f, DropY = 30f, DropZ = 0f, EndWorldTime = 300.0, Purse = 800, Seed = 7,
        }.Encode();
    }
}
