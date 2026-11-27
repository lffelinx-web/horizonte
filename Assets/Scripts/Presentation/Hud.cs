using System;
using System.Collections.Generic;
using System.Globalization;
using Horizonte.Simulation;
using UnityEngine;
using UnityEngine.UI;
using Terrain = Horizonte.Simulation.Terrain;

namespace Horizonte.Presentation
{
    /// <summary>
    /// On-screen interface, built in code: the top bar with the city's numbers, goals, alerts and finances on the left,
    /// the minimap, the building bar at the bottom, and the panel that describes the selected building.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        static readonly string[] MonthNames = { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
        static readonly NumberFormatInfo Numbers = new NumberFormatInfo { NumberGroupSeparator = " ", NumberDecimalSeparator = "," };

        /// <summary>Whole number with spaces between thousands, as written in Spanish.</summary>
        public static string N(long v) => v.ToString("#,0", Numbers);

        enum Popup { None, Finance, Alerts, Help, Settings, Wood, Layers }

        sealed class Btn
        {
            public RectTransform Root;
            public Image Back, Edge, Glow, Icon;
            public Text Label;
        }

        sealed class Card
        {
            public RectTransform Root;
            public Image Icon;
            public Text Label, Value;
            public RectTransform BarBack, BarFill;
        }

        sealed class Row
        {
            public RectTransform Root;
            public Image Icon;
            public Text Label, Value;
        }

        sealed class Objective
        {
            public RectTransform Root;
            public Image Box, Check;
            public Text Label, Value;
        }

        GameController game;
        Font font;
        RectTransform safe;
        /// <summary>Full width between the rounded corners: the bars at the top and bottom reach past the notch, which only covers the middle of the side.</summary>
        RectTransform wide;
        RectTransform minimapPanel;
        /// <summary>Space under the bottom bar and minimap: part of the home indicator's margin, never flush with the edge.</summary>
        float bottomPad = 14;
        const float MinimapSize = 206, MinimapGap = 16;
        /// <summary>Space between the screen's sides and the full-width elements; wider on phones with rounded corners.</summary>
        float sideMargin = 30;
        /// <summary>The top bar is laid out at full size and scaled down, so it covers less of the city.</summary>
        const float TopBarScale = .86f;
        RectTransform topBar;
        Rect lastSafeArea;
        float refreshAt, toastUntil, resetArmedUntil, mapRefreshAt;

        // Top bar.
        InputField cityName;
        Text levelText, popValue, popSub, satValue, satSub, moneyValue, moneySub, clockValue, clockDay, woodValue;
        Image clockIcon;
        readonly Text[] resourceValues = new Text[4];
        Image satIcon;
        RectTransform levelFill;
        readonly RectTransform[] demandFill = new RectTransform[3];

        // Goals.
        RectTransform goalsPanel;
        Image goalsChevron;
        bool goalsOpen = true;
        readonly Objective[] goals = new Objective[3];

        // Left column and its pop-up.
        Image alertBadge;
        Text alertBadgeText;
        readonly List<string> alerts = new List<string>();
        RectTransform popupPanel, taxRow, loanRow, settingsRow, woodRow, layersRow;
        readonly List<(CityView.Layer layer, Btn button)> layerButtons = new List<(CityView.Layer, Btn)>();
        Btn layersButton;
        Text popupTitle, popupBody, taxText, loanText;
        Btn loanButton;
        Btn newCityButton, soundButton, musicButton;
        Popup popup;

        // Minimap.
        Texture2D mapTexture;
        Color32[] mapPixels;
        RectTransform mapMarker;

        // Bottom bar.
        RectTransform bottomBar, flyout, hintPanel;
        Text hintText;
        Tools.BarItem flyoutItem;
        readonly List<(Tools.BarItem item, Btn button)> barButtons = new List<(Tools.BarItem, Btn)>();
        Tool currentTool;
        string costText;

        // Selected building.
        RectTransform infoPanel, detailsBox, upkeepRow;
        Image infoTile, infoIcon;
        Text infoTitle, infoSubtitle, upkeepValue, demolishLabel;
        Btn demolishButton;
        readonly Card[] cards = new Card[4];
        readonly Row[] rows = new Row[6];
        Vector2Int selected = new Vector2Int(-1, -1);

        // Messages.
        RectTransform toastPanel;
        Text toastText;

        public void Init(GameController g)
        {
            game = g;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("Interfaz", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 887);
            scaler.matchWidthOrHeight = 1;

            safe = Rect("Zona segura", canvasGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            wide = Rect("Ancho completo", canvasGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            BuildTopBar();
            BuildGoals();
            BuildLeftColumn();
            BuildPopup();
            BuildMinimap();
            BuildBottomBar();
            BuildInfoPanel();
            BuildToast();

            SelectTool(Tool.Explore);
            SetSpeedHighlight(1);
            Refresh();
        }

        // ================================================================ top bar

        void BuildTopBar()
        {
            var bar = topBar = Rect("Barra superior", wide, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -24), new Vector2(1600, 92), new Vector2(.5f, 1));
            bar.localScale = new Vector3(TopBarScale, TopBarScale, 1);
            Panel(bar, UiKit.PanelColor, 20);
            var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 0, 0);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            // City name, level and progress to the next level.
            var city = Block(bar, "Ciudad", 282);
            var badge = At("Escudo", city, 6, 15, 62, 62);
            Panel(badge, UiKit.Accent, 14, UiKit.AccentEdge);
            Icon(badge, "city", 31, 31, 40, Color.white);
            var nameRoot = At("Nombre", city, 80, 8, 166, 38);
            nameRoot.gameObject.AddComponent<Image>().color = Color.clear;
            var nameText = Label(Stretch("Texto", nameRoot), 27, TextAnchor.MiddleLeft, Color.white, true);
            nameText.raycastTarget = true;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            cityName = nameRoot.gameObject.AddComponent<InputField>();
            cityName.textComponent = nameText;
            cityName.characterLimit = 22;
            cityName.lineType = InputField.LineType.SingleLine;
            cityName.onEndEdit.AddListener(Rename);
            Icon(city, "pencil", 258, 27, 24, UiKit.TextDim);
            levelText = TextAt(city, 80, 45, 190, 22, 18, TextAnchor.MiddleLeft, UiKit.AccentText);
            var track = At("Progreso", city, 80, 70, 182, 8);
            Panel(track, new Color(1, 1, 1, .12f), 4, null);
            levelFill = Rect("Relleno", track, Vector2.zero, new Vector2(.5f, 1), Vector2.zero, Vector2.zero, Vector2.zero);
            Panel(levelFill, UiKit.AccentText, 4, null);

            (popValue, popSub, _) = Stat(bar, "Población", 190, "people", new Color(.75f, .86f, 1f), "Población");
            (satValue, satSub, satIcon) = Stat(bar, "Satisfacción", 190, "smile", UiKit.Good, "Satisfacción");
            (moneyValue, moneySub, _) = Stat(bar, "Presupuesto", 240, "coin", UiKit.Gold, "Presupuesto");
            BuildClock(bar);
            BuildWood(bar);
            BuildResources(bar);

            // Demand: bars grow up when the city wants more of a zone and down when there is too much.
            var demand = Block(bar, "Demanda", 140);
            TextAt(demand, 0, 8, 140, 22, 18, TextAnchor.MiddleCenter, UiKit.TextDim).text = "Demanda";
            string[] letters = { "R", "C", "I" };
            Category[] zones = { Category.Housing, Category.Commerce, Category.Industry };
            for (int k = 0; k < 3; k++)
            {
                float cx = 32 + k * 38;
                var t = At("Barra " + letters[k], demand, cx - 9, 32, 18, 40);
                Panel(t, new Color(1, 1, 1, .08f), 6, null);
                var axis = Rect("Eje", t, new Vector2(0, .5f), new Vector2(1, .5f), Vector2.zero, new Vector2(4, 2));
                Plain(axis, new Color(1, 1, 1, .45f));
                var fill = Rect("Relleno", t, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(12, 0), new Vector2(.5f, 0));
                Panel(fill, Tools.CategoryColor(zones[k]), 4, null);
                demandFill[k] = fill;
                TextAt(demand, cx - 15, 72, 30, 18, 15, TextAnchor.MiddleCenter, UiKit.TextDim, true).text = letters[k];
            }

            var spacer = new GameObject("Espacio", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(bar, false);
            spacer.GetComponent<LayoutElement>().flexibleWidth = 1;

            var gear = Block(bar, "Ajustes", 70, false);
            MakeButton(gear, "Ajustes", 4, 15, 62, 62, 18, "gear", 36, null, 0, () => TogglePopup(Popup.Settings));
        }

        /// <summary>Hour of the day with the sun or the moon, and the day number.</summary>
        void BuildClock(RectTransform bar)
        {
            var b = Block(bar, "Hora", 150);
            clockIcon = Icon(b, "sun", 34, 46, 40, UiKit.Gold);
            clockDay = TextAt(b, 62, 9, 84, 24, 19, TextAnchor.MiddleLeft, UiKit.TextDim);
            clockValue = TextAt(b, 62, 31, 84, 34, 29, TextAnchor.MiddleLeft, Color.white, true);
            FitOneLine(clockValue, 16);
        }

        /// <summary>Wood in stock; touching it opens the window to sell it.</summary>
        void BuildWood(RectTransform bar)
        {
            var b = Block(bar, "Madera", 160);
            b.gameObject.AddComponent<Image>().color = Color.clear;
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => TogglePopup(Popup.Wood));
            Icon(b, "wood", 34, 46, 42, new Color(.86f, .66f, .42f));
            TextAt(b, 62, 9, 94, 24, 19, TextAnchor.MiddleLeft, UiKit.TextDim).text = "Madera";
            woodValue = TextAt(b, 62, 31, 94, 34, 29, TextAnchor.MiddleLeft, Color.white, true);
            FitOneLine(woodValue, 16);
            var sub = TextAt(b, 62, 64, 94, 22, 18, TextAnchor.MiddleLeft, UiKit.AccentText);
            sub.text = "Vender";
        }

        /// <summary>What the city produces against what it uses: power, water, sewage and waste.</summary>
        void BuildResources(RectTransform bar)
        {
            const float itemWidth = 150;
            var b = Block(bar, "Recursos", itemWidth * 4 + 16);
            TextAt(b, 14, 8, 400, 24, 19, TextAnchor.MiddleLeft, UiKit.TextDim).text = "Producción / Consumo";
            string[] icons = { "bolt", "water", "water", "trash" };
            Color[] colors = { UiKit.Warn, new Color(.45f, .75f, 1f), new Color(.62f, .52f, .4f), new Color(.6f, .85f, .55f) };
            for (int k = 0; k < 4; k++)
            {
                float x = 8 + k * itemWidth;
                Icon(b, icons[k], x + 18, 58, 30, colors[k]);
                resourceValues[k] = TextAt(b, x + 38, 40, itemWidth - 44, 36, 23, TextAnchor.MiddleLeft, Color.white, true);
                FitOneLine(resourceValues[k], 14);
            }
        }

        void ShowResource(int k, int produced, int used, string unit)
        {
            var t = resourceValues[k];
            t.text = $"{N(produced)} / {N(used)} {unit}";
            t.color = produced == 0 && used == 0 ? UiKit.TextDim : used > produced ? UiKit.Bad : used > produced * .85f ? UiKit.Warn : UiKit.Good;
        }

        RectTransform Block(RectTransform bar, string name, float width, bool divider = true)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(bar, false);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = width;
            if (divider) Plain(Rect("Divisor", rt, new Vector2(1, .2f), new Vector2(1, .8f), Vector2.zero, new Vector2(2, 0)), UiKit.Divider);
            return rt;
        }

        (Text value, Text sub, Image icon) Stat(RectTransform bar, string name, float width, string icon, Color iconColor, string label)
        {
            var b = Block(bar, name, width);
            var img = Icon(b, icon, 36, 46, 44, iconColor);
            var title = TextAt(b, 68, 9, width - 76, 24, 19, TextAnchor.MiddleLeft, UiKit.TextDim);
            title.text = label;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var value = TextAt(b, 68, 31, width - 76, 34, 29, TextAnchor.MiddleLeft, Color.white, true);
            FitOneLine(value, 16);
            var sub = TextAt(b, 68, 64, width - 76, 22, 18, TextAnchor.MiddleLeft, UiKit.TextDim);
            sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            return (value, sub, img);
        }

        /// <summary>Shrinks the text as needed so it stays on one line.</summary>
        static void FitOneLine(Text t, int minSize)
        {
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = minSize;
            t.resizeTextMaxSize = t.fontSize;
            t.verticalOverflow = VerticalWrapMode.Truncate;
        }

        void Rename(string name)
        {
            name = name.Trim();
            if (name.Length == 0) name = "Horizonte";
            game.City.Name = name;
            cityName.text = name;
            game.Save();
        }

        // ================================================================ goals

        void BuildGoals()
        {
            goalsPanel = At("Objetivos", safe, 12, 115, 420, 172);
            Panel(goalsPanel, UiKit.PanelColor, 18);
            Icon(goalsPanel, "target", 32, 28, 32, UiKit.AccentText);
            TextAt(goalsPanel, 58, 8, 250, 40, 25, TextAnchor.MiddleLeft, Color.white, true).text = "Objetivos";
            var toggle = MakeButton(goalsPanel, "Plegar", 366, 6, 44, 44, 12, "chevron", 26, null, 0, ToggleGoals);
            toggle.Back.color = Color.clear;
            toggle.Edge.enabled = false;
            goalsChevron = toggle.Icon;
            for (int k = 0; k < goals.Length; k++)
            {
                var o = new Objective { Root = At("Objetivo " + k, goalsPanel, 0, 54 + k * 38, 420, 36) };
                var box = At("Casilla", o.Root, 18, 5, 26, 26);
                o.Box = Panel(box, UiKit.PanelInner, 6);
                o.Check = Icon(box, "check", 13, 13, 22, Color.white);
                o.Label = TextAt(o.Root, 56, 0, 236, 36, 20, TextAnchor.MiddleLeft, Color.white);
                FitOneLine(o.Label, 15);
                o.Value = TextAt(o.Root, 280, 0, 124, 36, 19, TextAnchor.MiddleRight, UiKit.TextDim);
                FitOneLine(o.Value, 14);
                goals[k] = o;
            }
            ApplyGoalsOpen();
        }

        void ToggleGoals()
        {
            goalsOpen = !goalsOpen;
            ApplyGoalsOpen();
        }

        void ApplyGoalsOpen()
        {
            goalsPanel.sizeDelta = new Vector2(420, goalsOpen ? 172 : 56);
            foreach (var o in goals) o.Root.gameObject.SetActive(goalsOpen);
            goalsChevron.rectTransform.localEulerAngles = new Vector3(0, 0, goalsOpen ? -90 : 0);
        }

        void RefreshGoals(City c)
        {
            // Show the last goal met (ticked) followed by the next ones.
            int first = Mathf.Max(0, Progress.FirstOpenGoal(c) - 1);
            first = Mathf.Min(first, Progress.Goals.Length - goals.Length);
            for (int k = 0; k < goals.Length; k++)
            {
                var goal = Progress.Goals[first + k];
                var o = goals[k];
                bool done = goal.Done(c);
                o.Label.text = goal.Text;
                o.Label.color = done ? UiKit.TextDim : Color.white;
                o.Value.text = goal.Target == 1 ? $"{goal.Current(c)} / 1" : goal.Target >= 1000 ? $"{Short(goal.Current(c))} / {Short(goal.Target)}" : $"{goal.Current(c)} / {goal.Target}";
                o.Box.color = done ? new Color(.2f, .68f, .32f) : UiKit.PanelInner;
                o.Check.enabled = done;
            }
        }

        static string Short(long v) => v >= 10000 ? $"{v / 1000} mil" : N(v);

        // ================================================================ left column and pop-up

        void BuildLeftColumn()
        {
            var col = At("Columna", safe, 12, 299, 76, 286);
            Panel(col, UiKit.PanelColor, 18);
            MakeButton(col, "Finanzas", 8, 8, 60, 60, 14, "chart", 32, null, 0, () => TogglePopup(Popup.Finance)).Edge.enabled = false;
            var alertsButton = MakeButton(col, "Avisos", 8, 78, 60, 60, 14, "warning", 32, null, 0, () => TogglePopup(Popup.Alerts));
            alertsButton.Edge.enabled = false;
            MakeButton(col, "Ayuda", 8, 148, 60, 60, 14, "question", 32, null, 0, () => TogglePopup(Popup.Help)).Edge.enabled = false;
            layersButton = MakeButton(col, "Capas", 8, 218, 60, 60, 14, "layers", 32, null, 0, () => TogglePopup(Popup.Layers));
            layersButton.Edge.enabled = false;

            var badge = At("Contador", alertsButton.Root, 40, -6, 26, 26);
            alertBadge = badge.gameObject.AddComponent<Image>();
            alertBadge.sprite = UiKit.Circle;
            alertBadge.color = new Color(.96f, .55f, .16f);
            alertBadge.raycastTarget = false;
            alertBadgeText = Label(Stretch("Número", badge), 16, TextAnchor.MiddleCenter, Color.white, true);
        }

        void BuildPopup()
        {
            popupPanel = At("Ventana", safe, 100, 299, 440, 300);
            Panel(popupPanel, UiKit.PanelColor, 18);
            popupTitle = TextAt(popupPanel, 22, 10, 330, 42, 25, TextAnchor.MiddleLeft, Color.white, true);
            var close = MakeButton(popupPanel, "Cerrar", 386, 8, 44, 44, 12, "close", 24, null, 0, () => TogglePopup(Popup.None));
            close.Back.color = Color.clear;
            close.Edge.enabled = false;
            popupBody = TextAt(popupPanel, 22, 58, 396, 200, 20, TextAnchor.UpperLeft, Color.white);
            popupBody.supportRichText = true;
            popupBody.lineSpacing = 1.15f;

            taxRow = At("Impuesto", popupPanel, 22, 0, 396, 56);
            Panel(taxRow, UiKit.PanelInner, 12);
            TextAt(taxRow, 16, 0, 160, 56, 21, TextAnchor.MiddleLeft, Color.white, true).text = "Impuesto";
            MakeButton(taxRow, "Bajar", 200, 6, 44, 44, 10, null, 0, "−", 28, () => game.ChangeTax(-1));
            taxText = TextAt(taxRow, 248, 0, 90, 56, 24, TextAnchor.MiddleCenter, UiKit.Gold, true);
            MakeButton(taxRow, "Subir", 342, 6, 44, 44, 10, null, 0, "+", 28, () => game.ChangeTax(1));

            loanRow = At("Préstamo", popupPanel, 22, 0, 396, 56);
            Panel(loanRow, UiKit.PanelInner, 12);
            loanText = TextAt(loanRow, 16, 0, 180, 56, 18, TextAnchor.MiddleLeft, Color.white, true);
            loanText.supportRichText = true;
            loanButton = MakeButton(loanRow, "Pedir", 200, 6, 186, 44, 10, null, 0, $"Pedir $ {N(City.LoanAmount)}", 19, TakeLoan);
            loanButton.Back.color = UiKit.Accent;

            woodRow = At("Vender madera", popupPanel, 22, 0, 396, 56);
            var sellSome = MakeButton(woodRow, "Vender 10", 0, 0, 190, 56, 14, null, 0, "Vender 10", 21, () => SellWood(10));
            sellSome.Back.color = UiKit.Accent;
            MakeButton(woodRow, "Vender todo", 206, 0, 190, 56, 14, null, 0, "Vender todo", 21, () => SellWood(int.MaxValue));

            layersRow = At("Capas", popupPanel, 22, 0, 396, 56);
            var layers = new[] { (CityView.Layer.Power, "bolt", UiKit.Warn), (CityView.Layer.Water, "water", new Color(.45f, .75f, 1f)), (CityView.Layer.Sewage, "water", new Color(.75f, .55f, .35f)), (CityView.Layer.Waste, "trash", new Color(.6f, .85f, .55f)), (CityView.Layer.Services, "cross", new Color(1f, .6f, .6f)) };
            for (int k = 0; k < layers.Length; k++)
            {
                var (l, icon, tint) = layers[k];
                var b = MakeButton(layersRow, l.ToString(), k * 80.5f, 0, 74, 56, 12, icon, 30, null, 0, () => SetLayer(l));
                b.Icon.color = tint;
                layerButtons.Add((l, b));
            }

            settingsRow = At("Opciones", popupPanel, 22, 0, 396, 56);
            newCityButton = MakeButton(settingsRow, "Nueva ciudad", 0, 0, 200, 56, 14, null, 0, "Ciudad nueva", 19, NewCity);
            musicButton = MakeButton(settingsRow, "Música", 208, 0, 90, 56, 14, null, 0, "Música", 17, () => { Sound.MusicMuted = !Sound.MusicMuted; FillPopup(); });
            soundButton = MakeButton(settingsRow, "Efectos", 306, 0, 90, 56, 14, null, 0, "Efectos", 17, () => { Sound.EffectsMuted = !Sound.EffectsMuted; FillPopup(); });
            newCityButton.Back.color = new Color(.45f, .14f, .13f, .95f);
            newCityButton.Edge.color = new Color(1, .5f, .45f, .6f);

            popupPanel.gameObject.SetActive(false);
        }

        void TogglePopup(Popup which)
        {
            popup = popup == which ? Popup.None : which;
            resetArmedUntil = 0;
            popupPanel.gameObject.SetActive(popup != Popup.None);
            if (popup == Popup.Settings)
            {
                // Settings hang under the gear on the right; the rest open beside the left column.
                popupPanel.anchorMin = popupPanel.anchorMax = popupPanel.pivot = new Vector2(1, 1);
                popupPanel.anchoredPosition = new Vector2(-12, -115);
                HideInfo();
            }
            else
            {
                popupPanel.anchorMin = popupPanel.anchorMax = popupPanel.pivot = new Vector2(0, 1);
                popupPanel.anchoredPosition = new Vector2(100, -299);
            }
            FillPopup();
        }

        void FillPopup()
        {
            if (popup == Popup.None) return;
            var c = game.City;
            taxRow.gameObject.SetActive(popup == Popup.Finance);
            loanRow.gameObject.SetActive(popup == Popup.Finance);
            woodRow.gameObject.SetActive(popup == Popup.Wood);
            layersRow.gameObject.SetActive(popup == Popup.Layers);
            settingsRow.gameObject.SetActive(popup == Popup.Settings);
            switch (popup)
            {
                case Popup.Finance:
                    popupTitle.text = "Finanzas";
                    int net = c.LastIncome - c.LastExpenses;
                    popupBody.text = !c.HasBudget
                        ? "Los impuestos y el mantenimiento se cobran cada día a las 6:00. El primer balance llega el próximo amanecer.\n"
                        : $"Impuesto a habitantes:  <b>$ {N(c.LastResidentTax)}</b>\nImpuesto a comercios e industrias:  <b>$ {N(c.LastBusinessTax)}</b>\nMantenimiento:  <b>$ {N(c.LastUpkeep)}</b>\n" +
                          (c.LastLoanPayment > 0 ? $"Cuota del préstamo:  <b>$ {N(c.LastLoanPayment)}</b>\n" : "") +
                          $"Balance del día:  <b><color={Hex(net >= 0 ? UiKit.Good : UiKit.Bad)}>{Signed(net)}</color></b>\n";
                    popupBody.text += "<color=#A9BCE0>Se cobra cada día a las 6:00. Más impuesto da más dinero pero baja la satisfacción.</color>";
                    taxText.text = $"{c.TaxRate} %";
                    loanText.text = c.LoanBalance > 0 ? $"Debes $ {N(c.LoanBalance)}\n<color=#A9BCE0>$ {N(c.LoanPayment)} al día</color>" : $"Sin deudas\n<color=#A9BCE0>{City.LoanDays} cuotas diarias, 10 %</color>";
                    loanButton.Label.text = c.CanBorrow ? $"Pedir $ {N(City.LoanAmount)}" : "Límite alcanzado";
                    loanButton.Back.color = c.CanBorrow ? UiKit.Accent : UiKit.ButtonColor;
                    break;
                case Popup.Wood:
                    popupTitle.text = "Madera";
                    int stations = c.Stations().Count, sawmills = c.Count("aserradero");
                    popupBody.text = $"Tienes <b>{N(c.Wood)}</b> de madera.\n" +
                        "Las casas y algunos edificios la usan para construirse; la que sobra se vende a " + $"<b>$ {Catalog.WoodPrice}</b> cada una.\n\n" +
                        (stations + sawmills == 0
                            ? "<color=#A9BCE0>Pon un <b>puesto de tala temporal</b> cerca de los árboles (Industria) y marca árboles con <b>Talar</b>. Fuera de la reserva los árboles no vuelven a crecer.</color>"
                            : $"<color=#A9BCE0>{stations} {(stations == 1 ? "puesto de tala" : "puestos de tala")} y {sawmills} {(sawmills == 1 ? "aserradero" : "aserraderos")} trabajando. Leñadores: {c.Lumberjacks} · casillas en tala: {c.Marked.Count + c.Felling.Count}.</color>");
                    break;
                case Popup.Layers:
                    popupTitle.text = "Capas";
                    popupBody.text = "Mira la ciudad por servicio: <color=#73FF73>verde</color> lo tiene, <color=#FF6157>rojo</color> le falta, <color=#73B3FF>azul</color> es de donde sale. La luz, el agua, el drenaje y la basura llegan por las calles conectadas a sus plantas.\n" + (game.View.CurrentLayer == CityView.Layer.None ? "" : "<color=#A9BCE0>Toca la misma capa para quitarla.</color>");
                    foreach (var (l, b) in layerButtons) b.Back.color = l == game.View.CurrentLayer ? UiKit.Accent : UiKit.ButtonColor;
                    break;
                case Popup.Alerts:
                    popupTitle.text = "Avisos";
                    popupBody.text = alerts.Count == 0 ? "Todo en orden." : "• " + string.Join("\n• ", alerts);
                    break;
                case Popup.Help:
                    popupTitle.text = "Cómo se juega";
                    popupBody.text =
                        "Traza <b>calles</b> conectadas con la carretera y coloca los edificios junto a ellas. Empieza con un <b>generador</b>, unas casas y una tienda: sin calle, sin luz y sin empleos nadie llega.\n\n" +
                        "Las casas se construyen con <b>madera</b> y no se construye donde hay árboles. Pon un <b>puesto de tala temporal</b> junto a los árboles y márcalos con <b>Talar</b>: el leñador los corta y trae la madera. Un día dura 8 minutos y los impuestos se cobran cada mañana.\n\n" +
                        "<color=#A9BCE0>Un dedo construye o, en Explorar, mueve el mapa. Dos dedos mueven, acercan y giran. Toca un edificio para ver sus datos.</color>";
                    break;
                case Popup.Settings:
                    popupTitle.text = "Ajustes";
                    popupBody.text = $"Horizonte {Application.version}\n<color=#A9BCE0>La partida se guarda sola cada hora del juego y al salir.</color>";
                    newCityButton.Label.text = Time.unscaledTime < resetArmedUntil ? "Toca otra vez" : "Ciudad nueva";
                    musicButton.Label.text = Sound.MusicMuted ? "Música: no" : "Música: sí";
                    musicButton.Back.color = Sound.MusicMuted ? UiKit.ButtonColor : UiKit.Accent;
                    soundButton.Label.text = Sound.EffectsMuted ? "Efectos: no" : "Efectos: sí";
                    soundButton.Back.color = Sound.EffectsMuted ? UiKit.ButtonColor : UiKit.Accent;
                    break;
            }
            float bodyHeight = popupBody.preferredHeight;
            popupBody.rectTransform.sizeDelta = new Vector2(396, bodyHeight);
            float y = 58 + bodyHeight + 14;
            foreach (var extra in new[] { taxRow, loanRow, woodRow, layersRow, settingsRow })
            {
                if (!extra.gameObject.activeSelf) continue;
                extra.anchoredPosition = new Vector2(22, -y);
                y += 56 + 14;
            }
            popupPanel.sizeDelta = new Vector2(440, y + 8);
            // Never run under the bottom bar: a tall window moves up as far as it needs to.
            if (popup != Popup.Settings)
            {
                float room = safe.rect.height - (bottomPad + 118 - safe.anchorMin.y * 887);
                float top = Mathf.Clamp(room - (y + 8), 12, 299);
                popupPanel.anchoredPosition = new Vector2(100, -top);
            }
        }

        void SetLayer(CityView.Layer l)
        {
            if (game.View.CurrentLayer == l) l = CityView.Layer.None;
            game.View.ShowLayer(l);
            layersButton.Back.color = l == CityView.Layer.None ? UiKit.ButtonColor : UiKit.Accent;
            FillPopup();
            UpdateHint();
        }

        void SellWood(int amount)
        {
            int sold = game.City.SellWood(amount);
            Toast(sold == 0 ? "No tienes madera para vender" : $"Vendiste {N(sold)} de madera por $ {N(sold * Catalog.WoodPrice)}");
            Refresh();
        }

        void TakeLoan()
        {
            if (!game.City.TakeLoan())
            {
                Toast("El banco ya no presta más hasta que pagues");
                return;
            }
            Toast($"Recibiste $ {N(City.LoanAmount)} del banco");
            Refresh();
        }

        void NewCity()
        {
            if (Time.unscaledTime > resetArmedUntil)
            {
                resetArmedUntil = Time.unscaledTime + 3;
                FillPopup();
                return;
            }
            resetArmedUntil = 0;
            TogglePopup(Popup.None);
            game.NewCity();
        }

        void RefreshAlerts(City c)
        {
            alerts.Clear();
            int unpowered = 0, outOfReach = 0, noRoad = 0, isolated = 0, consumers = 0;
            for (int i = 0; i < c.Building.Length; i++)
            {
                var def = c.DefAt(i);
                if (def == null || c.Anchor[i] != i) continue;
                if (def.IsRoad)
                {
                    if (!c.RoadConnected[i]) isolated++;
                    continue;
                }
                if (!c.Served[i] && !def.Standalone) noRoad++;
                if (def.Kind == Kind.Power || def.PowerSupply > 0 || def.PowerUse <= 0 || !c.Served[i]) continue;
                consumers++;
                if (c.Powered[i]) continue;
                unpowered++;
                bool reached = false;
                var (fw, fh) = c.Size(i);
                for (int dy = 0; dy < fh && !reached; dy++)
                for (int dx = 0; dx < fw && !reached; dx++)
                    reached = c.PowerCover[i + dy * c.Width + dx];
                if (!reached) outOfReach++;
            }
            if (c.PowerSupply == 0 && consumers > 0) alerts.Add("No hay centrales eléctricas: sin luz nada funciona.");
            else if (c.PowerDemand > c.PowerSupply) alerts.Add($"La energía no alcanza: faltan {N(c.PowerDemand - c.PowerSupply)} MW.");
            if (outOfReach > 0 && c.PowerSupply > 0) alerts.Add($"{N(outOfReach)} {(outOfReach == 1 ? "edificio queda" : "edificios quedan")} sin calle hasta una central: conecta su calle con la de una central.");
            else if (unpowered > 0 && c.PowerSupply > 0) alerts.Add($"{N(unpowered)} {(unpowered == 1 ? "edificio" : "edificios")} sin electricidad.");
            if (noRoad > 0) alerts.Add($"{N(noRoad)} {(noRoad == 1 ? "edificio" : "edificios")} sin calle conectada a la carretera.");
            foreach (var s in Catalog.Services)
            {
                if (c.Population < Catalog.ServiceThreshold(s)) continue;
                int missing = 100 - c.ServiceCoverage(s);
                if (missing > 30) alerts.Add($"{missing} % de los habitantes sin {Catalog.ServiceName(s).ToLowerInvariant()}.");
            }
            if (c.Workers > c.Jobs + 10) alerts.Add($"Faltan empleos: {N(c.Workers - c.Jobs)} personas buscan trabajo.");
            if (isolated > 0) alerts.Add($"{N(isolated)} {(isolated == 1 ? "tramo" : "tramos")} de calle sin salida a la carretera.");
            if (c.Money < 0) alerts.Add("La tesorería está en números rojos.");
            else if (c.HasBudget && c.LastIncome < c.LastExpenses) alerts.Add("Gastas más de lo que ingresas cada día.");
            var traffic = game.Traffic;
            if (traffic != null && (traffic.StreetParked >= 8 || traffic.TurnedAway >= 3))
                alerts.Add($"Faltan estacionamientos: {N(traffic.StreetParked)} coches en la calle. Construye uno en Transporte.");
            if (c.TaxRate >= 13) alerts.Add("Los impuestos altos espantan a la gente.");
            alertBadge.gameObject.SetActive(alerts.Count > 0);
            alertBadgeText.text = alerts.Count.ToString();
        }

        // ================================================================ minimap

        void BuildMinimap()
        {
            var panel = minimapPanel = Rect("Minimapa", wide, Vector2.zero, Vector2.zero, new Vector2(0, 10), new Vector2(206, 206), Vector2.zero);
            Panel(panel, UiKit.PanelColor, 18);
            var maskRt = Stretch("Máscara", panel, 8, 8, 8, 8);
            var maskImage = maskRt.gameObject.AddComponent<Image>();
            maskImage.sprite = UiKit.Rounded;
            maskImage.type = Image.Type.Sliced;
            maskImage.pixelsPerUnitMultiplier = 24f / 12;
            maskRt.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var w = game.City.Width;
            var h = game.City.Height;
            mapTexture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Minimapa" };
            mapPixels = new Color32[w * h];
            var raw = Stretch("Mapa", maskRt).gameObject.AddComponent<RawImage>();
            raw.texture = mapTexture;
            var touch = raw.gameObject.AddComponent<TouchArea>();
            touch.OnTouch = p => game.Rig.Focus = new Vector3(p.x * game.City.Width, 0, p.y * game.City.Height) * CityView.TileSize;

            mapMarker = Rect("Cámara", raw.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(26, 26));
            var marker = mapMarker.gameObject.AddComponent<Image>();
            marker.sprite = Icons.Get("play");
            marker.raycastTarget = false;
            mapMarker.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .7f);

            var north = TextAt(panel, 16, 12, 30, 26, 20, TextAnchor.MiddleCenter, Color.white, true);
            north.text = "N";
            north.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .7f);
        }

        void RefreshMinimap()
        {
            var c = game.City;
            if (mapTexture.width != c.Width || mapTexture.height != c.Height)
            {
                mapTexture.Reinitialize(c.Width, c.Height);
                mapPixels = new Color32[c.Width * c.Height];
            }
            // Land is kept dark and buildings bright so the city stands out at a glance.
            Color grass = new Color(.3f, .42f, .23f), forest = new Color(.18f, .29f, .15f), water = new Color(.2f, .44f, .7f);
            for (int i = 0; i < mapPixels.Length; i++)
            {
                var def = c.DefAt(i);
                Color col;
                if (def == null)
                {
                    switch (c.Terrain[i])
                    {
                        case Terrain.Water: col = water; break;
                        case Terrain.Forest: case Terrain.Sapling: col = forest; break;
                        case Terrain.Mountain: col = new Color(.45f, .43f, .4f); break;
                        case Terrain.Fertile: col = new Color(.46f, .47f, .25f); break;
                        case Terrain.Stumps: col = new Color(.36f, .33f, .22f); break;
                        default: col = grass; break;
                    }
                }
                else if (def.IsRoad) col = new Color(.86f, .87f, .9f);
                else col = Color.Lerp(Tools.CategoryColor(def.Category), Color.white, c.Construction[c.Anchor[i]] > 0 ? .7f : .3f);
                mapPixels[i] = col;
            }
            mapTexture.SetPixels32(mapPixels);
            mapTexture.Apply(false);
        }

        void UpdateMapMarker()
        {
            var c = game.City;
            var f = game.Rig.Focus / CityView.TileSize;
            mapMarker.anchorMin = mapMarker.anchorMax = new Vector2(f.x / c.Width, f.z / c.Height);
            mapMarker.anchoredPosition = Vector2.zero;
            // The icon points right; the camera looks north (up on the map) at yaw 0 and turns clockwise.
            mapMarker.localEulerAngles = new Vector3(0, 0, 90 - game.Rig.Yaw);
        }

        // ================================================================ bottom bar

        void BuildBottomBar()
        {
            var items = Tools.Bar;
            const float w = 90, h = 94, gap = 5;
            float width = items.Length * w + (items.Length - 1) * gap + 12;
            bottomBar = Rect("Construcción", wide, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(width, h + 12), new Vector2(.5f, 0));
            Panel(bottomBar, UiKit.PanelColor, 20);
            for (int k = 0; k < items.Length; k++)
            {
                var item = items[k];
                var b = MakeButton(bottomBar, item.Name, 6 + k * (w + gap), 6, w, h, 16, item.Icon, 42, item.Name, 16, () => PickItem(item), true);
                barButtons.Add((item, b));
            }

            hintPanel = Rect("Indicación", wide, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 132), new Vector2(600, 50), new Vector2(.5f, 0));
            Panel(hintPanel, UiKit.PanelColor, 25);
            hintText = Label(Stretch("Texto", hintPanel, 24, 0, 24, 0), 21, TextAnchor.MiddleCenter, Color.white);
            hintText.supportRichText = true;
        }

        void PickItem(Tools.BarItem item)
        {
            if (item.Tool.HasValue)
            {
                CloseFlyout();
                game.SelectTool(item.Tool.Value);
                return;
            }
            if (flyoutItem == item)
            {
                CloseFlyout();
                HighlightCategories();
                return;
            }
            OpenFlyout(item);
        }

        /// <summary>Menu above the bar with every building of a category: name, what it gives, and price or when it unlocks.</summary>
        void OpenFlyout(Tools.BarItem item)
        {
            CloseFlyout();
            HideInfo();
            if (popup != Popup.None) TogglePopup(Popup.None);
            flyoutItem = item;
            var category = item.Category.Value;
            var defs = new List<BuildingDef>(Catalog.InCategory(category));
            const float ch = 168, top = 46, minCard = 160, gap = 8;
            float left = MinimapSize + MinimapGap, available = wide.rect.width - left - MinimapGap;
            int rows = (available - gap) / defs.Count - gap < minCard ? 2 : 1;
            int perRow = (defs.Count + rows - 1) / rows;
            float cw = Mathf.Min(190, (available - gap) / perRow - gap);
            float flyWidth = perRow * (cw + gap) + gap;
            float centre = left + available * .5f - wide.rect.width * .5f;
            flyout = Rect("Menú " + item.Name, wide, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(centre, bottomPad + 116), new Vector2(flyWidth, top + rows * (ch + gap)), new Vector2(.5f, 0));
            Panel(flyout, UiKit.PanelColor, 18);
            TextAt(flyout, 16, 6, flyWidth - 32, 36, 22, TextAnchor.MiddleLeft, Color.white, true).text = Tools.CategoryName(category);
            var tint = Tools.CategoryColor(category);
            for (int k = 0; k < defs.Count; k++)
            {
                var def = defs[k];
                var tool = Tool.Build(def.Id);
                bool locked = game.City.Population < def.MinPopulation;
                var b = MakeButton(flyout, def.Name, gap + k % perRow * (cw + gap), top + k / perRow * (ch + gap), cw, ch, 14, Tools.Icon(def), 40, null, 0, () =>
                {
                    if (locked)
                    {
                        Toast($"{def.Name} se desbloquea con {N(def.MinPopulation)} habitantes");
                        return;
                    }
                    CloseFlyout();
                    game.SelectTool(tool);
                }, true);
                b.Icon.rectTransform.anchoredPosition = new Vector2(cw * .5f, -32);
                b.Icon.color = locked ? new Color(1, 1, 1, .3f) : Color.Lerp(tint, Color.white, .25f);
                var name = TextAt(b.Root, 6, 56, cw - 12, 44, 17, TextAnchor.MiddleCenter, locked ? UiKit.TextDim : Color.white, true);
                name.text = def.Name;
                name.lineSpacing = .9f;
                var summary = TextAt(b.Root, 4, 102, cw - 8, 22, 15, TextAnchor.MiddleCenter, UiKit.TextDim);
                summary.text = $"{def.Width}×{def.Height}  ·  {def.Summary}";
                FitOneLine(summary, 11);
                var price = TextAt(b.Root, 0, 128, cw, 28, 18, TextAnchor.MiddleCenter, locked ? UiKit.Bad : UiKit.Gold, true);
                price.text = locked ? $"Desde {N(def.MinPopulation)} hab." : Tools.CostLabel(tool);
                FitOneLine(price, 12);
                b.Glow.gameObject.SetActive(tool == currentTool);
                if (tool == currentTool) b.Back.color = UiKit.Accent;
                if (locked) b.Back.color = new Color(.08f, .12f, .2f, .9f);
            }
            HighlightCategories();
            UpdateHint();
        }

        void CloseFlyout()
        {
            if (flyout != null) Destroy(flyout.gameObject);
            flyout = null;
            flyoutItem = null;
            UpdateHint();
        }

        public void SelectTool(Tool t)
        {
            currentTool = t;
            HighlightCategories();
            HideInfo();
            UpdateHint();
        }

        void HighlightCategories()
        {
            var def = currentTool.Def;
            foreach (var (item, b) in barButtons)
            {
                bool on = flyoutItem != null ? flyoutItem == item
                    : item.Tool.HasValue ? item.Tool.Value == currentTool
                    : def != null && item.Category == def.Category;
                b.Back.color = on ? UiKit.Accent : Color.clear;
                b.Edge.color = on ? UiKit.AccentEdge : Color.clear;
                b.Glow.gameObject.SetActive(on);
                b.Label.color = on ? Color.white : new Color(.86f, .91f, 1f);
            }
        }

        public void ShowCost(string text)
        {
            costText = text;
            UpdateHint();
        }

        void UpdateHint()
        {
            if (hintPanel == null) return;
            string text = costText ?? Tools.Hint(currentTool);
            bool show = !string.IsNullOrEmpty(text) && flyout == null;
            hintPanel.gameObject.SetActive(show);
            if (!show) return;
            hintText.text = text;
            hintPanel.sizeDelta = new Vector2(hintText.preferredWidth + 56, 50);
        }

        // ================================================================ selected building

        void BuildInfoPanel()
        {
            infoPanel = Rect("Información", safe, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -115), new Vector2(430, 600), new Vector2(1, 1));
            Panel(infoPanel, UiKit.PanelColor, 20);

            var tile = At("Ícono", infoPanel, 14, 14, 88, 88);
            infoTile = Panel(tile, Color.white, 16, new Color(1, 1, 1, .35f));
            infoIcon = Icon(tile, "house", 44, 44, 56, Color.white);
            infoIcon.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .35f);
            infoTitle = TextAt(infoPanel, 116, 18, 254, 40, 29, TextAnchor.MiddleLeft, Color.white, true);
            FitOneLine(infoTitle, 18);
            infoSubtitle = TextAt(infoPanel, 116, 58, 300, 28, 20, TextAnchor.MiddleLeft, UiKit.AccentText);
            var close = MakeButton(infoPanel, "Cerrar", 374, 10, 46, 46, 12, "close", 24, null, 0, () => game.Inspect(-1, -1));
            close.Back.color = Color.clear;
            close.Edge.enabled = false;
            Plain(At("Divisor", infoPanel, 14, 114, 402, 2), UiKit.Divider);

            for (int k = 0; k < cards.Length; k++)
            {
                var card = new Card { Root = At("Dato " + k, infoPanel, 14 + k % 2 * 207, 126 + k / 2 * 94, 195, 84) };
                Panel(card.Root, UiKit.PanelInner, 12);
                card.Icon = Icon(card.Root, "people", 32, 42, 36, Color.white);
                card.Label = TextAt(card.Root, 60, 10, 130, 24, 18, TextAnchor.MiddleLeft, UiKit.TextDim);
                card.Value = TextAt(card.Root, 60, 33, 130, 32, 25, TextAnchor.MiddleLeft, Color.white, true);
                FitOneLine(card.Value, 15);
                card.BarBack = At("Barra", card.Root, 60, 68, 118, 7);
                Panel(card.BarBack, new Color(1, 1, 1, .12f), 4, null);
                card.BarFill = Rect("Relleno", card.BarBack, Vector2.zero, new Vector2(.5f, 1), Vector2.zero, Vector2.zero, Vector2.zero);
                Panel(card.BarFill, UiKit.Good, 4, null);
                cards[k] = card;
            }

            detailsBox = At("Detalles", infoPanel, 14, 0, 402, 150);
            Panel(detailsBox, UiKit.PanelInner, 12);
            TextAt(detailsBox, 16, 6, 300, 32, 21, TextAnchor.MiddleLeft, Color.white, true).text = "Detalles";
            for (int k = 0; k < rows.Length; k++)
            {
                var row = new Row { Root = At("Fila " + k, detailsBox, 0, 42 + k * 34, 402, 32) };
                row.Icon = Icon(row.Root, "bolt", 28, 16, 24, UiKit.Warn);
                row.Label = TextAt(row.Root, 50, 0, 220, 32, 20, TextAnchor.MiddleLeft, Color.white);
                row.Value = TextAt(row.Root, 220, 0, 166, 32, 20, TextAnchor.MiddleRight, Color.white, true);
                rows[k] = row;
            }

            upkeepRow = At("Mantenimiento", infoPanel, 14, 0, 402, 46);
            Panel(upkeepRow, UiKit.PanelInner, 12);
            Icon(upkeepRow, "coin", 28, 23, 26, UiKit.Gold);
            TextAt(upkeepRow, 50, 0, 200, 46, 20, TextAnchor.MiddleLeft, Color.white).text = "Mantenimiento";
            upkeepValue = TextAt(upkeepRow, 200, 0, 186, 46, 20, TextAnchor.MiddleRight, Color.white, true);

            demolishButton = MakeButton(infoPanel, "Demoler", 14, 0, 402, 54, 14, "bulldoze", 30, null, 0, () => game.Demolish(selected.x, selected.y));
            demolishButton.Back.color = new Color(.45f, .14f, .13f, .95f);
            demolishButton.Edge.color = new Color(1, .5f, .45f, .6f);
            demolishButton.Icon.rectTransform.anchoredPosition = new Vector2(120, -27);
            demolishLabel = TextAt(demolishButton.Root, 145, 0, 230, 54, 22, TextAnchor.MiddleLeft, Color.white, true);

            infoPanel.gameObject.SetActive(false);
        }

        /// <summary>Shows the panel for whatever is on a tile, or hides it when the tile is off the map.</summary>
        public void Select(int x, int y)
        {
            var c = game.City;
            if (!c.InBounds(x, y))
            {
                HideInfo();
                return;
            }
            if (popup == Popup.Settings) TogglePopup(Popup.None);
            CloseFlyout();
            HighlightCategories();
            selected = new Vector2Int(x, y);
            infoPanel.gameObject.SetActive(true);
            RefreshInfo();
        }

        public void HideInfo()
        {
            selected = new Vector2Int(-1, -1);
            if (infoPanel != null) infoPanel.gameObject.SetActive(false);
            if (game != null && game.View != null) game.View.ClearSelection();
        }

        void RefreshInfo()
        {
            var c = game.City;
            if (selected.x < 0) return;
            int i = c.Index(selected.x, selected.y);
            foreach (var card in cards) card.Root.gameObject.SetActive(false);
            foreach (var row in rows) row.Root.gameObject.SetActive(false);
            int nCards = 0, nRows = 0;
            void AddCard(string icon, Color iconColor, string label, string value, Color valueColor, float bar = -1)
            {
                var card = cards[nCards++];
                card.Root.gameObject.SetActive(true);
                card.Icon.sprite = Icons.Get(icon);
                card.Icon.color = iconColor;
                card.Label.text = label;
                card.Value.text = value;
                card.Value.color = valueColor;
                card.BarBack.gameObject.SetActive(bar >= 0);
                if (bar >= 0) card.BarFill.anchorMax = new Vector2(Mathf.Clamp01(bar), 1);
            }
            void AddRow(string icon, Color iconColor, string label, string value, Color valueColor)
            {
                var row = rows[nRows++];
                row.Root.gameObject.SetActive(true);
                row.Icon.sprite = Icons.Get(icon);
                row.Icon.color = iconColor;
                row.Label.text = label;
                row.Value.text = value;
                row.Value.color = valueColor;
            }
            (string, Color) YesNo(bool v) => v ? ("Sí", UiKit.Good) : ("No", UiKit.Bad);
            var blue = new Color(.6f, .8f, 1f);
            int desire = c.Desirability[i];
            Color desireColor = desire > 0 ? UiKit.Good : desire < 0 ? UiKit.Bad : Color.white;
            int upkeep = 0;
            string demolish = null;
            var def = c.DefAt(i);

            if (def == null)
            {
                var t = c.Terrain[i];
                bool reserve = c.InReserve(selected.x, selected.y);
                string icon = "explore";
                switch (t)
                {
                    case Terrain.Water:
                        infoTitle.text = "Río";
                        infoSubtitle.text = "Solo se cruza con un puente";
                        infoTile.color = new Color(.2f, .48f, .72f);
                        icon = "water";
                        break;
                    case Terrain.Mountain:
                        infoTitle.text = "Montaña";
                        infoSubtitle.text = "No se puede construir · más adelante, minas";
                        infoTile.color = new Color(.45f, .43f, .4f);
                        icon = "mountain";
                        break;
                    case Terrain.Fertile:
                        infoTitle.text = "Tierra fértil";
                        infoSubtitle.text = "Buena para granjas";
                        infoTile.color = new Color(.6f, .6f, .3f);
                        icon = "tree";
                        break;
                    case Terrain.Stumps:
                        infoTitle.text = "Tala reciente";
                        infoSubtitle.text = "Los árboles vuelven a crecer";
                        infoTile.color = new Color(.45f, .38f, .26f);
                        icon = "axe";
                        break;
                    case Terrain.Sapling:
                        infoTitle.text = "Árboles jóvenes";
                        infoSubtitle.text = "Pronto serán bosque";
                        infoTile.color = new Color(.3f, .55f, .3f);
                        icon = "forest";
                        break;
                    case Terrain.Forest:
                        infoTitle.text = reserve ? "Reserva forestal" : "Bosque";
                        infoSubtitle.text = reserve ? "Aquí los árboles vuelven a crecer" : "Márcalo con Talar para tener madera";
                        infoTile.color = new Color(.22f, .45f, .22f);
                        icon = "forest";
                        break;
                    default:
                        infoTitle.text = reserve ? "Reserva forestal" : "Terreno libre";
                        infoSubtitle.text = reserve ? "Claro del bosque" : "Terreno";
                        infoTile.color = new Color(.45f, .62f, .32f);
                        break;
                }
                infoIcon.sprite = Icons.Get(icon);
                AddCard("star", UiKit.Gold, "Atractivo", Signed(desire), desireColor);
                if (t != Terrain.Water && t != Terrain.Mountain)
                {
                    var (power, powerColor) = YesNo(c.PowerCover[i]);
                    AddCard("bolt", UiKit.Warn, "Red eléctrica", power, powerColor);
                }
                game.View.ClearSelection();
            }
            else
            {
                int a = c.Anchor[i] >= 0 ? c.Anchor[i] : i;
                infoTile.color = Tools.CategoryColor(def.Category);
                infoIcon.sprite = Icons.Get(Tools.Icon(def));
                infoTitle.text = def.Name;
                infoSubtitle.text = Tools.CategoryName(def.Category);
                upkeep = def.Upkeep;
                demolish = $"Demoler  ·  $ {Catalog.BulldozeCost}";
                var (power, powerColor) = YesNo(c.Powered[a]);
                var (street, streetColor) = YesNo(c.Served[a]);
                bool working = c.Working(a);
                switch (def.Kind)
                {
                    case Kind.Road:
                        bool connected = c.RoadConnected[i];
                        AddCard("road", blue, "Conexión", connected ? "Con salida" : "Aislada", connected ? UiKit.Good : UiKit.Bad);
                        AddCard("star", UiKit.Gold, "Atractivo", Signed(desire), desireColor);
                        break;
                    case Kind.Housing:
                        AddCard("people", blue, "Habitantes", $"{N(c.Occupants[a])} / {N(def.Residents)}", Color.white, c.Occupants[a] / (float)def.Residents);
                        int mood = Mathf.Clamp(Mathf.RoundToInt((float)c.HomeSatisfaction(a)), 0, 100);
                        AddCard("smile", mood >= 70 ? UiKit.Good : mood >= 45 ? UiKit.Warn : UiKit.Bad, "Satisfacción", $"{mood} %", Color.white, mood / 100f);
                        AddCard("bolt", UiKit.Warn, "Electricidad", power, powerColor);
                        AddCard("road", blue, "Calle conectada", street, streetColor);
                        // Services that reach this home, and the first one it misses.
                        int have = 0;
                        string missing = null;
                        foreach (var s in Catalog.Services)
                        {
                            if (c.Covered(a, s)) have++;
                            else if (missing == null && c.Population >= Catalog.ServiceThreshold(s)) missing = Catalog.ServiceName(s);
                        }
                        AddRow("cross", new Color(1f, .6f, .6f), "Servicios cerca", $"{have} de {Catalog.Services.Length}", Color.white);
                        if (missing != null) AddRow("warning", UiKit.Warn, "Le falta", missing, UiKit.Bad);
                        AddRow("star", UiKit.Gold, "Atractivo del lugar", Signed(desire), desireColor);
                        break;
                    case Kind.Jobs:
                        AddCard("briefcase", blue, "Empleos", working ? N(def.Jobs) : "0", working ? Color.white : UiKit.Bad);
                        AddCard("factory", blue, "Tipo", def.Industrial ? "Industria" : "Comercio", Color.white);
                        AddCard("bolt", UiKit.Warn, "Electricidad", power, powerColor);
                        AddCard("road", blue, "Calle conectada", street, streetColor);
                        AddRow("bolt", UiKit.Warn, "Consumo eléctrico", $"{def.PowerUse} MW", Color.white);
                        if (def.Desirability != 0) AddRow("star", UiKit.Gold, "Atractivo alrededor", Signed(def.Desirability), def.Desirability > 0 ? UiKit.Good : UiKit.Bad);
                        break;
                    case Kind.Power:
                        float use = c.PowerSupply > 0 ? c.PowerDemand / (float)c.PowerSupply : 0;
                        if (def.PowerSupply > 0) AddCard("bolt", UiKit.Warn, "Producción", working ? $"{N(def.PowerSupply)} MW" : "0 MW", working ? Color.white : UiKit.Bad);
                        AddCard("road", blue, "Red", "Por las calles", Color.white);
                        AddCard("chart", blue, "Uso de la red", $"{Mathf.RoundToInt(use * 100)} %", use > 1 ? UiKit.Bad : Color.white, use);
                        AddCard("road", blue, "Calle conectada", street, streetColor);
                        AddRow("bolt", UiKit.Warn, "Consumo de la ciudad", $"{N(c.PowerDemand)} MW", Color.white);
                        if (def.Desirability != 0) AddRow("star", UiKit.Gold, "Atractivo alrededor", Signed(def.Desirability), UiKit.Bad);
                        break;
                    default:
                        if (def.Service != Service.None)
                        {
                            AddCard("radius", blue, "Alcance", $"{def.Radius} casillas", Color.white);
                            int coverage = c.ServiceCoverage(def.Service);
                            AddCard("people", blue, "Cobertura", $"{coverage} %", Color.white, coverage / 100f);
                        }
                        else AddCard("star", UiKit.Gold, def.CityBonus > 0 ? "Satisfacción" : "Atractivo", def.CityBonus > 0 ? $"+{def.CityBonus}" : Signed(def.Desirability), UiKit.Good);
                        AddCard("bolt", UiKit.Warn, "Electricidad", power, powerColor);
                        AddCard("road", blue, "Calle conectada", street, streetColor);
                        if (def.Service != Service.None) AddRow("cross", new Color(1f, .6f, .6f), "Servicio", Catalog.ServiceName(def.Service), Color.white);
                        if (def.Capacity > 0) AddRow("chart", blue, "Capacidad / uso", $"{N(def.Capacity)} / {N(c.UtilityDemand(def.Service))} {Catalog.UtilityUnit(def.Service)}", Color.white);
                        AddRow("bolt", UiKit.Warn, "Consumo eléctrico", $"{def.PowerUse} MW", Color.white);
                        if (def.PowerSupply > 0) AddRow("bolt", UiKit.Warn, "Produce", $"{def.PowerSupply} MW", Color.white);
                        if (def.Desirability != 0) AddRow("star", UiKit.Gold, "Atractivo alrededor", Signed(def.Desirability), def.Desirability > 0 ? UiKit.Good : UiKit.Bad);
                        break;
                }
                if (c.Construction[a] > 0)
                {
                    int total = Mathf.Max(1, Catalog.BuildTicks(def)), left = c.Construction[a];
                    AddRow("level", UiKit.Warn, "En construcción", $"{(total - left) * 100 / total} %  ·  faltan {Duration(left)}", UiKit.Warn);
                }
                else if (!def.IsRoad && !working) AddRow("warning", UiKit.Bad, "No funciona", !c.Served[a] ? "sin calle conectada" : "sin electricidad", UiKit.Bad);
                var (sw, sh) = c.Size(a);
                game.View.SetSelection(a % c.Width, a / c.Width, sw, sh);
            }

            // Stack the sections that are in use.
            float y = 126 + (nCards + 1) / 2 * 94;
            detailsBox.gameObject.SetActive(nRows > 0);
            if (nRows > 0)
            {
                detailsBox.anchoredPosition = new Vector2(14, -y);
                detailsBox.sizeDelta = new Vector2(402, 46 + nRows * 34);
                y += 46 + nRows * 34 + 12;
            }
            upkeepRow.gameObject.SetActive(upkeep > 0);
            if (upkeep > 0)
            {
                upkeepRow.anchoredPosition = new Vector2(14, -y);
                upkeepValue.text = def.IsRoad ? $"$ {upkeep} /día por casilla" : $"$ {N(upkeep)} /día";
                y += 46 + 12;
            }
            demolishButton.Root.gameObject.SetActive(demolish != null);
            if (demolish != null)
            {
                demolishButton.Root.anchoredPosition = new Vector2(14, -y);
                demolishLabel.text = demolish;
                y += 54 + 12;
            }
            infoPanel.sizeDelta = new Vector2(430, y + 2);
        }

        // ================================================================ messages

        void BuildToast()
        {
            toastPanel = Rect("Aviso", wide, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -115), new Vector2(600, 56), new Vector2(.5f, 1));
            Panel(toastPanel, new Color(.36f, .09f, .09f, .94f), 18, new Color(1, .5f, .45f, .6f));
            Icon(toastPanel, "warning", 34, 28, 30, UiKit.Warn);
            toastText = TextAt(toastPanel, 60, 0, 520, 56, 22, TextAnchor.MiddleLeft, Color.white, true);
            toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
            foreach (var g in toastPanel.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            toastPanel.gameObject.SetActive(false);
        }

        public void Toast(string text)
        {
            toastText.text = text;
            toastPanel.sizeDelta = new Vector2(toastText.preferredWidth + 88, 56);
            toastPanel.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + 2.5f;
        }

        // ================================================================ updates

        void Update()
        {
            if (game == null) return;
            if (Screen.safeArea != lastSafeArea) ApplySafeArea();
            if (Time.unscaledTime >= refreshAt) Refresh();
            if (toastPanel.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastPanel.gameObject.SetActive(false);
            if (popup == Popup.Settings && resetArmedUntil > 0 && Time.unscaledTime > resetArmedUntil)
            {
                resetArmedUntil = 0;
                FillPopup();
            }
            UpdateMapMarker();
        }

        void ApplySafeArea()
        {
            lastSafeArea = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            safe.anchorMin = new Vector2(lastSafeArea.xMin / Screen.width, lastSafeArea.yMin / Screen.height);
            safe.anchorMax = new Vector2(lastSafeArea.xMax / Screen.width, lastSafeArea.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            wide.anchorMin = Vector2.zero;
            bottomPad = Mathf.Max(14, safe.anchorMin.y * 887 * .45f + 4);
            minimapPanel.anchoredPosition = new Vector2(0, bottomPad);
            bottomBar.anchoredPosition = new Vector2(0, bottomPad);
            hintPanel.anchoredPosition = new Vector2(0, bottomPad + 120);
            if (flyout != null) flyout.anchoredPosition = new Vector2(flyout.anchoredPosition.x, bottomPad + 116);
            wide.anchorMax = new Vector2(1, safe.anchorMax.y);
            // The screen's corners are rounded where the notch or island leaves an inset; keep clear of the curve.
            float inset = Mathf.Max(lastSafeArea.xMin, Screen.width - lastSafeArea.xMax) / Screen.height * 887;
            sideMargin = Mathf.Max(30, inset * .45f);
            wide.offsetMin = new Vector2(sideMargin, 0);
            wide.offsetMax = new Vector2(-sideMargin, 0);
            float wideWidth = Screen.width / (float)Screen.height * 887 - sideMargin * 2;
            topBar.sizeDelta = new Vector2(wideWidth / TopBarScale, 92);
        }

        public void Refresh()
        {
            refreshAt = Time.unscaledTime + .25f;
            var c = game.City;
            if (c == null) return;

            if (!cityName.isFocused && cityName.text != c.Name) cityName.text = c.Name;
            levelText.text = $"Nivel de ciudad {Progress.Level(c.Population)}";
            levelFill.anchorMax = new Vector2(Mathf.Max(.02f, Progress.LevelProgress(c.Population)), 1);

            popValue.text = N(c.Population);
            popSub.text = $"Empleos {N(c.Jobs)}";

            if (c.Population == 0)
            {
                satValue.text = "—";
                satSub.text = "Sin habitantes";
                satSub.color = UiKit.TextDim;
                satIcon.color = UiKit.TextDim;
            }
            else
            {
                var mood = c.Satisfaction >= 70 ? UiKit.Good : c.Satisfaction >= 45 ? UiKit.Warn : UiKit.Bad;
                satValue.text = $"{c.Satisfaction} %";
                satSub.text = c.Satisfaction >= 70 ? "Alta" : c.Satisfaction >= 45 ? "Media" : "Baja";
                satSub.color = mood;
                satIcon.color = mood;
            }

            moneyValue.text = $"$ {N(c.Money)}";
            moneyValue.color = c.Money < 0 ? UiKit.Bad : Color.white;
            int net = c.LastIncome - c.LastExpenses;
            if (!c.HasBudget)
            {
                moneySub.text = "Cobro a las 6:00";
                moneySub.color = UiKit.TextDim;
            }
            else
            {
                moneySub.text = $"{Signed(net)} /día";
                moneySub.color = net >= 0 ? UiKit.Good : UiKit.Bad;
            }

            float hour = game.TimeOfDay * 24;
            int hh = Mathf.FloorToInt(hour) % 24, mm = Mathf.FloorToInt(hour * 60) % 60 / 5 * 5;
            clockValue.text = $"{hh:00}:{mm:00}";
            clockDay.text = $"Día {c.Day + 1}";
            bool daylight = DayNight.SunHeight(game.TimeOfDay) > -.05f;
            clockIcon.sprite = Icons.Get(daylight ? "sun" : "moon");
            clockIcon.color = daylight ? UiKit.Gold : new Color(.75f, .82f, 1f);
            woodValue.text = N(c.Wood);

            ShowResource(0, c.PowerSupply, c.PowerDemand, "MW");
            ShowResource(1, c.UtilitySupply(Service.Water), c.UtilityDemand(Service.Water), "m³");
            ShowResource(2, c.UtilitySupply(Service.Sewage), c.UtilityDemand(Service.Sewage), "m³");
            ShowResource(3, c.UtilitySupply(Service.Waste), c.UtilityDemand(Service.Waste), "kg");

            SetDemand(0, c.DemandResidential);
            SetDemand(1, c.DemandCommercial);
            SetDemand(2, c.DemandIndustrial);

            RefreshGoals(c);
            RefreshAlerts(c);
            if (popup != Popup.None) FillPopup();
            if (selected.x >= 0) RefreshInfo();
            if (Time.unscaledTime >= mapRefreshAt)
            {
                mapRefreshAt = Time.unscaledTime + .3f;
                RefreshMinimap();
            }
        }

        void SetDemand(int k, float demand)
        {
            var bar = demandFill[k];
            bar.sizeDelta = new Vector2(12, Mathf.Max(2, Mathf.Abs(demand) * 19));
            bar.localScale = new Vector3(1, demand >= 0 ? 1 : -1, 1);
        }

        /// <summary>The clock runs at one steady pace; there are no speed buttons any more.</summary>
        public void SetSpeedHighlight(int s) { }

        /// <summary>Opens a pop-up or the building menu of a category; used by the screenshot mode.</summary>
        public void Showcase(string what)
        {
            if (what == "finanzas") TogglePopup(Popup.Finance);
            foreach (var item in Tools.Bar) if (item.Name == what) PickItem(item);
        }

        /// <summary>Ticks as hours and minutes: "2 h 15 min", "40 min".</summary>
        static string Duration(int ticks)
        {
            int minutes = ticks * 60 / City.TicksPerHour, h = minutes / 60, m = minutes % 60;
            return h == 0 ? $"{m} min" : m == 0 ? $"{h} h" : $"{h} h {m} min";
        }

        static string Signed(int v) => v > 0 ? $"+{N(v)}" : v < 0 ? $"−{N(-v)}" : "0";

        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ================================================================ widgets

        static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot ?? new Vector2(.5f, .5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>Box placed from the parent's top-left corner, measured downwards.</summary>
        static RectTransform At(string name, Transform parent, float x, float y, float w, float h) =>
            Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h), new Vector2(0, 1));

        static RectTransform Stretch(string name, Transform parent, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        static Image Plain(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Rounded panel with the usual light border. Radius is in canvas units.</summary>
        static Image Panel(RectTransform rt, Color color, float radius) => Panel(rt, color, radius, UiKit.EdgeColor);

        /// <summary>Rounded panel with a border of the given colour, or none when it is null.</summary>
        static Image Panel(RectTransform rt, Color color, float radius, Color? edge)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiKit.Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 24f / radius;
            img.color = color;
            if (edge.HasValue)
            {
                var border = Stretch("Borde", rt).gameObject.AddComponent<Image>();
                border.sprite = UiKit.Outline;
                border.type = Image.Type.Sliced;
                border.pixelsPerUnitMultiplier = 24f / radius;
                border.color = edge.Value;
                border.raycastTarget = false;
                border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            return img;
        }

        Image Icon(RectTransform parent, string icon, float cx, float cy, float size, Color color)
        {
            var rt = Rect("Ícono " + icon, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(cx, -cy), new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Icons.Get(icon);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Text Label(RectTransform rt, int size, TextAnchor anchor, Color color, bool bold = false)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Text TextAt(RectTransform parent, float x, float y, float w, float h, int size, TextAnchor anchor, Color color, bool bold = false) =>
            Label(At("Texto", parent, x, y, w, h), size, anchor, color, bold);

        /// <summary>Button with an optional halo, rounded background, border, icon and caption.</summary>
        Btn MakeButton(RectTransform parent, string name, float x, float y, float w, float h, float radius, string icon, float iconSize, string label, int labelSize, Action onClick, bool glow = false)
        {
            var b = new Btn { Root = At(name, parent, x, y, w, h) };
            var hit = b.Root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            if (glow)
            {
                b.Glow = Stretch("Halo", b.Root, -18, -18, -18, -18).gameObject.AddComponent<Image>();
                b.Glow.sprite = UiKit.Glow;
                b.Glow.type = Image.Type.Sliced;
                b.Glow.pixelsPerUnitMultiplier = 2.2f;
                b.Glow.color = new Color(.25f, .6f, 1f, .55f);
                b.Glow.raycastTarget = false;
                b.Glow.gameObject.SetActive(false);
            }
            var back = Stretch("Fondo", b.Root);
            b.Back = back.gameObject.AddComponent<Image>();
            b.Back.sprite = UiKit.Rounded;
            b.Back.type = Image.Type.Sliced;
            b.Back.pixelsPerUnitMultiplier = 24f / radius;
            b.Back.color = UiKit.ButtonColor;
            b.Back.raycastTarget = false;
            b.Edge = Stretch("Borde", b.Root).gameObject.AddComponent<Image>();
            b.Edge.sprite = UiKit.Outline;
            b.Edge.type = Image.Type.Sliced;
            b.Edge.pixelsPerUnitMultiplier = 24f / radius;
            b.Edge.color = UiKit.EdgeColor;
            b.Edge.raycastTarget = false;
            if (icon != null) b.Icon = Icon(b.Root, icon, w * .5f, label != null ? h * .4f : h * .5f, iconSize, Color.white);
            if (label != null)
            {
                b.Label = Label(At("Texto", b.Root, 0, label != null && icon != null ? h - 32 : 0, w, icon != null ? 28 : h), labelSize, TextAnchor.MiddleCenter, Color.white, true);
                b.Label.text = label;
            }
            var button = b.Root.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick());
            button.gameObject.AddComponent<PressFeedback>();
            return b;
        }
    }

    /// <summary>Shrinks a button slightly while it is held down.</summary>
    public sealed class PressFeedback : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => transform.localScale = Vector3.one * .94f;
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) => transform.localScale = Vector3.one;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => transform.localScale = Vector3.one;
    }
}
