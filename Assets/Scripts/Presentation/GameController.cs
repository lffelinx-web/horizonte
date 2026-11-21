using System;
using System.Collections;
using System.IO;
using Horizonte.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Horizonte.Presentation
{
    /// <summary>Entry point: owns the city, runs the clock, saves, and connects the views with the input.</summary>
    public sealed class GameController : MonoBehaviour
    {
        /// <summary>Game days per real second at each speed: paused, normal, fast.</summary>
        static readonly float[] DaysPerSecond = { 0, 1.5f, 6 };

        public City City { get; private set; }
        public CityView View { get; private set; }
        public CameraRig Rig { get; private set; }
        public Hud Hud { get; private set; }
        public PlacementController Placement { get; private set; }

        int speed = 1;
        float clock;

        static string SavePath => Path.Combine(Application.persistentDataPath, "ciudad.json");

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" }.GetComponent<Camera>();
            Rig = gameObject.AddComponent<CameraRig>();
            View = new GameObject("Ciudad").AddComponent<CityView>();
            Hud = gameObject.AddComponent<Hud>();
            Placement = gameObject.AddComponent<PlacementController>();

            bool demo = HasArg("-demo");
            City = demo ? Demo.Build() : Load() ?? CityGenerator.Create();
            Attach(cam);
            Hud.Init(this);
            Placement.Init(this, Rig, Hud);

            string capture = ArgValue("-captura");
            if (capture != null) StartCoroutine(Capture(capture));
        }

        void Attach(Camera cam)
        {
            City.MonthPassed += Save;
            View.Init(City);
            Rig.Init(cam, new Vector2(City.Width, City.Height) * CityView.TileSize);
            Rig.Focus = CityView.TileCentre(City.Width / 2 - 6, City.Height / 2);
            Rig.Distance = 150;
        }

        void Update()
        {
            clock += Time.unscaledDeltaTime * DaysPerSecond[speed];
            // Never fall more than a few days behind (for example after the app was in the background).
            clock = Mathf.Min(clock, 4);
            while (clock >= 1)
            {
                clock -= 1;
                City.Step();
            }
            View.Sync();
        }

        public void SelectTool(Tool t)
        {
            Placement.SetTool(t);
            Hud.SelectTool(t);
        }

        public void SetSpeed(int s)
        {
            speed = Mathf.Clamp(s, 0, DaysPerSecond.Length - 1);
            Hud.SetSpeedHighlight(speed);
        }

        public void ChangeTax(int delta)
        {
            City.TaxRate = Mathf.Clamp(City.TaxRate + delta, 0, 20);
            Hud.Refresh();
        }

        public void NewCity()
        {
            City.MonthPassed -= Save;
            City = CityGenerator.Create(seed: (uint)DateTime.Now.Ticks);
            Attach(Rig.Cam);
            Save();
            SelectTool(Tool.Explore);
            Hud.Refresh();
        }

        public void Inspect(int x, int y)
        {
            if (!City.InBounds(x, y))
            {
                Hud.HideInfo();
                return;
            }
            int i = City.Index(x, y);
            var s = City.Structure[i];
            string text;
            if (s == Structure.None)
            {
                string terrain = City.Terrain[i] == Simulation.Terrain.Water ? "Río" : City.Terrain[i] == Simulation.Terrain.Forest ? "Bosque" : "Terreno libre";
                text = $"<b>{terrain}</b>\nAtractivo {Signed(City.Desirability[i])}";
            }
            else
            {
                var def = Catalog.Get(s);
                int a = City.Anchor[i] >= 0 ? City.Anchor[i] : i;
                int level = City.Level[a];
                text = $"<b>{def.Name}</b>";
                if (def.IsZone)
                {
                    text += level == 0 ? "\nLote vacío, esperando construcción" : $" · nivel {level}";
                    int cap = Catalog.Capacity(s, level);
                    if (level > 0) text += s == Structure.Residential ? $"\n{cap} habitantes" : $"\n{cap} empleos";
                    text += $"\nCalle cercana: {YesNo(City.RoadAccess[i])}";
                }
                if (s != Structure.Road) text += $"\nElectricidad: {YesNo(City.Powered[i])}";
                if (s == Structure.PowerPlant) text += $"\nProduce {def.PowerSupply} de energía";
                if (s == Structure.Road) text += City.RoadConnected[i] ? "\nConectada con la carretera" : "\nSin conexión con la carretera";
                text += $"\nAtractivo {Signed(City.Desirability[i])}";
                if (def.Upkeep > 0) text += $"\nMantenimiento ${def.Upkeep}/mes";
            }
            Hud.ShowInfo(text);
        }

        static string YesNo(bool v) => v ? "sí" : "<color=#FF8A73>no</color>";
        static string Signed(int v) => v > 0 ? $"+{v}" : v.ToString();

        // ---------------------------------------------------------------- saving

        void OnApplicationPause(bool paused)
        {
            if (paused) Save();
        }

        void OnApplicationQuit() => Save();

        public void Save()
        {
            if (City == null || HasArg("-demo")) return;
            try
            {
                File.WriteAllText(SavePath + ".tmp", JsonUtility.ToJson(CitySave.From(City)));
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(SavePath + ".tmp", SavePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Horizonte: could not save the city: {e.Message}");
            }
        }

        static City Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return null;
                return JsonUtility.FromJson<CitySave>(File.ReadAllText(SavePath)).ToCity();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Horizonte: could not load the saved city, starting a new one: {e.Message}");
                return null;
            }
        }

        // ---------------------------------------------------------------- development helpers

        static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Saves screenshots from a few angles and quits. Used to check the look without a device.</summary>
        IEnumerator Capture(string folder)
        {
            Directory.CreateDirectory(folder);
            SetSpeed(0);
            var shots = new[] { (yaw: 30f, dist: 150f), (yaw: 120f, dist: 70f), (yaw: 210f, dist: 330f) };
            for (int k = 0; k < shots.Length; k++)
            {
                Rig.Yaw = shots[k].yaw;
                Rig.Distance = shots[k].dist;
                for (int f = 0; f < 20; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"captura_{k + 1}.png"));
                yield return null;
                yield return null;
            }
            if (HasArg("-inspeccion"))
            {
                SelectTool(Tool.Road);
                Inspect(City.Width / 2 - 4, City.Height / 2 + 1);
                Hud.Toast("Ejemplo de aviso");
                for (int f = 0; f < 10; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_interfaz.png"));
                yield return null;
                yield return null;
            }
            Application.Quit();
        }
    }
}
