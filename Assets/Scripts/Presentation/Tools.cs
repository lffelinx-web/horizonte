using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    public enum Tool { Explore, Road, Residential, Commercial, Industrial, PowerPlant, Park, Police, School, Doctor, Lab, Clinic, Emergency, Hospital, SpecialistHospital, Bulldoze }

    public static class Tools
    {
        public static readonly Tool[] All =
        {
            Tool.Explore, Tool.Road, Tool.Residential, Tool.Commercial, Tool.Industrial,
            Tool.PowerPlant, Tool.Park, Tool.Police, Tool.School,
            Tool.Doctor, Tool.Lab, Tool.Clinic, Tool.Emergency, Tool.Hospital, Tool.SpecialistHospital, Tool.Bulldoze,
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
                case Tool.Doctor: return Structure.Doctor;
                case Tool.Lab: return Structure.Lab;
                case Tool.Clinic: return Structure.Clinic;
                case Tool.Emergency: return Structure.Emergency;
                case Tool.Hospital: return Structure.Hospital;
                case Tool.SpecialistHospital: return Structure.SpecialistHospital;
                default: return Structure.None;
            }
        }

        public static string Label(Tool t) =>
            t == Tool.Explore ? "Explorar" : t == Tool.Bulldoze ? "Demoler" : Catalog.Get(ToStructure(t)).Name;

        public static string CostLabel(Tool t)
        {
            if (t == Tool.Explore) return "";
            if (t == Tool.Bulldoze) return $"$ {Catalog.BulldozeCost} por casilla";
            var def = Catalog.Get(ToStructure(t));
            return def.Type == Structure.Road || def.IsZone ? $"$ {def.Cost} por casilla" : $"$ {def.Cost:N0}";
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
                case Tool.Doctor:
                case Tool.Lab:
                case Tool.Clinic:
                case Tool.Emergency:
                case Tool.Hospital:
                case Tool.SpecialistHospital: return new Color(.86f, .32f, .34f);
                case Tool.Bulldoze: return new Color(.92f, .35f, .3f);
                default: return new Color(.85f, .85f, .85f);
            }
        }

        /// <summary>A button on the bottom bar; categories with several tools open a small menu.</summary>
        public sealed class Category
        {
            public string Name, Icon;
            public Tool[] Tools;
        }

        public static readonly Category[] Categories =
        {
            new Category { Name = "Explorar", Icon = "explore", Tools = new[] { Tool.Explore } },
            new Category { Name = "Calles", Icon = "road", Tools = new[] { Tool.Road } },
            new Category { Name = "Vivienda", Icon = "house", Tools = new[] { Tool.Residential } },
            new Category { Name = "Comercio", Icon = "shop", Tools = new[] { Tool.Commercial } },
            new Category { Name = "Industria", Icon = "factory", Tools = new[] { Tool.Industrial } },
            new Category { Name = "Energía", Icon = "bolt", Tools = new[] { Tool.PowerPlant } },
            new Category { Name = "Servicios", Icon = "shield", Tools = new[] { Tool.Police, Tool.School } },
            new Category { Name = "Salud", Icon = "cross", Tools = new[] { Tool.Doctor, Tool.Lab, Tool.Clinic, Tool.Emergency, Tool.Hospital, Tool.SpecialistHospital } },
            new Category { Name = "Parques", Icon = "tree", Tools = new[] { Tool.Park } },
            new Category { Name = "Demoler", Icon = "bulldoze", Tools = new[] { Tool.Bulldoze } },
        };

        public static string Icon(Tool t)
        {
            switch (t)
            {
                case Tool.Road: return "road";
                case Tool.Residential: return "house";
                case Tool.Commercial: return "shop";
                case Tool.Industrial: return "factory";
                case Tool.PowerPlant: return "bolt";
                case Tool.Park: return "tree";
                case Tool.Police: return "shield";
                case Tool.School: return "school";
                case Tool.Lab: return "flask";
                case Tool.Doctor:
                case Tool.Clinic:
                case Tool.Emergency:
                case Tool.Hospital:
                case Tool.SpecialistHospital: return "cross";
                case Tool.Bulldoze: return "bulldoze";
                default: return "explore";
            }
        }

        /// <summary>One line on how the tool is used, shown above the bar while it is selected.</summary>
        public static string Hint(Tool t)
        {
            switch (ShapeOf(t))
            {
                case Shape.Line: return $"{Label(t)}  ·  {CostLabel(t)}  ·  arrastra para trazar";
                case Shape.Rect: return t == Tool.Bulldoze ? $"Demoler  ·  {CostLabel(t)}  ·  arrastra sobre lo que quieras quitar" : $"Zona {Label(t).ToLowerInvariant()}  ·  {CostLabel(t)}  ·  arrastra para marcar un área";
                case Shape.Single: return $"{Label(t)}  ·  {CostLabel(t)}  ·  toca donde quieras construir";
                default: return null;
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
