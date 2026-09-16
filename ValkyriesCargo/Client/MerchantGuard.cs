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
    /// length of the visit he was a hungry, commandable, breedable pet that eats coins off the ground.
    /// The shipped Dverger carries none of this, so anything of the kind on OUR clone came from a mod
    /// that meant it for wild Dvergr, not for him.
    ///
    /// Runs once, on the client, from the <c>Humanoid.Awake</c> postfix that makes the clone Ingvar, and
    /// BEFORE <c>CargoMerchant</c> is added: the removed components are gone before our own
    /// <c>SetTamed</c> fires, so a taming mod's SetTamed patches (they key on their genetics component)
    /// never count him as a tame. Everything here touches our own spawned object and nothing else: not
    /// the prefab, not anyone's creature. <c>Object.Destroy</c> is deferred to the end of the frame, which
    /// is fine - Unity's null semantics make a destroyed component read as missing to every
    /// <c>GetComponent</c> and every cached reference after that, and a component's InvokeRepeating and
    /// coroutines die with it.
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
        /// Strip what other mods put on the clone for their Dvergr, empty his consume list, and stamp the
        /// SoM opt-out if this machine owns the ZDO. <paramref name="zdo"/> may be the ZNetView's or the
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

                // A taming mod feeds its Dvergr from the ground (coins, meat) through MonsterAI's consume
                // list on the prefab. The vanilla Dverger eats nothing, and neither does Ingvar.
                bool fed = false;
                MonsterAI ai = go.GetComponent<MonsterAI>();
                if (ai != null && ai.m_consumeItems != null && ai.m_consumeItems.Count > 0)
                {
                    ai.m_consumeItems = new List<ItemDrop>();
                    fed = true;
                }

                // The pilot owns the ZDO from the spawn (Spawner hands it over last), so this lands on the
                // pilot's machine and rides with the ZDO through saves; a watcher's machine skips it.
                bool stamped = false;
                if (zdo != null && zdo.IsOwner() && zdo.GetInt(SoMExemptKey, 0) != 1)
                {
                    zdo.Set(SoMExemptKey, 1);
                    stamped = true;
                }

                if (removed.Count > 0 || fed || stamped)
                    ValkyriesCargo.Log.LogInfo("merchant guard: " +
                        (removed.Count > 0 ? "removed " + string.Join(", ", removed.ToArray()) + " from Ingvar's clone" : "nothing foreign on Ingvar's clone") +
                        (fed ? "; his consume list emptied" : "") +
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
            UnityEngine.Object.Destroy(c);
        }
    }
}
