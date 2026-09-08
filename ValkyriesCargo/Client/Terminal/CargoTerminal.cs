using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Net;
using SharedUI;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Client.Terminal
{
    /// <summary>
    /// The Cargo Terminal (design 3.4): an IMGUI window on Wu'barrk's gilt theme that RENDERS the market
    /// and sends Deals. It never computes a price. Drawn from the mod's one OnGUI (CargoTick), driven from
    /// the mod's one Update (the cursor and input tokens are raised there, never from OnGUI: UIFocus's
    /// gotcha 3), closed by the panel rules: Escape, Use, Tab or M, the player dead, more than 5 m from
    /// the merchant, the visit leaving or over. Opened by the merchant's interact (P5) or by
    /// `cargo terminal demo|open`. The inventory is written only inside the answer, through DealApplier.
    ///
    /// The playtest of 2026-09-08 reshaped the lower half: the Coins/Barter switch is gone and the tray is
    /// always YOU GET beside YOU GIVE with one balance line (item 5); every staged line carries a count box
    /// and an "all" button (item 4); the window sits on a translucent black backdrop with brighter text
    /// (item 7), set through the theme's own options and never by editing the vendored theme.
    /// </summary>
    internal sealed class CargoTerminal : ICargoTerminal
    {
        /// <summary>Carries the mod's name: UIFocus is a registry shared between mods.</summary>
        public const string WindowId = "ValkyriesCargo_CargoTerminal";
        public const float CloseDistance = 5f;
        public const float DismissArmSeconds = 5f;
        public const float DismissAnswerSeconds = 4f;   // how long the footer waits on VCargo_dismissed before it says so
        public const float CountRefreshSeconds = 0.25f;
        /// <summary>The tray shows this many lines a side before YOU GIVE scrolls; its height never moves.</summary>
        public const int TrayRows = 4;
        public const float TrayRowHeight = 26f;
        /// <summary>IMGUI control names for the count boxes, prefixed with the mod's name like the window id.</summary>
        public const string CountBoxPrefix = "VCargo_count_";
        private const string HexAmber = "#E0A23C";
        private const string HexDim = "#B8AE9A";

        public static CargoTerminal Instance { get; private set; }

        /// <summary>Plugin Awake, where a player can be drawn: one terminal, registered for the merchant to call.</summary>
        public static void Install()
        {
            if (Instance != null) return;
            Instance = new CargoTerminal();
            CargoTerminalHost.Instance = Instance;
        }

        private readonly TrayModel _tray = new TrayModel();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private GameObject _merchant;
        /// <summary>Opened standing by a merchant. `_merchant != null` cannot answer this: Unity's operator
        /// reads a DESTROYED object as null, so a merchant despawned mid-trade would silently turn the 5 m
        /// rule off instead of closing the window. `cargo terminal open` opens with no merchant on purpose.</summary>
        private bool _hasMerchant;
        private int _visitId;
        private bool _demo;
        private float _openedAt;
        private float _countAge;
        private float _dismissArmedUntil;
        private float _dismissSentAt;                     // > 0 while a dismiss is on the wire without its answer
        private bool _awaiting;
        private int _coins;
        private Rect _rect;
        private Vector2 _scrollWares, _scrollWants, _scrollTray;
        private int _throws;
        /// <summary>The count box being typed in (its IMGUI control name) and the digits typed so far: the box
        /// shows these while it has the keyboard, so it can be emptied and retyped; the tray's own count is
        /// what it shows otherwise. A clamp writes the clamped number back here.</summary>
        private string _editName = "";
        private string _editText = "";
        /// <summary>Read in Draw (IMGUI answers it only there), raised as the text-focus token from Tick, and
        /// the reason Use, Tab and M do not close the window while a count is being typed.</summary>
        private bool _fieldFocused;
        /// <summary>Where the focused box is on screen: a click anywhere else hands the keyboard back.</summary>
        private Rect _focusedBox;
        private GUIStyle _small, _smallFrom;

        public bool IsOpen { get; private set; }
        public string LastCloseReason { get; private set; } = "";
        public int Opens { get; private set; }
        public bool IsDemo => _demo;
        public TrayModel Tray => _tray;

        // ---- ICargoTerminal -----------------------------------------------------------------------------

        public void Open(GameObject merchant, int visitId) { OpenInternal(merchant, visitId, false); }

        public void Close() { Close("closed"); }

        /// <summary>`cargo terminal demo`: the in-process market, no server, no merchant, no 5 m rule.</summary>
        public void OpenDemo()
        {
            CargoRpc.UseDemo(true);
            OpenInternal(null, CargoRpc.Visit.VisitId, true);
        }

        private void OpenInternal(GameObject merchant, int visitId, bool demo)
        {
            _merchant = merchant;
            _hasMerchant = merchant != null;
            _visitId = visitId;
            _demo = demo;
            _openedAt = Time.time;
            _dismissArmedUntil = 0f;
            _dismissSentAt = 0f;
            _awaiting = false;
            _countAge = CountRefreshSeconds;
            _tray.Clear();
            _editName = ""; _editText = ""; _fieldFocused = false;
            _scrollTray = Vector2.zero;
            _tray.Message = Lines.Open;
            IsOpen = true;
            Opens++;
            CargoRpc.Open(visitId);
            ValkyriesCargo.Log.LogInfo("terminal opened: visit #" + visitId + (demo ? " (demo)" : merchant != null ? " on " + merchant.name : " with no merchant"));
        }

        public void Close(string why)
        {
            if (!IsOpen) return;
            IsOpen = false;
            LastCloseReason = why ?? "";
            _fieldFocused = false;
            UIFocus.SetWantsCursor(WindowId, false);
            UIFocus.SetBlocksGameInput(WindowId, false);
            UIFocus.SetHasTextFocus(WindowId, false);
            CargoRpc.Close(_visitId);
            if (_demo) CargoRpc.UseDemo(false);
            ValkyriesCargo.Log.LogInfo("terminal closed: " + LastCloseReason);
        }

        /// <summary>Session end (logout): drop everything, release the cursor.</summary>
        public void Reset()
        {
            Close("session ended");
            _merchant = null;
            _hasMerchant = false;
            _counts.Clear();
        }

        // ---- Update side: tokens and the panel rules -------------------------------------------------------

        public void Tick(float dt)
        {
            if (!IsOpen) return;
            try
            {
                UIFocus.SetWantsCursor(WindowId, true);
                UIFocus.SetBlocksGameInput(WindowId, true);
                // The third registry, for an actual focused text field only (the knowledge base's warning):
                // the count box. Raised here, from Update, never from OnGUI.
                UIFocus.SetHasTextFocus(WindowId, _fieldFocused);

                // ZInput, not UnityEngine.Input: this build reads every key through ZInput's new-Input-System
                // wrapper (Menu.Update 307/364, FejdStartup 1702, InventoryGui 396, Minimap 616) and reads no
                // legacy Input anywhere, so whether the legacy manager is even enabled is not a thing to bet
                // the one way out of this window on. ZInput is null-safe and is initialised in FejdStartup.Awake
                // (314), so this works at the main menu too, which is where `cargo terminal demo` has to work.
                if (ZInput.GetKeyDown(KeyCode.Escape)) { Close("escape"); return; }
                // Every relayed button is stood down with ResetButtonStatus. Closing releases the
                // BlocksGameInput token in the same frame, and MonoBehaviour Update order is undefined:
                // InventoryGui.Update and Minimap.Update both open on `!Chat.HasFocus()` plus a still-
                // pressed ZInput button, so without this Tab closes the terminal AND opens the inventory,
                // M closes it AND opens the map. (UIFocus's own header prescribes this for a token holder.)
                // Not while a count box has the keyboard: E, Tab and M are then letters being typed (only digits
                // reach the tray, but the key still lands here). Escape closes either way, and Enter or a click
                // elsewhere hands the keyboard back (Draw).
                if (!_fieldFocused)
                {
                    if (ZInput.GetButtonDown("Use")) { ZInput.ResetButtonStatus("Use"); Close("use"); return; }
                    if (ZInput.GetButtonDown("Inventory")) { ZInput.ResetButtonStatus("Inventory"); Close("inventory"); return; }
                    if (ZInput.GetButtonDown("Map")) { ZInput.ResetButtonStatus("Map"); Close("map"); return; }
                }
                if (InventoryGui.IsVisible() || Minimap.IsOpen() || Menu.IsVisible()) { Close("a vanilla screen opened"); return; }

                Player p = Player.m_localPlayer;
                if (!_demo)
                {
                    if (p == null || p.IsDead()) { Close("player gone"); return; }
                    if (_hasMerchant)
                    {
                        if (_merchant == null) { Close("he is gone"); return; }
                        if (Vector3.Distance(p.transform.position, _merchant.transform.position) > CloseDistance) { Close("too far"); return; }
                    }
                    VisitSnapshot v = CargoRpc.Visit;
                    if (!v.Active || v.VisitId != _visitId) { Close("visit over"); return; }
                    if (v.Phase == VisitPhase.Leaving) { Close("he is leaving"); return; }
                }

                if (_dismissArmedUntil > 0f && Time.time > _dismissArmedUntil) { _dismissArmedUntil = 0f; }
                if (_dismissSentAt > 0f && Time.time - _dismissSentAt > DismissAnswerSeconds)
                {
                    _dismissSentAt = 0f;
                    _tray.Message = Lines.Refusal(DealReason.NoAnswer);
                    ValkyriesCargo.Log.LogWarning("terminal: no answer to the dismiss in " + DismissAnswerSeconds + " s");
                }

                _countAge += dt;
                if (_countAge >= CountRefreshSeconds)
                {
                    _countAge = 0f;
                    RefreshCounts(p);
                }
                _tray.Refresh(CargoRpc.Market);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("terminal tick threw: " + ex);
                Close("error");
            }
        }

        private void RefreshCounts(Player p)
        {
            _counts.Clear();
            if (_demo) { _coins = CargoRpc.Demo != null ? CargoRpc.Demo.PlayerCoins : 0; return; }
            Inventory inv = p != null ? p.GetInventory() : null;
            if (inv == null) { _coins = 0; return; }
            _coins = DealApplier.Count(inv, DealApplier.CoinsPrefab);
            foreach (MarketRow r in CargoRpc.Market.Rows) _counts[r.Prefab] = DealApplier.Count(inv, r.Prefab);
        }

        private int Has(string prefab)
        {
            int n;
            return _counts.TryGetValue(prefab, out n) ? n : 0;
        }

        // ---- OnGUI side ---------------------------------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;
            try
            {
                GiltFrameTheme.EnsureBuilt(Theme());
                Layout();
                Event ev = Event.current;
                // A click anywhere but the focused count box hands the keyboard back before the click lands.
                if (_fieldFocused && ev != null && ev.type == EventType.MouseDown && !_focusedBox.Contains(ev.mousePosition))
                    GUIUtility.keyboardControl = 0;
                GiltFrameTheme.DrawWindow(_rect, Lines.Title + "  -  Valkyrie's Cargo");
                DrawTitleBar();
                Rect body = GiltFrameTheme.Body(_rect);
                float S(float v) => GiltFrameTheme.S(v);

                MarketSnapshot m = CargoRpc.Market;

                // Header line: his purse, your coins, the countdown. The Coins/Barter switch lived here until
                // 2026-09-08 (the playtest's item 5): the tray now always shows both sides and one balance line.
                Rect header = new Rect(body.x, body.y, body.width, S(28f));
                GUI.Label(new Rect(header.x, header.y, S(220f), header.height), "His purse " + m.Purse + "c", GiltFrameTheme.Value);
                GUI.Label(new Rect(header.x + S(230f), header.y, S(220f), header.height), "Your coins " + _coins + "c", GiltFrameTheme.Value);
                string clock = VisitClock.Format(Remaining()) + " left";
                GUI.Label(new Rect(body.xMax - S(140f), header.y, S(140f), header.height), clock, GiltFrameTheme.SubTitle);

                // Two panes over a tray of fixed height, so nothing above jumps as lines come and go.
                float paneTop = header.yMax + S(8f);
                float footerH = S(26f);
                float trayH = S(30f) + TrayRows * S(TrayRowHeight) + S(6f);
                float buttonsH = S(28f);
                float paneH = body.yMax - paneTop - trayH - buttonsH - footerH - S(26f);
                float gap = S(12f);
                float paneW = (body.width - gap) * 0.5f;
                Rect left = new Rect(body.x, paneTop, paneW, paneH);
                Rect right = new Rect(body.x + paneW + gap, paneTop, paneW, paneH);
                DrawWares(left, m);
                DrawWants(right, m);

                // The tray, then the buttons with the balance line between them.
                Rect tray = new Rect(body.x, left.yMax + S(8f), body.width, trayH);
                DrawTray(tray, m);
                Rect buttons = new Rect(body.x, tray.yMax + S(6f), body.width, buttonsH);
                DrawButtons(buttons, m);

                // Footer: his words, or why the last confirm stopped.
                Rect foot = GiltFrameTheme.FooterLine(_rect);
                GUI.Label(foot, _awaiting ? "..." : _tray.Message, GiltFrameTheme.Footer);

                // Which control has the keyboard is a question only OnGUI can answer; Tick raises the token.
                string focused = GUI.GetNameOfFocusedControl();
                _fieldFocused = !string.IsNullOrEmpty(focused) && focused.StartsWith(CountBoxPrefix, StringComparison.Ordinal);
                if (_fieldFocused && ev != null && ev.type == EventType.KeyDown &&
                    (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter))
                {
                    GUIUtility.keyboardControl = 0;
                    _fieldFocused = false;
                    _editName = ""; _editText = "";
                    ev.Use();
                }
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("terminal draw threw: " + ex);
                GUI.enabled = true;
                Close("error");
            }
        }

        private ThemeOptions Theme()
        {
            ThemeOptions o = ThemeOptions.Default;
            o.Scale = Mathf.Clamp(ModConfig.TerminalScale.Value, 0.5f, 2f);
            if (ModConfig.Theme.Value == "Vanilla") o.Metal = new Color(0.66f, 0.48f, 0.26f, 1f);   // Valheim's bronze; BlackGold is the theme's own gilt
            // The backdrop (2026-09-08, the playtest's item 7): the theme's near-black panel at
            // Client.TerminalBackdropAlpha (0.4 = a 40% translucent black) instead of its 0.955, and both text
            // tones a step brighter. All through the theme's own options; the vendored file is not edited. The
            // panel keeps its warm near-black rather than pure black because every button and field surface is
            // derived from the panel colour (GiltFrameTheme.Surface), and black times anything is black.
            o.PanelOpacity = Mathf.Clamp01(ModConfig.TerminalBackdropAlpha.Value);
            o.Text = new Color(0.97f, 0.94f, 0.86f, 1f);
            o.MutedText = new Color(0.78f, 0.74f, 0.64f, 1f);
            return o;
        }

        private void Layout()
        {
            float w = Mathf.Min(Screen.width - 40f, GiltFrameTheme.S(980f));
            float h = Mathf.Min(Screen.height - 40f, GiltFrameTheme.S(700f));
            _rect = new Rect(Mathf.Round((Screen.width - w) * 0.5f), Mathf.Round((Screen.height - h) * 0.5f), w, h);
        }

        private void DrawTitleBar()
        {
            float s = GiltFrameTheme.S(26f);
            Rect x = new Rect(_rect.xMax - GiltFrameTheme.Band - GiltFrameTheme.Pad - s, _rect.y + GiltFrameTheme.Band + 12f, s, s);
            if (GUI.Button(x, "X", GiltFrameTheme.Button)) Close("close button");
        }

        private void DrawWares(Rect r, MarketSnapshot m)
        {
            float S(float v) => GiltFrameTheme.S(v);
            GUI.Label(new Rect(r.x, r.y, r.width, S(22f)), "HIS WARES", GiltFrameTheme.Header);
            GUI.Label(new Rect(r.xMax - S(150f), r.y, S(150f), S(22f)), "stock      price", GiltFrameTheme.Key);
            Rect well = new Rect(r.x, r.y + S(24f), r.width, r.height - S(24f));
            GiltFrameTheme.DrawInset(well);
            float rowH = S(30f);
            int rows = 0;
            foreach (MarketRow row in m.Rows) if (row.Kind == EntryKind.Ware) rows++;
            Rect view = new Rect(0, 0, well.width - S(16f), Mathf.Max(well.height, rows * rowH));
            _scrollWares = GUI.BeginScrollView(well, _scrollWares, view);
            try
            {
            float y = 0f;
            foreach (MarketRow row in m.Rows)
            {
                if (row.Kind != EntryKind.Ware) continue;
                Rect line = new Rect(0, y, view.width, rowH);
                if (RightClicked(line)) _tray.Unstage(row.Prefab, StageCount());
                bool clicked = GUI.Button(line, GUIContent.none, GiltFrameTheme.Row);
                DrawIcon(new Rect(line.x + S(4f), line.y + S(2f), rowH - S(4f), rowH - S(4f)), row.Prefab);
                GUI.Label(new Rect(line.x + rowH + S(6f), line.y, line.width - rowH - S(160f), rowH), Name(row.Prefab), GiltFrameTheme.Value);
                string stock = row.Stock == 0 ? "SOLD" : row.Stock + "/" + row.Target;
                GUI.Label(new Rect(line.xMax - S(150f), line.y, S(70f), rowH), stock, row.Stock == 0 ? GiltFrameTheme.Key : GiltFrameTheme.Value);
                GUI.Label(new Rect(line.xMax - S(76f), line.y, S(70f), rowH), row.Buy + "c " + Trend(row), GiltFrameTheme.Value);
                if (clicked) Stage(row, true);
                y += rowH;
            }
            }
            finally { GUI.EndScrollView(); }
        }

        private void DrawWants(Rect r, MarketSnapshot m)
        {
            float S(float v) => GiltFrameTheme.S(v);
            GUI.Label(new Rect(r.x, r.y, r.width, S(22f)), "YOUR GOODS HE WANTS", GiltFrameTheme.Header);
            GUI.Label(new Rect(r.xMax - S(190f), r.y, S(190f), S(22f)), "you have   his shelf   pays", GiltFrameTheme.Key);
            Rect well = new Rect(r.x, r.y + S(24f), r.width, r.height - S(24f));
            GiltFrameTheme.DrawInset(well);
            float rowH = S(30f);
            Rect view = new Rect(0, 0, well.width - S(16f), Mathf.Max(well.height, m.Count * rowH));
            _scrollWants = GUI.BeginScrollView(well, _scrollWants, view);
            try
            {
            float y = 0f;
            foreach (MarketRow row in m.Rows)
            {
                int has = Has(row.Prefab);
                Rect line = new Rect(0, y, view.width, rowH);
                if (RightClicked(line)) _tray.Unstage(row.Prefab, StageCount());
                bool clicked = GUI.Button(line, GUIContent.none, GiltFrameTheme.Row);
                DrawIcon(new Rect(line.x + S(4f), line.y + S(2f), rowH - S(4f), rowH - S(4f)), row.Prefab);
                GUI.Label(new Rect(line.x + rowH + S(6f), line.y, line.width - rowH - S(200f), rowH), Name(row.Prefab), has > 0 ? GiltFrameTheme.Value : GiltFrameTheme.Key);
                GUI.Label(new Rect(line.xMax - S(190f), line.y, S(60f), rowH), has > 0 ? "x" + has : "-", GiltFrameTheme.Value);
                bool full = row.Stock >= row.Max;
                GUI.Label(new Rect(line.xMax - S(128f), line.y, S(70f), rowH), row.Stock + "/" + row.Max + (full ? " FULL" : ""), full ? GiltFrameTheme.Key : GiltFrameTheme.Value);
                GUI.Label(new Rect(line.xMax - S(56f), line.y, S(52f), rowH), row.Sell + "c", GiltFrameTheme.Value);
                if (clicked) Stage(row, false);
                y += rowH;
            }
            }
            finally { GUI.EndScrollView(); }
        }

        /// <summary>One per click; Shift five; Ctrl twenty.</summary>
        private static int StageCount()
        {
            Event e = Event.current;
            return e != null && e.control ? 20 : e != null && e.shift ? 5 : 1;
        }

        /// <summary>A right mouse-down on the rect, consumed so the button underneath never sees it.</summary>
        private static bool RightClicked(Rect r)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.MouseDown || e.button != 1 || !r.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        /// <summary>A left click stages (one, Shift five, Ctrl twenty); a right click took back, above.</summary>
        private void Stage(MarketRow row, bool buy)
        {
            int n = StageCount();
            bool ok = buy ? _tray.StageBuy(row, n) : _tray.StageOffer(row, n, Has(row.Prefab));
            if (!ok) _tray.Message = buy ? (row.Kind != EntryKind.Ware ? "He buys that; he does not sell it." : "That shelf is bare.")
                                        : (Has(row.Prefab) < 1 ? "You carry none of those." : Lines.RefuseFull);
        }

        /// <summary>
        /// The tray (design 3.4, reshaped 2026-09-08): YOU GET on the left (the one wanted line), YOU GIVE on
        /// the right (the offered lines, scrolling past TrayRows), each line with its count box, "all" and x.
        /// </summary>
        private void DrawTray(Rect r, MarketSnapshot m)
        {
            float S(float v) => GiltFrameTheme.S(v);
            GiltFrameTheme.DrawRule(new Rect(r.x, r.y, r.width, 1f));
            float y = r.y + S(6f);
            float gap = S(12f);
            float colW = (r.width - gap) * 0.5f;
            float rowH = S(TrayRowHeight);
            Rect getCol = new Rect(r.x, y, colW, S(22f));
            Rect giveCol = new Rect(r.x + colW + gap, y, colW, S(22f));
            GUI.Label(new Rect(getCol.x, getCol.y, S(120f), getCol.height), "YOU GET", GiltFrameTheme.Header);
            GUI.Label(new Rect(giveCol.x, giveCol.y, S(120f), giveCol.height), "YOU GIVE", GiltFrameTheme.Header);
            foreach (Rect col in new[] { getCol, giveCol })
            {
                GUI.Label(new Rect(col.xMax - S(212f), col.y, S(60f), col.height), "count", GiltFrameTheme.Key);
                GUI.Label(new Rect(col.xMax - S(74f), col.y, S(70f), col.height), "value", GiltFrameTheme.Key);
            }

            float wellH = TrayRows * rowH + S(4f);
            Rect getWell = new Rect(getCol.x, getCol.yMax + S(2f), colW, wellH);
            Rect giveWell = new Rect(giveCol.x, giveCol.yMax + S(2f), colW, wellH);
            GiltFrameTheme.DrawInset(getWell);
            GiltFrameTheme.DrawInset(giveWell);

            if (_tray.Wanted != null)
                DrawTrayLine(new Rect(getWell.x + S(2f), getWell.y + S(2f), getWell.width - S(4f), rowH), _tray.Wanted, m, true, Vector2.zero);
            else
                GUI.Label(new Rect(getWell.x + S(8f), getWell.y, getWell.width - S(16f), getWell.height),
                          "click a ware on the left to buy it\nShift = 5, Ctrl = 20; type a count, or press all", GiltFrameTheme.Note);

            if (_tray.Offered.Count == 0)
            {
                GUI.Label(new Rect(giveWell.x + S(8f), giveWell.y, giveWell.width - S(16f), giveWell.height),
                          "click one of your goods on the right to offer it\nright-click takes one back; x clears the line", GiltFrameTheme.Note);
                return;
            }
            Rect inner = new Rect(giveWell.x + S(2f), giveWell.y + S(2f), giveWell.width - S(4f), giveWell.height - S(4f));
            int n = _tray.Offered.Count;
            bool scrolls = n > TrayRows;
            Rect view = new Rect(0, 0, inner.width - (scrolls ? S(16f) : 0f), Mathf.Max(inner.height, n * rowH));
            _scrollTray = GUI.BeginScrollView(inner, _scrollTray, view);
            try
            {
                Vector2 origin = inner.position - _scrollTray;   // view-local to screen, for the focused box
                for (int i = 0; i < n && i < _tray.Offered.Count; i++)
                {
                    TrayLine l = _tray.Offered[i];
                    DrawTrayLine(new Rect(0, i * rowH, view.width, rowH), l, m, false, origin);
                    if (i >= _tray.Offered.Count || _tray.Offered[i] != l) break;   // x took it out; the rest redraws next frame
                }
            }
            finally { GUI.EndScrollView(); }
        }

        /// <summary>
        /// One staged line: icon, name and unit price, the count box (item 4), "all", x, and the line's value
        /// now (amber where the price moved since it was staged). `origin` maps the box to screen space when
        /// the line is drawn inside the scroll view, so a click elsewhere can be told from a click on it.
        /// </summary>
        private void DrawTrayLine(Rect line, TrayLine l, MarketSnapshot m, bool get, Vector2 origin)
        {
            float S(float v) => GiltFrameTheme.S(v);
            float h = line.height;
            DrawIcon(new Rect(line.x + S(2f), line.y + S(2f), h - S(4f), h - S(4f)), l.Prefab);

            float right = line.xMax;
            string value = l.ValueNow + "c";
            if (l.Amber) value = "<color=" + HexAmber + ">" + value + "</color>";
            GUI.Label(new Rect(right - S(74f), line.y, S(72f), h), value, GiltFrameTheme.Value);
            right -= S(78f);

            GUIStyle small = SmallButton();
            if (GUI.Button(new Rect(right - S(24f), line.y + S(2f), S(24f), h - S(4f)), "x", small)) { _tray.Remove(l.Prefab); return; }
            right -= S(28f);
            if (GUI.Button(new Rect(right - S(40f), line.y + S(2f), S(40f), h - S(4f)), "all", small))
            {
                _tray.AllOf(m, l.Prefab, Has, _coins);
                if (_editName.Length > 0) { GUIUtility.keyboardControl = 0; _editName = ""; _editText = ""; }   // the box shows the tray's count again
            }
            right -= S(44f);

            // The count box. It shows the tray's count until the player types, then what they have typed
            // (digits only; empty is allowed while typing and changes nothing); a number the tray clamps is
            // written back so the box says what he will actually take.
            Rect box = new Rect(right - S(58f), line.y + S(2f), S(56f), h - S(4f));
            string name = CountBoxPrefix + (get ? "get_" : "give_") + l.Prefab;
            GUI.SetNextControlName(name);
            bool focused = GUI.GetNameOfFocusedControl() == name;
            string shown = focused && _editName == name ? _editText : l.Count.ToString();
            string typed = GUI.TextField(box, shown, 6, GiltFrameTheme.Field);
            if (focused) _focusedBox = new Rect(box.x + origin.x, box.y + origin.y, box.width, box.height);
            if (typed != shown)
            {
                string digits = Digits(typed);
                _editName = name;
                _editText = digits;
                int want;
                if (digits.Length > 0 && int.TryParse(digits, out want) && want >= 1)
                {
                    int applied = _tray.SetCount(m, l.Prefab, want, Has);
                    if (applied != want) _editText = applied.ToString();
                }
            }
            else if (!focused && _editName == name) { _editName = ""; _editText = ""; }
            right -= S(62f);

            GUI.Label(new Rect(line.x + h + S(2f), line.y, Mathf.Max(0f, right - line.x - h - S(4f)), h),
                      Name(l.Prefab) + "  <color=" + HexDim + ">@ " + l.UnitPriceNow + "c</color>", GiltFrameTheme.Value);
        }

        /// <summary>The theme's button with room for a two-letter face; rebuilt whenever the theme rebuilds its styles.</summary>
        private GUIStyle SmallButton()
        {
            if (_small == null || !ReferenceEquals(_smallFrom, GiltFrameTheme.Button))
            {
                _smallFrom = GiltFrameTheme.Button;
                _small = new GUIStyle(GiltFrameTheme.Button) { padding = new RectOffset(2, 2, 2, 2) };
            }
            return _small;
        }

        private static string Digits(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Confirm, Clear, "Cover it with my goods", the one balance line (item 5), and Send him off.</summary>
        private void DrawButtons(Rect r, MarketSnapshot m)
        {
            float S(float v) => GiltFrameTheme.S(v);
            float bh = r.height, bx = r.x;
            try
            {
                GUI.enabled = !_awaiting && !_tray.IsEmpty;
                if (GUI.Button(new Rect(bx, r.y, S(150f), bh), _tray.AnyAmber ? "Confirm new price" : "Confirm deal", GiltFrameTheme.Primary)) Confirm(m);
                GUI.enabled = !_awaiting;
                bx += S(158f);
                if (GUI.Button(new Rect(bx, r.y, S(90f), bh), "Clear", GiltFrameTheme.Button)) { _tray.Clear(); _tray.Message = ""; }
                bx += S(98f);
                // Whenever a ware is staged (no mode to switch into any more), unless the server turned barter off.
                if (ModConfig.EnableBarter.Value && _tray.Wanted != null)
                {
                    if (GUI.Button(new Rect(bx, r.y, S(190f), bh), "Cover it with my goods", GiltFrameTheme.Button))
                    {
                        int n = _tray.AutoFill(m, Has);
                        _tray.Message = n > 0 ? "Offered " + n + " kind(s) of your goods against it." : "Nothing of yours covers it.";
                    }
                    bx += S(198f);
                }
            }
            finally { GUI.enabled = true; }

            // The one balance line: who pays whom the difference, at the numbers on screen now.
            long net = _tray.Net;
            string balance = _tray.IsEmpty ? "" : net > 0 ? "you pay " + net + "c" : net < 0 ? "he pays you " + (-net) + "c" : "even";
            if (_tray.AnyAmber && balance.Length > 0) balance += "  <color=" + HexAmber + ">(a price moved)</color>";
            GUI.Label(new Rect(bx, r.y, Mathf.Max(0f, r.xMax - S(158f) - bx), bh), balance, GiltFrameTheme.SubTitle);

            string dismiss = _dismissSentAt > 0f ? "Sending him off" : _dismissArmedUntil > 0f ? "Ask once more" : "Send him off";
            if (GUI.Button(new Rect(r.xMax - S(150f), r.y, S(150f), bh), dismiss, GiltFrameTheme.Button)) DismissPressed();
        }

        private void Confirm(MarketSnapshot m)
        {
            string why = _tray.Validate(m, _coins, Has, ModConfig.EnableBarter.Value);
            if (why != null) { _tray.Message = TrayModel.Words(why); return; }
            Deal d = _tray.Build(_visitId);
            if (!_demo)
            {
                string cannot = DealApplier.CanApply(Provisional(d));
                if (cannot != null) { _tray.Message = Lines.Refusal(cannot); return; }
            }
            _awaiting = true;
            int visit = _visitId;
            CargoRpc.Send(d, r =>
            {
                _awaiting = false;
                try
                {
                    if (r != null && r.Reason == DealReason.PriceChanged && !string.IsNullOrEmpty(r.NewMarketState)) CargoRpc.PublishMarket(r.NewMarketState);
                    bool applied = r != null && r.Ok && (_demo || DealApplier.Apply(r));
                    _tray.Answer(r, CargoRpc.Market);
                    if (r != null && r.Ok && !applied) _tray.Message = "The deal went through but your pack refused it; the server keeps it for you (cargo claim).";
                    if (r != null && r.Ok) _countAge = CountRefreshSeconds;   // re-count at once
                    ValkyriesCargo.Log.LogInfo("terminal deal on visit #" + visit + ": " + (r != null ? (r.Ok ? "ok " + r.DeliveryId + " " + DealApplier.Describe(r) : r.Reason) : "no answer"));
                }
                catch (Exception ex)
                {
                    if (_throws++ < 3) ValkyriesCargo.Log.LogError("terminal answer threw: " + ex);
                }
            });
        }

        /// <summary>What the deal would do to the inventory if the server says yes: the pre-check's input.</summary>
        private static DealResult Provisional(Deal d)
        {
            var r = new DealResult { Ok = true, Nonce = d.Nonce, DeliveryId = "pre" };
            if (d.Wanted != null) r.ItemsToAdd.Add(d.Wanted);
            foreach (DealLine l in d.Offered) r.ItemsToRemove.Add(l);
            long offered = 0; foreach (DealLine l in d.Offered) offered += (long)l.Count * l.UnitPriceSeen;
            long price = d.Wanted != null ? (long)d.Wanted.Count * d.Wanted.UnitPriceSeen : 0;
            r.CoinsDelta = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, offered - price));
            return r;
        }

        private void DismissPressed()
        {
            if (_dismissArmedUntil <= 0f)
            {
                _dismissArmedUntil = Time.time + DismissArmSeconds;
                _tray.Message = Lines.DismissFirst;
                return;
            }
            if (_dismissSentAt > 0f) return;                      // one is on the wire; its answer decides
            _dismissArmedUntil = 0f;
            _dismissSentAt = Time.time;
            _tray.Message = Lines.DismissSent;
            int visit = _visitId;
            CargoRpc.Dismiss(visit, reason => OnDismissed(visit, reason));
        }

        /// <summary>
        /// The server's one answer to the dismiss (VCargo_dismissed). Ok: the farewell and the window closes;
        /// anything else stays open with his words for it. Until 2026-09-08 the window closed on trust and
        /// said the farewell while the server was refusing (visit 21: 134 m from the drop point).
        /// </summary>
        private void OnDismissed(int visit, string reason)
        {
            if (!IsOpen || visit != _visitId) return;
            _dismissSentAt = 0f;
            if (reason == DealReason.Ok) { _tray.Message = Lines.Farewell; Close("sent him off"); return; }
            _tray.Message = Lines.Refusal(reason);
            ValkyriesCargo.Log.LogInfo("terminal: dismiss of visit #" + visit + " refused: " + reason);
        }

        // ---- helpers -----------------------------------------------------------------------------------

        private double Remaining()
        {
            if (_demo) return Math.Max(0.0, CargoRpc.Visit.EndWorldTime - (Time.time - _openedAt));
            return ZNet.instance != null ? CargoRpc.Visit.Remaining(ZNet.instance.GetTimeSeconds()) : 0.0;
        }

        private static string Trend(MarketRow r)
        {
            if (!ModConfig.ShowPriceTrend.Value) return "";
            return r.Trend > 0 ? "▲" : r.Trend < 0 ? "▼" : "";
        }

        private string Name(string prefab)
        {
            string name;
            if (_names.TryGetValue(prefab, out name)) return name;
            name = prefab;
            try
            {
                string shared = DealApplier.SharedName(prefab);
                if (!string.IsNullOrEmpty(shared) && Localization.instance != null) name = Localization.instance.Localize(shared);
            }
            catch { }
            if (string.IsNullOrEmpty(name)) name = prefab;
            _names[prefab] = name;
            return name;
        }

        /// <summary>The item's icon from its sprite's own rect of the atlas page (Sprite.texture is the whole page).</summary>
        private void DrawIcon(Rect r, string prefab)
        {
            Sprite s;
            if (!_icons.TryGetValue(prefab, out s))
            {
                s = null;
                try
                {
                    GameObject go = DealApplier.Prefab(prefab);
                    ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (drop != null && drop.m_itemData != null) s = drop.m_itemData.GetIcon();
                }
                catch { }
                _icons[prefab] = s;
            }
            if (s == null || s.texture == null) return;
            Texture2D t = s.texture;
            Rect tr = s.textureRect;
            GUI.DrawTextureWithTexCoords(r, t, new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height));
        }
    }
}
