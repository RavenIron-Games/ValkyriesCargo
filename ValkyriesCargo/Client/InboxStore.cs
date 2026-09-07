using System;
using System.IO;
using System.Text;
using BepInEx;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// The client's inbox on disk: the delivery ids it has already applied (design 3.4), one small file in
    /// the BepInEx config folder, shared by every character on this machine (delivery ids carry the world's
    /// salt, so two worlds never collide). Loaded once per session, written after every applied delivery.
    /// Never throws: a missing or unreadable file is an empty inbox, which at worst redelivers once and is
    /// then recognised by the server's ledger.
    /// </summary>
    public static class InboxStore
    {
        public const string FileName = "com.raveniron.valkyriescargo.inbox.txt";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static int _failures;

        public static string Path
        {
            get
            {
                try { return System.IO.Path.Combine(Paths.ConfigPath, FileName); }
                catch { return null; }
            }
        }

        public static DealInbox Load()
        {
            string path = Path;
            try
            {
                if (path == null || !File.Exists(path)) return new DealInbox();
                return DealInbox.Parse(File.ReadAllText(path, Utf8NoBom).Trim());
            }
            catch (Exception ex)
            {
                if (_failures++ < 3) ValkyriesCargo.Log.LogWarning("inbox: could not read " + FileName + " (" + ex.Message + "); starting empty");
                return new DealInbox();
            }
        }

        public static bool Save(DealInbox inbox)
        {
            string path = Path;
            if (path == null || inbox == null) return false;
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, inbox.Encode() + "\n", Utf8NoBom);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                if (_failures++ < 3) ValkyriesCargo.Log.LogWarning("inbox: could not write " + FileName + " (" + ex.Message + ")");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }
    }
}
