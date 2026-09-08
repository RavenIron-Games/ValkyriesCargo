using System;
using System.Collections.Generic;
using System.Globalization;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The rotating shelf (the owner, 2026-09-08; issue #56): of the whole catalogue, a seeded subset is on sale
    /// at a time, re-rolled on a game-day clock. Nothing is persisted for it and nothing is sent for it: the
    /// shelf is a pure function of the world's salt, the world time and the catalogue, so every machine and
    /// every restart compute the same one.
    ///
    /// The clock. World time (`ZNet.GetTimeSeconds`, the same seconds the drift counts in) is cut into periods
    /// of `rotationDays` game days; the period index is the roll's second seed and the world salt its first.
    /// The roll. A partial Fisher-Yates over the catalogue's order with an xorshift64* generator seeded from
    /// FNV-1a of "salt#period", the first `size` positions taken, then returned IN CATALOGUE ORDER, so the
    /// pane never reorders under a player and two machines list the same shelf the same way. Because the
    /// swaps are sequential, a bigger shelf for the same period is a superset of the smaller one (the harness
    /// checks this), so raising `ShelfSize` mid-period adds items and never swaps any out.
    ///
    /// PURE: strings, doubles and integers only. Not `GetStableHashCode`, on purpose - this seed must not
    /// depend on the engine's hash, which is the thing that moved on Valheim 1.0.
    /// </summary>
    public static class Shelf
    {
        /// <summary>Ceiling on the shelf size; above the catalogue's own count everything is on the shelf anyway.</summary>
        public const int MaxSize = 200;
        public const double MinRotationDays = 0.1;
        public const double MaxRotationDays = 365.0;

        /// <summary>The most a backpack mod can multiply the shelf by (Wu'barrk's "x2 to x4", 2026-09-08).</summary>
        public const int MaxBackpackMultiplier = 4;

        /// <summary>
        /// The shelf size that ships when a backpack mod is on the server (the backpack add-on, 2026-09-08):
        /// players who can carry more get more to buy. `shelfSize` times `multiplier`, clamped to MaxSize
        /// (the roll clamps to the catalogue on its own). 0 stays 0 - the fixed shelf is not a size to scale;
        /// no backpack mod, or a multiplier below 1, is x1; above MaxBackpackMultiplier is MaxBackpackMultiplier.
        /// </summary>
        public static int Scaled(int shelfSize, int multiplier, bool backpackMod)
        {
            if (shelfSize <= 0) return 0;
            if (!backpackMod) return Math.Min(shelfSize, MaxSize);
            int m = Math.Max(1, Math.Min(multiplier, MaxBackpackMultiplier));
            long scaled = (long)shelfSize * m;
            return (int)Math.Min(scaled, MaxSize);
        }

        /// <summary>Seconds in one period, or 0 when either number is unusable (then nothing ever rolls).</summary>
        public static double PeriodSeconds(double secondsPerGameDay, double rotationDays)
        {
            if (double.IsNaN(secondsPerGameDay) || double.IsInfinity(secondsPerGameDay) || secondsPerGameDay <= 0) return 0;
            if (double.IsNaN(rotationDays) || double.IsInfinity(rotationDays) || rotationDays <= 0) return 0;
            return secondsPerGameDay * rotationDays;
        }

        /// <summary>The period index the world time falls in: floor(worldTime / periodSeconds), never negative.</summary>
        public static long Period(double worldTime, double secondsPerGameDay, double rotationDays)
        {
            double len = PeriodSeconds(secondsPerGameDay, rotationDays);
            if (len <= 0 || double.IsNaN(worldTime) || double.IsInfinity(worldTime)) return 0;
            return (long)Math.Floor(Math.Max(0.0, worldTime) / len);
        }

        /// <summary>The world time at which the period after <paramref name="period"/> begins.</summary>
        public static double NextRollAt(long period, double secondsPerGameDay, double rotationDays) =>
            (period + 1) * PeriodSeconds(secondsPerGameDay, rotationDays);

        /// <summary>
        /// The names on the shelf for this salt and period: `size` of `pool`, in pool order. The whole pool
        /// when `size` covers it; empty when `size` is 0 or the pool is. Never throws.
        /// </summary>
        public static List<string> Roll(string salt, long period, IReadOnlyList<string> pool, int size)
        {
            var result = new List<string>();
            if (pool == null || pool.Count == 0 || size <= 0) return result;
            int n = pool.Count;
            if (size >= n) { for (int i = 0; i < n; i++) result.Add(pool[i]); return result; }

            ulong state = Seed(salt, period);
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            // Partial Fisher-Yates: settle positions 0 .. size-1; the rest of the array is never needed.
            for (int i = 0; i < size; i++)
            {
                int j = i + (int)(Next(ref state) % (ulong)(n - i));
                int tmp = idx[i]; idx[i] = idx[j]; idx[j] = tmp;
            }
            var chosen = new bool[n];
            for (int i = 0; i < size; i++) chosen[idx[i]] = true;
            for (int i = 0; i < n; i++) if (chosen[i]) result.Add(pool[i]);
            return result;
        }

        /// <summary>FNV-1a 64 over "salt#period"; never 0, which xorshift cannot leave.</summary>
        public static ulong Seed(string salt, long period)
        {
            string s = (salt ?? "") + "#" + period.ToString(CultureInfo.InvariantCulture);
            ulong h = 14695981039346656037UL;
            unchecked
            {
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 1099511628211UL; }
            }
            return h == 0 ? 0x9E3779B97F4A7C15UL : h;
        }

        /// <summary>xorshift64*: small, fast, and good enough to pick twenty of seventy-two.</summary>
        private static ulong Next(ref ulong s)
        {
            unchecked
            {
                s ^= s >> 12; s ^= s << 25; s ^= s >> 27;
                return s * 2685821657736338717UL;
            }
        }
    }
}
