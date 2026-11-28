using System;
using System.Collections;
using System.IO;
using Horizonte.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Horizonte.Presentation
{
    /// <summary>Entry point: owns the city, runs the clock, saves, and connects the views with the input.</summary>
    public sealed class GameController : MonoBehaviour
    {
        /// <summary>Ticks (five game minutes) per real second at each speed: paused, normal (a day in eight minutes), fast.</summary>
        static readonly float[] TicksPerSecond = { 0, City.TicksPerDay / 480f, City.TicksPerDay / 160f };
        /// <summary>How fast street life moves at each speed.</summary>
        static readonly float[] StreetSpeed = { 0, 1, 3 };

        public City City { get; private set; }
        public CityView View { get; private set; }
        public CameraRig Rig { get; private set; }
        public Hud Hud { get; private set; }
        public PlacementController Placement { get; private set; }
        public Traffic Traffic { get; private set; }
        public DayNight DayNight { get; private set; }
        public Lumber Lumber { get; private set; }
        public Signals Signals { get; private set; }
        public Sound Sound { get; private set; }

        /// <summary>Time of day from 0 (midnight) to 1, moving smoothly between ticks.</summary>
        public float TimeOfDay => City == null ? .3f : (City.Tick % City.TicksPerDay + clock) / City.TicksPerDay;

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
            Traffic = new GameObject("Gente").AddComponent<Traffic>();
            DayNight = gameObject.AddComponent<DayNight>();
            Lumber = new GameObject("Leñadores").AddComponent<Lumber>();
            Signals = new GameObject("Semáforos").AddComponent<Signals>();
            Sound = gameObject.AddComponent<Sound>();
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            Hud = gameObject.AddComponent<Hud>();
            Placement = gameObject.AddComponent<PlacementController>();

            TryLook(ArgValue("-look"));
            if (HasArg("-sinniebla")) RenderSettings.fog = false;
            Relief.DumpFolder = ArgValue("-captura");
            if (Relief.DumpFolder != null) Directory.CreateDirectory(Relief.DumpFolder);
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
            City.HourPassed += Save;
            View.Init(City);
            Traffic.Init(City, Rig, View);
            Lumber.Init(City, View, Traffic);
            Signals.Init(City);
            Sound.Init(Rig);
            Rig.Init(cam, new Vector2(City.Width, City.Height) * CityView.TileSize);
            // Start at the entrance, looking west: the highway out of the tunnel into the clearing, forest all around.
            Rig.Focus = CityView.TileCentre(1, City.Height / 2) + new Vector3(-6, 0, 0);
            Rig.Yaw = 285;
            Rig.Distance = 120;
            UpdateReach();
        }

        /// <summary>The camera may zoom out further as the city grows: about 320 m for a village, up to 650 m for a city.</summary>
        void UpdateReach() => Rig.Reach = Mathf.Clamp(320 + Mathf.Sqrt(City.Population) * 12, 320, CameraRig.MaxDistance);

        void Update()
        {
            UpdateReach();
            clock += Time.unscaledDeltaTime * TicksPerSecond[speed];
            // Never fall more than a few ticks behind (for example after the app was in the background).
            clock = Mathf.Min(clock, 4);
            while (clock >= 1)
            {
                clock -= 1;
                City.Step();
            }
            View.Sync(clock);
            DayNight.Apply(TimeOfDay, Rig);
            Traffic.Tick(StreetSpeed[speed], TimeOfDay);
            Lumber.Tick(StreetSpeed[speed]);
            Signals.Tick(StreetSpeed[speed]);
            Sound.Tick(TimeOfDay, StreetSpeed[speed]);
        }

        public void SelectTool(Tool t)
        {
            Placement.SetTool(t);
            Hud.SelectTool(t);
        }

        public void SetSpeed(int s)
        {
            speed = Mathf.Clamp(s, 0, TicksPerSecond.Length - 1);
            Hud.SetSpeedHighlight(speed);
        }

        public void ChangeTax(int delta)
        {
            City.TaxRate = Mathf.Clamp(City.TaxRate + delta, 0, 20);
            Hud.Refresh();
        }

        public void NewCity()
        {
            City.HourPassed -= Save;
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

        /// <summary>
        /// Review aid (command line: -look suave|viva|intensa): the same city with gentler or stronger contrast and colour,
        /// to compare captures before a look is fixed in ProjectSetup.
        /// </summary>
        static void TryLook(string look)
        {
            if (string.IsNullOrEmpty(look)) return;
            float contrast, saturation, sun, shade;
            switch (look)
            {
                case "suave": contrast = 20; saturation = 25; sun = 1.9f; shade = 1.05f; break;
                case "intensa": contrast = 32; saturation = 45; sun = 2.3f; shade = .86f; break;
                default: contrast = 26; saturation = 35; sun = 2.1f; shade = .93f; break;
            }
            var volume = FindAnyObjectByType<Volume>();
            if (volume != null && volume.sharedProfile.TryGet(out ColorAdjustments colour))
            {
                colour.contrast.Override(contrast);
                colour.saturation.Override(saturation);
            }
            if (RenderSettings.sun != null) RenderSettings.sun.intensity = sun;
            RenderSettings.ambientSkyColor = new Color(.56f, .64f, .80f) * shade;
            RenderSettings.ambientEquatorColor = new Color(.5f, .53f, .5f) * shade;
            RenderSettings.ambientGroundColor = new Color(.31f, .29f, .25f) * shade;
        }

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
                var (w, h) = City.Size(i);
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

        /// <summary>People and the car of a house with a garage, in the morning and in the afternoon.</summary>
        IEnumerator StreetLife(string folder)
        {
            int house = -1;
            for (int i = 0; i < City.Building.Length && house < 0; i++)
                if (City.Anchor[i] == i && City.DefAt(i)?.Model == "casa_ampliada" && City.Working(i) && City.Occupants[i] > 0) house = i;
            if (house < 0) yield break;
            var (w, h) = City.Size(house);
            Rig.Focus = new Vector3((house % City.Width + w * .5f) * CityView.TileSize, 0, (house / City.Width + h * .5f) * CityView.TileSize);
            Rig.Yaw = 20 + City.Rotation[house] * 90;
            Rig.Distance = 75;
            // At triple speed an hour passes in under seven seconds.
            SetSpeed(2);
            var shots = new[] { (2f, 6f), (8f, -1f), (8f, -1f), (6f, 16.5f), (14f, -1f), (12f, -1f) };
            for (int k = 0; k < shots.Length; k++)
            {
                if (shots[k].Item2 >= 0) SetHour(shots[k].Item2);
                yield return new WaitForSecondsRealtime(shots[k].Item1);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"captura_gente_{k + 1}.png"));
                Debug.Log($"Horizonte: captura de gente {k + 1} a la hora {TimeOfDay * 24:0.0}");
                yield return null;
                yield return null;
            }
            SetSpeed(0);
        }

        /// <summary>Jumps the clock to an hour of the current day (screenshots).</summary>
        public void SetHour(float hour)
        {
            City.Tick = City.Day * City.TicksPerDay + Mathf.RoundToInt(hour * City.TicksPerHour);
            clock = 0;
        }

        /// <summary>The land around the town, the forest reserve at work, and the city at dusk and at night.</summary>
        IEnumerator Territory(string folder)
        {
            IEnumerator Shot(string file, Vector3 focus, float yaw, float distance, float wait = 1)
            {
                Rig.Focus = focus;
                Rig.Yaw = yaw;
                Rig.Distance = distance;
                yield return new WaitForSecondsRealtime(wait);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, file));
                yield return null;
                yield return null;
            }
            float T = CityView.TileSize;
            var centre = new Vector3(City.Width * .5f * T, 0, City.Height * .5f * T);
            SetHour(11);
            yield return Shot("captura_territorio.png", centre, 25, 640, 2);
            yield return Shot("captura_montanas.png", new Vector3(City.Width * .22f * T, 0, City.Height * .7f * T), 315, 420);
            yield return Shot("captura_montanas_cerca.png", new Vector3(City.Width * .2f * T, 0, City.Height * .78f * T), 300, 230);
            yield return Shot("captura_montanas_baja.png", new Vector3(City.Width * .3f * T, 0, City.Height * .68f * T), 315, 110);
            yield return Shot("captura_rio.png", new Vector3(City.Width * .66f * T, 0, City.Height * .5f * T), 180, 260);

            // The reserve: the lumberjack fells the trees marked by the demo and carries the logs back.
            // The logging camp by the entrance, or the gate of the reserve.
            int camp = System.Array.FindIndex(City.Building, b => b == Catalog.Get("puesto_tala").Id);
            var gate = camp >= 0 ? CityView.TileCentre(camp % City.Width, camp / City.Width) - new Vector3(0, 0, 25) : new Vector3((City.GateX + .5f) * T, 0, City.ReserveY0 * T);
            SetSpeed(2);
            yield return Shot("captura_tala_1.png", gate + new Vector3(0, 0, 25), 20, 95, 6);
            yield return Shot("captura_tala_2.png", gate + new Vector3(0, 0, 25), 20, 95, 10);
            yield return Shot("captura_tala_3.png", gate + new Vector3(0, 0, 25), 20, 95, 14);
            Debug.Log($"Horizonte: madera tras la tala {City.Wood}, árboles marcados {City.Marked.Count}");
            SetSpeed(0);

            SetHour(7);
            yield return Shot("captura_amanecer.png", centre + new Vector3(-90, 0, 0), 60, 260, 2);
            SetHour(18.8f);
            yield return Shot("captura_atardecer.png", centre + new Vector3(-90, 0, 0), 230, 260, 2);
            SetHour(22.5f);
            yield return Shot("captura_noche.png", centre + new Vector3(-90, 0, 0), 30, 200, 2);
            yield return Shot("captura_noche_cerca.png", centre + new Vector3(-110, 0, 0), 120, 90, 1);
            SetHour(10.5f);
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
                    var (sw, sh) = City.Size(i);
                    float size = Mathf.Max(sw, sh) * CityView.TileSize;
                    yield return CloseUp(folder, $"vitrina_{++shot:00}.png", j => j == i, size * 1.6f + 30);
                }
                Application.Quit();
                yield break;
            }
            SetHour(10.5f);
            if (HasArg("-inicio"))
            {
                // What a new game shows: the entrance as it opens, and zoomed out as far as a village may.
                for (int f = 0; f < 20; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_inicio.png"));
                yield return null;
                Rig.Zoom(10);
                for (int f = 0; f < 20; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_inicio_lejos.png"));
                yield return null;
                yield return null;
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
            if (HasArg("-territorio")) yield return Territory(folder);
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

                // A building going up: the lot, the frame rising, and the finished building inside the scaffold.
                City.IgnoreRequirements = true;
                var tower = Catalog.Get("torre_corporativa");
                int site = -1;
                for (int y = City.Height / 2 - 14; y < City.Height / 2 + 14 && site < 0; y++)
                for (int x = City.Width / 2 - 22; x < City.Width / 2 + 6 && site < 0; x++)
                    if (City.CanPlace(tower.Id, x, y) == PlaceResult.Ok) site = City.Index(x, y);
                if (site >= 0)
                {
                    City.Place(tower.Id, site % City.Width, site / City.Width);
                    Hud.HideInfo();
                    Rig.Focus = new Vector3((site % City.Width + tower.Width * .5f) * CityView.TileSize, 0, (site / City.Width + tower.Height * .5f) * CityView.TileSize);
                    Rig.Yaw = 40;
                    Rig.Distance = 150;
                    int total = Catalog.BuildTicks(tower), done = 0;
                    var stages = new[] { .06f, .45f, .9f };
                    for (int k = 0; k < stages.Length; k++)
                    {
                        for (; done < total * stages[k]; done++) City.Step();
                        yield return new WaitForSecondsRealtime(k == 0 ? 2 : 6);
                        ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"captura_obra_{k + 1}.png"));
                        yield return null;
                        yield return null;
                    }
                }
                City.IgnoreRequirements = false;

                // Close-ups to check that models face the street (rotation 0 faces south, toward a camera at yaw 0).
                Hud.HideInfo();
                yield return CloseUp(folder, "captura_casa.png", i => City.DefAt(i)?.Model == "casa_ampliada" && City.Rotation[i] == 0, 45);
                foreach (var model in new[] { "consultorio", "clinica", "urgencias", "hospital_general", "laboratorio" })
                    yield return CloseUp(folder, $"captura_{model}.png", i => City.DefAt(i)?.Model == model && City.Anchor[i] == i, 75);
                yield return StreetLife(folder);
                // A junction up close: rounded corners, zebra crossings, people crossing on them, cables to the houses.
                SetHour(8);
                Rig.Focus = CityView.TileCentre(City.Width / 2 - 6, City.Height / 2);
                Rig.Yaw = 35;
                Rig.Distance = 55;
                SetSpeed(2);
                yield return new WaitForSecondsRealtime(8);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_cruce.png"));
                yield return null;
                Rig.Distance = 110;
                yield return new WaitForSecondsRealtime(3);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_cruce_2.png"));
                yield return null;
                SetSpeed(0);

                // The morning arrivals: cars out of the tunnel, into the car park, drivers walking to work.
                int lot = System.Array.FindIndex(City.Building, b => b == Catalog.Get("estacionamiento").Id);
                if (lot >= 0)
                {
                    SetHour(6.2f);
                    Rig.Focus = CityView.TileCentre(lot % City.Width + 1, lot / City.Width + 1);
                    Rig.Yaw = 30;
                    Rig.Distance = 120;
                    SetSpeed(2);
                    yield return new WaitForSecondsRealtime(22);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_estacionamiento.png"));
                    yield return null;
                    Rig.Focus = CityView.TileCentre(2, City.Height / 2);
                    Rig.Yaw = 285;
                    Rig.Distance = 110;
                    yield return new WaitForSecondsRealtime(3);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_tunel.png"));
                    yield return null;
                    SetSpeed(0);
                }
                yield return CloseUp(folder, "captura_edificios.png", i => City.DefAt(i)?.Model == "apartamentos", 110);
                yield return CloseUp(folder, "captura_servicio.png", i => City.DefAt(i)?.Model == "escuela_primaria", 90);

                // Information layers over the whole town, and the warning icons without them.
                Rig.Focus = CityView.TileCentre(City.Width / 2 - 9, City.Height / 2);
                Rig.Yaw = 30;
                Rig.Distance = 230;
                foreach (var (l, file) in new[] { (CityView.Layer.None, "captura_avisos"), (CityView.Layer.Power, "captura_capa_luz"), (CityView.Layer.Water, "captura_capa_agua"), (CityView.Layer.Services, "captura_capa_servicios") })
                {
                    View.ShowLayer(l);
                    yield return new WaitForSecondsRealtime(1.5f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, file + ".png"));
                    yield return null;
                    yield return null;
                }
                View.ShowLayer(CityView.Layer.None);

                // Pop-up, building menu and a message.
                SelectTool(Tool.Explore);
                Hud.Showcase("finanzas");
                for (int f = 0; f < 10; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "captura_finanzas.png"));
                yield return null;
                yield return null;
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
