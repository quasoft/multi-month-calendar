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
    /// A dark-themed, multi-month WinForms calendar that adapts to available space,
    /// supports highlighting specific dates, and rendering task spans across date ranges.
    /// .NET Framework 4.6.1 compatible.
    /// </summary>
    [DefaultEvent("DateClicked")]
    public class MultiMonthCalendar : Control
    {
        private const int HeaderHeight = 28;
        private const int DowHeight = 20;
        private const int CellGap = 2;
        private const int MonthPadding = 10;

        private int _minMonthWidth = 240;
        private int _minMonthHeight = 200;

        private DateTime _startMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private readonly HashSet<DateTime> _highlighted = new HashSet<DateTime>();
        private readonly List<TaskSpan> _tasks = new List<TaskSpan>();

        private ToolTip _tooltip;
        private DateTime? _hoverDate;

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

            MouseWheel += MultiMonthCalendar_MouseWheel;
            MouseMove += MultiMonthCalendar_MouseMove;
            MouseLeave += (s, e) => { _hoverDate = null; _tooltip.Hide(this); Invalidate(); };
        }

        #region Public API

        [Category("Layout"), Description("Minimum width of a single month panel.")]
        public int MinMonthWidth
        {
            get => _minMonthWidth; set { _minMonthWidth = Math.Max(160, value); Invalidate(); }
        }

        [Category("Layout"), Description("Minimum height of a single month panel.")]
        public int MinMonthHeight
        {
            get => _minMonthHeight; set { _minMonthHeight = Math.Max(160, value); Invalidate(); }
        }

        [Category("Behavior"), Description("The first (top-left) month shown.")]
        public DateTime StartMonth
        {
            get => _startMonth;
            set { _startMonth = new DateTime(value.Year, value.Month, 1); Invalidate(); }
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
            public DateTime Start;   // inclusive date component
            public DateTime End;     // inclusive date component
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
            Invalidate();
        }

        public void AddTask(DateTime start, DateTime end, string text = null, Color? color = null)
            => AddTask(new TaskSpan(start, end, text, color));

        public void SetTasks(IEnumerable<TaskSpan> tasks)
        {
            _tasks.Clear();
            _tasks.AddRange(tasks ?? Enumerable.Empty<TaskSpan>());
            Invalidate();
        }

        public void ClearTasks()
        {
            _tasks.Clear();
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

        private struct MonthLayout
        {
            public DateTime Month;
            public Rectangle Bounds;
            public Rectangle GridBounds;
            public Size CellSize;
        }

        private List<MonthLayout> ComputeLayout(Graphics g)
        {
            var layouts = new List<MonthLayout>();
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return layouts;

            int cols = Math.Max(1, ClientSize.Width / (_minMonthWidth + MonthPadding));
            int rows = Math.Max(1, ClientSize.Height / (_minMonthHeight + MonthPadding));
            int total = cols * rows;

            int monthWidth = ClientSize.Width / cols;
            int monthHeight = ClientSize.Height / rows;

            var month = _startMonth;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var x = c * monthWidth;
                    var y = r * monthHeight;
                    var bounds = new Rectangle(x, y, monthWidth, monthHeight);

                    // Calculate grid area inside month panel
                    int contentLeft = bounds.Left + MonthPadding;
                    int contentTop = bounds.Top + MonthPadding + HeaderHeight + DowHeight;
                    int contentWidth = bounds.Width - MonthPadding * 2;
                    int contentHeight = bounds.Height - MonthPadding * 2 - HeaderHeight - DowHeight;

                    // 7 columns, 6 rows
                    int cellW = Math.Max(16, (contentWidth - (7 - 1) * CellGap) / 7);
                    int cellH = Math.Max(16, (contentHeight - (6 - 1) * CellGap) / 6);

                    var grid = new Rectangle(contentLeft, contentTop, cellW * 7 + CellGap * 6, cellH * 6 + CellGap * 5);

                    layouts.Add(new MonthLayout
                    {
                        Month = month,
                        Bounds = bounds,
                        GridBounds = grid,
                        CellSize = new Size(cellW, cellH)
                    });

                    month = month.AddMonths(1);
                }
            }

            return layouts;
        }

        private Rectangle GetDateCellRect(MonthLayout layout, DateTime date)
        {
            // date must be in the same displayed month
            if (date.Year != layout.Month.Year || date.Month != layout.Month.Month) return Rectangle.Empty;

            var first = new DateTime(layout.Month.Year, layout.Month.Month, 1);
            var culture = CultureInfo.CurrentCulture;
            var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;

            int offset = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
            int index = (date.Day - 1) + offset; // zero-based index into grid
            int row = index / 7;
            int col = index % 7;

            var x = layout.GridBounds.Left + col * (layout.CellSize.Width + CellGap);
            var y = layout.GridBounds.Top + row * (layout.CellSize.Height + CellGap);
            return new Rectangle(x, y, layout.CellSize.Width, layout.CellSize.Height);
        }

        private DateTime? HitTest(Point p)
        {
            using (var g = CreateGraphics())
            {
                foreach (var ml in ComputeLayout(g))
                {
                    if (!ml.Bounds.Contains(p)) continue;

                    // Iterate cells
                    var first = new DateTime(ml.Month.Year, ml.Month.Month, 1);
                    var daysInMonth = DateTime.DaysInMonth(ml.Month.Year, ml.Month.Month);
                    var culture = CultureInfo.CurrentCulture;
                    var fdow = culture.DateTimeFormat.FirstDayOfWeek;
                    int offset = ((int)first.DayOfWeek - (int)fdow + 7) % 7;

                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        int idx = (d - 1) + offset;
                        int row = idx / 7; int col = idx % 7;
                        var cell = new Rectangle(
                            ml.GridBounds.Left + col * (ml.CellSize.Width + CellGap),
                            ml.GridBounds.Top + row * (ml.CellSize.Height + CellGap),
                            ml.CellSize.Width,
                            ml.CellSize.Height);
                        if (cell.Contains(p))
                            return new DateTime(ml.Month.Year, ml.Month.Month, d);
                    }
                }
            }
            return null;
        }

        #endregion

        #region Painting

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

            var layouts = ComputeLayout(g);
            foreach (var ml in layouts)
            {
                DrawMonthPanel(g, ml);
            }
        }

        private void DrawMonthPanel(Graphics g, MonthLayout ml)
        {
            // Outer panel border (subtle)
            using (var borderPen = new Pen(GridLineColor))
            using (var headerBg = new SolidBrush(HeaderBackColor))
            using (var headerAccent = new SolidBrush(AccentColor))
            using (var textBrush = new SolidBrush(ForeColor))
            using (var gridPen = new Pen(GridLineColor))
            {
                g.DrawRectangle(borderPen, ml.Bounds.X, ml.Bounds.Y, ml.Bounds.Width - 1, ml.Bounds.Height - 1);

                // Header
                var headerRect = new Rectangle(ml.Bounds.Left + MonthPadding, ml.Bounds.Top + MonthPadding, ml.Bounds.Width - MonthPadding * 2, HeaderHeight);
                g.FillRectangle(headerBg, headerRect);

                string title = new DateTime(ml.Month.Year, ml.Month.Month, 1).ToString("Y", CultureInfo.CurrentCulture);
                var titleSize = g.MeasureString(title, Font);
                var titlePt = new PointF(headerRect.Left + 6, headerRect.Top + (headerRect.Height - titleSize.Height) / 2f);
                g.DrawString(title, Font, headerAccent, titlePt);

                // Day of week header
                var dowRect = new Rectangle(headerRect.Left, headerRect.Bottom, headerRect.Width, DowHeight);
                DrawDayOfWeek(g, dowRect);

                // Grid background
                DrawDateGrid(g, ml);

                // Draw tasks over grid
                DrawTasks(g, ml);
            }
        }

        private void DrawDayOfWeek(Graphics g, Rectangle rect)
        {
            var culture = CultureInfo.CurrentCulture;
            var names = culture.DateTimeFormat.AbbreviatedDayNames;
            var first = culture.DateTimeFormat.FirstDayOfWeek;

            using (var textBrush = new SolidBrush(Color.FromArgb(180, 180, 180)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < 7; i++)
                {
                    int idx = ((int)first + i) % 7;
                    string label = names[idx].ToUpperInvariant();
                    int w = rect.Width / 7;
                    var cell = new Rectangle(rect.Left + i * w, rect.Top, (i == 6 ? rect.Right - (rect.Left + i * w) : w), rect.Height);
                    g.DrawString(label, Font, textBrush, cell, sf);
                }
            }
        }

        private void DrawDateGrid(Graphics g, MonthLayout ml)
        {
            var first = new DateTime(ml.Month.Year, ml.Month.Month, 1);
            int days = DateTime.DaysInMonth(ml.Month.Year, ml.Month.Month);
            var culture = CultureInfo.CurrentCulture;
            var fdow = culture.DateTimeFormat.FirstDayOfWeek;
            int offset = ((int)first.DayOfWeek - (int)fdow + 7) % 7;

            using (var gridPen = new Pen(GridLineColor))
            using (var todayPen = new Pen(TodayOutlineColor, 2f))
            using (var textBrush = new SolidBrush(ForeColor))
            using (var mutedTextBrush = new SolidBrush(Color.FromArgb(170, 170, 170)))
            using (var highlightFill = new SolidBrush(HighlightFillColor))
            using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near })
            {
                // Draw cells
                for (int i = 0; i < 6; i++)
                {
                    for (int j = 0; j < 7; j++)
                    {
                        var x = ml.GridBounds.Left + j * (ml.CellSize.Width + CellGap);
                        var y = ml.GridBounds.Top + i * (ml.CellSize.Height + CellGap);
                        var rect = new Rectangle(x, y, ml.CellSize.Width, ml.CellSize.Height);
                        g.DrawRectangle(gridPen, rect);
                    }
                }

                // Numbers / highlights
                for (int day = 1; day <= days; day++)
                {
                    int idx = (day - 1) + offset;
                    int row = idx / 7; int col = idx % 7;
                    var rect = new Rectangle(
                        ml.GridBounds.Left + col * (ml.CellSize.Width + CellGap),
                        ml.GridBounds.Top + row * (ml.CellSize.Height + CellGap),
                        ml.CellSize.Width,
                        ml.CellSize.Height);

                    var date = new DateTime(ml.Month.Year, ml.Month.Month, day);

                    // highlight fill
                    if (_highlighted.Contains(date))
                    {
                        g.FillRectangle(highlightFill, rect);
                    }

                    // date number
                    var brush = (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) ? mutedTextBrush : textBrush;
                    var textRect = Rectangle.Inflate(rect, -4, -2);
                    g.DrawString(day.ToString(CultureInfo.InvariantCulture), Font, brush, textRect, sf);

                    // today outline
                    if (date.Date == DateTime.Today)
                    {
                        var inset = Rectangle.Inflate(rect, -2, -2);
                        g.DrawRectangle(todayPen, inset);
                    }

                    // hover effect
                    if (_hoverDate.HasValue && _hoverDate.Value == date)
                    {
                        using (var hoverPen = new Pen(AccentColor))
                        {
                            g.DrawRectangle(hoverPen, Rectangle.Inflate(rect, -1, -1));
                        }
                    }
                }
            }
        }

        private void DrawTasks(Graphics g, MonthLayout ml)
        {
            // Get visible date range for this month panel
            var first = new DateTime(ml.Month.Year, ml.Month.Month, 1);
            var last = new DateTime(ml.Month.Year, ml.Month.Month, DateTime.DaysInMonth(ml.Month.Year, ml.Month.Month));

            foreach (var t in _tasks)
            {
                var spanStart = (t.Start > first) ? t.Start : first;
                var spanEnd = (t.End < last) ? t.End : last;
                if (spanEnd < first || spanStart > last) continue; // no overlap

                // Iterate week by week across the month
                var culture = CultureInfo.CurrentCulture;
                var fdow = culture.DateTimeFormat.FirstDayOfWeek;
                DateTime cursor = spanStart;
                while (cursor <= spanEnd)
                {
                    // Start of the week containing cursor
                    int diff = ((int)cursor.DayOfWeek - (int)fdow + 7) % 7;
                    var weekStart = cursor.AddDays(-diff).Date;
                    var weekEnd = weekStart.AddDays(6);

                    var segStart = cursor;
                    var segEnd = (spanEnd < weekEnd) ? spanEnd : weekEnd;

                    // Rect: from segStart col to segEnd col within the row for segStart
                    var startRect = GetDateCellRect(ml, segStart);
                    var endRect = GetDateCellRect(ml, segEnd);

                    if (!startRect.IsEmpty && !endRect.IsEmpty)
                    {
                        var rowTop = startRect.Top + startRect.Height - 10; // position near bottom of cell
                        var barRect = Rectangle.FromLTRB(startRect.Left + 2, rowTop, endRect.Right - 2, rowTop + 8);

                        using (var barBrush = new SolidBrush(t.Color.IsEmpty ? TaskDefaultColor : t.Color))
                        using (var barPen = new Pen(Color.FromArgb(220, 220, 220), 1))
                        using (var textBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                        {
                            g.FillRectangle(barBrush, barRect);
                            g.DrawRectangle(barPen, barRect);

                            if (!string.IsNullOrEmpty(t.Text))
                            {
                                var textRect = Rectangle.Inflate(barRect, -4, -1);
                                using (var small = new Font(Font, FontStyle.Bold))
                                    g.DrawString(t.Text, small, textBrush, textRect, sf);
                            }
                        }
                    }

                    cursor = segEnd.AddDays(1);
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

            // Task hit-test: rough — check if we clicked on any task bar
            using (var g = CreateGraphics())
            {
                foreach (var ml in ComputeLayout(g))
                {
                    if (!ml.Bounds.Contains(e.Location)) continue;

                    // Build rectangles for tasks (same as draw)
                    var first = new DateTime(ml.Month.Year, ml.Month.Month, 1);
                    var last = new DateTime(ml.Month.Year, ml.Month.Month, DateTime.DaysInMonth(ml.Month.Year, ml.Month.Month));
                    var culture = CultureInfo.CurrentCulture;
                    var fdow = culture.DateTimeFormat.FirstDayOfWeek;

                    foreach (var t in _tasks)
                    {
                        var spanStart = (t.Start > first) ? t.Start : first;
                        var spanEnd = (t.End < last) ? t.End : last;
                        if (spanEnd < first || spanStart > last) continue;

                        DateTime cursor = spanStart;
                        while (cursor <= spanEnd)
                        {
                            int diff = ((int)cursor.DayOfWeek - (int)fdow + 7) % 7;
                            var weekStart = cursor.AddDays(-diff).Date;
                            var weekEnd = weekStart.AddDays(6);
                            var segStart = cursor;
                            var segEnd = (spanEnd < weekEnd) ? spanEnd : weekEnd;

                            var startRect = GetDateCellRect(ml, segStart);
                            var endRect = GetDateCellRect(ml, segEnd);
                            if (!startRect.IsEmpty && !endRect.IsEmpty)
                            {
                                var rowTop = startRect.Top + startRect.Height - 10;
                                var barRect = Rectangle.FromLTRB(startRect.Left + 2, rowTop, endRect.Right - 2, rowTop + 8);
                                if (barRect.Contains(e.Location))
                                {
                                    TaskClicked?.Invoke(this, new TaskClickedEventArgs(t));
                                    return;
                                }
                            }
                            cursor = segEnd.AddDays(1);
                        }
                    }
                }
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Allow arrow/pg keys for navigation
            var k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || k == Keys.PageUp || k == Keys.PageDown || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.PageUp:
                    StartMonth = StartMonth.AddMonths(-1);
                    break;
                case Keys.Right:
                case Keys.PageDown:
                    StartMonth = StartMonth.AddMonths(1);
                    break;
            }
        }

        private void MultiMonthCalendar_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0) StartMonth = StartMonth.AddMonths(-1);
            else if (e.Delta < 0) StartMonth = StartMonth.AddMonths(1);
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
                    // Build tooltip text: date + tasks that include it
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

        #endregion
    }
}
