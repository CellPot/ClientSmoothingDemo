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
        [Range(0.001f, 5f)] public float processNoise = 1f;

        [Tooltip("Measurement noise — distrust of each server snapshot. " +
                 "Higher = smoother output but more lag behind truth.")]
        [Range(0.001f, 5f)] public float measurementNoise = 0.05f;

        [Tooltip("Longest the estimate is extrapolated past the last snapshot (seconds).")]
        [Range(0.05f, 1f)] public float maxExtrapolation = 0.3f;

        private KalmanAxis _kx, _ky, _kz;
        private bool _initialized;

        protected override void Awake()
        {
            techniqueName = "Kalman Filter";
            color = new Color(0f, 1f, 0.5f);
            base.Awake();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _kx = new KalmanAxis();
            _ky = new KalmanAxis();
            _kz = new KalmanAxis();
            _initialized = false;
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            // First snapshot, or a gap so long the old state is meaningless: restart from the measurement.
            if (!_initialized || snap.timestamp - _kx.StateTime > 0.5f)
            {
                _kx.Init(snap.position.x, snap.velocity.x, snap.timestamp);
                _ky.Init(snap.position.y, snap.velocity.y, snap.timestamp);
                _kz.Init(snap.position.z, snap.velocity.z, snap.timestamp);
                _initialized = true;
                return;
            }

            _kx.Update(snap.position.x, snap.velocity.x, snap.timestamp, processNoise, measurementNoise);
            _ky.Update(snap.position.y, snap.velocity.y, snap.timestamp, processNoise, measurementNoise);
            _kz.Update(snap.position.z, snap.velocity.z, snap.timestamp, processNoise, measurementNoise);
        }

        protected override void UpdatePosition()
        {
            if (!_initialized) return;

            float t = Mathf.Min(Time.time, _kx.StateTime + maxExtrapolation);
            transform.position = new Vector3(_kx.PositionAt(t), _ky.PositionAt(t), _kz.PositionAt(t));
        }
    }
}
