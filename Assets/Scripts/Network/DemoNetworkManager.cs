using Mirror;
using UnityEngine;

namespace Network
{
// Place this script anywhere under Assets/ (e.g., Assets/Scripts/Network/DemoNetworkManager.cs)
// Use this when your ServerEntity is placed directly in the scene (not spawned as a prefab).
    public class DemoNetworkManager : NetworkManager
    {
        public GameObject serverEntityPrefab; // assign & add to Spawnable Prefabs

        // Register spawnable prefab as early as possible so remote clients can
        // instantiate it when the server calls NetworkServer.Spawn.
        public override void Awake()
        {
            base.Awake();
            if (serverEntityPrefab != null)
            {
                // Ensure it's in the spawnPrefabs list too (helps in Editor play mode reloads)
                if (!spawnPrefabs.Contains(serverEntityPrefab))
                {
                    spawnPrefabs.Add(serverEntityPrefab);
                    Debug.Log("[DemoNetworkManager] Added serverEntityPrefab to spawnPrefabs at Awake().");
                }
            }
            else
            {
                Debug.LogWarning("[DemoNetworkManager] serverEntityPrefab is not assigned at Awake().");
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (serverEntityPrefab == null)
            {
                Debug.LogError("[DemoNetworkManager] serverEntityPrefab is not assigned. Cannot spawn on server.");
                return;
            }

            var go = Instantiate(serverEntityPrefab);
            NetworkServer.Spawn(go);
            Debug.Log("[DemoNetworkManager] Spawned ServerEntity prefab on server.");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // No explicit NetworkClient.RegisterPrefab calls here.
            // Mirror will auto-register from spawnPrefabs via RegisterClientMessages().
            if (serverEntityPrefab == null)
            {
                Debug.LogWarning("[DemoNetworkManager] serverEntityPrefab is not assigned on client. Add it to Spawnable Prefabs.");
            }
        }

        public override void OnClientConnect()
        {
            // Keep default behavior; do not double-register prefabs here.
            base.OnClientConnect();
        }

        public override void OnServerReady(Mirror.NetworkConnectionToClient conn)
        {
            base.OnServerReady(conn);
            // Not strictly needed for prefab-spawn path, but harmless. Helps if any scene objects exist.
            NetworkServer.SpawnObjects();
        }

        public override void OnStopClient()
        {
            // No explicit unregister to avoid interfering with Mirror's internal lists.
            base.OnStopClient();
        }

        void OnDisable()
        {
            // Nothing to do. We keep spawnPrefabs as the source of truth.
        }
    }
}