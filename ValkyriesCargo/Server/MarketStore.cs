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

        /// <summary>The file's text, or null when there is none (a fresh world) or it cannot be read (quarantined, logged).</summary>
        public string Load()
        {
            if (Path == null) return null;
            try
            {
                if (!File.Exists(Path)) return null;
                return File.ReadAllText(Path, Utf8NoBom);
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("sidecar: could not read " + System.IO.Path.GetFileName(Path) + " (" + ex.Message + "); keeping it as .corrupt and starting fresh");
                Quarantine();
                return null;
            }
        }

        /// <summary>A file with content and nothing readable in it is evidence: keep it as .corrupt, never overwrite it.</summary>
        public void Quarantine()
        {
            if (Path == null) return;
            try
            {
                string dead = Path + ".corrupt";
                if (File.Exists(dead)) File.Delete(dead);
                if (File.Exists(Path)) File.Move(Path, dead);
                Quarantined = true;
            }
            catch { /* the load already degraded safely */ }
        }

        /// <summary>Write the whole text atomically. Never throws; false (logged) on failure.</summary>
        public bool Save(string text)
        {
            if (Path == null) return false;
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
