using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace CustomControls
{
    /// <summary>
    /// A dark-themed, continuous-week WinForms calendar that displays weeks vertically,
    /// supports highlighting specific dates, and rendering task spans across date ranges.
    /// .NET Framework 4.6.1 compatible.
    /// </summary>
    [DefaultEvent("DateClicked")]
    public class MultiMonthCalendar : Control
    {
        private const int DowHeight = 24;
        private const int TaskBarHeight = 8;
        private const int TaskBarSpacing = 2;
        private const int ControlPadding = 10;

        private int _minWeekRowHeight = 40;
        private int _weeksToDisplay = 12;

        private DateTime _startDate = GetMondayOfWeek(DateTime.Today);
        private readonly HashSet<DateTime> _highlighted = new HashSet<DateTime>();
        private readonly List<TaskSpan> _tasks = new List<TaskSpan>();

        private ToolTip _tooltip;
        private DateTime? _hoverDate;
        private VScrollBar _vScrollBar;
        private int _scrollWeekOffset = 0;

        public MultiMonthCalendar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9f);
            ForeColor = Color.FromArgb(230, 230, 230);
            BackColor = Color.FromArgb(30, 30, 30);
            HeaderBackColor = Color.FromArgb(45, 45, 45);
            AccentColor = Color.FromArgb(58, 122, 254);
            GridLineColor = Color.FromArgb(55, 55, 55);
            TodayOutlineColor = Color.FromArgb(90, 150, 255);
            HighlightFillColor = Color.FromArgb(60, 120, 255, 60);
            TaskDefaultColor = Color.FromArgb(90, 150, 255);

            _tooltip = new ToolTip { IsBalloon = false, UseFading = true, UseAnimation = true, AutoPopDelay = 8000, InitialDelay = 400, ReshowDelay = 200 };

            _vScrollBar = new VScrollBar { Dock = DockStyle.Right, Visible = true };
            _vScrollBar.Scroll += VScrollBar_Scroll;
            Controls.Add(_vScrollBar);

            MouseWheel += MultiMonthCalendar_MouseWheel;
            MouseMove += MultiMonthCalendar_MouseMove;
            MouseLeave += (s, e) => { _hoverDate = null; _tooltip.Hide(this); Invalidate(); };
        }

        private static DateTime GetMondayOfWeek(DateTime date)
        {
            var culture = CultureInfo.CurrentCulture;
            var fdow = culture.DateTimeFormat.FirstDayOfWeek;
            int diff = ((int)date.DayOfWeek - (int)fdow + 7) % 7;
            return date.AddDays(-diff).Date;
        }

        #region Public API

        [Category("Layout"), Description("Minimum height for a week row (without task bars).")]
        public int MinWeekRowHeight
        {
            get => _minWeekRowHeight;
            set { _minWeekRowHeight = Math.Max(30, value); Invalidate(); }
        }

        [Category("Behavior"), Description("Number of weeks to display in the view.")]
        public int WeeksToDisplay
        {
            get => _weeksToDisplay;
            set { _weeksToDisplay = Math.Max(1, value); UpdateScrollBar(); Invalidate(); }
        }

        [Category("Behavior"), Description("The first week's starting date (Monday).")]
        public DateTime StartDate
        {
            get => _startDate;
            set { _startDate = GetMondayOfWeek(value); UpdateScrollBar(); Invalidate(); }
        }

        [Category("Appearance"), Description("Header background color.")]
        public Color HeaderBackColor { get; set; }

        [Category("Appearance"), Description("Accent color (selection / header text / task default).")]
        public Color AccentColor { get; set; }

        [Category("Appearance"), Description("Grid line color.")]
        public Color GridLineColor { get; set; }

        [Category("Appearance"), Description("Outline color for today's date.")]
        public Color TodayOutlineColor { get; set; }

        [Category("Appearance"), Description("Default task color.")]
        public Color TaskDefaultColor { get; set; }

        [Category("Appearance"), Description("Fill for highlighted dates.")]
        public Color HighlightFillColor { get; set; }

        public struct TaskSpan
        {
            public DateTime Start;
            public DateTime End;
            public Color Color;
            public string Text;

            public TaskSpan(DateTime start, DateTime end, string text = null, Color? color = null)
            {
                Start = start.Date; End = end.Date;
                if (End < Start) { var t = Start; Start = End; End = t; }
                Text = text ?? string.Empty;
                Color = color ?? Color.Empty;
            }
        }

        public void AddHighlight(DateTime date)
        {
            _highlighted.Add(date.Date);
            Invalidate();
        }

        public void RemoveHighlight(DateTime date)
        {
            _highlighted.Remove(date.Date);
            Invalidate();
        }

        public void ClearHighlights()
        {
            _highlighted.Clear();
            Invalidate();
        }

        public void AddTask(TaskSpan task)
        {
            _tasks.Add(task);
            UpdateScrollBar();
            Invalidate();
        }

        public void AddTask(DateTime start, DateTime end, string text = null, Color? color = null)
            => AddTask(new TaskSpan(start, end, text, color));

        public void SetTasks(IEnumerable<TaskSpan> tasks)
        {
            _tasks.Clear();
            _tasks.AddRange(tasks ?? Enumerable.Empty<TaskSpan>());
            UpdateScrollBar();
            Invalidate();
        }

        public void ClearTasks()
        {
            _tasks.Clear();
            UpdateScrollBar();
            Invalidate();
        }

        public event EventHandler<DateClickedEventArgs> DateClicked;
        public class DateClickedEventArgs : EventArgs
        {
            public DateTime Date { get; private set; }
            public MouseButtons Button { get; private set; }
            public DateClickedEventArgs(DateTime date, MouseButtons button) { Date = date; Button = button; }
        }

        public event EventHandler<TaskClickedEventArgs> TaskClicked;
        public class TaskClickedEventArgs : EventArgs
        {
            public TaskSpan Task { get; private set; }
            public TaskClickedEventArgs(TaskSpan task) { Task = task; }
        }

        #endregion

        #region Layout helpers

        private struct WeekLayout
        {
            public DateTime WeekStart;
            public Rectangle Bounds;
            public int BaseHeight;
            public int TaskAreaHeight;
            public List<int> TaskRowsForWeek;
        }

        private List<int> AssignTaskRows()
        {
            var taskRows = new List<int>();
            var sortedIndices = Enumerable.Range(0, _tasks.Count)
                .OrderBy(i => _tasks[i].Start)
                .ThenBy(i => _tasks[i].End)
                .ToList();
            
            for (int idx = 0; idx < _tasks.Count; idx++)
            {
                taskRows.Add(-1);
            }
            
            foreach (var taskIndex in sortedIndices)
            {
                var task = _tasks[taskIndex];
                int row = 0;
                bool placed = false;
                
                while (!placed)
                {
                    bool hasConflict = false;
                    for (int i = 0; i < _tasks.Count; i++)
                    {
                        if (i == taskIndex || taskRows[i] < 0 || taskRows[i] != row) continue;
                        
                        var other = _tasks[i];
                        if (!(task.End < other.Start || task.Start > other.End))
                        {
                            hasConflict = true;
                            break;
                        }
                    }
                    
                    if (!hasConflict)
                    {
                        taskRows[taskIndex] = row;
                        placed = true;
                    }
                    else
                    {
                        row++;
                    }
                }
            }
            
            return taskRows;
        }

        private List<WeekLayout> ComputeWeekLayouts()
        {
            var layouts = new List<WeekLayout>();
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return layouts;

            var taskRows = AssignTaskRows();
            int contentWidth = ClientSize.Width - _vScrollBar.Width - ControlPadding * 2;
            int cellW = Math.Max(30, contentWidth / 7);

            int yPos = ControlPadding + DowHeight;
            DateTime weekStart = _startDate.AddDays(_scrollWeekOffset * 7);

            for (int w = 0; w < _weeksToDisplay; w++)
            {
                var currentWeekStart = weekStart.AddDays(w * 7);
                var currentWeekEnd = currentWeekStart.AddDays(6);

                // Find max task row for this week
                int maxTaskRow = -1;
                var weekTaskIndices = new List<int>();
                
                for (int i = 0; i < _tasks.Count; i++)
                {
                    if (taskRows[i] < 0) continue;
                    var t = _tasks[i];
                    
                    if (!(t.End < currentWeekStart || t.Start > currentWeekEnd))
                    {
                        weekTaskIndices.Add(i);
                        maxTaskRow = Math.Max(maxTaskRow, taskRows[i]);
                    }
                }

                int taskAreaHeight = maxTaskRow >= 0 ? (maxTaskRow + 1) * (TaskBarHeight + TaskBarSpacing) : 0;
                int totalHeight = _minWeekRowHeight + taskAreaHeight;

                var bounds = new Rectangle(ControlPadding, yPos, cellW * 7, totalHeight);

                layouts.Add(new WeekLayout
                {
                    WeekStart = currentWeekStart,
                    Bounds = bounds,
                    BaseHeight = _minWeekRowHeight,
                    TaskAreaHeight = taskAreaHeight,
                    TaskRowsForWeek = taskRows
                });

                yPos += totalHeight + 1;
            }

            return layouts;
        }

        private Rectangle GetDateCellRect(WeekLayout layout, DateTime date)
        {
            if (date < layout.WeekStart || date > layout.WeekStart.AddDays(6))
                return Rectangle.Empty;

            var culture = CultureInfo.CurrentCulture;
            var fdow = culture.DateTimeFormat.FirstDayOfWeek;
            int dayOffset = ((int)date.DayOfWeek - (int)fdow + 7) % 7;

            int cellW = layout.Bounds.Width / 7;
            var x = layout.Bounds.Left + dayOffset * cellW;
            var y = layout.Bounds.Top;
            
            return new Rectangle(x, y, cellW, layout.Bounds.Height);
        }

        private DateTime? HitTest(Point p)
        {
            foreach (var wl in ComputeWeekLayouts())
            {
                if (!wl.Bounds.Contains(p)) continue;

                int cellW = wl.Bounds.Width / 7;
                for (int col = 0; col < 7; col++)
                {
                    var cellRect = new Rectangle(wl.Bounds.Left + col * cellW, wl.Bounds.Top, cellW, wl.Bounds.Height);
                    if (cellRect.Contains(p))
                    {
                        return wl.WeekStart.AddDays(col);
                    }
                }
            }
            return null;
        }

        private void UpdateScrollBar()
        {
            if (_vScrollBar == null) return;
            
            int totalWeeks = 52;
            _vScrollBar.Maximum = Math.Max(0, totalWeeks - _weeksToDisplay + _vScrollBar.LargeChange - 1);
            _vScrollBar.LargeChange = Math.Max(1, _weeksToDisplay / 2);
            _vScrollBar.SmallChange = 1;
            _vScrollBar.Value = Math.Min(_scrollWeekOffset, _vScrollBar.Maximum);
        }

        #endregion

        #region Painting

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            DrawDayOfWeekHeader(g);

            var layouts = ComputeWeekLayouts();
            foreach (var wl in layouts)
            {
                DrawWeekRow(g, wl);
            }
        }

        private void DrawDayOfWeekHeader(Graphics g)
        {
            var culture = CultureInfo.CurrentCulture;
            var names = culture.DateTimeFormat.AbbreviatedDayNames;
            var first = culture.DateTimeFormat.FirstDayOfWeek;

            int contentWidth = ClientSize.Width - _vScrollBar.Width - ControlPadding * 2;
            int cellW = Math.Max(30, contentWidth / 7);
            var headerRect = new Rectangle(ControlPadding, ControlPadding, cellW * 7, DowHeight);

            using (var headerBg = new SolidBrush(HeaderBackColor))
            using (var textBrush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.FillRectangle(headerBg, headerRect);

                for (int i = 0; i < 7; i++)
                {
                    int idx = ((int)first + i) % 7;
                    string label = names[idx].ToUpperInvariant();
                    var cell = new Rectangle(headerRect.Left + i * cellW, headerRect.Top, cellW, headerRect.Height);
                    g.DrawString(label, Font, textBrush, cell, sf);
                }
            }
        }

        private void DrawWeekRow(Graphics g, WeekLayout wl)
        {
            using (var borderPen = new Pen(GridLineColor))
            using (var textBrush = new SolidBrush(ForeColor))
            using (var mutedTextBrush = new SolidBrush(Color.FromArgb(170, 170, 170)))
            using (var highlightFill = new SolidBrush(HighlightFillColor))
            using (var hoverFill = new SolidBrush(Color.FromArgb(50, 50, 50)))
            using (var todayPen = new Pen(TodayOutlineColor, 2f))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
            {
                g.DrawRectangle(borderPen, wl.Bounds);

                int cellW = wl.Bounds.Width / 7;

                // Draw each day cell
                for (int col = 0; col < 7; col++)
                {
                    var date = wl.WeekStart.AddDays(col);
                    var cellRect = new Rectangle(wl.Bounds.Left + col * cellW, wl.Bounds.Top, cellW, wl.Bounds.Height);

                    // Vertical separator
                    if (col > 0)
                        g.DrawLine(borderPen, cellRect.Left, cellRect.Top, cellRect.Left, cellRect.Bottom);

                    // Hover fill
                    if (_hoverDate.HasValue && _hoverDate.Value == date)
                    {
                        g.FillRectangle(hoverFill, cellRect);
                    }

                    // Highlight fill
                    if (_highlighted.Contains(date))
                    {
                        g.FillRectangle(highlightFill, cellRect);
                    }

                    // Date number
                    var brush = (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) ? mutedTextBrush : textBrush;
                    var textRect = new Rectangle(cellRect.X + 4, cellRect.Y + 4, cellRect.Width - 8, 20);
                    
                    string dayText = date.Day.ToString(CultureInfo.InvariantCulture);
                    if (date.Day == 1)
                        dayText = date.ToString("MMM d", CultureInfo.CurrentCulture);
                    
                    g.DrawString(dayText, Font, brush, textRect, sf);

                    // Today outline
                    if (date.Date == DateTime.Today)
                    {
                        var inset = Rectangle.Inflate(cellRect, -2, -2);
                        g.DrawRectangle(todayPen, inset);
                    }
                }

                // Draw task bars
                DrawTasksForWeek(g, wl);
            }
        }

        private void DrawTasksForWeek(Graphics g, WeekLayout wl)
        {
            var weekStart = wl.WeekStart;
            var weekEnd = weekStart.AddDays(6);

            for (int taskIndex = 0; taskIndex < _tasks.Count; taskIndex++)
            {
                var t = _tasks[taskIndex];
                if (t.End < weekStart || t.Start > weekEnd) continue;
                if (wl.TaskRowsForWeek[taskIndex] < 0) continue;

                int taskRow = wl.TaskRowsForWeek[taskIndex];

                var segStart = t.Start > weekStart ? t.Start : weekStart;
                var segEnd = t.End < weekEnd ? t.End : weekEnd;

                int cellW = wl.Bounds.Width / 7;
                var culture = CultureInfo.CurrentCulture;
                var fdow = culture.DateTimeFormat.FirstDayOfWeek;

                int startCol = ((int)segStart.DayOfWeek - (int)fdow + 7) % 7;
                int endCol = ((int)segEnd.DayOfWeek - (int)fdow + 7) % 7;

                int barTop = wl.Bounds.Bottom - wl.TaskAreaHeight + (taskRow * (TaskBarHeight + TaskBarSpacing)) + TaskBarSpacing;
                var barRect = Rectangle.FromLTRB(
                    wl.Bounds.Left + startCol * cellW + 2,
                    barTop,
                    wl.Bounds.Left + (endCol + 1) * cellW - 2,
                    barTop + TaskBarHeight);

                using (var barBrush = new SolidBrush(t.Color.IsEmpty ? TaskDefaultColor : t.Color))
                using (var textBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                {
                    g.FillRectangle(barBrush, barRect);

                    if (!string.IsNullOrEmpty(t.Text))
                    {
                        var textRect = Rectangle.Inflate(barRect, -4, -1);
                        using (var small = new Font(Font, FontStyle.Bold))
                            g.DrawString(t.Text, small, textBrush, textRect, sf);
                    }
                }

                // Hover effect: darken the portion over the hovered date
                if (_hoverDate.HasValue && _hoverDate.Value >= segStart && _hoverDate.Value <= segEnd)
                {
                    int hoverCol = ((int)_hoverDate.Value.DayOfWeek - (int)fdow + 7) % 7;
                    var hoverBarRect = Rectangle.FromLTRB(
                        wl.Bounds.Left + hoverCol * cellW,
                        barRect.Top,
                        wl.Bounds.Left + (hoverCol + 1) * cellW,
                        barRect.Bottom);

                    var clippedRect = Rectangle.Intersect(barRect, hoverBarRect);
                    if (!clippedRect.IsEmpty)
                    {
                        using (var darkenBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                        {
                            g.FillRectangle(darkenBrush, clippedRect);
                        }
                    }
                }
            }
        }

        #endregion

        #region Interaction

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var date = HitTest(e.Location);
            if (date.HasValue)
            {
                DateClicked?.Invoke(this, new DateClickedEventArgs(date.Value, e.Button));
            }

            // Task hit-test
            foreach (var wl in ComputeWeekLayouts())
            {
                if (!wl.Bounds.Contains(e.Location)) continue;

                var weekStart = wl.WeekStart;
                var weekEnd = weekStart.AddDays(6);
                int cellW = wl.Bounds.Width / 7;
                var culture = CultureInfo.CurrentCulture;
                var fdow = culture.DateTimeFormat.FirstDayOfWeek;

                for (int taskIndex = 0; taskIndex < _tasks.Count; taskIndex++)
                {
                    var t = _tasks[taskIndex];
                    if (t.End < weekStart || t.Start > weekEnd) continue;
                    if (wl.TaskRowsForWeek[taskIndex] < 0) continue;

                    int taskRow = wl.TaskRowsForWeek[taskIndex];
                    var segStart = t.Start > weekStart ? t.Start : weekStart;
                    var segEnd = t.End < weekEnd ? t.End : weekEnd;

                    int startCol = ((int)segStart.DayOfWeek - (int)fdow + 7) % 7;
                    int endCol = ((int)segEnd.DayOfWeek - (int)fdow + 7) % 7;

                    int barTop = wl.Bounds.Bottom - wl.TaskAreaHeight + (taskRow * (TaskBarHeight + TaskBarSpacing)) + TaskBarSpacing;
                    var barRect = Rectangle.FromLTRB(
                        wl.Bounds.Left + startCol * cellW + 2,
                        barTop,
                        wl.Bounds.Left + (endCol + 1) * cellW - 2,
                        barTop + TaskBarHeight);

                    if (barRect.Contains(e.Location))
                    {
                        TaskClicked?.Invoke(this, new TaskClickedEventArgs(t));
                        return;
                    }
                }
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            return k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up:
                    _scrollWeekOffset = Math.Max(0, _scrollWeekOffset - 1);
                    UpdateScrollBar();
                    Invalidate();
                    break;
                case Keys.Down:
                    _scrollWeekOffset++;
                    UpdateScrollBar();
                    Invalidate();
                    break;
                case Keys.PageUp:
                    _scrollWeekOffset = Math.Max(0, _scrollWeekOffset - _weeksToDisplay);
                    UpdateScrollBar();
                    Invalidate();
                    break;
                case Keys.PageDown:
                    _scrollWeekOffset += _weeksToDisplay;
                    UpdateScrollBar();
                    Invalidate();
                    break;
            }
        }

        private void MultiMonthCalendar_MouseWheel(object sender, MouseEventArgs e)
        {
            int delta = e.Delta / 120;
            _scrollWeekOffset = Math.Max(0, _scrollWeekOffset - delta);
            UpdateScrollBar();
            Invalidate();
        }

        private void VScrollBar_Scroll(object sender, ScrollEventArgs e)
        {
            _scrollWeekOffset = e.NewValue;
            Invalidate();
        }

        private void MultiMonthCalendar_MouseMove(object sender, MouseEventArgs e)
        {
            var hit = HitTest(e.Location);
            if (hit != _hoverDate)
            {
                _hoverDate = hit;
                Invalidate();

                if (_hoverDate.HasValue)
                {
                    var date = _hoverDate.Value;
                    var lines = new List<string> { date.ToString("D", CultureInfo.CurrentCulture) };
                    var relevant = _tasks.Where(t => t.Start <= date && t.End >= date && !string.IsNullOrEmpty(t.Text)).ToList();
                    if (relevant.Count > 0)
                    {
                        foreach (var t in relevant)
                        {
                            lines.Add("• " + t.Text);
                        }
                    }
                    _tooltip.Show(string.Join("\n", lines), this, e.Location + new Size(16, 16), 4000);
                }
                else
                {
                    _tooltip.Hide(this);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollBar();
        }

        #endregion
    }
}
