using System;
using System.IO;
using System.Text;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Server
{
    /// <summary>
    /// The world sidecar on disk (design 3.5): `valkyriescargo_{worldUid}.dat` beside the world, Cairn's
    /// Persistence pattern down to the failure modes, each paid for once already:
    /// 1. WORLD-SCOPED, one file per world uid. 2. ATOMIC WRITES: .tmp, rotate to .bak, move into place.
    /// 3. FAIL-SAFE: never throws at a caller; an unreadable file is quarantined to .corrupt so the next
    /// save cannot overwrite the evidence. 4. INVARIANT CULTURE. 5. NO BOM.
    /// The uid comes from the public `ZNet.GetWorldUID()`; the directory from `World.GetWorldSavePath(Local)`,
    /// explicitly Local because Auto/Cloud return "" under Steam Cloud (Cairn's note). What the file holds
    /// is Core.Sidecar's business; this only moves text.
    /// </summary>
    public sealed class MarketStore
    {
        public const string FileStem = "valkyriescargo";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public string Path { get; private set; }
        public string Detail { get; private set; } = "";
        public int Saves { get; private set; }
        public int Failures { get; private set; }
        public DateTime LastSaveUtc { get; private set; }
        public bool Quarantined { get; private set; }

        /// <summary>
        /// P10b, "refuse rather than corrupt". The file on disk claims a sidecar format this build does
        /// not know: it was written by a NEWER Valkyrie's Cargo, and it is somebody's market, not
        /// garbage. While this is set the store is READ-ONLY - `Load` answers nothing, `Save` writes
        /// nothing, `Quarantine` renames nothing - so a player who rolls the mod back and starts the
        /// world still has their purse when they roll forward again. Before P10b the first cadence save
        /// overwrote it and the format branch renamed it to `.corrupt`.
        /// </summary>
        public bool Held { get; private set; }

        /// <summary>Why the store is held, in words, for the log and `cargo status`. "" when it is not.</summary>
        public string HoldReason { get; private set; } = "";

        /// <summary>Resolve where this world's sidecar lives. Path is null (with Detail saying why) when it cannot be known.</summary>
        public static MarketStore Resolve(ZNet znet)
        {
            var s = new MarketStore();
            if (znet == null) { s.Detail = "no ZNet"; return s; }
            long uid;
            try { uid = znet.GetWorldUID(); }
            catch (Exception ex) { s.Detail = "world uid unreadable: " + ex.GetType().Name; return s; }
            if (uid == 0) { s.Detail = "world uid is 0 (world not loaded yet?)"; return s; }
            string dir;
            try { dir = World.GetWorldSavePath(FileHelpers.FileSource.Local); }
            catch (Exception ex) { s.Detail = "save path unreadable: " + ex.GetType().Name; return s; }
            if (string.IsNullOrEmpty(dir)) { s.Detail = "world save directory came back empty"; return s; }
            s.Path = System.IO.Path.Combine(dir, FileStem + "_" + uid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".dat");
            s.Detail = "uid " + uid + ", dir " + dir;
            return s;
        }

        /// <summary>
        /// The file's text, or null when there is none (a fresh world), when it cannot be read
        /// (quarantined, logged), or when it is from a NEWER build (held, logged, and left exactly as it
        /// was found). The format peek happens HERE rather than at the caller so that no caller can
        /// reach the quarantine branch with a newer file in its hand.
        /// </summary>
        public string Load()
        {
            if (Path == null) return null;
            try
            {
                if (!File.Exists(Path)) return null;
                string text = File.ReadAllText(Path, Utf8NoBom);
                int format = Sidecar.PeekFormat(text);
                if (format > Sidecar.FormatVersion)
                {
                    Held = true;
                    HoldReason = "the file says format " + Wire.Int(format) + " and this build reads format " +
                                 Wire.Int(Sidecar.FormatVersion) + "; it was written by a NEWER Valkyrie's Cargo";
                    ValkyriesCargo.Log.LogError(
                        "sidecar: REFUSING " + System.IO.Path.GetFileName(Path) + " - " + HoldReason +
                        ". The file is left exactly as it is (no .corrupt, no overwrite) and this session will not save: " +
                        "that is somebody's market, not a corrupt file. Run the newer build, or move the file aside yourself.");
                    return null;
                }
                return text;
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("sidecar: could not read " + System.IO.Path.GetFileName(Path) + " (" + ex.Message + "); keeping it as .corrupt and starting fresh");
                Quarantine();
                return null;
            }
        }

        /// <summary>
        /// A file with content and nothing readable in it is evidence: keep it as .corrupt, never
        /// overwrite it. A HELD file is not that - it is readable by a build that is not this one - so
        /// this refuses to rename it however it is called. The check is here rather than only at the
        /// call sites because `Quarantine` is public and the caller that reaches it is not ours.
        /// </summary>
        public void Quarantine()
        {
            if (Path == null) return;
            if (Held) { ValkyriesCargo.Log.LogWarning("sidecar: not quarantining " + System.IO.Path.GetFileName(Path) + " - " + HoldReason); return; }
            try
            {
                string dead = Path + ".corrupt";
                if (File.Exists(dead)) File.Delete(dead);
                if (File.Exists(Path)) File.Move(Path, dead);
                Quarantined = true;
            }
            catch { /* the load already degraded safely */ }
        }

        /// <summary>Write the whole text atomically. Never throws; false (logged) on failure, and never at all while held.</summary>
        public bool Save(string text)
        {
            if (Path == null) return false;
            if (Held)
            {
                // Once, and then quietly: the director saves on a 30 s cadence and would otherwise fill
                // the log with the same refusal every half minute for the life of the session.
                if (Failures++ == 0)
                    ValkyriesCargo.Log.LogError("sidecar: NOT saving - " + HoldReason +
                                                ". Nothing this session traded is persisted, and the file on disk is untouched.");
                return false;
            }
            string tmp = Path + ".tmp";
            string bak = Path + ".bak";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(tmp, text ?? "", Utf8NoBom);
                if (File.Exists(Path))
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(Path, bak);
                }
                File.Move(tmp, Path);
                Saves++;
                LastSaveUtc = DateTime.UtcNow;
                return true;
            }
            catch (Exception ex)
            {
                Failures++;
                if (Failures <= 3) ValkyriesCargo.Log.LogError("sidecar: save failed (" + ex.Message + "); the market is kept in memory");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }
    }
}
