using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    public enum Tool { Explore, Road, Residential, Commercial, Industrial, PowerPlant, Park, Police, School, Bulldoze }

    public static class Tools
    {
        public static readonly Tool[] All =
        {
            Tool.Explore, Tool.Road, Tool.Residential, Tool.Commercial, Tool.Industrial,
            Tool.PowerPlant, Tool.Park, Tool.Police, Tool.School, Tool.Bulldoze,
        };

        public static Structure ToStructure(Tool t)
        {
            switch (t)
            {
                case Tool.Road: return Structure.Road;
                case Tool.Residential: return Structure.Residential;
                case Tool.Commercial: return Structure.Commercial;
                case Tool.Industrial: return Structure.Industrial;
                case Tool.PowerPlant: return Structure.PowerPlant;
                case Tool.Park: return Structure.Park;
                case Tool.Police: return Structure.Police;
                case Tool.School: return Structure.School;
                default: return Structure.None;
            }
        }

        public static string Label(Tool t) =>
            t == Tool.Explore ? "Explorar" : t == Tool.Bulldoze ? "Demoler" : Catalog.Get(ToStructure(t)).Name;

        public static string CostLabel(Tool t)
        {
            if (t == Tool.Explore) return "";
            if (t == Tool.Bulldoze) return $"${Catalog.BulldozeCost}";
            var def = Catalog.Get(ToStructure(t));
            return def.Type == Structure.Road || def.IsZone ? $"${def.Cost}/casilla" : $"${def.Cost:N0}";
        }

        public static Color Swatch(Tool t)
        {
            switch (t)
            {
                case Tool.Road: return new Color(.35f, .36f, .4f);
                case Tool.Residential: return new Color(.45f, .78f, .38f);
                case Tool.Commercial: return new Color(.36f, .6f, .9f);
                case Tool.Industrial: return new Color(.92f, .74f, .27f);
                case Tool.PowerPlant: return new Color(.98f, .86f, .2f);
                case Tool.Park: return new Color(.25f, .6f, .3f);
                case Tool.Police: return new Color(.3f, .43f, .75f);
                case Tool.School: return new Color(.82f, .45f, .25f);
                case Tool.Bulldoze: return new Color(.92f, .35f, .3f);
                default: return new Color(.85f, .85f, .85f);
            }
        }

        /// <summary>Drag shape: roads follow an L path, zones and demolition fill a rectangle, buildings are placed one at a time.</summary>
        public enum Shape { None, Line, Rect, Single }

        public static Shape ShapeOf(Tool t)
        {
            if (t == Tool.Explore) return Shape.None;
            if (t == Tool.Road) return Shape.Line;
            if (t == Tool.Bulldoze || Catalog.Get(ToStructure(t)).IsZone) return Shape.Rect;
            return Shape.Single;
        }
    }
}
