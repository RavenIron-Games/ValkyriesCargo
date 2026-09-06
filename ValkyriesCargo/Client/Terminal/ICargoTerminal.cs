using UnityEngine;

namespace RavenIron.ValkyriesCargo.Client.Terminal
{
    /// <summary>
    /// What the merchant calls to show the trade window (design WORKSPLIT §2). Track B implements it
    /// in Client/Terminal/ and assigns CargoTerminalHost.Instance at plugin Awake. The merchant passes
    /// its own GameObject so the terminal can apply the vanilla 5 m hide rule; everything the terminal
    /// shows comes from CargoRpc.Market / CargoRpc.Visit, and everything it does goes through CargoRpc.
    /// </summary>
    public interface ICargoTerminal
    {
        bool IsOpen { get; }
        void Open(GameObject merchant, int visitId);
        void Close();
    }

    public static class CargoTerminalHost
    {
        /// <summary>Null until Track B's terminal registers itself; the merchant treats null as "no terminal built yet".</summary>
        public static ICargoTerminal Instance;
    }
}
