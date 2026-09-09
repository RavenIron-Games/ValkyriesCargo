namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Every ZDO key and RPC name this mod owns, in the one place that types the literal (issue #16,
    /// GitHub, Thorium/Ross, 2026-09-07). Before this file the same string was typed once per call site -
    /// a ZDO key here, a `ZRpc.Register` there - and two of those spellings could drift apart with no
    /// build error to catch it: `ZRpc.Register`/`ZRoutedRpc.Register` replace a handler by name with no
    /// error on a mismatch, and a ZDO key is hashed by `GetStableHashCode()`, so a typo is just a
    /// different, silently wrong, int. One place to change is also one place to prove distinct: see
    /// `KeysTests` in the off-game harness, which asserts every constant here is non-empty, carries the
    /// `VCargo_` prefix, and collides with none of the others.
    ///
    /// The prefix itself is `VCargo_`, not the original `vc_`: two letters was not this mod's namespace
    /// to claim, and any other mod abbreviating a V-C name could land on it (docs/knowledge-base's own
    /// `Fatty.md` records a real one - "Valheim Cuisine" ships prefab names under `VC_*`).
    ///
    /// PURE: no Unity type, no engine call, nothing but string constants and the comments that group them.
    /// The hashing (`GetStableHashCode()`) and the ZDO/RPC calls that use these strings stay at their own
    /// call sites in Server/Client/Net, which are not pure and were never meant to be.
    /// </summary>
    public static class Keys
    {
        // The bird's ZDO (Server/Spawner.cs): authored whole by the server, owned by the pilot,
        // non-persistent - it cannot survive a restart, by design.
        public const string Cargo = "VCargo_cargo";       // int visitId: this is ours
        public const string Target = "VCargo_target";     // Vector3: where to put him down
        public const string Dropped = "VCargo_dropped";   // bool: he is on the ground
        public const string Turn = "VCargo_turn";         // Vector3: the descent waypoint
        public const string Away = "VCargo_away";         // Vector3: where the empty bird leaves to

        // The merchant's ZDO (Server/Spawner.cs): the PERSISTENT half of the pair.
        public const string Ingvar = "VCargo_ingvar";     // int visitId: this is Ingvar
        public const string Seed = "VCargo_seed";         // int: his lines and his bearing
        public const string State = "VCargo_state";       // int: carried/approaching/trading/leaving
        public const string Carrier = "VCargo_carrier";   // ZDOID (two int keys): the bird he hangs from

        // The player's own character ZDO (Client/ComfortReporter.cs): client-written, client-owned,
        // so it replicates - the same trust class as vanilla's own `baseValue`.
        public const string Rested = "VCargo_rested";
        public const string Comfort = "VCargo_comfort";

        // Routed RPCs (Net/AdminRpc.cs): `cargo visit` / `cargo dismiss` from a client's console to the
        // server and the answer back. Forgeable (the packet names its own sender), so the server decides
        // who is an admin and never trusts the request.
        public const string Admin = "VCargo_admin";
        public const string Reply = "VCargo_reply";

        // Per-peer ZRpc names (Net/DealWire.cs): the deal wire, registered on EACH peer's own socket as
        // it connects. A direct peer socket cannot be forged by another client the way a routed RPC can,
        // which is why money rides here.
        public const string Open = "VCargo_open";
        public const string Close = "VCargo_close";
        public const string Deal = "VCargo_deal";
        public const string Ack = "VCargo_ack";
        public const string Claim = "VCargo_claim";
        public const string Dismiss = "VCargo_dismiss";
        public const string Dealt = "VCargo_dealt";
        public const string Dismissed = "VCargo_dismissed";   // string: the answer to a dismiss (DealReason.Ok / TooFar / StaleVisit)

        // Object RPCs (Client/CargoMerchant.cs): `ZNetView.Register`, scoped to the merchant's own
        // object. The low-risk end of the collision issue, but renamed with the rest for one prefix.
        public const string Say = "VCargo_say";
        public const string Vanish = "VCargo_vanish";
    }
}
