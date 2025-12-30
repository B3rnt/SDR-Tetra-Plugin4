using System;

namespace SDRSharp.Tetra.MultiChannel
{
    [Serializable]
    public class ChannelSettings
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "TETRA";
        public long FrequencyHz { get; set; } = 0;
        public bool Enabled { get; set; } = true;

        // Per-channel AGC (internal, not SDR# main AGC)
        public bool AgcEnabled { get; set; } = true;
        public float AgcTargetRms { get; set; } = 0.25f;
        public float AgcAttack { get; set; } = 0.02f;   // 0..1
        public float AgcDecay { get; set; } = 0.002f;   // 0..1

        // Decoder options
        public bool MmOnlyMode { get; set; } = false;
    }
}
