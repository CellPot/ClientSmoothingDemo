using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE 3 — Dead Reckoning
    /// Uses the last known position + velocity to predict where the entity *should*
    /// be right now, then snaps/blends back to reality when a new snapshot arrives.
    /// Great for physics-based movement; diverges on sudden direction changes.
    /// </summary>
    public class DeadReckoningClient : BaseClientEntity
    {
        [Header("Settings")]
        [Range(1f, 20f)]  public float correctionSpeed = 8f;
        [Tooltip("Max prediction time before we stop extrapolating (avoids runaway).")]
        [Range(0.1f, 1f)] public float maxPredictTime = 0.5f;

        private Vector3 _predictedPosition;
        private Vector3 _velocity;
        private float   _timeSinceSnapshot;
        private bool    _initialized;

        void Awake()
        {
            techniqueName = "Dead Reckoning";
            color = new Color(1f, 0.5f, 0f); // orange
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            // Blend from predicted position back toward confirmed server position
            if (_initialized)
            {
                _predictedPosition = Vector3.Lerp(_predictedPosition, snap.position, 0.5f);
            }
            else
            {
                _predictedPosition = snap.position;
                _initialized = true;
            }

            _velocity          = snap.velocity;
            _timeSinceSnapshot = 0f;
        }

        protected override void UpdatePosition()
        {
            if (!_initialized) return;

            _timeSinceSnapshot += Time.deltaTime;
            float dt = Mathf.Min(_timeSinceSnapshot, maxPredictTime);

            // Extrapolate using velocity
            Vector3 extrapolated = _predictedPosition + _velocity * dt;

            // Smooth toward the extrapolated position
            transform.position = Vector3.Lerp(transform.position, extrapolated, correctionSpeed * Time.deltaTime);
        }
    }
}
