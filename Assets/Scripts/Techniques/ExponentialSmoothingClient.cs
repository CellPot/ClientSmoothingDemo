using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE 4 — Exponential Smoothing (Low-Pass Filter)
    /// Each frame blends the current position toward the target using a fixed alpha.
    /// Extremely simple, zero-overhead, suitable for UI and non-physics values.
    /// Higher alpha = more responsive but jittery. Lower = smoother but lags more.
    /// </summary>
    public class ExponentialSmoothingClient : BaseClientEntity
    {
        [Header("Settings")] [Range(0.01f, 0.99f)] [Tooltip("Blend factor per second. 0 = frozen, 1 = instant snap.")]
        public float smoothingAlpha = 0.85f;

        private Vector3 _smoothed;
        private Vector3 _target;
        private bool _initialized;

        protected override void Awake()
        {
            techniqueName = "Exponential Smoothing";
            color = Color.magenta;
            base.Awake();
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            _target = snap.position;
            if (!_initialized)
            {
                _smoothed = snap.position;
                _initialized = true;
            }
        }

        protected override void UpdatePosition()
        {
            if (!_initialized) return;
            // was: NetworkSimulator.Instance.sendRateHz
            float alpha = 1f - Mathf.Pow(1f - smoothingAlpha, Time.deltaTime * transport.sendRateHz);
            _smoothed = Vector3.Lerp(_smoothed, _target, alpha);
            transform.position = _smoothed;
        }
    }
}