namespace KeyboardAnalogThrottle.Core.Emulation;

/// <summary>
/// The output to retain when throttle and brake base keys are both held.
/// </summary>
public enum ConflictMode
{
    BrakeWins,
    ThrottleWins,
    CancelBoth,
    LastPressedWins
}
