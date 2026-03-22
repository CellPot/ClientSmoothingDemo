using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE 5 — Kalman Filter (1D applied per axis)
    /// Statistically optimal estimation combining a motion model prediction with
    /// noisy measurements. Adapts confidence based on process/measurement noise.
    /// Excellent for irregular update rates and sensor-like inputs.
    /// </summary>
    public class KalmanFilterClient : BaseClientEntity
    {
        [Header("Settings")]
        [Tooltip("Process noise — uncertainty added per second to the motion model. " +
                 "Higher = reacts faster to direction changes but jitterier.")]
        [Range(0.001f, 2f)] public float processNoise = 0.05f;

        [Tooltip("Measurement noise — distrust of each server snapshot. " +
                 "Higher = smoother output but more lag behind truth.")]
        [Range(0.001f, 5f)] public float measurementNoise = 0.8f;

        private KalmanAxis _kx, _ky, _kz;
        private bool  _initialized;
        private float _lastSnapshotTime;
        private float _timeSinceSnapshot;

        void Awake()
        {
            techniqueName = "Kalman Filter";
            color = new Color(0f, 1f, 0.5f);
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
                _lastSnapshotTime  = snap.timestamp;
                _timeSinceSnapshot = 0f;
                _initialized = true;
                return;
            }

            // Use server-side timestamp delta — not arrival time — so jitter doesn't corrupt dt
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

            // Predict forward each frame; stop if no packet arrived for too long
            if (_timeSinceSnapshot < 0.5f)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                _kx.Predict(dt, processNoise);
                _ky.Predict(dt, processNoise);
                _kz.Predict(dt, processNoise);
            }

            transform.position = new Vector3(_kx.position, _ky.position, _kz.position);
        }

        // ── Inner Kalman axis ─────────────────────────────────────────────────────

        private class KalmanAxis
        {
            public  float position;
            private float _velocity;
            private float _pPos = 1f; // position error covariance
            private float _pVel = 1f; // velocity error covariance

            public void Init(float pos, float vel)
            {
                position  = pos;
                _velocity = vel;
                _pPos     = 1f;
                _pVel     = 1f;
            }

            /// <summary>Predict step — moves the estimate forward by dt without a measurement.</summary>
            public void Predict(float dt, float Q)
            {
                position += _velocity * dt;
                _pPos    += _pVel * dt * dt + Q * dt;
                _pVel    += Q * dt;
            }

            /// <summary>Full predict + correct — called when a snapshot arrives.</summary>
            public void Update(float measPos, float measVel, float dt, float Q, float R)
            {
                // Predict to snapshot timestamp first
                position += _velocity * dt;
                _pPos    += _pVel * dt * dt + Q * dt;
                _pVel    += Q * dt;

                // Correct position
                float Kp  = _pPos / (_pPos + R);
                position += Kp * (measPos - position);
                _pPos    *= (1f - Kp);

                // Correct velocity — higher R because velocity is a noisier derivative
                float Kv   = _pVel / (_pVel + R * 4f);
                _velocity += Kv * (measVel - _velocity);
                _pVel     *= (1f - Kv);
            }
        }
    }
}
