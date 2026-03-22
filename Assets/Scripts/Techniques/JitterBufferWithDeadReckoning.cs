using System.Collections.Generic;
using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE COMBO — Jitter Buffer + Dead Reckoning
    /// Jitter buffer drives normal playback from a sorted, clock-driven queue.
    /// When the buffer genuinely runs dry (loss burst or long gap), dead reckoning
    /// takes over from the exact last rendered position and velocity — not from a
    /// stale snapshot — so there is no discontinuity at the handoff point.
    /// </summary>
    public class JitterBufferWithDeadReckoning : BaseClientEntity
    {
        [Header("Jitter Buffer")] [Range(0.05f, 0.5f)]
        public float bufferDepth = 0.12f;

        [Range(0.001f, 0.05f)] public float clockAdjustRate = 0.01f;

        [Header("Dead Reckoning Fallback")] [Range(0.1f, 1f)]
        public float maxReckoningTime = 0.4f;

        // Sorted by server timestamp
        private SortedList<float, NetworkSimulator.Snapshot> _queue
            = new SortedList<float, NetworkSimulator.Snapshot>();

        private float _playbackTime = -1f;
        private bool _initialized;

        // Reckoning state — updated every frame from the last rendered output
        private Vector3 _reckonPos;
        private Vector3 _reckonVel;
        private float _reckonTimer; // how long we've been reckoning

        protected override void Awake()
        {
            techniqueName = "Jitter Buffer + Dead Reckoning";
            color = new Color(0f, 0.85f, 1f);
            base.Awake();
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            float key = snap.timestamp;
            while (_queue.ContainsKey(key)) key += 0.00001f;
            _queue.Add(key, snap);

            if (!_initialized)
            {
                _playbackTime = snap.timestamp - bufferDepth;
                _initialized = true;
            }

            // Reset reckoning countdown — fresh data arrived
            _reckonTimer = 0f;

            while (_queue.Count > 128)
                _queue.RemoveAt(0);
        }

        protected override void UpdatePosition()
        {
            if (!_initialized) return;

            // Advance playback clock
            _playbackTime += Time.deltaTime;

            // Adaptive clock correction
            if (_queue.Count > 0)
            {
                float newestInBuffer = _queue.Keys[_queue.Count - 1];
                float bufferSize = newestInBuffer - _playbackTime;
                _playbackTime += (bufferSize - bufferDepth) * clockAdjustRate;
            }

            // Find the two snapshots that bracket playback time
            int fromIdx = -1;
            for (int i = 0; i < _queue.Count - 1; i++)
            {
                if (_queue.Keys[i] <= _playbackTime && _queue.Keys[i + 1] >= _playbackTime)
                {
                    fromIdx = i;
                    break;
                }
            }

            if (fromIdx >= 0)
            {
                // ── Normal jitter-buffer playback ─────────────────────────────────
                var from = _queue.Values[fromIdx];
                var to = _queue.Values[fromIdx + 1];
                float span = to.timestamp - from.timestamp;
                float t = span > 0f
                    ? Mathf.Clamp01((_playbackTime - from.timestamp) / span)
                    : 1f;

                Vector3 rendered = Vector3.Lerp(from.position, to.position, t);
                Vector3 rendVel = Vector3.Lerp(from.velocity, to.velocity, t);

                // Keep reckoning state synced to rendered output every frame
                _reckonPos = rendered;
                _reckonVel = rendVel;
                _reckonTimer = 0f;

                transform.position = rendered;

                // Trim entries well behind playback
                while (_queue.Count > 2 && _queue.Keys[1] < _playbackTime - bufferDepth - 0.3f)
                    _queue.RemoveAt(0);
            }
            else
            {
                // ── Dead reckoning fallback ───────────────────────────────────────
                // Buffer is dry: either we're ahead of all packets or behind all of them.
                // Extrapolate from the last good rendered position/velocity.
                _reckonTimer += Time.deltaTime;

                float dt = Mathf.Min(_reckonTimer, maxReckoningTime);
                transform.position = _reckonPos + _reckonVel * dt;
            }
        }
    }
}