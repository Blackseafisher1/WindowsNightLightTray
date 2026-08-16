using System;
using System.Windows.Forms;

namespace NightLightTray
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool always = false;
            foreach (string arg in args)
            {
                if (arg == "--always" || arg == "-always" || arg == "--always-on")
                {
                    always = true;
                    break;
                }
            }
            Application.Run(new Form1(always));
        }
    }
}
