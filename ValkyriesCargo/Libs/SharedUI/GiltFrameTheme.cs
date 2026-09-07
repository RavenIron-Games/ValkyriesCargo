// NOT OURS. VikingOS SharedUI by Wubarrk - libs-Tools/SharedUI/GiltFrameTheme.cs at VikingOS 0.9.8,
// 2026-09-06. License MIT. Vendored as shared source; update from libs-Tools, never edit here.
using System;
using BepInEx.Logging;
using UnityEngine;

namespace SharedUI
{
    // Which set of ornament the frame is cut from. The METRICS are shared - every style uses the same Band,
    // Pad, corner tile and crest box - so a style is purely a change of what is painted into those, and any
    // window laid out for one is laid out for all four. That is deliberate: a frame the player can swap must
    // never reflow the window it surrounds.
    internal enum FrameStyle
    {
        Gilt,        // the original: classical double rail, acanthus corners, palmette crest
        Runic,       // carved standing stone: one broad chiselled band, bind-rune corners
        Serpent,     // Jormungandr: a two-strand plait resolving into beast heads at the corners
        Ironbound    // shield-wall: heavy plain strap, riveted, with a corner bracket and a boss
    }

    // Everything the theme is built from, in one object.
    //
    // WHY A STRUCT INSTEAD OF MORE PARAMETERS. EnsureBuilt used to take (gold, scale, fontDelta,
    // headingDelta) and the argument list was already the reason ConfigManager.ApplyTheme exists - six
    // copies of it across five files was how a fourth knob reached four of them and was skipped in the other
    // two. 0.9.0 takes that from four knobs to twenty. A struct means a new knob is a new FIELD with a
    // default, so every existing caller keeps compiling and keeps meaning exactly what it meant before.
    internal struct ThemeOptions
    {
        // ---- metal
        public Color Metal;
        public bool DeriveTones;     // derive the shadow/highlight pair from Metal, as every version did
        public Color Deep;           // used only when DeriveTones is false
        public Color Bright;

        // ---- surfaces and text
        public Color Panel;          // the window fill. THE ANSWER TO "this is too dark"
        public float PanelOpacity;
        public Color Text;           // body text
        public Color MutedText;      // secondary text - keys, notes, footers

        // ---- sizing
        public float Scale;
        public int BodyDelta;        // the flat delta every style still starts from

        // Per-style deltas, applied on top of BodyDelta. THE ANSWER TO "not one size cascades here":
        // each of these moves exactly one group of styles and nothing else.
        public int TitleDelta;
        public int SubTitleDelta;
        public int HeaderDelta;
        public int ButtonDelta;
        public int RowDelta;
        public int FooterDelta;
        public int FieldDelta;

        public FrameStyle Frame;

        // The 0.8.x look, exactly. Anything that does not set a field gets the value that version baked in,
        // so "defaults" and "what shipped before" are the same sentence.
        public static ThemeOptions Default => new ThemeOptions
        {
            Metal = new Color(0.80f, 0.62f, 0.26f, 1f),
            DeriveTones = true,
            Deep = new Color(0.26f, 0.17f, 0.05f, 1f),
            Bright = new Color(1.00f, 0.94f, 0.72f, 1f),
            Panel = new Color(0.075f, 0.065f, 0.051f, 1f),
            PanelOpacity = 0.955f,
            Text = new Color(0.90f, 0.86f, 0.75f, 1f),
            MutedText = new Color(0.60f, 0.56f, 0.48f, 1f),
            Scale = 1f,
            BodyDelta = 0,
            TitleDelta = 0,
            SubTitleDelta = 0,
            HeaderDelta = 0,
            ButtonDelta = 0,
            RowDelta = 0,
            FooterDelta = 0,
            FieldDelta = 0,
            Frame = FrameStyle.Gilt
        };
    }

    // CANONICAL shared copy. Consume via <Compile Include="..\libs-Tools\SharedUI\GiltFrameTheme.cs" />,
    // never by copy-pasting the file into a project - that is exactly how TortalPortal's UI/TortalUITheme.cs
    // and Fatty's Patches/GiltFrameTheme.cs drifted apart (the latter is missing the live gold-recolour and
    // the favourite heart the former gained later). One file, edited once, is the whole point.
    //
    // Originally authored in TortalPortal (UI/TortalUITheme.cs) and ported once already into Fatty
    // (Patches/GiltFrameTheme.cs) by copy-paste. Promoted here verbatim, with exactly one change: EnsureBuilt
    // takes its gold colour / text scale / font delta as PARAMETERS instead of reading a static
    // `Configuration.uiGoldColour` etc. field. That was the one thing keeping the original file from being
    // shareable as-is - every mod's Configuration class has its own type and its own GUID, so the theme
    // cannot reach into any one of them. The caller (each mod's own OnGUI) already owns its own ConfigEntry<T>
    // values and just passes `.Value` through. See IMPLEMENTATIONS/SharedInfrastructure.md for the promotion
    // note and which mods still carry a pre-promotion copy.
    //
    // Look and feel: the gilt picture-frame reference - slim polished double rails running each edge, a big
    // acanthus flourish mitring every corner, and a palmette crest set into the middle of the top and bottom
    // rails. All ornament, no motion - "scrolling" here is scrollwork.
    //
    // Every pixel is generated at runtime: a mod with no AssetBundle ships no texture that isn't computed in
    // code. Textures are static and flagged HideAndDontSave - without that Unity collects them on the next
    // scene load and OnGUI starts handing destroyed textures to the draw calls the moment the player reloads
    // a world.
    //
    // Shapes are painted into a coverage buffer plus a HEIGHT buffer, then embossed at bake time against one
    // fixed light over the top-left shoulder. Doing the lighting once, globally, at the end is what keeps four
    // separately generated edges and four mirrored corners agreeing on where the light is - and it means a
    // mirrored corner is still lit correctly, because mirroring only moves pixels, never the lamp.
    internal static class GiltFrameTheme
    {
        // The metal is one caller-supplied colour; the shadow and highlight tones are derived from it, so a
        // recolour to silver or verdigris is a single value passed into EnsureBuilt. Defaults reproduce the
        // original hand-tuned gilt exactly when the caller passes the same default colour.
        public static Color GoldDeep = new Color(0.26f, 0.17f, 0.05f, 1f);
        public static Color Gold = new Color(0.80f, 0.62f, 0.26f, 1f);
        public static Color GoldBright = new Color(1.00f, 0.94f, 0.72f, 1f);

        // The three metal tones at an arbitrary alpha, because writing that out by hand is how a consumer
        // ends up with the SHIPPED gold baked into a literal instead of the player's chosen one.
        //
        // That is not hypothetical: an audit of VikingOS in 0.9.2 found `new Color(1f, 0.85f, 0.4f, a)`
        // spelled out in nine places - every corner grip, every edit-mode highlight, the emoji hover tint -
        // so setting Metal Colour to silver recoloured the frames and left every handle on the screen gold.
        // Anything tinted with the metal goes through one of these three.
        public static Color Metal(float alpha) => new Color(Gold.r, Gold.g, Gold.b, alpha);
        public static Color MetalBright(float alpha) => new Color(GoldBright.r, GoldBright.g, GoldBright.b, alpha);
        public static Color MetalDeep(float alpha) => new Color(GoldDeep.r, GoldDeep.g, GoldDeep.b, alpha);

        // No longer readonly: these are the two text colours, and "this is too dark / this is extremely
        // bright" is a complaint about exactly these and the panel behind them. Still public fields rather
        // than properties, because half a dozen call sites across the consuming mod read them directly to
        // tint their own fills and outlines, and those should keep reading whatever the player chose.
        public static Color Parchment = new Color(0.90f, 0.86f, 0.75f, 1f);
        public static Color Muted = new Color(0.60f, 0.56f, 0.48f, 1f);

        // WHAT THE LIVE TEXTURE SET WAS BAKED FROM. Every field here costs a rebake when it changes, which
        // is why it is a separate list from the style inputs: text sizes and colours are rebuilt from
        // nothing but GUIStyle objects, while these are painted into pixels. Keeping the two apart is what
        // lets a player drag a font-size slider without regenerating a single ornament texture.
        private static ThemeOptions _bakedFrom = ThemeOptions.Default;
        private static bool _everBaked;

        private static bool BakeInputsMatch(ThemeOptions o)
        {
            return _everBaked
                && o.Frame == _bakedFrom.Frame
                && o.Metal == _bakedFrom.Metal
                && o.DeriveTones == _bakedFrom.DeriveTones
                && (o.DeriveTones || (o.Deep == _bakedFrom.Deep && o.Bright == _bakedFrom.Bright))
                && o.Panel == _bakedFrom.Panel
                && Mathf.Approximately(o.PanelOpacity, _bakedFrom.PanelOpacity);
        }

        // Which frame the live set was last (re)affirmed in, used to spot two consumers disagreeing inside
        // a single frame - see the CONSUMER CONFLICT note in EnsureBuilt.
        private static int _appliedFrame = -1;
        private static bool _conflictLogged;

        // Fully qualified: UnityEngine also declares a Logger type, and this file compiles directly into
        // whichever project includes it, where UnityEngine is always in scope.
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("SharedUI.GiltFrameTheme");

        // Rich-text colours, kept beside the Color values they mirror so the two never drift apart.
        public const string HexMuted = "#9A9182";
        public const string HexLocked = "#E0736B";
        public const string HexOpen = "#87D278";

        // ---- frame metrics -------------------------------------------------------------------------------
        public const float Band = 18f;          // rail band thickness, and the rail textures' across size
        public const float Pad = 22f;           // clears the corner flourishes, which reach 36px deep

        // Grow with the caller's text scale, because they are the bands the title and footer text sit in.
        // The fixed part of TitleHeight is the crest clearance: the top palmette reaches 26px into the
        // window, so the title has to start below that whatever the text is doing.
        public static float TitleHeight = 54f;
        public static float FooterHeight = 30f;

        // What text scale resolved to for the styles currently built. Scale layout metrics that hold text
        // through S() so they grow with the font instead of clipping it.
        public static float TextScale { get; private set; } = 1f;

        // A flat point offset applied after the scale, so two mods sharing this theme can agree on a size:
        // pass the same delta and their windows read identically. Every per-style delta is added ON TOP of
        // this one, so it stays the single "make everything bigger" dial it always was.
        private static int _fontDelta;

        // What the live GUIStyle set was built from. Cheap to rebuild (no pixels), so this is checked
        // separately from the bake inputs above and a size change never touches a texture.
        private static ThemeOptions _styledFrom = ThemeOptions.Default;
        private static bool _everStyled;

        private static bool StyleInputsMatch(ThemeOptions o)
        {
            return _everStyled
                && Title != null
                && Mathf.Approximately(o.Scale, _styledFrom.Scale)
                && o.BodyDelta == _styledFrom.BodyDelta
                && o.TitleDelta == _styledFrom.TitleDelta
                && o.SubTitleDelta == _styledFrom.SubTitleDelta
                && o.HeaderDelta == _styledFrom.HeaderDelta
                && o.ButtonDelta == _styledFrom.ButtonDelta
                && o.RowDelta == _styledFrom.RowDelta
                && o.FooterDelta == _styledFrom.FooterDelta
                && o.FieldDelta == _styledFrom.FieldDelta
                && o.Text == _styledFrom.Text
                && o.MutedText == _styledFrom.MutedText;
        }

        public static float S(float v) => Mathf.Round(v * TextScale);

        private const int CornerTile = 84;      // corner texture is square
        private const float Overhang = 10f;     // how far corners and crests sit outside the window rect
        private const float CornerExtent = CornerTile - Overhang;   // rail run starts this far along each edge
        private const int CrestW = 56;
        private const int CrestH = 34;
        private const float CrestOverhang = 8f;

        // Rail cross-section, measured from the OUTER edge of the band inwards.
        private const float OuterMid = 4.4f, OuterHalf = 2.7f;
        private const float InnerMid = 12.4f, InnerHalf = 1.6f;
        private const float BendCentre = 32f;   // corner arc centre, in corner-tile space (includes Overhang)

        public static GUIStyle Title, SubTitle, Header, Key, Value, Note, Footer, Button, Primary, Row, Field, ImageButton;

        private static Texture2D _railTop, _railBottom, _railLeft, _railRight;
        private static Texture2D _cornerTL, _cornerTR, _cornerBL, _cornerBR;
        private static Texture2D _crestTop, _crestBottom, _diamond, _heart, _dot;
        private static Texture2D _panel, _white;
        private static Texture2D _btnNormal, _btnHover, _btnActive;
        private static Texture2D _rowNormal, _rowHover, _rowActive, _selection;
        private static Texture2D _fieldTex;

        // Set with the rails, read by DrawFrame. A style whose rail carries a pattern along its length has
        // to be TILED rather than stretched - see RailTile for why that is not a detail.
        private static bool _railRepeats;
        private static float _railTileLength = 4f;

        // Call at the top of every OnGUI, passing the caller's own config values through. The null check on
        // _railTop is not laziness: these are UnityEngine.Objects, so "already built" and "still alive" are
        // different questions.
        // The pre-0.9.0 signature, kept so this file stays drop-in for a consumer that never asked for any
        // of the new knobs. It means exactly what it always meant: metal, scale, body delta, and one delta
        // shared by the three headings.
        public static void EnsureBuilt(Color goldColour, float textScale = 1f, int fontSizeDelta = 0, int headingDelta = 0)
        {
            ThemeOptions o = ThemeOptions.Default;
            o.Metal = goldColour;
            o.Scale = textScale;
            o.BodyDelta = fontSizeDelta;
            o.TitleDelta = headingDelta;
            o.SubTitleDelta = headingDelta;
            o.HeaderDelta = headingDelta;
            EnsureBuilt(o);
        }

        public static void EnsureBuilt(ThemeOptions o)
        {
            // Fast path: the live set already matches what this caller wants. A handful of compares per
            // OnGUI, which is what every frame after the first looks like.
            if (_railTop != null && BakeInputsMatch(o) && StyleInputsMatch(o))
            {
                _appliedFrame = Time.frameCount;
                return;
            }

            // CONSUMER CONFLICT. This is ONE process-wide baked set shared by every mod that compiles this
            // file in - that is the point of it. If a second consumer asks for different values in the same
            // frame a caller already built, honouring both means tearing down and re-baking every procedural
            // texture twice per frame, forever, which costs far more than either mod's window is worth.
            // First caller in a frame wins and keeps winning; the disagreement is logged once so it surfaces
            // as a config note instead of as unexplained frame time.
            if (_railTop != null && _appliedFrame == Time.frameCount)
            {
                if (!_conflictLogged)
                {
                    _conflictLogged = true;
                    Log.LogWarning(
                        "Two consumers of SharedUI.GiltFrameTheme asked for different values in the same " +
                        $"frame (live: metal={_bakedFrom.Metal}, frame={_bakedFrom.Frame}, scale={TextScale}, " +
                        $"delta={_fontDelta}; requested: metal={o.Metal}, frame={o.Frame}, scale={o.Scale}, " +
                        $"delta={o.BodyDelta}). The theme is a single shared bake, so the first caller each " +
                        "frame wins. Set the same UI theme values in every mod that uses this theme to resolve it.");
                }
                return;
            }

            // A changed metal, panel or FRAME STYLE invalidates every baked pixel. Rebuilding the full set
            // costs a few milliseconds ONCE per dial change - the check itself is a handful of compares.
            if (_railTop != null && !BakeInputsMatch(o))
            {
                DestroyTextures();
            }

            if (_railTop != null)
            {
                // Styles are cheap to rebuild and textures are not, so every size and text colour is picked
                // up live without regenerating a single pixel of ornament.
                if (!StyleInputsMatch(o)) BuildStyles(o);
                _appliedFrame = Time.frameCount;
                return;
            }

            _bakedFrom = o;
            _everBaked = true;

            // Derive the family from the base metal, unless the player has said otherwise. The untouched
            // default gets the original hand-tuned trio verbatim, so nobody who never opens the dial sees a
            // single pixel change.
            Gold = new Color(o.Metal.r, o.Metal.g, o.Metal.b, 1f);
            Parchment = o.Text;
            Muted = o.MutedText;

            if (!o.DeriveTones)
            {
                GoldDeep = o.Deep;
                GoldBright = o.Bright;
            }
            else if (Mathf.Approximately(Gold.r, 0.80f) && Mathf.Approximately(Gold.g, 0.62f) && Mathf.Approximately(Gold.b, 0.26f))
            {
                GoldDeep = new Color(0.26f, 0.17f, 0.05f, 1f);
                GoldBright = new Color(1.00f, 0.94f, 0.72f, 1f);
            }
            else
            {
                GoldDeep = new Color(Gold.r * 0.325f, Gold.g * 0.274f, Gold.b * 0.192f, 1f);
                GoldBright = Color.Lerp(Gold, Color.white, 0.72f);
            }

            _railTop = BuildRail(o.Frame, Edge.Top);
            _railBottom = BuildRail(o.Frame, Edge.Bottom);
            _railLeft = BuildRail(o.Frame, Edge.Left);
            _railRight = BuildRail(o.Frame, Edge.Right);

            _railRepeats = RailRepeats(o.Frame);
            _railTileLength = RailTile(o.Frame);

            // Repeat rather than Clamp, or the tex-coords DrawFrame hands it would smear the last pixel
            // across the whole run instead of starting the pattern again.
            if (_railRepeats)
            {
                _railTop.wrapMode = TextureWrapMode.Repeat;
                _railBottom.wrapMode = TextureWrapMode.Repeat;
                _railLeft.wrapMode = TextureWrapMode.Repeat;
                _railRight.wrapMode = TextureWrapMode.Repeat;
            }

            _cornerTL = BuildCorner(o.Frame, false, false);
            _cornerTR = BuildCorner(o.Frame, true, false);
            _cornerBL = BuildCorner(o.Frame, false, true);
            _cornerBR = BuildCorner(o.Frame, true, true);

            _crestTop = BuildCrest(o.Frame, false);
            _crestBottom = BuildCrest(o.Frame, true);
            _diamond = BuildDiamond(14);
            _heart = BuildHeart(28);
            _dot = BuildDot(24);

            _panel = BuildPanel(64, o.Panel, o.PanelOpacity);
            _white = BuildSolid(Color.white);

            // BUTTONS, ROWS AND FIELDS NOW SIT ON THE PLAYER'S PANEL COLOUR, not on three hardcoded
            // near-blacks. Lightening a window to taste and finding every control on it still pitch black
            // was the other half of "this is too dark" - the panel was only ever the backdrop, and the
            // things you actually click were a separate, unreachable decision.
            _btnNormal = BuildPatch(Surface(o, 0.35f, 0.95f), GoldDeep);
            _btnHover = BuildPatch(Surface(o, 1.30f, 0.97f), Gold);
            _btnActive = BuildPatch(Surface(o, 2.60f, 0.98f), GoldBright);

            _rowNormal = BuildPatch(Color.clear, Color.clear);
            _rowHover = BuildPatch(new Color(Gold.r, Gold.g, Gold.b, 0.10f), new Color(Gold.r, Gold.g, Gold.b, 0.45f));
            _rowActive = BuildPatch(new Color(Gold.r, Gold.g, Gold.b, 0.20f), Gold);
            _selection = BuildSolid(new Color(Gold.r, Gold.g, Gold.b, 0.15f));

            // The one control that stays DARKER than its panel whatever the panel is: a text field reads as
            // a well cut into the surface, and a field level with the panel behind it stops looking editable.
            _fieldTex = BuildPatch(Surface(o, -0.45f, 0.95f), GoldDeep);

            BuildStyles(o);
            _appliedFrame = Time.frameCount;
        }

        // A control surface derived from the panel colour: `lift` above 1 warms it towards the metal (which
        // is what a hovered gold button is doing), below 0 sinks it towards black. Kept multiplicative on the
        // panel rather than absolute, so a light theme's controls stay light and a dark one's stay dark
        // without either needing its own table of colours.
        private static Color Surface(ThemeOptions o, float lift, float alpha)
        {
            Color baseCol = o.Panel;

            if (lift >= 0f)
            {
                float warm = Mathf.Clamp01(lift * 0.25f);
                baseCol = Color.Lerp(baseCol * (1f + lift * 0.55f), o.Metal, warm);
            }
            else
            {
                baseCol *= Mathf.Clamp01(1f + lift);
            }

            return new Color(Mathf.Clamp01(baseCol.r), Mathf.Clamp01(baseCol.g), Mathf.Clamp01(baseCol.b), alpha);
        }

        // Tear the whole baked set down so EnsureBuilt regenerates it in the new metal. Explicit destroys,
        // not just dropped references - HideAndDontSave textures are immortal until somebody kills them.
        private static void DestroyTextures()
        {
            Texture2D[] all =
            {
                _railTop, _railBottom, _railLeft, _railRight,
                _cornerTL, _cornerTR, _cornerBL, _cornerBR,
                _crestTop, _crestBottom, _diamond, _heart,
                _panel, _white, _dot,
                _btnNormal, _btnHover, _btnActive,
                _rowNormal, _rowHover, _rowActive, _selection,
                _fieldTex
            };
            foreach (Texture2D t in all)
            {
                if (t != null) UnityEngine.Object.Destroy(t);
            }

            _railTop = _railBottom = _railLeft = _railRight = null;
            _cornerTL = _cornerTR = _cornerBL = _cornerBR = null;
            _crestTop = _crestBottom = _diamond = _heart = _dot = null;
            _panel = _white = null;
            _btnNormal = _btnHover = _btnActive = null;
            _rowNormal = _rowHover = _rowActive = _selection = null;
            _fieldTex = null;
            Title = null; // styles reference dead textures now; BuildStyles runs after the rebake
        }

        // ---- window chrome -------------------------------------------------------------------------------

        public static void DrawWindow(Rect win, string title)
        {
            DrawPanelFill(win);
            DrawFrame(win);

            Rect titleRect = new Rect(win.x + CornerExtent, win.y + Band + 8f, win.width - 2f * CornerExtent, TitleHeight - 20f);
            DrawShadowed(titleRect, (title ?? string.Empty).ToUpperInvariant(), Title);

            DrawRule(new Rect(win.x + Band + Pad, win.y + Band + TitleHeight - 12f, win.width - 2f * (Band + Pad), 1f));
        }

        // Split out of DrawWindow so a consumer needing interactive chrome (a title-bar toggle button, a
        // drag region scoped to less than the full title band) can lay out its own title/controls between
        // the fill and the frame - exactly what Fatty's FeastLedgerGui.cs needed for its collapse button,
        // done here once instead of as a per-project local copy-paste.
        public static void DrawPanelFill(Rect win)
        {
            GUI.DrawTexture(new Rect(win.x + 1f, win.y + 1f, win.width - 2f, win.height - 2f), _panel, ScaleMode.StretchToFill);
        }

        // The content rectangle inside the frame, the title and the footer strip.
        public static Rect Body(Rect win)
        {
            return new Rect(
                win.x + Band + Pad,
                win.y + Band + TitleHeight,
                win.width - 2f * (Band + Pad),
                win.height - 2f * Band - TitleHeight - FooterHeight);
        }

        public static Rect FooterLine(Rect win)
        {
            return new Rect(win.x + Band + Pad, win.yMax - Band - FooterHeight, win.width - 2f * (Band + Pad), S(18f));
        }

        // Public for the same reason as DrawPanelFill above - a draggable/resizable consumer window lays
        // out its own title bar between the fill and the frame instead of going through DrawWindow whole.
        public static void DrawFrame(Rect r)
        {
            float spanX = r.width - 2f * CornerExtent;
            float spanY = r.height - 2f * CornerExtent;

            if (_railRepeats)
            {
                // TILED, not stretched. The texture wraps, so the tex-coord width is "how many repeats fit
                // in this span" - a rivet stays a rivet at every window size, and the pattern of a wide
                // window matches the pattern of a narrow one. Note the span can be shorter than one tile on
                // a very small window, which simply draws part of a repeat; that is correct, and it is why
                // the count is not rounded to a whole number. Rounding it would stretch the pattern again,
                // just less obviously.
                float tile = Mathf.Max(1f, _railTileLength);

                GUI.DrawTextureWithTexCoords(new Rect(r.x + CornerExtent, r.y, spanX, Band), _railTop,
                    new Rect(0f, 0f, spanX / tile, 1f));
                GUI.DrawTextureWithTexCoords(new Rect(r.x + CornerExtent, r.yMax - Band, spanX, Band), _railBottom,
                    new Rect(0f, 0f, spanX / tile, 1f));
                GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y + CornerExtent, Band, spanY), _railLeft,
                    new Rect(0f, 0f, 1f, spanY / tile));
                GUI.DrawTextureWithTexCoords(new Rect(r.xMax - Band, r.y + CornerExtent, Band, spanY), _railRight,
                    new Rect(0f, 0f, 1f, spanY / tile));
            }
            else
            {
                GUI.DrawTexture(new Rect(r.x + CornerExtent, r.y, spanX, Band), _railTop);
                GUI.DrawTexture(new Rect(r.x + CornerExtent, r.yMax - Band, spanX, Band), _railBottom);
                GUI.DrawTexture(new Rect(r.x, r.y + CornerExtent, Band, spanY), _railLeft);
                GUI.DrawTexture(new Rect(r.xMax - Band, r.y + CornerExtent, Band, spanY), _railRight);
            }

            float far = CornerTile - Overhang;
            GUI.DrawTexture(new Rect(r.x - Overhang, r.y - Overhang, CornerTile, CornerTile), _cornerTL);
            GUI.DrawTexture(new Rect(r.xMax - far, r.y - Overhang, CornerTile, CornerTile), _cornerTR);
            GUI.DrawTexture(new Rect(r.x - Overhang, r.yMax - far, CornerTile, CornerTile), _cornerBL);
            GUI.DrawTexture(new Rect(r.xMax - far, r.yMax - far, CornerTile, CornerTile), _cornerBR);

            float cx = r.center.x - CrestW * 0.5f;
            GUI.DrawTexture(new Rect(cx, r.y - CrestOverhang, CrestW, CrestH), _crestTop);
            GUI.DrawTexture(new Rect(cx, r.yMax + CrestOverhang - CrestH, CrestW, CrestH), _crestBottom);

            // The hairline where the frame meets the picture. No outer box line - it would slice straight
            // through the corner flourishes that deliberately overhang the rect.
            DrawOutline(new Rect(r.x + Band, r.y + Band, r.width - 2f * Band, r.height - 2f * Band),
                        new Color(Gold.r, Gold.g, Gold.b, 0.45f), 1f);
        }

        // ---- primitives ----------------------------------------------------------------------------------

        public static void DrawOutline(Rect r, Color c, float t)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), _white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), _white);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), _white);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), _white);
            GUI.color = prev;
        }

        public static void DrawFill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        // A recessed well - used behind lists and preview slots.
        public static void DrawInset(Rect r)
        {
            DrawFill(r, new Color(0f, 0f, 0f, 0.45f));
            DrawOutline(r, new Color(Gold.r, Gold.g, Gold.b, 0.32f), 1f);
        }

        public static void DrawSelection(Rect r)
        {
            GUI.DrawTexture(r, _selection);
        }

        // A filled circle, baked white and tinted here - e.g. a "live" recording badge. Drawn from a texture
        // rather than a text glyph for the same reason the heart is: a font that happens not to carry the
        // character would silently draw nothing, and this is the one indicator that must never fail to appear.
        public static void DrawDot(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _dot, ScaleMode.ScaleToFit);
            GUI.color = prev;
        }

        // A favourite/starred mark: a heart that turns gold when lit, a grey ghost when not. Drawn from our
        // own generated texture rather than a text glyph, so it is never at the mercy of whichever font the
        // lookup found. The texture is baked white and tinted here, so one texture serves both states and
        // any future colour.
        public static void DrawHeartMark(Rect r, bool lit)
        {
            Color prev = GUI.color;
            GUI.color = lit ? Gold : new Color(0.75f, 0.75f, 0.75f, 0.28f);
            GUI.DrawTexture(r, _heart, ScaleMode.ScaleToFit);
            GUI.color = prev;
        }

        // A hairline with a lozenge set into the middle of it.
        public static void DrawRule(Rect r)
        {
            DrawFill(r, new Color(Gold.r, Gold.g, Gold.b, 0.40f));
            const float d = 14f;
            GUI.DrawTexture(new Rect(r.center.x - d * 0.5f, r.y - d * 0.5f + 0.5f, d, d), _diamond);
        }

        // IMGUI has no text shadow, so draw the string twice. Mutating the shared style is safe here - OnGUI
        // is single threaded and the colour is put back before anything else can read it.
        public static void DrawShadowed(Rect r, string text, GUIStyle style)
        {
            if (style == null || string.IsNullOrEmpty(text)) return;

            Color prev = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = prev;
            GUI.Label(r, text, style);
        }

        // ---- styles --------------------------------------------------------------------------------------

        private static void BuildStyles(ThemeOptions o)
        {
            TextScale = Mathf.Clamp(o.Scale, 0.6f, 3f);
            _fontDelta = Mathf.Clamp(o.BodyDelta, -6, 24);

            Parchment = o.Text;
            Muted = o.MutedText;

            // Each style group's own delta, clamped once here so nothing downstream has to.
            int dTitle = Mathf.Clamp(o.TitleDelta, -16, 32);
            int dSub = Mathf.Clamp(o.SubTitleDelta, -16, 32);
            int dHeader = Mathf.Clamp(o.HeaderDelta, -16, 32);
            int dButton = Mathf.Clamp(o.ButtonDelta, -16, 32);
            int dRow = Mathf.Clamp(o.RowDelta, -16, 32);
            int dFooter = Mathf.Clamp(o.FooterDelta, -16, 32);
            int dField = Mathf.Clamp(o.FieldDelta, -16, 32);

            // The title and footer bands have to grow with the delta as well as the scale, or a positive
            // delta just clips the text they exist to hold. Only a POSITIVE delta widens them - shrinking the
            // text must not pull the title band up into the crest, which is fixed ornament at 26px.
            //
            // The title band answers to the TITLE delta as well, and to whichever of the two is asking for
            // more room: the band exists to hold the title, so sizing it off the body delta alone would clip
            // the one piece of text it is there for. Same argument for the footer and its own delta - which
            // is new, and is why a footer can now be made bigger without the window mangling it.
            float slack = Mathf.Max(0, _fontDelta);
            float titleSlack = Mathf.Max(slack, Mathf.Max(0, _fontDelta + dTitle));
            float footerSlack = Mathf.Max(slack, Mathf.Max(0, _fontDelta + dFooter));
            TitleHeight = 26f + Mathf.Round(28f * TextScale) + Mathf.Round(titleSlack * 1.5f);
            FooterHeight = Mathf.Max(30f, Mathf.Round(30f * TextScale) + footerSlack);

            Font font = FindFont();

            // EVERY STYLE NOW CARRIES ITS OWN DELTA. Before 0.9.0 there were exactly two dials for twelve
            // styles - a body delta and one shared by all three headings - so "the buttons are too small"
            // could only be answered by making the whole window bigger. Each group below moves alone.
            Title = Text(font, Pt(25, dTitle), FontStyle.Bold, GoldBright, TextAnchor.MiddleCenter);
            SubTitle = Text(font, Pt(18, dSub), FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
            Header = Text(font, Pt(13, dHeader), FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
            Key = Text(font, Pt(13), FontStyle.Normal, Muted, TextAnchor.MiddleLeft);
            Value = Text(font, Pt(13), FontStyle.Bold, Parchment, TextAnchor.MiddleLeft);
            Note = Text(font, Pt(13), FontStyle.Italic, Muted, TextAnchor.MiddleCenter);
            Note.wordWrap = true;
            Footer = Text(font, Pt(11, dFooter), FontStyle.Normal, Muted, TextAnchor.MiddleCenter);

            Button = Patch(font, Pt(13, dButton), FontStyle.Bold, TextAnchor.MiddleCenter, _btnNormal, _btnHover, _btnActive);
            Button.padding = new RectOffset(12, 12, 6, 6);

            Primary = Patch(font, Pt(16, dButton), FontStyle.Bold, TextAnchor.MiddleCenter, _btnNormal, _btnHover, _btnActive);
            Primary.padding = new RectOffset(12, 12, 8, 8);
            Primary.normal.textColor = Gold;

            Row = Patch(font, Pt(14, dRow), FontStyle.Normal, TextAnchor.MiddleLeft, _rowNormal, _rowHover, _rowActive);
            Row.padding = new RectOffset(12, 12, 4, 4);
            Row.normal.textColor = Parchment;

            // Transparent until hovered, when the row textures give the picture a gold edge - the affordance
            // for "this opens full size".
            ImageButton = Patch(font, Pt(11, dRow), FontStyle.Normal, TextAnchor.LowerCenter, _rowNormal, _rowHover, _rowActive);

            Field = new GUIStyle
            {
                font = font,
                fontSize = Pt(13, dField),
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 4, 4),
                border = new RectOffset(3, 3, 3, 3),
                clipping = TextClipping.Clip
            };
            Field.normal.background = _fieldTex;
            Field.focused.background = _fieldTex;
            Field.hover.background = _fieldTex;
            Field.active.background = _fieldTex;
            Field.normal.textColor = Parchment;
            Field.focused.textColor = GoldBright;
            Field.hover.textColor = Parchment;
            Field.active.textColor = Parchment;

            // Recorded LAST, deliberately. Written at the top, a throw anywhere above would leave the
            // latch claiming these inputs are on screen over a half-built style set - with no retry, and
            // a null style handed to GUI.Window takes the whole window down for the session. At the
            // bottom, a failed build simply runs again next frame. (Fatty's builder had the top-latch and
            // paid for it - 2026-08-14.)
            _styledFrom = o;
            _everStyled = true;
        }

        // Scale first, then the flat deltas - so text scale stays a proportional zoom of the whole window and
        // a delta stays worth exactly what it says in points, whatever the scale is set to.
        //
        // `styleDelta` is the per-group dial; omitting it is the body text, which has only the shared one.
        // The 8pt floor is on the TOTAL, so any group can be brought down level with the body text but never
        // past the point of being unreadable - a style dialled to nothing would otherwise vanish with no
        // indication of which of twenty settings did it.
        private static int Pt(int designSize, int styleDelta = 0)
        {
            return Mathf.Max(8, Mathf.RoundToInt(designSize * TextScale) + _fontDelta + styleDelta);
        }

        private static GUIStyle Text(Font font, int size, FontStyle fs, Color colour, TextAnchor anchor)
        {
            GUIStyle s = new GUIStyle
            {
                font = font,
                fontSize = size,
                fontStyle = fs,
                alignment = anchor,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            s.normal.textColor = colour;
            return s;
        }

        private static GUIStyle Patch(Font font, int size, FontStyle fs, TextAnchor anchor, Texture2D normal, Texture2D hover, Texture2D active)
        {
            GUIStyle s = new GUIStyle
            {
                font = font,
                fontSize = size,
                fontStyle = fs,
                alignment = anchor,
                richText = true,
                wordWrap = false,
                border = new RectOffset(3, 3, 3, 3),
                clipping = TextClipping.Clip
            };
            s.normal.background = normal;
            s.hover.background = hover;
            s.active.background = active;
            s.focused.background = normal;
            s.normal.textColor = Parchment;
            s.hover.textColor = GoldBright;
            s.active.textColor = GoldBright;
            s.focused.textColor = Parchment;
            return s;
        }

        // Borrow the host game's own body font so the window sits in Valheim rather than on top of it.
        // Returning null is a valid answer - IMGUI then falls back to GUI.skin.font - and it is the right
        // answer when nothing matches, because Arial.ttf stopped being a builtin resource in the Unity
        // version Valheim ships on, so asking for it would just hand back null anyway.
        private static Font FindFont()
        {
            Font[] all = Resources.FindObjectsOfTypeAll<Font>();
            string[] preferred = { "AveriaSerifLibre", "Norsebold", "Norse" };

            foreach (string want in preferred)
            {
                foreach (Font f in all)
                {
                    if (f != null && f.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) return f;
                }
            }
            return null;
        }

        // ---- ornament -------------------------------------------------------------------------------------

        private enum Edge { Top, Bottom, Left, Right }

        // HOW LONG ONE REPEAT OF A RAIL IS, along the run.
        //
        // Gilt and Runic have a profile that is CONSTANT along the edge, so four pixels is the whole rail
        // and IMGUI stretching it across the window costs nothing and stays perfectly sharp. Serpent and
        // Ironbound do not - a plait has to cross over somewhere and a strap has to put its rivets
        // somewhere - so their rails carry a real pattern that must TILE rather than stretch. Stretching a
        // rivet is what a frame looks like when nobody thought about it: the studs turn into stripes as the
        // window widens, and two windows of different sizes stop looking like the same frame.
        private static int RailTile(FrameStyle style)
        {
            switch (style)
            {
                case FrameStyle.Serpent: return 48;
                case FrameStyle.Ironbound: return 40;
                default: return 4;
            }
        }

        private static bool RailRepeats(FrameStyle style) => RailTile(style) > 4;

        // A straight run of the rail.
        //
        // Which side of the band is "outer" flips per edge, and that is the whole reason there are four of
        // these rather than one rotated texture - it is what puts the thick rail on the outside all the way
        // round, and lets the global bake light shade each edge the way a real frame catches light.
        //
        // Laid out in ALONG/ACROSS space and mapped to texture x/y at the end, so one description of each
        // profile serves all four edges. `across` is always measured from the OUTER edge inwards.
        private static Texture2D BuildRail(FrameStyle style, Edge edge)
        {
            bool horizontal = edge == Edge.Top || edge == Edge.Bottom;
            int along = RailTile(style);
            int band = (int)Band;

            int w = horizontal ? along : band;
            int h = horizontal ? band : along;

            var p = new Painter(w, h)
            {
                // Bottom and Right want the profile flipped so the heavy side stays outermost.
                MirrorY = edge == Edge.Bottom,
                MirrorX = edge == Edge.Right
            };

            // Map (along, across) to the painter's own coordinates for this edge's orientation.
            System.Action<float, float, float> disc = (a, ac, r) =>
            {
                if (horizontal) p.Disc(a, ac, r);
                else p.Disc(ac, a, r);
            };

            switch (style)
            {
                case FrameStyle.Runic:
                    // A broad chiselled band: two raised ridges with a channel cut between them, and a fine
                    // bead at the outer lip. The painter only ever takes the MAXIMUM of what is written, so
                    // a groove cannot be carved by subtracting - it is the untouched gap between two ridges.
                    RailProfile(p, horizontal, along, band, 2.0f, 0.9f);
                    RailProfile(p, horizontal, along, band, 6.6f, 2.3f);
                    RailProfile(p, horizontal, along, band, 12.8f, 2.3f);
                    break;

                case FrameStyle.Serpent:
                    // Two strands braided down the run, crossing twice per tile. Drawn as overlapping discs
                    // rather than a profile, because the whole point is that the across position MOVES.
                    for (int i = 0; i <= along * 3; i++)
                    {
                        float t = i / (float)(along * 3);
                        float a = t * along;
                        float swing = Mathf.Sin(t * Mathf.PI * 2f) * 3.6f;
                        disc(a, 9f + swing, 2.3f);
                        disc(a, 9f - swing, 2.3f);
                    }
                    break;

                case FrameStyle.Ironbound:
                    // A heavy flat strap with a raised lip each side, studded once per tile.
                    RailProfile(p, horizontal, along, band, 2.4f, 1.3f);
                    RailProfile(p, horizontal, along, band, 9f, 5.2f);
                    RailProfile(p, horizontal, along, band, 15.6f, 1.3f);
                    disc(along * 0.5f, 9f, 3.1f);
                    break;

                default:
                    RailProfile(p, horizontal, along, band, OuterMid, OuterHalf);
                    RailProfile(p, horizontal, along, band, InnerMid, InnerHalf);
                    break;
            }

            return p.Bake();
        }

        // One constant-profile ridge running the length of the rail, at `mid` across the band.
        private static void RailProfile(Painter p, bool horizontal, int along, int band, float mid, float half)
        {
            for (int a = 0; a < along; a++)
            {
                for (int across = 0; across < band; across++)
                {
                    float d = Mathf.Abs(across - mid);
                    if (horizontal) p.RailPixel(a, across, d, half);
                    else p.RailPixel(across, a, d, half);
                }
            }
        }

        // The corner flourish: both rails mitred round a rounded elbow, a palmette sitting outside the bend on
        // the diagonal, and - repeated for each of the two edges via the painter's diagonal transpose - a
        // volute, a leaf frond and a small terminal curl running back along the rail.
        //
        // The ornament is deliberately shallow (nothing reaches past ~36px from the edge) and long: that is
        // both what the reference does and what keeps it clear of Body(), which starts at Band + Pad = 40.
        private static Texture2D BuildCorner(FrameStyle style, bool mirrorX, bool mirrorY)
        {
            if (style != FrameStyle.Gilt) return BuildCornerAlt(style, mirrorX, mirrorY);

            Painter p = new Painter(CornerTile, CornerTile) { MirrorX = mirrorX, MirrorY = mirrorY };

            p.RailElbow(OuterMid + Overhang, OuterHalf, BendCentre);
            p.RailElbow(InnerMid + Overhang, InnerHalf, BendCentre);

            // Finial outside the bend on the diagonal, tied back into the outer rail so it reads as cast onto
            // the frame rather than floating beside it - which is exactly how it looked before the taper.
            p.Lozenge(11f, 11f, 4.5f);
            p.Taper(new Vector2(13.5f, 13.5f), new Vector2(19.3f, 19.3f), 1.2f, 2.2f);

            // Boss in the pocket inside the elbow. Self-symmetric about the diagonal, so it is painted once.
            p.Lozenge(36f, 36f, 3.4f);

            for (int pass = 0; pass < 2; pass++)
            {
                p.Transpose = pass == 1;

                // Stem linking the boss to this edge's volute, so the two edges read as one ornament.
                p.Taper(new Vector2(38.5f, 35f), new Vector2(47f, 32f), 1.4f, 0.45f);
                // Volute: 1.1 turns of a logarithmic spiral, thin at the eye and thickening to the tail. Its
                // eye is kept well clear of the bend centre - sat on top of the elbow it just read as a blob.
                p.Spiral(new Vector2(50f, 31f), 1.3f, 0.33f, 0f, 6.8f, 5.371f, 0.55f, 2.4f);
                // Leaf frond sweeping on along the rail, dying out before the tile edge meets the straight run.
                p.Bezier(new Vector2(56f, 36f), new Vector2(68f, 41f), new Vector2(78f, 30f), 2.2f, 0.35f);
                // Terminal curl where the frond runs out.
                p.Spiral(new Vector2(76f, 32f), 0.9f, 0.32f, 0f, 4.6f, 1.0f, 1.1f, 0.3f);
                // Bead over the joint between volute and frond.
                p.Disc(58f, 34f, 1.8f);
            }
            p.Transpose = false;

            return p.Bake();
        }

        // The three alternate corners. All three keep the gilt's metrics exactly - the same 84px tile, the
        // same 10px overhang, ornament kept inside ~36px of the edge so it never reaches Body() at 40 - so
        // swapping style never moves a single control in a window.
        private static Texture2D BuildCornerAlt(FrameStyle style, bool mirrorX, bool mirrorY)
        {
            Painter p = new Painter(CornerTile, CornerTile) { MirrorX = mirrorX, MirrorY = mirrorY };

            switch (style)
            {
                case FrameStyle.Runic:
                    // SQUARE-SHOULDERED, because a carved stone does not bend. The three ridges of the runic
                    // rail run straight into the corner and stop against each other rather than mitring
                    // round an arc - the one thing that most says "cut with a chisel" rather than "cast".
                    p.SquareElbow(2.0f + Overhang, 0.9f, BendCentre);
                    p.SquareElbow(6.6f + Overhang, 2.3f, BendCentre);
                    p.SquareElbow(12.8f + Overhang, 2.3f, BendCentre);

                    // A bind-rune cut into the pocket inside the shoulder: two straight strokes crossed, with
                    // a struck boss where they meet. Straight lines only - every curve here would be a lie
                    // about the tool.
                    p.Taper(new Vector2(26f, 54f), new Vector2(54f, 26f), 1.9f, 1.9f);
                    p.Taper(new Vector2(30f, 30f), new Vector2(50f, 50f), 1.5f, 1.5f);
                    p.Lozenge(40f, 40f, 4.2f);

                    // Drilled pits marching away along both edges, so the corner reads as the start of a
                    // carved run rather than an isolated mark.
                    for (int pass = 0; pass < 2; pass++)
                    {
                        p.Transpose = pass == 1;
                        p.Disc(52f, 9.7f, 1.9f);
                        p.Disc(66f, 9.7f, 1.9f);
                        p.Disc(80f, 9.7f, 1.9f);
                    }
                    p.Transpose = false;
                    break;

                case FrameStyle.Serpent:
                    // The two strands come round the bend still braided, then resolve into a beast head
                    // facing out along the diagonal - Jormungandr biting the corner of the frame.
                    p.RailElbow(5.4f + Overhang, 2.3f, BendCentre);
                    p.RailElbow(12.6f + Overhang, 2.3f, BendCentre);

                    // Skull, snout and jaw, built on the diagonal so it is self-symmetric and painted once.
                    p.Lozenge(19f, 19f, 7.2f);
                    p.Taper(new Vector2(15f, 15f), new Vector2(6f, 6f), 3.4f, 1.1f);
                    p.Taper(new Vector2(23f, 14f), new Vector2(12f, 8f), 1.6f, 0.5f);
                    p.Taper(new Vector2(14f, 23f), new Vector2(8f, 12f), 1.6f, 0.5f);

                    // The eyes, one per side of the diagonal, and the brow ridge over them.
                    p.Disc(24f, 16f, 1.7f);
                    p.Disc(16f, 24f, 1.7f);
                    p.Bezier(new Vector2(28f, 20f), new Vector2(24f, 24f), new Vector2(20f, 28f), 1.5f, 1.5f);

                    // Each strand runs on along its edge and curls back under the other - the crossing that
                    // makes it read as woven rather than as two parallel wires.
                    for (int pass = 0; pass < 2; pass++)
                    {
                        p.Transpose = pass == 1;
                        p.Bezier(new Vector2(34f, 30f), new Vector2(50f, 24f), new Vector2(64f, 33f), 2.3f, 2.3f);
                        p.Spiral(new Vector2(74f, 26f), 1.1f, 0.30f, 0f, 5.0f, 2.2f, 2.1f, 0.9f);
                    }
                    p.Transpose = false;
                    break;

                default:
                    // IRONBOUND: a bracket hammered over the corner. One broad plate mitred round the bend
                    // with a raised lip each side, and the rivets that hold it down.
                    p.SquareElbow(2.4f + Overhang, 1.3f, BendCentre);
                    p.SquareElbow(9f + Overhang, 5.2f, BendCentre);
                    p.SquareElbow(15.6f + Overhang, 1.3f, BendCentre);

                    // The corner rivet sits on the diagonal where the two straps overlap, and is the biggest
                    // one because that is the joint doing the most work.
                    p.Disc(19f, 19f, 4.6f);
                    p.Disc(19f, 19f, 2.0f);

                    for (int pass = 0; pass < 2; pass++)
                    {
                        p.Transpose = pass == 1;
                        p.Disc(44f, 19f, 3.1f);
                        p.Disc(70f, 19f, 3.1f);
                    }
                    p.Transpose = false;
                    break;
            }

            return p.Bake();
        }

        // The palmette set into the middle of the top and bottom rails: a lozenge on a short spine with a
        // symmetric pair of curls falling away from it.
        private static Texture2D BuildCrest(FrameStyle style, bool flip)
        {
            if (style != FrameStyle.Gilt) return BuildCrestAlt(style, flip);

            Painter p = new Painter(CrestW, CrestH) { MirrorY = flip };
            const float mid = CrestW * 0.5f;

            p.Disc(mid, 2.5f, 1.8f);
            p.Lozenge(mid, 9f, 6.5f);
            p.Taper(new Vector2(mid, 14f), new Vector2(mid, 24f), 2.2f, 1.2f);
            p.Spiral(new Vector2(mid - 11f, 25f), 1.2f, 0.33f, 0f, 5.4f, 2.6f, 2.0f, 0.35f);
            p.Spiral(new Vector2(mid + 11f, 25f), 1.2f, 0.33f, 0f, 5.4f, 2.6f, 2.0f, 0.35f, true);
            p.Bezier(new Vector2(mid - 5f, 19f), new Vector2(mid - 15f, 24f), new Vector2(mid - 24f, 16f), 1.6f, 0.3f);
            p.Bezier(new Vector2(mid + 5f, 19f), new Vector2(mid + 15f, 24f), new Vector2(mid + 24f, 16f), 1.6f, 0.3f);
            p.Disc(mid - 25f, 14f, 1.5f);
            p.Disc(mid + 25f, 14f, 1.5f);

            return p.Bake();
        }

        // The crest for the three alternates: same 56x34 box set into the middle of the top and bottom
        // rails, so the ornament changes and the geometry does not.
        private static Texture2D BuildCrestAlt(FrameStyle style, bool flip)
        {
            Painter p = new Painter(CrestW, CrestH) { MirrorY = flip };
            const float mid = CrestW * 0.5f;

            switch (style)
            {
                case FrameStyle.Runic:
                    // A struck mark: a lozenge on a straight stave with two angled branches, flanked by the
                    // same drilled pits the corners use.
                    p.Lozenge(mid, 8f, 6.0f);
                    p.Taper(new Vector2(mid, 13f), new Vector2(mid, 27f), 2.0f, 2.0f);
                    p.Taper(new Vector2(mid, 16f), new Vector2(mid - 11f, 25f), 1.7f, 1.1f);
                    p.Taper(new Vector2(mid, 16f), new Vector2(mid + 11f, 25f), 1.7f, 1.1f);
                    p.Disc(mid - 21f, 10f, 1.9f);
                    p.Disc(mid + 21f, 10f, 1.9f);
                    break;

                case FrameStyle.Serpent:
                    // The two strands tie themselves into a knot at the midpoint: a pair of mirrored curls
                    // around a central boss, with the strand running out to either side.
                    p.Lozenge(mid, 11f, 5.2f);
                    p.Spiral(new Vector2(mid - 9f, 17f), 1.2f, 0.32f, 0f, 5.6f, 2.4f, 2.1f, 0.5f);
                    p.Spiral(new Vector2(mid + 9f, 17f), 1.2f, 0.32f, 0f, 5.6f, 2.4f, 2.1f, 0.5f, true);
                    p.Bezier(new Vector2(mid - 4f, 20f), new Vector2(mid - 16f, 26f), new Vector2(mid - 26f, 15f), 2.0f, 1.0f);
                    p.Bezier(new Vector2(mid + 4f, 20f), new Vector2(mid + 16f, 26f), new Vector2(mid + 26f, 15f), 2.0f, 1.0f);
                    break;

                default:
                    // IRONBOUND: a shield boss - a domed stud with a ring of rivets and a strap running out
                    // under it to either side.
                    p.Taper(new Vector2(mid - 25f, 12f), new Vector2(mid + 25f, 12f), 4.4f, 4.4f);
                    p.Disc(mid, 12f, 8.2f);
                    p.Disc(mid, 12f, 4.4f);
                    p.Disc(mid - 17f, 12f, 2.4f);
                    p.Disc(mid + 17f, 12f, 2.4f);
                    p.Disc(mid, 25f, 2.4f);
                    break;
            }

            return p.Bake();
        }

        // A flat WHITE circle, tinted at draw time so one texture serves the live red and the warming amber
        // and anything after them. 2x2 supersampled, like the heart, so the edge is clean at badge size.
        private static Texture2D BuildDot(int size)
        {
            Texture2D tex = New(size, size, TextureWrapMode.Clamp, UnityEngine.FilterMode.Bilinear);
            Color[] px = new Color[size * size];
            float mid = (size - 1) * 0.5f;
            float r = size * 0.42f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float hit = 0f;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float dx = x + (sx + 0.5f) * 0.5f - 0.5f - mid;
                            float dy = y + (sy + 0.5f) * 0.5f - 0.5f - mid;
                            if (Mathf.Sqrt(dx * dx + dy * dy) <= r) hit += 0.25f;
                        }
                    }
                    px[y * size + x] = new Color(1f, 1f, 1f, hit);
                }
            }

            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        private static Texture2D BuildDiamond(int size)
        {
            Painter p = new Painter(size, size);
            float mid = (size - 1) * 0.5f;
            p.Lozenge(mid, mid, size * 0.36f);
            return p.Bake();
        }

        // The favourite heart, baked WHITE so DrawHeartMark can tint one texture into both its states. Drawn
        // from the classic implicit heart curve, 2x2 supersampled for a clean edge at icon size. Row 0 of a
        // texture is the bottom, and the curve is authored y-up, which for once means NO flip.
        private static Texture2D BuildHeart(int size)
        {
            Texture2D tex = New(size, size, TextureWrapMode.Clamp, UnityEngine.FilterMode.Bilinear);
            Color[] px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float hit = 0f;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            // Map the pixel into the curve's frame: x in [-1.3, 1.3], y in [-1.5, 1.4].
                            float u = ((x + (sx + 0.5f) * 0.5f) / size - 0.5f) * 2.6f;
                            float v = ((y + (sy + 0.5f) * 0.5f) / size - 0.42f) * 2.9f;
                            float a = u * u + v * v - 1f;
                            if (a * a * a - u * u * v * v * v <= 0f) hit += 0.25f;
                        }
                    }
                    px[y * size + x] = new Color(1f, 1f, 1f, hit);
                }
            }

            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        // ---- flat textures ---------------------------------------------------------------------------------

        // The window fill: a flat colour that lifts very slightly towards the middle. The vignette is radial
        // so it still reads correctly however far IMGUI stretches it.
        //
        // THE COLOUR AND THE OPACITY ARE BOTH THE PLAYER'S NOW. The old body hardcoded a warm near-black
        // (0.075/0.065/0.051 at the centre, 0.955 alpha) and that number IS the "this is too dark" report -
        // there was no setting for it at any point in 0.8.x, only a metal colour for the ornament on top of
        // it. The default reproduces the old constants exactly, so an untouched install is pixel-identical.
        //
        // The vignette keeps its RELATIVE shape rather than its absolute depth: a light panel dimmed by the
        // old fixed 0.02 floor would have been almost flat, and a panel with no falloff at all reads as a
        // rectangle of paint rather than a lit surface.
        private static Texture2D BuildPanel(int size, Color colour, float opacity)
        {
            Texture2D tex = New(size, size, TextureWrapMode.Clamp, UnityEngine.FilterMode.Bilinear);
            Color[] px = new Color[size * size];
            float mid = (size - 1) * 0.5f;
            float alpha = Mathf.Clamp01(opacity);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - mid) / mid, ny = (y - mid) / mid;
                    float v = Mathf.Clamp01(1f - 0.55f * Mathf.Sqrt(0.6f * (nx * nx + ny * ny)));

                    // 0.73 at the rim to 1.0 at the centre, the same ratio the original constants gave.
                    float lit = 0.73f + 0.27f * v;
                    px[y * size + x] = new Color(colour.r * lit, colour.g * lit, colour.b * lit, alpha);
                }
            }

            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        // A 9-sliced patch: one pixel of border around a flat fill. Point filtered and sliced at 3px so the
        // outline stays exactly one pixel however far the control stretches.
        private static Texture2D BuildPatch(Color fill, Color border)
        {
            const int s = 12;
            Texture2D tex = New(s, s, TextureWrapMode.Clamp, UnityEngine.FilterMode.Point);
            Color[] px = new Color[s * s];

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(s - 1 - x, s - 1 - y));
                    px[y * s + x] = edge == 0 ? border : fill;
                }
            }

            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        private static Texture2D BuildSolid(Color c)
        {
            Texture2D tex = New(1, 1, TextureWrapMode.Clamp, UnityEngine.FilterMode.Point);
            tex.SetPixel(0, 0, c);
            tex.Apply(false);
            return tex;
        }

        // UnityEngine.FilterMode is spelled out throughout this file, since a consuming project may define
        // its own FilterMode enum (item filters, etc.) in its own namespace - and that one would otherwise win.
        private static Texture2D New(int w, int h, TextureWrapMode wrap, UnityEngine.FilterMode filter)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = wrap,
                filterMode = filter,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        // ---- the painter -------------------------------------------------------------------------------------

        // Paints coverage and height into two buffers, then bakes both into a lit metal texture.
        //
        // Coordinates are design-space pixels with y pointing DOWN, which is how the shapes above are laid out
        // and how they read on screen. MirrorX/MirrorY place the same design in the other three corners, and
        // Transpose reflects it about the diagonal to repeat an edge's ornament on the adjoining edge. All
        // three only move pixels, so the bake light stays put no matter which variant is being drawn.
        private sealed class Painter
        {
            public bool MirrorX, MirrorY, Transpose;

            private readonly int _w, _h;
            private readonly float[] _a;
            private readonly float[] _z;

            public Painter(int w, int h)
            {
                _w = w;
                _h = h;
                _a = new float[w * h];
                _z = new float[w * h];
            }

            // ---- writes

            private void Put(float fx, float fy, float a, float z)
            {
                if (a <= 0f) return;

                if (Transpose) { float t = fx; fx = fy; fy = t; }
                int x = Mathf.RoundToInt(fx);
                int y = Mathf.RoundToInt(fy);
                if (MirrorX) x = _w - 1 - x;
                if (MirrorY) y = _h - 1 - y;
                if (x < 0 || y < 0 || x >= _w || y >= _h) return;

                int i = y * _w + x;
                if (a > _a[i]) _a[i] = a;
                if (z > _z[i]) _z[i] = z;
            }

            // A soft dome. Everything except the rails and the lozenge is built out of these.
            public void Disc(float cx, float cy, float r)
            {
                if (r <= 0f) return;
                int x0 = Mathf.FloorToInt(cx - r - 1f), x1 = Mathf.CeilToInt(cx + r + 1f);
                int y0 = Mathf.FloorToInt(cy - r - 1f), y1 = Mathf.CeilToInt(cy + r + 1f);

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(r + 0.5f - d);
                        if (a <= 0f) continue;
                        Put(x, y, a, Mathf.Sqrt(Mathf.Max(0f, 1f - (d / r) * (d / r))));
                    }
                }
            }

            public void Lozenge(float cx, float cy, float r)
            {
                int x0 = Mathf.FloorToInt(cx - r - 1f), x1 = Mathf.CeilToInt(cx + r + 1f);
                int y0 = Mathf.FloorToInt(cy - r - 1f), y1 = Mathf.CeilToInt(cy + r + 1f);

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Abs(x - cx) + Mathf.Abs(y - cy);
                        float a = Mathf.Clamp01(r + 0.5f - d);
                        if (a <= 0f) continue;
                        Put(x, y, a, Mathf.Sqrt(Mathf.Max(0f, 1f - (d / r) * (d / r))));
                    }
                }
            }

            public void Taper(Vector2 a, Vector2 b, float w0, float w1)
            {
                int steps = Mathf.Max(8, Mathf.CeilToInt(Vector2.Distance(a, b) * 3f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    Vector2 p = Vector2.Lerp(a, b, t);
                    Disc(p.x, p.y, Mathf.Lerp(w0, w1, t));
                }
            }

            public void Bezier(Vector2 a, Vector2 b, Vector2 c, float w0, float w1)
            {
                int steps = Mathf.Max(16, Mathf.CeilToInt((Vector2.Distance(a, b) + Vector2.Distance(b, c)) * 3f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float u = 1f - t;
                    Vector2 p = u * u * a + 2f * u * t * b + t * t * c;
                    Disc(p.x, p.y, Mathf.Lerp(w0, w1, t));
                }
            }

            // A logarithmic spiral - r grows as r0 * e^(growth * t) - stroked from the eye outward. This is
            // the volute the whole frame hangs on, so it gets a real curve rather than a stack of arcs.
            public void Spiral(Vector2 eye, float r0, float growth, float t0, float t1, float phase, float w0, float w1, bool mirror = false)
            {
                int steps = Mathf.Max(48, Mathf.CeilToInt((t1 - t0) * 40f));
                for (int i = 0; i <= steps; i++)
                {
                    float k = i / (float)steps;
                    float t = Mathf.Lerp(t0, t1, k);
                    float r = r0 * Mathf.Exp(growth * t);
                    float ang = t + phase;
                    float dx = Mathf.Cos(ang) * r;
                    float dy = Mathf.Sin(ang) * r;
                    if (mirror) dx = -dx;
                    Disc(eye.x + dx, eye.y + dy, Mathf.Lerp(w0, w1, k));
                }
            }

            // One pixel of a straight rail, given its distance from the rail centreline.
            public void RailPixel(int x, int y, float d, float half)
            {
                float a = Mathf.Clamp01(half + 0.5f - d);
                if (a <= 0f) return;
                Put(x, y, a, Mathf.Sqrt(Mathf.Max(0f, 1f - (d / half) * (d / half))));
            }

            // A rail mitred SQUARE: the two straight runs meet at a right angle instead of turning through an
            // arc. Distance to the ridge is the nearer of the two straight runs everywhere, which is exactly
            // an L - and the two arms overlap in the corner square rather than being joined by anything, so
            // the join reads as one piece of stone or one lapped strap rather than a bent wire.
            public void SquareElbow(float mid, float half, float centre)
            {
                for (int y = 0; y < _h; y++)
                {
                    for (int x = 0; x < _w; x++)
                    {
                        // Outside the corner square each arm is on its own; inside it, whichever ridge is
                        // nearer wins, which is what squares off the turn.
                        float d = Mathf.Min(Mathf.Abs(x - mid), Mathf.Abs(y - mid));

                        // Beyond the elbow on BOTH axes there is no frame at all - that is the empty quadrant
                        // the window's interior occupies, and painting the L across it would draw a bar
                        // straight through the content.
                        if (x > centre && y > centre) continue;

                        RailPixel(x, y, d, half);
                    }
                }
            }

            // A rail mitred round a corner: straight runs at "mid" from each edge, joined by an arc centred on
            // (centre, centre). Every rail shares that centre, so the two bends stay concentric and the double
            // rail keeps its spacing all the way round the turn.
            public void RailElbow(float mid, float half, float centre)
            {
                float r = centre - mid;

                for (int y = 0; y < _h; y++)
                {
                    for (int x = 0; x < _w; x++)
                    {
                        float d;
                        if (x <= centre && y <= centre)
                        {
                            float dx = x - centre, dy = y - centre;
                            d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r);
                        }
                        else if (y <= centre) d = Mathf.Abs(y - mid);
                        else if (x <= centre) d = Mathf.Abs(x - mid);
                        else d = Mathf.Min(Mathf.Abs(y - mid), Mathf.Abs(x - mid));

                        RailPixel(x, y, d, half);
                    }
                }
            }

            // ---- bake

            public Texture2D Bake()
            {
                Texture2D tex = New(_w, _h, TextureWrapMode.Clamp, UnityEngine.FilterMode.Bilinear);
                Color[] px = new Color[_w * _h];

                for (int y = 0; y < _h; y++)
                {
                    for (int x = 0; x < _w; x++)
                    {
                        int i = y * _w + x;
                        float a = _a[i];
                        // Texture row 0 is the bottom of the image, design y counts down from the top.
                        int o = (_h - 1 - y) * _w + x;

                        if (a <= 0f) { px[o] = Color.clear; continue; }

                        float s = (Z(x - 1, y) - Z(x + 1, y)) + (Z(x, y - 1) - Z(x, y + 1));
                        float shade = Mathf.Clamp01(0.42f + 0.50f * s + 0.18f * _z[i]);
                        Color c = shade < 0.5f
                            ? Color.Lerp(GoldDeep, Gold, shade * 2f)
                            : Color.Lerp(Gold, GoldBright, (shade - 0.5f) * 2f);

                        px[o] = new Color(c.r, c.g, c.b, a);
                    }
                }

                tex.SetPixels(px);
                tex.Apply(false);
                return tex;
            }

            private float Z(int x, int y)
            {
                return _z[Mathf.Clamp(y, 0, _h - 1) * _w + Mathf.Clamp(x, 0, _w - 1)];
            }
        }
    }
}
