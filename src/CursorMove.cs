using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("CurDrif")]
[assembly: AssemblyDescription("Controlled, randomized cursor movement between waits. Stay in motion.")]
[assembly: AssemblyProduct("CurDrif")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace CursorMoveApp
{
    internal static class Palette
    {
        internal static readonly Color Background = Color.FromArgb(13, 15, 20);
        internal static readonly Color Card = Color.FromArgb(23, 26, 33);
        internal static readonly Color CardBorder = Color.FromArgb(39, 44, 55);
        internal static readonly Color Text = Color.FromArgb(235, 238, 244);
        internal static readonly Color Muted = Color.FromArgb(142, 151, 166);
        internal static readonly Color Dim = Color.FromArgb(100, 109, 124);
        internal static readonly Color Input = Color.FromArgb(14, 17, 23);
        internal static readonly Color Green = Color.FromArgb(34, 197, 94);
        internal static readonly Color GreenBright = Color.FromArgb(74, 222, 128);
        internal static readonly Color Red = Color.FromArgb(239, 68, 68);
        internal static readonly Color RedBright = Color.FromArgb(252, 165, 165);
        internal static readonly Color Blue = Color.FromArgb(96, 165, 250);
    }

    internal static class Program
    {
        internal const string MutexName = "Local\\CursorMove.SingleInstance";
        internal const string StopEventName = "Local\\CursorMove.StopRequest";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--stop", StringComparison.OrdinalIgnoreCase))
            {
                RequestStop();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "CursorMove is already running.\n\nUse the existing window, or press F9 / ESC to stop it.",
                        "CursorMove",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    NativeApi.SetProcessDPIAware();
                }
                catch (Exception)
                {
                    // DPI awareness is cosmetic; ignore if the API is unavailable.
                }

                try
                {
                    using (EventWaitHandle stopRequest = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName))
                    {
                        Application.Run(new MainForm(stopRequest));
                    }
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        "CursorMove could not start.\n\n" + exception.Message,
                        "CursorMove",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// --stop mode: no GUI, no movement loop. It only signals the running instance's
        /// named event so StopCursorMove.bat can stop movement without force-killing the app.
        /// </summary>
        private static void RequestStop()
        {
            try
            {
                using (EventWaitHandle request = EventWaitHandle.OpenExisting(StopEventName))
                {
                    request.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Environment.ExitCode = 1;
            }
            catch (UnauthorizedAccessException)
            {
                Environment.ExitCode = 1;
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private const int HotkeyStart = 0x5101;
        private const int HotkeyStop = 0x5102;
        private const int HotkeyEmergency = 0x5103;

        private readonly EventWaitHandle stopRequest;
        private readonly CursorEngine engine = new CursorEngine();
        private readonly System.Windows.Forms.Timer countdownTimer = new System.Windows.Forms.Timer();
        private readonly ToolTip toolTip = new ToolTip();

        private StatusPill statusPill;
        private NumericUpDown radiusInput;
        private NumericUpDown minimumInput;
        private NumericUpDown maximumInput;
        private ToggleSwitch exactDelayToggle;
        private NumericUpDown exactDelayInput;
        private ToggleSwitch scheduleToggle;
        private NumericUpDown scheduleFromHourInput;
        private NumericUpDown scheduleFromMinuteInput;
        private NumericUpDown scheduleToHourInput;
        private NumericUpDown scheduleToMinuteInput;
        private ToggleSwitch sleepToggle;
        private NumericUpDown sleepFromHourInput;
        private NumericUpDown sleepFromMinuteInput;
        private NumericUpDown sleepToHourInput;
        private NumericUpDown sleepToMinuteInput;
        private Button startButton;
        private Button stopButton;
        private Label centerXValue;
        private Label centerYValue;
        private Label radiusValue;
        private Label nextValue;
        private BoundaryPreview preview;
        private ListBox logList;

        private bool armed;
        private Point sessionCenter;
        private Rectangle sessionBounds;
        private FormWindowState sessionWindowState;
        private DateTime lastActivityNudge;
        private int sessionRadius;
        private int sessionMinimumSeconds;
        private int sessionMaximumSeconds;
        private bool sessionScheduleEnabled;
        private bool sessionSleepEnabled;
        private TimeSpan sessionScheduleFrom;
        private TimeSpan sessionScheduleTo;
        private TimeSpan sessionSleepFrom;
        private TimeSpan sessionSleepTo;
        private SchedulePhase lastPhase;

        private bool escapeHotkeyRegistered;
        private bool startHotkeyRegistered;
        private bool stopHotkeyRegistered;
        private volatile bool closing;

        internal MainForm(EventWaitHandle stopRequest)
        {
            this.stopRequest = stopRequest;

            InitializeWindow();
            BuildInterface();
            WireEvents();
            StartStopRequestListener();

            AddLog("Ready");
            ApplyStoppedInterface();
        }

        private void InitializeWindow()
        {
            Text = "CurDrif";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowIcon = true;
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 820);
            DoubleBuffered = true;
            KeyPreview = true;
        }

        private void BuildInterface()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Palette.Background;
            root.Padding = new Padding(16, 16, 16, 12);
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.Margin = new Padding(0);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 152F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 184F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildSettingsCard(), 0, 1);
            root.Controls.Add(BuildScheduleCard(), 0, 2);
            root.Controls.Add(BuildMiddleRow(), 0, 3);
            root.Controls.Add(BuildButtonRow(), 0, 4);
            root.Controls.Add(BuildLogCard(), 0, 5);
            root.Controls.Add(BuildFooter(), 0, 6);
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Palette.Background;
            header.Margin = new Padding(0, 0, 0, 8);
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Panel titles = new Panel();
            titles.Dock = DockStyle.Fill;
            titles.Margin = new Padding(0);
            titles.BackColor = Palette.Background;
            Label title = new Label();
            title.Text = "CURSORMOVE";
            title.ForeColor = Palette.Text;
            title.Font = new Font("Segoe UI", 19F, FontStyle.Bold, GraphicsUnit.Point);
            title.AutoSize = true;
            title.Location = new Point(0, 0);
            titles.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Stay in motion. Controlled random movement between waits.";
            subtitle.ForeColor = Palette.Muted;
            subtitle.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(2, 46);
            titles.Controls.Add(subtitle);
            header.Controls.Add(titles, 0, 0);

            Panel statusHost = new Panel();
            statusHost.Dock = DockStyle.Fill;
            statusHost.Margin = new Padding(0);
            statusHost.BackColor = Palette.Background;
            statusPill = new StatusPill();
            statusPill.Location = new Point(2, 8);
            statusHost.Controls.Add(statusPill);
            header.Controls.Add(statusHost, 1, 0);

            return header;
        }

        private Control BuildSettingsCard()
        {
            Panel card = CreateCard();
            card.Margin = new Padding(0, 0, 0, 8);
            card.Padding = new Padding(14, 28, 14, 8);

            Label heading = new Label();
            heading.Text = "SETTINGS";
            heading.ForeColor = Palette.Dim;
            heading.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
            heading.AutoSize = true;
            heading.Location = new Point(14, 9);
            card.Controls.Add(heading);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.BackColor = Palette.Card;
            grid.Margin = new Padding(0);
            grid.ColumnCount = 3;
            grid.RowCount = 3;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));

            grid.Controls.Add(CreateNumberField("MOVEMENT RADIUS", "px", 10, 200, 30, 5, true, out radiusInput), 0, 0);
            grid.Controls.Add(CreateNumberField("MINIMUM INTERVAL", "sec", 5, 300, 20, 5, false, out minimumInput), 1, 0);
            grid.Controls.Add(CreateNumberField("MAXIMUM INTERVAL", "sec", 5, 600, 60, 5, false, out maximumInput), 2, 0);

            Panel exactRow = new Panel();
            exactRow.Dock = DockStyle.Fill;
            exactRow.BackColor = Palette.Card;
            exactRow.Margin = new Padding(0);

            exactDelayToggle = new ToggleSwitch();
            exactDelayToggle.Text = "Exact delay";
            exactDelayToggle.Width = 132;
            exactDelayToggle.Location = new Point(0, 2);
            exactRow.Controls.Add(exactDelayToggle);

            exactDelayInput = new NumericUpDown();
            exactDelayInput.Minimum = 5;
            exactDelayInput.Maximum = 600;
            exactDelayInput.Value = 20;
            exactDelayInput.Increment = 5;
            exactDelayInput.Width = 68;
            exactDelayInput.Location = new Point(136, 0);
            exactDelayInput.BorderStyle = BorderStyle.FixedSingle;
            exactDelayInput.BackColor = Palette.Input;
            exactDelayInput.ForeColor = Palette.Text;
            exactDelayInput.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            exactDelayInput.TextAlign = HorizontalAlignment.Right;
            exactRow.Controls.Add(exactDelayInput);

            Label exactUnit = new Label();
            exactUnit.Text = "sec";
            exactUnit.ForeColor = Palette.Dim;
            exactUnit.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            exactUnit.AutoSize = true;
            exactUnit.Location = new Point(208, 5);
            exactRow.Controls.Add(exactUnit);

            Label exactHint = new Label();
            exactHint.Text = "Off: random between min and max";
            exactHint.ForeColor = Palette.Dim;
            exactHint.Font = new Font("Segoe UI", 7.25F, FontStyle.Regular, GraphicsUnit.Point);
            exactHint.AutoSize = true;
            exactHint.Location = new Point(252, 7);
            exactRow.Controls.Add(exactHint);

            toolTip.SetToolTip(exactDelayToggle, "Use one fixed delay instead of a random minimum/maximum range.");
            toolTip.SetToolTip(exactDelayInput, "Fixed delay between movements: 5 to 600 seconds.");

            grid.Controls.Add(exactRow, 0, 1);
            grid.SetColumnSpan(exactRow, 3);

            Label hint = new Label();
            hint.Text = "Each move ends with a left click inside the fixed box.";
            hint.ForeColor = Palette.Dim;
            hint.Font = new Font("Segoe UI", 7.25F, FontStyle.Regular, GraphicsUnit.Point);
            hint.AutoSize = false;
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.BottomLeft;
            grid.Controls.Add(hint, 0, 2);
            grid.SetColumnSpan(hint, 3);

            card.Controls.Add(grid);
            return card;
        }

        private Control BuildScheduleCard()
        {
            Panel card = CreateCard();
            card.Margin = new Padding(0, 0, 0, 8);
            card.Padding = new Padding(14, 28, 14, 8);

            Label heading = new Label();
            heading.Text = "SCHEDULE & SLEEP";
            heading.UseMnemonic = false;
            heading.ForeColor = Palette.Dim;
            heading.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
            heading.AutoSize = true;
            heading.Location = new Point(14, 9);
            card.Controls.Add(heading);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.BackColor = Palette.Card;
            grid.Margin = new Padding(0);
            grid.ColumnCount = 3;
            grid.RowCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            Panel scheduleHost = new Panel();
            scheduleHost.Dock = DockStyle.Fill;
            scheduleHost.BackColor = Palette.Card;
            scheduleHost.Margin = new Padding(0);
            scheduleToggle = new ToggleSwitch();
            scheduleToggle.Text = "Run on schedule";
            scheduleToggle.Width = 156;
            scheduleToggle.Location = new Point(0, 19);
            scheduleHost.Controls.Add(scheduleToggle);
            grid.Controls.Add(scheduleHost, 0, 0);
            grid.Controls.Add(
                CreateTimeField("FROM", 9, 0, out scheduleFromHourInput, out scheduleFromMinuteInput),
                1,
                0);
            grid.Controls.Add(
                CreateTimeField("TO", 17, 0, out scheduleToHourInput, out scheduleToMinuteInput),
                2,
                0);

            Panel sleepHost = new Panel();
            sleepHost.Dock = DockStyle.Fill;
            sleepHost.BackColor = Palette.Card;
            sleepHost.Margin = new Padding(0);
            sleepToggle = new ToggleSwitch();
            sleepToggle.Text = "Sleep";
            sleepToggle.Width = 156;
            sleepToggle.Location = new Point(0, 19);
            sleepHost.Controls.Add(sleepToggle);
            grid.Controls.Add(sleepHost, 0, 1);
            grid.Controls.Add(
                CreateTimeField("FROM", 23, 0, out sleepFromHourInput, out sleepFromMinuteInput),
                1,
                1);
            grid.Controls.Add(
                CreateTimeField("TO", 7, 0, out sleepToHourInput, out sleepToMinuteInput),
                2,
                1);

            toolTip.SetToolTip(scheduleToggle, "Arm the schedule: movement only runs inside this daily window. START is still required.");
            toolTip.SetToolTip(sleepToggle, "Pause movement inside this daily window. Sleep works with or without a schedule.");
            toolTip.SetToolTip(
                scheduleFromHourInput,
                "24-hour time. An end earlier than start wraps past midnight.");
            toolTip.SetToolTip(
                scheduleToHourInput,
                "24-hour time. An end earlier than start wraps past midnight.");
            toolTip.SetToolTip(
                sleepFromHourInput,
                "24-hour time. An end earlier than start wraps past midnight.");
            toolTip.SetToolTip(
                sleepToHourInput,
                "24-hour time. An end earlier than start wraps past midnight.");

            card.Controls.Add(grid);
            return card;
        }

        private Control CreateTimeField(
            string caption,
            int defaultHour,
            int defaultMinute,
            out NumericUpDown hourInput,
            out NumericUpDown minuteInput)
        {
            Panel field = new Panel();
            field.Dock = DockStyle.Fill;
            field.BackColor = Palette.Card;
            field.Margin = new Padding(0, 0, 10, 0);

            Label label = new Label();
            label.Text = caption;
            label.ForeColor = Palette.Muted;
            label.Font = new Font("Segoe UI", 7.25F, FontStyle.Bold, GraphicsUnit.Point);
            label.AutoSize = true;
            label.Location = new Point(0, 0);
            field.Controls.Add(label);

            hourInput = CreateTimeInput(23, defaultHour, 1);
            hourInput.Location = new Point(0, 17);
            field.Controls.Add(hourInput);

            Label colon = new Label();
            colon.Text = ":";
            colon.ForeColor = Palette.Muted;
            colon.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
            colon.AutoSize = true;
            colon.Location = new Point(48, 21);
            field.Controls.Add(colon);

            minuteInput = CreateTimeInput(59, defaultMinute, 5);
            minuteInput.Location = new Point(56, 17);
            field.Controls.Add(minuteInput);

            return field;
        }

        private NumericUpDown CreateTimeInput(int maximum, int value, int increment)
        {
            TimeInput input = new TimeInput();
            input.Minimum = 0;
            input.Maximum = maximum;
            input.Value = value;
            input.Increment = increment;
            input.Width = 44;
            input.BorderStyle = BorderStyle.FixedSingle;
            input.BackColor = Palette.Input;
            input.ForeColor = Palette.Text;
            input.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            input.TextAlign = HorizontalAlignment.Right;
            return input;
        }

        private Control CreateNumberField(
            string labelText,
            string unitText,
            int minimum,
            int maximum,
            int value,
            int increment,
            bool isRadius,
            out NumericUpDown input)
        {
            Panel field = new Panel();
            field.Dock = DockStyle.Fill;
            field.BackColor = Palette.Card;
            field.Margin = new Padding(0, 0, 14, 0);

            Label label = new Label();
            label.Text = labelText;
            label.ForeColor = Palette.Muted;
            label.Font = new Font("Segoe UI", 7.25F, FontStyle.Bold, GraphicsUnit.Point);
            label.AutoSize = true;
            label.Location = new Point(0, 0);
            field.Controls.Add(label);

            input = new NumericUpDown();
            input.Minimum = minimum;
            input.Maximum = maximum;
            input.Value = value;
            input.Increment = increment;
            input.Width = 92;
            input.Location = new Point(0, 18);
            input.BorderStyle = BorderStyle.FixedSingle;
            input.BackColor = Palette.Input;
            input.ForeColor = Palette.Text;
            input.Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            input.TextAlign = HorizontalAlignment.Right;
            field.Controls.Add(input);

            Label unit = new Label();
            unit.Text = unitText;
            unit.ForeColor = Palette.Dim;
            unit.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            unit.AutoSize = true;
            unit.Location = new Point(98, 24);
            field.Controls.Add(unit);

            if (isRadius)
            {
                toolTip.SetToolTip(input, "Movement radius: 10 to 200 pixels. Default: 30 px.");
            }
            else
            {
                toolTip.SetToolTip(input, "Interval range: 5 to 300 seconds minimum, 5 to 600 seconds maximum.");
            }

            return field;
        }

        private Control BuildMiddleRow()
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.BackColor = Palette.Background;
            row.Margin = new Padding(0, 0, 0, 8);
            row.ColumnCount = 2;
            row.RowCount = 1;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));

            row.Controls.Add(BuildCenterCard(), 0, 0);
            row.Controls.Add(BuildPreviewCard(), 1, 0);
            return row;
        }

        private Control BuildCenterCard()
        {
            Panel card = CreateCard();
            card.Margin = new Padding(0, 0, 8, 0);
            card.Padding = new Padding(14, 10, 14, 10);

            Label heading = new Label();
            heading.Text = "MOVEMENT CENTER";
            heading.ForeColor = Palette.Dim;
            heading.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
            heading.AutoSize = true;
            heading.Location = new Point(14, 10);
            card.Controls.Add(heading);

            Label xCaption = SmallCaption("X", 14, 42);
            card.Controls.Add(xCaption);
            centerXValue = ValueLabel("--", 34, 35);
            card.Controls.Add(centerXValue);

            Label yCaption = SmallCaption("Y", 132, 42);
            card.Controls.Add(yCaption);
            centerYValue = ValueLabel("--", 152, 35);
            card.Controls.Add(centerYValue);

            Panel divider = new Panel();
            divider.BackColor = Palette.CardBorder;
            divider.Height = 1;
            divider.Location = new Point(14, 76);
            divider.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            divider.Width = 260;
            card.Controls.Add(divider);
            card.Resize += delegate
            {
                divider.Width = Math.Max(10, card.ClientSize.Width - 28);
            };

            Label radiusCaption = SmallCaption("Radius", 14, 91);
            card.Controls.Add(radiusCaption);
            radiusValue = ValueLabel("30 px", 112, 84);
            radiusValue.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            card.Controls.Add(radiusValue);

            Label nextCaption = SmallCaption("Next movement", 14, 119);
            card.Controls.Add(nextCaption);
            nextValue = ValueLabel("--", 146, 112);
            nextValue.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            nextValue.ForeColor = Palette.Blue;
            card.Controls.Add(nextValue);

            Label note = new Label();
            note.Text = "Box is fixed in this window.";
            note.ForeColor = Palette.Dim;
            note.Font = new Font("Segoe UI", 7.25F, FontStyle.Regular, GraphicsUnit.Point);
            note.AutoSize = true;
            note.Location = new Point(14, 155);
            card.Controls.Add(note);

            return card;
        }

        private Control BuildPreviewCard()
        {
            Panel card = CreateCard();
            card.Margin = new Padding(8, 0, 0, 0);
            card.Padding = new Padding(12, 30, 12, 10);

            Label heading = new Label();
            heading.Text = "FIXED BOX";
            heading.ForeColor = Palette.Dim;
            heading.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
            heading.AutoSize = true;
            heading.Location = new Point(12, 10);
            card.Controls.Add(heading);

            preview = new BoundaryPreview();
            preview.Dock = DockStyle.Fill;
            preview.SetRadius(30);
            card.Controls.Add(preview);
            preview.BringToFront();
            return card;
        }

        private Control BuildButtonRow()
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.BackColor = Palette.Background;
            row.Margin = new Padding(0, 0, 0, 8);
            row.ColumnCount = 2;
            row.RowCount = 1;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            startButton = new GlowButton(Palette.Green);
            startButton.Text = "START";
            startButton.Dock = DockStyle.Fill;
            startButton.Margin = new Padding(0, 0, 6, 0);
            startButton.ForeColor = Color.FromArgb(4, 25, 12);
            startButton.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            startButton.Click += delegate { StartAutomation(); };
            row.Controls.Add(startButton, 0, 0);

            stopButton = new GlowButton(Palette.Red);
            stopButton.Text = "STOP";
            stopButton.Dock = DockStyle.Fill;
            stopButton.Margin = new Padding(6, 0, 0, 0);
            stopButton.ForeColor = Color.White;
            stopButton.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            stopButton.Click += delegate { StopAutomation(false, "Stopped"); };
            row.Controls.Add(stopButton, 1, 0);

            return row;
        }

        private Control BuildLogCard()
        {
            Panel card = CreateCard();
            card.Margin = new Padding(0, 0, 0, 8);
            card.Padding = new Padding(14, 28, 14, 8);

            Label heading = new Label();
            heading.Text = "ACTIVITY";
            heading.ForeColor = Palette.Dim;
            heading.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
            heading.AutoSize = true;
            heading.Location = new Point(14, 9);
            card.Controls.Add(heading);

            logList = new ListBox();
            logList.Dock = DockStyle.Fill;
            logList.BackColor = Palette.Card;
            logList.ForeColor = Color.FromArgb(178, 187, 200);
            logList.BorderStyle = BorderStyle.None;
            logList.Font = new Font("Consolas", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            logList.IntegralHeight = false;
            logList.SelectionMode = SelectionMode.None;
            logList.DrawMode = DrawMode.OwnerDrawFixed;
            logList.ItemHeight = 17;
            logList.TabStop = false;
            logList.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                using (SolidBrush background = new SolidBrush(Palette.Card))
                {
                    e.Graphics.FillRectangle(background, e.Bounds);
                }
                if (e.Index < 0 || e.Index >= logList.Items.Count)
                {
                    return;
                }
                string line = Convert.ToString(logList.Items[e.Index]);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(178, 187, 200)))
                {
                    e.Graphics.DrawString(line, e.Font, brush, e.Bounds.Left + 2, e.Bounds.Top + 2);
                }
            };
            card.Controls.Add(logList);
            return card;
        }

        private Control BuildFooter()
        {
            Label footer = new Label();
            footer.Dock = DockStyle.Fill;
            footer.Text = "F8  Start     |     F9  Stop     |     ESC  Emergency stop";
            footer.TextAlign = ContentAlignment.MiddleCenter;
            footer.ForeColor = Palette.Muted;
            footer.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            footer.Margin = new Padding(0);
            return footer;
        }

        private void WireEvents()
        {
            engine.Activity += OnEngineActivity;
            engine.NextMovementScheduled += OnNextMovementScheduled;
            engine.CursorMoved += OnEngineCursorMoved;
            engine.Stopped += OnEngineStopped;

            radiusInput.ValueChanged += delegate
            {
                int radius = (int)radiusInput.Value;
                preview.SetRadius(radius);
                radiusValue.Text = radius + " px";
            };
            if (preview != null)
            {
                preview.SetRadius((int)radiusInput.Value);
            }
            radiusValue.Text = ((int)radiusInput.Value) + " px";

            exactDelayToggle.CheckedChanged += delegate { UpdateSettingsAvailability(); };
            scheduleToggle.CheckedChanged += delegate { UpdateSettingsAvailability(); };
            sleepToggle.CheckedChanged += delegate { UpdateSettingsAvailability(); };

            countdownTimer.Interval = 1000;
            countdownTimer.Tick += delegate
            {
                if (closing || !armed)
                {
                    return;
                }
                CheckSchedule(false);
                if (engine.IsRunning)
                {
                    UpdateCountdown();
                    if ((DateTime.UtcNow - lastActivityNudge).TotalSeconds >= 60.0)
                    {
                        engine.NudgeInput();
                        lastActivityNudge = DateTime.UtcNow;
                    }
                }
            };

            KeyDown += OnFormKeyDown;
            FormClosing += OnFormClosing;
            LocationChanged += OnWindowLayoutChanged;
            SizeChanged += OnWindowLayoutChanged;
        }

        private void StartStopRequestListener()
        {
            Thread listener = new Thread(delegate()
            {
                while (!closing)
                {
                    try
                    {
                        if (!stopRequest.WaitOne(500))
                        {
                            continue;
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    if (closing)
                    {
                        return;
                    }

                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            StopAutomation(false, "Stopped (external request)");
                        }));
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                }
            });
            listener.IsBackground = true;
            listener.Name = "CursorMove.StopListener";
            listener.Start();
        }

        private void StartAutomation()
        {
            if (closing || armed)
            {
                return;
            }

            int radius = (int)radiusInput.Value;
            int minimum = (int)minimumInput.Value;
            int maximum = (int)maximumInput.Value;
            bool useExactDelay = exactDelayToggle.Checked;
            if (useExactDelay)
            {
                minimum = (int)exactDelayInput.Value;
                maximum = minimum;
            }
            else if (minimum > maximum)
            {
                MessageBox.Show(
                    "Minimum interval cannot be greater than maximum interval.",
                    "CursorMove",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                minimumInput.Focus();
                return;
            }

            bool useSchedule = scheduleToggle.Checked;
            bool useSleep = sleepToggle.Checked;
            TimeSpan scheduleFrom = ReadTimeOfDay(scheduleFromHourInput, scheduleFromMinuteInput);
            TimeSpan scheduleTo = ReadTimeOfDay(scheduleToHourInput, scheduleToMinuteInput);
            TimeSpan sleepFrom = ReadTimeOfDay(sleepFromHourInput, sleepFromMinuteInput);
            TimeSpan sleepTo = ReadTimeOfDay(sleepToHourInput, sleepToMinuteInput);

            if (useSchedule && scheduleFrom == scheduleTo)
            {
                MessageBox.Show(
                    "Schedule start and end must be different.",
                    "CursorMove",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                scheduleFromHourInput.Focus();
                return;
            }

            if (useSleep && sleepFrom == sleepTo)
            {
                MessageBox.Show(
                    "Sleep start and end must be different.",
                    "CursorMove",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                sleepFromHourInput.Focus();
                return;
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
                Activate();
            }

            // The box lives in this window, so its screen center is the fixed center and the
            // preview control's screen rectangle is the hard movement boundary.
            sessionBounds = preview.ScreenBounds;
            sessionCenter = preview.ScreenCenter;
            sessionWindowState = WindowState;
            sessionRadius = Math.Min(radius, preview.MaxBoxRadius);
            if (sessionBounds.Width <= 0 || sessionBounds.Height <= 0)
            {
                MessageBox.Show(
                    "The fixed box is not visible. Restore the window and try again.",
                    "CursorMove",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            if (sessionRadius < radius)
            {
                AddLog("Box fits " + sessionRadius + " px; radius limited by the window");
            }
            sessionMinimumSeconds = minimum;
            sessionMaximumSeconds = maximum;
            sessionScheduleEnabled = useSchedule;
            sessionScheduleFrom = scheduleFrom;
            sessionScheduleTo = scheduleTo;
            sessionSleepEnabled = useSleep;
            sessionSleepFrom = sleepFrom;
            sessionSleepTo = sleepTo;

            armed = true;
            ApplyArmedInterface();
            countdownTimer.Start();
            RegisterEscapeHotkey();
            CheckSchedule(true);
        }

        private void StopAutomation(bool emergency, string logMessage)
        {
            if (!armed && !engine.IsRunning)
            {
                return;
            }

            armed = false;
            engine.Stop();
            countdownTimer.Stop();
            UnregisterEscapeHotkey();
            ApplyStoppedInterface();

            if (emergency)
            {
                AddLog("Emergency stop (ESC)");
            }
            else
            {
                AddLog(logMessage);
            }
        }

        /// <summary>
        /// The box is anchored to this window. Moving, resizing, or minimizing it would leave the
        /// saved screen center behind, so end the session instead of moving outside the box.
        /// </summary>
        private void OnWindowLayoutChanged(object sender, EventArgs e)
        {
            if (closing || !armed || preview == null)
            {
                return;
            }

            bool changed;
            try
            {
                changed =
                    WindowState != sessionWindowState ||
                    WindowState == FormWindowState.Minimized ||
                    preview.ScreenCenter != sessionCenter ||
                    preview.ScreenBounds != sessionBounds;
            }
            catch (Exception)
            {
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            StopAutomation(
                false,
                WindowState == FormWindowState.Minimized
                    ? "Stopped (window minimized)"
                    : "Stopped (window moved or resized)");
        }

        private void ApplyArmedInterface()
        {
            armed = true;
            radiusValue.Text = sessionRadius + " px";
            centerXValue.Text = sessionCenter.X.ToString();
            centerYValue.Text = sessionCenter.Y.ToString();
            nextValue.Text = "checking schedule...";
            preview.SetRadius(sessionRadius);
            preview.ResetPosition();
            UpdateSettingsAvailability();
        }

        private void ApplyStoppedInterface()
        {
            armed = false;
            statusPill.State = StatusPillState.Stopped;
            centerXValue.Text = "--";
            centerYValue.Text = "--";
            nextValue.Text = "--";
            radiusValue.Text = ((int)radiusInput.Value) + " px";
            preview.ResetPosition();
            UpdateSettingsAvailability();
        }

        private void UpdateSettingsAvailability()
        {
            bool editable = !armed;
            startButton.Enabled = editable;
            stopButton.Enabled = !editable;

            radiusInput.Enabled = editable;
            exactDelayToggle.Enabled = editable;
            bool exact = exactDelayToggle.Checked;
            exactDelayInput.Enabled = editable && exact;
            minimumInput.Enabled = editable && !exact;
            maximumInput.Enabled = editable && !exact;

            scheduleToggle.Enabled = editable;
            bool schedule = scheduleToggle.Checked;
            scheduleFromHourInput.Enabled = editable && schedule;
            scheduleFromMinuteInput.Enabled = editable && schedule;
            scheduleToHourInput.Enabled = editable && schedule;
            scheduleToMinuteInput.Enabled = editable && schedule;

            sleepToggle.Enabled = editable;
            bool sleep = sleepToggle.Checked;
            sleepFromHourInput.Enabled = editable && sleep;
            sleepFromMinuteInput.Enabled = editable && sleep;
            sleepToHourInput.Enabled = editable && sleep;
            sleepToMinuteInput.Enabled = editable && sleep;
        }

        private void CheckSchedule(bool initial)
        {
            if (closing || !armed)
            {
                return;
            }

            SchedulePhase phase = ScheduleMath.Evaluate(
                DateTime.Now.TimeOfDay,
                sessionScheduleEnabled,
                sessionScheduleFrom,
                sessionScheduleTo,
                sessionSleepEnabled,
                sessionSleepFrom,
                sessionSleepTo);

            if (phase == SchedulePhase.Active)
            {
                if (!engine.IsRunning)
                {
                    try
                    {
                        engine.StartAt(
                            sessionCenter,
                            sessionBounds,
                            sessionRadius,
                            sessionMinimumSeconds,
                            sessionMaximumSeconds);
                    }
                    catch (Exception exception)
                    {
                        AddLog("Start failed: " + exception.Message);
                        StopAutomation(false, "Stopped (schedule start failed)");
                        return;
                    }

                    NativeApi.SetCursorPosition(sessionCenter);
                    lastActivityNudge = DateTime.UtcNow;
                    statusPill.State = StatusPillState.Running;
                    radiusValue.Text = engine.Radius + " px";
                    centerXValue.Text = engine.Center.X.ToString();
                    centerYValue.Text = engine.Center.Y.ToString();
                    nextValue.Text = "waiting...";
                    preview.SetRadius(engine.Radius);
                    preview.SetOffset(0, 0);

                    AddLog(string.Format(
                        "{0} (center {1},{2}, radius {3} px)",
                        initial ? "Started" : "Resumed",
                        engine.Center.X,
                        engine.Center.Y,
                        engine.Radius));
                }
                else
                {
                    statusPill.State = StatusPillState.Running;
                }

                UpdateCountdown();
            }
            else
            {
                if (engine.IsRunning)
                {
                    engine.Stop();
                }

                if (phase == SchedulePhase.OutsideSchedule)
                {
                    statusPill.State = StatusPillState.Scheduled;
                    nextValue.Text = "starts " + ScheduleMath.FormatTime(sessionScheduleFrom);
                    if (initial)
                    {
                        AddLog("Armed - outside schedule; starts " +
                               ScheduleMath.FormatTime(sessionScheduleFrom));
                    }
                    else if (lastPhase != phase)
                    {
                        AddLog("Waiting for schedule; starts " +
                               ScheduleMath.FormatTime(sessionScheduleFrom));
                    }
                }
                else
                {
                    statusPill.State = StatusPillState.Sleeping;
                    nextValue.Text = "until " + ScheduleMath.FormatTime(sessionSleepTo);
                    if (initial)
                    {
                        AddLog("Armed - sleeping until " +
                               ScheduleMath.FormatTime(sessionSleepTo));
                    }
                    else if (lastPhase != phase)
                    {
                        AddLog("Sleeping until " +
                               ScheduleMath.FormatTime(sessionSleepTo));
                    }
                }

                centerXValue.Text = sessionCenter.X.ToString();
                centerYValue.Text = sessionCenter.Y.ToString();
                preview.ResetPosition();
            }

            lastPhase = phase;
        }

        private static TimeSpan ReadTimeOfDay(NumericUpDown hourInput, NumericUpDown minuteInput)
        {
            return TimeSpan.FromHours((int)hourInput.Value) +
                   TimeSpan.FromMinutes((int)minuteInput.Value);
        }

        private void RegisterEscapeHotkey()
        {
            if (escapeHotkeyRegistered || !IsHandleCreated)
            {
                return;
            }

            escapeHotkeyRegistered = NativeApi.RegisterHotKey(
                Handle,
                HotkeyEmergency,
                NativeApi.ModNoRepeat,
                (uint)Keys.Escape);

            if (!escapeHotkeyRegistered)
            {
                AddLog("ESC global hotkey unavailable; press ESC with this window focused");
            }
        }

        private void UnregisterEscapeHotkey()
        {
            if (!escapeHotkeyRegistered)
            {
                return;
            }

            if (IsHandleCreated)
            {
                NativeApi.UnregisterHotKey(Handle, HotkeyEmergency);
            }
            escapeHotkeyRegistered = false;
        }

        private void RegisterPersistentHotkeys()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            startHotkeyRegistered = NativeApi.RegisterHotKey(
                Handle,
                HotkeyStart,
                NativeApi.ModNoRepeat,
                (uint)Keys.F8);
            stopHotkeyRegistered = NativeApi.RegisterHotKey(
                Handle,
                HotkeyStop,
                NativeApi.ModNoRepeat,
                (uint)Keys.F9);

            if (!startHotkeyRegistered || !stopHotkeyRegistered)
            {
                AddLog("Global F8/F9 hotkey unavailable; use the window shortcuts");
            }
        }

        private void UnregisterPersistentHotkeys()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            if (startHotkeyRegistered)
            {
                NativeApi.UnregisterHotKey(Handle, HotkeyStart);
                startHotkeyRegistered = false;
            }
            if (stopHotkeyRegistered)
            {
                NativeApi.UnregisterHotKey(Handle, HotkeyStop);
                stopHotkeyRegistered = false;
            }
        }

        private void OnEngineActivity(string message)
        {
            InvokeSafely(delegate
            {
                if (message == "Random movement")
                {
                    nextValue.Text = "moving...";
                }
                AddLog(message);
            });
        }

        private void OnNextMovementScheduled(DateTime scheduled)
        {
            InvokeSafely(delegate { UpdateCountdown(); });
        }

        private void OnEngineCursorMoved(Point position)
        {
            int offsetX = 0;
            int offsetY = 0;
            Point center = engine.Center;
            offsetX = position.X - center.X;
            offsetY = position.Y - center.Y;

            InvokeSafely(delegate
            {
                if (engine.IsRunning)
                {
                    preview.SetOffset(offsetX, offsetY);
                }
            });
        }

        private void OnEngineStopped()
        {
            InvokeSafely(delegate
            {
                if (closing || engine.IsRunning || !armed)
                {
                    return;
                }

                StopAutomation(false, "Stopped (movement loop ended)");
            });
        }

        private void UpdateCountdown()
        {
            if (!engine.IsRunning)
            {
                return;
            }

            DateTime scheduled = engine.NextMoveAt;
            if (scheduled == DateTime.MinValue)
            {
                nextValue.Text = "moving...";
                return;
            }

            double remaining = (scheduled - DateTime.Now).TotalSeconds;
            int seconds = remaining > 0 ? (int)Math.Ceiling(remaining) : 0;
            nextValue.Text = seconds + " sec";
        }

        private void AddLog(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            logList.Items.Add(line);
            while (logList.Items.Count > 20)
            {
                logList.Items.RemoveAt(0);
            }
            if (logList.Items.Count > 0)
            {
                logList.TopIndex = logList.Items.Count - 1;
            }
        }

        private void InvokeSafely(Action action)
        {
            if (closing || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (ObjectDisposedException)
                {
                    // The window is closing; dropping a UI update is safe.
                }
                catch (InvalidOperationException)
                {
                    // The window is closing; dropping a UI update is safe.
                }
            }
            else
            {
                action();
            }
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F8)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                StartAutomation();
            }
            else if (e.KeyCode == Keys.F9)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                StopAutomation(false, "Stopped");
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                StopAutomation(true, "Stopped");
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            closing = true;
            countdownTimer.Stop();
            engine.Stop();
            UnregisterEscapeHotkey();
            UnregisterPersistentHotkeys();

            try
            {
                stopRequest.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeApi.ApplyDarkTitleBar(Handle);
            RegisterPersistentHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterPersistentHotkeys();
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeApi.WmHotKey)
            {
                int id = message.WParam.ToInt32();
                if (id == HotkeyStart)
                {
                    StartAutomation();
                    return;
                }
                if (id == HotkeyStop)
                {
                    StopAutomation(false, "Stopped");
                    return;
                }
                if (id == HotkeyEmergency)
                {
                    StopAutomation(true, "Stopped");
                    return;
                }
            }

            base.WndProc(ref message);
        }

        private static Panel CreateCard()
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.BackColor = Palette.Card;
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                Control control = (Control)sender;
                using (Pen pen = new Pen(Palette.CardBorder))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, control.ClientSize.Width - 1, control.ClientSize.Height - 1);
                }
            };
            return card;
        }

        private static Label SmallCaption(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Palette.Muted;
            label.Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
            label.AutoSize = true;
            label.Location = new Point(x, y);
            return label;
        }

        private static Label ValueLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Palette.Text;
            label.Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point);
            label.AutoSize = true;
            label.Location = new Point(x, y);
            return label;
        }
    }

    /// <summary>
    /// Flat button with a small animated glow. The effect is a native take on the React Bits
    /// glow-cursor idea: a soft light follows the pointer and a subtle sheen crosses the button.
    /// Animation runs only while the pointer is over the button.
    /// </summary>
    internal sealed class GlowButton : Button
    {
        private readonly Color accent;
        private readonly System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();
        private float hoverAmount;
        private float pressAmount;
        private float shinePhase;
        private bool hovered;
        private bool pressed;
        private Point pointer;
        private bool pointerInside;

        internal GlowButton(Color accent)
        {
            this.accent = accent;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Palette.Background;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            TabStop = true;
            animationTimer.Interval = 33;
            animationTimer.Tick += delegate { AnimateButton(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(BackColor);

            RectangleF bounds = new RectangleF(2f, 2f, Width - 4f, Height - 4f);
            if (bounds.Width <= 1f || bounds.Height <= 1f)
            {
                return;
            }

            using (GraphicsPath path = RoundedRectangleF(bounds, 9f))
            {
                if (!Enabled)
                {
                    using (SolidBrush disabledFill = new SolidBrush(Color.FromArgb(25, 29, 37)))
                    using (Pen disabledBorder = new Pen(Color.FromArgb(45, 51, 62)))
                    {
                        graphics.FillPath(disabledFill, path);
                        graphics.DrawPath(disabledBorder, path);
                    }
                }
                else
                {
                    Color top = Blend(accent, Color.White, 0.09f + 0.05f * hoverAmount);
                    Color bottom = Blend(accent, Color.Black, 0.22f);
                    using (LinearGradientBrush fill = new LinearGradientBrush(bounds, top, bottom, LinearGradientMode.Vertical))
                    {
                        graphics.FillPath(fill, path);
                    }

                    if (hoverAmount > 0.01f)
                    {
                        for (int layer = 1; layer <= 3; layer++)
                        {
                            using (Pen halo = new Pen(
                                Color.FromArgb((int)(13f * hoverAmount / layer), Blend(accent, Color.White, 0.55f)),
                                layer * 1.6f))
                            {
                                graphics.DrawPath(halo, path);
                            }
                        }

                        if (pointerInside)
                        {
                            float glowDiameter = Height * 1.5f;
                            using (GraphicsPath pointerGlow = new GraphicsPath())
                            {
                                pointerGlow.AddEllipse(
                                    pointer.X - glowDiameter / 2f,
                                    pointer.Y - glowDiameter / 2f,
                                    glowDiameter,
                                    glowDiameter);
                                using (PathGradientBrush light = new PathGradientBrush(pointerGlow))
                                {
                                    light.CenterColor = Color.FromArgb((int)(55f * hoverAmount), Color.White);
                                    light.SurroundColors = new Color[] { Color.FromArgb(0, Color.White) };
                                    GraphicsState state = graphics.Save();
                                    graphics.SetClip(path);
                                    graphics.FillPath(light, pointerGlow);
                                    graphics.Restore(state);
                                }
                            }
                        }

                        float stripeWidth = 74f;
                        float stripeX = -stripeWidth + shinePhase * (Width + stripeWidth * 2f);
                        RectangleF stripeBounds = new RectangleF(stripeX, bounds.Top, stripeWidth, bounds.Height);
                        using (LinearGradientBrush sheen = new LinearGradientBrush(
                            stripeBounds,
                            Color.Transparent,
                            Color.Transparent,
                            LinearGradientMode.Horizontal))
                        {
                            ColorBlend blend = new ColorBlend(3);
                            blend.Colors = new Color[]
                            {
                                Color.FromArgb(0, Color.White),
                                Color.FromArgb((int)(34f * hoverAmount), Color.White),
                                Color.FromArgb(0, Color.White)
                            };
                            blend.Positions = new float[] { 0f, 0.5f, 1f };
                            sheen.InterpolationColors = blend;
                            GraphicsState state = graphics.Save();
                            graphics.SetClip(path);
                            graphics.FillRectangle(sheen, stripeBounds);
                            graphics.Restore(state);
                        }
                    }

                    if (pressAmount > 0.01f)
                    {
                        using (SolidBrush pressShade = new SolidBrush(Color.FromArgb((int)(38f * pressAmount), Color.Black)))
                        {
                            graphics.FillPath(pressShade, path);
                        }
                    }

                    using (Pen border = new Pen(
                        Color.FromArgb((int)(120 + 70 * hoverAmount), Blend(accent, Color.White, 0.55f))))
                    {
                        graphics.DrawPath(border, path);
                    }
                }

                if (Focused && ShowFocusCues)
                {
                    RectangleF focusBounds = new RectangleF(5f, 5f, Width - 10f, Height - 10f);
                    using (GraphicsPath focusPath = RoundedRectangleF(focusBounds, 7f))
                    using (Pen focusPen = new Pen(Color.FromArgb(180, Color.White)))
                    {
                        focusPen.DashStyle = DashStyle.Dot;
                        graphics.DrawPath(focusPen, focusPath);
                    }
                }
            }

            Color textColor = Enabled ? ForeColor : Palette.Dim;
            Rectangle textBounds = new Rectangle(0, (int)Math.Round(pressAmount), Width, Height);
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                textBounds,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            EnsureAnimation();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            pointer = e.Location;
            pointerInside = true;
            if (hovered)
            {
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            pressed = false;
            pointerInside = false;
            EnsureAnimation();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
            {
                pressed = true;
                EnsureAnimation();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressed = false;
            EnsureAnimation();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled)
            {
                hovered = false;
                pressed = false;
                pointerInside = false;
            }
            EnsureAnimation();
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            pressed = false;
            EnsureAnimation();
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        private void EnsureAnimation()
        {
            bool needed = Enabled && (hovered || hoverAmount > 0.01f || pressAmount > 0.01f);
            if (needed && !animationTimer.Enabled)
            {
                animationTimer.Start();
            }
            else if (!needed)
            {
                animationTimer.Stop();
                hoverAmount = 0f;
                pressAmount = 0f;
            }
        }

        private void AnimateButton()
        {
            float hoverTarget = hovered && Enabled ? 1f : 0f;
            float pressTarget = pressed && Enabled ? 1f : 0f;
            hoverAmount += (hoverTarget - hoverAmount) * 0.2f;
            pressAmount += (pressTarget - pressAmount) * 0.35f;

            if (hovered && Enabled)
            {
                shinePhase += 0.014f;
                if (shinePhase > 1f)
                {
                    shinePhase -= 1f;
                }
            }

            if (!hovered && hoverAmount < 0.01f)
            {
                hoverAmount = 0f;
            }
            if (!pressed && pressAmount < 0.01f)
            {
                pressAmount = 0f;
            }

            Invalidate();
            if (!hovered && hoverAmount <= 0f && pressAmount <= 0f)
            {
                animationTimer.Stop();
            }
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        private static GraphicsPath RoundedRectangleF(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 1f)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationTimer.Stop();
                animationTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Two-digit number box used for the hour and minute fields.</summary>
    internal sealed class TimeInput : NumericUpDown
    {
        protected override void UpdateEditText()
        {
            Text = Value.ToString("00");
        }
    }

    /// <summary>Native toggle used for the exact-delay and schedule options.</summary>
    internal sealed class ToggleSwitch : CheckBox
    {
        internal ToggleSwitch()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            AutoSize = false;
            Height = 22;
            BackColor = Palette.Card;
            ForeColor = Palette.Text;
            Font = new Font("Segoe UI", 7.75F, FontStyle.Bold, GraphicsUnit.Point);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(BackColor);

            int trackWidth = 34;
            int trackHeight = 18;
            int trackY = (Height - trackHeight) / 2;
            Color track = Checked ? Palette.Green : Color.FromArgb(57, 63, 75);
            Color border = Checked ? Palette.GreenBright : Color.FromArgb(80, 88, 102);
            if (!Enabled)
            {
                track = Color.FromArgb(40, 45, 55);
                border = Color.FromArgb(55, 61, 72);
            }

            using (GraphicsPath path = RoundedRectangle(new Rectangle(0, trackY, trackWidth, trackHeight), trackHeight))
            using (SolidBrush fill = new SolidBrush(track))
            using (Pen pen = new Pen(border))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(pen, path);
            }

            int knobSize = 12;
            int knobX = Checked ? trackWidth - knobSize - 3 : 3;
            using (SolidBrush knob = new SolidBrush(Enabled ? Color.White : Color.FromArgb(120, 126, 138)))
            {
                graphics.FillEllipse(
                    knob,
                    knobX,
                    trackY + (trackHeight - knobSize) / 2f,
                    knobSize,
                    knobSize);
            }

            int textX = trackWidth + 8;
            Color textColor = Enabled ? ForeColor : Palette.Dim;
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                new Rectangle(textX, 0, Math.Max(0, Width - textX), Height),
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues && Width > textX)
            {
                ControlPaint.DrawFocusRectangle(
                    graphics,
                    new Rectangle(textX - 2, 2, Width - textX, Height - 4),
                    textColor,
                    BackColor);
            }
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal enum StatusPillState
    {
        Stopped,
        Running,
        Scheduled,
        Sleeping
    }

    /// <summary>Rounded status badge with a state-colored dot and, while running, a pulse.</summary>
    internal sealed class StatusPill : Control
    {
        private readonly System.Windows.Forms.Timer pulseTimer;
        private StatusPillState state;
        private float pulsePhase;

        internal StatusPill()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Palette.Background;
            Size = new Size(136, 32);
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);

            pulseTimer = new System.Windows.Forms.Timer();
            pulseTimer.Interval = 45;
            pulseTimer.Tick += delegate
            {
                pulsePhase += 0.055f;
                if (pulsePhase >= 1f)
                {
                    pulsePhase -= 1f;
                }
                Invalidate();
            };
            state = StatusPillState.Stopped;
        }

        internal StatusPillState State
        {
            get
            {
                return state;
            }
            set
            {
                if (state == value)
                {
                    return;
                }

                state = value;
                pulsePhase = 0f;
                if (state == StatusPillState.Running)
                {
                    pulseTimer.Start();
                }
                else
                {
                    pulseTimer.Stop();
                }
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Palette.Background);

            Color fill;
            Color border;
            Color accent;
            string label;
            switch (state)
            {
                case StatusPillState.Running:
                    fill = Color.FromArgb(16, 44, 30);
                    border = Color.FromArgb(26, 80, 54);
                    accent = Color.FromArgb(74, 222, 128);
                    label = "RUNNING";
                    break;
                case StatusPillState.Scheduled:
                    fill = Color.FromArgb(15, 29, 46);
                    border = Color.FromArgb(42, 94, 139);
                    accent = Color.FromArgb(147, 197, 253);
                    label = "SCHEDULED";
                    break;
                case StatusPillState.Sleeping:
                    fill = Color.FromArgb(38, 30, 16);
                    border = Color.FromArgb(113, 86, 36);
                    accent = Color.FromArgb(253, 224, 171);
                    label = "SLEEPING";
                    break;
                default:
                    fill = Color.FromArgb(30, 34, 43);
                    border = Color.FromArgb(52, 57, 69);
                    accent = Color.FromArgb(160, 169, 183);
                    label = "STOPPED";
                    break;
            }

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedRectangle(bounds, Height / 2))
            using (SolidBrush fillBrush = new SolidBrush(fill))
            using (Pen borderPen = new Pen(border))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

            float dotX = 20f;
            float dotY = Height / 2f;
            if (state == StatusPillState.Running)
            {
                float pulse = 1f - Math.Abs(0.5f - pulsePhase) * 2f;
                float haloRadius = 8f + pulse * 2.5f;
                using (SolidBrush halo = new SolidBrush(Color.FromArgb((int)(35 + pulse * 35), accent)))
                {
                    e.Graphics.FillEllipse(halo, dotX - haloRadius, dotY - haloRadius, haloRadius * 2f, haloRadius * 2f);
                }
            }

            using (SolidBrush dot = new SolidBrush(accent))
            {
                e.Graphics.FillEllipse(dot, dotX - 4.5f, dotY - 4.5f, 9f, 9f);
            }

            TextRenderer.DrawText(
                e.Graphics,
                label,
                Font,
                new Rectangle(34, 0, Width - 34, Height),
                accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pulseTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Fixed box: the pointer is moved here at START and stays inside it.</summary>
    internal sealed class BoundaryPreview : Control
    {
        private int radius = 30;
        private bool hasOffset;
        private int offsetX;
        private int offsetY;

        internal BoundaryPreview()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = Palette.Card;
        }

        internal void SetRadius(int value)
        {
            if (value < 1)
            {
                value = 1;
            }
            if (radius != value)
            {
                radius = value;
                Invalidate();
            }
        }

        internal void SetOffset(int x, int y)
        {
            offsetX = x;
            offsetY = y;
            hasOffset = true;
            Invalidate();
        }

        internal void ResetPosition()
        {
            hasOffset = false;
            offsetX = 0;
            offsetY = 0;
            Invalidate();
        }

        /// <summary>Largest radius that still fits inside this control at its current size.</summary>
        internal int MaxBoxRadius
        {
            get
            {
                return MaxDiameter / 2;
            }
        }

        /// <summary>
        /// Screen position of the drawn circle's center. START puts the pointer here and the
        /// engine uses it as the fixed center.
        /// </summary>
        internal Point ScreenCenter
        {
            get
            {
                return PointToScreen(new Point(Width / 2, Height / 2 + 4));
            }
        }

        internal Rectangle ScreenBounds
        {
            get
            {
                return RectangleToScreen(ClientRectangle);
            }
        }

        private int MaxDiameter
        {
            get
            {
                int padding = 16;
                int diameter = Math.Min(Width - padding * 2, Height - padding * 2 - 4);
                return diameter < 24 ? 24 : diameter;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            float centerX = Width / 2f;
            float centerY = Height / 2f + 4f;
            int shownRadius = Math.Min(radius, MaxBoxRadius);
            float circleRadius = shownRadius;
            float diameter = circleRadius * 2f;

            float dotX = centerX;
            float dotY = centerY;
            bool showPosition = hasOffset && radius > 0;
            if (showPosition)
            {
                float normalizedX = Math.Max(-1f, Math.Min(1f, offsetX / (float)radius));
                float normalizedY = Math.Max(-1f, Math.Min(1f, offsetY / (float)radius));
                dotX = centerX + normalizedX * circleRadius;
                dotY = centerY + normalizedY * circleRadius;
            }

            using (SolidBrush circleFill = new SolidBrush(Color.FromArgb(17, 27, 36)))
            using (Pen boundaryPen = new Pen(Color.FromArgb(55, 99, 140), 1.2f))
            using (Pen guidePen = new Pen(Color.FromArgb(35, 43, 54)))
            using (GraphicsPath circlePath = new GraphicsPath())
            {
                boundaryPen.DashStyle = DashStyle.Dash;
                guidePen.DashStyle = DashStyle.Dot;
                circlePath.AddEllipse(centerX - circleRadius, centerY - circleRadius, diameter, diameter);

                e.Graphics.FillEllipse(circleFill, centerX - circleRadius, centerY - circleRadius, diameter, diameter);

                // Fine dot grid, inspired by the React Bits CursorGrid look; native GDI+ keeps
                // the app dependency-free. Only the dots around the current position brighten.
                GraphicsState gridState = e.Graphics.Save();
                e.Graphics.SetClip(circlePath, CombineMode.Intersect);
                int spacing = 15;
                float startX = centerX - (float)Math.Floor(circleRadius / spacing) * spacing;
                float startY = centerY - (float)Math.Floor(circleRadius / spacing) * spacing;
                for (float gridY = startY; gridY <= centerY + circleRadius; gridY += spacing)
                {
                    for (float gridX = startX; gridX <= centerX + circleRadius; gridX += spacing)
                    {
                        float dx = gridX - centerX;
                        float dy = gridY - centerY;
                        if (dx * dx + dy * dy > (circleRadius - 3f) * (circleRadius - 3f))
                        {
                            continue;
                        }

                        Color gridColor = Color.FromArgb(25, 112, 155, 205);
                        float markerSize = 1.6f;
                        if (showPosition)
                        {
                            float distance = (float)Math.Sqrt(
                                (gridX - dotX) * (gridX - dotX) + (gridY - dotY) * (gridY - dotY));
                            if (distance < 40f)
                            {
                                float strength = 1f - distance / 40f;
                                gridColor = Color.FromArgb(
                                    (int)(35 + 85 * strength),
                                    120 + (int)(40 * strength),
                                    190,
                                    140 + (int)(70 * strength));
                                markerSize = 1.6f + strength * 1.5f;
                            }
                        }

                        using (SolidBrush gridDot = new SolidBrush(gridColor))
                        {
                            e.Graphics.FillEllipse(
                                gridDot,
                                gridX - markerSize / 2f,
                                gridY - markerSize / 2f,
                                markerSize,
                                markerSize);
                        }
                    }
                }
                e.Graphics.Restore(gridState);

                e.Graphics.DrawEllipse(boundaryPen, centerX - circleRadius, centerY - circleRadius, diameter, diameter);

                float guideExtent = circleRadius + 6f;
                e.Graphics.DrawLine(guidePen, centerX - guideExtent, centerY, centerX + guideExtent, centerY);
                e.Graphics.DrawLine(guidePen, centerX, centerY - guideExtent, centerX, centerY + guideExtent);

                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(55, 120, 180)))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4.0;
                        float x = centerX + circleRadius * (float)Math.Cos(angle);
                        float y = centerY + circleRadius * (float)Math.Sin(angle);
                        e.Graphics.FillEllipse(dotBrush, x - 2f, y - 2f, 4f, 4f);
                    }
                }
            }

            using (Pen centerPen = new Pen(Palette.Green, 1.8f))
            {
                e.Graphics.DrawLine(centerPen, centerX - 7f, centerY, centerX + 7f, centerY);
                e.Graphics.DrawLine(centerPen, centerX, centerY - 7f, centerX, centerY + 7f);
            }

            if (showPosition)
            {
                using (Pen pathPen = new Pen(Color.FromArgb(45, Palette.GreenBright), 1f))
                {
                    pathPen.DashStyle = DashStyle.Dot;
                    e.Graphics.DrawLine(pathPen, centerX, centerY, dotX, dotY);
                }

                float glowDiameter = Math.Max(30f, circleRadius * 0.85f);
                using (GraphicsPath glowPath = new GraphicsPath())
                {
                    glowPath.AddEllipse(
                        dotX - glowDiameter / 2f,
                        dotY - glowDiameter / 2f,
                        glowDiameter,
                        glowDiameter);
                    using (PathGradientBrush glow = new PathGradientBrush(glowPath))
                    {
                        glow.CenterColor = Color.FromArgb(105, Palette.GreenBright);
                        glow.SurroundColors = new Color[] { Color.FromArgb(0, Palette.GreenBright) };
                        e.Graphics.FillPath(glow, glowPath);
                    }
                }

                using (SolidBrush ring = new SolidBrush(Color.FromArgb(210, Palette.GreenBright)))
                using (SolidBrush core = new SolidBrush(Color.White))
                {
                    e.Graphics.FillEllipse(ring, dotX - 4f, dotY - 4f, 8f, 8f);
                    e.Graphics.FillEllipse(core, dotX - 1.8f, dotY - 1.8f, 3.6f, 3.6f);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                shownRadius + " px radius",
                new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point),
                new Rectangle(2, 0, Width - 4, 16),
                Palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(
                e.Graphics,
                "actual size",
                new Font("Segoe UI", 7F, FontStyle.Regular, GraphicsUnit.Point),
                new Rectangle(2, Height - 16, Width - 4, 16),
                Palette.Dim,
                TextFormatFlags.Right | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);
        }
    }
}
