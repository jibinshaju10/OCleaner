using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using WpfApp1.Services;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly FileScanner _scanner = new FileScanner();
        private CancellationTokenSource? _cts;
        private List<FoundFile> _found = new List<FoundFile>();
        private double _progressValue;

        // display items and view for grouping/selection
        private ListCollectionView? _itemsView;
        private List<DisplayItem> _displayItems = new List<DisplayItem>();

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            ScanButton.Click += ScanButton_Click;
            CleanButton.Click += CleanButton_Click;
            CancelButton.Click += CancelButton_Click;
            DataContext = this;
        }

        private class DisplayItem : INotifyPropertyChanged
        {
            public FoundFile Source { get; }
            public string Path => Source.Path;
            public string Size { get; }
            public DateTime LastWrite => Source.LastWrite;
            public string Category => Source.Category;

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public DisplayItem(FoundFile source, string sizeText)
            {
                Source = source;
                Size = sizeText;
                _isSelected = false;
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                if (Math.Abs(_progressValue - value) < 0.0001) return;
                _progressValue = Math.Max(0.0, Math.Min(1.0, value));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressValue)));
                ProgressPercent.Text = _progressValue.ToString("P0");
            }
        }

        private async void ScanButton_Click(object? sender, RoutedEventArgs e)
        {
            ScanButton.IsEnabled = false;
            CleanButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusText.Text = "Scanning...";
            ProgressValue = 0;
            FilesListView.ItemsSource = null;
            FoundCount.Text = "0";
            FoundSize.Text = "0 B";

            _cts = new CancellationTokenSource();

            try
            {
                var results = await _scanner.ScanAsync(_cts.Token);
                _found = results;

                _displayItems = _found.Select(f => new DisplayItem(f, FormatSize(f.Size))).ToList();

                _itemsView = new ListCollectionView(_displayItems);
                _itemsView.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

                FilesListView.ItemsSource = _itemsView;

                FoundCount.Text = _found.Count.ToString();
                FoundSize.Text = FormatSize(_found.Sum(x => x.Size));
                StatusText.Text = $"Scan complete ({_found.Count} items)";
                CleanButton.IsEnabled = _found.Any();
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Scan canceled";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Scan failed";
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ScanButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                _cts = null;
                ProgressValue = 1.0;
            }
        }

        private async void CleanButton_Click(object? sender, RoutedEventArgs e)
        {
            // collect selected items
            var selected = _displayItems.Where(d => d.IsSelected).Select(d => d.Source).ToList();

            if (!selected.Any())
            {
                MessageBox.Show("No items selected to delete.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"Delete {selected.Count} files and free {FormatSize(selected.Sum(x => x.Size))}?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            ScanButton.IsEnabled = false;
            CleanButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusText.Text = "Cleaning...";
            ProgressValue = 0;

            _cts = new CancellationTokenSource();
            var progress = new Progress<double>(p => { Dispatcher.Invoke(() => ProgressValue = p); });

            try
            {
                var freed = await _scanner.DeleteFilesAsync(selected, progress, _cts.Token);
                StatusText.Text = $"Clean complete, freed {FormatSize(freed)}";

                // remove deleted items from lists
                var deletedPaths = selected.Select(s => s.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _found.RemoveAll(f => deletedPaths.Contains(f.Path));
                _displayItems.RemoveAll(d => deletedPaths.Contains(d.Path));

                if (_itemsView != null)
                    _itemsView.Refresh();

                FilesListView.ItemsSource = _itemsView;

                FoundCount.Text = _found.Count.ToString();
                FoundSize.Text = FormatSize(_found.Sum(x => x.Size));
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Clean canceled";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Clean failed";
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ScanButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                _cts = null;
                ProgressValue = 1.0;
                CleanButton.IsEnabled = _found.Any();
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            CancelButton.IsEnabled = false;
            _cts?.Cancel();
        }

        // Group header checkbox handlers
        private void GroupCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is CollectionViewGroup group)
                SetGroupSelection(group, true);
        }

        private void GroupCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is CollectionViewGroup group)
                SetGroupSelection(group, false);
        }

        private void SetGroupSelection(CollectionViewGroup group, bool isSelected)
        {
            foreach (var item in group.Items)
            {
                if (item is DisplayItem di)
                    di.IsSelected = isSelected;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.##") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.##") + " MB";
            double gb = mb / 1024.0;
            return gb.ToString("0.##") + " GB";
        }
    }
}