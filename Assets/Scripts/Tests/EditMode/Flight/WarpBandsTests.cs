using System;
using System.Diagnostics;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    /// <summary>The warp's own band images: Data.WarpBands.</summary>
    public class WarpBandsTests
    {
        static double Mean(byte[] image) => image.Average(b => b / 255.0);

        static double Spread(byte[] image)
        {
            double mean = Mean(image);
            return Math.Sqrt(image.Average(b => (b / 255.0 - mean) * (b / 255.0 - mean)));
        }

        /// <summary>The largest step between neighbours, inside the image or across its edge.</summary>
        static (int inside, int edge) Steps(byte[] image, int size)
        {
            int inside = 0, edge = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int here = image[y * size + x];
                    int across = Math.Abs(here - image[y * size + (x + 1) % size]), down = Math.Abs(here - image[(y + 1) % size * size + x]);
                    if (x == size - 1) edge = Math.Max(edge, across); else inside = Math.Max(inside, across);
                    if (y == size - 1) edge = Math.Max(edge, down); else inside = Math.Max(inside, down);
                }
            return (inside, edge);
        }

        [Test]
        public void TheTunnelSitsOnGreyAndTheDetailIsMostlyDark()
        {
            var tunnel = WarpBands.Tunnel();
            var detail = WarpBands.Detail();
            Assert.AreEqual(WarpBands.TunnelSize * WarpBands.TunnelSize, tunnel.Length);
            Assert.AreEqual(WarpBands.DetailSize * WarpBands.DetailSize, detail.Length);
            Assert.AreEqual(0.5, Mean(tunnel), 0.02);
            Assert.AreEqual(0.13, Spread(tunnel), 0.02);
            Assert.AreEqual(0.31, Mean(detail), 0.03);
            Assert.AreEqual(0.1, Spread(detail), 0.03);
        }

        [Test]
        public void BothTileWithNoSeam()
        {
            var tunnel = Steps(WarpBands.Tunnel(), WarpBands.TunnelSize);
            var detail = Steps(WarpBands.Detail(), WarpBands.DetailSize);
            Assert.LessOrEqual(tunnel.edge, tunnel.inside + 1);
            Assert.LessOrEqual(detail.edge, detail.inside + 1);
        }

        [Test]
        public void TheSameSeedMakesTheSameImage()
        {
            Assert.IsTrue(WarpBands.Detail().SequenceEqual(WarpBands.Detail()));
            Assert.IsFalse(WarpBands.Detail(3).SequenceEqual(WarpBands.Detail()));
        }

        [Test]
        public void MakingThemDoesNotHitchALoad()
        {
            var clock = Stopwatch.StartNew();
            WarpBands.Tunnel();
            WarpBands.Detail();
            UnityEngine.Debug.Log($"WarpBands: {clock.ElapsedMilliseconds} ms");
            Assert.Less(clock.ElapsedMilliseconds, 500);
        }
    }
}
