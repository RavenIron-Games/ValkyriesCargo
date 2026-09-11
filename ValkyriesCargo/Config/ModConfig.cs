using System.Collections.Generic;
using BepInEx.Configuration;
using RavenIron.ValkyriesCargo.Core;
using ServerSync;

namespace RavenIron.ValkyriesCargo.Config
{
    /// <summary>
    /// The whole config surface, and the ServerSync instance that carries it.
    ///
    /// Server.* entries are read where the world runs and are synced and LOCKED: a client's
    /// local edit is shown read-only and, if sent, rejected by the server unless that client
    /// is on adminlist.txt. Client.* entries are local and never leave the machine. The two
    /// custom values, VisitState and MarketState, are the server's broadcast channels; only
    /// the server writes them (design section 2).
    ///
    /// The version gate: ModRequired with MinimumRequiredVersion == CurrentVersion means a
    /// client without the mod, or on any other version, is refused at handshake with a
    /// message naming both versions. Every client must run this exact build because the
    /// visuals, the terminal and the wire all live in client code.
    /// </summary>
    public static class ModConfig
    {
        public static ConfigSync Sync { get; private set; }

        // ---- Server (synced, locked) ------------------------------------------------------

        public static ConfigEntry<bool>   LockConfiguration;
        public static ConfigEntry<bool>   Enabled;
        public static ConfigEntry<bool>   RequireRested;
        public static ConfigEntry<int>    MinComfortLevel;
        public static ConfigEntry<int>    MinBaseValue;
        public static ConfigEntry<bool>   DaytimeOnly;
        public static ConfigEntry<float>  EventCheckIntervalMinutes;
        public static ConfigEntry<float>  EventChancePercent;
        public static ConfigEntry<float>  PlayerCooldownMinutes;
        public static ConfigEntry<float>  CooldownRadius;
        public static ConfigEntry<float>  MerchantLifespanSeconds;
        public static ConfigEntry<float>  ApproachDistance;
        public static ConfigEntry<string> BodyPrefab;
        public static ConfigEntry<bool>   CustomBody;
        public static ConfigEntry<float>  FlightStartDistance;
        public static ConfigEntry<float>  FlightStartAltitude;
        public static ConfigEntry<float>  FlightDescentDistance;
        public static ConfigEntry<float>  FlightSpeed;
        public static ConfigEntry<float>  FlightTurnRate;
        public static ConfigEntry<string> CarryOffset;
        public static ConfigEntry<string> CatalogueLine;
        public static ConfigEntry<float>  PriceElasticity;
        public static ConfigEntry<float>  MinPriceMultiplier;
        public static ConfigEntry<float>  MaxPriceMultiplier;
        public static ConfigEntry<float>  SpreadBuy;
        public static ConfigEntry<bool>   FairMarketAct;
        public static ConfigEntry<float>  WareHalfLifeGameDays;
        public static ConfigEntry<float>  WantHalfLifeGameDays;
        public static ConfigEntry<int>    ShelfSize;               // the rotating shelf (2026-09-08): 0 = fixed
        public static ConfigEntry<float>  ShelfRotationGameDays;
        public static ConfigEntry<int>    BackpackShelfMultiplier; // the backpack add-on (2026-09-08): shelf x this when the mod is loaded
        public static ConfigEntry<string> BackpackModGuid;
        public static ConfigEntry<int>    PurseCoins;
        public static ConfigEntry<int>    PurseCarryPercent;
        public static ConfigEntry<bool>   EnableBarter;
        public static ConfigEntry<bool>   BarrkBotExport;

        // ---- Client (local) ---------------------------------------------------------------

        public static ConfigEntry<bool>   ShowArrivalMessage;
        public static ConfigEntry<bool>   ShowPriceTrend;
        public static ConfigEntry<string> Theme;
        public static ConfigEntry<float>  TerminalScale;
        public static ConfigEntry<float>  TerminalBackdropAlpha;
        /// <summary>
        /// The half-turn Ingvar's body gets when it is attached to the merchant and stood up by the
        /// preview. On StormTest, 2026-09-07 15:50, the owner watched him WALK BACKWARD on a live visit:
        /// the bundle's forward axis is the Dverger's back (the source is authored on a different axis
        /// convention; the bind-pose box already lies about the up axis for the same reason), and the
        /// loader attached him with identity rotation. A knob rather than a constant so a bake that comes
        /// out the other way round is a config edit, not a rebuild; the attach line logs the value used.
        /// </summary>
        public static ConfigEntry<float>  BodyYawDegrees;

        // ---- Broadcast channels (server writes, everyone reads) ---------------------------

        public static CustomSyncedValue<string> VisitState;
        public static CustomSyncedValue<string> MarketState;

        // ---- Derived --------------------------------------------------------------------

        /// <summary>The parsed catalogue; re-parsed whenever the synced line changes.</summary>
        public static Catalogue CatalogueParsed { get; private set; } = Catalogue.Parse(null, null);

        /// <summary>Every entry the parser refused, in words, for `cargo status`.</summary>
        public static List<string> CatalogueProblems { get; private set; } = new List<string>();

        /// <summary>
        /// Counts up on every re-parse of the synced line (2026-09-07): the director compares it against
        /// the version its market was built from, once a second, and rebuilds the shelf between visits
        /// when they differ. One integer, so a change that arrived by any route - `cargo catalogue`, an
        /// admin's Configuration Manager, a listen host's own file - is noticed the same way.
        /// </summary>
        public static int CatalogueVersion { get; private set; }

        public static void Bind(ConfigFile cfg, string pluginId, string displayName, string version)
        {
            Sync = new ConfigSync(pluginId)
            {
                DisplayName = displayName,
                CurrentVersion = version,
                MinimumRequiredVersion = version,
                ModRequired = true,
            };

            LockConfiguration = cfg.Bind("Server", "LockConfiguration", true,
                "Server enforces every Server.* value on every client. Admins on adminlist.txt may still change them. Read by ServerSync on EVERY machine; the server's value is the one that arrives.");
            Sync.AddLockingConfigEntry(LockConfiguration);

            Enabled = S(cfg, "Server", "Enabled", true,
                "Roll visits at all. Read on the SERVER.");
            RequireRested = S(cfg, "Server", "RequireRested", true,
                "A player must carry the Rested effect to be eligible. Read on the SERVER.");
            MinComfortLevel = S(cfg, "Server", "MinComfortLevel", 4,
                "Minimum comfort level (the number in the Rested tooltip) for eligibility. Vanilla counts 1 outdoors, 2 under a roof, and one step more for each distinct comfort piece within 10 m - a bed, a fire, a chair, a banner (SE_Rested.CalculateComfortLevel). `cargo status` prints what your own character reports. Read on the SERVER.",
                new AcceptableValueRange<int>(0, 20));
            MinBaseValue = S(cfg, "Server", "MinBaseValue", 1,
                "Vanilla base value at the player (workbench/forge coverage); vanilla raids use 3. Read on the SERVER.",
                new AcceptableValueRange<int>(0, 10));
            DaytimeOnly = S(cfg, "Server", "DaytimeOnly", true,
                "Only roll visits by day; the flight is the show. Read on the SERVER.");
            EventCheckIntervalMinutes = S(cfg, "Server", "EventCheckIntervalMinutes", 25f,
                "Real minutes between rolls. Read on the SERVER.",
                new AcceptableValueRange<float>(1f, 240f));
            EventChancePercent = S(cfg, "Server", "EventChancePercent", 25f,
                "Chance per roll when at least one player is eligible. Read on the SERVER.",
                new AcceptableValueRange<float>(0f, 100f));
            PlayerCooldownMinutes = S(cfg, "Server", "PlayerCooldownMinutes", 60f,
                "Real minutes before the same player can be chosen again; stamped at dispatch. Read on the SERVER.",
                new AcceptableValueRange<float>(0f, 1440f));
            CooldownRadius = S(cfg, "Server", "CooldownRadius", 60f,
                "Metres: a base on cooldown blocks its neighbours within this radius. Read on the SERVER.",
                new AcceptableValueRange<float>(0f, 500f));
            MerchantLifespanSeconds = S(cfg, "Server", "MerchantLifespanSeconds", 300f,
                "How long Ingvar stays, as the vanilla random event's duration. Ours alone: Odin's prefab says 60, not the 300 his field initialiser says, so this number was never inherited from him. Read on the SERVER.",
                new AcceptableValueRange<float>(30f, 1800f));
            ApproachDistance = S(cfg, "Server", "ApproachDistance", 3.5f,
                "Metres from the player at which Ingvar stops walking up (P5, CargoMerchant). Read on the CLIENT that owns the merchant, synced from the server.",
                new AcceptableValueRange<float>(1f, 10f));
            BodyPrefab = S(cfg, "Server", "BodyPrefab", "Dverger",
                "The engine creature prefab the merchant is CLONED FROM, for good: Character, MonsterAI and the collider all come from it. Must have a Humanoid, a MonsterAI and an Animator. This is not the custom-body switch - that is CustomBody. Read on the SERVER.");
            CustomBody = S(cfg, "Server", "CustomBody", true,
                "Put Ingvar's own body on the BodyPrefab clone from the AssetBundle embedded in this DLL. False keeps the Dverger stand-in visible, and so does a build with no bundle embedded (`cargo body` says which). This is the switch, NOT BodyPrefab: BodyPrefab stays the engine prefab the merchant is cloned from, because Character, MonsterAI and the collider all come from it. Read on the CLIENT, synced from the server; a dedicated server never reads it.");
            FlightStartDistance = S(cfg, "Server", "FlightStartDistance", 90f,
                "Metres from the pilot where the Valkyrie appears; shrunk at runtime, 12 m at a time, until the start fits inside the pilot's active zone block. The floor is FlightPlan.MinimumStartDistance (30 m): below that the bird would appear on top of the player. Read on the SERVER.",
                new AcceptableValueRange<float>(30f, 200f));
            FlightStartAltitude = S(cfg, "Server", "FlightStartAltitude", 120f,
                "Altitude of the Valkyrie's start point, metres above the drop. Read on the SERVER.",
                new AcceptableValueRange<float>(30f, 400f));
            FlightDescentDistance = S(cfg, "Server", "FlightDescentDistance", 50f,
                "Metres out at which the descent leg begins. Read on the SERVER.",
                new AcceptableValueRange<float>(10f, 200f));
            // Ours, not the prefab's. Vanilla's Valkyrie is tuned for a 500 m approach: at its 20 m/s
            // our 76 m run is over in seven seconds, where design 3.2 asks for fifteen to twenty. At 8
            // the same flight takes 17 s. The turn rate is ours for the same reason, and because the
            // prefab's 20 deg/s is a 57 m turning circle - wider than the whole approach (PR #8).
            FlightSpeed = S(cfg, "Server", "FlightSpeed", 8f,
                "Metres a second the Valkyrie flies, overriding the prefab's own speed. 8 gives design 3.2's 15-20 s of sky over a 90 m approach. Read on the CLIENT that owns the bird (CargoFlight.Awake), synced from the server; the server never reads it.",
                new AcceptableValueRange<float>(2f, 40f));
            FlightTurnRate = S(cfg, "Server", "FlightTurnRate", 45f,
                "Degrees a second the Valkyrie may turn, overriding the prefab's own. Read on the CLIENT that owns the bird, synced from the server; the server never reads it.",
                new AcceptableValueRange<float>(5f, 360f));
            // Where he hangs under the bird. Vanilla's own offset carries a PLAYER in the intro and
            // Ingvar is a head shorter, so the prefab's numbers read as dangling. (0, 0, 0) - his feet on
            // the talon - was tuned live on a machine 2026-09-11 and is what ships; empty still follows the
            // prefab. Read on EVERY machine that has him instanced: the pin runs there, and every screen
            // must agree.
            CarryOffset = S(cfg, "Server", "CarryOffset", Core.CarryOffset.TunedDefault,
                "Where the merchant hangs while the Valkyrie carries him: three numbers 'x, y, z' in the TALON's own space, SUBTRACTED from the talon. The default '0, 0, 0' holds his feet on the talon itself, tuned on a machine for Ingvar's height; bigger numbers hang him further off it (y below, z behind) and negative ones carry him past it. Leave it EMPTY to follow the Valkyrie prefab's own offset instead, (0, 0.3, 0.4) on the shipped bird, which is vanilla's framing for a full-height player. Each axis is bounded at 5 m, and a value past that or any text that is not three numbers is refused with one log line and the prefab's used instead. Read on EVERY machine that has him instanced, synced from the server; it can be changed while he is in the air and lands on the next physics step.");
            CatalogueLine = S(cfg, "Server", "Catalogue", Catalogue.DefaultLine,
                "What Ingvar sells and buys: Prefab:BasePrice:TargetStock:MaxStock:Kind entries separated by commas; Kind is Ware (sells and buys back) or Want (buys only). Every number's reason is in docs/CATALOGUE.md. A prefab this game has no item for is dropped with one log line when the shelf is built (at boot, and on every live edit). Editable on a running server: `cargo catalogue add|remove|reset` (admin), or Configuration Manager as an admin; a change applies as soon as no visit is running. Read on the SERVER.");
            PriceElasticity = S(cfg, "Server", "PriceElasticity", 0.35f,
                "Exponent of (target / stock) in the price; higher = steeper. Read on the SERVER.",
                new AcceptableValueRange<float>(0.05f, 1.5f));
            MinPriceMultiplier = S(cfg, "Server", "MinPriceMultiplier", 0.4f,
                "Floor on the price multiplier. A GUARD for an edited catalogue, not a price a player will see: " +
                "every shipped row has MaxStock = 3 x TargetStock, so the lowest multiplier trading can reach is " +
                "(1/3)^Elasticity = 0.68, and this floor would need MaxStock above 13.7 x target to bind at all. " +
                "If you want a real flooded-out floor, raise MaxStock in the catalogue, not this. Read on the SERVER.",
                new AcceptableValueRange<float>(0.05f, 1f));
            MaxPriceMultiplier = S(cfg, "Server", "MaxPriceMultiplier", 3f,
                "Ceiling on the price multiplier when he is out. Read on the SERVER.",
                new AcceptableValueRange<float>(1f, 10f));
            SpreadBuy = S(cfg, "Server", "SpreadBuy", 0.7f,
                "What he pays as a fraction of what he charges for the same item. Read on the SERVER.",
                new AcceptableValueRange<float>(0.1f, 1f));
            FairMarketAct = S(cfg, "Server", "FairMarketAct", true,
                "Caps what he pays to buy back a Ware at par (base x SpreadBuy), so MaxPriceMultiplier x SpreadBuy > 1 can never turn buying a shelf out and selling it straight back into free coins. Read on the SERVER.");
            WareHalfLifeGameDays = S(cfg, "Server", "WareHalfLifeGameDays", 0f,
                "Between visits a WARE's stock (what he sells) drifts back toward its target with this half-life, in game days; a game day is 30 real minutes of server uptime. 0 = never: the shelf keeps exactly what trading left, so what he has to sell is what players sold him plus what an admin's target says (cargo catalogue add Prefab:Base:Target:Max:Ware raises the level that holds). The shipped 0 is the owner's 2026-09-07 decision; the sweep behind it is docs/ECONOMY-SIM.md section 10. Read on the SERVER.",
                new AcceptableValueRange<float>(0f, 365f));
            WantHalfLifeGameDays = S(cfg, "Server", "WantHalfLifeGameDays", 3f,
                "Between visits a WANT's stock (what he only buys) drifts back toward its target with this half-life, in game days: he passes on what he was sold, so a flooded row half-clears in this many days and he never fills up for good. 0 = never, and then every Want fills to its max and he stops buying it (docs/ECONOMY-SIM.md section 10: 27 of 30 supplying visits refused). Read on the SERVER.",
                new AcceptableValueRange<float>(0f, 365f));
            ShelfSize = S(cfg, "Server", "ShelfSize", 20,
                "The rotating shelf (2026-09-08, issue #56). How many catalogue entries he SELLS at a time, drawn from the whole catalogue - Wares and Wants alike - by a roll every ShelfRotationGameDays; an entry on the shelf is sold at the curve, bought back under the Fair Market Act and drifts on WareHalfLifeGameDays, every other entry is bought only and drifts on WantHalfLifeGameDays. The roll is a function of the world, the game time and the catalogue, so a restart shows the same shelf. 0 = the fixed shelf: the Ware/Want kinds in the Catalogue line decide, as they did before. Changing it with a visit running applies when the visit ends. Read on the SERVER.",
                new AcceptableValueRange<int>(0, 200));
            ShelfRotationGameDays = S(cfg, "Server", "ShelfRotationGameDays", 2f,
                "How many game days one shelf lasts before it is re-rolled; a game day is 30 real minutes of server uptime with somebody online. The roll happens on the first tick of a new period with no visit running, never under an open terminal. Read on the SERVER.",
                new AcceptableValueRange<float>(0.1f, 365f));
            BackpackShelfMultiplier = S(cfg, "Server", "BackpackShelfMultiplier", 2,
                "The backpack add-on (2026-09-08). When the backpack mod named by BackpackModGuid is loaded on the server, the shelf is ShelfSize times this, capped at 200 and at the catalogue: players who can carry more get more to buy. Nothing changes without the mod, and 0 in ShelfSize stays the fixed shelf. Read on the SERVER.",
                new AcceptableValueRange<int>(1, 4));
            BackpackModGuid = S(cfg, "Server", "BackpackModGuid", "org.bepinex.plugins.backpacks",
                "The BepInEx GUID of the backpack mod to look for (Smoothbrain's Backpacks by default), looked up on the server at director up and once a second after, so it can be changed live. Empty = never look. Read on the SERVER.");
            PurseCoins = S(cfg, "Server", "PurseCoins", 1500,
                "Coins he arrives with. 800 was thin for what this mod is for: one visit bought 39 silver ore " +
                "for 795 and left him with 5, and a dozen flametal ore was the whole purse. Read on the SERVER.",
                new AcceptableValueRange<int>(0, 100000));
            PurseCarryPercent = S(cfg, "Server", "PurseCarryPercent", 50,
                "Percent of the coins last visit took IN -- gross, not net -- added to the next purse, capped at " +
                "three purses. Measured on the gross so a visit where players sold him as much as they bought " +
                "still carries something forward; on the net it carried nothing, and that is precisely the " +
                "supplying server the catalogue was written for. Read on the SERVER.",
                new AcceptableValueRange<int>(0, 100));
            EnableBarter = S(cfg, "Server", "EnableBarter", true,
                "Allow paying with goods he wants, valued at his live buy price. False hides the terminal's Barter button; the server settles a barter deal either way. Read on the CLIENT, synced from the server.");
            // PriceChangePolicy is deliberately NOT bound. It was a synced, locked knob offering a choice
            // between Reconfirm and Teardown that NOTHING read: the terminal implements Reconfirm and only
            // Reconfirm (`Client/Terminal/TrayModel.cs`). A setting that promises a behaviour the code does
            // not have is worse than no setting, because a server owner will set it and believe it. 0.1.0 is
            // unreleased, so removing it costs nobody a migration. If Teardown is ever built, bind it then.
            BarrkBotExport = S(cfg, "Server", "BarrkBotExport", true,
                "Write barrkbot_cargo_market.json, barrkbot_cargo_traders.json and barrkbot_cargo_visits.json under BepInEx/config/ValkyriesCargo/ for BarrkBOT to read off the server filesystem (BARRKBOT_CONTRACT.md), refreshed about once a minute. Never the source of truth: the world sidecar always saves first. Read on the SERVER.");

            ShowArrivalMessage = C(cfg, "Client", "ShowArrivalMessage", true,
                "Show the private 'wings beat in the upper skies' line when you are the chosen player. Read on the CLIENT.");
            ShowPriceTrend = C(cfg, "Client", "ShowPriceTrend", true,
                "Show the up/down glyph against base price in the terminal. Read on the CLIENT.");
            Theme = C(cfg, "Client", "Theme", "Vanilla",
                "Terminal metal colour. Read on the CLIENT.",
                new AcceptableValueList<string>("Vanilla", "BlackGold"));
            TerminalScale = C(cfg, "Client", "TerminalScale", 1f,
                "Terminal size multiplier. Read on the CLIENT.",
                new AcceptableValueRange<float>(0.5f, 2f));
            TerminalBackdropAlpha = C(cfg, "Client", "TerminalBackdropAlpha", 0.4f,
                "Opacity of the black backdrop behind the terminal's text: 0.4 is a 40% translucent black (the playtest's ask, 2026-09-08), " +
                "1 is the solid panel of before, 0 is the frame alone over the world. Read on the CLIENT.",
                new AcceptableValueRange<float>(0f, 1f));
            BodyYawDegrees = C(cfg, "Client", "BodyYawDegrees", 180f,
                "Degrees Ingvar's body is turned about the vertical when it is attached to the merchant (and in `cargo body preview`). " +
                "180 because the shipped bundle's forward axis faces the Dverger's back, so with 0 he walks backward (seen 2026-09-07). " +
                "Set 0 for a bake that comes out facing forward. Read on the CLIENT.",
                new AcceptableValueRange<float>(-180f, 180f));

            VisitState  = new CustomSyncedValue<string>(Sync, "visit", "");
            MarketState = new CustomSyncedValue<string>(Sync, "market", "");

            // The channels feed the client-side surface the terminal reads (design WORKSPLIT §2).
            // ServerSync raises ValueChanged on every server write and on the initial sync at login.
            VisitState.ValueChanged  += () => Net.CargoRpc.PublishVisit(VisitState.Value);
            MarketState.ValueChanged += () => Net.CargoRpc.PublishMarket(MarketState.Value);

            ReparseCatalogue();
            CatalogueLine.SettingChanged += (_, __) => ReparseCatalogue();
        }

        // ---- The pure cores' rule bags, filled from the live entries (the director refreshes them once a second) ----

        /// <summary>A MarketRules from the Server.* entries; the day length comes from the engine, not config.</summary>
        public static MarketRules BuildMarketRules(double secondsPerGameDay, List<string> problems)
        {
            var r = new MarketRules { SecondsPerGameDay = secondsPerGameDay };
            FillMarketRules(r, problems);
            return r;
        }

        public static void FillMarketRules(MarketRules r, List<string> problems)
        {
            if (r == null) return;
            r.Elasticity = PriceElasticity.Value;
            r.MinMultiplier = MinPriceMultiplier.Value;
            r.MaxMultiplier = MaxPriceMultiplier.Value;
            r.Spread = SpreadBuy.Value;
            r.FairMarketAct = FairMarketAct.Value;
            r.WareHalfLifeGameDays = WareHalfLifeGameDays.Value;
            r.WantHalfLifeGameDays = WantHalfLifeGameDays.Value;
            // The backpack add-on: the shelf the mod ships is ShelfSize times the multiplier when the named
            // backpack mod is loaded here (Server/BackpackMod.cs decides that, at director up and every tick).
            r.ShelfSize = Shelf.Scaled(ShelfSize.Value, BackpackShelfMultiplier.Value, Server.BackpackMod.Present);
            r.ShelfRotationGameDays = ShelfRotationGameDays.Value;
            r.PurseCoins = PurseCoins.Value;
            r.PurseCarryPercent = PurseCarryPercent.Value;
            r.PurseCapMultiple = 3;
            r.Sanitize(problems);
        }

        /// <summary>The backpack multiplier as the shelf applies it (clamped the way `Shelf.Scaled` clamps), for the log and `cargo status`.</summary>
        public static int EffectiveBackpackMultiplier()
            => BackpackShelfMultiplier == null ? 1 : System.Math.Max(1, System.Math.Min(BackpackShelfMultiplier.Value, Shelf.MaxBackpackMultiplier));

        public static SchedulerRules BuildSchedulerRules(List<string> problems)
        {
            var r = new SchedulerRules();
            FillSchedulerRules(r, problems);
            return r;
        }

        public static void FillSchedulerRules(SchedulerRules r, List<string> problems)
        {
            if (r == null) return;
            r.Enabled = Enabled.Value;
            r.RequireRested = RequireRested.Value;
            r.DaytimeOnly = DaytimeOnly.Value;
            r.MinComfort = MinComfortLevel.Value;
            r.MinBaseValue = MinBaseValue.Value;
            r.IntervalSeconds = EventCheckIntervalMinutes.Value * 60f;
            r.ChancePercent = EventChancePercent.Value;
            r.PlayerCooldownSeconds = PlayerCooldownMinutes.Value * 60f;
            r.CooldownRadius = CooldownRadius.Value;
            r.TownRadius = 40f;   // design 3.1: candidates within 40 m are one ticket; not a knob
            r.Sanitize(problems);
        }

        private static void ReparseCatalogue()
        {
            var problems = new List<string>();
            CatalogueParsed = Catalogue.Parse(CatalogueLine.Value, problems);
            CatalogueProblems = problems;
            CatalogueVersion++;
        }

        // Synced: goes to every client and is locked by LockConfiguration.
        private static ConfigEntry<T> S<T>(ConfigFile cfg, string section, string key, T def, string desc,
                                           AcceptableValueBase range = null)
        {
            ConfigEntry<T> e = range == null
                ? cfg.Bind(section, key, def, desc)
                : cfg.Bind(section, key, def, new ConfigDescription(desc, range));
            Sync.AddConfigEntry(e);
            return e;
        }

        // Local: bound so it shows in config managers, never synchronised.
        private static ConfigEntry<T> C<T>(ConfigFile cfg, string section, string key, T def, string desc,
                                           AcceptableValueBase range = null)
        {
            ConfigEntry<T> e = range == null
                ? cfg.Bind(section, key, def, desc)
                : cfg.Bind(section, key, def, new ConfigDescription(desc, range));
            Sync.AddConfigEntry(e).SynchronizedConfig = false;
            return e;
        }
    }
}
