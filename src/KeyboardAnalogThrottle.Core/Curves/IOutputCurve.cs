namespace KeyboardAnalogThrottle.Core.Curves;

public interface IOutputCurve
{
    double Apply(double value);
}
