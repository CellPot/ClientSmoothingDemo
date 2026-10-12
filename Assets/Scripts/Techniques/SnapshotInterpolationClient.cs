using System.Collections.Generic;
using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE 2 — Snapshot Interpolation
    /// Buffers a history of snapshots and interpolates between two *past* confirmed
    /// server states, trading a small fixed delay for smooth, jitter-free movement.
    /// This is the standard approach used in competitive multiplayer games.
    /// </summary>
    public class SnapshotInterpolationClient : BaseClientEntity
    {
        [Header("Settings")]
        [Tooltip("How far behind real-time (seconds) to render. Must be > one snapshot interval.")]
        [Range(0.05f, 0.5f)]
        public float bufferDelay = 0.15f;
        public override float AddedDelaySeconds => bufferDelay;

        private List<NetworkSimulator.Snapshot> _buffer = new List<NetworkSimulator.Snapshot>();

        protected override void Awake()
        {
            techniqueName = "Snapshot Interpolation";
            color = Color.cyan;
            base.Awake();
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            _buffer.Add(snap);
            SnapshotSampler.CapCount(_buffer, 64);
        }

        protected override void UpdatePosition()
        {
            if (_buffer.Count < 2) return;

            float renderTime = Time.time - bufferDelay;

            // Straddling pair; if renderTime is outside the buffer, clamp to the last two snapshots
            int fromIdx = SnapshotSampler.FindBracket(_buffer, renderTime);
            if (fromIdx < 0) fromIdx = _buffer.Count - 2;

            SnapshotSampler.Sample(_buffer, fromIdx, renderTime, out Vector3 pos, out _);
            transform.position = pos;

            SnapshotSampler.TrimBefore(_buffer, renderTime - 0.5f);
        }
    }
}