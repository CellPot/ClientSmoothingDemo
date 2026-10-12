using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// Mirror-based transport implementing INetworkTransport.
    /// - On server: Send() broadcasts Snapshot to all clients using a Mirror NetworkMessage.
    /// - On client: receives Snapshot messages and forwards them to registered listeners.
    /// Note: Use Mirror's built-in NetworkManager and NetworkManagerHUD to start Host/Client.
    /// Assign this component to ServerEntity/BaseClientEntity transportBehaviour fields in the scene.
    /// </summary>
    public class MirrorNetworkTransport : MonoBehaviour, INetworkTransport, ITruthSender, IReferenceSource
    {
        // Mirror message carrying the snapshot data
        public struct SnapshotMessage : NetworkMessage
        {
            public float timestamp;
            public Vector3 position;
            public Vector3 velocity;
        }

        // High-rate ground-truth stream used only for the error metric
        public struct TruthMessage : NetworkMessage
        {
            public float timestamp;
            public Vector3 position;
        }

        [Tooltip("Measure error against the server's true path at the matching local time instead of the live (replicated) server transform.")]
        public bool latencyAdjustedReference = true;

        private const float TruthHistorySeconds = 3f;
        private const float MaxExtrapolation = 0.1f;
        private readonly List<(float t, Vector3 p)> _truth = new List<(float, Vector3)>();

        private readonly List<Action<NetworkSimulator.Snapshot>> _listeners = new List<Action<NetworkSimulator.Snapshot>>();
        private bool _clientHandlerRegistered;

        // Clock alignment: server timestamps use the server's Time.time, which differs per process
        // (e.g. ParrelSync clone). We estimate offset = localArrival - serverTimestamp as the minimum
        // over a rolling window (the min sample ~ the least-delayed packet), then rebase timestamps
        // onto the local clock so techniques can keep comparing them against Time.time.
        private const int OffsetWindow = 100;
        private float _offset;
        private float _windowMin;
        private int _windowCount;
        private bool _hasOffset;

        // Unreliable delivery can reorder/duplicate; drop stale snapshots (but accept a big
        // backwards jump as a new server session).
        private float _lastRawTimestamp;
        private bool _hasLast;

        void OnEnable()
        {
            RegisterClientHandler();
        }

        void OnDisable()
        {
            UnregisterClientHandler();
        }

        public void RegisterListener(Action<NetworkSimulator.Snapshot> listener)
        {
            if (!_listeners.Contains(listener))
                _listeners.Add(listener);
            // ensure handler is set when the first listener is added at runtime
            RegisterClientHandler();
        }

        public void UnregisterListener(Action<NetworkSimulator.Snapshot> listener)
        {
            _listeners.Remove(listener);
            if (_listeners.Count == 0)
                UnregisterClientHandler();
        }

        public void Send(NetworkSimulator.Snapshot snapshot)
        {
            // Only the server should broadcast snapshots to clients
            if (!NetworkServer.active)
            {
                // Silently ignore if not running as server to keep behavior simple in editor tests
                return;
            }

            SnapshotMessage msg = new SnapshotMessage
            {
                timestamp = snapshot.timestamp,
                position = snapshot.position,
                velocity = snapshot.velocity
            };

            // Broadcast to all ready connections. Unreliable: retransmits would add head-of-line
            // blocking and hide the loss/jitter the smoothing techniques are meant to handle.
            NetworkServer.SendToAll(msg, Channels.Unreliable);
        }

        public void SendTruth(float timestamp, Vector3 position)
        {
            if (!NetworkServer.active) return;
            NetworkServer.SendToAll(new TruthMessage { timestamp = timestamp, position = position }, Channels.Unreliable);
        }

        public bool TryGetTruth(float localTime, out Vector3 position)
        {
            position = default;
            int n = _truth.Count;
            if (!latencyAdjustedReference || n < 2 || localTime < _truth[0].t) return false;

            var last = _truth[n - 1];
            if (localTime >= last.t)
            {
                // Newest data is slightly behind "now": extrapolate a little along the last segment.
                var prev = _truth[n - 2];
                float span = last.t - prev.t;
                Vector3 vel = span > 0f ? (last.p - prev.p) / span : Vector3.zero;
                position = last.p + vel * Mathf.Min(localTime - last.t, MaxExtrapolation);
                return true;
            }

            for (int i = n - 2; i >= 0; i--)
            {
                if (_truth[i].t > localTime) continue;
                float span = _truth[i + 1].t - _truth[i].t;
                float k = span > 0f ? (localTime - _truth[i].t) / span : 1f;
                position = Vector3.Lerp(_truth[i].p, _truth[i + 1].p, k);
                return true;
            }
            return false;
        }

        // Register client-side handler once
        void RegisterClientHandler()
        {
            if (_clientHandlerRegistered)
                return;

            // Register a persistent handler; Mirror will queue messages after client connects
            NetworkClient.RegisterHandler<SnapshotMessage>(OnSnapshotMessage, false);
            NetworkClient.RegisterHandler<TruthMessage>(OnTruthMessage, false);
            _clientHandlerRegistered = true;
        }

        void UnregisterClientHandler()
        {
            if (!_clientHandlerRegistered)
                return;

            NetworkClient.UnregisterHandler<SnapshotMessage>();
            NetworkClient.UnregisterHandler<TruthMessage>();
            _truth.Clear();
            _clientHandlerRegistered = false;
            _hasOffset = false;
            _hasLast = false;
        }

        void OnTruthMessage(TruthMessage msg)
        {
            if (!_hasOffset) return; // clock offset comes from the first snapshot

            float t = msg.timestamp + _offset;
            int i = _truth.Count;
            while (i > 0 && _truth[i - 1].t > t) i--; // keep sorted despite reordering
            _truth.Insert(i, (t, msg.position));

            float cutoff = _truth[_truth.Count - 1].t - TruthHistorySeconds;
            int drop = 0;
            while (drop < _truth.Count - 1 && _truth[drop].t < cutoff) drop++;
            if (drop > 0) _truth.RemoveRange(0, drop);
        }

        void OnSnapshotMessage(SnapshotMessage msg)
        {
            if (_hasLast && msg.timestamp <= _lastRawTimestamp && _lastRawTimestamp - msg.timestamp < 1f)
                return;
            _lastRawTimestamp = msg.timestamp;
            _hasLast = true;

            float sample = Time.time - msg.timestamp;
            if (!_hasOffset || sample < _windowMin - 1f)
            {
                // First packet or a new server session: hard reset.
                _offset = _windowMin = sample;
                _windowCount = 0;
                _hasOffset = true;
                _truth.Clear();
            }
            else
            {
                _windowMin = Mathf.Min(_windowMin, sample);
                if (++_windowCount >= OffsetWindow)
                {
                    _offset = _windowMin;
                    _windowMin = sample;
                    _windowCount = 0;
                }
                else
                {
                    _offset = Mathf.Min(_offset, sample);
                }
            }

            var snap = new NetworkSimulator.Snapshot
            {
                timestamp = msg.timestamp + _offset,
                position = msg.position,
                velocity = msg.velocity
            };

            // Deliver to all registered listeners
            for (int i = 0; i < _listeners.Count; i++)
            {
                try { _listeners[i]?.Invoke(snap); }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            }
        }
    }
}
