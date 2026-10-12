using System;
using System.IO;
using System.Linq;
using Mirror;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// Writes networking facts to Logs/netdiag.log inside the running project (each editor / ParrelSync clone has
    /// its own file, timestamps are wall-clock so host and client logs can be lined up). Also mirrors every Unity
    /// log message, so Mirror errors end up in the same place.
    /// </summary>
    public static class NetDiagnostics
    {
        private static StreamWriter _writer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            try
            {
                string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
                Directory.CreateDirectory(dir);
                _writer?.Dispose();
                _writer = new StreamWriter(new FileStream(Path.Combine(dir, "netdiag.log"), FileMode.Create,
                    FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                Application.logMessageReceived -= OnLog;
                Application.logMessageReceived += OnLog;
                Write($"--- play session started, project={Application.dataPath}");
            }
            catch (Exception)
            {
                // diagnostics must never break the app
            }
        }

        public static void Write(string message)
        {
            try { _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}"); }
            catch (Exception) { }
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log)
            {
                Write(condition);
                return;
            }

            string firstLines = string.Join("\n    ", stackTrace.Split('\n').Take(4));
            Write($"[{type}] {condition}\n    {firstLines}");
        }

        public static string DescribeServerWorld()
        {
            if (!NetworkServer.active) return "(server not active)";
            string entities = string.Join("; ", NetworkServer.spawned.Values.Select(i =>
                $"{i.name} netId={i.netId} assetId={i.assetId} sceneId={i.sceneId} observers=[{string.Join(",", i.observers.Keys)}]"));
            return $"spawned: {(entities.Length == 0 ? "(none)" : entities)}";
        }
    }
}
