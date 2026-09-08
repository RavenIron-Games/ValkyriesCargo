using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RavenIron.ValkyriesCargo.Server
{
    /// <summary>
    /// Where this world's saves live, resolved by REFLECTION rather than by a compiled call, because
    /// the compiled call had two independent ways to be wrong on Valheim 1.0 and neither of them would
    /// have said so.
    ///
    /// WHAT THIS REPLACES. `MarketStore.Resolve` used to read
    /// `World.GetWorldSavePath(FileHelpers.FileSource.Local)` directly. Against the 1.0 playtest
    /// (build 23105022 / 0.221.13, decompiled at `libs-Tools/DECOMPILED-PLAYTEST-build23105022/`):
    ///
    /// 1. **The method is gone.** 0.221.12 has
    ///    `World.GetWorldSavePath(FileSource)` -> `GetSaveDataPath(fileSource) + ((int)fileSource == 1
    ///    ? "/worlds_local" : "/worlds")`. 1.0 renames it `World.GetWorldsSaveRootPath(FileSource)` and
    ///    tests `fileSource.IsLocal()` instead. A compiled call to the old name is a
    ///    `MissingMethodException`.
    /// 2. **The enum value moved, and C# had baked ours in.** `FileHelpers.FileSource` was implicit
    ///    sequential on 0.221.12 - `Auto, Local, Cloud, Legacy` = 0, 1, 2, 3 - and is explicit BIT FLAGS
    ///    on 1.0: `Auto = 1, Local = 2, Cloud = 4, Legacy = 8`. An enum constant is inlined at OUR
    ///    compile time, so our DLL carries the literal `1`, which on 1.0 is `Auto`, not `Local`. Even
    ///    had the method survived, `Auto.IsLocal()` is `HasAll(Local)` - `(1 &amp; 2) == 2` - which is
    ///    false, so the sidecar would have resolved against `/worlds` instead of `/worlds_local`. No
    ///    exception, no wrong-looking log line, just a different directory. This is the same trap
    ///    `Core/EngineBaseline.cs` already records for the `Version` constants, in a new place.
    ///
    /// AND THE CATCH COULD NOT HAVE SAVED IT. The old call sat inside `try`/`catch` in `Resolve`, which
    /// would never have run: Mono resolves a member access when the CALLER is JIT-compiled, so a
    /// `MissingMethodException` is thrown while `Resolve` is being compiled, before its first
    /// instruction. That is the house rule `EngineCheck.cs` is built around - every probe there is a
    /// catching half and a `[MethodImpl(NoInlining)]` half - and this file now obeys it too: `Resolve`
    /// catches, `Find` touches the game types and is never inlined into it.
    ///
    /// Resolving the name at RUNTIME also means ONE DLL works on both builds, which is what a mod
    /// shipped to players who update at their own pace actually needs. Nothing here is private: both
    /// the method and the enum are public, so this is not an exception to house rule 5.
    /// </summary>
    public static class WorldSavePath
    {
        /// <summary>
        /// Where to look, in order. 0.221.12 has `World.GetWorldSavePath`; 1.0 has
        /// `SaveSystem.GetWorldsSaveRootPath` - the method moved TYPE as well as name, which is why the
        /// candidates are pairs and not just a list of names. `SaveSystem` does not exist at our compile
        /// time, so it is resolved off the running assembly by string; `World` is named the same way for
        /// symmetry, so neither build is the privileged one. Verified against BOTH real assemblies
        /// 2026-09-08 (`libs/assembly_valheim_publicized.dll` and the 0.221.13 dedicated-server
        /// `assembly_valheim.dll` in the playtest snapshot): exactly one candidate matches on each, and
        /// a first pass that searched only `World` matched nothing at all on 1.0.
        /// </summary>
        internal static readonly string[][] Candidates =
        {
            new[] { "World", "GetWorldSavePath" },              // 0.221.12 and earlier
            new[] { "SaveSystem", "GetWorldsSaveRootPath" },    // 1.0
        };

        /// <summary>The member of `FileHelpers.FileSource` we want, BY NAME - never by a baked value.</summary>
        internal const string SourceName = "Local";

        /// <summary>
        /// The world save directory, or null with <paramref name="detail"/> saying why. The catching
        /// half of the pair: every engine type this feature touches is reached from `Find`, so a
        /// member that moved lands here as a caught exception instead of taking the director down.
        /// </summary>
        public static string Resolve(out string detail)
        {
            try { return Find(out detail); }
            catch (Exception ex)
            {
                detail = "save path unreadable: " + ex.GetType().Name;
                return null;
            }
        }

        /// <summary>
        /// The half that names game types. `NoInlining` is not decoration: inlined, its `typeof` tokens
        /// would resolve when `Resolve` is compiled and put us back where we started.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string Find(out string detail)
        {
            detail = "";
            // `typeof(World)` is only the handle on the assembly - the type we WANT may be SaveSystem,
            // which this DLL was not compiled against and cannot name.
            Assembly asm = typeof(World).Assembly;

            // Matched on shape as well as name - static, returns string, one parameter, that parameter
            // an enum - so a same-named method of another shape is not mistaken for this one.
            MethodInfo found = FindCandidate(asm);
            if (found == null)
            {
                detail = "no world save path method (tried " + Describe() + ")";
                return null;
            }

            // The enum value BY NAME, read off the running assembly. This is the whole fix for the
            // 0/1/2/3 -> 1/2/4/8 change: whatever `Local` is worth on THIS build is what gets passed.
            Type sourceType = found.GetParameters()[0].ParameterType;
            if (!Enum.IsDefined(sourceType, SourceName))
            {
                detail = sourceType.Name + " has no member named '" + SourceName + "'";
                return null;
            }
            object local = Enum.Parse(sourceType, SourceName);

            object result = found.Invoke(null, new[] { local });
            string dir = result as string;
            if (string.IsNullOrEmpty(dir))
            {
                detail = found.Name + "(" + SourceName + ") came back empty";
                return null;
            }

            detail = found.DeclaringType.Name + "." + found.Name + "(" + SourceName + "=" +
                     Convert.ToInt64(local) + ")";
            return dir;
        }

        /// <summary>
        /// The first candidate (type, method) pair this build actually carries, or null. Shared by the
        /// resolver and by `EngineCheck`'s probe, so the probe can never pass on a shape the resolver
        /// would then refuse.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static MethodInfo FindCandidate(Assembly asm)
        {
            if (asm == null) return null;
            for (int i = 0; i < Candidates.Length; i++)
            {
                Type t = asm.GetType(Candidates[i][0], false);
                if (t == null) continue;
                MethodInfo[] all = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int j = 0; j < all.Length; j++)
                {
                    if (all[j].Name != Candidates[i][1]) continue;
                    if (all[j].ReturnType != typeof(string)) continue;
                    ParameterInfo[] ps = all[j].GetParameters();
                    if (ps.Length != 1 || !ps[0].ParameterType.IsEnum) continue;
                    return all[j];
                }
            }
            return null;
        }

        /// <summary>The candidates in words, for the one line a failure gets to write.</summary>
        internal static string Describe()
        {
            var parts = new string[Candidates.Length];
            for (int i = 0; i < Candidates.Length; i++) parts[i] = Candidates[i][0] + "." + Candidates[i][1];
            return string.Join(", ", parts);
        }
    }
}
