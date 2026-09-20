using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GI_Subtitles.Views.TextSearch
{
    public static class Util
    {
        public static List<TextBox> GetTextBoxes(object container)
        {
            if (!(container is FrameworkElement fe)) throw new Exception("Except a FrameworkElement");
            
            // https://stackoverflow.com/a/92765
            var tree = new Stack<FrameworkElement>();
            tree.Push(fe);
            var textBoxes = new List<TextBox>();
            
            while (tree.Count > 0)
            {
                FrameworkElement current = tree.Pop();
                if (current is TextBox box)
                    textBoxes.Add(box);

                int count = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < count; ++i)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(current, i);
                    if (child is FrameworkElement element)
                        tree.Push(element);
                }
            }

            textBoxes.Reverse();
            return textBoxes;
        }
    }
}