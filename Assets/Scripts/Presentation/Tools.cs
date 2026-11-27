using System;
using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>What a finger does on the map: look around, demolish, mark trees to fell, or build one building of the catalog.</summary>
    public readonly struct Tool : IEquatable<Tool>
    {
        enum Mode : byte { Explore, Bulldoze, Build, Fell }

        readonly Mode mode;
        public readonly ushort Building;

        Tool(Mode mode, ushort building)
        {
            this.mode = mode;
            Building = building;
        }

        public static readonly Tool Explore = new Tool(Mode.Explore, 0);
        public static readonly Tool Bulldoze = new Tool(Mode.Bulldoze, 0);
        public static readonly Tool Fell = new Tool(Mode.Fell, 0);
        public static Tool Build(ushort id) => new Tool(Mode.Build, id);
        public static Tool Build(string model) => Build(Catalog.Get(model).Id);

        public bool IsBuild => mode == Mode.Build;
        public bool IsBulldoze => mode == Mode.Bulldoze;
        public bool IsExplore => mode == Mode.Explore;
        public bool IsFell => mode == Mode.Fell;
        public BuildingDef Def => IsBuild ? Catalog.Get(Building) : null;

        public bool Equals(Tool o) => mode == o.mode && Building == o.Building;
        public override bool Equals(object o) => o is Tool t && Equals(t);
        public override int GetHashCode() => ((int)mode << 16) | Building;
        public static bool operator ==(Tool a, Tool b) => a.Equals(b);
        public static bool operator !=(Tool a, Tool b) => !a.Equals(b);
    }

    public static class Tools
    {
        /// <summary>A button on the bottom bar: a tool of its own or a category whose buildings open in a menu.</summary>
        public sealed class BarItem
        {
            public string Name, Icon;
            public Tool? Tool;
            public Category? Category;
        }

        public static readonly BarItem[] Bar =
        {
            new BarItem { Name = "Explorar", Icon = "explore", Tool = Presentation.Tool.Explore },
            new BarItem { Name = "Transporte", Icon = "road", Category = Simulation.Category.Transport },
            new BarItem { Name = "Vivienda", Icon = "house", Category = Simulation.Category.Housing },
            new BarItem { Name = "Comercio", Icon = "shop", Category = Simulation.Category.Commerce },
            new BarItem { Name = "Industria", Icon = "factory", Category = Simulation.Category.Industry },
            new BarItem { Name = "Energía", Icon = "bolt", Category = Simulation.Category.Energy },
            new BarItem { Name = "Agua", Icon = "water", Category = Simulation.Category.Water },
            new BarItem { Name = "Seguridad", Icon = "shield", Category = Simulation.Category.Safety },
            new BarItem { Name = "Educación", Icon = "school", Category = Simulation.Category.Education },
            new BarItem { Name = "Salud", Icon = "cross", Category = Simulation.Category.Health },
            new BarItem { Name = "Parques", Icon = "tree", Category = Simulation.Category.Parks },
            new BarItem { Name = "Talar", Icon = "axe", Tool = Presentation.Tool.Fell },
            new BarItem { Name = "Demoler", Icon = "bulldoze", Tool = Presentation.Tool.Bulldoze },
        };

        public static string CategoryName(Category c)
        {
            switch (c)
            {
                case Category.Transport: return "Transporte y movilidad";
                case Category.Housing: return "Viviendas";
                case Category.Commerce: return "Comercio y oficinas";
                case Category.Industry: return "Industria y producción";
                case Category.Energy: return "Energía";
                case Category.Water: return "Agua, saneamiento y residuos";
                case Category.Safety: return "Seguridad y gobierno";
                case Category.Education: return "Educación e investigación";
                case Category.Health: return "Salud";
                default: return "Parques, cultura y futuro";
            }
        }

        public static string CategoryIcon(Category c)
        {
            foreach (var item in Bar) if (item.Category == c) return item.Icon;
            return "explore";
        }

        public static Color CategoryColor(Category c)
        {
            switch (c)
            {
                case Category.Transport: return new Color(.5f, .55f, .62f);
                case Category.Housing: return new Color(.45f, .74f, .38f);
                case Category.Commerce: return new Color(.36f, .6f, .9f);
                case Category.Industry: return new Color(.92f, .7f, .27f);
                case Category.Energy: return new Color(.96f, .82f, .2f);
                case Category.Water: return new Color(.27f, .66f, .86f);
                case Category.Safety: return new Color(.34f, .45f, .78f);
                case Category.Education: return new Color(.86f, .52f, .26f);
                case Category.Health: return new Color(.86f, .32f, .34f);
                default: return new Color(.3f, .66f, .4f);
            }
        }

        public static string Label(Tool t) => t.IsExplore ? "Explorar" : t.IsBulldoze ? "Demoler" : t.IsFell ? "Talar" : t.Def.Name;

        public static string CostLabel(Tool t)
        {
            if (t.IsExplore) return "";
            if (t.IsFell) return $"{Catalog.WoodPerTree} de madera por árbol";
            if (t.IsBulldoze) return $"$ {Catalog.BulldozeCost} por casilla";
            var def = t.Def;
            return def.IsRoad ? $"$ {Hud.N(def.Cost)} por casilla" : def.Wood > 0 ? $"$ {Hud.N(def.Cost)} + {def.Wood} madera" : $"$ {Hud.N(def.Cost)}";
        }

        public static Color Swatch(Tool t) =>
            t.IsBulldoze ? new Color(.92f, .35f, .3f) : t.IsFell ? new Color(.86f, .66f, .42f) : t.IsExplore ? new Color(.85f, .85f, .85f) : CategoryColor(t.Def.Category);

        public static string Icon(Tool t) => t.IsBulldoze ? "bulldoze" : t.IsFell ? "axe" : t.IsExplore ? "explore" : Icon(t.Def);

        public static string Icon(BuildingDef def)
        {
            if (def.Category == Category.Health && def.Model == "laboratorio") return "flask";
            if (def.Category == Category.Water) return def.Service == Service.Waste ? "trash" : "water";
            return CategoryIcon(def.Category);
        }

        /// <summary>One line on how the tool is used, shown above the bar while it is selected.</summary>
        public static string Hint(Tool t)
        {
            switch (ShapeOf(t))
            {
                case Shape.Line: return $"{Label(t)}  ·  {CostLabel(t)}  ·  arrastra para trazar";
                case Shape.Rect:
                    return t.IsFell
                        ? "Talar  ·  toca árboles de la reserva o arrastra para marcar varios  ·  el leñador de la caseta los corta"
                        : $"Demoler  ·  {CostLabel(t)}  ·  arrastra sobre lo que quieras quitar";
                case Shape.Single:
                    var def = t.Def;
                    string where = def.NeedsWater ? "junto al río y a una calle" : "junto a una calle";
                    return $"{def.Name}  ·  {CostLabel(t)}  ·  {def.Width} × {def.Height}  ·  toca {where}";
                default: return null;
            }
        }

        /// <summary>Drag shape: roads follow an L path, demolition fills a rectangle, buildings are placed one at a time.</summary>
        public enum Shape { None, Line, Rect, Single }

        public static Shape ShapeOf(Tool t)
        {
            if (t.IsExplore) return Shape.None;
            if (t.IsBulldoze || t.IsFell) return Shape.Rect;
            return t.Def.IsRoad ? Shape.Line : Shape.Single;
        }
    }
}
