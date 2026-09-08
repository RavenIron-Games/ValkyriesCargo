namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Ingvar's words (design section 7). Text lives here and only here; what crosses the wire is an
    /// index or a phase, never a string, so every client says the same thing from its own copy. PURE.
    /// </summary>
    public static class Lines
    {
        public const string Title = "Ingvar the Far-Travelled";
        public const string BannerStart = "Valkyrie's Cargo has landed";
        public const string BannerEnd = "Ingvar has gone back to the mist";
        public const string PilotDispatch = "Wings beat in the upper skies... an emissary from Asgard descends.";
        public const string Open = "Ah, the sweet smoke of a well-earned hearth. What do you bring, and what do you need?";
        public const string PriceChanged = "The wind shifted while you counted. Say yes again and it's done.";
        public const string OneMinute = "Hurry your bargaining, friend! The Valkyrie's horn sounds in the wind.";
        public const string DismissFirst = "Send me off, then? Ask once more and I'll go.";
        public const string Farewell = "The Allfather calls me back to the mist!";

        public static readonly string[] Arrival =
        {
            "Hail, hearth-keeper! Ingvar the Far-Travelled, down from the Bifrost with cargo from nine realms!",
            "By Odin's ravens, that Valkyrie has never heard of a soft landing! Well met, warrior. Ingvar, at your service.",
            "The wandering trader is come! Five minutes of the Allfather's patience, and then I am mist again.",
            "From Yggdrasil's high branches to your front gate. Make it quick, my escort circles overhead.",
        };

        public static readonly string[] Buy = { "Sold, and may it serve you.", "A fair price, for today.", "Scarce goods, dear goods." };
        public static readonly string[] Sell = { "I'll take those.", "Enough of these and I'll stop paying, mind.", "The Plains will want that." };

        public const string RefuseFull = "I've all the linen a man can carry.";
        public const string RefusePurse = "My purse is bare, friend.";
        public const string RefuseUnknown = "That I do not deal in.";

        /// <summary>
        /// The table `VCargo_say` indexes (design 3.6: an index crosses the wire, never text, so a line
        /// can be reworded in a patch without a protocol change and a hostile client cannot make
        /// Ingvar say anything he does not already know). Append only - an index that moves changes
        /// what an old client hears. The seeded arrival lines live here too (F3, audit 2026-09-07), at
        /// `Say.ArrivalBase` and up, so the SAME `RPC_Say(index)` handler that already carries the
        /// reactions can carry the arrival callout - one wire index space for everything Ingvar says,
        /// rather than the arrival line reaching every screen by a different mechanism than the rest.
        /// </summary>
        public static readonly string[] Says =
        {
            Open,           // 0
            PriceChanged,   // 1
            OneMinute,      // 2
            DismissFirst,   // 3
            Farewell,       // 4
            RefuseFull,     // 5
            RefusePurse,    // 6
            RefuseUnknown,  // 7
            Arrival[0],     // 8  = Say.ArrivalBase
            Arrival[1],     // 9
            Arrival[2],     // 10
            Arrival[3],     // 11
        };

        public static class Say
        {
            public const int Open = 0;
            public const int PriceChanged = 1;
            public const int OneMinute = 2;
            public const int DismissFirst = 3;
            public const int Farewell = 4;
            public const int RefuseFull = 5;
            public const int RefusePurse = 6;
            public const int RefuseUnknown = 7;

            /// <summary>The first of the `Arrival` lines: index `ArrivalBase + i` is `Arrival[i]`.</summary>
            public const int ArrivalBase = 8;
        }

        /// <summary>One line by index, or empty for an index this build does not know.</summary>
        public static string Reaction(int index)
            => index >= 0 && index < Says.Length ? Says[index] : "";

        /// <summary>
        /// True for an index naming one of the seeded arrival lines (F3): the only ones `RPC_Say` plays
        /// large and greets to, exactly as the old direct `ArrivalFor` call site used to.
        /// </summary>
        public static bool IsArrival(int index)
            => index >= Say.ArrivalBase && index < Say.ArrivalBase + Arrival.Length;

        /// <summary>The arrival line a visit's seed picks; the same on every client.</summary>
        public static string ArrivalFor(int seed) => Pick(Arrival, seed);

        /// <summary>
        /// The SAME line as `ArrivalFor`, as a stable `Says` index instead of text - what actually
        /// crosses the wire in `Keys.Say`'s payload (F3): design 7 keeps text off the wire even for the
        /// one line whose pick depends on the seed, exactly like everything else here.
        /// </summary>
        public static int ArrivalIndexFor(int seed) => Say.ArrivalBase + PickIndex(Arrival.Length, seed);

        public static string BuyFor(long nonce) => Pick(Buy, (int)(nonce & 0x7fffffff));
        public static string SellFor(long nonce) => Pick(Sell, (int)(nonce & 0x7fffffff));

        /// <summary>What he says to a refusal, by reason token; the token itself for the ones he has no words for.</summary>
        public static string Refusal(string reason)
        {
            switch (reason)
            {
                case DealReason.OverMax: return RefuseFull;
                case DealReason.PurseEmpty: return RefusePurse;
                case DealReason.UnknownItem: return RefuseUnknown;
                case DealReason.SoldOut: return "That shelf is bare.";
                case DealReason.NotOnShelf: return "Not in this load, friend. Ask me again in a few days.";
                case DealReason.CoinsShort: return "Your purse is lighter than that.";
                case DealReason.InventoryFull: return "You have no room to carry it.";
                case DealReason.VisitOver: return "The Valkyrie is already circling; the bargaining is done.";
                case DealReason.StaleVisit: return "That was another visit, friend.";
                case DealReason.NotConnected: return "No word reaches him from here.";
                default: return reason ?? "";
            }
        }

        private static string Pick(string[] table, int seed) => table[PickIndex(table.Length, seed)];

        /// <summary>`seed % length`, folded back positive - a negative seed still lands inside the table.</summary>
        private static int PickIndex(int length, int seed)
        {
            int i = seed % length;
            if (i < 0) i += length;
            return i;
        }
    }
}
