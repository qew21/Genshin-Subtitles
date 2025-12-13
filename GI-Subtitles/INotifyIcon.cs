using Emgu.CV.Ocl;
using GI_Subtitles.Properties;
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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media.Animation;
using ZedGraph;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace GI_Subtitles
{

    public class INotifyIcon
    {
        System.Windows.Forms.ContextMenuStrip contextMenuStrip;
        ToolStripMenuItem fontSizeSelector;
        private int Size = Config.Get<int>("Size");
        private bool AutoStart = Config.Get("AutoStart", false);
        public string[] Region = Config.Get<string>("Region").Split(',');
        public string[] Region2 = Config.Get<string>("Region2", "").Split(',');
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        double Scale = 1;
        public bool isContextMenuOpen = false;
        private SettingsWindow data;


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

            ToolStripMenuItem settingItem = new ToolStripMenuItem("程序设定");
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出程序");
            ToolStripMenuItem versionItem = new ToolStripMenuItem(version)
            {
                Enabled = false
            };
            settingItem.Click += (sender, e) =>
            {
                data.ShowDialog();
            };
            exitItem.Click += (sender, e) => { System.Windows.Application.Current.Shutdown(); };
            contextMenuStrip.Items.Add(versionItem);
            contextMenuStrip.Items.Add(new ToolStripSeparator());
            contextMenuStrip.Items.Add(fontSizeSelector);
            contextMenuStrip.Items.Add(settingItem);
            contextMenuStrip.Items.Add(exitItem);
            contextMenuStrip.Opening += ContextMenuStrip_Opening; // 菜单打开前触发
            contextMenuStrip.Closed += ContextMenuStrip_Closed;   // 菜单关闭后触发
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

        public void SetData(SettingsWindow data)
        {
            this.data = data;
        }

        private void DateUpdate()
        {
            data.ShowDialog();
        }

        public void ChooseRegion()
        {
            try
            {
                var rect = Screenshot.Screenshot.GetRegion();
                Config.Set("Region", $"{Convert.ToInt16(rect.TopLeft.X)},{Convert.ToInt16(rect.TopLeft.Y)},{Convert.ToInt16(rect.Width)},{Convert.ToInt16(rect.Height)}");
                Region = Config.Get<string>("Region").ToString().Split(',');
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public void ChooseRegion2()
        {
            try
            {
                var rect = Screenshot.Screenshot.GetRegion();
                Config.Set("Region2", $"{Convert.ToInt16(rect.TopLeft.X)},{Convert.ToInt16(rect.TopLeft.Y)},{Convert.ToInt16(rect.Width)},{Convert.ToInt16(rect.Height)}");
                Region2 = Config.Get<string>("Region2").ToString().Split(',');
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
            if (Size == Convert.ToInt16(code))
            {
                item.Checked = true;
            }
            return item;
        }

        private void SizeItem_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem selectedSize && selectedSize.Checked)
            {
                int newSize = Convert.ToInt16(selectedSize.Tag.ToString());
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
                    Config.Set("Size", Size);
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
        private void ContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            isContextMenuOpen = true;
        }

        private void ContextMenuStrip_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            isContextMenuOpen = false;
        }

        public void ShowRegionOverlay()
        {
            if (Region[1] == "0") return;
            int x = Convert.ToInt16(int.Parse(Region[0]) / Scale);
            int y = Convert.ToInt16(int.Parse(Region[1]) / Scale);
            int w = Convert.ToInt16(int.Parse(Region[2]) / Scale);
            int h = Convert.ToInt16(int.Parse(Region[3]) / Scale);
            Logger.Log.Debug($"x {x} y {y} w {w} h {h}");

            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight,
                Left = 0,
                Top = 0
            };

            var canvas = new Canvas();
            var rect = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.LimeGreen,
                StrokeThickness = 10,
                Width = w,
                Height = h,
                IsHitTestVisible = true // 确保可以捕获鼠标事件
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            canvas.Children.Add(rect);
            overlay.Content = canvas;

            overlay.Show();

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                overlay.Close();
            };
            timer.Start();
        }
    }
}
