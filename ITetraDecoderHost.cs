namespace SDRSharp.Tetra
{
    /// <summary>
    /// Minimal contract the decoder needs from a host (panel/engine).
    /// </summary>
    public interface ITetraDecoderHost
    {
        bool MmOnlyMode { get; }
    }
}
