using System;

namespace Network
{
    /// <summary>Network conditions applied to the server's outgoing traffic (and mirrored on clients' uplink).</summary>
    [Serializable]
    public struct NetConditions
    {
        public float latencyMs;
        public float jitterMs;
        public float lossPct;
        public float scramblePct;
        public float sendRateHz;
    }

    public static class NetPresets
    {
        public readonly struct Preset
        {
            public readonly string name;
            public readonly NetConditions conditions;

            public Preset(string name, float latencyMs, float jitterMs, float lossPct, float sendRateHz)
            {
                this.name = name;
                conditions = new NetConditions
                {
                    latencyMs = latencyMs,
                    jitterMs = jitterMs,
                    lossPct = lossPct,
                    scramblePct = lossPct > 0f ? 2f : 0f,
                    sendRateHz = sendRateHz
                };
            }
        }

        public static readonly Preset[] All =
        {
            new Preset("Perfect", 0, 0, 0, 20),
            new Preset("LAN", 10, 5, 0, 20),
            new Preset("Broadband", 80, 20, 2, 20),
            new Preset("Mobile", 150, 60, 5, 10),
            new Preset("Lossy", 200, 100, 15, 10),
            new Preset("Chaos", 300, 150, 25, 5),
        };
    }
}
