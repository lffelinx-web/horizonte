using System;
using System.Collections.Generic;

namespace Horizonte.Simulation
{
    /// <summary>
    /// A lake: a basin of tiles, deep in the middle and shallow at the shore, holding water the town can pump through
    /// an intake on its bank. Water drawn faster than the lake fills up lowers it: the shallow tiles around the edge dry
    /// out first and the deep middle last. Left alone it fills up again in about ten days. Once completely dry, its bed
    /// can be built on, and the tiles built on are no longer part of the lake.
    /// </summary>
    public sealed class Lake
    {
        public int Id;
        /// <summary>Water in it now and when full, in the same units as the town's water (m³ a day).</summary>
        public float Volume, Capacity;
        /// <summary>Water pumped out of it a day right now, and water flowing in a day.</summary>
        public float Draw, Inflow;
        /// <summary>Metres the surface has fallen below full.</summary>
        public float Drop;
        public float MaxDepth;
        public readonly List<int> Tiles = new List<int>();

        public float Level => Capacity <= 0 ? 0 : Volume / Capacity;
        /// <summary>Once run dry, a lake gives no water until it is back to a tenth of its size.</summary>
        public bool Dried;
        public bool Dry => Dried || Volume < .5f;
        /// <summary>Days of water left at the present rate, or -1 if it is not going down.</summary>
        public float DaysLeft => Draw > Inflow ? Volume / (Draw - Inflow) : -1;
    }

    public sealed partial class City
    {
        /// <summary>Water units each metre of depth of a lake tile holds.</summary>
        public const float LakeWaterPerMetre = 20;
        /// <summary>A lake fills up in this many days with nothing drawn.</summary>
        public const float LakeRefillDays = 10;

        /// <summary>Lake of each tile (its id, 1 and up; 0 where there is none). Wet or dry, the tile belongs to it.</summary>
        public byte[] LakeOf;
        /// <summary>Depth of each lake tile when the lake is full, in decimetres.</summary>
        public byte[] LakeDepth;
        public readonly List<Lake> Lakes = new List<Lake>();

        public Lake LakeAt(int i) => LakeOf != null && LakeOf[i] > 0 && LakeOf[i] <= Lakes.Count ? Lakes[LakeOf[i] - 1] : null;

        void AllocateLakes()
        {
            LakeOf = new byte[Width * Height];
            LakeDepth = new byte[Width * Height];
        }

        /// <summary>Gathers the tiles and size of every lake from the tile layers; volumes stay as given (or full when new).</summary>
        public void RebuildLakes(float[] volumes = null)
        {
            var old = new List<Lake>(Lakes);
            Lakes.Clear();
            int count = 0;
            for (int i = 0; i < LakeOf.Length; i++) count = Math.Max(count, LakeOf[i]);
            for (int k = 1; k <= count; k++) Lakes.Add(new Lake { Id = k });
            for (int i = 0; i < LakeOf.Length; i++)
            {
                if (LakeOf[i] == 0) continue;
                var lake = Lakes[LakeOf[i] - 1];
                lake.Tiles.Add(i);
                lake.MaxDepth = Math.Max(lake.MaxDepth, LakeDepth[i] / 10f);
            }
            for (int k = 0; k < Lakes.Count; k++)
            {
                var lake = Lakes[k];
                lake.Capacity = VolumeAt(lake, 0);
                lake.Inflow = lake.Capacity / LakeRefillDays;
                float v = volumes != null && k < volumes.Length ? volumes[k] : k < old.Count ? old[k].Volume : lake.Capacity;
                lake.Volume = Math.Max(0, Math.Min(lake.Capacity, v));
                lake.Dried = lake.Volume < .5f;
                lake.Draw = k < old.Count ? old[k].Draw : 0;
                SettleLake(lake);
            }
        }

        float VolumeAt(Lake lake, float drop)
        {
            float v = 0;
            foreach (int i in lake.Tiles) v += Math.Max(0, LakeDepth[i] / 10f - drop);
            return v * LakeWaterPerMetre;
        }

        /// <summary>Finds how far the surface has fallen for the water in the lake, and which tiles are under water.</summary>
        void SettleLake(Lake lake)
        {
            float lo = 0, hi = lake.MaxDepth;
            if (lake.Volume >= lake.Capacity) hi = 0;
            for (int k = 0; k < 24 && hi - lo > .001f; k++)
            {
                float mid = (lo + hi) * .5f;
                if (VolumeAt(lake, mid) > lake.Volume) lo = mid; else hi = mid;
            }
            lake.Drop = lake.Dry ? lake.MaxDepth : hi;
            foreach (int i in lake.Tiles)
            {
                var t = LakeDepth[i] / 10f > lake.Drop + .001f ? Simulation.Terrain.Water : Simulation.Terrain.Lakebed;
                if (Terrain[i] == t) continue;
                Terrain[i] = t;
                MarkChanged(i);
                lakesChanged = true;
            }
        }

        bool lakesChanged;

        /// <summary>Every hour: water flows in, the town's intakes draw it out, the shore moves.</summary>
        void UpdateLakes()
        {
            foreach (var lake in Lakes)
            {
                lake.Volume = Math.Max(0, Math.Min(lake.Capacity, lake.Volume + (lake.Inflow - lake.Draw) / HoursPerDay));
                if (lake.Volume < .5f) lake.Dried = true;
                else if (lake.Dried && lake.Volume >= lake.Capacity * .1f) lake.Dried = false;
                SettleLake(lake);
            }
            // Water that comes or goes changes which intakes work.
            if (lakesChanged)
            {
                lakesChanged = false;
                UpdateNetworks();
            }
        }

        /// <summary>Where an intake draws from: a lake's id, 0 for the river, -1 if no water touches it.</summary>
        public int SourceOf(int anchor)
        {
            var (w, h) = Size(anchor);
            int ax = anchor % Width, ay = anchor / Width, source = -1;
            for (int k = 0; k < w; k++) { Check(ax + k, ay - 1); Check(ax + k, ay + h); }
            for (int k = 0; k < h; k++) { Check(ax - 1, ay + k); Check(ax + w, ay + k); }
            return source;

            void Check(int x, int y)
            {
                if (!InBounds(x, y)) return;
                int i = Index(x, y);
                if (LakeOf[i] > 0) { if (source <= 0) source = LakeOf[i]; }
                else if (Terrain[i] == Simulation.Terrain.Water && source < 0) source = 0;
            }
        }

        /// <summary>Water an intake can give a day: all of its capacity, unless its lake has run dry.</summary>
        public int IntakeYield(int anchor)
        {
            var def = DefAt(anchor);
            if (def == null || def.Intake <= 0 || !Working(anchor)) return 0;
            int source = SourceOf(anchor);
            if (source < 0) return 0;
            if (source > 0 && Lakes[source - 1].Dry) return 0;
            return def.Intake;
        }

        /// <summary>Whether a dry lake bed may be built on: only once the whole lake is dry.</summary>
        bool LakebedFree(int i) => LakeAt(i)?.Dry ?? true;

        /// <summary>A tile built on stops being part of its lake: firm ground from now on.</summary>
        void LeaveLake(int i)
        {
            if (LakeOf[i] == 0) return;
            LakeOf[i] = 0;
            LakeDepth[i] = 0;
            Terrain[i] = Simulation.Terrain.Grass;
            MarkChanged(i);
            RebuildLakes();
        }
    }
}
