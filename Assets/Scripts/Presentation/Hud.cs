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

        enum Popup { None, Finance, Alerts, Help, Settings }

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
        Rect lastSafeArea;
        float refreshAt, toastUntil, resetArmedUntil, mapRefreshAt;

        // Top bar.
        InputField cityName;
        Text levelText, popValue, popSub, satValue, satSub, moneyValue, moneySub, powerValue, powerSub, dateText, dayText;
        Image satIcon;
        RectTransform levelFill;
        readonly RectTransform[] demandFill = new RectTransform[3];
        readonly Btn[] speedButtons = new Btn[3];

        // Goals.
        RectTransform goalsPanel;
        Image goalsChevron;
        bool goalsOpen = true;
        readonly Objective[] goals = new Objective[3];

        // Left column and its pop-up.
        Image alertBadge;
        Text alertBadgeText;
        readonly List<string> alerts = new List<string>();
        RectTransform popupPanel, taxRow, settingsRow;
        Text popupTitle, popupBody, taxText;
        Btn newCityButton;
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
        readonly Row[] rows = new Row[5];
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
            var bar = Rect("Barra superior", safe, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -10), new Vector2(-24, 92), new Vector2(.5f, 1));
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
            (powerValue, powerSub, _) = Stat(bar, "Energía", 240, "bolt", UiKit.Warn, "Energía");

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

            var date = Block(bar, "Fecha", 150, false);
            dateText = TextAt(date, 0, 14, 150, 24, 19, TextAnchor.MiddleCenter, UiKit.TextDim);
            dayText = TextAt(date, 0, 40, 150, 38, 30, TextAnchor.MiddleCenter, Color.white, true);

            var speed = Block(bar, "Velocidad", 180, false);
            var pill = At("Botones", speed, 8, 15, 164, 62);
            Panel(pill, UiKit.PanelInner, 31);
            string[] icons = { "pause", "play", "fast" };
            for (int k = 0; k < 3; k++)
            {
                int s = k;
                speedButtons[k] = MakeButton(pill, icons[k], 6 + k * 51, 6, 50, 50, 25, icons[k], 28, null, 0, () => game.SetSpeed(s));
                speedButtons[k].Edge.enabled = false;
            }

            var gear = Block(bar, "Ajustes", 70, false);
            MakeButton(gear, "Ajustes", 4, 15, 62, 62, 18, "gear", 36, null, 0, () => TogglePopup(Popup.Settings));
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
            goalsPanel = At("Objetivos", safe, 12, 114, 420, 172);
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
            var col = At("Columna", safe, 12, 298, 76, 216);
            Panel(col, UiKit.PanelColor, 18);
            MakeButton(col, "Finanzas", 8, 8, 60, 60, 14, "chart", 32, null, 0, () => TogglePopup(Popup.Finance)).Edge.enabled = false;
            var alertsButton = MakeButton(col, "Avisos", 8, 78, 60, 60, 14, "warning", 32, null, 0, () => TogglePopup(Popup.Alerts));
            alertsButton.Edge.enabled = false;
            MakeButton(col, "Ayuda", 8, 148, 60, 60, 14, "question", 32, null, 0, () => TogglePopup(Popup.Help)).Edge.enabled = false;

            var badge = At("Contador", alertsButton.Root, 40, -6, 26, 26);
            alertBadge = badge.gameObject.AddComponent<Image>();
            alertBadge.sprite = UiKit.Circle;
            alertBadge.color = new Color(.96f, .55f, .16f);
            alertBadge.raycastTarget = false;
            alertBadgeText = Label(Stretch("Número", badge), 16, TextAnchor.MiddleCenter, Color.white, true);
        }

        void BuildPopup()
        {
            popupPanel = At("Ventana", safe, 100, 298, 440, 300);
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

            settingsRow = At("Opciones", popupPanel, 22, 0, 396, 56);
            newCityButton = MakeButton(settingsRow, "Nueva ciudad", 0, 0, 396, 56, 14, null, 0, "Empezar una ciudad nueva", 22, NewCity);
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
                popupPanel.anchoredPosition = new Vector2(-12, -114);
                HideInfo();
            }
            else
            {
                popupPanel.anchorMin = popupPanel.anchorMax = popupPanel.pivot = new Vector2(0, 1);
                popupPanel.anchoredPosition = new Vector2(100, -298);
            }
            FillPopup();
        }

        void FillPopup()
        {
            if (popup == Popup.None) return;
            var c = game.City;
            taxRow.gameObject.SetActive(popup == Popup.Finance);
            settingsRow.gameObject.SetActive(popup == Popup.Settings);
            switch (popup)
            {
                case Popup.Finance:
                    popupTitle.text = "Finanzas";
                    int net = c.LastIncome - c.LastExpenses;
                    popupBody.text = c.Day < City.DaysPerMonth
                        ? "El primer balance llega al terminar el mes.\n\n"
                        : $"Ingresos del mes pasado:  <b>$ {N(c.LastIncome)}</b>\nGastos del mes pasado:  <b>$ {N(c.LastExpenses)}</b>\nBalance:  <b><color={Hex(net >= 0 ? UiKit.Good : UiKit.Bad)}>{Signed(net)}</color></b>\n\n";
                    popupBody.text += "<color=#A9BCE0>Cada mes cobras impuestos a habitantes y empleos, y pagas el mantenimiento. Subir el impuesto da más dinero pero frena el crecimiento y baja la satisfacción.</color>";
                    taxText.text = $"{c.TaxRate} %";
                    break;
                case Popup.Alerts:
                    popupTitle.text = "Avisos";
                    popupBody.text = alerts.Count == 0 ? "Todo en orden." : "• " + string.Join("\n• ", alerts);
                    break;
                case Popup.Help:
                    popupTitle.text = "Cómo se juega";
                    popupBody.text =
                        "Traza <b>calles</b> conectadas con la carretera y marca <b>zonas</b> a su lado. Construye una <b>central eléctrica</b>: sin calle y sin luz nada crece.\n\n" +
                        "<color=#A9BCE0>Un dedo construye o, en Explorar, mueve el mapa. Dos dedos mueven, acercan y giran. Toca un edificio para ver sus datos.</color>";
                    break;
                case Popup.Settings:
                    popupTitle.text = "Ajustes";
                    popupBody.text = $"Horizonte {Application.version}\n<color=#A9BCE0>La partida se guarda sola cada mes y al salir.</color>";
                    newCityButton.Label.text = Time.unscaledTime < resetArmedUntil ? "Toca otra vez para confirmar" : "Empezar una ciudad nueva";
                    break;
            }
            float bodyHeight = popupBody.preferredHeight;
            popupBody.rectTransform.sizeDelta = new Vector2(396, bodyHeight);
            float y = 58 + bodyHeight + 14;
            foreach (var extra in new[] { taxRow, settingsRow })
            {
                if (!extra.gameObject.activeSelf) continue;
                extra.anchoredPosition = new Vector2(22, -y);
                y += 56 + 14;
            }
            popupPanel.sizeDelta = new Vector2(440, y + 8);
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
                if (!c.Served[i]) noRoad++;
                if (def.Kind == Kind.Power || !c.Served[i]) continue;
                consumers++;
                if (c.Powered[i]) continue;
                unpowered++;
                bool reached = false;
                for (int dy = 0; dy < def.Height && !reached; dy++)
                for (int dx = 0; dx < def.Width && !reached; dx++)
                    reached = c.PowerCover[i + dy * c.Width + dx];
                if (!reached) outOfReach++;
            }
            if (c.PowerSupply == 0 && consumers > 0) alerts.Add("No hay centrales eléctricas: sin luz nada funciona.");
            else if (c.PowerDemand > c.PowerSupply) alerts.Add($"La energía no alcanza: faltan {N(c.PowerDemand - c.PowerSupply)} MW.");
            if (outOfReach > 0 && c.PowerSupply > 0) alerts.Add($"{N(outOfReach)} {(outOfReach == 1 ? "edificio queda" : "edificios quedan")} lejos de la red: construye una subestación cerca.");
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
            else if (c.Day >= City.DaysPerMonth && c.LastIncome < c.LastExpenses) alerts.Add("Gastas más de lo que ingresas cada mes.");
            if (c.TaxRate >= 13) alerts.Add("Los impuestos altos espantan a la gente.");
            alertBadge.gameObject.SetActive(alerts.Count > 0);
            alertBadgeText.text = alerts.Count.ToString();
        }

        // ================================================================ minimap

        void BuildMinimap()
        {
            var panel = Rect("Minimapa", safe, Vector2.zero, Vector2.zero, new Vector2(12, 12), new Vector2(236, 236), Vector2.zero);
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
            Color grass = new Color(.4f, .55f, .29f), forest = new Color(.22f, .38f, .18f), water = new Color(.2f, .44f, .66f);
            for (int i = 0; i < mapPixels.Length; i++)
            {
                var def = c.DefAt(i);
                Color col;
                if (def == null) col = c.Terrain[i] == Terrain.Water ? water : c.Terrain[i] == Terrain.Forest ? forest : grass;
                else if (def.IsRoad) col = new Color(.86f, .87f, .9f);
                else col = Tools.CategoryColor(def.Category);
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
            const float w = 96, h = 94, gap = 5;
            float width = items.Length * w + (items.Length - 1) * gap + 12;
            bottomBar = Rect("Construcción", safe, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(width, h + 12), new Vector2(.5f, 0));
            Panel(bottomBar, UiKit.PanelColor, 20);
            for (int k = 0; k < items.Length; k++)
            {
                var item = items[k];
                var b = MakeButton(bottomBar, item.Name, 6 + k * (w + gap), 6, w, h, 16, item.Icon, 42, item.Name, 18, () => PickItem(item), true);
                barButtons.Add((item, b));
            }

            hintPanel = Rect("Indicación", safe, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 132), new Vector2(600, 50), new Vector2(.5f, 0));
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
            int index = Array.IndexOf(Tools.Bar, item);
            var button = barButtons[index].button.Root;
            float centre = button.anchoredPosition.x + button.sizeDelta.x * .5f - bottomBar.sizeDelta.x * .5f;
            float available = safe.rect.width - 24;
            float cw = Mathf.Min(190, (available - 8) / defs.Count - 8);
            const float ch = 168, top = 46;
            float flyWidth = defs.Count * (cw + 8) + 8;
            float limit = Mathf.Max(0, safe.rect.width * .5f - 12 - flyWidth * .5f);
            centre = Mathf.Clamp(centre, -limit, limit);
            flyout = Rect("Menú " + item.Name, safe, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(centre, 128), new Vector2(flyWidth, ch + top + 8), new Vector2(.5f, 0));
            Panel(flyout, UiKit.PanelColor, 18);
            TextAt(flyout, 16, 6, flyWidth - 32, 36, 22, TextAnchor.MiddleLeft, Color.white, true).text = Tools.CategoryName(category);
            var tint = Tools.CategoryColor(category);
            for (int k = 0; k < defs.Count; k++)
            {
                var def = defs[k];
                var tool = Tool.Build(def.Id);
                bool locked = game.City.Population < def.MinPopulation;
                var b = MakeButton(flyout, def.Name, 8 + k * (cw + 8), top, cw, ch, 14, Tools.Icon(def), 40, null, 0, () =>
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
            infoPanel = Rect("Información", safe, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -114), new Vector2(430, 600), new Vector2(1, 1));
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
                infoTitle.text = t == Terrain.Water ? "Río" : t == Terrain.Forest ? "Bosque" : "Terreno libre";
                infoSubtitle.text = t == Terrain.Water ? "Solo se cruza con un puente" : "Terreno";
                infoTile.color = t == Terrain.Water ? new Color(.2f, .48f, .72f) : t == Terrain.Forest ? new Color(.22f, .45f, .22f) : new Color(.45f, .62f, .32f);
                infoIcon.sprite = Icons.Get(t == Terrain.Water ? "water" : t == Terrain.Forest ? "forest" : "explore");
                AddCard("star", UiKit.Gold, "Atractivo", Signed(desire), desireColor);
                if (t != Terrain.Water)
                {
                    var (power, powerColor) = YesNo(c.PowerCover[i]);
                    AddCard("bolt", UiKit.Warn, "Red eléctrica", power, powerColor);
                }
                if (t == Terrain.Forest) demolish = $"Talar  ·  $ {Catalog.ClearForestCost}";
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
                        AddCard("radius", blue, "Alcance de la red", $"{def.PowerReach} casillas", Color.white);
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
                        AddRow("bolt", UiKit.Warn, "Consumo eléctrico", $"{def.PowerUse} MW", Color.white);
                        if (def.PowerSupply > 0) AddRow("bolt", UiKit.Warn, "Produce", $"{def.PowerSupply} MW", Color.white);
                        if (def.Desirability != 0) AddRow("star", UiKit.Gold, "Atractivo alrededor", Signed(def.Desirability), def.Desirability > 0 ? UiKit.Good : UiKit.Bad);
                        break;
                }
                if (!def.IsRoad && !working) AddRow("warning", UiKit.Bad, "No funciona", !c.Served[a] ? "sin calle conectada" : "sin electricidad", UiKit.Bad);
                game.View.SetSelection(a % c.Width, a / c.Width, def.Width, def.Height);
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
                upkeepValue.text = def.IsRoad ? $"$ {upkeep} /mes por casilla" : $"$ {N(upkeep)} /mes";
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
            toastPanel = Rect("Aviso", safe, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -114), new Vector2(600, 56), new Vector2(.5f, 1));
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
            if (c.Day < City.DaysPerMonth)
            {
                moneySub.text = "Primer mes";
                moneySub.color = UiKit.TextDim;
            }
            else
            {
                moneySub.text = $"{Signed(net)} /mes";
                moneySub.color = net >= 0 ? UiKit.Good : UiKit.Bad;
            }

            if (c.PowerSupply == 0)
            {
                powerValue.text = "0 MW";
                powerSub.text = "Sin centrales";
                powerSub.color = UiKit.Bad;
            }
            else
            {
                float use = c.PowerDemand / (float)c.PowerSupply;
                powerValue.text = $"{N(c.PowerDemand)} / {N(c.PowerSupply)} MW";
                powerSub.text = use > 1 ? "No alcanza" : $"Uso {Mathf.RoundToInt(use * 100)} %";
                powerSub.color = use > 1 ? UiKit.Bad : use > .85f ? UiKit.Warn : UiKit.Good;
            }

            SetDemand(0, c.DemandResidential);
            SetDemand(1, c.DemandCommercial);
            SetDemand(2, c.DemandIndustrial);

            dateText.text = $"{c.Day % City.DaysPerMonth + 1} {MonthNames[c.Month]}  ·  Año {c.Year}";
            dayText.text = $"Día {N(c.Day + 1)}";

            RefreshGoals(c);
            RefreshAlerts(c);
            if (popup != Popup.None) FillPopup();
            if (selected.x >= 0) RefreshInfo();
            if (Time.unscaledTime >= mapRefreshAt)
            {
                mapRefreshAt = Time.unscaledTime + 1;
                RefreshMinimap();
            }
        }

        void SetDemand(int k, float demand)
        {
            var bar = demandFill[k];
            bar.sizeDelta = new Vector2(12, Mathf.Max(2, Mathf.Abs(demand) * 19));
            bar.localScale = new Vector3(1, demand >= 0 ? 1 : -1, 1);
        }

        public void SetSpeedHighlight(int s)
        {
            for (int i = 0; i < speedButtons.Length; i++)
            {
                speedButtons[i].Back.color = i == s ? UiKit.Accent : Color.clear;
                speedButtons[i].Icon.color = i == s ? Color.white : UiKit.TextDim;
            }
        }

        /// <summary>Opens a pop-up or the building menu of a category; used by the screenshot mode.</summary>
        public void Showcase(string what)
        {
            if (what == "finanzas") TogglePopup(Popup.Finance);
            foreach (var item in Tools.Bar) if (item.Name == what) PickItem(item);
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
