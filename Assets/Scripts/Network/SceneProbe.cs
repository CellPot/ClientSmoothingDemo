using System.Linq;
using Techniques;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// Diagnostic: every 2 s for the first 30 s of play (any scene), writes what is alive and moving
    /// (time scale, server state, simulator counters, techniques) to Logs/netdiag.log.
    /// </summary>
    public class SceneProbe : MonoBehaviour
    {
        private float _next;
        private int _lines;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("SceneProbe") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<SceneProbe>();
        }

        void Update()
        {
            if (_lines >= 15 || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            _lines++;

            var servers = FindObjectsByType<ServerEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var sim = FindFirstObjectByType<NetworkSimulator>(FindObjectsInactive.Include);
            var clients = FindObjectsByType<BaseClientEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int receiving = clients.Count(c => c.snapshotsReceived > 0);

            NetDiagnostics.Write($"PROBE frame={Time.frameCount} timeScale={Time.timeScale} dt={Time.deltaTime:F4} " +
                                 $"servers={servers.Length} clients={clients.Length} clientsReceiving={receiving} " +
                                 (sim ? $"sim(enabled={sim.enabled} sent={sim.packetsSentTotal} dropped={sim.packetsDroppedTotal})" : "sim=none"));
            foreach (var s in servers) NetDiagnostics.Write("  SERVER " + s.DebugState());
            foreach (var c in clients.Take(2)) NetDiagnostics.Write("  CLIENT " + c.DebugState());
        }
    }
}
