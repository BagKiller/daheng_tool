using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
namespace VegaBeamTool.CameraUI
{
    public partial class ProfileLinesSeriesViewModel : ObservableObject
    {
        public ProfileLinesSeriesViewModel(string strTitle)
        {
            ProfileLinesSeriesModel = new PlotModel
            {
                Title = strTitle,
            };
            InitLinesSeries(strTitle);
        }

        [ObservableProperty]
        private PlotModel _profileLinesSeriesModel;



        public LineSeries? OriginalSeries;
        public LineSeries? ShapingSeries;

        private void InitLinesSeries(string strTitle)
        {
            LinearAxis xAxis = new LinearAxis()
            {
                Position = AxisPosition.Bottom,
                IsAxisVisible = true,
                //Title = "X轴",//显示标题内容
                //TitlePosition = 1,//显示标题位置
                //TitleColor = OxyColor.Parse("#d3d3d3"),//显示标题位置
                IsZoomEnabled = false,//坐标轴缩放关闭
                IsPanEnabled = false,//图表缩放功能关闭
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
            };
            //定义y轴
            LinearAxis yAxis = new LinearAxis()
            {
                Position = AxisPosition.Left,
                IsAxisVisible = true,
                //Minimum = 0,
                //Maximum = 100,
                //Title = "Y轴",//显示标题内容
                //TitlePosition = 1,//显示标题位置
                //TitleColor = OxyColor.Parse("#d3d3d3"),//显示标题位置
                IsZoomEnabled = false,//坐标轴缩放关闭
                IsPanEnabled = false,//图表缩放功能关闭
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
            };
            ProfileLinesSeriesModel.Axes.Add(xAxis);
            ProfileLinesSeriesModel.Axes.Add(yAxis);
            OriginalSeries = new LineSeries()
            {
                Color = OxyColors.Green,
                StrokeThickness = 1,
                //MarkerSize = 3,
                //MarkerStroke = OxyColors.DarkGreen,
                //MarkerType = MarkerType.Diamond,
                //Title = strTitle
            };

            ShapingSeries = new LineSeries()
            {
                Color = OxyColors.Red,
                StrokeThickness = 1,
            };

            ProfileLinesSeriesModel.Series.Add(OriginalSeries);
            ProfileLinesSeriesModel.Series.Add(ShapingSeries);
        }
    }
}
