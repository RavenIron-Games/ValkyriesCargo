using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The server's memory of accepted deals it has not yet seen acknowledged (design 3.4, the escrow rule
    /// ported from VikingOS): a deal the server committed but the client may never have applied. Keyed by
    /// the player's platform id (stable across sessions; the peer uid is not). On `VCargo_ack` a row clears;
    /// on `VCargo_claim` at login every row the player still owes is redelivered; the client's inbox makes a
    /// second delivery harmless. Bounded per player and overall, oldest evicted first. Rows:
    /// "owed\tplayerKey\tdeliveryId\tresult" where result is DealResult.Encode() with NewMarketState
    /// dropped (an owed result is always an accepted one). PURE.
    /// </summary>
    public sealed class OwedLedger
    {
        public const int DefaultPerPlayer = 50;
        public const int DefaultTotal = 2000;

        private sealed class Row { public string Player; public string DeliveryId; public DealResult Result; }

        private readonly int _perPlayer;
        private readonly int _total;
        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);

        public OwedLedger(int perPlayer = DefaultPerPlayer, int total = DefaultTotal)
        {
            _perPlayer = Math.Max(1, perPlayer);
            _total = Math.Max(_perPlayer, total);
        }

        public int Count => _rows.Count;
        public int Evictions { get; private set; }

        /// <summary>A player key is a wire token with no tab; anything else is refused so a row can never break the file.</summary>
        public static bool IsPlayerKey(string key) =>
            Wire.IsToken(key) && key.IndexOf('\t') < 0 && key.IndexOf('\n') < 0 && key.IndexOf('\r') < 0;

        /// <summary>Record an accepted delivery. False (nothing stored) for a refused result, a bad key, or a known id.</summary>
        public bool Add(string playerKey, DealResult result)
        {
            if (result == null || !result.Ok || string.IsNullOrEmpty(result.DeliveryId) || !IsPlayerKey(playerKey)) return false;
            if (!_ids.Add(result.DeliveryId)) return false;
            _rows.Add(new Row { Player = playerKey, DeliveryId = result.DeliveryId, Result = Strip(result) });
            int mine = 0;
            for (int i = _rows.Count - 1; i >= 0; i--)
                if (_rows[i].Player == playerKey && ++mine > _perPlayer) { _ids.Remove(_rows[i].DeliveryId); _rows.RemoveAt(i); Evictions++; }
            while (_rows.Count > _total) { _ids.Remove(_rows[0].DeliveryId); _rows.RemoveAt(0); Evictions++; }
            return true;
        }

        /// <summary>The client has it. True if a row cleared; false for an unknown id or another player's row.</summary>
        public bool Ack(string playerKey, string deliveryId)
        {
            if (string.IsNullOrEmpty(deliveryId) || !_ids.Contains(deliveryId)) return false;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].DeliveryId != deliveryId) continue;
                if (_rows[i].Player != playerKey) return false;
                _ids.Remove(deliveryId);
                _rows.RemoveAt(i);
                return true;
            }
            return false;
        }

        public bool Owes(string deliveryId) => !string.IsNullOrEmpty(deliveryId) && _ids.Contains(deliveryId);

        /// <summary>Everything this player is still owed, oldest first.</summary>
        public List<DealResult> For(string playerKey)
        {
            var list = new List<DealResult>();
            foreach (Row r in _rows) if (r.Player == playerKey) list.Add(r.Result);
            return list;
        }

        public int CountFor(string playerKey)
        {
            int n = 0;
            foreach (Row r in _rows) if (r.Player == playerKey) n++;
            return n;
        }

        public void Clear() { _rows.Clear(); _ids.Clear(); }

        // ---- rows ----------------------------------------------------------------------------

        public List<string> EncodeRows()
        {
            var rows = new List<string>(_rows.Count);
            foreach (Row r in _rows) rows.Add("owed\t" + r.Player + "\t" + r.DeliveryId + "\t" + r.Result.Encode());
            return rows;
        }

        /// <summary>Apply saved rows on top of what is held. Bad rows are reported and skipped. Never throws.</summary>
        public int ApplyRows(IList<string> rows, List<string> problems)
        {
            int applied = 0;
            if (rows == null) return 0;
            foreach (string raw in rows)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                string[] f = raw.TrimEnd('\r').Split('\t');
                if (f.Length != 4 || f[0] != "owed") { Wire.Report(problems, "owed row did not parse: " + raw); continue; }
                if (!IsPlayerKey(f[1])) { Wire.Report(problems, "owed row has a bad player key: " + raw); continue; }
                DealResult r = DealResult.Parse(f[3], problems);
                if (r == null) continue;
                if (!r.Ok || r.DeliveryId != f[2]) { Wire.Report(problems, "owed row is not an accepted delivery of its own id: " + raw); continue; }
                if (Add(f[1], r)) applied++;
            }
            return applied;
        }

        private static DealResult Strip(DealResult r)
        {
            return new DealResult
            {
                Nonce = r.Nonce, DeliveryId = r.DeliveryId, Ok = true, Reason = DealReason.Ok, CoinsDelta = r.CoinsDelta,
                ItemsToAdd = new List<DealLine>(r.ItemsToAdd), ItemsToRemove = new List<DealLine>(r.ItemsToRemove), NewMarketState = "",
            };
        }
    }
}
