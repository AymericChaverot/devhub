using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace DevHub.App.Animation;

/// <summary>
/// Tweens a progress value between 0 and 1 once per rendered frame, for transitions that
/// have to drive layout (heights, paddings) and therefore can't run as composition animations.
/// Retargeting mid-flight continues from the current value at the same pace.
/// </summary>
public sealed class ProgressAnimator(Action<double> apply, CubicBezierEasing easing, TimeSpan fullDuration)
{
    private readonly Stopwatch _clock = new();
    private double _from;
    private TimeSpan _duration;
    private Action? _completed;

    /// <summary>Current eased progress, as last applied.</summary>
    public double Value { get; private set; }

    /// <summary>Where the running (or last) animation is heading.</summary>
    public double Target { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>Animates to <paramref name="target"/>; the duration scales with the distance left to cover.</summary>
    public void AnimateTo(double target, Action? completed = null)
    {
        target = Math.Clamp(target, 0, 1);
        var distance = Math.Abs(target - Value);
        if (distance < 0.001)
        {
            JumpTo(target);
            completed?.Invoke();
            return;
        }

        _from = Value;
        Target = target;
        _duration = fullDuration * distance;
        _completed = completed;
        _clock.Restart();

        if (!IsRunning)
        {
            IsRunning = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>Applies <paramref name="value"/> immediately, cancelling any running animation.</summary>
    public void JumpTo(double value)
    {
        Stop();
        Value = Target = Math.Clamp(value, 0, 1);
        apply(Value);
    }

    public void Stop()
    {
        if (IsRunning)
        {
            IsRunning = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, object e)
    {
        var t = Math.Clamp(_clock.Elapsed / _duration, 0, 1);
        Value = _from + ((Target - _from) * easing.Ease(t));
        apply(Value);

        if (t >= 1)
        {
            Stop();
            _completed?.Invoke();
        }
    }
}
