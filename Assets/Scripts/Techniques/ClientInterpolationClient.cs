using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE 1 — Client-Side Interpolation
    /// Smoothly moves the visual position toward the latest received server position
    /// each frame. Simple but adds perceived latency equal to the lerp speed.
    /// </summary>
    public class ClientInterpolationClient : BaseClientEntity
    {
        [Header("Settings")]
        [Range(1f, 30f)] public float lerpSpeed = 10f;

        // Exponential chase of the latest position: steady-state lag is 1/lerpSpeed seconds.
        public override float AddedDelaySeconds => 1f / lerpSpeed;

        private Vector3 _targetPosition;

        protected override void Awake()
        {
            techniqueName = "Client-Side Interpolation";
            color = Color.yellow;
            base.Awake();
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            _targetPosition = snap.position;
        }

        protected override void UpdatePosition()
        {
            if (!hasSnapshot) return;
            transform.position = Vector3.Lerp(transform.position, _targetPosition, lerpSpeed * Time.deltaTime);
        }
    }
}
