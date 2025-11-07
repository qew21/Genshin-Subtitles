using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ZedGraph;

namespace GI_Subtitles
{
    /// <summary>
    /// About.xaml 的交互逻辑
    /// </summary>
    public partial class About : Window
    {
        INotifyIcon notifyIcon;
        public About(string version, INotifyIcon notify)
        {
            InitializeComponent();
            this.Title += $"({version})";
            notifyIcon = notify;
        }

        private void ResetLocation_Click(object sender, RoutedEventArgs e)
        {
            Config.Set("Pad", 86);
        }

        private void SecondRegion_Click(object sender, RoutedEventArgs e)
        {
            notifyIcon.ChooseRegion2();
        }
    }
}
