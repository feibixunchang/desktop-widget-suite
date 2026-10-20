using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XiaoPuHeWeather;

public sealed class DismissibleContextMenuStrip : ContextMenuStrip
{
    private const int WatchIntervalMilliseconds = 250;
    private const int PointerLeaveMargin = 12;
    private const int PointerMoveThreshold = 6;
    private const int EscapeVirtualKey = 0x1B;

    private readonly System.Windows.Forms.Timer _watchTimer;
    private DateTime _lastActivityUtc;
    private Point _openedPointerLocation;
    private Point _lastPointerLocation;
    private bool _isOpen;

    public DismissibleContextMenuStrip()
    {
        AutoCloseDelay = TimeSpan.FromSeconds(8);
        _watchTimer = new System.Windows.Forms.Timer { Interval = WatchIntervalMilliseconds };
        _watchTimer.Tick += OnWatchTimerTick;
        Opening += OnMenuOpening;
        KeyDown += OnMenuKeyDown;
        KeyUp += OnMenuKeyDown;
    }

    public TimeSpan AutoCloseDelay { get; set; }

    public static bool HasPointerMoved(Point current, Point origin)
    {
        var deltaX = current.X - origin.X;
        var deltaY = current.Y - origin.Y;
        return deltaX * deltaX + deltaY * deltaY > PointerMoveThreshold * PointerMoveThreshold;
    }

    public static bool ShouldDismiss(
        bool escapePressed,
        bool pointerInsideMenu,
        bool pointerMovedAway,
        TimeSpan idleTime,
        TimeSpan autoCloseDelay)
    {
        if (escapePressed || idleTime >= autoCloseDelay)
        {
            return true;
        }

        return pointerMovedAway && !pointerInsideMenu;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watchTimer.Tick -= OnWatchTimerTick;
            _watchTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (_isOpen && keyData == Keys.Escape)
        {
            Dismiss();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    private void OnMenuOpening(object? sender, CancelEventArgs eventArgs)
    {
        var pointer = Cursor.Position;
        _isOpen = true;
        _lastActivityUtc = DateTime.UtcNow;
        _openedPointerLocation = pointer;
        _lastPointerLocation = pointer;
        _watchTimer.Start();
    }

    private void OnMenuKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        _lastActivityUtc = DateTime.UtcNow;
        if (eventArgs.KeyCode == Keys.Escape)
        {
            Dismiss();
            eventArgs.Handled = true;
        }
    }

    private void OnWatchTimerTick(object? sender, EventArgs eventArgs)
    {
        if (!Visible)
        {
            StopWatching();
            return;
        }

        var pointer = Cursor.Position;
        var moved = HasPointerMoved(pointer, _lastPointerLocation);
        if (moved)
        {
            _lastPointerLocation = pointer;
            _lastActivityUtc = DateTime.UtcNow;
        }

        var dismiss = ShouldDismiss(
            IsEscapePressed(),
            GetMenuBounds().Contains(pointer),
            HasPointerMoved(pointer, _openedPointerLocation),
            DateTime.UtcNow - _lastActivityUtc,
            AutoCloseDelay);
        if (!dismiss)
        {
            return;
        }

        StopWatching();
        Hide();
    }

    private Rectangle GetMenuBounds()
    {
        var bounds = OwnerItem is null ? Bounds : RectangleToScreen(Bounds);

        // 子菜单（例如"背景透明度"）展开时，指针是落在子菜单上的，不算"离开菜单"。
        // 以前只算主菜单自己的边界，一开子菜单就被判定成指针移开、菜单立刻隐藏，
        // 结果子菜单根本没法点——这个挂件以前全是平铺菜单项，所以一直没暴露。
        UnionVisibleDropDowns(this, Items, ref bounds);
        return Rectangle.Inflate(bounds, PointerLeaveMargin, PointerLeaveMargin);
    }

    private static void UnionVisibleDropDowns(ToolStrip owner, ToolStripItemCollection items, ref Rectangle bounds)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is not ToolStripDropDownItem dropDownItem || !dropDownItem.DropDown.Visible)
            {
                continue;
            }

            bounds = Rectangle.Union(bounds, owner.RectangleToScreen(dropDownItem.DropDown.Bounds));
            UnionVisibleDropDowns(dropDownItem.DropDown, dropDownItem.DropDown.Items, ref bounds);
        }
    }

    private void Dismiss()
    {
        StopWatching();
        if (Visible)
        {
            Hide();
        }
    }

    private void StopWatching()
    {
        _isOpen = false;
        if (_watchTimer.Enabled)
        {
            _watchTimer.Stop();
        }
    }

    private static bool IsEscapePressed()
    {
        return (GetAsyncKeyState(EscapeVirtualKey) & 0x8001) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
