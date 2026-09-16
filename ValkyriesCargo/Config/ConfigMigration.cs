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
        private static Dictionary<string, string> _snapshot;
        private static ConfigLedger.MigrationPlan _plan;
        private static string _path;

        /// <summary>The last migration's boot line, kept for `cargo status` / `cargo config`. Empty when nothing has run.</summary>
        public static string LastSummary { get; private set; } = "";

        public static void Begin(ConfigFile cfg)
        {
            _snapshot = null;
            _plan = null;
            _path = null;

            try
            {
                if (cfg == null) return;
                string path = cfg.ConfigFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return; // fresh install: new defaults bind on their own

                _snapshot = ConfigLedger.ParseIni(File.ReadAllLines(path));
                int fileVersion = ConfigLedger.ReadVersion(_snapshot);
                if (fileVersion >= ConfigLedger.CurrentVersion) return;

                _path = path;
                Backup(path, fileVersion);

                _plan = ConfigLedger.Plan(_snapshot, fileVersion, Catalogue.DefaultLine, Catalogue.HistoricalDefaults);
                LastSummary = ConfigLedger.Describe(_plan);

                List<string> catProblems = _plan.CatalogueTransform != null ? _plan.CatalogueTransform.Problems : null;
                string problemsSuffix = catProblems != null && catProblems.Count > 0
                    ? " (" + catProblems.Count + " old catalogue row(s) did not parse and were dropped, first: " + catProblems[0] + ")"
                    : "";
                ValkyriesCargo.Log.LogWarning(LastSummary + problemsSuffix + " (the previous file is backed up beside it, .v" + fileVersion + ".bak)");
            }
            catch (Exception ex)
            {
                // A failed migration must never stop the mod loading - worst case the config binds
                // exactly as it always did, which is the pre-migration behaviour.
                ValkyriesCargo.Log.LogError("Config migration could not start, settings will be read as-is. Reason: " + ex);
            }
        }

        /// <summary>
        /// After every bind, before <see cref="ModConfig.RecomputeCatalogue"/>: reset the slots version 1
        /// found at an old default, hand version 2's derived line to `CatalogueOverrides`, consume the
        /// retired `Server.Catalogue` key (BepInEx keeps an orphaned line forever otherwise: it lives in
        /// `ConfigFile.OrphanedEntries`, a PRIVATE property - not touched, house rule 5 - so this binds it
        /// under a throwaway default, which pulls it out of the orphan set, then removes it, both public
        /// `ConfigFile` API), stamp the version and save.
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
                        ModConfig.CatalogueOverrides.Value = _plan.CatalogueTransform.Overrides;
                }

                if (_path != null && cfg != null) ConsumeRetiredCatalogueKey(cfg);

                if (versionEntry != null) versionEntry.Value = ConfigLedger.CurrentVersion;
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
            }
        }

        private static void Backup(string path, int fromVersion)
        {
            try
            {
                string bak = path + ".v" + fromVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bak";
                if (File.Exists(bak))
                {
                    // Somebody's only clean copy already lives here - a migration that half-finished on an
                    // earlier boot (Finish threw after the file was partly rewritten but before the version
                    // stamped) would otherwise be overwritten by File.Copy's own overwrite:true. Fall back
                    // to a timestamped name instead of clobbering it.
                    bak = path + ".v" + fromVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
                          DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".bak";
                }
                File.Copy(path, bak, overwrite: false);
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("Could not back up the config before migrating (" + ex.Message + "). Migrating anyway.");
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
