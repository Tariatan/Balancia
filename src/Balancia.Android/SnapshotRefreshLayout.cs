using Android.Content;
using Android.Graphics;
using Android.Views;

namespace Balancia.Android;

/// <summary>Observes pulls before child controls can consume them, preserving taps and ordinary scrolling.</summary>
internal sealed class SnapshotRefreshLayout(Context context, System.Action refresh) : LinearLayout(context)
{
    private readonly float density = context.Resources!.DisplayMetrics!.Density;
    private readonly int touchSlop = ViewConfiguration.Get(context)!.ScaledTouchSlop;
    private float startX;
    private float startY;
    private float pull;
    private bool eligible;
    private bool dragging;

    internal Func<bool> CanRefresh { get; set; } = () => false;

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e is null) { return false; }
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                startX = e.GetX();
                startY = e.GetY();
                eligible = CanRefresh();
                dragging = false;
                break;
            case MotionEventActions.Move:
                var distance = e.GetY() - startY;
                if (eligible && !dragging)
                {
                    if (Math.Abs(e.GetX() - startX) > touchSlop || distance < -touchSlop || !CanRefresh())
                    {
                        eligible = false;
                    }
                    else if (distance > touchSlop)
                    {
                        // Cancel the child's tap/drag before taking ownership of the pull.
                        using var cancel = MotionEvent.Obtain(e)!;
                        cancel.Action = MotionEventActions.Cancel;
                        base.DispatchTouchEvent(cancel);
                        dragging = true;
                    }
                }
                if (dragging)
                {
                    pull = Math.Clamp(distance / density, 0, 120);
                    Invalidate();
                    return true;
                }
                break;
            case MotionEventActions.PointerDown:
                eligible = false;
                if (dragging)
                {
                    ResetPull();
                    return true;
                }
                break;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                var released = dragging && e.ActionMasked == MotionEventActions.Up && pull >= 80;
                var consumed = dragging;
                ResetPull();
                if (released)
                {
                    PerformClick();
                    refresh();
                }
                if (consumed) { return true; }
                break;
        }
        var handled = base.DispatchTouchEvent(e);
        // Keep receiving the gesture even when it starts on a non-clickable label or empty space.
        return handled || e.ActionMasked == MotionEventActions.Down;
    }

    private void ResetPull()
    {
        dragging = false;
        eligible = false;
        pull = 0;
        Invalidate();
    }

    protected override void DispatchDraw(Canvas canvas)
    {
        base.DispatchDraw(canvas);
        if (pull <= 0) { return; }
        using var paint = new Paint(PaintFlags.AntiAlias);
        var x = Width / 2f;
        var y = Math.Min(pull / 2, 48) * density;
        paint.Color = ViewerPalette.Card;
        canvas.DrawCircle(x, y, 20 * density, paint);
        paint.Color = ViewerPalette.Accent;
        paint.TextSize = 24 * density;
        paint.TextAlign = Paint.Align.Center;
        canvas.DrawText(pull >= 80 ? "↻" : "↓", x, y + 8 * density, paint);
    }

    public override bool PerformClick() => base.PerformClick();
}
