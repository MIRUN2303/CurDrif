using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CursorMoveApp
{
    /// <summary>Win32 helpers. Kept separate from the UI and from movement logic.</summary>
    internal static class NativeApi
    {
        internal const int WmHotKey = 0x0312;
        internal const uint ModNoRepeat = 0x4000;
        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point32
        {
            public int X;
            public int Y;

            public Point32(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect32
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect32 Monitor;
            public Rect32 Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point32 point);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(Point32 point, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        private const int SmCxScreen = 0;
        private const int SmCyScreen = 1;

        internal static Point GetCursorPosition()
        {
            Point32 point;
            if (GetCursorPos(out point))
            {
                return new Point(point.X, point.Y);
            }
            return Point.Empty;
        }

        internal static void SetCursorPosition(Point point)
        {
            SetCursorPos(point.X, point.Y);
        }

        /// <summary>
        /// Real injected mouse input. SetCursorPos does not reset the Windows idle timer; this does.
        /// The caller restores the pointer afterwards.
        /// </summary>
        internal static void SendActivityInput()
        {
            mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero);
        }

        internal static void LeftClick()
        {
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // left button down
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // left button up
        }

        /// <summary>Dark title bar on Windows 10/11; all calls are best-effort.</summary>
        internal static void ApplyDarkTitleBar(IntPtr window)
        {
            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(window, 20, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(window, 19, ref enabled, sizeof(int));
                }

                // Windows 11 caption, text and border colors (COLORREF is 0x00BBGGRR).
                int caption = 0x00140F0D;
                int text = 0x00F4EEEB;
                int border = 0x00372C27;
                DwmSetWindowAttribute(window, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(window, 36, ref text, sizeof(int));
                DwmSetWindowAttribute(window, 34, ref border, sizeof(int));
            }
            catch (Exception)
            {
                // Older Windows builds simply keep their default title bar.
            }
        }

        /// <summary>
        /// Returns the full bounds of the monitor under the point. Full monitor bounds (rather
        /// than the work area) keep the starting point itself inside the allowed area even when
        /// the cursor starts over the taskbar.
        /// </summary>
        internal static Rectangle GetMonitorBounds(Point point)
        {
            Point32 nativePoint = new Point32(point.X, point.Y);
            IntPtr monitor = MonitorFromPoint(nativePoint, MonitorDefaultToNearest);
            MonitorInfo info = new MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(MonitorInfo));

            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                return Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
            }

            return new Rectangle(0, 0, GetSystemMetrics(SmCxScreen), GetSystemMetrics(SmCyScreen));
        }
    }

    /// <summary>Pure geometry used by the engine and by the automated tests.</summary>
    internal static class MovementMath
    {
        /// <summary>
        /// Picks a random point in the disc centered on <paramref name="center"/>.
        /// A uniform-angle + square-root-distance pair gives an even distribution over the disc.
        /// Every candidate is clamped to the monitor and checked against x^2 + y^2 &lt;= r^2.
        /// Invalid candidates are rejected and regenerated. The center is a guaranteed fallback.
        /// </summary>
        internal static Point RandomDestination(Random random, Point center, int radius, Rectangle bounds)
        {
            if (radius <= 0)
            {
                return center;
            }

            for (int attempt = 0; attempt < 200; attempt++)
            {
                double angle = random.NextDouble() * Math.PI * 2.0;
                double distance = radius * Math.Sqrt(random.NextDouble());
                int x = (int)Math.Round(center.X + distance * Math.Cos(angle));
                int y = (int)Math.Round(center.Y + distance * Math.Sin(angle));

                Point candidate = ClampToArea(new Point(x, y), bounds);
                if (IsInsideCircle(candidate, center, radius))
                {
                    return candidate;
                }
            }

            return ClampToArea(center, bounds);
        }

        internal static bool IsInsideCircle(Point point, Point center, int radius)
        {
            long dx = point.X - center.X;
            long dy = point.Y - center.Y;
            return dx * dx + dy * dy <= (long)radius * radius;
        }

        internal static Point ClampToArea(Point point, Rectangle bounds)
        {
            int minX = bounds.Left;
            int minY = bounds.Top;
            int maxX = bounds.Right - 1;
            int maxY = bounds.Bottom - 1;

            if (maxX < minX)
            {
                maxX = minX;
            }
            if (maxY < minY)
            {
                maxY = minY;
            }

            int x = Math.Min(Math.Max(point.X, minX), maxX);
            int y = Math.Min(Math.Max(point.Y, minY), maxY);
            return new Point(x, y);
        }

        /// <summary>
        /// Clamps a point to the monitor and then pulls it back toward the center until it is
        /// inside the circle. Used as a final guard during interpolation.
        /// </summary>
        internal static Point ClampToCircle(Point point, Point center, int radius, Rectangle bounds)
        {
            Point candidate = ClampToArea(point, bounds);
            if (IsInsideCircle(candidate, center, radius))
            {
                return candidate;
            }

            double dx = candidate.X - center.X;
            double dy = candidate.Y - center.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 0.0001)
            {
                return candidate;
            }

            // Step inward in one-pixel increments; rounding and monitor clamping can otherwise
            // leave a point a fraction of a pixel outside the mathematical boundary.
            for (int inset = 1; inset <= radius + 2; inset++)
            {
                double safeDistance = radius - inset;
                if (safeDistance < 0)
                {
                    break;
                }

                int x = center.X + (int)Math.Round(dx / distance * safeDistance);
                int y = center.Y + (int)Math.Round(dy / distance * safeDistance);
                Point inward = ClampToArea(new Point(x, y), bounds);
                if (IsInsideCircle(inward, center, radius))
                {
                    return inward;
                }
            }

            return ClampToArea(center, bounds);
        }

        internal static Point Lerp(Point from, Point to, double amount)
        {
            int x = (int)Math.Round(from.X + (to.X - from.X) * amount);
            int y = (int)Math.Round(from.Y + (to.Y - from.Y) * amount);
            return new Point(x, y);
        }
    }

    /// <summary>What the armed session should be doing right now.</summary>
    internal enum SchedulePhase
    {
        Active,
        OutsideSchedule,
        Sleeping
    }

    /// <summary>Daily time-window math shared by the scheduler and its tests.</summary>
    internal static class ScheduleMath
    {
        /// <summary>
        /// True inside a daily window. A window that starts later than it ends wraps past
        /// midnight. Equal endpoints describe an empty window and are never active.
        /// </summary>
        internal static bool IsWithinWindow(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            if (start == end)
            {
                return false;
            }

            if (start < end)
            {
                return now >= start && now < end;
            }

            // Overnight window, for example 22:00 to 06:00.
            return now >= start || now < end;
        }

        /// <summary>
        /// The schedule window is checked first, then sleep. Sleep can pause the middle of a
        /// scheduled day, while time outside the schedule always wins over sleep.
        /// </summary>
        internal static SchedulePhase Evaluate(
            TimeSpan now,
            bool scheduleEnabled,
            TimeSpan scheduleStart,
            TimeSpan scheduleEnd,
            bool sleepEnabled,
            TimeSpan sleepStart,
            TimeSpan sleepEnd)
        {
            if (scheduleEnabled && !IsWithinWindow(now, scheduleStart, scheduleEnd))
            {
                return SchedulePhase.OutsideSchedule;
            }

            if (sleepEnabled && IsWithinWindow(now, sleepStart, sleepEnd))
            {
                return SchedulePhase.Sleeping;
            }

            return SchedulePhase.Active;
        }

        internal static string FormatTime(TimeSpan time)
        {
            return time.ToString(@"hh\:mm");
        }
    }

    /// <summary>
    /// Owns the cancellable movement loop. It never touches the GUI directly; events are
    /// raised for the form to marshal onto the UI thread.
    /// </summary>
    internal sealed class CursorEngine
    {
        private readonly Func<Point> getCursor;
        private readonly Action<Point> setCursor;
        private readonly Func<Point, Rectangle> getMonitorBounds;
        private readonly Action sendInput;
        private readonly Action click;
        private readonly object gate = new object();

        private CancellationTokenSource cancellation;
        private Point center;
        private Rectangle screenBounds;
        private int radius;
        private DateTime nextMoveAt;

        internal event Action<string> Activity;
        internal event Action<DateTime> NextMovementScheduled;
        internal event Action<Point> CursorMoved;
        internal event Action Stopped;

        internal CursorEngine()
            : this(
                NativeApi.GetCursorPosition,
                NativeApi.SetCursorPosition,
                NativeApi.GetMonitorBounds,
                NativeApi.SendActivityInput,
                NativeApi.LeftClick)
        {
        }

        internal CursorEngine(Func<Point> getCursor, Action<Point> setCursor, Func<Point, Rectangle> getMonitorBounds)
            : this(getCursor, setCursor, getMonitorBounds, null, null)
        {
        }

        internal CursorEngine(
            Func<Point> getCursor,
            Action<Point> setCursor,
            Func<Point, Rectangle> getMonitorBounds,
            Action sendInput)
            : this(getCursor, setCursor, getMonitorBounds, sendInput, null)
        {
        }

        internal CursorEngine(
            Func<Point> getCursor,
            Action<Point> setCursor,
            Func<Point, Rectangle> getMonitorBounds,
            Action sendInput,
            Action click)
        {
            this.getCursor = getCursor;
            this.setCursor = setCursor;
            this.getMonitorBounds = getMonitorBounds;
            this.sendInput = sendInput;
            this.click = click;
        }

        internal bool IsRunning
        {
            get
            {
                lock (gate)
                {
                    return cancellation != null;
                }
            }
        }

        internal Point Center
        {
            get
            {
                lock (gate)
                {
                    return center;
                }
            }
        }

        internal Rectangle ScreenBounds
        {
            get
            {
                lock (gate)
                {
                    return screenBounds;
                }
            }
        }

        internal int Radius
        {
            get
            {
                lock (gate)
                {
                    return radius;
                }
            }
        }

        internal DateTime NextMoveAt
        {
            get
            {
                lock (gate)
                {
                    return nextMoveAt;
                }
            }
        }

        /// <summary>
        /// Captures the current cursor position as the fixed center, then starts a background
        /// loop. Only one loop can run at a time; callers must stop it before starting again.
        /// </summary>
        internal void Start(int movementRadius, double minimumIntervalSeconds, double maximumIntervalSeconds)
        {
            StartCore(true, Point.Empty, Rectangle.Empty, movementRadius, minimumIntervalSeconds, maximumIntervalSeconds);
        }

        /// <summary>Starts at an already captured center so a paused session keeps its box.</summary>
        internal void StartAt(
            Point fixedCenter,
            int movementRadius,
            double minimumIntervalSeconds,
            double maximumIntervalSeconds)
        {
            StartCore(false, fixedCenter, Rectangle.Empty, movementRadius, minimumIntervalSeconds, maximumIntervalSeconds);
        }

        /// <summary>
        /// Starts inside a caller-supplied screen rectangle. The fixed box in the window uses this
        /// so every write and click stays on the box control even if the desktop is larger.
        /// </summary>
        internal void StartAt(
            Point fixedCenter,
            Rectangle fixedBounds,
            int movementRadius,
            double minimumIntervalSeconds,
            double maximumIntervalSeconds)
        {
            StartCore(false, fixedCenter, fixedBounds, movementRadius, minimumIntervalSeconds, maximumIntervalSeconds);
        }

        private void StartCore(
            bool captureCenter,
            Point fixedCenter,
            Rectangle fixedBounds,
            int movementRadius,
            double minimumIntervalSeconds,
            double maximumIntervalSeconds)
        {
            if (movementRadius <= 0)
            {
                throw new ArgumentOutOfRangeException("movementRadius", "Radius must be positive.");
            }
            if (minimumIntervalSeconds <= 0 || maximumIntervalSeconds <= 0 || minimumIntervalSeconds > maximumIntervalSeconds)
            {
                throw new ArgumentOutOfRangeException("minimumIntervalSeconds", "Intervals must be positive and minimum must not exceed maximum.");
            }

            CancellationTokenSource source;
            Point capturedCenter;
            Rectangle bounds;

            lock (gate)
            {
                if (cancellation != null)
                {
                    throw new InvalidOperationException("Cursor movement is already running.");
                }

                capturedCenter = captureCenter ? getCursor() : fixedCenter;
                bounds = fixedBounds.IsEmpty ? getMonitorBounds(capturedCenter) : fixedBounds;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    throw new InvalidOperationException("Could not determine the movement bounds.");
                }

                center = capturedCenter;
                screenBounds = bounds;
                radius = movementRadius;
                nextMoveAt = DateTime.MinValue;

                source = new CancellationTokenSource();
                cancellation = source;
            }

            // The async state machine keeps the loop alive until it completes; exceptions are
            // handled inside the loop, so no unobserved task can escape.
#pragma warning disable 4014
            RunLoopAsync(source, capturedCenter, bounds, movementRadius, minimumIntervalSeconds, maximumIntervalSeconds);
#pragma warning restore 4014
        }

        /// <summary>
        /// Cancels the pending delay or animation immediately. Detaching the token source first
        /// means the loop can no longer pass its IsActive check and therefore cannot move the
        /// cursor again, even if Start is called again right away.
        /// </summary>
        internal void Stop()
        {
            CancellationTokenSource source;
            lock (gate)
            {
                source = cancellation;
                if (source == null)
                {
                    return;
                }

                cancellation = null;
                nextMoveAt = DateTime.MinValue;
            }

            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The loop finished at the same moment; there is nothing left to cancel.
            }

            // The Stopped event is reserved for a loop that ends unexpectedly. A user-requested
            // stop is already reflected by the caller, so raising it here would duplicate UI work.
        }

        /// <summary>
        /// Sends one real mouse input so Windows and idle-based presence timers see activity,
        /// then restores the pointer. SetCursorPos alone does not reset that timer.
        /// </summary>
        internal bool NudgeInput()
        {
            if (sendInput == null)
            {
                return false;
            }

            lock (gate)
            {
                if (cancellation == null)
                {
                    return false;
                }

                Point current = getCursor();
                sendInput();
                setCursor(current);
                return true;
            }
        }

        /// <summary>
        /// Clicks only when the active loop still owns the cursor and the pointer is inside the
        /// circle, so STOP or a manual move can never produce a click outside the box.
        /// </summary>
        private bool TryClick(CancellationTokenSource source)
        {
            if (click == null)
            {
                return false;
            }

            lock (gate)
            {
                if (!object.ReferenceEquals(cancellation, source) || source.IsCancellationRequested)
                {
                    return false;
                }

                Point position = getCursor();
                if (!MovementMath.IsInsideCircle(position, center, radius))
                {
                    return false;
                }

                click();
                return true;
            }
        }

        private bool IsActive(CancellationTokenSource source)
        {
            lock (gate)
            {
                return object.ReferenceEquals(cancellation, source) && !source.IsCancellationRequested;
            }
        }

        /// <summary>
        /// Sets the cursor only while this exact loop is still the active one. Stop() takes the
        /// same lock before detaching the source, so once Stop() returns no in-flight step can
        /// write another cursor position.
        /// </summary>
        private bool TrySetCursor(CancellationTokenSource source, Point point)
        {
            lock (gate)
            {
                if (!object.ReferenceEquals(cancellation, source) || source.IsCancellationRequested)
                {
                    return false;
                }

                setCursor(point);
                return true;
            }
        }

        private async Task RunLoopAsync(
            CancellationTokenSource source,
            Point fixedCenter,
            Rectangle bounds,
            int movementRadius,
            double minimumIntervalSeconds,
            double maximumIntervalSeconds)
        {
            CancellationToken token = source.Token;
            Random random = CreateRandom();

            try
            {
                while (IsActive(source))
                {
                    double delaySeconds = minimumIntervalSeconds + random.NextDouble() * (maximumIntervalSeconds - minimumIntervalSeconds);
                    int delayMilliseconds = Math.Max(1, (int)Math.Round(delaySeconds * 1000.0));
                    int firstHalf = delayMilliseconds / 2;
                    int secondHalf = delayMilliseconds - firstHalf;
                    if (firstHalf < 1 && delayMilliseconds >= 1)
                    {
                        firstHalf = 1;
                        secondHalf = delayMilliseconds - 1;
                    }

                    DateTime scheduledMove = DateTime.Now.AddMilliseconds(firstHalf + secondHalf);
                    lock (gate)
                    {
                        if (object.ReferenceEquals(cancellation, source))
                        {
                            nextMoveAt = scheduledMove;
                        }
                    }

                    Action<DateTime> scheduledHandler = NextMovementScheduled;
                    if (scheduledHandler != null)
                    {
                        scheduledHandler(scheduledMove);
                    }

                    // Cancelling this delay is what makes STOP during a waiting period immediate.
                    if (firstHalf > 0)
                    {
                        await Task.Delay(firstHalf, token).ConfigureAwait(false);
                        if (!IsActive(source))
                        {
                            break;
                        }
                    }

                    if (TryClick(source))
                    {
                        Action<string> clickHandler = Activity;
                        if (clickHandler != null)
                        {
                            clickHandler("Click between movements");
                        }
                    }

                    if (secondHalf > 0)
                    {
                        await Task.Delay(secondHalf, token).ConfigureAwait(false);
                        if (!IsActive(source))
                        {
                            break;
                        }
                    }

                    Point destination = MovementMath.RandomDestination(random, fixedCenter, movementRadius, bounds);
                    if (!IsActive(source))
                    {
                        break;
                    }

                    lock (gate)
                    {
                        if (object.ReferenceEquals(cancellation, source))
                        {
                            nextMoveAt = DateTime.MinValue;
                        }
                    }

                    Action<string> activityHandler = Activity;
                    if (activityHandler != null)
                    {
                        activityHandler("Random movement");
                    }

                    await AnimateAsync(destination, source, fixedCenter, movementRadius, bounds, random).ConfigureAwait(false);
                    if (!IsActive(source))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when STOP or emergency stop cancels a delay or animation.
            }
            catch (Exception exception)
            {
                Action<string> activityHandler = Activity;
                if (activityHandler != null)
                {
                    activityHandler("Movement error: " + exception.Message);
                }
            }
            finally
            {
                bool stoppedItself;
                lock (gate)
                {
                    stoppedItself = object.ReferenceEquals(cancellation, source);
                    if (stoppedItself)
                    {
                        cancellation = null;
                        nextMoveAt = DateTime.MinValue;
                    }
                }

                source.Dispose();

                if (stoppedItself)
                {
                    Action handler = Stopped;
                    if (handler != null)
                    {
                        handler();
                    }
                }
            }
        }

        /// <summary>
        /// Animates from the current cursor position to the destination in short steps. Every
        /// step is checked against both the circle and the monitor before SetCursorPos is called.
        /// </summary>
        private async Task AnimateAsync(
            Point destination,
            CancellationTokenSource source,
            Point fixedCenter,
            int movementRadius,
            Rectangle bounds,
            Random random)
        {
            CancellationToken token = source.Token;
            Point start = getCursor();

            // If the user moved the cursor outside the circle by hand, snap the animation's
            // start point back inside first so this app never writes a position outside the boundary.
            if (!MovementMath.IsInsideCircle(start, fixedCenter, movementRadius))
            {
                start = MovementMath.ClampToCircle(start, fixedCenter, movementRadius, bounds);
                if (!TrySetCursor(source, start))
                {
                    return;
                }
            }

            int durationMilliseconds = 180 + random.Next(160);
            Stopwatch clock = Stopwatch.StartNew();

            while (true)
            {
                if (!IsActive(source))
                {
                    return;
                }

                double progress = clock.ElapsedMilliseconds / (double)durationMilliseconds;
                if (progress >= 1.0)
                {
                    break;
                }

                // Smoothstep: gentle start and end, no abrupt single-pixel jumps.
                double eased = progress * progress * (3.0 - 2.0 * progress);
                Point step = MovementMath.Lerp(start, destination, eased);
                step = MovementMath.ClampToCircle(step, fixedCenter, movementRadius, bounds);

                if (!TrySetCursor(source, step))
                {
                    return;
                }

                await Task.Delay(15, token).ConfigureAwait(false);
            }

            if (!TrySetCursor(source, destination))
            {
                return;
            }

            Action<Point> movedHandler = CursorMoved;
            if (movedHandler != null)
            {
                movedHandler(destination);
            }
        }

        /// <summary>A time-seeded, crypto-seeded Random is plenty for human-scale movement.</summary>
        private static Random CreateRandom()
        {
            byte[] seedBytes = new byte[4];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(seedBytes);
            }
            return new Random(BitConverter.ToInt32(seedBytes, 0));
        }
    }
}
