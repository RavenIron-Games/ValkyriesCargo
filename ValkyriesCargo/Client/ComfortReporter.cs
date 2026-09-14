using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// Writes `VCargo_rested` and `VCargo_comfort` on the LOCAL player's own character ZDO (design 3.1) every
    /// 2 s and on change, from the mod's one tick. Comfort never leaves the client in vanilla
    /// (`SE_Rested.CalculateComfortLevel` runs locally and `Player.m_comfortLevel` is private), so the
    /// client reports it the way vanilla reports `baseValue`: on its own ZDO, which replicates because
    /// the client owns it. The server reads both from `GetAllCharacterZDOS()`. Same trust class as
    /// `baseValue`; a lying client can only make itself eligible for a merchant.
    /// </summary>
    public sealed class ComfortReporter
    {
        public static readonly int RestedHash = Keys.Rested.GetStableHashCode();
        public static readonly int ComfortHash = Keys.Comfort.GetStableHashCode();
        public static readonly int BuiltHash = Keys.Built.GetStableHashCode();

        public const float IntervalSeconds = 2f;

        /// <summary>
        /// The built-base scan runs on its own, slower clock. `Piece.GetAllPiecesInRadius` walks EVERY
        /// instanced piece and measures each one, so on a large hall it is the most expensive thing in
        /// this file, while the answer it produces changes about as often as somebody builds a wall. The
        /// gate that consumes it only runs on the roll, once a minute.
        /// </summary>
        public const float BuiltIntervalSeconds = 5f;

        private float _timer = IntervalSeconds;   // report on the first tick a player exists
        private float _builtTimer = BuiltIntervalSeconds;
        private ZNetView _nview;
        private Player _player;

        /// <summary>Reused so the scan does not allocate a list every five seconds.</summary>
        private readonly List<Piece> _pieces = new List<Piece>(256);

        /// <summary>The radius the scan last ran at, pushed in from the synced config.</summary>
        public float BuiltRadius = HomeGround.DefaultBuiltRadius;

        public bool Reported { get; private set; }
        public bool LastRested { get; private set; }
        public int LastComfort { get; private set; }
        public bool LastBuilt { get; private set; }
        public int LastPiecesSeen { get; private set; }
        public int Writes { get; private set; }
        public float SecondsSinceWrite { get; private set; }

        public void Tick(float dt)
        {
            _timer += dt;
            SecondsSinceWrite += dt;
            if (_timer < IntervalSeconds) return;
            _timer = 0f;

            Player p = Player.m_localPlayer;
            if (p == null) { Reset(); return; }
            if (p != _player) { _player = p; _nview = p.GetComponent<ZNetView>(); }
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            ZDO zdo = _nview.GetZDO();
            if (zdo == null) return;

            bool rested;
            int comfort;
            try
            {
                SEMan seman = p.GetSEMan();
                rested = seman != null && seman.HaveStatusEffect(SEMan.s_statusEffectRested);
                comfort = p.GetComfortLevel();
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("comfort report: reading the player threw " + ex.Message);
                return;
            }

            // Written every interval, not only on change: a fresh ZDO (respawn, relog) starts blank and the
            // server must never read a stale "rested" from a previous body.
            zdo.Set(RestedHash, rested);
            zdo.Set(ComfortHash, comfort);

            // On its own clock, but written EVERY interval like the two above: a fresh ZDO starts blank
            // and the server must never read a stale "there is a base here" off a previous body.
            _builtTimer += IntervalSeconds;
            if (_builtTimer >= BuiltIntervalSeconds)
            {
                _builtTimer = 0f;
                LastBuilt = ScanForBuiltBase(p);
            }
            zdo.Set(BuiltHash, LastBuilt);

            Writes++;
            SecondsSinceWrite = 0f;
            Reported = true;
            LastRested = rested;
            LastComfort = comfort;
        }

        /// <summary>
        /// Is there a piece near the player that a PLAYER placed? Vanilla stamps `creator` on a piece
        /// when a player builds it and leaves it 0 on everything a location brought with it, which is
        /// exactly the line issue #79 needed drawn: the Bog Witch's camp is shelter and comfort and
        /// baseValue, and not one board of it was built by anybody.
        ///
        /// `IsPlacedByPlayer` and NOT `IsCreator`, deliberately. On a shared server one player raises the
        /// hall and everyone else lives in it; keying on the builder would mean only the builder ever saw
        /// a merchant, which trades a reported bug for a worse unreported one.
        ///
        /// Returns false on a throw, which is the safe direction: the visit simply does not happen, and
        /// `Server.RequireBuiltBase` turns the whole gate off for anyone it gets wrong.
        /// </summary>
        private bool ScanForBuiltBase(Player p)
        {
            try
            {
                float radius = BuiltRadius;
                if (radius < HomeGround.MinBuiltRadius) radius = HomeGround.MinBuiltRadius;
                if (radius > HomeGround.MaxBuiltRadius) radius = HomeGround.MaxBuiltRadius;

                _pieces.Clear();
                Piece.GetAllPiecesInRadius(p.transform.position, radius, _pieces);
                LastPiecesSeen = _pieces.Count;

                for (int i = 0; i < _pieces.Count; i++)
                {
                    Piece piece = _pieces[i];
                    if (piece != null && piece.IsPlacedByPlayer()) { _pieces.Clear(); return true; }
                }
                _pieces.Clear();
                return false;
            }
            catch (Exception ex)
            {
                if (_builtThrows++ < 3) ValkyriesCargo.Log.LogWarning("built-base scan threw " + ex.Message);
                return false;
            }
        }

        private int _throws;
        private int _builtThrows;

        public void Reset()
        {
            _player = null;
            _nview = null;
            Reported = false;
            LastBuilt = false;
            LastPiecesSeen = 0;
            _timer = IntervalSeconds;
            _builtTimer = BuiltIntervalSeconds;
        }
    }
}
