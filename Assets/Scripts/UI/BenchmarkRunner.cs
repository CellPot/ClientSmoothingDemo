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
        public const float StutterThreshold = 15f; // velocity change (units/s) in one frame counted as a stutter

        public struct Row
        {
            public string technique;
            public float mean, p95, max, delayMs;
            public float adjMean, adjP95; // vs truth rewound by the added delay
            public float rmsDv;           // RMS frame-to-frame velocity change (smoothness, lower = smoother)
            public int stutters;
            public float score;           // combined rank, lower is better, see Score()          // frames with a velocity change above StutterThreshold
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
        private List<List<float>> _samples, _adjSamples, _dvSamples;
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

            Status = "";
            _routine = StartCoroutine(Run());
        }

        public void Cancel()
        {
            if (!Running) return;
            if (_routine != null) StopCoroutine(_routine);
            Finish("Cancelled.");
        }

        void LateUpdate()
        {
            if (!_recording) return;
            for (int i = 0; i < _ui.clients.Count; i++)
            {
                var c = _ui.clients[i];
                if (!c.gameObject.activeInHierarchy) continue;
                _samples[i].Add(c.currentError);
                _adjSamples[i].Add(c.currentAdjustedError);
                _dvSamples[i].Add(c.currentVelocityChange);
            }
        }

        // Runs the benchmark, turning any exception into a visible status instead of a stuck "Running" state.
        IEnumerator Run()
        {
            Running = true;
            var steps = RunSteps();
            while (true)
            {
                object current;
                try
                {
                    if (!steps.MoveNext()) yield break;
                    current = steps.Current;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Finish($"Failed: {ex.Message}");
                    yield break;
                }

                yield return current;
            }
        }

        IEnumerator RunSteps()
        {
            Results.Clear();
            _restore = _ui.GetConditions();
            _ui.PrepareBenchmark();

            var clients = _ui.clients;
            _samples = new List<List<float>>();
            _adjSamples = new List<List<float>>();
            _dvSamples = new List<List<float>>();
            foreach (var _ in clients)
            {
                _samples.Add(new List<float>());
                _adjSamples.Add(new List<float>());
                _dvSamples.Add(new List<float>());
            }

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
                    _adjSamples[i].Clear();
                    _dvSamples[i].Clear();
                    startCounts[i] = clients[i].snapshotsReceived;
                }

                NetDiagnostics.Write($"[bench] {preset.name} recording started");
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
                    result.rows.Add(Summarize(clients[i], _samples[i], _adjSamples[i], _dvSamples[i]));
                    if (i == 0) result.rxHz = (clients[i].snapshotsReceived - startCounts[i]) / Duration;
                }

                NetDiagnostics.Write($"[bench] {preset.name} recording done; worst-error frame per technique:");
                for (int i = 0; i < clients.Count; i++)
                    NetDiagnostics.Write($"[bench]   {clients[i].techniqueName}: {clients[i].maxErrorInfo}");

                Results.Add(result);
            }

            Finish($"Done. {WriteCsv()}");
        }

        static Row Summarize(Techniques.BaseClientEntity c, List<float> samples, List<float> adj, List<float> dv)
        {
            var row = new Row { technique = c.techniqueName, delayMs = c.AddedDelaySeconds * 1000f };
            int n = samples.Count;
            if (n == 0) return row;

            var sorted = new List<float>(samples);
            sorted.Sort();
            double sum = 0;
            foreach (float s in samples) sum += s;
            row.mean = (float)(sum / n);
            row.p95 = Percentile95(sorted);
            row.max = sorted[n - 1];

            if (adj.Count > 0)
            {
                var adjSorted = new List<float>(adj);
                adjSorted.Sort();
                double adjSum = 0;
                foreach (float s in adj) adjSum += s;
                row.adjMean = (float)(adjSum / adj.Count);
                row.adjP95 = Percentile95(adjSorted);
            }

            if (dv.Count > 0)
            {
                double sq = 0;
                foreach (float d in dv)
                {
                    sq += (double)d * d;
                    if (d > StutterThreshold) row.stutters++;
                }

                row.rmsDv = (float)Math.Sqrt(sq / dv.Count);
            }

            row.score = Score(row);
            return row;
        }

        // Combined rank (lower = better): delay-adjusted accuracy + roughness + intentional lag. The weights are a
        // judgement call (1 u of error ~ 50 u/s of velocity change ~ 330 ms of delay); the CSV keeps the raw columns.
        public static float Score(Row r) => r.adjMean + 0.02f * r.rmsDv + 0.003f * r.delayMs;

        static float Percentile95(List<float> sorted) =>
            sorted[Mathf.Clamp((int)Math.Ceiling(0.95 * sorted.Count) - 1, 0, sorted.Count - 1)];

        void Finish(string status)
        {
            _recording = false;
            Running = false;
            Status = status;
            try { _ui.ApplyConditions(_restore); }
            catch (Exception ex) { Debug.LogException(ex); }
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
                sb.AppendLine("preset,latency_ms,jitter_ms,loss_pct,rx_hz,technique,mean,p95,max,added_delay_ms,adj_mean,adj_p95,rms_dv,stutters,score");
                foreach (var r in Results)
                {
                    var c = r.preset.conditions;
                    foreach (var row in r.rows)
                        sb.AppendLine(string.Join(",",
                            r.preset.name, c.latencyMs.ToString("F0", ci), c.jitterMs.ToString("F0", ci),
                            c.lossPct.ToString("F0", ci), r.rxHz.ToString("F1", ci), row.technique.Replace(",", ";"),
                            row.mean.ToString("F4", ci), row.p95.ToString("F4", ci), row.max.ToString("F4", ci),
                            row.delayMs.ToString("F0", ci), row.adjMean.ToString("F4", ci), row.adjP95.ToString("F4", ci),
                            row.rmsDv.ToString("F3", ci), row.stutters.ToString(ci), row.score.ToString("F3", ci)));
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
