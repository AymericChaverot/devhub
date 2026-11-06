namespace DevHub.App.Animation;

/// <summary>A CSS-style cubic-bezier(x1, y1, x2, y2) timing curve from (0,0) to (1,1).</summary>
public sealed class CubicBezierEasing(double x1, double y1, double x2, double y2)
{
    /// <summary>Gentle start and a long, soft settle: suits a panel that resizes.</summary>
    public static CubicBezierEasing SmoothDecelerate { get; } = new(0.3, 0, 0, 1);

    /// <summary>Maps linear time <paramref name="t"/> in [0, 1] to eased progress.</summary>
    public double Ease(double t)
    {
        if (t <= 0)
        {
            return 0;
        }

        if (t >= 1)
        {
            return 1;
        }

        return Sample(SolveCurveParameter(t), y1, y2);
    }

    /// <summary>Finds u such that x(u) = <paramref name="x"/>: Newton's method, bisection as a fallback.</summary>
    private double SolveCurveParameter(double x)
    {
        var u = x;
        for (var i = 0; i < 8; i++)
        {
            var error = Sample(u, x1, x2) - x;
            if (Math.Abs(error) < 1e-6)
            {
                return u;
            }

            var slope = SampleDerivative(u, x1, x2);
            if (Math.Abs(slope) < 1e-6)
            {
                break;
            }

            u -= error / slope;
        }

        double low = 0, high = 1;
        u = x;
        while (high - low > 1e-6)
        {
            if (Sample(u, x1, x2) < x)
            {
                low = u;
            }
            else
            {
                high = u;
            }

            u = (low + high) / 2;
        }

        return u;
    }

    private static double Sample(double u, double p1, double p2)
    {
        var inv = 1 - u;
        return (3 * inv * inv * u * p1) + (3 * inv * u * u * p2) + (u * u * u);
    }

    private static double SampleDerivative(double u, double p1, double p2)
    {
        var inv = 1 - u;
        return (3 * inv * inv * p1) + (6 * inv * u * (p2 - p1)) + (3 * u * u * (1 - p2));
    }
}
