using System.Collections.Generic;
using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE 7 — Client-Side Prediction + Server Reconciliation
    /// The client immediately applies its own simulated movement each frame ("prediction"),
    /// then when the server confirms a state, the client rewinds to that confirmed state
    /// and replays all unacknowledged inputs on top of it.
    /// This gives zero perceived input latency while staying authoritative-server correct.
    /// Most common in FPS / action games (Quake, Overwatch, Rocket League).
    /// </summary>
    using System.Collections.Generic;
    using UnityEngine;

    public class ServerReconciliationClient : BaseClientEntity
    {
        [Range(1f, 20f)] public float correctionBlend = 12f;

        private struct InputRecord
        {
            public float timestamp;
            public Vector3 delta;
        }

        private List<InputRecord> _inputHistory = new List<InputRecord>();
        private Vector3 _predictedPosition;
        private Vector3 _confirmedPosition;
        private Vector3 _confirmedVelocity;
        private float _timeSinceConfirm;
        private bool _initialized;

        void Awake()
        {
            techniqueName = "Client Prediction + Reconciliation";
            color = new Color(1f, 1f, 0.2f);
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            if (!_initialized)
            {
                _predictedPosition = snap.position;
                _confirmedPosition = snap.position;
                _confirmedVelocity = snap.velocity;
                _initialized = true;
                return;
            }

            _confirmedPosition = snap.position;
            _confirmedVelocity = snap.velocity;
            _timeSinceConfirm = 0f;

            // Replay unacknowledged inputs on top of confirmed server position
            _inputHistory.RemoveAll(r => r.timestamp <= snap.timestamp);
            Vector3 reconciled = _confirmedPosition;
            foreach (var record in _inputHistory)
                reconciled += record.delta;

            _predictedPosition = Vector3.Lerp(_predictedPosition, reconciled, correctionBlend * Time.deltaTime);
        }

        protected override void UpdatePosition()
        {
            if (!_initialized) return;

            if (server.playerControlled)
            {
                // Mirror the same input the server is processing
                Vector3 input = new Vector3(
                    Input.GetAxisRaw("Horizontal"),
                    Input.GetAxisRaw("Vertical"),
                    0f
                );
                Vector3 delta = input.normalized * (server.moveSpeed * server.timeScale * Time.deltaTime);

                if (delta != Vector3.zero)
                {
                    _predictedPosition += delta;
                    _inputHistory.Add(new InputRecord
                    {
                        // Stamp as "when server will confirm this" = now + RTT
                        timestamp = Time.time + (NetworkSimulator.Instance.baseLatencyMs * 2f / 1000f),
                        delta = delta
                    });
                    if (_inputHistory.Count > 256)
                        _inputHistory.RemoveAt(0);
                }
            }
            else
            {
                // Automatic path — predict forward with server velocity
                _timeSinceConfirm += Time.deltaTime;
                Vector3 predicted = _confirmedPosition + _confirmedVelocity * _timeSinceConfirm;
                _predictedPosition = Vector3.Lerp(_predictedPosition, predicted, correctionBlend * Time.deltaTime);
            }

            transform.position = _predictedPosition;
        }
    }
}