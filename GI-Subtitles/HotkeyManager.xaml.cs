using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GI_Subtitles
{
    /// <summary>
    /// HotkeyManager.xaml 的交互逻辑
    /// </summary>
    public partial class HotkeyManager : Window
    {
        // Windows API 函数用于注册和注销热键
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 热键常量
        private const int HOTKEY_ID_1 = 9000;
        private const int HOTKEY_ID_2 = 9001;
        private const int HOTKEY_ID_3 = 9002;
        private const int HOTKEY_ID_4 = 9003;

        private const uint MOD_NONE = 0x0000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CTRL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        private IntPtr _windowHandle;
        private ObservableCollection<HotkeyViewModel> _hotkeys;

        public HotkeyManager()
        {
            InitializeComponent();
            DataContext = this;

            // 初始化热键列表
            InitializeHotkeys();
            Console.WriteLine("InitializeHotkeys");
            // 绑定按钮事件
            saveButton.Click += SaveButton_Click;
            resetButton.Click += ResetButton_Click;
        }

        // 修改InitializeHotkeys方法
        public void InitializeHotkeys()
        {
            // 从设置加载热键
            var settings = HotkeySettingsManager.LoadSettings();

            // 创建可用按键列表 (A-Z)
            var availableKeys = Enumerable.Range(65, 26).Select(c => (char)c).ToList();

            // 初始化热键集合
            _hotkeys = new ObservableCollection<HotkeyViewModel>(
                settings.Hotkeys.Select(h => new HotkeyViewModel
                {
                    Id = h.Id,
                    Description = h.Description,
                    IsCtrl = h.IsCtrl,
                    IsShift = h.IsShift,
                    SelectedKey = h.SelectedKey,
                    AvailableKeys = availableKeys
                })
            );

            hotkeyListView.ItemsSource = _hotkeys;
        }

        // 修改SaveButton_Click方法，添加保存到文件的功能
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 验证每个热键：必须包含 Ctrl 或 Shift
            foreach (var hotkey in _hotkeys)
            {
                if (!hotkey.IsCtrl && !hotkey.IsShift)
                {
                    MessageBox.Show($"快捷键 \"{hotkey.Description}\" 必须包含 Ctrl 或 Shift。",
                                    "无效快捷键", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 确保选中的是 A-Z（防御性检查）
                if (!char.IsLetter(hotkey.SelectedKey) || hotkey.SelectedKey < 'A' || hotkey.SelectedKey > 'Z')
                {
                    MessageBox.Show($"快捷键 \"{hotkey.Description}\" 必须选择 A-Z 之间的字母。",
                                    "无效按键", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 检查重复
            var hotkeyTexts = _hotkeys.Select(h => h.GetHotkeyText()).ToList();
            if (hotkeyTexts.GroupBy(t => t).Any(g => g.Count() > 1))
            {
                MessageBox.Show("发现重复的快捷键组合，请修改后再保存。",
                                "重复的快捷键", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 保存设置
            var settings = new HotkeySettings
            {
                Hotkeys = _hotkeys.Select(h => new HotkeyData
                {
                    Id = h.Id,
                    Description = h.Description,
                    IsCtrl = h.IsCtrl,
                    IsShift = h.IsShift,
                    SelectedKey = h.SelectedKey
                }).ToList()
            };

            HotkeySettingsManager.SaveSettings(settings);
            RegisterAllHotkeys();

            MessageBox.Show("快捷键设置已保存。", "保存成功",
                            MessageBoxButton.OK, MessageBoxImage.Information);

            foreach (var hotkey in _hotkeys)
            {
                hotkey.IsEditing = false;
            }
        }
        public void InitializeKey(IntPtr handle)
        {
            Console.WriteLine("OnSourceInitialized");
            _windowHandle = handle;
            RegisterAllHotkeys();
        }


        private void HandleHotkeyPress(int hotkeyId)
        {
            var hotkey = _hotkeys.FirstOrDefault(h => h.Id == hotkeyId);
            if (hotkey != null)
            {
                MessageBox.Show($"触发快捷键: {hotkey.Description}\n组合键: {hotkey.GetHotkeyText()}",
                                "快捷键触发", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RegisterAllHotkeys()
        {
            // 先注销所有热键
            UnregisterAllHotkeys();

            // 注册所有热键
            foreach (var hotkey in _hotkeys)
            {
                RegisterHotkey(hotkey);
            }
        }

        // 添加这个辅助方法到你的类中
        private uint GetVirtualKeyFromChar(char c)
        {
            // 对于字母字符，直接转换为对应的虚拟键码
            if (char.IsLetter(c))
            {
                // 字母的虚拟键码是ASCII码值 (A=65, B=66, ..., Z=90)
                return (uint)char.ToUpper(c);
            }

            return 0;
        }

        private void RegisterHotkey(HotkeyViewModel hotkey)
        {
            uint modifiers = 0;
            if (hotkey.IsCtrl) modifiers |= MOD_CTRL;
            if (hotkey.IsShift) modifiers |= MOD_SHIFT;

            // 使用这个自定义转换方法
            uint virtualKey = GetVirtualKeyFromChar(hotkey.SelectedKey);



            if (!RegisterHotKey(_windowHandle, hotkey.Id, modifiers, virtualKey))
            {
                // 注册失败，可能是因为热键冲突
                MessageBox.Show($"无法注册快捷键 {hotkey.GetHotkeyText()}\n可能与其他应用程序冲突。",
                                "注册失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void UnregisterAllHotkeys()
        {
            foreach (var hotkey in _hotkeys)
            {
                UnregisterHotKey(_windowHandle, hotkey.Id);
            }
        }


        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定要恢复默认快捷键设置吗？", "确认恢复默认",
                               MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                InitializeHotkeys();
                RegisterAllHotkeys();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            UnregisterAllHotkeys();
        }
    }

    public class HotkeyViewModel : INotifyPropertyChanged
    {
        private int _id;
        private string _description;
        private bool _isCtrl;
        private bool _isShift;
        private char _selectedKey;
        private bool _isEditing;
        private List<char> _availableKeys;

        public event PropertyChangedEventHandler PropertyChanged;

        public int Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged(nameof(Description));
            }
        }

        public bool IsCtrl
        {
            get => _isCtrl;
            set
            {
                _isCtrl = value;
                OnPropertyChanged(nameof(IsCtrl));
                OnPropertyChanged(nameof(GetHotkeyText));
            }
        }

        public bool IsShift
        {
            get => _isShift;
            set
            {
                _isShift = value;
                OnPropertyChanged(nameof(IsShift));
                OnPropertyChanged(nameof(GetHotkeyText));
            }
        }

        public char SelectedKey
        {
            get => _selectedKey;
            set
            {
                _selectedKey = value;
                OnPropertyChanged(nameof(SelectedKey));
                OnPropertyChanged(nameof(GetHotkeyText));
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                _isEditing = value;
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(ButtonText));
            }
        }

        public List<char> AvailableKeys
        {
            get => _availableKeys;
            set
            {
                _availableKeys = value;
                OnPropertyChanged(nameof(AvailableKeys));
            }
        }

        public string ButtonText => IsEditing ? "取消" : "修改";

        public ICommand ToggleEditCommand => new RelayCommand(ToggleEdit);

        public string GetHotkeyText()
        {
            var parts = new List<string>();
            if (IsCtrl) parts.Add("Ctrl");
            if (IsShift) parts.Add("Shift");
            parts.Add(SelectedKey.ToString());
            return string.Join("+", parts);
        }

        private void ToggleEdit()
        {
            IsEditing = !IsEditing;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
