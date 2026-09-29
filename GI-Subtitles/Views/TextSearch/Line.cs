using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace GI_Subtitles.Views.TextSearch
{
    public class TextRow : INotifyPropertyChanged
    {
        private int _textBoxUid;
        public int TextBoxUid
        {
            get => _textBoxUid;
            set
            {
                _textBoxUid = value;
                OnPropertyChanged();
            }
        }

        private int _lineNum;
        public int LineNum
        {
            get => _lineNum;
            set
            {
                _lineNum = value;
                OnPropertyChanged();
            }
        }

        private string _sourceText = "";
        public string SourceText
        {
            get => _sourceText;
            set
            {
                _sourceText = value;
                OnPropertyChanged();
            }
        }
        
        private string _targetText = "";
        public string TargetText
        {
            get => _targetText;
            set
            {
                _targetText = value;
                OnPropertyChanged();
            }
        }

        private double _textBoxWidth;
        public double TextBoxWidth
        {
            get => _textBoxWidth;
            set
            {
                _textBoxWidth = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            // https://stackoverflow.com/a/74785422
            var changed = PropertyChanged;
            changed?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    
    public class Line : TextBox
    {
        private double _listViewActualWidth;
        public ObservableCollection<TextRow> Rows { get; set; }

        public Line()
        {
            Rows = new ObservableCollection<TextRow>
            {
                new TextRow
                {
                    TextBoxUid = 0,
                    LineNum = 1,
                    TextBoxWidth = CalcTextBoxWidth()
                }
            };
        }
        
        public ObservableCollection<TextRow> LineOperation(string opName, int rowIndexWhenKeyDown)
        {
            // RowIndex starts at 0 and is for programming purpose.
            // LineNum starts at 1 and is for displaying purpose. (LineNum = RowIndex + 1)
            int lineNumChangeFactor;

            if (opName == "ADD")
                lineNumChangeFactor = 1;
            else if (opName == "DEL")
                lineNumChangeFactor = -1;
            else
            {
                MessageBox.Show($"UNREACHABLE\nExcept opName = \"ADD\" or \"DEL\", but got {opName}");
                return Rows;
            }

            int currLineNum;
            if (opName == "ADD")
            {
                // Insert a new line
                var newLine = new TextRow
                {
                    TextBoxUid = rowIndexWhenKeyDown,
                    LineNum = rowIndexWhenKeyDown + 1,
                    TextBoxWidth = CalcTextBoxWidth()
                };
                Rows.Insert(rowIndexWhenKeyDown + 1, newLine);
                currLineNum = rowIndexWhenKeyDown + 1;
            }
            else // opName == "DEL"
            {
                // If at the first line, refuse to act.
                if (rowIndexWhenKeyDown == 0)
                    return Rows;
                Rows.RemoveAt(rowIndexWhenKeyDown);
                currLineNum = rowIndexWhenKeyDown;
            }

            // Update LineNum and RowIndex
            // When adding a new line, both are increased by 1 (lineNumChangeFactor = 1);
            // when deleting a line, both are decreased by 1 (lineNumChangeFactor = -1).
            // All of these are applied to lines below the line added or deleted.
            // currLineNum < Rows.Count = rowIndexWhenKeyDown < Rows.Count - 1
            if (currLineNum < Rows.Count)
                for (int i = currLineNum; i < Rows.Count; i++)
                {
                    var row = Rows[i];
                    row.LineNum += lineNumChangeFactor;
                    row.TextBoxUid += lineNumChangeFactor;
                }
            
            return Rows;
        }

        public void UpdateTextBoxActualWidth(double listViewActualWidth)
        {
            _listViewActualWidth = listViewActualWidth;
            foreach (var row in Rows)
                row.TextBoxWidth = CalcTextBoxWidth();
        }
            
        public double CalcTextBoxWidth()
        {
            // 20 is the width of <TextBox> of row indexes; 55 is the <CheckBox> of "RegEx"
            return _listViewActualWidth / 2 - (20 + 55);
        }
    }
}