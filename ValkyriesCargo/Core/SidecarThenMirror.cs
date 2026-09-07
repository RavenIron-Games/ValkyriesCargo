using System;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Decision 4 (docs/DECISIONS-WUBARRK.md #4): the world sidecar stays the source of truth and the
    /// BarrkBOT JSON is a mirror of it, never the other way round. The ordering is part of the decision,
    /// not an implementation detail: the sidecar write runs first and to completion, and only if it
    /// reports success does the mirror run at all; whatever the mirror does, it can never affect the
    /// sidecar's own result, and a throw inside it can never reach the caller. PURE: <paramref
    /// name="primary"/> and <paramref name="mirror"/> are delegates, so the ordering and the fault
    /// isolation are provable without a filesystem, a market or a director -- give it a primary that
    /// records "I ran" and a mirror that throws, and the test is the proof.
    /// </summary>
    public static class SidecarThenMirror
    {
        /// <summary>
        /// Runs <paramref name="primary"/> first (swallowed if it throws, counted as failure: the sidecar
        /// save already promises never to throw, this is a second line of defence, not a licence to skip
        /// the first). Only when it returns true does <paramref name="mirror"/> run; any exception it
        /// throws is caught here and handed to <paramref name="onMirrorFailed"/>, never rethrown. Returns
        /// what <paramref name="primary"/> returned either way -- the mirror can never change that answer.
        /// </summary>
        public static bool Run(Func<bool> primary, Action mirror, Action<Exception> onMirrorFailed = null)
        {
            bool ok;
            try { ok = primary != null && primary(); }
            catch { ok = false; }

            if (ok && mirror != null)
            {
                try { mirror(); }
                catch (Exception ex) { if (onMirrorFailed != null) onMirrorFailed(ex); }
            }
            return ok;
        }
    }
}
