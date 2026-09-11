using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using UnityEngine;

namespace RavenIron.ValkyriesCargo
{
    /// <summary>
    /// The one place `Server.CarryOffset` meets the game, the twin of `ActiveAreaLive`: the rule is pure
    /// (`Core/CarryOffset.cs`) and this is the read. Two callers, both in Track B's files and both one
    /// line: `CargoFlight.AttachOffset`, which the merchant takes his pin offset from every physics step
    /// (`CargoMerchant.ResolveCarrier`), and the fallback in that same resolve for a bird with no
    /// `CargoFlight` on it.
    ///
    /// Because the resolve runs every FixedUpdate, a change pushed by ServerSync lands on the NEXT
    /// physics step - the offset can be tuned from Configuration Manager, as an admin, while he is in
    /// the air. A refused value is logged at most three times and then followed silently, which is the
    /// cosmetics rule: a typo in a config must not take the flight down with it.
    /// </summary>
    public static class CarryPinLive
    {
        /// <summary>The shipped prefab's own offset, for a bird with no `Valkyrie` component to ask.</summary>
        public static Vector3 PrefabFallback =>
            new Vector3(CarryOffset.PrefabX, CarryOffset.PrefabY, CarryOffset.PrefabZ);

        private static string _lastProblem;
        private static int _logged;

        /// <summary>
        /// The offset to hang him at: the config's when it is usable, this bird's own prefab value when
        /// it is not. <paramref name="prefabOffset"/> is the live `Valkyrie.m_attachOffset`, never a
        /// constant, so an unedited config still follows the game if Iron Gate ever moves it.
        /// </summary>
        public static Vector3 Read(Vector3 prefabOffset)
        {
            string text = ModConfig.CarryOffset != null ? ModConfig.CarryOffset.Value : CarryOffset.FollowThePrefab;
            CarryOffset.Resolved r = CarryOffset.Resolve(text, prefabOffset.x, prefabOffset.y, prefabOffset.z);
            Report(r);
            return new Vector3(r.X, r.Y, r.Z);
        }

        /// <summary>What `cargo status` prints, resolved against the shipped prefab's numbers.</summary>
        public static string StatusLine()
        {
            Vector3 p = PrefabFallback;
            string text = ModConfig.CarryOffset != null ? ModConfig.CarryOffset.Value : CarryOffset.FollowThePrefab;
            return CarryOffset.Describe(CarryOffset.Resolve(text, p.x, p.y, p.z));
        }

        /// <summary>Once per distinct problem, three times at most: this is read every physics step.</summary>
        private static void Report(CarryOffset.Resolved r)
        {
            if (r.Problem == null) { _lastProblem = null; return; }
            if (r.Problem == _lastProblem || _logged >= 3) return;
            _lastProblem = r.Problem;
            _logged++;
            ValkyriesCargo.Log.LogWarning("Server.CarryOffset refused: " + r.Problem +
                                          "; he hangs at the Valkyrie prefab's own offset instead");
        }
    }
}
