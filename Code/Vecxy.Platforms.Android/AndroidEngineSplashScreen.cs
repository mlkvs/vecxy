using Android.App;
using Android.Animation;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Vecxy.Engine;

namespace Vecxy.Platforms.Android;

internal sealed class AndroidEngineSplashScreen : global::Android.Views.View, IEngineSplashScreen
{
    private readonly Activity _activity;
    private readonly Dialog _dialog;
    private readonly Bitmap? _logo;
    private readonly Paint _paint = new(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
    private float _progress = 0.06f;
    private int _dismissed;
    private ValueAnimator? _fadeAnimator;

    private AndroidEngineSplashScreen(Activity activity, Dialog dialog, Bitmap? logo)
        : base(activity)
    {
        _activity = activity;
        _dialog = dialog;
        _logo = logo;
        Clickable = true;
        Focusable = true;
        ImportantForAccessibility = ImportantForAccessibility.NoHideDescendants;
        SetBackgroundColor(global::Android.Graphics.Color.Black);
    }

    public static AndroidEngineSplashScreen Attach(
        Activity activity,
        AssetManager assets,
        string logoPath)
    {
        AndroidEngineSplashScreen? splash = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();

        void Create()
        {
            try
            {
                Bitmap? logo = null;
                try
                {
                    using var stream = assets.Open(logoPath, Access.Streaming);
                    logo = BitmapFactory.DecodeStream(stream);
                }
                catch (IOException)
                {
                    // The progress indicator remains usable without an optional logo.
                }

                var dialog = new Dialog(
                    activity,
                    global::Android.Resource.Style.ThemeMaterialLightNoActionBarFullscreen);
                dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);
                dialog.SetCancelable(false);
                splash = new AndroidEngineSplashScreen(activity, dialog, logo);
                dialog.SetContentView(
                    splash,
                    new ViewGroup.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent,
                        ViewGroup.LayoutParams.MatchParent));
                dialog.Show();
                if (dialog.Window is { } window)
                {
                    // The view itself is white. A transparent window lets its alpha fade
                    // reveal the already-rendered SDL frame instead of another white layer.
                    window.SetBackgroundDrawable(new ColorDrawable(global::Android.Graphics.Color.Transparent));
                    window.ClearFlags(WindowManagerFlags.DimBehind);
                    window.SetFlags(WindowManagerFlags.Fullscreen, WindowManagerFlags.Fullscreen);
                    if (OperatingSystem.IsAndroidVersionAtLeast(30))
                    {
                        if (window.InsetsController is { } insetsController)
                        {
                            insetsController.Hide(WindowInsets.Type.SystemBars());
                            insetsController.SystemBarsBehavior =
                                (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                        }
                    }
                    else
                    {
                        window.SetStatusBarColor(global::Android.Graphics.Color.White);
                        window.SetNavigationBarColor(global::Android.Graphics.Color.White);
                        window.DecorView.SystemUiFlags =
                            SystemUiFlags.ImmersiveSticky |
                            SystemUiFlags.Fullscreen |
                            SystemUiFlags.HideNavigation |
                            SystemUiFlags.LayoutStable |
                            SystemUiFlags.LayoutFullscreen |
                            SystemUiFlags.LayoutHideNavigation;
                    }
                    window.SetLayout(
                        ViewGroup.LayoutParams.MatchParent,
                        ViewGroup.LayoutParams.MatchParent);
                }
                splash.BringToFront();
                splash.Elevation = float.MaxValue;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                ready.Set();
            }
        }

        if (Looper.MyLooper() == Looper.MainLooper)
            Create();
        else
            activity.RunOnUiThread(Create);

        ready.Wait();
        if (failure is not null)
            throw new InvalidOperationException("Unable to create the Android splash screen.", failure);

        return splash!;
    }

    public void ReportProgress(float progress)
    {
        if (Volatile.Read(ref _dismissed) != 0)
            return;

        Volatile.Write(ref _progress, Math.Clamp(progress, 0.04f, 1.0f));
        PostInvalidateOnAnimation();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        var width = Width;
        var height = Height;
        if (width <= 0 || height <= 0)
            return;

        canvas.DrawColor(global::Android.Graphics.Color.White);

        var shortestSide = Math.Min(width, height);
        var logoSize = Math.Min(width * 0.62f, height * 0.46f);
        var barWidth = Math.Min(width * 0.56f, logoSize * 0.86f);
        var barHeight = Math.Max(6.0f * Resources!.DisplayMetrics!.Density, shortestSide * 0.012f);
        var gap = Math.Max(24.0f * Resources.DisplayMetrics.Density, shortestSide * 0.055f);
        var groupHeight = logoSize + gap + barHeight;
        var logoTop = (height - groupHeight) * 0.5f;
        var logoLeft = (width - logoSize) * 0.5f;

        if (_logo is not null && !_logo.IsRecycled)
        {
            var destination = new RectF(
                logoLeft,
                logoTop,
                logoLeft + logoSize,
                logoTop + logoSize);
            canvas.DrawBitmap(_logo, null, destination, _paint);
        }

        var barLeft = (width - barWidth) * 0.5f;
        var barTop = logoTop + logoSize + gap;
        var radius = barHeight * 0.5f;

        _paint.Color = global::Android.Graphics.Color.Rgb(219, 233, 220);
        canvas.DrawRoundRect(
            barLeft,
            barTop,
            barLeft + barWidth,
            barTop + barHeight,
            radius,
            radius,
            _paint);

        _paint.Color = global::Android.Graphics.Color.Rgb(79, 204, 99);
        canvas.DrawRoundRect(
            barLeft,
            barTop,
            barLeft + barWidth * Volatile.Read(ref _progress),
            barTop + barHeight,
            radius,
            radius,
            _paint);
    }

    public void Dismiss()
    {
        Volatile.Write(ref _progress, 1.0f);
        if (Interlocked.Exchange(ref _dismissed, 1) != 0)
            return;

        _activity.RunOnUiThread(() =>
        {
            BringToFront();
            Alpha = 1.0f;
            Invalidate();
            var animator = ValueAnimator.OfFloat(1.0f, 0.0f);
            if (animator is null)
            {
                RemoveFromActivity();
                return;
            }

            _fadeAnimator = animator;
            animator.SetDuration(520);
            animator.Update += (_, _) =>
            {
                if (_dialog.Window is not { } window ||
                    animator.AnimatedValue is not Java.Lang.Float animatedAlpha ||
                    window.Attributes is not { } attributes)
                {
                    return;
                }

                attributes.Alpha = animatedAlpha.FloatValue();
                window.Attributes = attributes;
            };
            animator.AnimationEnd += (_, _) => RemoveFromActivity();
            PostOnAnimation(new Java.Lang.Runnable(animator.Start));
        });
    }

    public void PrepareForFirstFrame()
    {
        // The native view remains above the SDL surface until its fade completes.
    }

    private void RemoveFromActivity()
    {
        if (_dialog.IsShowing)
            _dialog.Dismiss();
        _fadeAnimator?.Dispose();
        _fadeAnimator = null;
        _logo?.Recycle();
        _paint.Dispose();
        _dialog.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _dismissed, 1) == 0)
            _activity.RunOnUiThread(RemoveFromActivity);
        base.Dispose(disposing);
    }
}
