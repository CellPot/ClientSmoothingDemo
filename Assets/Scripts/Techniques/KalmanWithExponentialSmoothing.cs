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
        [Range(0.001f, 5f)] public float processNoise     = 1f;
        [Range(0.001f, 5f)] public float measurementNoise = 0.05f;
        [Range(0.05f, 1f)]  public float maxExtrapolation = 0.3f;

        [Header("Visual Smoothing")]
        [Tooltip("How quickly the visual follows the Kalman estimate. " +
                 "Lower = smoother but more lag. Higher = snappier.")]
        [Range(1f, 30f)] public float smoothSpeed = 20f;

        // A first-order lag chasing a moving target trails it by 1/k seconds.
        public override float AddedDelaySeconds => 1f / smoothSpeed;

        private KalmanAxis _kx, _ky, _kz;
        private Vector3 _visualPos;           // smoothed visual output
        private bool    _initialized;

        protected override void Awake()
        {
            techniqueName = "Kalman + Exponential Smoothing";
            color = new Color(0.6f, 1f, 0.6f);
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
            if (!_initialized || snap.timestamp - _kx.StateTime > 0.5f)
            {
                _kx.Init(snap.position.x, snap.velocity.x, snap.timestamp);
                _ky.Init(snap.position.y, snap.velocity.y, snap.timestamp);
                _kz.Init(snap.position.z, snap.velocity.z, snap.timestamp);
                _visualPos   = snap.position;
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
            var kalmanEstimate = new Vector3(_kx.PositionAt(t), _ky.PositionAt(t), _kz.PositionAt(t));

            // Smoothing is applied to the visual only, never fed back into the filter.
            _visualPos = Vector3.Lerp(_visualPos, kalmanEstimate, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime));
            transform.position = _visualPos;
        }
    }
}
