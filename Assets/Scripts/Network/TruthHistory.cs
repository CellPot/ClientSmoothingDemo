using System.Collections.Generic;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// Time-sorted history of the authoritative position on the local clock. Shared by every
    /// <see cref="IReferenceSource"/> so both scenes measure error against the same kind of reference.
    /// </summary>
    public class TruthHistory
    {
        private const float HistorySeconds = 3f;
        private const float MaxExtrapolation = 0.1f;
        // Faster than anything the demo path does legitimately: a segment this fast spans a teleport, so don't extrapolate along it.
        private const float MaxPlausibleSpeed = 40f;

        private readonly List<(float t, Vector3 p)> _samples = new List<(float, Vector3)>();

        public void Clear() => _samples.Clear();

        public void Add(float t, Vector3 position)
        {
            int i = _samples.Count;
            while (i > 0 && _samples[i - 1].t > t) i--; // keep sorted despite reordering
            _samples.Insert(i, (t, position));

            float cutoff = _samples[_samples.Count - 1].t - HistorySeconds;
            int drop = 0;
            while (drop < _samples.Count - 1 && _samples[drop].t < cutoff) drop++;
            if (drop > 0) _samples.RemoveRange(0, drop);
        }

        public bool TryGet(float localTime, out Vector3 position)
        {
            position = default;
            int n = _samples.Count;
            if (n < 2 || localTime < _samples[0].t) return false;

            var last = _samples[n - 1];
            if (localTime >= last.t)
            {
                // Newest data is slightly behind "now": extrapolate a little along the last segment.
                var prev = _samples[n - 2];
                float span = last.t - prev.t;
                Vector3 vel = span > 0f ? (last.p - prev.p) / span : Vector3.zero;
                if (vel.sqrMagnitude > MaxPlausibleSpeed * MaxPlausibleSpeed) vel = Vector3.zero;
                position = last.p + vel * Mathf.Min(localTime - last.t, MaxExtrapolation);
                return true;
            }

            for (int i = n - 2; i >= 0; i--)
            {
                if (_samples[i].t > localTime) continue;
                float span = _samples[i + 1].t - _samples[i].t;
                float k = span > 0f ? (localTime - _samples[i].t) / span : 1f;
                position = Vector3.Lerp(_samples[i].p, _samples[i + 1].p, k);
                return true;
            }
            return false;
        }
    }
}
