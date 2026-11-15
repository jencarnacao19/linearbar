namespace LinearBar.Core.Animation;

/// <summary>
/// Critically damped spring that eases a value towards a target without overshooting.
/// Frame-rate independent: stepping with many small time slices or a few large ones lands
/// in (almost) the same place, which matters because WPF render ticks are irregular.
/// </summary>
public struct SmoothDamp
{
    private const double SettleDistance = 0.0005;
    private const double SettleVelocity = 0.005;

    private double _velocity;

    public SmoothDamp(double initial)
    {
        Value = initial;
        Target = initial;
        _velocity = 0;
    }

    /// <summary>Current value.</summary>
    public double Value { get; private set; }

    /// <summary>Value being approached.</summary>
    public double Target { get; set; }

    /// <summary>True once the value has reached the target and stopped moving.</summary>
    public readonly bool IsSettled => Value == Target && _velocity == 0;

    /// <summary>Jumps straight to <paramref name="value"/> and stops.</summary>
    public void Snap(double value)
    {
        Value = value;
        Target = value;
        _velocity = 0;
    }

    /// <summary>
    /// Advances the spring.
    /// </summary>
    /// <param name="deltaSeconds">Time since the previous step.</param>
    /// <param name="smoothTime">Roughly the time it takes to cover most of the distance, in seconds.</param>
    /// <returns>True while the value is still moving; false once it has settled.</returns>
    public bool Step(double deltaSeconds, double smoothTime)
    {
        if (IsSettled)
        {
            return false;
        }

        if (smoothTime <= 0 || double.IsNaN(deltaSeconds))
        {
            Snap(Target);
            return false;
        }

        // Long stalls (debugger, sleep) would otherwise be integrated as one giant step.
        double dt = Math.Clamp(deltaSeconds, 0, 0.1);
        if (dt > 0)
        {
            // Closed-form integration of a critically damped spring ("SmoothDamp").
            double omega = 2.0 / smoothTime;
            double x = omega * dt;
            double decay = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = Value - Target;
            double temp = (_velocity + omega * change) * dt;

            _velocity = (_velocity - omega * temp) * decay;
            double next = Target + (change + temp) * decay;

            // Never cross the target: a critically damped spring should not overshoot.
            if ((change > 0) == (next - Target < 0) && change != 0)
            {
                next = Target;
                _velocity = 0;
            }

            Value = next;
        }

        if (Math.Abs(Value - Target) < SettleDistance && Math.Abs(_velocity) < SettleVelocity)
        {
            Snap(Target);
            return false;
        }

        return true;
    }
}
