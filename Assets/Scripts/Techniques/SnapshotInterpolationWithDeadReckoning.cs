using System.Collections.Generic;
using Network;
using UnityEngine;

namespace Techniques
{
    public class SnapshotInterpolationWithDeadReckoning : BaseClientEntity
    {
        [Range(0.05f, 0.5f)] public float bufferDelay = 0.15f;
        [Range(0.1f, 1f)] public float maxReckoningTime = 0.4f;

        private List<NetworkSimulator.Snapshot> _buffer = new List<NetworkSimulator.Snapshot>();

        private Vector3 _lastRenderedPos;
        private Vector3 _lastRenderedVel;
        private float _lastRenderedTime;
        private bool _initialized;
        private bool _reckoning;

        protected override void Awake()
        {
            techniqueName = "Snapshot Interp + Dead Reckoning";
            color = new Color(0.2f, 0.8f, 0.4f);
            base.Awake();
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            _buffer.Add(snap);
            while (_buffer.Count > 64) _buffer.RemoveAt(0);

            if (_reckoning)
            {
                _lastRenderedPos = snap.position;
                _lastRenderedVel = snap.velocity;
                _lastRenderedTime = Time.time;
            }

            _initialized = true;
        }

        protected override void UpdatePosition()
        {
            if (!_initialized || _buffer.Count < 2) return;

            float renderTime = Time.time - bufferDelay;

            int fromIdx = -1;
            for (int i = 0; i < _buffer.Count - 1; i++)
            {
                if (_buffer[i].timestamp <= renderTime && _buffer[i + 1].timestamp >= renderTime)
                {
                    fromIdx = i;
                    break;
                }
            }

            if (fromIdx >= 0)
            {
                var from = _buffer[fromIdx];
                var to = _buffer[fromIdx + 1];
                float span = to.timestamp - from.timestamp;
                float t = span > 0f
                    ? Mathf.Clamp01((renderTime - from.timestamp) / span)
                    : 1f;

                _lastRenderedPos = Vector3.Lerp(from.position, to.position, t);
                _lastRenderedVel = Vector3.Lerp(from.velocity, to.velocity, t);
                _lastRenderedTime = Time.time;
                _reckoning = false;

                transform.position = _lastRenderedPos;

                while (_buffer.Count > 2 && _buffer[1].timestamp < renderTime - 0.5f)
                    _buffer.RemoveAt(0);
            }
            else
            {
                _reckoning = true;
                float elapsed = Mathf.Min(Time.time - _lastRenderedTime, maxReckoningTime);
                transform.position = _lastRenderedPos + _lastRenderedVel * elapsed;
            }
        }
    }
}