using System.Collections.Generic;
using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>Shared lookup/interpolation helpers for techniques that keep a time-sorted snapshot buffer.</summary>
    public static class SnapshotSampler
    {
        /// <summary>Index i such that buf[i].timestamp &lt;= time &lt;= buf[i+1].timestamp, or -1 if time is outside the buffer.</summary>
        public static int FindBracket(IList<NetworkSimulator.Snapshot> buf, float time)
        {
            for (int i = 0; i < buf.Count - 1; i++)
                if (buf[i].timestamp <= time && buf[i + 1].timestamp >= time)
                    return i;
            return -1;
        }

        /// <summary>Interpolation fraction of time between two snapshots, clamped to 0..1 (1 when they share a timestamp).</summary>
        public static float Fraction(NetworkSimulator.Snapshot from, NetworkSimulator.Snapshot to, float time)
        {
            float span = to.timestamp - from.timestamp;
            return span > 0f ? Mathf.Clamp01((time - from.timestamp) / span) : 1f;
        }

        /// <summary>Interpolates position and velocity between buf[fromIdx] and buf[fromIdx + 1].</summary>
        public static void Sample(IList<NetworkSimulator.Snapshot> buf, int fromIdx, float time,
            out Vector3 position, out Vector3 velocity)
        {
            var from = buf[fromIdx];
            var to = buf[fromIdx + 1];
            float t = Fraction(from, to, time);
            position = Vector3.Lerp(from.position, to.position, t);
            velocity = Vector3.Lerp(from.velocity, to.velocity, t);
        }

        /// <summary>Drops the oldest snapshots while the buffer exceeds <paramref name="maxCount"/>.</summary>
        public static void CapCount(List<NetworkSimulator.Snapshot> buf, int maxCount)
        {
            int excess = buf.Count - maxCount;
            if (excess > 0) buf.RemoveRange(0, excess);
        }

        /// <summary>Drops snapshots that are entirely older than <paramref name="cutoff"/>, always keeping at least two.</summary>
        public static void TrimBefore(List<NetworkSimulator.Snapshot> buf, float cutoff)
        {
            while (buf.Count > 2 && buf[1].timestamp < cutoff)
                buf.RemoveAt(0);
        }
    }
}
