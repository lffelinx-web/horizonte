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
            City = HasArg("-vitrina") ? Demo.Showcase() : demo ? Demo.Build() : Load() ?? CityGenerator.Create();
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
            Rig.Distance = 190;
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
            View.Sync(clock);
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
            Hud.HideInfo();
            SelectTool(Tool.Explore);
            Hud.Refresh();
        }

        public void Inspect(int x, int y) => Hud.Select(x, y);

        public void Demolish(int x, int y)
        {
            if (!City.Bulldoze(x, y))
            {
                Hud.Toast("No alcanza el dinero para demoler");
                return;
            }
            Hud.HideInfo();
            Hud.Refresh();
        }

        // ---------------------------------------------------------------- saving

        void OnApplicationPause(bool paused)
        {
            if (paused) Save();
        }

        void OnApplicationQuit() => Save();

        public void Save()
        {
            if (City == null || HasArg("-demo") || HasArg("-vitrina")) return;
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

        IEnumerator CloseUp(string folder, string file, Func<int, bool> pick, float distance)
        {
            for (int i = 0; i < City.Building.Length; i++)
            {
                if (City.Anchor[i] != i || !pick(i)) continue;
                int w = City.DefAt(i).Width, h = City.DefAt(i).Height;
                Rig.Focus = new Vector3((i % City.Width + w * .5f) * CityView.TileSize, 0, (i / City.Width + h * .5f) * CityView.TileSize);
                Rig.Yaw = 0;
                Rig.Distance = distance;
                for (int f = 0; f < 10; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, file));
                yield return null;
                yield return null;
                yield break;
            }
        }

        /// <summary>Saves screenshots from a few angles and quits. Used to check the look without a device.</summary>
        IEnumerator Capture(string folder)
        {
            Directory.CreateDirectory(folder);
            SetSpeed(0);
            if (HasArg("-vitrina"))
            {
                int shot = 0;
                for (int i = 0; i < City.Building.Length; i++)
                {
                    var def = City.DefAt(i);
                    if (City.Anchor[i] != i || def == null || def.IsRoad) continue;
                    float size = Mathf.Max(def.Width, def.Height) * CityView.TileSize;
                    yield return CloseUp(folder, $"vitrina_{++shot:00}.png", j => j == i, size * 1.6f + 30);
                }
                Application.Quit();
                yield break;
            }
            var shots = new[] { (yaw: 30f, dist: 190f), (yaw: 120f, dist: 90f), (yaw: 210f, dist: 410f) };
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
                // The biggest home in town, with its panel open.
                int best = 0;
                for (int i = 0; i < City.Building.Length; i++)
                {
                    var d = City.DefAt(i);
                    if (d != null && d.Kind == Kind.Housing && City.Anchor[i] == i && (City.DefAt(best) == null || d.Residents > City.DefAt(best).Residents)) best = i;
                }
                Rig.Focus = CityView.TileCentre(best % City.Width, best / City.Width);
                Rig.Yaw = 35;
                Rig.Distance = 140;
                Inspect(best % City.Width, best / City.Width);
                for (int f = 0; f < 10; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_interfaz.png"));
                yield return null;
                yield return null;

                // A building going up, a third of the way through its works.
                City.IgnoreRequirements = true;
                var tower = Catalog.Get("torre_corporativa");
                int site = -1;
                for (int y = City.Height / 2 - 14; y < City.Height / 2 + 14 && site < 0; y++)
                for (int x = City.Width / 2 - 22; x < City.Width / 2 + 6 && site < 0; x++)
                    if (City.CanPlace(tower.Id, x, y) == PlaceResult.Ok) site = City.Index(x, y);
                if (site >= 0)
                {
                    City.Place(tower.Id, site % City.Width, site / City.Width);
                    for (int d = 0; d < Catalog.BuildDays(tower) / 2; d++) City.Step();
                    Hud.HideInfo();
                    Rig.Focus = new Vector3((site % City.Width + tower.Width * .5f) * CityView.TileSize, 0, (site / City.Width + tower.Height * .5f) * CityView.TileSize);
                    Rig.Yaw = 40;
                    Rig.Distance = 230;
                    yield return new WaitForSecondsRealtime(4);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_obra.png"));
                    yield return null;
                    yield return null;
                }
                City.IgnoreRequirements = false;

                // Close-ups to check that models face the street (rotation 0 faces south, toward a camera at yaw 0).
                Hud.HideInfo();
                yield return CloseUp(folder, "captura_casa.png", i => City.DefAt(i)?.Model == "casa_ampliada" && City.Rotation[i] == 0, 45);
                yield return CloseUp(folder, "captura_edificios.png", i => City.DefAt(i)?.Model == "apartamentos", 110);
                yield return CloseUp(folder, "captura_servicio.png", i => City.DefAt(i)?.Model == "escuela_primaria", 90);

                // Pop-up, building menu and a message.
                SelectTool(Tool.Explore);
                Hud.Showcase("finanzas");
                Hud.Showcase("Parques");
                Hud.Toast("Ejemplo de aviso");
                for (int f = 0; f < 10; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_interfaz_2.png"));
                yield return null;
                yield return null;

                // The grid that shows up while building.
                Hud.Showcase("Parques");
                SelectTool(Tool.Build("casa_basica"));
                Rig.Focus = CityView.TileCentre(City.Width / 2 - 12, City.Height / 2 + 12);
                Rig.Yaw = 20;
                Rig.Distance = 170;
                for (int f = 0; f < 10; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_cuadricula.png"));
                yield return null;
                yield return null;
            }
            Application.Quit();
        }
    }
}
