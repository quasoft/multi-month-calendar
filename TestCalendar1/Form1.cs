using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestCalendar1
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            calendar.AddHighlight(DateTime.Today);

            // Example: Set a custom font for task bars
            // calendar.TaskBarFont = new Font("Consolas", 7f, FontStyle.Bold);
            
            calendar.AddTask(
                start: new DateTime(2025, 11, 3),
                end: new DateTime(2025, 11, 12),
                text: "Sprint 24",
                color: Color.FromArgb(0xFF, 0x6F, 0x00)
            );

            calendar.AddTask(
                start: new DateTime(2025, 11, 3),
                end: new DateTime(2025, 11, 14),
                text: "Task 3",
                color: Color.FromArgb(0x13, 0xFF, 0x2f)
            );
        }
    }
}
