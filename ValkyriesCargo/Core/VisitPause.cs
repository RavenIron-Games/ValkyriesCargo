namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Whether the running visit's clock stands still, PURE so it is provable off-game.
    ///
    /// The owner's finding, 2026-09-16: a visit whose pilot walked off or logged out never ended, because
    /// the vanilla event it rides was registered with `m_pauseIfNoPlayerInArea` on and the engine adds no
    /// time to such an event while nobody is within 96 m of it - and one random event at a time means an
    /// open visit also held every raid and every later visit. The decision: the clock runs whoever is or is
    /// not near him, on the server's own real time (the engine's FixedUpdate, which has no players-online
    /// gate), so a visit ends its lifespan after it began, full stop. The ONE exception is the owner's
    /// option `Server.PauseVisitWhenEmpty` (off by default): an EMPTY server holds the clock, so a visit
    /// started at bedtime is still there in the morning. "Empty" is `ZNet.GetNrOfPlayers() == 0`, the same
    /// count the engine freezes the world clock on; a listen host counts itself, so the option never engages
    /// there. Distance never enters into it any more. With nobody online the director also stops mirroring
    /// the clock to VisitState (nothing to mirror; the first tick with a player back retargets it once).
    /// </summary>
    public static class VisitPause
    {
        /// <summary>True when the clock should stand still: the option is on and nobody is online.</summary>
        public static bool ShouldPause(bool pauseWhenEmpty, int playersOnline) => pauseWhenEmpty && playersOnline <= 0;

        /// <summary>The log line for a change of state.</summary>
        public static string Describe(bool paused, int playersOnline) => paused
            ? "clock paused: nobody online (Server.PauseVisitWhenEmpty)"
            : "clock running: " + Wire.Int(playersOnline) + " online";
    }
}
