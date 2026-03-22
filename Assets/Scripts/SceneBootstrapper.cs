using System.Collections.Generic;
using Network;
using Techniques;
using UI;
using UnityEngine;

/// <summary>
/// Bootstraps the entire demo scene at runtime — no manual scene wiring needed.
/// Attach this script to an empty GameObject named "_Bootstrapper" in an empty scene.
/// It creates the camera, server entity, all client entities, trails, UI, and
/// network simulator programmatically.
/// </summary>
public class SceneBootstrapper : MonoBehaviour
{
    void Awake()
    {
        SetupCamera();
        var netSim  = SetupNetworkSimulator();
        var server  = SetupServer();
        var clients = SetupClients(server);
        SetupUI(netSim, server, clients);
    }

    // ── Camera ────────────────────────────────────────────────────────────────

    static void SetupCamera()
    {
        var cam = Camera.main ?? new GameObject("Main Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.transform.position = new Vector3(0f, 0f, -12f);
        cam.orthographic = true;
        cam.orthographicSize = 6f;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.1f);
        cam.clearFlags = CameraClearFlags.SolidColor;
    }

    // ── Network simulator ─────────────────────────────────────────────────────

    static NetworkSimulator SetupNetworkSimulator()
    {
        var go  = new GameObject("NetworkSimulator");
        var sim = go.AddComponent<NetworkSimulator>();
        sim.baseLatencyMs  = 100f;
        sim.jitterMs       = 30f;
        sim.packetLossRate = 0.05f;
        sim.sendRateHz     = 20f;
        sim.showDebugGUI   = false; // UI manager handles display
        return sim;
    }

    // ── Server ────────────────────────────────────────────────────────────────

    static ServerEntity SetupServer()
    {
        var go = new GameObject("Server_TruthEntity");

        // Visual: white diamond sprite
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CreateDiamondSprite(Color.white);
        sr.sortingOrder = 10;
        go.transform.localScale = Vector3.one * 0.45f;

        // Trail on server so you can see the true path
        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 3f;
        trail.startWidth = 0.08f;
        trail.endWidth   = 0.01f;
        trail.material   = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = new Color(1f, 1f, 1f, 0.6f);
        trail.endColor   = new Color(1f, 1f, 1f, 0f);

        var server = go.AddComponent<ServerEntity>();
        server.addRandomKicks = true;
        return server;
    }

    // ── Clients ───────────────────────────────────────────────────────────────

    static List<BaseClientEntity> SetupClients(ServerEntity server)
    {
        var list = new List<BaseClientEntity>();

        // (type, name, color) tuples
        var defs = new (System.Type type, string name, Color color)[]
        {
            (typeof(NaiveClient),                "Naive (No Smoothing)",           Color.red),
            (typeof(ClientInterpolationClient),  "Client-Side Interpolation",      Color.yellow),
            (typeof(SnapshotInterpolationClient),"Snapshot Interpolation",          Color.cyan),
            (typeof(DeadReckoningClient),        "Dead Reckoning",                 new Color(1f,0.5f,0f)),
            (typeof(ExponentialSmoothingClient), "Exponential Smoothing",           Color.magenta),
            (typeof(KalmanFilterClient),         "Kalman Filter",                  new Color(0f,1f,0.5f)),
            (typeof(JitterBufferClient),         "Jitter Buffer",                  new Color(0.5f,0.5f,1f)),
            (typeof(ServerReconciliationClient), "Client Prediction+Reconciliation",new Color(.6f,1f,0.2f)),
            (typeof(JitterBufferWithDeadReckoning), "Jitter Buffer + Dead Reckoning", new Color(.6f, 0.9f, 1f)),
            (typeof(SnapshotInterpolationWithDeadReckoning), "Snapshot Interp + Dead Reckoning", new Color(0.2f, 0.4f, 0.2f)),
            (typeof(KalmanWithExponentialSmoothing),         "Kalman + Exponential Smoothing",    new Color(0.6f, 1f, 0.6f)),
        };

        foreach (var def in defs)
        {
            var go = new GameObject("Client_" + def.name);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite(def.color);
            sr.sortingOrder = 5;
            go.transform.localScale = Vector3.one * 0.3f;

            var client = (BaseClientEntity)go.AddComponent(def.type);
            client.techniqueName = def.name;
            client.color         = def.color;
            client.server        = server;

            go.AddComponent<TechniqueTrail>();

            list.Add(client);
        }

        return list;
    }

    // ── UI manager ────────────────────────────────────────────────────────────

    static void SetupUI(NetworkSimulator sim, ServerEntity server, List<BaseClientEntity> clients)
    {
        var go = new GameObject("UIManager");
        var ui = go.AddComponent<DemoUIManager>();
        ui.networkSim = sim;
        ui.server     = server;
        ui.clients    = clients;
    }

    // ── Sprite helpers ────────────────────────────────────────────────────────

    static Sprite CreateCircleSprite(Color color)
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = Vector2.one * (size / 2f);
        float radius = size / 2f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
    }

    static Sprite CreateDiamondSprite(Color color)
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = Vector2.one * (size / 2f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - center.x) / (size / 2f);
                float dy = Mathf.Abs(y - center.y) / (size / 2f);
                float alpha = Mathf.Clamp01(1f - (dx + dy) + 0.05f) > 0.05f ? 1f : 0f;
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
    }
}
