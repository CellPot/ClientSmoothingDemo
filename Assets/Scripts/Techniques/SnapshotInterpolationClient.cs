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
            // Keep buffer bounded
            while (_buffer.Count > 64)
                _buffer.RemoveAt(0);
        }

        protected override void UpdatePosition()
        {
            if (_buffer.Count < 2) return;

            float renderTime = Time.time - bufferDelay;

            // Find the two snapshots that straddle renderTime
            NetworkSimulator.Snapshot from = _buffer[0];
            NetworkSimulator.Snapshot to = _buffer[0];

            for (int i = 0; i < _buffer.Count - 1; i++)
            {
                if (_buffer[i].timestamp <= renderTime && _buffer[i + 1].timestamp >= renderTime)
                {
                    from = _buffer[i];
                    to = _buffer[i + 1];
                    break;
                }

                // If renderTime is past the newest snapshot, extrapolate from last two
                if (i == _buffer.Count - 2)
                {
                    from = _buffer[_buffer.Count - 2];
                    to = _buffer[_buffer.Count - 1];
                }
            }

            float span = to.timestamp - from.timestamp;
            if (span <= 0f)
            {
                transform.position = to.position;
                return;
            }

            float t = Mathf.Clamp01((renderTime - from.timestamp) / span);
            transform.position = Vector3.Lerp(from.position, to.position, t);

            // Trim old snapshots
            while (_buffer.Count > 2 && _buffer[1].timestamp < renderTime - 0.5f)
                _buffer.RemoveAt(0);
        }
    }
}