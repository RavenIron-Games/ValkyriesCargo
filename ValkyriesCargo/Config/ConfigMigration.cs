// From WingsoftheValkyrie's ConfigMigration.cs (Wu'barrk, RGlabs84), the family's shape; adapted 2026-09-16 at the owner's word.
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Config
{
    /// <summary>
    /// The engine-facing half of the config migration; the decisions themselves are
    /// <see cref="Core.ConfigLedger"/> (PURE, off-game). Same machinery as the family's (TortalPortal,
    /// Fatty, Wings): snapshot the raw file BEFORE any bind, a stamped layout version, a backup beside the
    /// file, and a failed migration never stops the mod loading.
    ///
    /// <see cref="Begin"/> runs before any `cfg.Bind`; <see cref="Finish"/> runs after every bind, once
    /// `ModConfig.CatalogueOverrides` exists to receive the derived line.
    /// </summary>
    public static class ConfigMigration
    {
        /// <summary>
        /// What <see cref="Begin"/> decided, so <see cref="Finish"/> can tell "nothing to do" from "tried
        /// and could not tell" (should-fix, review round 2): stamping <c>ConfigVersion</c> on a state the
        /// mod never actually reached makes the failure permanent, because the next boot sees a current
        /// file and never retries. Fresh = no file existed to migrate (a first install). AlreadyCurrent =
        /// the file's own stamped version already meets or beats <see cref="ConfigLedger.CurrentVersion"/>.
        /// Planned = a plan was built (a stored catalogue line, if any, was read and derived). Failed = the
        /// <c>try</c> in <see cref="Begin"/> threw before a plan could be built (a sharing violation on the
        /// cfg at boot is the realistic one on Windows) - only this one blocks the stamp.
        /// </summary>
        private enum MigrationState { Fresh, AlreadyCurrent, Planned, Failed }

        private static Dictionary<string, string> _snapshot;
        private static ConfigLedger.MigrationPlan _plan;
        private static string _path;
        private static MigrationState _state = MigrationState.Fresh;

        /// <summary>Whether <see cref="Backup"/> actually landed a copy (or found an identical one already there) for THIS boot's migration. False with <see cref="_path"/> non-null means the raw file still holds the retired key, on purpose (should-fix, review round 2).</summary>
        private static bool _backedUp;

        /// <summary>
        /// Whether THIS boot is the first to derive `CatalogueOverrides` from the old `Catalogue` line: true
        /// only while the file carries no `Server.CatalogueOverrides` key at all (BepInEx writes one on the
        /// first boot of this build). Should-fix, review round 3: a boot that derived the line but could not
        /// back the file up leaves it unstamped, so the next boot plans the transform again - and must NOT
        /// overwrite whatever the admin has since put in `CatalogueOverrides` from the console.
        /// </summary>
        private static bool _firstDerive = true;

        /// <summary>The last migration's boot line, kept for `cargo status` / `cargo config`. Empty when nothing has run.</summary>
        public static string LastSummary { get; private set; } = "";

        public static void Begin(ConfigFile cfg)
        {
            _snapshot = null;
            _plan = null;
            _path = null;
            _state = MigrationState.Fresh;
            _backedUp = false;
            _firstDerive = true;

            try
            {
                if (cfg == null) return; // Fresh: nothing to migrate, nothing to stamp against
                string path = cfg.ConfigFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return; // fresh install: new defaults bind on their own (Fresh)

                _snapshot = ConfigLedger.ParseIni(ReadLinesShared(path));
                _firstDerive = !_snapshot.ContainsKey("Server::CatalogueOverrides");
                int fileVersion = ConfigLedger.ReadVersion(_snapshot);
                if (fileVersion >= ConfigLedger.CurrentVersion) { _state = MigrationState.AlreadyCurrent; return; }

                _path = path;
                _backedUp = Backup(path, fileVersion);

                _plan = ConfigLedger.Plan(_snapshot, fileVersion, Catalogue.DefaultLine, Catalogue.HistoricalDefaults);
                _state = MigrationState.Planned;
                LastSummary = ConfigLedger.Describe(_plan);

                List<string> catProblems = _plan.CatalogueTransform != null ? _plan.CatalogueTransform.Problems : null;
                string problemsSuffix = catProblems != null && catProblems.Count > 0
                    ? " (" + catProblems.Count + " old catalogue row(s) did not parse and were dropped, first: " + catProblems[0] + ")"
                    : "";
                string backupSuffix = _backedUp
                    ? " (the previous file is backed up beside it, .v" + fileVersion + ".bak)"
                    : " (no backup could be written; the retired Catalogue key is left in the file and the version is left unstamped so this migration retries next boot)";
                ValkyriesCargo.Log.LogWarning(LastSummary + problemsSuffix + backupSuffix);
            }
            catch (Exception ex)
            {
                // A failed migration must never stop the mod loading. Every other value binds exactly as it
                // always did; the ONE thing that differs from before this build is the catalogue, because the
                // old `Server.Catalogue` key is no longer bound: this boot runs the shipped catalogue plus
                // whatever `CatalogueOverrides` already holds, and the stored line waits, unread, for the
                // next boot's retry (review round 3). _state stays Failed so Finish neither stamps a version
                // the mod never reached nor retires the key (an unstamped file is what makes the retry possible).
                _state = MigrationState.Failed;
                ValkyriesCargo.Log.LogError("Config migration could not start. Every other value binds as it always did; the old Server.Catalogue " +
                                            "line was NOT read, so the shipped catalogue plus CatalogueOverrides is in force this boot, and the " +
                                            "migration retries on the next boot. Reason: " + ex);
            }
        }

        /// <summary>
        /// After every bind, before <see cref="ModConfig.RecomputeCatalogue"/>: reset the slots version 1
        /// found at an old default, hand version 2's derived line to `CatalogueOverrides`, consume the
        /// retired `Server.Catalogue` key (BepInEx keeps an orphaned line forever otherwise: it lives in
        /// `ConfigFile.OrphanedEntries`, a PRIVATE property - not touched, house rule 5 - so this binds it
        /// under a throwaway default, which pulls it out of the orphan set, then removes it, both public
        /// `ConfigFile` API), stamp the version and save.
        ///
        /// The stamp and the key-consumption are both gated on the boot having actually backed up the file
        /// (should-fix, review round 2): stamping <c>ConfigVersion</c> when <see cref="Begin"/> failed, or
        /// dropping the admin's only copy of the old `Catalogue` line when no backup could be written,
        /// both destroy the one thing this migration promises never to lose. Skipping either here is safe
        /// - an unstamped file with its old key still in it is exactly a pre-migration file, so the next
        /// boot's `Begin` retries the whole thing from scratch.
        /// </summary>
        public static void Finish(ConfigFile cfg, ConfigEntry<int> versionEntry)
        {
            try
            {
                if (_plan != null)
                {
                    foreach (string slot in _plan.ResetToDefault)
                    {
                        int i = slot.IndexOf("::", StringComparison.Ordinal);
                        if (i < 0) continue;
                        var def = new ConfigDefinition(slot.Substring(0, i), slot.Substring(i + 2));
                        if (cfg == null || !cfg.ContainsKey(def)) continue;
                        ConfigEntryBase entry = cfg[def];
                        entry.BoxedValue = entry.DefaultValue;
                    }

                    if (_plan.CatalogueTransform != null && _plan.CatalogueTransform.StoredLine != null)
                    {
                        if (_firstDerive)
                            ModConfig.CatalogueOverrides.Value = _plan.CatalogueTransform.Overrides;
                        else
                            ValkyriesCargo.Log.LogWarning("Server.CatalogueOverrides was already derived by an earlier boot that could not finish; " +
                                                          "leaving your current value alone and only retiring the old Catalogue key.");
                    }
                }

                // A migration was attempted (_path != null) only when Begin found a file below the current
                // version; that is also the only case where anything below needs gating.
                bool migrationAttempted = _path != null;
                bool safeToFinish = _state != MigrationState.Failed && (!migrationAttempted || _backedUp);

                if (_state == MigrationState.Failed)
                    ValkyriesCargo.Log.LogWarning("Config migration did not run this boot: the shipped catalogue (plus CatalogueOverrides) is in force, " +
                                                  "your stored Catalogue line was not read, and it will be migrated on the next successful boot.");

                if (migrationAttempted && cfg != null)
                {
                    if (safeToFinish)
                    {
                        ConsumeRetiredCatalogueKey(cfg);
                    }
                    else
                    {
                        ValkyriesCargo.Log.LogWarning(
                            "Leaving the retired Server.Catalogue line in the config file: no backup could be written for it, " +
                            "so this is the admin's only copy. It is harmless once the version is stamped (BepInEx keeps an " +
                            "orphaned key, never applies it), but the version is NOT stamped this boot for the same reason - " +
                            "the migration retries next boot.");
                    }
                }

                if (versionEntry != null)
                {
                    if (safeToFinish)
                        versionEntry.Value = ConfigLedger.CurrentVersion;
                    else
                        ValkyriesCargo.Log.LogWarning("Config migration did not finish cleanly; ConfigVersion is left unstamped so the next boot retries instead of treating this one as done.");
                }
                if (cfg != null) cfg.Save();
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("Config migration could not finish - check the backup beside your config file. Reason: " + ex);
            }
            finally
            {
                _snapshot = null;
                _plan = null;
                _path = null;
                _state = MigrationState.Fresh;
                _backedUp = false;
                _firstDerive = true;
            }
        }

        /// <summary>
        /// The raw file, line by line, opened with FileShare.ReadWrite so another process holding the cfg
        /// open for writing (a config manager, an editor, a sync tool - the realistic Windows case) does
        /// not fail the migration the way File.ReadAllLines would (review round 3).
        /// </summary>
        private static List<string> ReadLinesShared(string path)
        {
            var lines = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
            }
            return lines;
        }

        /// <summary>
        /// Copies `path` beside itself as `path.v` + `fromVersion` + `.bak` (never overwritten - falls back
        /// to a timestamped name so a half-finished earlier run cannot destroy the only clean copy). Returns
        /// true when a copy now exists on disk for this migration (the fresh copy landed, or an identical
        /// one was already there from an earlier attempt); false means no safe copy of the pre-migration
        /// file exists anywhere, which the caller must treat as a reason not to touch the original further
        /// (should-fix, review round 2). Never throws.
        /// </summary>
        private static bool Backup(string path, int fromVersion)
        {
            try
            {
                string bak = path + ".v" + fromVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bak";
                if (File.Exists(bak))
                {
                    if (BytesEqual(bak, path)) return true; // already backed up, byte for byte - nothing more to do

                    // Somebody's only clean copy already lives here and it is NOT what we are about to
                    // migrate - a migration that half-finished on an earlier boot (Finish threw after the
                    // file was partly rewritten but before the version stamped) would otherwise be
                    // overwritten by File.Copy's own overwrite:true. Fall back to a timestamped name
                    // instead of clobbering it.
                    bak = path + ".v" + fromVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
                          DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".bak";
                }
                File.Copy(path, bak, overwrite: false);
                return true;
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("Could not back up the config before migrating (" + ex.Message + "). The retired Catalogue key will be left in place and the version left unstamped so this retries next boot.");
                return false;
            }
        }

        private static bool BytesEqual(string pathA, string pathB)
        {
            try
            {
                byte[] a = File.ReadAllBytes(pathA);
                byte[] b = File.ReadAllBytes(pathB);
                if (a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) return false;
                return true;
            }
            catch
            {
                return false; // can't prove they match - treat as different, which routes to the timestamped fallback
            }
        }

        /// <summary>
        /// Drop the retired `Server.Catalogue` key through public `ConfigFile` API alone: `Bind` under a
        /// throwaway default reads whatever is still in `OrphanedEntries` (BepInEx's own `Bind` removes it
        /// from there the moment it binds), and `Remove` then takes the now-bound entry back out of
        /// `Entries` too, so neither collection carries it into the next `Save`. Never names the private
        /// property itself.
        /// </summary>
        private static void ConsumeRetiredCatalogueKey(ConfigFile cfg)
        {
            try
            {
                var def = new ConfigDefinition("Server", "Catalogue");
                cfg.Bind<string>(def, "");
                cfg.Remove(def);
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("Could not drop the retired Catalogue key from the config file (harmless - it is unbound and ignored from here). Reason: " + ex.Message);
            }
        }
    }
}
