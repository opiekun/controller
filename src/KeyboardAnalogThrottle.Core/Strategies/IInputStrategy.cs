using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Strategies;

/// <summary>
/// Produces a normalized channel value from one sampled input frame.
/// </summary>
public interface IInputStrategy
{
    double Update(InputSnapshot input, double currentValue, TimeSpan elapsed, ChannelConfiguration configuration);
}
