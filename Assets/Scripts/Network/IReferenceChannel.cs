using UnityEngine;

namespace Network
{
    /// <summary>Server side: stream the exact authoritative position at a high rate (metrics only).</summary>
    public interface ITruthSender
    {
        void SendTruth(float timestamp, Vector3 position);
    }

    /// <summary>
    /// Client side: authoritative position at a given local time, reconstructed from the truth stream
    /// (timestamps already rebased onto the local clock). Returns false when no reference is available.
    /// </summary>
    public interface IReferenceSource
    {
        bool TryGetTruth(float localTime, out Vector3 position);

        /// <summary>Diagnostics: how far <paramref name="localTime"/> is past the newest truth sample (seconds).</summary>
        float TruthAge(float localTime) => 0f;
    }
}
