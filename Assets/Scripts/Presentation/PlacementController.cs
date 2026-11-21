using System.Collections.Generic;
using Horizonte.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Horizonte.Presentation
{
    /// <summary>
    /// Touch and mouse input. One finger builds with the selected tool (or pans in Explore mode); two fingers pan, pinch to zoom
    /// and twist to rotate. In the editor: left button like a finger, right or middle drag pans, wheel zooms, Q/E rotate.
    /// </summary>
    public sealed class PlacementController : MonoBehaviour
    {
        GameController game;
        CameraRig rig;
        Hud hud;

        public Tool Tool { get; private set; } = Tool.Explore;

        bool pressing, dragged, waitForRelease;
        Vector2 pressPos, lastPos;
        Vector2Int startTile, currentTile;

        bool gesturing;
        Vector2 gestureCentre;
        float gestureDistance, gestureAngle;

        MeshFilter previewFilter;
        readonly MeshBuilder preview = new MeshBuilder(3);
        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        readonly List<(int x, int y)> shape = new List<(int x, int y)>();

        public void Init(GameController g, CameraRig r, Hud h)
        {
            game = g;
            rig = r;
            hud = h;
            var go = new GameObject("Vista previa", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            previewFilter = go.GetComponent<MeshFilter>();
            previewFilter.sharedMesh = new Mesh();
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterials = Palette.Get("PreviewOk", "PreviewBad", "PreviewDemolish");
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void OnEnable() => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        public void SetTool(Tool t)
        {
            Tool = t;
            CancelPress();
        }

        void Update()
        {
            if (game == null || game.City == null) return;
            var touches = Touch.activeTouches;
            if (touches.Count >= 2)
            {
                CancelPress();
                waitForRelease = true;
                Gesture(touches[0].screenPosition, touches[1].screenPosition);
                return;
            }
            gesturing = false;
            if (touches.Count == 1)
            {
                var t = touches[0];
                if (waitForRelease)
                {
                    if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled) waitForRelease = false;
                    return;
                }
                bool down = t.phase == UnityEngine.InputSystem.TouchPhase.Began;
                bool up = t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                Pointer(down, up, t.screenPosition);
                return;
            }
            waitForRelease = false;
            if (Touchscreen.current != null && Mouse.current == null) return;
            MouseAndKeyboard();
        }

        void MouseAndKeyboard()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 pos = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame)
                    Pointer(mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame, pos);
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    PanScreen(pos - delta, pos);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > .01f && !OverUi(pos)) rig.Zoom(scroll > 0 ? .88f : 1.14f);
            }
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.qKey.isPressed) rig.Rotate(-90 * Time.unscaledDeltaTime);
            if (kb.eKey.isPressed) rig.Rotate(90 * Time.unscaledDeltaTime);
            var move = Vector2.zero;
            if (kb.wKey.isPressed) move.y += 1;
            if (kb.sKey.isPressed) move.y -= 1;
            if (kb.aKey.isPressed) move.x -= 1;
            if (kb.dKey.isPressed) move.x += 1;
            if (move != Vector2.zero)
            {
                var fwd = Quaternion.Euler(0, rig.Yaw, 0);
                rig.Pan(fwd * new Vector3(move.x, 0, move.y) * rig.Distance * Time.unscaledDeltaTime);
            }
        }

        void Gesture(Vector2 a, Vector2 b)
        {
            Vector2 centre = (a + b) * .5f;
            float distance = Vector2.Distance(a, b);
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            if (gesturing)
            {
                PanScreen(gestureCentre, centre);
                if (distance > 1) rig.Zoom(gestureDistance / distance);
                rig.Rotate(-Mathf.DeltaAngle(gestureAngle, angle));
            }
            gesturing = true;
            gestureCentre = centre;
            gestureDistance = distance;
            gestureAngle = angle;
        }

        void PanScreen(Vector2 from, Vector2 to)
        {
            if (rig.GroundPoint(from, out var a) && rig.GroundPoint(to, out var b)) rig.Pan(a - b);
        }

        void Pointer(bool down, bool up, Vector2 pos)
        {
            if (down)
            {
                if (OverUi(pos)) return;
                pressing = true;
                dragged = false;
                pressPos = lastPos = pos;
                startTile = currentTile = TileAt(pos);
                if (Tool != Tool.Explore) UpdatePreview();
                return;
            }
            if (!pressing) return;

            float slop = Mathf.Max(10, Screen.dpi > 0 ? Screen.dpi * .06f : 10);
            if ((pos - pressPos).sqrMagnitude > slop * slop) dragged = true;
            if (Tool == Tool.Explore)
            {
                if (dragged) PanScreen(lastPos, pos);
            }
            else
            {
                var tile = TileAt(pos);
                if (tile != currentTile)
                {
                    currentTile = tile;
                    UpdatePreview();
                }
            }
            lastPos = pos;

            if (!up) return;
            pressing = false;
            if (Tool == Tool.Explore)
            {
                if (!dragged) game.Inspect(currentTile.x, currentTile.y);
            }
            else Commit();
            ClearPreview();
        }

        void CancelPress()
        {
            pressing = false;
            ClearPreview();
        }

        Vector2Int TileAt(Vector2 screen)
        {
            if (!rig.GroundPoint(screen, out var p)) return new Vector2Int(-1, -1);
            return new Vector2Int(Mathf.FloorToInt(p.x / CityView.TileSize), Mathf.FloorToInt(p.z / CityView.TileSize));
        }

        bool OverUi(Vector2 pos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            uiHits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = pos }, uiHits);
            return uiHits.Count > 0;
        }

        // ---------------------------------------------------------------- shapes and preview

        void BuildShape()
        {
            shape.Clear();
            switch (Tools.ShapeOf(Tool))
            {
                case Tools.Shape.Line: shape.AddRange(City.Line(startTile.x, startTile.y, currentTile.x, currentTile.y)); break;
                case Tools.Shape.Rect: shape.AddRange(City.Rect(startTile.x, startTile.y, currentTile.x, currentTile.y)); break;
                case Tools.Shape.Single:
                    var a = SingleAnchor();
                    shape.Add((a.x, a.y));
                    break;
            }
        }

        /// <summary>Buildings bigger than one tile are centred under the finger.</summary>
        Vector2Int SingleAnchor()
        {
            var def = Catalog.Get(Tools.ToStructure(Tool));
            return new Vector2Int(currentTile.x - (def.Width - 1) / 2, currentTile.y - (def.Height - 1) / 2);
        }

        void UpdatePreview()
        {
            var city = game.City;
            BuildShape();
            preview.Clear();
            long budget = city.Money, total = 0;
            int count = 0;
            var type = Tools.ToStructure(Tool);
            const float lift = .25f, inset = .25f;
            foreach (var (x, y) in shape)
            {
                if (!city.InBounds(x, y)) continue;
                float x0 = x * CityView.TileSize + inset, z0 = y * CityView.TileSize + inset;
                if (Tool == Tool.Bulldoze)
                {
                    int i = city.Index(x, y);
                    bool something = city.Structure[i] != Structure.None || city.Terrain[i] == Simulation.Terrain.Forest;
                    if (!something) continue;
                    preview.Flat(2, x0, z0, x0 + CityView.TileSize - 2 * inset, z0 + CityView.TileSize - 2 * inset, lift);
                    total += city.Structure[i] != Structure.None ? Catalog.BulldozeCost : Catalog.ClearForestCost;
                    count++;
                    continue;
                }
                var def = Catalog.Get(type);
                var result = city.CanPlace(type, x, y);
                int cost = city.PlacementCost(type, x, y);
                bool ok = (result == PlaceResult.Ok || result == PlaceResult.NoMoney) && total + cost <= budget;
                if (ok) { total += cost; count++; }
                float w = def.Width * CityView.TileSize - 2 * inset, d = def.Height * CityView.TileSize - 2 * inset;
                preview.Flat(ok ? 0 : 1, x0, z0, x0 + w, z0 + d, lift);
            }
            previewFilter.sharedMesh = preview.ToMesh(previewFilter.sharedMesh);
            hud.ShowCost(count == 0 ? null : $"{Tools.Label(Tool)} · {count} {(count == 1 ? "casilla" : "casillas")} · ${total:N0}");
        }

        void ClearPreview()
        {
            preview.Clear();
            if (previewFilter != null) previewFilter.sharedMesh = preview.ToMesh(previewFilter.sharedMesh);
            if (hud != null) hud.ShowCost(null);
        }

        void Commit()
        {
            var city = game.City;
            BuildShape();
            if (Tool == Tool.Bulldoze)
            {
                int removed = 0;
                foreach (var (x, y) in shape) if (city.Bulldoze(x, y)) removed++;
                if (removed == 0) hud.Toast("No hay nada que demoler ahí");
                return;
            }
            var type = Tools.ToStructure(Tool);
            int placed = city.PlaceMany(type, shape, out var problem);
            if (problem != PlaceResult.Ok && (placed == 0 || problem == PlaceResult.NoMoney)) hud.Toast(Message(problem));
        }

        static string Message(PlaceResult r)
        {
            switch (r)
            {
                case PlaceResult.NoMoney: return "No alcanza el dinero";
                case PlaceResult.Water: return "No se puede construir sobre el agua";
                case PlaceResult.OutOfBounds: return "Fuera del terreno de la ciudad";
                default: return "Ese lugar ya está ocupado";
            }
        }
    }
}
