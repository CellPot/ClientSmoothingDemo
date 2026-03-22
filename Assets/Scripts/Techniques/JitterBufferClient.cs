using System.Collections.Generic;
using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE 6 — Jitter Buffer
    /// Queues incoming snapshots and plays them back at a steady, clock-driven rate,
    /// absorbing network jitter (variable arrival timing). Trades a small delay for
    /// smooth, consistent playback — the same strategy VoIP and video streaming use.
    /// </summary>
    public class JitterBufferClient : BaseClientEntity
    {
        [Header("Settings")]
        [Tooltip("Target buffer depth in seconds. Must be > max expected jitter.")]
        [Range(0.05f, 0.5f)] public float bufferDepth = 0.12f;

        [Tooltip("How aggressively the playback clock corrects drift.")]
        [Range(0.001f, 0.05f)] public float clockAdjustRate = 0.01f;

        private SortedList<float, NetworkSimulator.Snapshot> _queue
            = new SortedList<float, NetworkSimulator.Snapshot>();

        private float _playbackTime = -1f;   // Our local playback clock
        private Vector3 _currentPos;
        private bool _initialized;

        void Awake()
        {
            techniqueName = "Jitter Buffer";
            color = new Color(0.5f, 0.5f, 1f); // periwinkle
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            // Avoid duplicate timestamps (add tiny epsilon if collision)
            float key = snap.timestamp;
            while (_queue.ContainsKey(key)) key += 0.0001f;
            _queue.Add(key, snap);

            // Initialise playback clock on first packet
            if (!_initialized)
            {
                _playbackTime = snap.timestamp - bufferDepth;
                _initialized  = true;
            }

            // Prune old entries
            while (_queue.Count > 128)
                _queue.RemoveAt(0);
        }

        protected override void UpdatePosition()
        {
            if (!_initialized || _queue.Count == 0) return;

            // Advance playback clock
            _playbackTime += Time.deltaTime;

            // Adaptive clock: if buffer is too thin, slow down; too thick, speed up
            float bufferSize = _queue.Keys[_queue.Count - 1] - _playbackTime;
            float drift = bufferSize - bufferDepth;
            _playbackTime += drift * clockAdjustRate;

            // Find the snapshot at or just before playback time
            NetworkSimulator.Snapshot best = _queue.Values[0];
            foreach (var kvp in _queue)
            {
                if (kvp.Key <= _playbackTime)
                    best = kvp.Value;
                else
                    break;
            }

            // Linear interpolation to next snapshot for sub-interval smoothness
            int idx = _queue.IndexOfKey(best.timestamp);
            if (idx < _queue.Count - 1)
            {
                var next = _queue.Values[idx + 1];
                float span = next.timestamp - best.timestamp;
                float t    = span > 0f ? Mathf.Clamp01((_playbackTime - best.timestamp) / span) : 1f;
                _currentPos = Vector3.Lerp(best.position, next.position, t);
            }
            else
            {
                _currentPos = best.position;
            }

            transform.position = _currentPos;

            // Trim consumed snapshots (keep one behind for interpolation)
            while (_queue.Count > 2 && _queue.Keys[1] < _playbackTime - bufferDepth - 0.2f)
                _queue.RemoveAt(0);
        }
    }
}
