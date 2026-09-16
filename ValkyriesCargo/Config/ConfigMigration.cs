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
                ValkyriesCargo.Log.LogWarning(LastSummary + " (the previous file is backed up beside it, .v" + fileVersion + ".bak)");
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
        /// found at an old default, hand version 2's derived line to `CatalogueOverrides`, stamp the
        /// version, save, then rewrite the raw file once to drop the now-unbound `Catalogue = ...` line
        /// under `[Server]` (BepInEx keeps an orphaned line forever; `ConfigFile.OrphanedEntries` is
        /// internal - house rule 5 - so this is a plain line filter over the file instead).
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

                if (versionEntry != null) versionEntry.Value = ConfigLedger.CurrentVersion;
                if (cfg != null) cfg.Save();

                if (_path != null) DropCatalogueLine(_path);
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
                File.Copy(path, path + ".v" + fromVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bak", overwrite: true);
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("Could not back up the config before migrating (" + ex.Message + "). Migrating anyway.");
            }
        }

        /// <summary>
        /// Drop the `Catalogue = ...` line under `[Server]`, once, now that it is unbound. A plain line
        /// filter: parse just enough to know the current section, keep everything else byte for byte.
        /// </summary>
        private static void DropCatalogueLine(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                var keep = new List<string>(lines.Length);
                string section = "";
                bool dropped = false;

                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length > 0 && line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        keep.Add(raw);
                        continue;
                    }

                    if (string.Equals(section, "Server", StringComparison.OrdinalIgnoreCase))
                    {
                        int eq = line.IndexOf('=');
                        string key = eq > 0 ? line.Substring(0, eq).Trim() : "";
                        if (string.Equals(key, "Catalogue", StringComparison.OrdinalIgnoreCase))
                        {
                            dropped = true;
                            continue;
                        }
                    }

                    keep.Add(raw);
                }

                if (dropped) File.WriteAllLines(path, keep.ToArray());
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("Could not drop the retired Catalogue line from the config file (harmless - it is unbound and ignored from here). Reason: " + ex.Message);
            }
        }
    }
}
