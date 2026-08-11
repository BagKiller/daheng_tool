using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace VegaBeamTool.CameraUI
{
    /// <summary>
    /// CoordinateRecordView.xaml 的交互逻辑
    /// </summary>
    public partial class CoordinateRecordView : UserControl
    {
        public CoordinateRecordView()
        {
            InitializeComponent();
        }

        public delegate void DelegateRecordDataGrid<T>(T sNo);
        public event DelegateRecordDataGrid<int> RecordDataGridEvent;

        public void ItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs listBoxItemschangeEvent)
        {
            if (null == listBoxItemschangeEvent)
            {
                return;
            }

            if (listBoxItemschangeEvent.Action == NotifyCollectionChangedAction.Add)
            {
                RecordDataScrollView.ScrollToEnd();
            }
        }



        private void DataGrid_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.RightButton == System.Windows.Input.MouseButtonState.Pressed
                && sender is DataGrid dataGrid
                && dataGrid is not null
                && dataGrid.SelectedItem is not null)
            {
                dataGrid.ContextMenu.DataContext = dataGrid.SelectedItem;
                dataGrid.ContextMenu.IsOpen = true;
            }
        }

        private void DataGrid_DeleteItmeClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem
                && menuItem is not null
                && menuItem.DataContext is CoordinateRecordItemInfo itemInfo
                && DataContext is CoordinateRecordViewModel coordinateRecordViewModel)
            {
                var items = RecordDataGrid.ItemsSource as System.Collections.IList;
                if (items is not null)
                {
                    items.Remove(menuItem.DataContext);
                    RecordDataGridEvent?.Invoke(itemInfo.SNoIndex);
                }
            }
        }
    }
}
