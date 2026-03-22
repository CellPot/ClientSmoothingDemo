using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// Base class for all client smoothing technique implementations.
    /// Handles snapshot registration, position error tracking, and display.
    /// </summary>
    public abstract class BaseClientEntity : MonoBehaviour
    {
        [Header("Display")]
        public string techniqueName = "Base";
        public Color color = Color.white;
        public ServerEntity server;

        // Error metrics
        [HideInInspector] public float currentError;
        [HideInInspector] public float averageError;
        [HideInInspector] public float maxError;

        private float _errorAccum;
        private int   _errorSamples;

        protected NetworkSimulator.Snapshot latestSnapshot;
        protected bool hasSnapshot;

        protected virtual void OnEnable()
        {
            NetworkSimulator.Instance.RegisterListener(OnSnapshotReceived);
        }

        protected virtual void OnDisable()
        {
            if (NetworkSimulator.Instance != null)
                NetworkSimulator.Instance.UnregisterListener(OnSnapshotReceived);
        }

        private void OnSnapshotReceived(NetworkSimulator.Snapshot snap)
        {
            latestSnapshot = snap;
            hasSnapshot = true;
            OnSnapshot(snap);
        }

        /// <summary>Override to handle incoming snapshot for your technique.</summary>
        protected abstract void OnSnapshot(NetworkSimulator.Snapshot snap);

        protected virtual void Update()
        {
            UpdatePosition();
            TrackError();
        }

        /// <summary>Override to set transform.position each frame.</summary>
        protected abstract void UpdatePosition();

        private void TrackError()
        {
            if (server == null) return;
            currentError = Vector3.Distance(transform.position, server.TruePosition);
            _errorAccum  += currentError;
            _errorSamples++;
            averageError  = _errorAccum / _errorSamples;
            if (currentError > maxError) maxError = currentError;
        }

        public void ResetMetrics()
        {
            currentError = averageError = maxError = 0f;
            _errorAccum  = 0f;
            _errorSamples = 0;
        }
    }
}
