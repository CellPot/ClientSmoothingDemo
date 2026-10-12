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
    public class MirrorNetworkTransport : MonoBehaviour, INetworkTransport
    {
        // Mirror message carrying the snapshot data
        public struct SnapshotMessage : NetworkMessage
        {
            public float timestamp;
            public Vector3 position;
            public Vector3 velocity;
        }

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

        // Register client-side handler once
        void RegisterClientHandler()
        {
            if (_clientHandlerRegistered)
                return;

            // Register a persistent handler; Mirror will queue messages after client connects
            NetworkClient.RegisterHandler<SnapshotMessage>(OnSnapshotMessage, false);
            _clientHandlerRegistered = true;
        }

        void UnregisterClientHandler()
        {
            if (!_clientHandlerRegistered)
                return;

            NetworkClient.UnregisterHandler<SnapshotMessage>();
            _clientHandlerRegistered = false;
            _hasOffset = false;
            _hasLast = false;
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
