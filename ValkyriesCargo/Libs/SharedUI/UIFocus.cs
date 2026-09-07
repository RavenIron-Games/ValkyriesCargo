// NOT OURS. VikingOS SharedUI by Wubarrk - libs-Tools/SharedUI/UIFocus.cs at VikingOS 0.9.8,
// 2026-09-06. License MIT. Vendored as shared source; update from libs-Tools, never edit here.
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SharedUI
{
    // CANONICAL shared copy. Consume via <Compile Include="..\libs-Tools\SharedUI\UIFocus.cs" />.
    //
    // Generalizes a gotcha first solved in TortalPortal (InputFocusPatch.cs) for its ONE IMGUI window. Any
    // mod that owns more than one independent IMGUI window (a HUD toggle panel, a chat overhaul, a settings
    // window...) needs the same fix applied once per window, and a single shared bool doesn't compose - two
    // windows open at once would have the second one's OnGUI stomp the first one's flag. UIFocus is a simple
    // named-token registry instead: each window announces its own wants under its own string id every frame,
    // and the two patches below just ask "is ANYONE asking".
    //
    // ---- the three gotchas this exists to fix (all confirmed against the decompiled assembly) ----
    //
    // 1) An auto-focused/open IMGUI text field silently kills the game's "Use" key, not just movement.
    //    Player.Update reads `bool flag2 = TakeInput()`, and TakeInput() consults Chat.HasFocus() - which
    //    also gates the inventory toggle, the map's pan/zoom/pin keys, and ZInput.GetButtonDown("Use").
    //    Reporting focus through the SAME gate the game already uses stops all of it at once, and costs
    //    nothing while nothing has focus. Do NOT try to intercept each of those inputs individually - Chat
    //    already IS the single choke point vanilla itself uses.
    //
    // 2) Valheim only frees the mouse cursor for windows it knows by name. GameCamera.UpdateMouseCapture
    //    hardcodes the map/inventory/store/barber; an IMGUI window is on nobody's list, so it opens with the
    //    cursor still locked to the camera and invisible. A POSTFIX that corrects the lock state afterwards
    //    is not enough: Unity SNAPS the cursor to the screen centre the instant it is (re-)locked, so vanilla
    //    locking first and a postfix unlocking afterwards still yanks the pointer to centre every single
    //    frame. This has to be a PREFIX that replaces the method outright when a window wants the cursor.
    //
    // ---- 3) ⚠️ RAISE THE TOKEN FROM Update, NOT FROM OnGUI (added 2026-08-12, BarrkUI 0.9.4) ------------
    //
    // THE SINGLE MOST EXPENSIVE MISTAKE A CONSUMER OF THIS FILE CAN MAKE, because it looks correct, compiles,
    // and works most of the time. The obvious place to announce a window is inside the window's own OnGUI -
    // that is where the window knows it is open, and gotcha 1 above is satisfied by it. It is still wrong.
    //
    // **Unity runs every Update before any OnGUI.** Player.TakeInput (decompile 17774) and
    // PlayerController.TakeInput (22606) are both consulted from Update. So a token set while drawing is a
    // token set AFTER the game has already asked the question and already been told no. On the frame a
    // window opens - which is the frame the player clicked, which is the frame a mouse button is down - the
    // game reads that button as an attack. Every time. The window then behaves perfectly from the second
    // frame onward, which is exactly why this survives testing.
    //
    // AND THE CLICK ITSELF REMOVES THE COVER YOU DID NOT KNOW YOU HAD. Chat.Update (34306-34311) drops the
    // chat input's focus on ANY Mouse0 down while it is focused - SetSelectedGameObject(null) then
    // m_input.gameObject.SetActive(false). A window opened from a focused chat box was being covered by
    // vanilla's own Chat.HasFocus() the whole time it was being developed; the press that opens it is the
    // press that ends that.
    //
    // e.Use() DOES NOT HELP AND NEVER WILL. IMGUI events and ZInput are two independent readings of the same
    // physical button. Consuming an IMGUI event stops your other IMGUI handlers seeing it and tells the game
    // nothing whatsoever.
    //
    // So: raise the token from a per-frame Update hook, and raise it on HOVER rather than on click - if the
    // pointer is over your window's rect, claim, before there is a click to be late for. Last frame's rect
    // is the right thing to test against; chrome does not teleport. Two riders, both learned the hard way:
    //
    //   - **Gate the hover test on Cursor.visible.** Input.mousePosition does not stop reporting while the
    //     cursor is locked to the camera, it just keeps the value it had when the pointer was last free. A
    //     player who closed your window with the pointer resting on it would otherwise walk away unable to
    //     attack, with the answer never changing because the position never changes.
    //   - **Bound anything that latches.** A "the mouse is down on my chrome" flag cleared by a MouseUp your
    //     OnGUI might not run to see is unbounded, and the cost of it sticking is no longer "a panel lingers"
    //     but "the player cannot move, with nothing on screen to explain it". Give it a deadline.
    internal static class UIFocus
    {
        private static readonly HashSet<string> _cursorWindows = new HashSet<string>();
        private static readonly HashSet<string> _textFocusWindows = new HashSet<string>();
        private static readonly HashSet<string> _inputBlockWindows = new HashSet<string>();

        // Call every frame from a window's OnGUI (or Update) with a stable id for that window - typically
        // the window's own class name. Safe to call every frame with the same value; cheap HashSet churn.
        public static void SetWantsCursor(string windowId, bool active)
        {
            if (active) _cursorWindows.Add(windowId);
            else _cursorWindows.Remove(windowId);
        }

        public static void SetHasTextFocus(string windowId, bool active)
        {
            if (active) _textFocusWindows.Add(windowId);
            else _textFocusWindows.Remove(windowId);
        }

        // ---- windows that are not text fields but must still stop the game reading input -----------------
        //
        // WHY THIS IS NOT JUST SetHasTextFocus. Both end up in the same place - the Chat.HasFocus postfix
        // below, which is the choke point Player.TakeInput() and PlayerController.TakeInput() both consult -
        // but they answer DIFFERENT questions, and one caller in every consuming mod needs to tell them
        // apart: the gate that stands a hotkey down while the player is typing.
        //
        // A drag-to-arrange overlay is the case this exists for. It takes the mouse and the arrow keys, so
        // the game must not also read them as swinging an axe and walking - but nobody is TYPING, and a
        // hotkey has nothing to stand down for. Reporting it as text focus would jam that gate on for as
        // long as the overlay was up, which for an overlay whose only way out IS a hotkey means being
        // locked in. Two registries, one answer, and "is anyone typing" stays a question that can be asked.
        //
        // ⚠️ THIS ALSO STANDS DOWN VANILLA'S OWN PANEL HOTKEYS, and a consumer has to decide what to do
        // about it. InventoryGui.Update (41462) and Minimap.Update (47165) each open with
        // `!Chat.instance.HasFocus()`, so for as long as you hold this token Tab does not open the inventory
        // and M does not open the map. For a drag-to-arrange overlay that is a real problem - "open the
        // inventory and arrange it" is the entire workflow - and the answer is to relay those two keys
        // yourself while you hold the token, applying the same stand-downs vanilla applies (chat focused,
        // console visible, text input, minimap text input, menu visible) and calling ZInput.ResetButtonStatus
        // on the button so the press is not still pending when you let go. BarrkUI's
        // LayoutEditor.HandlePanelKeys is the worked example, and it relays for WHOEVER holds the token
        // rather than only for its own overlay. Two keys is enough; everything else on those screens is a
        // uGUI button driven by the EventSystem, which never went through TakeInput at all.
        //
        // See gotcha 3 in the header for WHEN to set this. Setting it from OnGUI is a frame too late.
        public static void SetBlocksGameInput(string windowId, bool active)
        {
            if (active) _inputBlockWindows.Add(windowId);
            else _inputBlockWindows.Remove(windowId);
        }

        public static bool WantsCursor => _cursorWindows.Count > 0;
        public static bool HasTextFocus => _textFocusWindows.Count > 0;
        public static bool BlocksGameInput => _inputBlockWindows.Count > 0;

        // ---- Harmony ----
        //
        // Self-installing: PatchAll(typeof(UIFocusPatch)) from the consuming mod's Awake is the only wiring
        // needed. Uses its own log source rather than the consuming mod's, so this file drops into any
        // project with zero per-project edits.
        [HarmonyPatch]
        internal static class UIFocusPatch
        {
            // Fully qualified: UnityEngine also declares a Logger type, and this file is compiled directly
            // into whichever project includes it, so a bare `Logger` is ambiguous the moment UnityEngine is
            // also in scope (as it always is here).
            private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("SharedUI.UIFocus");

            [HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
            [HarmonyPostfix]
            private static void Chat_HasFocus_Postfix(ref bool __result)
            {
                try
                {
                    if (HasTextFocus || BlocksGameInput) __result = true;
                }
                catch (Exception ex)
                {
                    Log.LogError($"{nameof(Chat_HasFocus_Postfix)} failed (non-fatal, keyboard may leak into gameplay while a window is focused). Reason: {ex}");
                }
            }

            [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
            [HarmonyPrefix]
            private static bool GameCamera_UpdateMouseCapture_Prefix()
            {
                try
                {
                    if (!WantsCursor) return true;

                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    return false;
                }
                catch (Exception ex)
                {
                    Log.LogError($"{nameof(GameCamera_UpdateMouseCapture_Prefix)} failed (non-fatal, falling back to vanilla cursor handling). Reason: {ex}");
                    return true;
                }
            }

            // Confirmed by compat audit (2026-08-04, BarrkUI vs Azumatt's AzuExtendedPlayerInventory): that
            // mod carries its OWN postfix on this exact method (JCBPUIUseBleedGuard, dormant unless
            // Jewelcrafting or Backpacks is also installed) that re-touches Cursor.lockState to keep the
            // cursor warped over inventory slots. A Harmony postfix always runs after the prefix - and after
            // the original body, skipped or not - so a sibling mod's postfix can silently undo our prefix's
            // unlock the instant both happen to be active at once. Harmony postfixes run low-priority-first,
            // high-priority-last, so a Priority.First postfix here is guaranteed to have the final word
            // regardless of what any other installed mod's postfix (at the Harmony default priority) does to
            // Cursor.lockState first. This is a narrow, two-line reassertion of our OWN already-decided state
            // against one confirmed real conflict - not the retracted "max-priority-prefix + min-priority-
            // postfix" pattern documented as an anti-pattern elsewhere (that one re-ran whole guard chains at
            // int.MaxValue/int.MinValue specifically to defeat sibling mods' ordering broadly; this is a
            // Priority.First reassertion of a value we already own, costing two field writes).
            [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
            [HarmonyPostfix]
            [HarmonyPriority(Priority.First)]
            private static void GameCamera_UpdateMouseCapture_Postfix()
            {
                try
                {
                    if (!WantsCursor) return;

                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                catch (Exception ex)
                {
                    Log.LogError($"{nameof(GameCamera_UpdateMouseCapture_Postfix)} failed (non-fatal, another mod's cursor handling may win this frame). Reason: {ex}");
                }
            }
        }
    }
}
