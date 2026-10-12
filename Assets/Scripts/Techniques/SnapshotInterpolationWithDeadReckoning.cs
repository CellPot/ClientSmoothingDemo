using System.Collections.Generic;
using Network;
using UnityEngine;

namespace Techniques
{
    public class SnapshotInterpolationWithDeadReckoning : BaseClientEntity
    {
        [Tooltip("Fixed buffer delay, or the starting value when the buffer is adaptive.")]
        [Range(0.05f, 0.5f)] public float bufferDelay = 0.15f;
        [Tooltip("Size the buffer from the measured send interval and arrival jitter instead of using a fixed delay.")]
        public bool adaptiveBuffer = true;
        [Tooltip("Buffer = intervalFactor * send interval + jitterFactor * arrival jitter, clamped to the limits below.")]
        [Range(0f, 2f)] public float intervalFactor = 0.5f;
        [Range(0f, 6f)] public float jitterFactor = 2.5f;
        [Range(0.02f, 0.5f)] public float minBufferDelay = 0.05f;
        [Range(0.05f, 0.8f)] public float maxBufferDelay = 0.4f;

        public override float AddedDelaySeconds => _delay;
        [Range(0.1f, 1f)] public float maxReckoningTime = 0.4f;

        private List<NetworkSimulator.Snapshot> _buffer = new List<NetworkSimulator.Snapshot>();

        private Vector3 _lastRenderedPos;
        private Vector3 _lastRenderedVel;
        private float _lastRenderedTime;
        private bool _initialized;
        private bool _reckoning;

        private float _delay;             // buffer delay currently in use
        private float _arrivalOffset;     // smoothed (arrival time - snapshot timestamp)
        private float _arrivalJitter = 0.02f;
        private float _lastTimestamp = float.NegativeInfinity;
        private readonly Queue<float> _gaps = new Queue<float>();
        private bool _haveOffset;

        protected override void Awake()
        {
            techniqueName = "Snapshot Interp + Dead Reckoning";
            color = new Color(0.2f, 0.8f, 0.4f);
            base.Awake();
            _delay = bufferDelay;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _delay = bufferDelay;
            _haveOffset = false;
            _arrivalJitter = 0.02f;
            _lastTimestamp = float.NegativeInfinity;
            _gaps.Clear();
        }

        // Timestamps are on the client clock (rebased by the transport), so arrival - timestamp is the one-way
        // delay plus a constant. Its deviation is the jitter; the smallest timestamp gap is the send interval
        // (losses only make gaps larger).
        void AdaptBuffer(NetworkSimulator.Snapshot snap)
        {
            float offset = Time.time - snap.timestamp;
            if (!_haveOffset)
            {
                _arrivalOffset = offset;
                _haveOffset = true;
            }
            else
            {
                _arrivalJitter += (Mathf.Abs(offset - _arrivalOffset) - _arrivalJitter) * 0.1f;
                _arrivalOffset += (offset - _arrivalOffset) * 0.1f;
            }

            if (snap.timestamp > _lastTimestamp)
            {
                if (!float.IsNegativeInfinity(_lastTimestamp))
                {
                    _gaps.Enqueue(snap.timestamp - _lastTimestamp);
                    if (_gaps.Count > 32) _gaps.Dequeue();
                }
                _lastTimestamp = snap.timestamp;
            }

            float interval = 0.05f;
            if (_gaps.Count > 0)
            {
                interval = float.MaxValue;
                foreach (float g in _gaps) interval = Mathf.Min(interval, g);
            }

            float target = Mathf.Clamp(intervalFactor * interval + jitterFactor * _arrivalJitter,
                minBufferDelay, maxBufferDelay);
            _delay += (target - _delay) * 0.2f;
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            if (adaptiveBuffer) AdaptBuffer(snap);
            else _delay = bufferDelay;

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

            float renderTime = Time.time - _delay;

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