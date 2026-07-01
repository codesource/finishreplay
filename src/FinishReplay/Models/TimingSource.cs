namespace FinishReplay.Models;

/// <summary>Which timing provider supplies trigger markers.</summary>
public enum TimingSource
{
    /// <summary>Software buttons only (no hardware).</summary>
    Manual,

    /// <summary>ALGE TimY3 over its serial / USB-serial port.</summary>
    AlgeTimySerial,
}
