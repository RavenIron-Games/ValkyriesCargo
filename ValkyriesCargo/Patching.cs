using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo
{
    /// <summary>
    /// Applies every Harmony patch class in this assembly ONE AT A TIME, each in its own try/catch, and
    /// writes the outcome into <see cref="PatchLedger"/>. The game-side half of issue #31; the decision
    /// (degraded versus refused) is the pure half's.
    ///
    /// This is exactly what <c>Harmony.PatchAll(Assembly)</c> does inside — every type through
    /// <c>CreateClassProcessor(type).Patch()</c> — minus the one property that made it a hard blocker:
    /// it stops at the first throw. Types with no Harmony attribute are skipped, so the ledger's
    /// "expected" is the number of patch classes, not the number of types.
    ///
    /// The vendored ServerSync patches itself on the next frame with its own Harmony id, and skips when
    /// it finds its <c>RegisterRPCPatch</c> already on <c>ZNet.Awake</c>; so today it is THIS loop that
    /// applies the version gate, and those rows are the load-bearing ones (<see cref="PatchLedger.IsLoadBearing"/>).
    /// </summary>
    public static class Patching
    {
        public static PatchLedger Ledger { get; private set; } = new PatchLedger();

        public static void Apply(Harmony harmony, Assembly assembly)
        {
            PatchLedger ledger = new PatchLedger();
            Type[] types;
            try { types = AccessTools.GetTypesFromAssembly(assembly); }
            catch (Exception ex)
            {
                // Cannot even enumerate the types: record it as one load-bearing failure so the mod refuses.
                ledger.Record("(assembly)", "ServerSync.(enumeration)", false, 0, ex.Message);
                Ledger = ledger;
                return;
            }

            foreach (Type type in types)
            {
                if (type == null) continue;
                bool isPatch;
                try { isPatch = type.GetCustomAttributes(typeof(HarmonyAttribute), true).Length > 0; }
                catch { isPatch = false; }
                if (!isPatch) continue;

                string name = Short(type);
                try
                {
                    PatchClassProcessor processor = harmony.CreateClassProcessor(type);
                    List<MethodInfo> patched = processor.Patch();
                    ledger.Record(name, type.FullName, true, patched != null ? patched.Count : 0, null);
                }
                catch (Exception ex)
                {
                    PatchLedger.Row r = ledger.Record(name, type.FullName, false, 0, ex.Message);
                    ValkyriesCargo.Log.LogError("patch " + name + " did not apply" + (r.LoadBearing ? " [LOAD-BEARING]" : "") + ": " + ex.Message);
                }
            }
            Ledger = ledger;
        }

        /// <summary>`ConfigSync+RegisterRPCPatch` for a nested type, `Patch_Valkyrie_Awake` for a plain one.</summary>
        private static string Short(Type t)
        {
            if (t.DeclaringType == null) return t.Name;
            return Short(t.DeclaringType) + "+" + t.Name;
        }
    }
}
