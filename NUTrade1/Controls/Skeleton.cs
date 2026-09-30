namespace NUTrade1.Controls;

/// <summary>
/// A screen's loading state: grey bones (the <c>Bone</c> / <c>BoneRound</c> styles) laid out
/// like the content on its way, pulsing gently until it lands.
///
/// Replaces the spinners and loading dots that used to stand in for whole screens. A
/// skeleton shows the student the shape of what's coming straight away, and nothing jumps
/// when the real content arrives, because it takes the same space. Bind
/// <see cref="IsActive"/> to the ViewModel's IsLoading; the control hides itself when it
/// is not active, so there is no separate IsVisible to keep in step.
/// </summary>
public class Skeleton : ContentView
{
    private const string PulseAnimation = "SkeletonPulse";
    private const uint PulseMs = 1200;

    public Skeleton()
    {
        IsVisible = false;
        InputTransparent = true;

        // Animations need a handler, so one requested before the control was on screen
        // starts here instead.
        Loaded += (_, _) =>
        {
            if (IsActive) StartPulse();
        };
        Unloaded += (_, _) => StopPulse();
    }

    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive), typeof(bool), typeof(Skeleton), false,
        propertyChanged: (b, _, v) => ((Skeleton)b).OnIsActiveChanged((bool)v));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private void OnIsActiveChanged(bool active)
    {
        IsVisible = active;
        if (active) StartPulse();
        else StopPulse();
    }

    private void StartPulse()
    {
        if (Handler is null || this.AnimationIsRunning(PulseAnimation)) return;

        // One fade for the whole placeholder rather than one per bone, so every block
        // breathes together and a long list costs a single animation. It fades the
        // content, not the control, so a skeleton with a background (QrSkeleton, laid over
        // an old QR while a new one is minted) stays opaque and hides what's beneath.
        var pulse = new Animation
        {
            { 0, 0.5, new Animation(SetBoneOpacity, 1, 0.45) },
            { 0.5, 1, new Animation(SetBoneOpacity, 0.45, 1) },
        };
        pulse.Commit(this, PulseAnimation, length: PulseMs, easing: Easing.SinInOut, repeat: () => IsActive);
    }

    private void SetBoneOpacity(double value)
    {
        if (Content is { } content) content.Opacity = value;
    }

    private void StopPulse()
    {
        this.AbortAnimation(PulseAnimation);
        SetBoneOpacity(1);
    }
}
