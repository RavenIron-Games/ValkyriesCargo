// Stand-ins for the game / BepInEx types the compiled source mentions — the same idea as
// tests\CoreTests\Stubs.cs, which merges the RavenEye and Ragnarok's Wrath stub set.
//
// This harness compiles five files: Core\Catalogue.cs, Core\Wire.cs, Core\MarketSnapshot.cs,
// Core\Deal.cs and Core\Market.cs. Checked line by line, they name NOTHING outside
// System, System.Collections.Generic, System.Globalization, System.Text and (for
// Deal.NewNonce, which the simulation never calls — its nonces are a counter, so the run is
// deterministic) System.Security.Cryptography. No ZPackage, no ZNet, no ConfigEntry, no
// UnityEngine.
//
// So there is nothing to stub, and the honest stub file is an empty one. It exists rather
// than being left out because the next file someone adds to the .csproj — DemoMarket is pure
// too, but CargoRpc, TrayModel and Sidecar are not — will need this file, and the CoreTests
// copy is the place to lift the stub from. Do NOT reference the CoreTests project: two
// harnesses that share a project share a failure.
