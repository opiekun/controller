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
    EmulationFault? Fault,
    bool IsKeyboardHookConnected = false,
    bool IsControllerConnected = false,
    string? ActiveThrottleBinding = null,
    string? ActiveBrakeBinding = null,
    bool IsThrottleCutActive = false,
    bool IsInputSuppressionEnabled = false,
    VirtualControllerAvailability ControllerAvailability = VirtualControllerAvailability.Unknown)
{
    public static EmulationState Stopped { get; } = new(
        false, 0d, 0d, 0d, 0d, 0, 0, InputHealth.Healthy, null, false, false);
}

/// <summary>
/// Represents whether a virtual-controller backend could be constructed, independently of its connection state.
/// </summary>
public enum VirtualControllerAvailability
{
    Unknown,
    Available,
    Unavailable
}
