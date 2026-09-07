namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Ingvar's words (design section 7). Text lives here and only here; what crosses the wire is an
    /// index or a phase, never a string, so every client says the same thing from its own copy. PURE.
    /// </summary>
    public static class Lines
    {
        public const string BannerStart = "Valkyrie's Cargo has landed";
        public const string BannerEnd = "Ingvar has gone back to the mist";
        public const string PilotDispatch = "Wings beat in the upper skies... an emissary from Asgard descends.";
        public const string OneMinute = "Hurry your bargaining, friend! The Valkyrie's horn sounds in the wind.";
        public const string Farewell = "The Allfather calls me back to the mist!";

        public static readonly string[] Arrival =
        {
            "Hail, hearth-keeper! Ingvar the Far-Travelled, down from the Bifrost with cargo from nine realms!",
            "By Odin's ravens, that Valkyrie has never heard of a soft landing! Well met, warrior. Ingvar, at your service.",
            "The wandering trader is come! Five minutes of the Allfather's patience, and then I am mist again.",
            "From Yggdrasil's high branches to your front gate. Make it quick, my escort circles overhead.",
        };

        /// <summary>The arrival line a visit's seed picks; the same on every client.</summary>
        public static string ArrivalFor(int seed)
        {
            int i = seed % Arrival.Length;
            if (i < 0) i += Arrival.Length;
            return Arrival[i];
        }
    }
}
