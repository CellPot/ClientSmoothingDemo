using System;
using System.Collections.Generic;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// Emulates network conditions: latency, jitter, and packet loss.
    /// Delivers "server snapshots" to registered listeners with simulated delay.
    /// </summary>
    public class NetworkSimulator : MonoBehaviour, INetworkTransport
    {
        [Header("Network Conditions")] [Range(0f, 500f)]
        public float baseLatencyMs = 100f;

        [Range(0f, 200f)] public float jitterMs = 30f;
        [Range(0f, 1f)] public float packetLossRate = 0.05f;
        [Range(1f, 64f)] public float sendRateHz = 20f;

        [Header("Debug")] public bool showDebugGUI = true;

        // Snapshot definition
        public struct Snapshot
        {
            public float timestamp;
            public Vector3 position;
            public Vector3 velocity;
        }

        // Internal pending delivery
        private struct PendingPacket
        {
            public Snapshot snapshot;
            public float deliverAt; // Time.time when it should arrive
        }

        private List<PendingPacket> _pending = new List<PendingPacket>();
        private List<Action<Snapshot>> _listeners = new List<Action<Snapshot>>();

        // Stats
        [HideInInspector] public float lastMeasuredLatency;
        [HideInInspector] public int packetsDroppedTotal;
        [HideInInspector] public int packetsSentTotal;


        public void RegisterListener(Action<Snapshot> listener)
        {
            if (!_listeners.Contains(listener))
                _listeners.Add(listener);
        }

        public void UnregisterListener(Action<Snapshot> listener)
        {
            _listeners.Remove(listener);
        }

        /// <summary>Called by the Server object each tick to broadcast state.</summary>
        public void Send(Snapshot snapshot)
        {
            packetsSentTotal++;

            // Packet loss
            if (UnityEngine.Random.value < packetLossRate)
            {
                packetsDroppedTotal++;
                return;
            }

            float jitter = UnityEngine.Random.Range(-jitterMs, jitterMs);
            float delay = Mathf.Max(0f, (baseLatencyMs + jitter) / 1000f);
            lastMeasuredLatency = delay * 1000f;

            _pending.Add(new PendingPacket
            {
                snapshot = snapshot,
                deliverAt = Time.time + delay
            });
        }

        void Update()
        {
            float now = Time.time;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (now >= _pending[i].deliverAt)
                {
                    var snap = _pending[i].snapshot;
                    foreach (var listener in _listeners)
                        listener?.Invoke(snap);
                    _pending.RemoveAt(i);
                }
            }
        }

        void OnGUI()
        {
            if (!showDebugGUI) return;
            GUI.Label(new Rect(10, 10, 300, 20),
                $"Latency: {baseLatencyMs:F0}ms  Jitter: ±{jitterMs:F0}ms  Loss: {packetLossRate * 100:F0}%  Rate: {sendRateHz:F0}Hz");
        }
    }
}