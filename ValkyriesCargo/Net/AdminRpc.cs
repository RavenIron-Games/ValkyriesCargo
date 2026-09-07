using System;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Server;

namespace RavenIron.ValkyriesCargo.Net
{
    /// <summary>
    /// The admin verbs (`cargo visit`, `cargo dismiss`) from a client's console to the server, and the
    /// answer back, on vanilla's routed RPC. Routed RPCs are forgeable, so the SERVER decides who is an
    /// admin (AdminGate, vanilla's own list) and never trusts the request. Registered per session:
    /// `ZRoutedRpc.instance` is null for the whole of plugin Awake and is re-created on every world join.
    /// The deal wire (P6) is a different thing: direct peer ZRpc, not this.
    /// </summary>
    public static class AdminRpc
    {
        public const string Request = Keys.Admin;
        public const string Reply = Keys.Reply;

        private static ZRoutedRpc _registeredOn;
        private static int _throws;

        public static bool Registered => _registeredOn != null && ReferenceEquals(_registeredOn, ZRoutedRpc.instance);

        /// <summary>Called every tick while a world is live; registers once per ZRoutedRpc instance.</summary>
        public static void EnsureRegistered()
        {
            ZRoutedRpc rr = ZRoutedRpc.instance;
            if (rr == null || ReferenceEquals(rr, _registeredOn)) return;
            try
            {
                rr.Register<string, string>(Request, OnRequest);
                rr.Register<string>(Reply, OnReply);
                _registeredOn = rr;
                ValkyriesCargo.Log.LogInfo("routed RPCs registered for this session: " + Request + ", " + Reply);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("routed RPC registration threw: " + ex);
            }
        }

        public static void Reset() { _registeredOn = null; }

        /// <summary>Client side: ask the server. Returns what to print now; the server's answer prints when it arrives.</summary>
        public static string Send(string verb, string arg)
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return "cargo: no world loaded";
            if (!Registered) return "cargo: routed RPC not registered yet; try again in a moment";
            ZNetPeer server = znet.GetServerPeer();
            if (server == null) return "cargo: no server peer";
            ZRoutedRpc.instance.InvokeRoutedRPC(server.m_uid, Request, verb, arg ?? "");
            return "cargo: asked the server to " + verb + (string.IsNullOrEmpty(arg) ? "" : " " + arg) + "; its answer prints here";
        }

        private static void OnRequest(long sender, string verb, string arg)
        {
            try
            {
                ZNet znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                ZNetPeer peer = znet.GetPeer(sender);
                string who = peer != null ? peer.m_playerName : "?";
                string answer;
                if (!AdminGate.IsAdmin(znet, peer))
                {
                    answer = "cargo: not an admin (the server's adminlist.txt decides)";
                    ValkyriesCargo.Log.LogWarning("refused " + Request + " " + verb + " from " + who + " (" + Wire.Long(sender) + "): not an admin");
                }
                else
                {
                    ValkyriesCargo.Log.LogInfo("admin " + who + " (" + Wire.Long(sender) + "): cargo " + verb + " " + (arg ?? ""));
                    answer = CargoTick.Admin(verb, arg, sender, who);
                }
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, Reply, answer ?? "");
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError(Request + " handler threw: " + ex);
            }
        }

        private static void OnReply(long sender, string text)
        {
            try
            {
                ValkyriesCargo.Log.LogInfo("server answered: " + text);
                if (Console.instance != null) Console.instance.AddString(text ?? "");
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError(Reply + " handler threw: " + ex);
            }
        }
    }
}
