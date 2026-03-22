using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE COMBO — Kalman Filter + Exponential Smoothing
    /// Kalman runs a full predict/update cycle to estimate the true position.
    /// Exponential smoothing is applied only to the *visual output* — it never
    /// feeds back into the Kalman state — so the filter stays mathematically
    /// correct while the rendered position is visually polished.
    /// </summary>
    public class KalmanWithExponentialSmoothing : BaseClientEntity
    {
        [Header("Kalman")]
        [Range(0.001f, 2f)] public float processNoise     = 0.05f;
        [Range(0.001f, 5f)] public float measurementNoise = 0.8f;

        [Header("Visual Smoothing")]
        [Tooltip("How quickly the visual follows the Kalman estimate. " +
                 "Lower = smoother but more lag. Higher = snappier.")]
        [Range(1f, 30f)] public float smoothSpeed = 12f;

        private KalmanAxis _kx, _ky, _kz;
        private Vector3 _visualPos;           // smoothed visual output
        private bool    _initialized;
        private float   _lastSnapshotTime;
        private float   _timeSinceSnapshot;

        void Awake()
        {
            techniqueName = "Kalman + Exponential Smoothing";
            color = new Color(0.6f, 1f, 0.6f);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _kx = new KalmanAxis();
            _ky = new KalmanAxis();
            _kz = new KalmanAxis();
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            if (!_initialized)
            {
                _kx.Init(snap.position.x, snap.velocity.x);
                _ky.Init(snap.position.y, snap.velocity.y);
                _kz.Init(snap.position.z, snap.velocity.z);
                _visualPos         = snap.position;
                _lastSnapshotTime  = snap.timestamp;
                _timeSinceSnapshot = 0f;
                _initialized = true;
                return;
            }

            float dt = Mathf.Clamp(snap.timestamp - _lastSnapshotTime, 0.001f, 0.5f);
            _lastSnapshotTime  = snap.timestamp;
            _timeSinceSnapshot = 0f;

            _kx.Update(snap.position.x, snap.velocity.x, dt, processNoise, measurementNoise);
            _ky.Update(snap.position.y, snap.velocity.y, dt, processNoise, measurementNoise);
            _kz.Update(snap.position.z, snap.velocity.z, dt, processNoise, measurementNoise);
        }

        protected override void UpdatePosition()
        {
            if (!_initialized) return;

            _timeSinceSnapshot += Time.deltaTime;

            // Predict the Kalman state forward (stops if no packet for too long)
            if (_timeSinceSnapshot < 0.5f)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                _kx.Predict(dt, processNoise);
                _ky.Predict(dt, processNoise);
                _kz.Predict(dt, processNoise);
            }

            // Kalman estimate — this is the "true" state the filter believes in
            Vector3 kalmanEstimate = new Vector3(_kx.position, _ky.position, _kz.position);

            // Exponential smoothing applied only to visual output, NOT fed back into Kalman
            // This removes per-frame jitter from the predict step without corrupting the filter
            _visualPos = Vector3.Lerp(_visualPos, kalmanEstimate, smoothSpeed * Time.deltaTime);

            transform.position = _visualPos;
        }

        // ── Inner Kalman axis (identical to standalone KalmanFilterClient) ────────

        private class KalmanAxis
        {
            public  float position;
            private float _velocity;
            private float _pPos = 1f;
            private float _pVel = 1f;

            public void Init(float pos, float vel)
            {
                position  = pos;
                _velocity = vel;
                _pPos     = 1f;
                _pVel     = 1f;
            }

            public void Predict(float dt, float Q)
            {
                position += _velocity * dt;
                _pPos    += _pVel * dt * dt + Q * dt;
                _pVel    += Q * dt;
            }

            public void Update(float measPos, float measVel, float dt, float Q, float R)
            {
                position += _velocity * dt;
                _pPos    += _pVel * dt * dt + Q * dt;
                _pVel    += Q * dt;

                float Kp  = _pPos / (_pPos + R);
                position += Kp * (measPos - position);
                _pPos    *= (1f - Kp);

                float Kv   = _pVel / (_pVel + R * 4f);
                _velocity += Kv * (measVel - _velocity);
                _pVel     *= (1f - Kv);
            }
        }
    }
}
