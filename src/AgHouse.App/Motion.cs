using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AgHouse.App;

/// <summary>How an element moves when it appears. Set with <c>Motion.Entrance</c> in XAML.</summary>
public enum Entrance
{
    None,
    /// <summary>The selection pill: grows from its centre, as WinUI's list selection does.</summary>
    Pill,
    /// <summary>The attention dot: scales in from 40% and fades up.</summary>
    Dot,
    /// <summary>
    /// The attention dot's halo: ripples out once on arrival, then again every few seconds for
    /// <see cref="Motion.PulseFor"/> after the request arrived (<c>Motion.PulseSince</c>), then rests.
    /// A request the user is leaving on purpose stops tugging at the eye; a new one starts it again.
    /// </summary>
    Halo,
}

/// <summary>
/// aghouse's motion: a few short, Fluent-timed transitions in the chrome, never on the terminals
/// (they're native child windows) and never on anything that would resize them. Everything honours
/// Windows' "Animation effects" setting: off, and elements simply appear in their final state.
/// </summary>
public static class Motion
{
    // WinUI's control timings and its "fast out, slow in" decelerate curve.
    public static readonly Duration Fast = TimeSpan.FromMilliseconds(167);
    public static readonly Duration Normal = TimeSpan.FromMilliseconds(250);
    public static readonly IEasingFunction Decelerate = new SplineEase(0, 0, 0, 1);

    /// <summary>Windows' Settings › Accessibility › Visual effects › Animation effects.</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    /// <summary>How long a halo keeps rippling after its request arrived. After that the dot and the words stay, still.</summary>
    public static readonly TimeSpan PulseFor = TimeSpan.FromSeconds(90);

    /// <summary>
    /// When the request a halo stands for arrived (UTC). Unset, the halo counts from when it first showed.
    /// A later value (the pane asked again) ripples at once and restarts the pulse.
    /// </summary>
    public static readonly DependencyProperty PulseSinceProperty = DependencyProperty.RegisterAttached(
        "PulseSince", typeof(DateTime), typeof(Motion), new PropertyMetadata(default(DateTime), OnPulseSinceChanged));

    public static DateTime GetPulseSince(DependencyObject d) => (DateTime)d.GetValue(PulseSinceProperty);
    public static void SetPulseSince(DependencyObject d, DateTime value) => d.SetValue(PulseSinceProperty, value);

    private static void OnPulseSinceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var (was, now) = ((DateTime)e.OldValue, (DateTime)e.NewValue);
        if (d is FrameworkElement el && now > was) HaloPulse.Renew(el, now, ripple: was != default);
    }

    public static readonly DependencyProperty EntranceProperty = DependencyProperty.RegisterAttached(
        "Entrance", typeof(Entrance), typeof(Motion), new PropertyMetadata(Entrance.None, OnEntranceChanged));

    public static Entrance GetEntrance(DependencyObject d) => (Entrance)d.GetValue(EntranceProperty);
    public static void SetEntrance(DependencyObject d, Entrance value) => d.SetValue(EntranceProperty, value);

    private static void OnEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        el.IsVisibleChanged -= OnVisibleChanged;
        el.Unloaded -= OnUnloaded;
        if ((Entrance)e.NewValue == Entrance.None) return;
        el.IsVisibleChanged += OnVisibleChanged;
        el.Unloaded += OnUnloaded;
        // A halo already showing when its row is created (the Agents list rebuilds its rows) still pulses,
        // but doesn't play the arrival: the request isn't new.
        if ((Entrance)e.NewValue == Entrance.Halo)
            el.Loaded += (_, _) => { if (el.IsVisible) HaloPulse.Add(el); };
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => HaloPulse.Remove((FrameworkElement)sender);

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var el = (FrameworkElement)sender;
        var entrance = GetEntrance(el);
        if (!(bool)e.NewValue)
        {
            if (entrance == Entrance.Halo) HaloPulse.Remove(el);
            return;
        }
        if (entrance == Entrance.Halo) HaloPulse.Add(el);
        // Only a change on a live element moves. A container that is just being built isn't news.
        if (!el.IsLoaded || !Enabled) return;
        switch (entrance)
        {
            case Entrance.Pill:
                Scale(el).BeginAnimation(ScaleTransform.ScaleYProperty, From(0, 1, Fast));
                break;
            case Entrance.Dot:
                var dot = Scale(el);
                var grow = From(0.4, 1, Normal);
                dot.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                dot.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                el.BeginAnimation(UIElement.OpacityProperty, From(0, 1, Fast));
                break;
            case Entrance.Halo:
                if (HaloPulse.IsYoung(el)) HaloPulse.Ripple(el);
                break;
        }
    }

    /// <summary>
    /// A pane header's "Needs you" tint arriving: it swells to about two and a half times its resting
    /// strength, then settles, so the moment a pane starts waiting reads as an event, not a repaint.
    /// </summary>
    public static void Breathe(UIElement tint, double rest)
    {
        if (!Enabled)
        {
            tint.BeginAnimation(UIElement.OpacityProperty, null);
            tint.Opacity = rest;
            return;
        }
        var breath = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
        breath.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        breath.KeyFrames.Add(new SplineDoubleKeyFrame(rest * 2.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), new KeySpline(0, 0, 0, 1)));
        breath.KeyFrames.Add(new SplineDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), new KeySpline(0.3, 0, 0, 1)));
        breath.Freeze();
        tint.BeginAnimation(UIElement.OpacityProperty, breath);
    }

    /// <summary>
    /// A flyout's content arriving from the edge it opened from: <paramref name="offsetY"/> px above
    /// (negative) or below, fading up. The window itself appears at once, so typing never waits.
    /// </summary>
    public static void Arrive(FrameworkElement content, double offsetY)
    {
        if (!Enabled) return;
        var shift = new TranslateTransform();
        content.RenderTransform = shift;
        shift.BeginAnimation(TranslateTransform.YProperty, From(offsetY, 0, Fast));
        content.BeginAnimation(UIElement.OpacityProperty, From(0, 1, Fast));
    }

    internal static DoubleAnimation From(double from, double to, Duration duration)
    {
        var a = new DoubleAnimation(from, to, duration) { EasingFunction = Decelerate, FillBehavior = FillBehavior.Stop };
        a.Freeze();
        return a;
    }

    /// <summary>The element's own centred ScaleTransform, created on first use.</summary>
    internal static ScaleTransform Scale(UIElement el)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        if (el.RenderTransform is ScaleTransform { IsFrozen: false } s) return s;
        s = new ScaleTransform();
        el.RenderTransform = s;
        return s;
    }

    /// <summary>A cubic Bézier easing, as CSS's cubic-bezier() and WinUI's KeySpline easings.</summary>
    private sealed class SplineEase(double x1, double y1, double x2, double y2) : IEasingFunction
    {
        private readonly KeySpline _spline = Frozen(new KeySpline(x1, y1, x2, y2));
        public double Ease(double t) => _spline.GetSplineProgress(t);
        private static KeySpline Frozen(KeySpline k) { k.Freeze(); return k; }
    }

    /// <summary>
    /// Every young halo ripples together on one timer, so a sidebar with three waiting workspaces
    /// pulses as one signal rather than three out of step. The timer runs only while some halo is still
    /// young (<see cref="PulseFor"/>), and WPF's render loop only during the ripple itself.
    /// </summary>
    private static class HaloPulse
    {
        /// <summary>Each showing halo and when its request arrived.</summary>
        private static readonly Dictionary<FrameworkElement, DateTime> Halos = [];
        private static readonly Dictionary<FrameworkElement, double> Rest = [];
        private static DispatcherTimer? _timer;

        public static void Add(FrameworkElement halo)
        {
            if (Halos.ContainsKey(halo)) return;
            var since = GetPulseSince(halo);
            Halos[halo] = since == default ? DateTime.UtcNow : since;
            Rest[halo] = halo.Opacity;
            Wake();
        }

        /// <summary>The pane asked again: ripple now and pulse for another <see cref="PulseFor"/>.</summary>
        public static void Renew(FrameworkElement halo, DateTime since, bool ripple)
        {
            if (!Halos.ContainsKey(halo)) return;
            Halos[halo] = since;
            if (ripple && halo.IsLoaded && IsYoung(halo)) Ripple(halo);
            Wake();
        }

        /// <summary>Still within <see cref="PulseFor"/> of its request (or not showing yet, so not judged).</summary>
        public static bool IsYoung(FrameworkElement halo) =>
            !Halos.TryGetValue(halo, out var since) || DateTime.UtcNow - since < PulseFor;

        private static void Wake()
        {
            if (_timer is null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
                _timer.Tick += (_, _) =>
                {
                    var young = Halos.Keys.Where(IsYoung).ToList();
                    // Every request has had its minute and a half: rest until a new one arrives.
                    if (young.Count == 0) { _timer!.Stop(); return; }
                    if (!Enabled) return;
                    foreach (var h in young) Ripple(h);
                };
            }
            if (!_timer.IsEnabled && Halos.Keys.Any(IsYoung)) _timer.Start();
        }

        public static void Remove(FrameworkElement halo)
        {
            if (!Halos.Remove(halo)) return;
            Rest.Remove(halo);
            halo.BeginAnimation(UIElement.OpacityProperty, null);
            if (halo.RenderTransform is ScaleTransform s)
            {
                s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
            if (Halos.Count == 0) _timer?.Stop();
        }

        /// <summary>The ring travels out to 1.6× and fades, then a fresh ring fades back in at rest.</summary>
        public static void Ripple(FrameworkElement halo)
        {
            if (!Enabled) return;
            var rest = Rest.TryGetValue(halo, out var r) ? r : halo.Opacity;
            var scale = Scale(halo);
            var grow = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1200), FillBehavior = FillBehavior.Stop };
            grow.KeyFrames.Add(new SplineDoubleKeyFrame(1.6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)), new KeySpline(0, 0, 0, 1)));
            grow.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
            grow.Freeze();
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1200), FillBehavior = FillBehavior.Stop };
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(Math.Max(rest, 0.45), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)), new KeySpline(0.3, 0, 0.6, 1)));
            fade.KeyFrames.Add(new SplineDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200)), new KeySpline(0, 0, 0, 1)));
            fade.Freeze();
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            halo.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
