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

            // Broadcast to all ready connections
            NetworkServer.SendToAll(msg);
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
        }

        void OnSnapshotMessage(SnapshotMessage msg)
        {
            var snap = new NetworkSimulator.Snapshot
            {
                timestamp = msg.timestamp,
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
