using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using CursorMoveApp;

/// <summary>
/// Headless tests for the geometry and the cancellable movement loop. The fake cursor records
/// every position the engine writes, so no real mouse movement is needed while testing.
/// </summary>
internal static class TestRunner
{
    private static int checks;
    private static int failures;

    private static int Main()
    {
        Console.WriteLine("CursorMove core tests");
        Console.WriteLine("=====================");

        Run("Random destinations stay inside the circle and monitor", TestGeometry);
        Run("Schedule and sleep windows wrap correctly and sleep can pause a scheduled day", TestScheduleWindows);
        Run("TEST 1 + TEST 5: movement stays inside radius, center is fixed", TestInsideBoundaryAndFixedCenter);
        Run("TEST 2: STOP during waiting cancels the pending movement", TestStopDuringWait);
        Run("TEST 3: emergency-style STOP during an animation", TestStopDuringAnimation);
        Run("TEST 4: START near a monitor edge stays on screen", TestMonitorEdge);
        Run("TEST 6: STOP, manual move, START captures a new center", TestNewCenter);
        Run("Clicks stay inside the fixed box and activity nudge is safe", TestClicksAndNudge);
        Run("Input validation rejects an invalid radius and interval range", TestValidation);

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine(string.Format("PASS - {0} checks", checks));
            return 0;
        }

        Console.WriteLine(string.Format("FAIL - {0} of {1} checks failed", failures, checks));
        return 1;
    }

    private static void Run(string name, Action test)
    {
        Console.WriteLine();
        Console.WriteLine(name);
        try
        {
            test();
        }
        catch (Exception exception)
        {
            Fail("unhandled exception: " + exception);
        }
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
        {
            Fail(message);
        }
    }

    private static void Fail(string message)
    {
        failures++;
        Console.WriteLine("  FAIL: " + message);
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (condition())
            {
                return true;
            }
            Thread.Sleep(5);
        }
        return condition();
    }

    private static CursorEngine CreateEngine(FakeCursor cursor, Rectangle bounds)
    {
        return new CursorEngine(
            cursor.GetPosition,
            cursor.SetPosition,
            delegate(Point point) { return bounds; });
    }

    private static void TestGeometry()
    {
        Random random = new Random(2026);
        Rectangle[] monitors = new Rectangle[]
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(-1920, 0, 1920, 1080),
            new Rectangle(1920, 0, 2560, 1440)
        };
        Point[][] centers = new Point[][]
        {
            new Point[] { new Point(960, 540), new Point(2, 2), new Point(1917, 1077), new Point(0, 0), new Point(1919, 1079), new Point(1910, 5) },
            new Point[] { new Point(-1918, 5), new Point(-960, 540), new Point(-5, 1075), new Point(-1920, 0), new Point(-1, 1079) },
            new Point[] { new Point(1922, 2), new Point(4478, 1438), new Point(3200, 720), new Point(4480, 0) }
        };
        int[] radii = new int[] { 10, 30, 50, 100, 200 };
        HashSet<Point> distinct = new HashSet<Point>();
        int generated = 0;

        for (int monitorIndex = 0; monitorIndex < monitors.Length; monitorIndex++)
        {
            Rectangle bounds = monitors[monitorIndex];
            for (int centerIndex = 0; centerIndex < centers[monitorIndex].Length; centerIndex++)
            {
                Point center = centers[monitorIndex][centerIndex];
                for (int radiusIndex = 0; radiusIndex < radii.Length; radiusIndex++)
                {
                    int radius = radii[radiusIndex];
                    for (int i = 0; i < 400; i++)
                    {
                        Point destination = MovementMath.RandomDestination(random, center, radius, bounds);
                        generated++;
                        Check(bounds.Contains(destination), "destination left the monitor bounds");
                        Check(MovementMath.IsInsideCircle(destination, center, radius), "destination left the radius");
                        if (i < 20)
                        {
                            distinct.Add(destination);
                        }
                    }
                }
            }
        }

        Check(distinct.Count > 100, "random destinations are not varied enough");
        Check(generated == 30000, "geometry sample count changed unexpectedly");
    }

    private static void TestScheduleWindows()
    {
        TimeSpan nine = TimeSpan.FromHours(9);
        TimeSpan seventeen = TimeSpan.FromHours(17);
        TimeSpan twentyTwo = TimeSpan.FromHours(22);
        TimeSpan six = TimeSpan.FromHours(6);

        Check(!ScheduleMath.IsWithinWindow(TimeSpan.FromMinutes(8 * 60 + 59), nine, seventeen), "day window started too early");
        Check(ScheduleMath.IsWithinWindow(nine, nine, seventeen), "day window excluded its start");
        Check(ScheduleMath.IsWithinWindow(TimeSpan.FromHours(12), nine, seventeen), "day window excluded the middle");
        Check(ScheduleMath.IsWithinWindow(TimeSpan.FromMinutes(16 * 60 + 59), nine, seventeen), "day window ended too early");
        Check(!ScheduleMath.IsWithinWindow(seventeen, nine, seventeen), "day window included its end");

        Check(!ScheduleMath.IsWithinWindow(TimeSpan.FromMinutes(21 * 60 + 59), twentyTwo, six), "overnight window started too early");
        Check(ScheduleMath.IsWithinWindow(twentyTwo, twentyTwo, six), "overnight window excluded its start");
        Check(ScheduleMath.IsWithinWindow(TimeSpan.Zero, twentyTwo, six), "overnight window excluded midnight");
        Check(ScheduleMath.IsWithinWindow(TimeSpan.FromMinutes(5 * 60 + 59), twentyTwo, six), "overnight window ended too early");
        Check(!ScheduleMath.IsWithinWindow(six, twentyTwo, six), "overnight window included its end");
        Check(!ScheduleMath.IsWithinWindow(nine, nine, nine), "empty window was active");

        SchedulePhase bothOff = ScheduleMath.Evaluate(
            TimeSpan.FromHours(12), false, nine, seventeen, false, twentyTwo, six);
        Check(bothOff == SchedulePhase.Active, "windows disengaged should be active");

        SchedulePhase outsideSchedule = ScheduleMath.Evaluate(
            TimeSpan.FromHours(7), true, nine, seventeen, true, twentyTwo, six);
        Check(outsideSchedule == SchedulePhase.OutsideSchedule, "outside schedule should win over sleep");

        SchedulePhase sleeping = ScheduleMath.Evaluate(
            TimeSpan.FromHours(12), true, nine, seventeen, true, nine, seventeen);
        Check(sleeping == SchedulePhase.Sleeping, "sleep window should pause a scheduled day");

        SchedulePhase active = ScheduleMath.Evaluate(
            TimeSpan.FromHours(12), true, nine, seventeen, true, twentyTwo, six);
        Check(active == SchedulePhase.Active, "active day should be active when sleep is outside it");

        SchedulePhase sleepOnly = ScheduleMath.Evaluate(
            TimeSpan.FromHours(23), false, nine, seventeen, true, twentyTwo, six);
        Check(sleepOnly == SchedulePhase.Sleeping, "sleep should work without a schedule");

        Check(ScheduleMath.FormatTime(TimeSpan.FromMinutes(9 * 60 + 5)) == "09:05", "time formatting changed");
    }

    private static void TestInsideBoundaryAndFixedCenter()
    {
        Rectangle bounds = new Rectangle(0, 0, 1920, 1080);
        Point captured = new Point(960, 540);
        FakeCursor cursor = new FakeCursor(captured);
        CursorEngine engine = CreateEngine(cursor, bounds);
        int completed = 0;
        List<Point> destinations = new List<Point>();

        engine.CursorMoved += delegate(Point point)
        {
            lock (destinations)
            {
                completed++;
                destinations.Add(point);
            }
        };

        engine.Start(30, 0.03, 0.06);
        Check(engine.Center == captured, "START did not capture the current cursor position");
        Check(engine.Radius == 30, "radius was not stored");

        bool moved = WaitUntil(delegate
        {
            lock (destinations)
            {
                return completed >= 3;
            }
        }, 5000);
        Check(moved, "engine did not complete three random movements in time");

        engine.Stop();
        int writesAtStop = cursor.AutomatedCount;
        Thread.Sleep(400);

        Check(!engine.IsRunning, "engine reported running after STOP");
        Check(cursor.AutomatedCount == writesAtStop, "cursor was written after STOP returned");
        Check(engine.Center == captured, "movement center changed during the session");

        List<Point> allWrites = cursor.AutomatedSnapshot();
        foreach (Point point in allWrites)
        {
            Check(MovementMath.IsInsideCircle(point, captured, 30), "interpolated position left the radius");
            Check(bounds.Contains(point), "interpolated position left the monitor");
        }

        lock (destinations)
        {
            Check(destinations.Count >= 3, "fewer than three destinations were recorded");
            HashSet<Point> uniqueDestinations = new HashSet<Point>(destinations);
            Check(uniqueDestinations.Count >= 2, "destinations were not randomized");
            foreach (Point point in destinations)
            {
                Check(MovementMath.IsInsideCircle(point, captured, 30), "destination left the radius");
                Check(bounds.Contains(point), "destination left the monitor");
            }
        }

        Console.WriteLine(string.Format(
            "  center stayed at {0},{1}; {2} cursor writes checked; {3} destinations",
            captured.X,
            captured.Y,
            allWrites.Count,
            completed));
    }

    private static void TestStopDuringWait()
    {
        Rectangle bounds = new Rectangle(0, 0, 1000, 800);
        FakeCursor cursor = new FakeCursor(new Point(500, 400));
        CursorEngine engine = CreateEngine(cursor, bounds);
        int schedules = 0;

        engine.NextMovementScheduled += delegate { schedules++; };
        engine.Start(30, 0.8, 1.2);
        Check(schedules > 0, "first movement was not scheduled");
        Thread.Sleep(100);
        Check(cursor.AutomatedCount == 0, "cursor moved before the first delay expired");

        Stopwatch stopClock = Stopwatch.StartNew();
        engine.Stop();
        stopClock.Stop();
        Check(stopClock.ElapsedMilliseconds < 100, "STOP blocked for too long");

        Thread.Sleep(1400);
        Check(cursor.AutomatedCount == 0, "cursor moved after STOP cancelled the pending wait");
        Check(!engine.IsRunning, "engine still running after STOP");
        Console.WriteLine("  pending delay cancelled with zero cursor writes");
    }

    private static void TestStopDuringAnimation()
    {
        Rectangle bounds = new Rectangle(0, 0, 1000, 800);
        Point center = new Point(500, 400);
        FakeCursor cursor = new FakeCursor(center);
        CursorEngine engine = CreateEngine(cursor, bounds);

        engine.Start(50, 0.02, 0.03);
        bool animating = WaitUntil(delegate { return cursor.AutomatedCount > 2; }, 2500);
        Check(animating, "animation did not start in time");

        Stopwatch stopClock = Stopwatch.StartNew();
        engine.Stop();
        stopClock.Stop();
        int writesAtStop = cursor.AutomatedCount;

        Thread.Sleep(500);
        Check(stopClock.ElapsedMilliseconds < 100, "emergency stop blocked for too long");
        Check(cursor.AutomatedCount == writesAtStop, "cursor kept moving after emergency stop");
        Check(!engine.IsRunning, "engine still running after emergency stop");

        foreach (Point point in cursor.AutomatedSnapshot())
        {
            Check(MovementMath.IsInsideCircle(point, center, 50), "animated position left the radius");
            Check(bounds.Contains(point), "animated position left the monitor");
        }

        Console.WriteLine(string.Format(
            "  stopped mid-animation in {0} ms; no further writes",
            stopClock.ElapsedMilliseconds));
    }

    private static void TestMonitorEdge()
    {
        Rectangle bounds = new Rectangle(0, 0, 1920, 1080);

        TestEdgePoint(bounds, new Point(1916, 1076), "bottom-right corner");
        TestEdgePoint(bounds, new Point(4, 4), "top-left corner");
    }

    private static void TestEdgePoint(Rectangle bounds, Point center, string label)
    {
        FakeCursor cursor = new FakeCursor(center);
        CursorEngine engine = CreateEngine(cursor, bounds);
        int completed = 0;

        engine.CursorMoved += delegate { Interlocked.Increment(ref completed); };
        engine.Start(200, 0.02, 0.03);
        Check(engine.Center == center, "edge center was not captured");
        bool moved = WaitUntil(delegate { return completed >= 2; }, 3500);
        Check(moved, "edge movement did not complete in time");
        engine.Stop();
        Thread.Sleep(150);

        foreach (Point point in cursor.AutomatedSnapshot())
        {
            Check(bounds.Contains(point), "edge movement left the screen (" + label + ")");
            Check(MovementMath.IsInsideCircle(point, center, 200), "edge movement left the radius (" + label + ")");
        }

        Console.WriteLine(string.Format(
            "  {0}: {1} writes, all inside screen and radius",
            label,
            cursor.AutomatedCount));
    }

    private static void TestNewCenter()
    {
        Rectangle bounds = new Rectangle(0, 0, 1200, 900);
        FakeCursor cursor = new FakeCursor(new Point(400, 400));
        CursorEngine engine = CreateEngine(cursor, bounds);
        int completed = 0;

        engine.CursorMoved += delegate { Interlocked.Increment(ref completed); };

        engine.Start(40, 0.02, 0.03);
        Check(engine.Center == new Point(400, 400), "first center was not captured");
        bool moved = WaitUntil(delegate { return completed >= 1; }, 2500);
        Check(moved, "first session did not move");
        engine.Stop();

        Thread.Sleep(100);
        cursor.MoveManually(new Point(750, 300));
        int mark = cursor.AutomatedCount;

        engine.Start(40, 0.02, 0.03);
        Check(engine.Center == new Point(750, 300), "second START did not capture the new cursor position");
        int completedBefore = completed;
        bool movedAgain = WaitUntil(delegate { return completed > completedBefore; }, 2500);
        Check(movedAgain, "second session did not move");
        engine.Stop();
        Thread.Sleep(150);

        List<Point> writes = cursor.AutomatedSnapshot();
        for (int i = mark; i < writes.Count; i++)
        {
            Check(MovementMath.IsInsideCircle(writes[i], new Point(750, 300), 40), "new session used the old center");
            Check(bounds.Contains(writes[i]), "new session left the monitor");
        }
        Check(engine.Center == new Point(750, 300), "new center changed during the second session");
        Console.WriteLine("  second START captured 750,300 and stayed inside the new radius");
    }

    private static void TestClicksAndNudge()
    {
        Rectangle bounds = new Rectangle(0, 0, 1920, 1080);
        Rectangle box = new Rectangle(900, 500, 120, 80);
        Point fixedCenter = new Point(960, 540);
        FakeCursor cursor = new FakeCursor(new Point(200, 200));
        object clickGate = new object();
        List<Point> clicks = new List<Point>();
        int nudges = 0;

        CursorEngine engine = new CursorEngine(
            cursor.GetPosition,
            cursor.SetPosition,
            delegate(Point point) { return bounds; },
            delegate { Interlocked.Increment(ref nudges); },
            delegate
            {
                lock (clickGate)
                {
                    clicks.Add(cursor.GetPosition());
                }
            });

        Check(!engine.NudgeInput(), "nudge fired while stopped");

        engine.StartAt(fixedCenter, box, 30, 0.05, 0.05);
        Check(engine.Center == fixedCenter, "fixed center was not used");

        bool clicked = WaitUntil(delegate
        {
            lock (clickGate)
            {
                return clicks.Count > 0;
            }
        }, 4000);
        Check(clicked, "no click happened while running");

        Point clickPoint;
        lock (clickGate)
        {
            clickPoint = clicks[0];
        }
        Check(MovementMath.IsInsideCircle(clickPoint, fixedCenter, 30), "click was outside the circle");
        Check(box.Contains(clickPoint), "click was outside the fixed box");
        foreach (Point write in cursor.AutomatedSnapshot())
        {
            Check(box.Contains(write), "a write left the fixed box");
        }

        Point beforeNudge = cursor.GetPosition();
        Check(engine.NudgeInput(), "nudge was ignored while running");
        Check(nudges == 1, "nudge did not send input");
        Check(cursor.GetPosition() == beforeNudge, "nudge moved the pointer");

        engine.Stop();
        int clicksAtStop;
        lock (clickGate)
        {
            clicksAtStop = clicks.Count;
        }
        Thread.Sleep(300);
        lock (clickGate)
        {
            Check(clicks.Count == clicksAtStop, "click happened after STOP");
        }
        Check(!engine.NudgeInput(), "nudge fired after STOP");

        cursor.MoveManually(new Point(100, 100));
        engine.StartAt(fixedCenter, box, 30, 600, 600);
        Check(engine.Center == fixedCenter, "resuming changed the fixed center");
        engine.Stop();

        Console.WriteLine("  click stayed inside 30 px of 960,540 and inside the fixed box; nudge restored the pointer");
    }

    private static void TestValidation()
    {
        Rectangle bounds = new Rectangle(0, 0, 1000, 800);
        FakeCursor cursor = new FakeCursor(new Point(500, 400));
        CursorEngine engine = CreateEngine(cursor, bounds);
        bool radiusRejected = false;
        bool rangeRejected = false;

        try
        {
            engine.Start(0, 20, 60);
        }
        catch (ArgumentOutOfRangeException)
        {
            radiusRejected = true;
        }

        try
        {
            engine.Start(30, 60, 20);
        }
        catch (ArgumentOutOfRangeException)
        {
            rangeRejected = true;
        }

        Check(radiusRejected, "radius 0 was accepted");
        Check(rangeRejected, "minimum greater than maximum was accepted");
        Check(!engine.IsRunning, "failed validation started the engine");
    }

    private sealed class FakeCursor
    {
        private readonly object gate = new object();
        private readonly List<Point> automatedWrites = new List<Point>();
        private Point position;

        internal FakeCursor(Point start)
        {
            position = start;
        }

        internal Point GetPosition()
        {
            lock (gate)
            {
                return position;
            }
        }

        internal void SetPosition(Point point)
        {
            lock (gate)
            {
                position = point;
                automatedWrites.Add(point);
            }
        }

        internal void MoveManually(Point point)
        {
            lock (gate)
            {
                position = point;
            }
        }

        internal int AutomatedCount
        {
            get
            {
                lock (gate)
                {
                    return automatedWrites.Count;
                }
            }
        }

        internal List<Point> AutomatedSnapshot()
        {
            lock (gate)
            {
                return new List<Point>(automatedWrites);
            }
        }
    }
}
