using System;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Server
{
    /// <summary>
    /// The vanilla RandomEvent the visit rides (design 3.1, 3.6). Registered on EVERY machine, because a
    /// client resolves the server's `SetEvent` broadcast by name from its own list (RandEventSystem.RPC_SetEvent
    /// → SetRandomEventByName → GetEvent(m_events)); an unregistered client would simply never see it.
    ///
    /// What the engine does with it, from the decompile (2026-09-06):
    /// - `m_random = false` keeps it out of `GetPossibleRandomEvents` (checked at RandEventSystem.cs:417), and
    ///   `m_standaloneInterval = 0` keeps it out of the standalone loop; only we start it, by name.
    /// - the server's FixedUpdate runs `Update(...)`: `m_time += dt` while a player is within `m_eventRange` of
    ///   `m_pos` (`m_pauseIfNoPlayerInArea`), and ends it (`SetRandomEvent(null)`) when `m_time > m_duration`.
    /// - every 2 s the server broadcasts name, time and position; a client whose local player is inside the
    ///   range makes it the ACTIVE event: `OnActivate` shows `m_startMessage` once, `OnDeactivate(end)` shows
    ///   `m_endMessage` when the event ended while active.
    /// - `m_cameraShakeCurve` MUST be empty: with keys, `Update` calls `GameCamera.instance.AddShake`, and on a
    ///   dedicated server `GameCamera.instance` is null.
    /// - `m_forceMusic` / `m_forceEnvironment` empty: no raid music, no weather; house rule 4 never touches EnvMan.
    /// </summary>
    public static class CargoEvent
    {
        public const string Name = "valkyries_cargo";

        private static int _failures;
        private static bool _disabledLogged;

        /// <summary>
        /// P10b: the rank-1 probe said the RandomEvent surface is not what this file was written
        /// against. Everything below would then be a silent no-op - vanilla's `SetRandomEventByName`
        /// resolves the name from each machine's OWN list, so a registration that quietly did nothing
        /// looks exactly like a registration that worked. So we do not register, we do not start, and
        /// `cargo status` says the visit is disabled and why.
        /// </summary>
        public static bool Disabled => !EngineProbes.Current.Ok(EngineProbes.RandEvent);

        /// <summary>The probe's own words, or "". What `cargo status` prints beside "disabled".</summary>
        public static string DisabledReason => EngineProbes.Current.Reason(EngineProbes.RandEvent);

        /// <summary>Say it once, wherever the refusal is noticed. Returns true when the event is off.</summary>
        private static bool RefuseOnce(string what)
        {
            if (!Disabled) return false;
            if (!_disabledLogged)
            {
                _disabledLogged = true;
                ValkyriesCargo.Log.LogWarning(
                    "event '" + Name + "' is DISABLED: the engine probe 'randevent' failed (" + DisabledReason +
                    "). No visit can start on this build. Everything else keeps running; `cargo engine` has the detail.");
            }
            ValkyriesCargo.Log.LogDebug("event '" + Name + "': " + what + " skipped, the engine probe failed");
            return true;
        }

        public static bool IsRegistered(RandEventSystem res) => res != null && res.HaveEvent(Name);

        /// <summary>The registered prototype (the list entry `SetRandomEventByName` clones), or null.</summary>
        public static RandomEvent Prototype(RandEventSystem res)
        {
            if (res == null || res.m_events == null) return null;
            foreach (RandomEvent e in res.m_events) if (e != null && e.m_name == Name) return e;
            return null;
        }

        /// <summary>Idempotent; swallows its own failures. A world without our event must never be a world that fails to load.</summary>
        public static void Register(RandEventSystem res)
        {
            try
            {
                if (RefuseOnce("registration")) return;
                if (res == null || res.m_events == null) return;
                if (Prototype(res) != null) return;
                var ev = new RandomEvent
                {
                    m_name = Name,
                    m_enabled = true,
                    m_random = false,
                    m_duration = Lifespan(),
                    m_nearBaseOnly = false,
                    m_pauseIfNoPlayerInArea = true,
                    m_eventRange = 96f,
                    m_standaloneInterval = 0f,
                    m_standaloneChance = 0f,
                    m_spawnerDelay = 0f,
                    m_cameraShakeCurve = new AnimationCurve(),
                    m_biome = Heightmap.Biome.All,
                    m_startMessage = Lines.BannerStart,
                    m_endMessage = Lines.BannerEnd,
                    m_forceMusic = "",
                    m_forceEnvironment = "",
                };
                res.m_events.Add(ev);
                ValkyriesCargo.Log.LogInfo("event '" + Name + "' registered (" + res.m_events.Count + " events now); duration " +
                                           ev.m_duration.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s, pauses with nobody within 96 m, no spawns, no music, no weather.");
            }
            catch (Exception ex)
            {
                if (_failures++ < 3) ValkyriesCargo.Log.LogError("event registration failed: " + ex);
            }
        }

        /// <summary>The configured lifespan; the prototype's duration is refreshed from it before every start.</summary>
        public static float Lifespan()
        {
            float v = ModConfig.MerchantLifespanSeconds != null ? ModConfig.MerchantLifespanSeconds.Value : 300f;
            return Mathf.Clamp(v, 30f, 1800f);
        }

        /// <summary>
        /// Start the event at a point, refreshing the prototype's duration from config first. True if the
        /// engine now reports our event as current; false (logged) otherwise.
        /// </summary>
        public static bool Start(RandEventSystem res, Vector3 pos)
        {
            if (RefuseOnce("start")) return false;
            if (res == null) return false;
            RandomEvent proto = Prototype(res);
            if (proto == null) { Register(res); proto = Prototype(res); if (proto == null) return false; }
            proto.m_duration = Lifespan();
            res.SetRandomEventByName(Name, pos);
            RandomEvent current = res.GetCurrentRandomEvent();
            return current != null && current.m_name == Name;
        }

        /// <summary>Real seconds the running event has left, or -1 when it is not ours or not running.</summary>
        public static double Remaining(RandEventSystem res)
        {
            RandomEvent current = res != null ? res.GetCurrentRandomEvent() : null;
            if (current == null || current.m_name != Name) return -1;
            return Math.Max(0.0, current.m_duration - current.m_time);
        }
    }
}
