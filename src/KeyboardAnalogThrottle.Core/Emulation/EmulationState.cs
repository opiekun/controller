namespace KeyboardAnalogThrottle.Core.Emulation;

/// <summary>
/// Immutable state suitable for display by a UI without participating in the hot loop.
/// </summary>
public sealed record EmulationState(
    bool IsRunning,
    double RawThrottle,
    double RawBrake,
    double Throttle,
    double Brake,
    byte RightTrigger,
    byte LeftTrigger,
    InputHealth InputHealth,
    EmulationFault? Fault)
{
    public static EmulationState Stopped { get; } = new(
        false, 0d, 0d, 0d, 0d, 0, 0, InputHealth.Healthy, null);
}
