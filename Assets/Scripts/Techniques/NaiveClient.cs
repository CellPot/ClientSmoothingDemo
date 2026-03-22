using Network;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// TECHNIQUE 0 — Naive / No Smoothing
    /// Sets position directly to the latest server snapshot with zero interpolation.
    /// Demonstrates the raw "teleporting" problem of unsmoothed server updates.
    /// </summary>
    public class NaiveClient : BaseClientEntity
    {
        protected override void Awake()
        {
            techniqueName = "Naive (No Smoothing)";
            color = Color.red;
            base.Awake();
        }

        protected override void OnSnapshot(NetworkSimulator.Snapshot snap)
        {
            // Position is applied immediately inside UpdatePosition
        }

        protected override void UpdatePosition()
        {
            if (!hasSnapshot) return;
            transform.position = latestSnapshot.position;
        }
    }
}