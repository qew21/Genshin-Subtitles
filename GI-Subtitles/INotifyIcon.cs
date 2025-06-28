using Microsoft.Win32;
using Screenshot;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media.Animation;
using ZedGraph;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace GI_Subtitles
{

    internal class INotifyIcon
    {
        System.Windows.Forms.ContextMenuStrip contextMenuStrip;
        ToolStripMenuItem fontSizeSelector;
        string Size = ConfigurationManager.AppSettings["Size"];
        bool AutoStart = ConfigurationManager.AppSettings["AutoStart"] == "1";
        public string[] Region = ConfigurationManager.AppSettings["Region"].Split(',');
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        double Scale = 1;


        public NotifyIcon InitializeNotifyIcon(double scale)
        {
            Scale = scale;
            NotifyIcon notifyIcon;
            contextMenuStrip = new ContextMenuStrip();

            fontSizeSelector = new ToolStripMenuItem("字号选择");
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("14"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("16"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("18"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("20"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("22"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("24"));

            ToolStripMenuItem dataItem = new ToolStripMenuItem("语言包管理");
            ToolStripMenuItem aboutItem = new ToolStripMenuItem("帮助");
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出程序");
            ToolStripMenuItem versionItem = new ToolStripMenuItem(version)
            {
                Enabled = false
            };
            ToolStripMenuItem startupItem = new ToolStripMenuItem("开机启动")
            {
                CheckOnClick = true,   // 允许点击打钩
                Checked = AutoStart    // 根据 AutoStart 设置初始状态
            };
            dataItem.Click += (sender, e) => { DateUpdate(); };
            aboutItem.Click += (sender, e) => { About about = new About(version); about.Show(); };
            exitItem.Click += (sender, e) => { System.Windows.Application.Current.Shutdown(); };
            startupItem.Click += (sender, e) =>
            {
                AutoStart = startupItem.Checked;
                SetAutoStart(AutoStart);
            };
            contextMenuStrip.Items.Add(versionItem);
            contextMenuStrip.Items.Add(new ToolStripSeparator());
            contextMenuStrip.Items.Add(fontSizeSelector);
            contextMenuStrip.Items.Add(dataItem);
            contextMenuStrip.Items.Add(startupItem);
            contextMenuStrip.Items.Add(aboutItem);
            contextMenuStrip.Items.Add(exitItem);

            Uri iconUri = new Uri("pack://application:,,,/Resources/mask.ico");
            Stream iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream;
            notifyIcon = new NotifyIcon
            {
                Icon = new Icon(iconStream),
                Visible = true,
                ContextMenuStrip = contextMenuStrip
            };
            SetAutoStart(AutoStart);
            return notifyIcon;
        }


        private void DateUpdate()
        {
            Data data = new Data(version);
            data.ShowDialog();
        }


        public void ChooseRegion()
        {
            try
            {
                var rect = Screenshot.Screenshot.GetRegion(Scale);
                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                config.AppSettings.Settings["Region"].Value = $"{Convert.ToInt16(rect.TopLeft.X * Scale)},{Convert.ToInt16(rect.TopLeft.Y * Scale)},{Convert.ToInt16(rect.Width * Scale)},{Convert.ToInt16(rect.Height * Scale)}";
                Console.WriteLine(config.AppSettings.Settings["Region"].Value);
                Region = config.AppSettings.Settings["Region"].Value.Split(',');
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }


        private ToolStripMenuItem CreateSizeItem(string code)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(code)
            {
                Tag = code,
                CheckOnClick = true
            };
            item.CheckedChanged += SizeItem_CheckedChanged;
            if (Size == code)
            {
                item.Checked = true;
            }
            return item;
        }

        private void SizeItem_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem selectedSize && selectedSize.Checked)
            {
                string newSize = selectedSize.Tag.ToString();
                if (Size != newSize)
                {
                    Size = newSize;

                    foreach (ToolStripMenuItem langItem in fontSizeSelector.DropDownItems)
                    {
                        if (langItem != selectedSize)
                        {
                            langItem.Checked = false;
                        }
                    }
                    Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                    config.AppSettings.Settings["Size"].Value = newSize;
                    config.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection("appSettings");
                }
            }
        }

        private void SetAutoStart(bool autoStart)
        {
            string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null)
            {
                Logger.Log.Error("无法打开注册表项");
            }

            string existingValue = (string)key.GetValue(Process.GetCurrentProcess().ProcessName, null);
            if (autoStart)
            {
                if (existingValue != appPath)
                {
                    key.SetValue(Process.GetCurrentProcess().ProcessName, appPath);
                    Logger.Log.Info("开机启动项添加成功！");
                }
            }
            else
            {
                if (existingValue != null)
                {
                    key.DeleteValue(Process.GetCurrentProcess().ProcessName, false);
                    Logger.Log.Info("开机启动项已移除！");
                }
            }
        }
    }
}
