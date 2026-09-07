using System;

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
        public static readonly int RestedHash = "VCargo_rested".GetStableHashCode();
        public static readonly int ComfortHash = "VCargo_comfort".GetStableHashCode();

        public const float IntervalSeconds = 2f;

        private float _timer = IntervalSeconds;   // report on the first tick a player exists
        private ZNetView _nview;
        private Player _player;

        public bool Reported { get; private set; }
        public bool LastRested { get; private set; }
        public int LastComfort { get; private set; }
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
            Writes++;
            SecondsSinceWrite = 0f;
            Reported = true;
            LastRested = rested;
            LastComfort = comfort;
        }

        private int _throws;

        public void Reset()
        {
            _player = null;
            _nview = null;
            Reported = false;
            _timer = IntervalSeconds;
        }
    }
}
