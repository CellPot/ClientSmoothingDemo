using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Mirror;
using Network;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UI
{
    /// <summary>
    /// Repeatable benchmark: for every preset, restarts the server's seeded path, waits for things to settle,
    /// then records each technique's error every frame for <see cref="Duration"/> seconds.
    /// Results are shown in the UI and written to a CSV file.
    /// </summary>
    public class BenchmarkRunner : MonoBehaviour
    {
        public const float Warmup = 3f;
        public const float Duration = 20f;
        public const int Seed = 12345;

        public struct Row
        {
            public string technique;
            public float mean, p95, max, delayMs;
        }

        public class PresetResult
        {
            public NetPresets.Preset preset;
            public float rxHz;
            public List<Row> rows = new List<Row>();
        }

        public bool Running { get; private set; }
        public string Status { get; private set; } = "";
        public string LastFile { get; private set; }
        public readonly List<PresetResult> Results = new List<PresetResult>();

        private DemoUIManager _ui;
        private List<List<float>> _samples;
        private bool _recording;
        private Coroutine _routine;
        private NetConditions _restore;

        public void Init(DemoUIManager ui) => _ui = ui;

        public void Begin()
        {
            if (Running) return;
            if (_ui.server == null)
            {
                Status = "No server yet - start host/client first.";
                return;
            }

            _routine = StartCoroutine(Run());
        }

        public void Cancel()
        {
            if (!Running) return;
            StopCoroutine(_routine);
            Finish("Cancelled.");
        }

        void LateUpdate()
        {
            if (!_recording) return;
            for (int i = 0; i < _ui.clients.Count; i++)
            {
                var c = _ui.clients[i];
                if (c.gameObject.activeInHierarchy) _samples[i].Add(c.currentError);
            }
        }

        IEnumerator Run()
        {
            Running = true;
            Results.Clear();
            _restore = _ui.GetConditions();
            _ui.PrepareBenchmark();

            var clients = _ui.clients;
            _samples = new List<List<float>>();
            foreach (var _ in clients) _samples.Add(new List<float>());

            for (int p = 0; p < NetPresets.All.Length; p++)
            {
                var preset = NetPresets.All[p];
                string tag = $"[{p + 1}/{NetPresets.All.Length}] {preset.name}";

                _ui.ApplyConditions(preset.conditions);
                _ui.server.RequestPathReset(Seed);

                for (float t = 0; t < Warmup; t += Time.unscaledDeltaTime)
                {
                    Status = $"{tag}: settling {Warmup - t:F0}s";
                    yield return null;
                }

                var startCounts = new int[clients.Count];
                for (int i = 0; i < clients.Count; i++)
                {
                    clients[i].ResetMetrics();
                    _samples[i].Clear();
                    startCounts[i] = clients[i].snapshotsReceived;
                }

                _recording = true;
                for (float t = 0; t < Duration; t += Time.unscaledDeltaTime)
                {
                    Status = $"{tag}: recording {Duration - t:F0}s";
                    yield return null;
                }

                _recording = false;

                var result = new PresetResult { preset = preset };
                for (int i = 0; i < clients.Count; i++)
                {
                    result.rows.Add(Summarize(clients[i], _samples[i]));
                    if (i == 0) result.rxHz = (clients[i].snapshotsReceived - startCounts[i]) / Duration;
                }

                Results.Add(result);
            }

            Finish($"Done. {WriteCsv()}");
        }

        static Row Summarize(Techniques.BaseClientEntity c, List<float> samples)
        {
            var row = new Row { technique = c.techniqueName, delayMs = c.AddedDelaySeconds * 1000f };
            int n = samples.Count;
            if (n == 0) return row;

            var sorted = new List<float>(samples);
            sorted.Sort();
            double sum = 0;
            foreach (float s in samples) sum += s;
            row.mean = (float)(sum / n);
            row.p95 = sorted[Mathf.Clamp((int)Math.Ceiling(0.95 * n) - 1, 0, n - 1)];
            row.max = sorted[n - 1];
            return row;
        }

        void Finish(string status)
        {
            _recording = false;
            Running = false;
            _ui.ApplyConditions(_restore);
            Status = status;
        }

        string WriteCsv()
        {
            try
            {
                string mode = _ui.networkSim != null ? "simulated"
                    : NetworkServer.active && NetworkClient.active ? "host"
                    : NetworkClient.active ? "client"
                    : "offline";
                var ci = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine($"# scene={SceneManager.GetActiveScene().name} mode={mode} seed={Seed} warmup={Warmup}s duration={Duration}s date={DateTime.Now:s}");
                sb.AppendLine("preset,latency_ms,jitter_ms,loss_pct,rx_hz,technique,mean,p95,max,added_delay_ms");
                foreach (var r in Results)
                {
                    var c = r.preset.conditions;
                    foreach (var row in r.rows)
                        sb.AppendLine(string.Join(",",
                            r.preset.name, c.latencyMs.ToString("F0", ci), c.jitterMs.ToString("F0", ci),
                            c.lossPct.ToString("F0", ci), r.rxHz.ToString("F1", ci), row.technique.Replace(",", ";"),
                            row.mean.ToString("F4", ci), row.p95.ToString("F4", ci), row.max.ToString("F4", ci),
                            row.delayMs.ToString("F0", ci)));
                }

                string dir = Path.Combine(Application.persistentDataPath, "benchmarks");
                Directory.CreateDirectory(dir);
                LastFile = Path.Combine(dir, $"benchmark_{mode}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(LastFile, sb.ToString());
                Debug.Log($"[Benchmark] Wrote {LastFile}");
                return LastFile;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return "CSV write failed (see console).";
            }
        }
    }
}
