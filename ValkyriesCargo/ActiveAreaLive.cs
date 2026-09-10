using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo
{
    /// <summary>
    /// The one place the live simulation distance is read off the game. `ZNet.GetSyncedSimulationDistance()`
    /// is, on the server, its own setting, and on a client the value the server validated for it
    /// (`RPC_ValidatedSimulationDistance`); before a world is up there is no ZNet and the answer is
    /// vanilla's own default. Three members, all probed by `zone_maths`; the rule they size is
    /// `Core/ActiveArea.cs`.
    /// </summary>
    public static class ActiveAreaLive
    {
        public static SimDistance Read()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return SimDistance.Original;
            SimulationDistance sd = znet.GetSyncedSimulationDistance();
            return new SimDistance(sd.NearSimulationDistance, sd.IsClassic);
        }
    }
}
