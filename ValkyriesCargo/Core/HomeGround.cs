using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Whether the ground a candidate is standing on is somewhere Ingvar should be dropped: a place a
    /// PLAYER built, and not another merchant's camp or the mouth of a dungeon.
    ///
    /// **Why this file exists (issue #79, 2026-09-14, from a player: "Little man shows up at the
    /// Bogwitch if you happen to be there when he appears").** The visit gate asked seven things and
    /// none of them asked whose base it was. `baseValue` is vanilla's own number and vanilla computes
    /// it as `EffectArea.GetBaseValue(position, 20f)` - a count of PlayerBase-flagged effect areas
    /// within 20 m with NO ownership test of any kind. So any sheltered, comfortable spot passed every
    /// gate, including the game's own NPC camps. The Bog Witch was the one that got reported; Haldor's
    /// camp and any ruin with a fire in it are the same shape. The store page promises "beside your
    /// hearth" and "at your own fire" four times over, so the reporter's expectation was the one we
    /// wrote.
    ///
    /// TWO TESTS, because neither alone is enough.
    ///
    /// 1. **Somebody built here.** A piece the game placed as part of a location carries `creator == 0`;
    ///    a piece a player placed carries their id (`Piece.IsPlacedByPlayer`). So "is there a
    ///    player-built piece within the same 20 m vanilla measures baseValue over" excludes the Bog
    ///    Witch's camp outright. Deliberately NOT "did YOU build it" (`Piece.IsCreator`): on a shared
    ///    server one player builds the hall and the rest live in it, and keying on the builder would
    ///    mean only the builder ever got a visit. The client reports this the way it already reports
    ///    comfort, on its own ZDO.
    /// 2. **Not at a merchant's camp, and not at a dungeon's door.** Test 1 still passes if somebody
    ///    plants a workbench beside the Bog Witch, and a Valkyrie dropping a rival merchant into her
    ///    camp is the complaint. The first cut of this rule refused EVERY location the game owns, and
    ///    the live test on 2026-09-15 showed why that was wrong: the owner's own base, built on a
    ///    Meadows `WoodHouse3` ruin - one of the most common first bases in the game - was refused
    ///    too. So the rule names its reasons. A location counts when it holds a `Trader` (Haldor,
    ///    Hildir, the Bog Witch, any modded merchant - no name list) or when it has an interior
    ///    (every crypt, cave, mine and fortress). A ruin, a runestone, a stone circle: yours to build
    ///    on, yours to be visited at. The location's own exterior radius is what "at" means, plus a
    ///    small clearance so he is not dropped on the boundary fence; that is what `RingOffsets` is.
    ///
    /// Everything here is pure arithmetic and words. The engine reads are `LocationsLive.Read` (on
    /// the server for the gate, on the client for `cargo status`) and `Piece.GetAllPiecesInRadius`
    /// on the client.
    /// </summary>
    public static class HomeGround
    {
        // --- the built-base radius -------------------------------------------------------------

        /// <summary>
        /// 20 m, which is not a taste call: it is the radius vanilla itself measures `baseValue` over
        /// (`Player.UpdateBaseValue` -> `EffectArea.GetBaseValue(position, 20f)`). Matching it means the
        /// two gates agree about how far "here" reaches, so a player can never sit in the strange band
        /// where the game says they are at a base and we say they are not.
        /// </summary>
        public const float DefaultBuiltRadius = 20f;

        /// <summary>Below this a doorway would fail its own house.</summary>
        public const float MinBuiltRadius = 4f;

        /// <summary>
        /// One zone. The client scans every instanced piece to answer this, so the radius is bounded
        /// for the same reason the scan is on its own slow timer.
        /// </summary>
        public const float MaxBuiltRadius = 64f;

        // --- the clearance added to a location's own radius -------------------------------------

        /// <summary>
        /// The margin around the player that is also asked "is this a merchant's camp": the location's
        /// own radius does the work, and this only keeps Ingvar from being dropped on the boundary
        /// fence. Small on purpose.
        /// </summary>
        public const float DefaultClearance = 8f;

        public const float MinClearance = 0f;

        /// <summary>
        /// Past this the clearance would stop being a margin and start being a search, and the eight
        /// sample points of the ring would have gaps a whole camp could hide in. The knob stops before
        /// it can lie.
        /// </summary>
        public const float MaxClearance = 64f;

        public static float ClampBuiltRadius(float r, List<string> problems)
        {
            if (r < MinBuiltRadius)
            {
                Wire.Report(problems, "BuiltBaseRadius clamped to " + Wire.Float(MinBuiltRadius));
                return MinBuiltRadius;
            }
            if (r > MaxBuiltRadius)
            {
                Wire.Report(problems, "BuiltBaseRadius clamped to " + Wire.Float(MaxBuiltRadius));
                return MaxBuiltRadius;
            }
            return r;
        }

        public static float ClampClearance(float c, List<string> problems)
        {
            if (c < MinClearance)
            {
                Wire.Report(problems, "LocationClearance clamped to " + Wire.Float(MinClearance));
                return MinClearance;
            }
            if (c > MaxClearance)
            {
                Wire.Report(problems, "LocationClearance clamped to " + Wire.Float(MaxClearance));
                return MaxClearance;
            }
            return c;
        }

        // --- the ring ---------------------------------------------------------------------------

        /// <summary>
        /// Eight points on a ring `clearance` metres out from the player - the compass points and the
        /// diagonals - as (x, z) pairs, flattened. The engine's own `Location.GetLocation(point)` says
        /// which location a POINT is inside; asking it at the player and at these eight is how "inside,
        /// or within the clearance of" is answered without naming the engine's private location list.
        /// Empty when the clearance is 0: then the player's own point is the whole question.
        /// </summary>
        public static float[] RingOffsets(float clearance)
        {
            if (clearance <= 0f) return new float[0];
            float d = clearance * 0.70710678f;
            return new float[]
            {
                clearance, 0f,   -clearance, 0f,   0f, clearance,   0f, -clearance,
                d, d,   d, -d,   -d, d,   -d, -d,
            };
        }

        // --- the kinds, and the words ------------------------------------------------------------

        /// <summary>A location holding a `Trader`. Haldor, Hildir, the Bog Witch, any modded merchant.</summary>
        public const string MerchantCamp = "a merchant's camp";

        /// <summary>A location with an interior: every crypt, cave, mine and fortress.</summary>
        public const string DungeonEntrance = "a dungeon entrance";

        /// <summary>
        /// The refusal a candidate standing on ground nobody built gets, in the shape the roll line
        /// already speaks. `radius` is the one actually in force after clamping, not the configured one,
        /// so a clamped value never reads back as the number somebody typed.
        /// </summary>
        public static string NoBuiltBaseReason(float radius)
        {
            return "nothing player-built within " + Wire.Float(radius) + " m";
        }

        /// <summary>
        /// "Hildir_camp, a merchant's camp". The location is NAMED, because "at a location" with no
        /// name is the kind of line that costs somebody an evening; an empty or missing name degrades
        /// to the honest "an unnamed location" rather than to a blank. The kind is one of the two
        /// constants above, or empty, and says WHY this location counts.
        /// </summary>
        public static string LocationLabel(string locationName, string kind)
        {
            string n = locationName == null ? "" : locationName.Trim();
            if (n.Length == 0) n = "an unnamed location";
            string k = kind == null ? "" : kind.Trim();
            return k.Length == 0 ? n : n + ", " + k;
        }

        /// <summary>The refusal for standing at one: "inside Hildir_camp, a merchant's camp".</summary>
        public static string InsideLocationReason(string label)
        {
            string l = label == null ? "" : label.Trim();
            if (l.Length == 0) l = "an unnamed location";
            return "inside " + l;
        }
    }
}
