using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Horizonte.Presentation
{
    /// <summary>Colours and procedurally drawn sprites for the interface: rounded panels, outlines, glows and icons.</summary>
    public static class UiKit
    {
        public static readonly Color PanelColor = new Color(.05f, .09f, .17f, .9f);
        public static readonly Color PanelInner = new Color(.09f, .15f, .26f, .85f);
        public static readonly Color EdgeColor = new Color(.38f, .64f, 1f, .32f);
        public static readonly Color ButtonColor = new Color(.11f, .18f, .3f, .95f);
        public static readonly Color Accent = new Color(.17f, .5f, .95f, 1);
        public static readonly Color AccentEdge = new Color(.62f, .84f, 1f, .95f);
        public static readonly Color AccentText = new Color(.48f, .76f, 1f);
        public static readonly Color TextDim = new Color(.68f, .77f, .9f);
        public static readonly Color Good = new Color(.42f, .9f, .5f);
        public static readonly Color Warn = new Color(1f, .8f, .3f);
        public static readonly Color Bad = new Color(1f, .47f, .42f);
        public static readonly Color Gold = new Color(1f, .77f, .22f);
        public static readonly Color Divider = new Color(1, 1, 1, .1f);

        static Sprite rounded, outline, glow, circle;

        /// <summary>Rounded rectangle, lighter at the top, for sliced panels and buttons.</summary>
        public static Sprite Rounded => rounded != null ? rounded : rounded = MakeRounded(false);

        /// <summary>Thin rounded border drawn over a panel.</summary>
        public static Sprite Outline => outline != null ? outline : outline = MakeRounded(true);

        /// <summary>Soft halo for highlighted elements.</summary>
        public static Sprite Glow => glow != null ? glow : glow = MakeGlow();

        public static Sprite Circle => circle != null ? circle : circle = Icons.Draw(64, p => p.Circle(.5f, .5f, .46f));

        static Sprite MakeRounded(bool border)
        {
            const int w = 64, h = 128, radius = 24;
            var tex = NewTexture(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = RoundedBox(x + .5f, y + .5f, w, h, radius);
                float a;
                if (border)
                {
                    float band = Mathf.Abs(d + 1.4f) - 1.4f;
                    a = Mathf.Clamp01(.5f - band) * Mathf.Lerp(.55f, 1, y / (float)h);
                    px[y * w + x] = new Color(1, 1, 1, a);
                }
                else
                {
                    a = Mathf.Clamp01(.5f - d);
                    float shade = Mathf.Lerp(.78f, 1, y / (float)h);
                    px[y * w + x] = new Color(shade, shade, shade, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(26, 26, 26, 26));
        }

        static Sprite MakeGlow()
        {
            const int size = 128, inset = 40, radius = 20;
            var tex = NewTexture(size, size);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundedBox(x + .5f - inset, y + .5f - inset, size - 2 * inset, size - 2 * inset, radius);
                float a = d <= 0 ? 1 : Mathf.Pow(1 - Mathf.Clamp01(d / inset), 2.2f);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(62, 62, 62, 62));
        }

        /// <summary>Signed distance from a point to a rounded box covering (0,0)-(w,h).</summary>
        static float RoundedBox(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w * .5f) - (w * .5f - r);
            float qy = Mathf.Abs(y - h * .5f) - (h * .5f - r);
            float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        public static Texture2D NewTexture(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
    }

    /// <summary>
    /// Flat white icons painted from simple shapes (signed distances, antialiased) so the game needs no image files.
    /// Coordinates run from 0 to 1 with y up. Images tint them.
    /// </summary>
    public static class Icons
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string name)
        {
            if (cache.TryGetValue(name, out var s)) return s;
            s = Draw(96, p => Paint(name, p));
            cache[name] = s;
            return s;
        }

        public static Sprite Draw(int size, Action<Painter> paint)
        {
            var p = new Painter(size);
            paint(p);
            return p.ToSprite();
        }

        static void Paint(string name, Painter p)
        {
            switch (name)
            {
                case "city":
                    p.Box(.1f, .1f, .34f, .6f, .03f).Box(.38f, .1f, .62f, .88f, .03f).Box(.66f, .1f, .9f, .48f, .03f);
                    for (int k = 0; k < 4; k++) p.Box(.44f, .26f + k * .15f, .56f, .33f + k * .15f, 0, true);
                    p.Box(.16f, .4f, .28f, .47f, 0, true).Box(.16f, .25f, .28f, .32f, 0, true).Box(.72f, .3f, .84f, .37f, 0, true);
                    break;
                case "people":
                    p.Circle(.72f, .64f, .11f).Box(.58f, .1f, .92f, .44f, .12f);
                    p.Circle(.4f, .68f, .18f, true).Box(.1f, .04f, .7f, .54f, .2f, true);
                    p.Circle(.4f, .68f, .13f).Box(.16f, .1f, .64f, .46f, .15f);
                    break;
                case "smile":
                    p.Circle(.5f, .5f, .44f).Circle(.36f, .59f, .065f, true).Circle(.64f, .59f, .065f, true);
                    p.Arc(.5f, .5f, .23f, .075f, 200, 340, true);
                    break;
                case "coin":
                    p.Circle(.5f, .5f, .44f).Ring(.5f, .5f, .34f, .045f, true);
                    p.Arc(.5f, .585f, .1f, .06f, 30, 270, true).Arc(.5f, .415f, .1f, .06f, 210, 450, true);
                    p.Line(.5f, .24f, .5f, .76f, .05f, true);
                    break;
                case "bolt":
                    p.Poly(.56f, .96f, .2f, .45f, .47f, .45f, .36f, .04f, .82f, .58f, .54f, .58f, .74f, .96f);
                    break;
                case "house":
                    p.Poly(.5f, .92f, .05f, .5f, .95f, .5f).Box(.17f, .08f, .83f, .56f, .02f).Box(.66f, .62f, .78f, .86f);
                    p.Box(.41f, .08f, .59f, .36f, .02f, true);
                    break;
                case "shop":
                    p.Box(.08f, .64f, .92f, .86f, .03f);
                    for (int k = 0; k < 4; k++) p.Circle(.185f + k * .21f, .64f, .105f);
                    p.Box(.15f, .08f, .85f, .5f, .02f);
                    p.Box(.22f, .08f, .42f, .4f, 0, true).Box(.5f, .22f, .77f, .42f, 0, true);
                    break;
                case "factory":
                    p.Box(.06f, .08f, .94f, .5f, .02f).Poly(.06f, .49f, .06f, .74f, .36f, .49f).Poly(.36f, .49f, .36f, .74f, .66f, .49f);
                    p.Box(.72f, .4f, .86f, .94f, .02f);
                    for (int k = 0; k < 3; k++) p.Box(.14f + k * .25f, .2f, .27f + k * .25f, .33f, 0, true);
                    break;
                case "cross":
                    p.Circle(.5f, .5f, .44f).Box(.42f, .22f, .58f, .78f, .02f, true).Box(.22f, .42f, .78f, .58f, .02f, true);
                    break;
                case "tree":
                    p.Circle(.5f, .64f, .27f).Circle(.3f, .5f, .19f).Circle(.7f, .5f, .19f).Box(.44f, .06f, .56f, .42f, .02f);
                    break;
                case "road":
                    p.Poly(.06f, .06f, .3f, .06f, .44f, .94f, .38f, .94f).Poly(.94f, .06f, .7f, .06f, .56f, .94f, .62f, .94f);
                    p.Box(.465f, .08f, .535f, .28f).Box(.47f, .4f, .53f, .56f).Box(.475f, .68f, .525f, .8f);
                    break;
                case "bulldoze":
                    p.Box(.26f, .38f, .68f, .64f, .04f).Box(.34f, .62f, .6f, .9f, .04f).Box(.41f, .68f, .53f, .82f, 0, true);
                    p.Box(.08f, .08f, .72f, .34f, .13f).Box(.18f, .16f, .62f, .26f, .05f, true);
                    p.Poly(.76f, .08f, .95f, .08f, .95f, .56f, .84f, .56f).Line(.66f, .48f, .84f, .34f, .07f);
                    break;
                case "explore":
                    p.Poly(.26f, .94f, .26f, .2f, .43f, .36f, .56f, .06f, .68f, .11f, .55f, .4f, .78f, .42f);
                    break;
                case "pause":
                    p.Box(.24f, .16f, .42f, .84f, .04f).Box(.58f, .16f, .76f, .84f, .04f);
                    break;
                case "play":
                    p.Poly(.26f, .14f, .26f, .86f, .84f, .5f);
                    break;
                case "fast":
                    p.Poly(.08f, .18f, .08f, .82f, .5f, .5f).Poly(.5f, .18f, .5f, .82f, .92f, .5f);
                    break;
                case "gear":
                    p.Circle(.5f, .5f, .3f);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * Mathf.PI / 4;
                        p.Line(.5f, .5f, .5f + Mathf.Cos(a) * .37f, .5f + Mathf.Sin(a) * .37f, .15f);
                    }
                    p.Circle(.5f, .5f, .12f, true);
                    break;
                case "chart":
                    p.Box(.12f, .1f, .32f, .46f, .03f).Box(.4f, .1f, .6f, .7f, .03f).Box(.68f, .1f, .88f, .92f, .03f);
                    break;
                case "warning":
                    p.Poly(.5f, .93f, .04f, .09f, .96f, .09f);
                    p.Box(.455f, .36f, .545f, .7f, .03f, true).Circle(.5f, .23f, .06f, true);
                    break;
                case "check":
                    p.Line(.2f, .52f, .42f, .3f, .13f).Line(.42f, .3f, .8f, .74f, .13f);
                    break;
                case "chevron":
                    p.Line(.38f, .2f, .64f, .5f, .11f).Line(.64f, .5f, .38f, .8f, .11f);
                    break;
                case "close":
                    p.Line(.26f, .26f, .74f, .74f, .1f).Line(.26f, .74f, .74f, .26f, .1f);
                    break;
                case "target":
                    p.Ring(.5f, .5f, .38f, .08f).Ring(.5f, .5f, .2f, .08f).Circle(.5f, .5f, .07f);
                    break;
                case "shield":
                    p.Poly(.5f, .94f, .86f, .8f, .82f, .4f, .5f, .06f, .18f, .4f, .14f, .8f);
                    p.Poly(Scale(.72f, .5f, .5f, .5f, .94f, .86f, .8f, .82f, .4f, .5f, .06f, .18f, .4f, .14f, .8f), true);
                    p.Poly(Scale(.55f, .5f, .5f, .5f, .94f, .86f, .8f, .82f, .4f, .5f, .06f, .18f, .4f, .14f, .8f));
                    break;
                case "school":
                    p.Poly(.5f, .86f, .96f, .62f, .5f, .38f, .04f, .62f).Box(.24f, .2f, .76f, .5f, .1f);
                    p.Poly(new[] { .5f, .44f, .96f, .62f, .5f, .32f, .04f, .62f }, true);
                    p.Poly(.5f, .86f, .96f, .62f, .5f, .4f, .04f, .62f).Line(.86f, .6f, .86f, .28f, .045f).Circle(.86f, .26f, .05f);
                    break;
                case "flask":
                    p.Box(.38f, .62f, .62f, .9f, .02f).Box(.32f, .86f, .68f, .93f, .02f);
                    p.Poly(.38f, .66f, .62f, .66f, .9f, .14f, .86f, .07f, .14f, .07f, .1f, .14f);
                    p.Poly(new[] { .45f, .62f, .55f, .62f, .55f, .9f, .45f, .9f }, true).Box(.24f, .36f, .76f, .4f, 0, true);
                    break;
                case "question":
                    p.Arc(.5f, .64f, .18f, .1f, -50, 180).Line(.62f, .5f, .5f, .4f, .1f).Line(.5f, .4f, .5f, .33f, .1f).Circle(.5f, .15f, .07f);
                    break;
                case "briefcase":
                    p.Box(.08f, .12f, .92f, .7f, .08f).Box(.32f, .64f, .68f, .86f, .06f).Box(.42f, .64f, .58f, .78f, .02f, true);
                    p.Box(.08f, .42f, .92f, .47f, 0, true);
                    break;
                case "star":
                    var star = new float[20];
                    for (int k = 0; k < 10; k++)
                    {
                        float a = Mathf.PI / 2 + k * Mathf.PI / 5, r = k % 2 == 0 ? .46f : .2f;
                        star[k * 2] = .5f + Mathf.Cos(a) * r;
                        star[k * 2 + 1] = .48f + Mathf.Sin(a) * r;
                    }
                    p.Poly(star);
                    break;
                case "pencil":
                    p.Line(.3f, .3f, .76f, .76f, .16f).Poly(.14f, .14f, .32f, .2f, .2f, .32f);
                    break;
                case "level":
                    p.Box(.1f, .1f, .36f, .4f, .02f).Box(.37f, .1f, .63f, .64f, .02f).Box(.64f, .1f, .9f, .9f, .02f);
                    break;
                case "radius":
                    p.Ring(.5f, .5f, .4f, .06f).Ring(.5f, .5f, .22f, .06f).Circle(.5f, .5f, .08f);
                    break;
                case "forest":
                    p.Poly(.3f, .92f, .06f, .36f, .54f, .36f).Poly(.68f, .8f, .44f, .3f, .92f, .3f);
                    p.Box(.26f, .1f, .34f, .38f).Box(.64f, .1f, .72f, .32f);
                    break;
                case "water":
                    p.Poly(.5f, .94f, .2f, .46f, .8f, .46f).Circle(.5f, .38f, .3f);
                    break;
                default:
                    p.Circle(.5f, .5f, .3f);
                    break;
            }
        }

        /// <summary>Scales polygon coordinates around a centre.</summary>
        static float[] Scale(float s, float cx, float cy, params float[] xy)
        {
            var r = new float[xy.Length];
            for (int i = 0; i < xy.Length; i += 2)
            {
                r[i] = cx + (xy[i] - cx) * s;
                r[i + 1] = cy + (xy[i + 1] - cy) * s;
            }
            return r;
        }

        public sealed class Painter
        {
            readonly int n;
            readonly float[] alpha;

            public Painter(int size)
            {
                n = size;
                alpha = new float[size * size];
            }

            void Apply(Func<Vector2, float> sdf, bool cut)
            {
                float pixel = 1f / n;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + .5f) * pixel, (y + .5f) * pixel);
                    float cover = Mathf.Clamp01(.5f - sdf(p) / pixel);
                    int i = y * n + x;
                    alpha[i] = cut ? Mathf.Min(alpha[i], 1 - cover) : Mathf.Max(alpha[i], cover);
                }
            }

            public Painter Circle(float cx, float cy, float r, bool cut = false)
            {
                var c = new Vector2(cx, cy);
                Apply(p => (p - c).magnitude - r, cut);
                return this;
            }

            public Painter Ring(float cx, float cy, float r, float width, bool cut = false)
            {
                var c = new Vector2(cx, cy);
                Apply(p => Mathf.Abs((p - c).magnitude - r) - width * .5f, cut);
                return this;
            }

            /// <summary>Part of a ring from angle a0 counter-clockwise to a1, in degrees, with round ends.</summary>
            public Painter Arc(float cx, float cy, float r, float width, float a0, float a1, bool cut = false)
            {
                var c = new Vector2(cx, cy);
                float span = a1 - a0;
                var e0 = c + new Vector2(Mathf.Cos(a0 * Mathf.Deg2Rad), Mathf.Sin(a0 * Mathf.Deg2Rad)) * r;
                var e1 = c + new Vector2(Mathf.Cos(a1 * Mathf.Deg2Rad), Mathf.Sin(a1 * Mathf.Deg2Rad)) * r;
                Apply(p =>
                {
                    var d = p - c;
                    float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    if (Mathf.Repeat(angle - a0, 360) <= span) return Mathf.Abs(d.magnitude - r) - width * .5f;
                    return Mathf.Min((p - e0).magnitude, (p - e1).magnitude) - width * .5f;
                }, cut);
                return this;
            }

            public Painter Box(float x0, float y0, float x1, float y1, float radius = 0, bool cut = false)
            {
                var c = new Vector2((x0 + x1) * .5f, (y0 + y1) * .5f);
                var half = new Vector2((x1 - x0) * .5f, (y1 - y0) * .5f);
                Apply(p =>
                {
                    var q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(radius, radius);
                    return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
                }, cut);
                return this;
            }

            /// <summary>Segment with round ends.</summary>
            public Painter Line(float x0, float y0, float x1, float y1, float width, bool cut = false)
            {
                var a = new Vector2(x0, y0);
                var b = new Vector2(x1, y1);
                var ab = b - a;
                Apply(p =>
                {
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
                    return (p - (a + ab * t)).magnitude - width * .5f;
                }, cut);
                return this;
            }

            public Painter Poly(params float[] xy) => Poly(xy, false);

            public Painter Poly(float[] xy, bool cut)
            {
                int count = xy.Length / 2;
                var v = new Vector2[count];
                for (int i = 0; i < count; i++) v[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
                Apply(p =>
                {
                    float d = Vector2.Dot(p - v[0], p - v[0]);
                    float s = 1;
                    for (int i = 0, j = count - 1; i < count; j = i, i++)
                    {
                        var e = v[j] - v[i];
                        var w = p - v[i];
                        var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                        d = Mathf.Min(d, Vector2.Dot(b, b));
                        bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                        if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
                    }
                    return s * Mathf.Sqrt(d);
                }, cut);
                return this;
            }

            public Sprite ToSprite()
            {
                var tex = UiKit.NewTexture(n, n);
                var px = new Color[n * n];
                for (int i = 0; i < px.Length; i++) px[i] = new Color(1, 1, 1, alpha[i]);
                tex.SetPixels(px);
                tex.Apply(false, true);
                return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100);
            }
        }
    }

    /// <summary>Reports where a finger or the mouse touches or drags on an element, from 0 to 1 on each axis.</summary>
    public sealed class TouchArea : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<Vector2> OnTouch;

        public void OnPointerDown(PointerEventData e) => Report(e);
        public void OnDrag(PointerEventData e) => Report(e);

        void Report(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local)) return;
            var r = rt.rect;
            OnTouch?.Invoke(new Vector2(Mathf.InverseLerp(r.xMin, r.xMax, local.x), Mathf.InverseLerp(r.yMin, r.yMax, local.y)));
        }
    }
}
