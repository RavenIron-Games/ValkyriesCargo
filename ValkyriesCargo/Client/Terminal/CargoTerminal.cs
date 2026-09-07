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
    /// </summary>
    internal sealed class CargoTerminal : ICargoTerminal
    {
        /// <summary>Carries the mod's name: UIFocus is a registry shared between mods.</summary>
        public const string WindowId = "ValkyriesCargo_CargoTerminal";
        public const float CloseDistance = 5f;
        public const float DismissArmSeconds = 5f;
        public const float CountRefreshSeconds = 0.25f;

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
        private bool _awaiting;
        private int _coins;
        private Rect _rect;
        private Vector2 _scrollWares, _scrollWants;
        private int _throws;

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
            _awaiting = false;
            _countAge = CountRefreshSeconds;
            _tray.Clear();
            _tray.Mode = PayMode.Coins;
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
            UIFocus.SetWantsCursor(WindowId, false);
            UIFocus.SetBlocksGameInput(WindowId, false);
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
                if (ZInput.GetButtonDown("Use")) { ZInput.ResetButtonStatus("Use"); Close("use"); return; }
                if (ZInput.GetButtonDown("Inventory")) { ZInput.ResetButtonStatus("Inventory"); Close("inventory"); return; }
                if (ZInput.GetButtonDown("Map")) { ZInput.ResetButtonStatus("Map"); Close("map"); return; }
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
                GiltFrameTheme.DrawWindow(_rect, Lines.Title + "  -  Valkyrie's Cargo");
                DrawTitleBar();
                Rect body = GiltFrameTheme.Body(_rect);
                float S(float v) => GiltFrameTheme.S(v);

                MarketSnapshot m = CargoRpc.Market;

                // Header line: purse, pay mode, countdown.
                Rect header = new Rect(body.x, body.y, body.width, S(28f));
                GUI.Label(new Rect(header.x, header.y, S(220f), header.height), "Purse " + m.Purse + "c", GiltFrameTheme.Value);
                float bx = header.x + S(230f);
                GUI.Label(new Rect(bx, header.y, S(70f), header.height), "Pay with:", GiltFrameTheme.Key);
                bx += S(76f);
                if (GUI.Button(new Rect(bx, header.y, S(120f), header.height), "Coins " + _coins, _tray.Mode == PayMode.Coins ? GiltFrameTheme.Primary : GiltFrameTheme.Button)) _tray.Mode = PayMode.Coins;
                bx += S(126f);
                if (ModConfig.EnableBarter.Value &&
                    GUI.Button(new Rect(bx, header.y, S(120f), header.height), "Barter", _tray.Mode == PayMode.Barter ? GiltFrameTheme.Primary : GiltFrameTheme.Button)) _tray.Mode = PayMode.Barter;
                string clock = VisitClock.Format(Remaining()) + " left";
                GUI.Label(new Rect(body.xMax - S(140f), header.y, S(140f), header.height), clock, GiltFrameTheme.SubTitle);

                // Two panes.
                float paneTop = header.yMax + S(8f);
                float footerH = S(26f);
                float trayH = S(78f);
                float paneH = body.yMax - paneTop - trayH - footerH - S(12f);
                float gap = S(12f);
                float paneW = (body.width - gap) * 0.5f;
                Rect left = new Rect(body.x, paneTop, paneW, paneH);
                Rect right = new Rect(body.x + paneW + gap, paneTop, paneW, paneH);
                DrawWares(left, m);
                DrawWants(right, m);

                // The tray and the buttons.
                Rect tray = new Rect(body.x, left.yMax + S(8f), body.width, trayH);
                DrawTray(tray, m);

                // Footer: his words, or why the last confirm stopped.
                Rect foot = GiltFrameTheme.FooterLine(_rect);
                GUI.Label(foot, _awaiting ? "..." : _tray.Message, GiltFrameTheme.Footer);
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
            return o;
        }

        private void Layout()
        {
            float w = Mathf.Min(Screen.width - 40f, GiltFrameTheme.S(980f));
            float h = Mathf.Min(Screen.height - 40f, GiltFrameTheme.S(620f));
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

        private void DrawTray(Rect r, MarketSnapshot m)
        {
            float S(float v) => GiltFrameTheme.S(v);
            GiltFrameTheme.DrawRule(new Rect(r.x, r.y, r.width, 1f));
            float y = r.y + S(6f);
            GUI.Label(new Rect(r.x, y, S(80f), S(22f)), "STAGING", GiltFrameTheme.Header);

            var sb = new System.Text.StringBuilder();
            if (_tray.Wanted != null)
                sb.Append("buy: ").Append(Span(_tray.Wanted, Name(_tray.Wanted.Prefab)));
            if (_tray.Offered.Count > 0)
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append("offer: ");
                for (int i = 0; i < _tray.Offered.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Span(_tray.Offered[i], Name(_tray.Offered[i].Prefab)));
                }
            }
            if (sb.Length == 0) sb.Append("<color=" + GiltFrameTheme.HexMuted + ">click a ware to buy it, one of your goods to offer it; Shift = 5, Ctrl = 20, right-click takes back</color>");
            long net = _tray.Net;
            string pay = _tray.IsEmpty ? "" : net > 0 ? "   ->  you pay " + net + "c" : net < 0 ? "   ->  he pays you " + (-net) + "c" : "   ->  even";
            GUIStyle rich = GiltFrameTheme.Value;
            bool wasRich = rich.richText;
            rich.richText = true;
            try { GUI.Label(new Rect(r.x + S(84f), y, r.width - S(84f), S(22f)), sb + pay, rich); }
            finally { rich.richText = wasRich; }

            y += S(28f);
            float bw = S(150f), bh = S(28f), bx = r.x;
            try
            {
            GUI.enabled = !_awaiting && !_tray.IsEmpty;
            if (GUI.Button(new Rect(bx, y, bw, bh), _tray.AnyAmber ? "Confirm new price" : "Confirm deal", GiltFrameTheme.Primary)) Confirm(m);
            GUI.enabled = !_awaiting;
            bx += bw + S(8f);
            if (GUI.Button(new Rect(bx, y, S(90f), bh), "Clear", GiltFrameTheme.Button)) { _tray.Clear(); _tray.Message = ""; }
            bx += S(98f);
            if (_tray.Mode == PayMode.Barter && _tray.Wanted != null &&
                GUI.Button(new Rect(bx, y, S(170f), bh), "Fill from my goods", GiltFrameTheme.Button))
            {
                int n = _tray.AutoFill(m, Has);
                _tray.Message = n > 0 ? "Offered " + n + " kind(s) of your goods against it." : "Nothing of yours covers it.";
            }
            }
            finally { GUI.enabled = true; }
            string dismiss = _dismissArmedUntil > 0f ? "Ask once more" : "Send him off";
            if (GUI.Button(new Rect(r.xMax - S(150f), y, S(150f), bh), dismiss, GiltFrameTheme.Button)) DismissPressed();
        }

        private static string Span(TrayLine l, string name)
        {
            string text = name + " x" + l.Count + " (" + l.ValueNow + "c)";
            return l.Amber ? "<color=#E0A23C>" + text + "</color>" : text;
        }

        private void Confirm(MarketSnapshot m)
        {
            string why = _tray.Validate(m, _coins, Has);
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
            _dismissArmedUntil = 0f;
            CargoRpc.Dismiss(_visitId);
            _tray.Message = Lines.Farewell;
            Close("sent him off");
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
