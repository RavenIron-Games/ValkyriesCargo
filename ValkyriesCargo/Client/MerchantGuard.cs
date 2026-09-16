using System;
using System.Collections.Generic;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// Ingvar is our clone of the vanilla Dverger prefab, and other mods edit that prefab for their own
    /// Dvergr. A taming mod puts a Tameable, a breeding component and a genetics component on it:
    /// DvergrAllies 1.0.7 injects three at <c>ZNetScene.Awake</c>, on every process that loads it. Seen on
    /// Wonderland, 2026-09-15: the use key gave vanilla's tame-follow ("Ingvar the Far-Travelled follows
    /// you") instead of our terminal, because <c>Player.Interact</c> takes the first Interactable in
    /// component order and a prefab component always precedes one added at runtime; the hover gained
    /// "(Female)" and "Hungry", painted onto the HUD by the genetics component's LateUpdate; and for the
    /// length of the visit he was a commandable, breedable pet. The shipped Dverger carries none of this,
    /// so anything of the kind on OUR clone came from a mod that meant it for wild Dvergr, not for him.
    ///
    /// Runs once, on the client, from the <c>Humanoid.Awake</c> postfix that makes the clone Ingvar, and
    /// BEFORE <c>CargoMerchant</c> is added. The order and the call are both deliberate:
    /// <c>CargoMerchant.Awake</c> calls <c>SetTamed(true)</c> in this same frame, and a taming mod's
    /// <c>SetTamed</c> postfixes key on <c>GetComponent</c> of their genetics component (DvergrAllies counts
    /// a tame that way); a deferred <c>Object.Destroy</c> leaves a component findable until the end of the
    /// frame, so it is <c>DestroyImmediate</c>, the same call the taming mod used to put them there. And
    /// because a runtime-added component sits after <c>Humanoid</c> in component order, its own Awake has
    /// not run yet when this does, and never will: no "Command" RPC registered on his ZNetView, no
    /// TamingUpdate, no Procreate timer. Everything here touches our own spawned object and nothing else:
    /// not the prefab, not anyone's creature. His consume list (the prefab's own, or the taming mod's)
    /// is <c>CargoMerchant</c>'s business, swapped on the owner where the only reader runs.
    /// </summary>
    public static class MerchantGuard
    {
        /// <summary>
        /// Shadows of Midgard's opt-out, a frozen public key (its contract v1): int 1 on the creature's ZDO
        /// means SoM leaves the creature vanilla AI and vanilla perception. Written from the owner, once.
        /// Inert where SoM is absent: one int on the ZDO that nothing reads.
        /// </summary>
        public const string SoMExemptKey = "SoMStealthExempt";

        /// <summary>
        /// Pet-system components known to be injected into the Dvergr prefab, by full type name, so no
        /// reference to those mods is needed. A Tameable or Procreation of any subclass is removed by type
        /// whatever it is called; this list catches the ones that are plain MonoBehaviours.
        /// </summary>
        public static readonly string[] KnownPetComponents =
        {
            "DvergrAllies.DvergrTameable",
            "DvergrAllies.DvergrProcreation",
            "DvergrAllies.DvergrGenetics",
        };

        private static int _throws;

        /// <summary>
        /// Strip what other mods put on the clone for their Dvergr, and stamp the SoM opt-out if this
        /// machine owns the ZDO. <paramref name="zdo"/> may be the ZNetView's or the
        /// <c>ZNetView.m_initZDO</c> fallback the caller already resolved; both are this object's.
        /// </summary>
        public static void Apply(GameObject go, ZDO zdo)
        {
            if (go == null) return;
            try
            {
                var removed = new List<string>();
                foreach (Tameable t in go.GetComponents<Tameable>()) Remove(t, removed);
                foreach (Procreation p in go.GetComponents<Procreation>()) Remove(p, removed);
                foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    if (Array.IndexOf(KnownPetComponents, mb.GetType().FullName) >= 0) Remove(mb, removed);
                }

                // The pilot owns the ZDO from the spawn (Spawner hands it over last), so this lands on the
                // pilot's machine and rides with the ZDO through saves; a watcher's machine skips it.
                // Not covered in practice: a merchant ADOPTED across a restart. The dedicated server never
                // instantiates him, and the client that does is not his owner when Humanoid.Awake runs (a
                // still-carried one Spawner.Sweep hands to the server's session id; a dropped one is nobody's
                // until the ownership sweep gives him away), so no stamp lands, and the guard runs once per
                // instantiation. Acceptable: SoM's own guard sequence tests 'not
                // tamed' before the exempt key, and CargoMerchant tames him on the owner, so the stamp is a
                // second defence, not the only one. The authoring-time stamp beside Spawner's SetOwner would
                // close it for good; that file is Wu'barrk's, so it is proposed, not written.
                bool stamped = false;
                if (zdo != null && zdo.IsOwner() && zdo.GetInt(SoMExemptKey, 0) != 1)
                {
                    zdo.Set(SoMExemptKey, 1);
                    stamped = true;
                }

                if (removed.Count > 0 || stamped)
                    ValkyriesCargo.Log.LogInfo("merchant guard: " +
                        (removed.Count > 0 ? "removed " + string.Join(", ", removed.ToArray()) + " from Ingvar's clone" : "nothing foreign on Ingvar's clone") +
                        (stamped ? "; " + SoMExemptKey + " stamped" : ""));
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("merchant guard threw: " + ex);
            }
        }

        private static void Remove(Component c, List<string> removed)
        {
            if (c == null) return;
            string name = c.GetType().FullName;
            if (!removed.Contains(name)) removed.Add(name);
            UnityEngine.Object.DestroyImmediate(c);
        }
    }
}
