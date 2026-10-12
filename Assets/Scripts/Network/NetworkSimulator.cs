using System;
using System.Collections.Generic;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// Emulates network conditions: latency, jitter, and packet loss.
    /// Delivers "server snapshots" to registered listeners with simulated delay.
    /// </summary>
    public class NetworkSimulator : MonoBehaviour, INetworkTransport, ITruthSender, IReferenceSource
    {
        [Tooltip("Measure error against the server's true path delayed by the minimum one-way latency (same reference as the Mirror scene) instead of the live server transform.")]
        public bool latencyAdjustedReference = true;

        private readonly TruthHistory _truth = new TruthHistory();

        [Header("Network Conditions")] [Range(0f, 500f)]
        public float baseLatencyMs = 100f;

        [Range(0f, 200f)] public float jitterMs = 30f;
        [Range(0f, 1f)] public float packetLossRate = 0.05f;
        [Tooltip("Fraction of packets inserted at a random place in the delivery queue (like Mirror's LatencySimulation 'scramble').")]
        [Range(0f, 1f)] public float scrambleRate = 0f;
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
            public float rawTimestamp; // server timestamp before the clock rebase, for the stale check
        }

        // Like MirrorNetworkTransport: unreliable delivery can reorder, so stale snapshots are dropped
        // (but a big backwards jump counts as a new session).
        private float _lastRawTimestamp;
        private bool _hasLast;

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

            float raw = snapshot.timestamp;

            // Rebase onto the "client clock" like MirrorNetworkTransport does (timestamp + minimum one-way latency),
            // so buffered techniques and the truth reference share one timeline.
            snapshot.timestamp += MinDelaySeconds;

            var packet = new PendingPacket
            {
                snapshot = snapshot,
                deliverAt = Time.time + delay,
                rawTimestamp = raw
            };

            // Same as LatencySimulation: a scrambled packet goes to a random place in the queue, and ready packets
            // are flushed in queue order, so it can overtake (or be overtaken by) others that become ready together.
            bool scramble = UnityEngine.Random.value < scrambleRate;
            int index = scramble ? UnityEngine.Random.Range(0, _pending.Count + 1) : _pending.Count;
            _pending.Insert(index, packet);
        }

        // The Mirror client rebases server time with the least-delayed packet's offset, so its reference is the
        // server path shifted by the minimum one-way latency. Do the same here to keep both scenes comparable.
        public void SendTruth(float timestamp, Vector3 position)
        {
            _truth.Add(timestamp + MinDelaySeconds, position);
        }

        private float MinDelaySeconds => Mathf.Max(0f, baseLatencyMs - jitterMs) / 1000f;


        public bool TryGetTruth(float localTime, out Vector3 position)
        {
            position = default;
            return latencyAdjustedReference && _truth.TryGet(localTime, out position);
        }

        void Update()
        {
            float now = Time.time;
            for (int i = 0; i < _pending.Count; )
            {
                if (now < _pending[i].deliverAt)
                {
                    i++;
                    continue;
                }

                var packet = _pending[i];
                _pending.RemoveAt(i);

                if (_hasLast && packet.rawTimestamp <= _lastRawTimestamp && _lastRawTimestamp - packet.rawTimestamp < 1f)
                    continue; // stale (reordered)
                _lastRawTimestamp = packet.rawTimestamp;
                _hasLast = true;

                foreach (var listener in _listeners)
                    listener?.Invoke(packet.snapshot);
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