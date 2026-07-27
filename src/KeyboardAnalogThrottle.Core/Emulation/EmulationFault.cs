namespace KeyboardAnalogThrottle.Core.Emulation;

/// <summary>
/// Categorizes a fault that forced emulation to stop.
/// </summary>
public enum EmulationFaultKind
{
    Startup,
    InputUnavailable,
    InputSynchronizationTimedOut,
    Controller,
    Unexpected
}

/// <summary>
/// Immutable information about a fault observed by the emulation engine.
/// </summary>
public sealed record EmulationFault(EmulationFaultKind Kind, string Message, Exception? Exception = null);
