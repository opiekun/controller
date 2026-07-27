namespace KeyboardAnalogThrottle.Core.Emulation;

/// <summary>
/// The synchronization state reported by the keyboard hook.
/// </summary>
public enum InputHealth
{
    Healthy,
    Synchronizing,
    Unavailable
}
