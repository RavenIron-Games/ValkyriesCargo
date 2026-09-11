using System;
using System.Runtime.CompilerServices;

namespace RavenIron.ValkyriesCargo.Server
{
    /// <summary>
    /// "Is this peer an admin?" answered by vanilla's own PUBLIC `ZNet.IsAdmin(string hostName)`, whose
    /// whole body is `ListContainsId(m_adminList, hostName)`: the check the game itself applies before
    /// (On 1.0.7 that body ended with `PlatformUserID.FilterPlatformUserID`, whose result OVERRODE the older
    /// bare / `Steam_` match: the list wanted `V_<steamid>`, and vanilla's devcommands and this gate refused
    /// together when it did not - the shape seen on Storm10, 2026-09-10. **1.0.12 FIXED THAT**: the
    /// assignment became `flag |= `, so an earlier bare or `Steam_` match now SURVIVES the filtered one and
    /// any of the three forms admits. An admin list that 1.0.7 broke works again, with no edit.)
    /// it honours a remote console command, a kick or a ban. RavenEye's shape (decompile-verified there
    /// on 0.221.12 and again here on 2026-09-06: ZNet.cs:2592). Exactly one method in this mod names
    /// the game's admin API. FAIL CLOSED: any doubt, any exception, is "not an admin", logged once.
    /// </summary>
    public static class AdminGate
    {
        private static bool _failureLogged;

        public static bool IsAdmin(ZNet znet, ZNetPeer peer)
        {
            if (znet == null || peer == null) return false;

            string host;
            try { host = peer.m_socket?.GetHostName(); }
            catch { return false; }
            if (string.IsNullOrEmpty(host)) return false;

            try
            {
                return Check(znet, host);
            }
            catch (Exception ex)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    ValkyriesCargo.Log.LogError("AdminGate: ZNet.IsAdmin(string) threw " + ex.GetType().Name + ": " + ex.Message +
                                                ". No remote admin command will be honoured. If this is a MissingMethodException, Valheim's API moved.");
                }
                return false;
            }
        }

        /// <summary>Never inlined, so a vanished `ZNet.IsAdmin` raises inside the try above, not while JIT-compiling the caller.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Check(ZNet znet, string host) => znet.IsAdmin(host);
    }
}
