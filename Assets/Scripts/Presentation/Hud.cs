using System;
using System.Collections.Generic;
using Horizonte.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Horizonte.Presentation
{
    /// <summary>On-screen interface, built in code: city stats, demand, speed, taxes and the tool bar.</summary>
    public sealed class Hud : MonoBehaviour
    {
        static readonly string[] MonthNames = { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
        static readonly Color PanelColor = new Color(.08f, .1f, .13f, .78f);
        static readonly Color ButtonColor = new Color(.16f, .19f, .24f, .92f);
        static readonly Color SelectedColor = new Color(.95f, .78f, .3f, 1);

        GameController game;
        Font font;
        RectTransform safe;
        Text money, delta, people, date, power, tax, cost, toast, info;
        Image toastBack, infoBack;
        RectTransform demandR, demandC, demandI;
        readonly Dictionary<Tool, Image> toolButtons = new Dictionary<Tool, Image>();
        readonly Image[] speedButtons = new Image[3];
        float toastUntil, refreshAt, resetArmedUntil;
        Rect lastSafeArea;

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
            BuildTopLeft();
            BuildDemand();
            BuildTopRight();
            BuildToolbar();

            cost = Label(Rect("Costo", safe, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 150), new Vector2(900, 50)), 30, TextAnchor.MiddleCenter);
            cost.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .8f);

            var toastRect = Rect("Aviso", safe, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -200), new Vector2(760, 64));
            toastBack = Panel(toastRect, new Color(.55f, .14f, .12f, .9f));
            toast = Label(Rect("Texto", toastRect, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-24, 0)), 30, TextAnchor.MiddleCenter);
            toastBack.raycastTarget = false;
            toastBack.gameObject.SetActive(false);
            toast.raycastTarget = false;

            var infoRect = Rect("Información", safe, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-250, 40), new Vector2(460, 300));
            infoBack = Panel(infoRect, PanelColor);
            info = Label(Rect("Texto", infoRect, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-40, -30)), 27, TextAnchor.UpperLeft);
            infoBack.gameObject.SetActive(false);

            SelectTool(Tool.Explore);
            SetSpeedHighlight(1);
            Refresh();
        }

        // ---------------------------------------------------------------- layout

        void BuildTopLeft()
        {
            var r = Rect("Finanzas", safe, new Vector2(0, 1), new Vector2(0, 1), new Vector2(290, -88), new Vector2(560, 160));
            Panel(r, PanelColor);
            money = Label(Rect("Dinero", r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -36), new Vector2(-36, 56)), 44, TextAnchor.MiddleLeft);
            money.fontStyle = FontStyle.Bold;
            delta = Label(Rect("Balance", r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -78), new Vector2(-36, 32)), 24, TextAnchor.MiddleLeft);
            people = Label(Rect("Población", r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -110), new Vector2(-36, 32)), 26, TextAnchor.MiddleLeft);
            date = Label(Rect("Fecha", r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -140), new Vector2(-36, 30)), 24, TextAnchor.MiddleLeft);
            date.color = new Color(.8f, .85f, .9f);
        }

        void BuildDemand()
        {
            var r = Rect("Demanda", safe, new Vector2(0, 1), new Vector2(0, 1), new Vector2(660, -88), new Vector2(150, 160));
            Panel(r, PanelColor);
            Label(Rect("Título", r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -20), new Vector2(0, 30)), 20, TextAnchor.MiddleCenter).text = "DEMANDA";
            demandR = DemandBar(r, -40, Tools.Swatch(Tool.Residential), "R");
            demandC = DemandBar(r, 0, Tools.Swatch(Tool.Commercial), "C");
            demandI = DemandBar(r, 40, Tools.Swatch(Tool.Industrial), "I");
        }

        RectTransform DemandBar(RectTransform parent, float x, Color color, string letter)
        {
            // Axis in the middle of the panel; bars grow up for demand and down for surplus.
            var axis = Rect("Eje " + letter, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(x, -8), new Vector2(26, 2));
            Panel(axis, new Color(1, 1, 1, .4f));
            var bar = Rect("Barra " + letter, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(x, -8), new Vector2(24, 0));
            bar.pivot = new Vector2(.5f, 0);
            Panel(bar, color);
            Label(Rect("Letra " + letter, parent, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(x, 16), new Vector2(30, 26)), 20, TextAnchor.MiddleCenter).text = letter;
            return bar;
        }

        void BuildTopRight()
        {
            var r = Rect("Controles", safe, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-280, -88), new Vector2(520, 160));
            Panel(r, PanelColor);
            string[] speeds = { "II", "1x", "3x" };
            for (int i = 0; i < 3; i++)
            {
                int s = i;
                speedButtons[i] = Button(r, new Vector2(.5f, 1), new Vector2(-170 + i * 92, -40), new Vector2(84, 58), speeds[i], 28, () => SetSpeed(s));
            }
            Button(r, new Vector2(.5f, 1), new Vector2(170, -40), new Vector2(150, 58), "Nueva", 26, NewCity);
            tax = Label(Rect("Impuesto", r, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-110, -104), new Vector2(240, 40)), 26, TextAnchor.MiddleLeft);
            Button(r, new Vector2(.5f, 1), new Vector2(40, -104), new Vector2(60, 48), "-", 32, () => game.ChangeTax(-1));
            Button(r, new Vector2(.5f, 1), new Vector2(108, -104), new Vector2(60, 48), "+", 32, () => game.ChangeTax(1));
            power = Label(Rect("Energía", r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -142), new Vector2(-40, 30)), 22, TextAnchor.MiddleLeft);
        }

        void BuildToolbar()
        {
            var tools = Tools.All;
            const float w = 156, gap = 8, h = 112;
            float total = tools.Length * w + (tools.Length - 1) * gap;
            var bar = Rect("Herramientas", safe, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, h * .5f + 14), new Vector2(total + 24, h + 20));
            Panel(bar, PanelColor);
            for (int i = 0; i < tools.Length; i++)
            {
                var tool = tools[i];
                float x = -total * .5f + w * .5f + i * (w + gap);
                var img = Button(bar, new Vector2(.5f, .5f), new Vector2(x, 0), new Vector2(w, h), "", 24, () => game.SelectTool(tool));
                var rt = img.rectTransform;
                var swatch = Rect("Color", rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -6), new Vector2(-20, 8));
                Panel(swatch, Tools.Swatch(tool)).raycastTarget = false;
                var name = Label(Rect("Nombre", rt, new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, 8), new Vector2(-8, 50)), 23, TextAnchor.MiddleCenter);
                name.text = Tools.Label(tool);
                name.fontStyle = FontStyle.Bold;
                var price = Label(Rect("Precio", rt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 22), new Vector2(-8, 30)), 19, TextAnchor.MiddleCenter);
                price.text = Tools.CostLabel(tool);
                price.color = new Color(.8f, .85f, .9f);
                toolButtons[tool] = img;
            }
        }

        // ---------------------------------------------------------------- updates

        void Update()
        {
            if (game == null) return;
            if (Screen.safeArea != lastSafeArea) ApplySafeArea();
            if (Time.unscaledTime >= refreshAt) Refresh();
            if (toastBack.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBack.gameObject.SetActive(false);
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
            money.text = $"$ {c.Money:N0}";
            money.color = c.Money < 0 ? new Color(1, .45f, .4f) : Color.white;
            int net = c.LastIncome - c.LastExpenses;
            delta.text = c.Day < City.DaysPerMonth ? "Primer mes en curso" : $"{(net >= 0 ? "+" : "")}{net:N0} al mes  ·  ingresos {c.LastIncome:N0}  ·  gastos {c.LastExpenses:N0}";
            delta.color = net >= 0 ? new Color(.6f, .92f, .6f) : new Color(1, .6f, .5f);
            people.text = $"Habitantes {c.Population:N0}   ·   Empleos {c.Jobs:N0}";
            date.text = $"{c.Day % City.DaysPerMonth + 1} {MonthNames[c.Month]} · Año {c.Year}";
            tax.text = $"Impuesto {c.TaxRate}%";
            power.text = c.PowerSupply == 0 ? "Energía: sin centrales" : $"Energía {c.PowerDemand:N0} / {c.PowerSupply:N0}";
            power.color = c.PowerDemand > c.PowerSupply ? new Color(1, .6f, .45f) : new Color(.8f, .85f, .9f);
            SetBar(demandR, c.DemandResidential);
            SetBar(demandC, c.DemandCommercial);
            SetBar(demandI, c.DemandIndustrial);
        }

        static void SetBar(RectTransform bar, float demand)
        {
            const float max = 52;
            float h = Mathf.Abs(demand) * max;
            bar.sizeDelta = new Vector2(24, Mathf.Max(2, h));
            bar.localScale = new Vector3(1, demand >= 0 ? 1 : -1, 1);
        }

        public void SelectTool(Tool t)
        {
            foreach (var kv in toolButtons) kv.Value.color = kv.Key == t ? SelectedColor : ButtonColor;
            foreach (var kv in toolButtons)
                foreach (var text in kv.Value.GetComponentsInChildren<Text>()) text.color = kv.Key == t ? new Color(.1f, .1f, .12f) : (text.name == "Precio" ? new Color(.8f, .85f, .9f) : Color.white);
            HideInfo();
        }

        void SetSpeed(int s)
        {
            game.SetSpeed(s);
            SetSpeedHighlight(s);
        }

        public void SetSpeedHighlight(int s)
        {
            for (int i = 0; i < speedButtons.Length; i++) speedButtons[i].color = i == s ? SelectedColor : ButtonColor;
        }

        void NewCity()
        {
            if (Time.unscaledTime > resetArmedUntil)
            {
                resetArmedUntil = Time.unscaledTime + 3;
                Toast("Toca \"Nueva\" otra vez para empezar una ciudad nueva");
                return;
            }
            resetArmedUntil = 0;
            game.NewCity();
        }

        public void ShowCost(string text)
        {
            cost.text = text ?? "";
        }

        public void Toast(string text)
        {
            toast.text = text;
            toastBack.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + 2.5f;
        }

        public void ShowInfo(string text)
        {
            info.text = text;
            infoBack.gameObject.SetActive(true);
        }

        public void HideInfo()
        {
            if (infoBack != null) infoBack.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- widgets

        static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Image Panel(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        Text Label(RectTransform rt, int size, TextAnchor anchor)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Image Button(RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, string text, int fontSize, Action onClick)
        {
            var rt = Rect("Botón " + text, parent, anchor, anchor, pos, size);
            var img = Panel(rt, ButtonColor);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.pressedColor = new Color(.75f, .75f, .75f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            if (!string.IsNullOrEmpty(text))
            {
                var label = Label(Rect("Texto", rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), fontSize, TextAnchor.MiddleCenter);
                label.text = text;
                label.fontStyle = FontStyle.Bold;
            }
            return img;
        }
    }
}
