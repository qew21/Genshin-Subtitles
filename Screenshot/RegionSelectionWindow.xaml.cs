using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Screenshot
{
    public partial class RegionSelectionWindow
    {
        private Point? _selectionStartPos;

        public RegionSelectionWindow()
        {
            InitializeComponent();

            Loaded += (s, e) => Activate();
        }

        public Rect? SelectedRegion { get; private set; }

        public string Prompt
        {
            get { return PromptLabel.Text; }
            set
            {
                PromptLabel.Text = value ?? string.Empty;
                PromptLabel.Visibility = string.IsNullOrWhiteSpace(value)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            Close();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            _selectionStartPos = e.GetPosition(this);

            Mouse.Capture(this);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (!Equals(Mouse.Captured, this) || _selectionStartPos == null)
            {
                return;
            }

            SelectedRegion = new Rect(_selectionStartPos.Value, e.GetPosition(this));

            _selectionStartPos = null;

            Mouse.Capture(null);

            Close();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!Equals(Mouse.Captured, this) || _selectionStartPos == null)
            {
                return;
            }

            var position = e.GetPosition(this);

            var left = Math.Min(_selectionStartPos.Value.X, position.X);
            var top = Math.Min(_selectionStartPos.Value.Y, position.Y);

            Canvas.SetLeft(SelectionImage, -left);
            Canvas.SetTop(SelectionImage, -top);

            Canvas.SetLeft(SelectionBorder, left);
            Canvas.SetTop(SelectionBorder, top);
            SelectionBorder.Width = Math.Abs(position.X - _selectionStartPos.Value.X);
            SelectionBorder.Height = Math.Abs(position.Y - _selectionStartPos.Value.Y);
        }
    }
}