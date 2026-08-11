using BeamProcessor;
using Client.Util;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace VegaBeamTool.CameraUI
{
    public partial class CoordinateRecordViewModel : ObservableObject
    {
        public CoordinateRecordViewModel()
        {
            RecordDataTable = [];
        }
        public void AddReordItem(RecordItems centerInfo)
        {
            RecordDataTable.Add(new()
            {
                SNoIndex = centerInfo.SNoIndex,
                CenterX = centerInfo.CenterX,
                CenterY = centerInfo.CenterY,
                CenterColor = new SolidColorBrush(Color.FromArgb(255,
                BitConverter.GetBytes(centerInfo.ColorRed)[0],
                BitConverter.GetBytes(centerInfo.ColorGreen)[0],
                BitConverter.GetBytes(centerInfo.ColorBlue)[0])),
            });
        }
        [ObservableProperty]
        private ObservableCollection<CoordinateRecordItemInfo> _recordDataTable;

        public delegate void DealRecordCallback<T>(T sNo);
        public event DealRecordCallback<int>? OnCallbackDealRecord;
        public void RegisterDealRecord(DealRecordCallback<int> dealRecordCallback) => OnCallbackDealRecord += dealRecordCallback;
        public void UnRegisterDealRecord(DealRecordCallback<int> dealRecordCallback) => OnCallbackDealRecord -= dealRecordCallback;

        public void RecordDataGridCallback(int sNo)
        {
            try
            {
                OnCallbackDealRecord?.Invoke(sNo);
                int index = 1;
                foreach (var cur in RecordDataTable)
                {
                    cur.SNoIndex = index;
                    index++;
                }
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }

        }
    }


    public partial class CoordinateRecordItemInfo : ObservableObject
    {
        [ObservableProperty]
        private int _sNoIndex;
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public Brush CenterColor { get; set; }
    }

    public class DataGridRowToIndexNumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DataGridRow row)
            {
                return row.GetIndex() + 1;
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
