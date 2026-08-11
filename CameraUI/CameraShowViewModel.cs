using BeamProcessor;
using Client.Util;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OxyPlot;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VegaBeamTool.Camera;
namespace VegaBeamTool.CameraUI
{
    public partial class CameraShowViewModel : ObservableObject
    {
        public CameraShowViewModel()
        {
            _camera = new Mercury3Camera();
            _camera?.RegisterCallbackBitmap(ImageProcessorFunc);
            _curDispatcher = Dispatcher.CurrentDispatcher;
            CameraImage = null;
            ImageWidth = 2484;
            ImageHeight = 2484;
            CameraStartStatus = true;
            CameraStopStatus = false;
            InitLoadConfig();

            RecordTable = new CoordinateRecordView() { DataContext = new CoordinateRecordViewModel() };

            if (RecordTable.DataContext is CoordinateRecordViewModel coordinateRecordViewModel)
            {
                coordinateRecordViewModel.RecordDataTable.CollectionChanged += RecordTable.ItemsCollectionChanged;
                coordinateRecordViewModel.RegisterDealRecord(DealRecordItem);
                RecordTable.RecordDataGridEvent += coordinateRecordViewModel.RecordDataGridCallback;

            }
            LineSeriesElement = [];
            LineSeriesElement.Add(new ProfileLinesSeriesView() { DataContext = new ProfileLinesSeriesViewModel("Tangential Beam Profile") });
            LineSeriesElement.Add(new ProfileLinesSeriesView() { DataContext = new ProfileLinesSeriesViewModel("Radial Beam Profile") });
            LineSeriesElement.Add(new ProfileLinesSeriesView() { DataContext = new ProfileLinesSeriesViewModel("Long Beam Profile") });
            LineSeriesElement.Add(new ProfileLinesSeriesView() { DataContext = new ProfileLinesSeriesViewModel("Short Beam Profile") });
            TestText = "开始连续存图";
            /*LineSeriesElement.Add(new GraphImageView() { DataContext = new GraphImageViewModel() });
            LineSeriesElement.Add(new GraphImageView() { DataContext = new GraphImageViewModel() });*/
        }

        public void RegisterUpdateImage(CallbackUpdateImageBitmap callbackUpdate) => CallBackUpdateImage += callbackUpdate;
        public void UnRegisterUpdateImage(CallbackUpdateImageBitmap callbackUpdate) => CallBackUpdateImage -= callbackUpdate;

        public void DealRecordItem(int sNo)
        {
            try
            {
                lock (_lockRecord)
                {
                    bool reset = false;
                    for (int curIndex = 0; curIndex < _listRecordCenter.Count; curIndex++)
                    {
                        if (_listRecordCenter[curIndex].SNoIndex == sNo)
                        {
                            _listRecordCenter.Remove(_listRecordCenter[curIndex]);
                            reset = true;
                            curIndex--;
                            continue;
                        }

                        if (reset)
                        {
                            _listRecordCenter[curIndex].SNoIndex = curIndex + 1;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }

        }

        private void InitLoadConfig()
        {
            if (File.Exists(_configName))
            {
                System.Xml.Serialization.XmlSerializer ser = new(typeof(string[]));
                System.IO.FileStream fs = new(_configName, System.IO.FileMode.Open);
                string[]? Type = new string[4];
                Type = ser.Deserialize(fs) as string[];
                fs.Close();
                if (null != Type && Type.Length >= 6)
                {
                    SNValue = Type[0];
                    SelectPathValue = Type[1];
                    ExposureTimeValue = Convert.ToInt32(Type[2]);
                    GlobalGainValue = Convert.ToInt32(Type[3]);
                    CoefficientValue = Convert.ToDouble(Type[4]);
                    AverageTimesValue = Convert.ToInt32(Type[5]);
                }
                else
                {
                    SNValue = string.Empty;
                    SelectPathValue = string.Empty;
                    ExposureTimeValue = 0;
                    GlobalGainValue = 0;
                    CoefficientValue = 1;
                    AverageTimesValue = 1;
                }
            }
            else
            {
                SNValue = string.Empty;
                SelectPathValue = string.Empty;
                ExposureTimeValue = 0;
                GlobalGainValue = 0;
                CoefficientValue = 1;
                AverageTimesValue = 1;
            }
        }

        /*private List<GraphDataItem> _tangentialBeamGraphDataItems = new List<GraphDataItem>();
        private List<GraphDataItem> _radialBeamGraphDataItems = new List<GraphDataItem>();*/


        public void CallBackImage(Bitmap bitmap, CALLBACK_INFO callbackInfo)
        {
            _curDispatcher.BeginInvoke(() =>
            {
                try
                {

                    if (_listRealTimeDatas.Count < AverageTimesValue)
                    {
                        _listRealTimeDatas.Add((callbackInfo.Center.X, callbackInfo.Center.Y,
                                                callbackInfo.PointRadius.X, callbackInfo.PointRadius.Y,
                                                callbackInfo.AxisSize.X, callbackInfo.AxisSize.Y,
                                                callbackInfo.TiltAngle));
                    }

                    if (_listRealTimeDatas.Count >= AverageTimesValue)
                    {
                        var bitMapSource = BitmapToBitmapImage(bitmap);//ConvertBitmapToBitmapSource(bitmap); //
                        CameraImage = bitMapSource;
                        CallBackUpdateImage?.Invoke(callbackInfo.BeamCountor);

                        double centerX = 0.0;
                        double centerY = 0.0;
                        double pointRadiusX = 0.0;
                        double pointRadiusY = 0.0;
                        double axisSizeX = 0;
                        double axisSizeY = 0;
                        double tiltAngle = 0;
                        foreach (var item in _listRealTimeDatas)
                        {
                            centerX += item.CenterX;
                            centerY += item.CenterY;
                            pointRadiusX += item.RadiusX;
                            pointRadiusY += item.RadiusY;
                            axisSizeX += item.LongAxis;
                            axisSizeY += item.ShortAxis;
                            tiltAngle += item.TiltAngle;
                        }

                        BeamXValue = centerX / AverageTimesValue;
                        BeamYValue = centerY / AverageTimesValue;
                        RadiusXValue = pointRadiusX / AverageTimesValue;
                        RadiusYValue = pointRadiusY / AverageTimesValue;
                        LongAxisValue = axisSizeX / AverageTimesValue;
                        ShortAxisValue = axisSizeY / AverageTimesValue;
                        TiltAngleValue = tiltAngle / AverageTimesValue;
                        _listRealTimeDatas.RemoveAt(0);

                        if (LineSeriesElement.Count >= 4)
                        {
                            if (LineSeriesElement[0].DataContext is ProfileLinesSeriesViewModel tangentialBeam
                                && callbackInfo.XDataOriginal is not null
                                && callbackInfo.XDataShaping is not null)
                            {
                                tangentialBeam.OriginalSeries?.Points.Clear();
                                tangentialBeam.ShapingSeries?.Points.Clear();
                                tangentialBeam.OriginalSeries?.Points.AddRange(callbackInfo.XDataOriginal);
                                tangentialBeam.ShapingSeries?.Points.AddRange(callbackInfo.XDataShaping);
                                tangentialBeam.ProfileLinesSeriesModel.InvalidatePlot(true);
                            }

                            if (LineSeriesElement[1].DataContext is ProfileLinesSeriesViewModel radialBeam
                                && callbackInfo.YDataOriginal is not null
                                && callbackInfo.YDataShaping is not null)
                            {
                                radialBeam.OriginalSeries?.Points.Clear();
                                radialBeam.ShapingSeries?.Points.Clear();
                                radialBeam.OriginalSeries?.Points.AddRange(callbackInfo.YDataOriginal);
                                radialBeam.ShapingSeries?.Points.AddRange(callbackInfo.YDataShaping);
                                radialBeam.ProfileLinesSeriesModel.InvalidatePlot(true);
                            }

                            if (LineSeriesElement[2].DataContext is ProfileLinesSeriesViewModel longBeam
                                && callbackInfo.LongAxisOriginal is not null
                                && callbackInfo.LongAxisShaping is not null)
                            {
                                longBeam.OriginalSeries?.Points.Clear();
                                longBeam.ShapingSeries?.Points.Clear();
                                longBeam.OriginalSeries?.Points?.AddRange(callbackInfo.LongAxisOriginal);
                                longBeam.ShapingSeries?.Points?.AddRange(callbackInfo.LongAxisShaping);
                                longBeam.ProfileLinesSeriesModel.InvalidatePlot(true);
                            }

                            if (LineSeriesElement[3].DataContext is ProfileLinesSeriesViewModel shortBeam
                                && callbackInfo.ShortAxisOriginal is not null
                                && callbackInfo.ShortAxisShaping is not null)
                            {
                                shortBeam.OriginalSeries?.Points.Clear();
                                shortBeam.ShapingSeries?.Points.Clear();
                                shortBeam.OriginalSeries?.Points?.AddRange(callbackInfo.ShortAxisOriginal);
                                shortBeam.ShapingSeries?.Points?.AddRange(callbackInfo.ShortAxisShaping);
                                shortBeam.ProfileLinesSeriesModel.InvalidatePlot(true);
                            }

                        }
                    }
                    bitmap.Dispose();
                }
                catch (Exception ex)
                {
                    testLogger.Error(ex.Message, ex);
                }
            });
        }


        private bool ImageProcessorFunc(byte[] originalByteImage, bool bSaveImage)
        {
            if (originalByteImage is null)
            {
                return false;
            }

            try
            {

                // Stopwatch stopwatch = Stopwatch.StartNew();
                // Bitmap bitmap = Convert16BitGrayToBitmap(originalByteImage, ImageWidth, ImageHeight);
                Mat mat = Convert16BitGrayToMat(originalByteImage, ImageWidth, ImageHeight);
                Mat image8bit = Convert16BitGrayTo8BitMat(originalByteImage, ImageWidth, ImageHeight, true);
                Mat resultImage;

                if (bSaveImage
                    && !string.IsNullOrEmpty(SelectPathValue)
                    && Directory.Exists(SelectPathValue))
                {
                    string strImageName = $"{DateTime.Now.Year}_{DateTime.Now.Month}_{DateTime.Now.Day}_{DateTime.Now.Hour}_{DateTime.Now.Minute}_{DateTime.Now.Second}";
                    string str = $"{SelectPathValue}{strImageName}.tif";
                    Save16BitByte(originalByteImage, str);
                }

                BeamParameters? beamResult;
                List<DataPoint>? xRadiusOriginal = null;
                List<DataPoint>? xRadiusShaping = null;
                List<DataPoint>? yRadiusOriginal = null;
                List<DataPoint>? yRadiusShaping = null;

                List<DataPoint>? longAxisOriginal = null;
                List<DataPoint>? longAxisShaping = null;
                List<DataPoint>? shortAxisOriginal = null;
                List<DataPoint>? shortAxisShaping = null;
                List<System.Windows.Point>? beamCountor = null;
                try
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    beamResult = BeamProcessor.BeamProcessor.GetBeamSpotParameters(mat, image8bit, _listRecordCenter, out resultImage);
                    stopwatch.Stop();
                    if (stopwatch.ElapsedMilliseconds > 3000)
                    {
                        testLogger.Info($"deal mat times:{stopwatch.ElapsedMilliseconds}");
                    }
                }
                catch (Exception ex)
                {
                    if (_exceptionSaveImage
                        && !string.IsNullOrEmpty(SelectPathValue)
                        && Directory.Exists(SelectPathValue))
                    {
                        string str = $"{SelectPathValue}ExceptionImage\\{DateTime.Now.Year}_{DateTime.Now.Month}_{DateTime.Now.Day}_{DateTime.Now.Hour}_{DateTime.Now.Minute}_{DateTime.Now.Second}_exception.tif";
                        Save16BitByte(originalByteImage, str);
                        testLogger.Error(ex.Message, ex);
                    }
                    return false;
                }

                Point2d center = new Point2d(0, 0);
                Point2d pointRadius = new Point2d(0, 0);
                Point2d axisSize = new Point2d(0, 0);
                double tiltAngle = 0.0;
                List<Task> listTask = [];
                if (beamResult is not null
                    && beamResult.LineA is not null
                    && beamResult.LineB is not null
                    && beamResult.LineA.ProfilePoints is not null
                    && beamResult.LineA.DstCurvePoints is not null
                    && beamResult.LineB.ProfilePoints is not null
                    && beamResult.LineB.DstCurvePoints is not null)
                {

                    listTask.Add(Task.Run(() =>
                    {
                        ConvertDataType(beamResult.LineA.ProfilePoints,
                                        beamResult.LineA.DstCurvePoints,
                                        beamResult.LineB.ProfilePoints,
                                        beamResult.LineB.DstCurvePoints,
                                        out xRadiusOriginal, out xRadiusShaping,
                                        out yRadiusOriginal, out yRadiusShaping);
                    }));
                }
                if (beamResult is not null
                    && beamResult.LineV is not null
                    && beamResult.LineH is not null
                    && beamResult.LineV.ProfilePoints is not null
                    && beamResult.LineV.DstCurvePoints is not null
                    && beamResult.LineH.ProfilePoints is not null
                    && beamResult.LineH.DstCurvePoints is not null)
                {

                    listTask.Add(Task.Run(() =>
                    {
                        axisSize.X = beamResult.LineV.Radius * CoefficientValue;
                        axisSize.Y = beamResult.LineH.Radius * CoefficientValue;
                        ConvertDataType(beamResult.LineV.ProfilePoints,
                                        beamResult.LineV.DstCurvePoints,
                                        beamResult.LineH.ProfilePoints,
                                        beamResult.LineH.DstCurvePoints,
                                        out longAxisOriginal, out longAxisShaping,
                                        out shortAxisOriginal, out shortAxisShaping);
                    }));
                }

                if (beamResult is not null && beamResult.BeamCountor is not null)
                {
                    listTask.Add(Task.Run(() =>
                    {
                        beamCountor = new(beamResult.BeamCountor.Length);
                        for (int i = 0; i < beamResult.BeamCountor.Length; i++)
                        {
                            beamCountor.Add(new(beamResult.BeamCountor[i].X, beamResult.BeamCountor[i].Y));
                        }
                    }));
                }

                if (beamResult is not null)
                {
                    center.X = beamResult.CenterX * CoefficientValue;
                    center.Y = beamResult.CenterY * CoefficientValue;
                    pointRadius.X = beamResult.WidthMajor / 2 * CoefficientValue;
                    pointRadius.Y = beamResult.WidthMinor / 2 * CoefficientValue;
                    tiltAngle = beamResult.RotatedRect.Angle * CoefficientValue;
                }
                Task.WaitAll([.. listTask]);

                CALLBACK_INFO info = new()
                {
                    XDataOriginal = xRadiusOriginal,
                    XDataShaping = xRadiusShaping,
                    YDataOriginal = yRadiusOriginal,
                    YDataShaping = yRadiusShaping,
                    LongAxisOriginal = longAxisOriginal,
                    LongAxisShaping = longAxisShaping,
                    ShortAxisOriginal = shortAxisOriginal,
                    ShortAxisShaping = shortAxisShaping,
                    BeamCountor = beamCountor,
                    Center = center,
                    PointRadius = pointRadius,
                    AxisSize = axisSize,
                    TiltAngle = tiltAngle,

                };
                Bitmap bitmap = resultImage.ToBitmap();

                CallBackImage(bitmap, info);
                // Store raw data for pixel value lookup
                lock (_lockRawData)
                {
                    _latestRawImageData = new byte[originalByteImage.Length];
                    Array.Copy(originalByteImage, _latestRawImageData, originalByteImage.Length);
                }

                mat.Dispose();
                image8bit.Dispose();
                resultImage.Dispose();

                //stopwatch.Stop();
                //testLogger.Info($"deal bitmap times:{stopwatch.ElapsedMilliseconds}");
                return true;
            }
            catch (Exception ex)
            {
                testLogger.Debug(ex.Message, ex);
                return false;
            }
        }


        private BitmapImage BitmapToBitmapImage(Bitmap bitmap)
        {
            BitmapImage bitmapImage = new BitmapImage();
            using (MemoryStream memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                memoryStream.Seek(0, SeekOrigin.Begin);
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
            }
            return bitmapImage;
        }

        private void ConvertDataType(Point2d[] xOriginal, Point2d[] xShaping, Point2d[] yOriginal,
        Point2d[] yShaping, out List<DataPoint> xDataOriginal, out List<DataPoint> xDataShaping,
        out List<DataPoint> yDataOriginal, out List<DataPoint> yDataShaping)
        {
            xDataOriginal = new(xOriginal.Length);
            xDataShaping = new(xShaping.Length);
            yDataOriginal = new(yOriginal.Length);
            yDataShaping = new(yShaping.Length);

            int nXSize = Math.Min(xOriginal.Length, xShaping.Length);
            int nYSize = Math.Min(yOriginal.Length, yShaping.Length);
            for (int i = 0; i < nXSize; i++)
            {
                xDataOriginal.Add(new DataPoint(xOriginal[i].X, xOriginal[i].Y));
                xDataShaping.Add(new DataPoint(xShaping[i].X, xShaping[i].Y));
            }

            for (int j = 0; j < nYSize; j++)
            {
                yDataOriginal.Add(new DataPoint(yOriginal[j].X, yOriginal[j].Y));
                yDataShaping.Add(new DataPoint(yShaping[j].X, yShaping[j].Y));
            }
        }

        private void ChangeButtonStatus()
        {
            if (_camera.GetCameraStartSatuts())
            {
                CameraStartStatus = false;
                CameraStopStatus = true;

            }
            else
            {
                CameraStartStatus = true;
                CameraStopStatus = false;
            }

        }

        public ushort? GetRawPixelValue(int x, int y)
        {
            lock (_lockRawData)
            {
                if (_latestRawImageData == null) return null;
                if (x < 0 || x >= ImageWidth || y < 0 || y >= ImageHeight) return null;
                int index = (y * ImageWidth + x) * 2;
                if (index + 1 >= _latestRawImageData.Length) return null;
                return ReadUInt16(_latestRawImageData, index, true);
            }
        }


        private static ushort ReadUInt16(byte[] data, int index, bool isLittleEndian)
        {
            if (index + 1 >= data.Length)
                throw new IndexOutOfRangeException();

            if (isLittleEndian)
            {
                return (ushort)(data[index] | (data[index + 1] << 8));
            }
            else
            {
                return (ushort)((data[index] << 8) | data[index + 1]);
            }
        }

        public static Mat Convert16BitGrayTo8BitMat(byte[] sixteenBitData, int width, int height, bool isLittleEndian = true)
        {
            if (sixteenBitData == null || sixteenBitData.Length == 0)
                throw new ArgumentException("输入数据不能为空");

            int totalPixels = width * height;
            if (sixteenBitData.Length != totalPixels * 2)
                throw new ArgumentException($"数据长度不匹配。期望: {totalPixels * 2}，实际: {sixteenBitData.Length}");

            Mat eightBitMat = new Mat(height, width, MatType.CV_8UC1);

            ushort min = ushort.MaxValue;
            ushort max = ushort.MinValue;

            for (int i = 0; i < totalPixels; i++)
            {
                int byteIndex = i * 2;
                ushort value = ReadUInt16(sixteenBitData, byteIndex, isLittleEndian);

                if (value < min) min = value;
                if (value > max) max = value;
            }
            if (max == min)
            {
                eightBitMat.SetTo(128);
                return eightBitMat;
            }

            double scale = 255.0 / (max - min);

            unsafe
            {
                byte* targetPtr = (byte*)eightBitMat.DataPointer;
                int stride = (int)eightBitMat.Step();

                for (int y = 0; y < height; y++)
                {
                    int targetRowOffset = y * stride;
                    int sourceByteOffset = y * width * 2;

                    for (int x = 0; x < width; x++)
                    {
                        int sourceIndex = sourceByteOffset + x * 2;
                        ushort value = ReadUInt16(sixteenBitData, sourceIndex, isLittleEndian);

                        double scaledValue = (value - min) * scale;
                        targetPtr[targetRowOffset + x] = (byte)Math.Max(0, Math.Min(255, scaledValue));
                    }
                }
            }

            return eightBitMat;
        }
        public Mat Convert16BitGrayToMat(byte[] byteStream, int width, int height)
        {
            int totalPixels = width * height;

            if (byteStream.Length < totalPixels * 2)
            {
                throw new ArgumentException("字节流长度与图像尺寸不匹配。");
            }

            Mat mat = Mat.FromPixelData(height, width, MatType.CV_16UC1, byteStream);
            return mat;
        }

        public Bitmap Convert16BitGrayToBitmap(byte[] byteStream, int width, int height)
        {
            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format16bppGrayScale);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

            try
            {
                Marshal.Copy(byteStream, 0, bitmapData.Scan0, bitmapData.Stride * height);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
            return bitmap;
        }

        public BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            Rectangle rect = new Rectangle(0, 0, width, height);
            BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

            try
            {

                BitmapSource bitmapSource = BitmapSource.Create(
                        width,
                        height,
                        bitmap.HorizontalResolution,
                        bitmap.VerticalResolution,
                        PixelFormats.Gray16,
                        null,
                        bmpData.Scan0,
                        bmpData.Stride * height,
                        bmpData.Stride
                    );

                return bitmapSource;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        public void Save16BitByte(byte[] originalByteImage, string filePath)
        {
            if (originalByteImage.Length < ImageWidth * 2 * ImageHeight)
            {
                return;
            }

            try
            {
                BitmapSource bitmapSource = BitmapSource.Create(
                    ImageWidth,
                    ImageHeight,
                    144,
                    144,
                    PixelFormats.Gray16,
                    null,
                    originalByteImage,
                    ImageWidth * 2
                );

                TiffBitmapEncoder encoder = new TiffBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

                using (FileStream stream = new FileStream(filePath, FileMode.Create))
                {
                    encoder.Save(stream);
                }
            }
            catch
            {

            }
        }

        #region

        private List<(double, double)> _listCentreMassXY = [];
        private int _interLockCapture = 0;
        private int _interLockSnapshot = 0;



        private readonly Mercury3Camera _camera;
        private Dispatcher _curDispatcher;
        private const string _configName = "ToolConfig";

        private int _exposureTimeMin = 0;
        private int _exposureTimeMax = 10000;
        private int _gainMin = 0;
        private int _gainMax = 16;

        private bool _exceptionSaveImage = false;

        private readonly object _lockRecord = new();
        private byte[] _latestRawImageData;
        private readonly object _lockRawData = new();
        private List<RecordItems> _listRecordCenter = [];
        private readonly List<(double CenterX, double CenterY,
                                double RadiusX, double RadiusY,
                                double LongAxis, double ShortAxis,
                                double TiltAngle)> _listRealTimeDatas = [];
        public delegate void CallbackUpdateImageBitmap(List<System.Windows.Point>? beamCountor);
        public event CallbackUpdateImageBitmap? CallBackUpdateImage;
        #endregion




        #region
        [ObservableProperty]
        private double _beamXValue;

        [ObservableProperty]
        private double _beamYValue;

        [ObservableProperty]
        private double _radiusXValue;

        [ObservableProperty]
        private double _radiusYValue;

        [ObservableProperty]
        private double _longAxisValue;

        [ObservableProperty]
        private double _shortAxisValue;

        [ObservableProperty]
        private double _tiltAngleValue;

        [ObservableProperty]
        private string _sNValue;

        [ObservableProperty]
        private int _exposureTimeValue;

        [ObservableProperty]
        private int _globalGainValue;

        [ObservableProperty]
        private string _selectPathValue;

        [ObservableProperty]
        private ImageSource _cameraImage;

        [ObservableProperty]
        private int _imageHeight;

        [ObservableProperty]
        private int _imageWidth;

        [ObservableProperty]
        private ObservableCollection<ProfileLinesSeriesView> _lineSeriesElement;

        [ObservableProperty]
        private bool _cameraStartStatus;

        [ObservableProperty]
        private bool _cameraStopStatus;

        [ObservableProperty]
        private CoordinateRecordView _recordTable;

        [ObservableProperty]
        private double _coefficientValue;

        [ObservableProperty]
        private int _averageTimesValue;

        [ObservableProperty]
        private string _testText;
        private bool _save = false;
        [RelayCommand]
        public void TestImage()
        {
            if (Interlocked.Exchange(ref _interLockSnapshot, 1) == 0)
            {
                if (_save)
                {
                    _save = false;
                    TestText = "开启连续存图";
                }
                else
                {
                    _save = true;
                    TestText = "关闭连续存图";
                }

                Task.Run(() =>
                {
                    try
                    {

                        Bitmap bitmap = new Bitmap("C:\\Users\\17584\\Desktop\\CCD\\CCD\\SHAPE\\Pic_20251212132352064.tiff");
                        Bitmap bitmap1 = new Bitmap("C:\\Users\\17584\\Desktop\\CCD\\CCD\\SHAPE\\Pic_20251212132352064.tiff");
                        Bitmap bitmap2 = new Bitmap("C:\\Users\\17584\\Desktop\\CCD\\CCD\\SHAPE\\Pic_20251212132401935.tiff");
                        Bitmap bitmap3 = new Bitmap("C:\\Users\\17584\\Desktop\\CCD\\CCD\\SHAPE\\Pic_20251212132352064.tiff");
                        Bitmap bitmap4 = new Bitmap("C:\\Users\\17584\\Desktop\\CCD\\CCD\\SHAPE\\Pic_20251212132356100.tiff");
                        Bitmap bitmap5 = new Bitmap("C:\\Users\\17584\\Desktop\\CCD\\CCD\\SHAPE\\Pic_20251212132401935.tiff");
                        ImageWidth = bitmap.Width;
                        ImageHeight = bitmap.Height;
                        byte[] ddd = Convert8BitTo12Bit(bitmap);
                        byte[] ddd1 = Convert8BitTo12Bit(bitmap1);
                        byte[] ddd2 = Convert8BitTo12Bit(bitmap2);
                        byte[] ddd3 = Convert8BitTo12Bit(bitmap3);
                        byte[] ddd4 = Convert8BitTo12Bit(bitmap4);
                        byte[] ddd5 = Convert8BitTo12Bit(bitmap5);
                        // 
                        ImageProcessorFunc(ddd, false);
                        Thread.Sleep(50);
                        while (true)
                        {
                            ImageProcessorFunc(ddd, false);
                            Thread.Sleep(50);
                            ImageProcessorFunc(ddd1, false);
                            Thread.Sleep(50);
                            ImageProcessorFunc(ddd2, false);
                            Thread.Sleep(50);
                            ImageProcessorFunc(ddd3, false);
                            Thread.Sleep(50);
                            ImageProcessorFunc(ddd4, false);
                            Thread.Sleep(50);
                            ImageProcessorFunc(ddd5, false);
                            Thread.Sleep(50);

                        }

                    }
                    finally
                    {
                        Interlocked.Exchange(ref _interLockSnapshot, 0);
                    }

                });
            }
        }

        public static byte[] Convert8BitTo12Bit(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            int width = bitmap.Width;
            int height = bitmap.Height;
            int pixelCount = width * height;
            byte[] result = new byte[pixelCount * 2]; // 每个像素 2 字节

            BitmapData bmpData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format8bppIndexed);

            try
            {
                int stride = bmpData.Stride;
                IntPtr scan0 = bmpData.Scan0;

                byte[] pixels = new byte[stride * height];
                Marshal.Copy(scan0, pixels, 0, pixels.Length);

                int index = 0;
                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        byte pixel8 = pixels[rowOffset + x];
                        ushort pixel12 = (ushort)(pixel8 << 4);

                        result[index++] = (byte)(pixel12 & 0xFF);
                        result[index++] = (byte)((pixel12 >> 8) & 0x0F);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }

            return result;
        }





        [RelayCommand]
        public void CoordinateRecord()
        {
            try
            {
                Task.Run(() =>
                {

                    _curDispatcher.BeginInvoke(() =>
                    {
                        if (RecordTable.DataContext is CoordinateRecordViewModel recordViewModel)
                        {
                            var colorRandom = new Random();
                            var center = new RecordItems
                            {
                                SNoIndex = recordViewModel.RecordDataTable.Count + 1,
                                CenterX = Math.Round(BeamXValue, 2),
                                CenterY = Math.Round(BeamYValue, 2),
                                ColorRed = colorRandom.Next(0, 100),
                                ColorGreen = colorRandom.Next(0, 256),
                                ColorBlue = colorRandom.Next(0, 256),

                            };
                            RecordItems items = new RecordItems
                            {
                                SNoIndex = center.SNoIndex,
                                CenterX = center.CenterX / CoefficientValue,
                                CenterY = center.CenterY / CoefficientValue,
                                ColorRed = center.ColorRed,
                                ColorGreen = center.ColorGreen,
                                ColorBlue = center.ColorBlue,
                            };
                            lock (_lockRecord)
                            {
                                _listRecordCenter.Add(items);
                            }
                            recordViewModel.AddReordItem(center);
                            if (0 == _interLockCapture)
                            {
                                Task.Run(() => ImageProcessorFunc(_latestRawImageData, false));
                            }
                        }
                    });

                });
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }


        }


        [RelayCommand]
        public void StartCamera()
        {
            try
            {
                if (_camera is null
                || _camera.GetCameraStartSatuts())
                {
                    return;
                }

                if (string.IsNullOrEmpty(SNValue))
                {
                    MessageBox.Show("SN is empty", "Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _camera.SetCameraSN(SNValue);
                _camera.Start();
                if (_camera.GetCameraStartSatuts())
                {
                    _exposureTimeMin = _camera.GetCameraExposureTimeMin();
                    _exposureTimeMax = _camera.GetCameraExposureTimeMax();

                    _gainMax = _camera.GetCameraGainMax();
                    _gainMin = _camera.GetCameraGainMin();

                    _camera.SetCameraGain(GlobalGainValue);
                    _camera.SetCameraExposureTime(ExposureTimeValue);
                    _camera.GetCameraColorMode();
                    ImageWidth = _camera.ImageWidth;
                    ImageHeight = _camera.ImageHeight;
                }
                else
                {
                    MessageBox.Show("Start camera failed", "Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ChangeButtonStatus();
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }
        }

        [RelayCommand]
        public void StopCamera()
        {
            _camera.Stop();
            ChangeButtonStatus();
        }

        [RelayCommand]
        public void StartCapture()
        {
            if (Interlocked.Exchange(ref _interLockCapture, 1) == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        _camera.Capture();
                    }
                    catch (Exception ex)
                    {
                        testLogger.Error(ex.Message);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _interLockCapture, 0);
                    }
                });
            }
        }

        [RelayCommand]
        public void StartSnapshot()
        {
            if (Interlocked.Exchange(ref _interLockSnapshot, 1) == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        int counts = AverageTimesValue;
                        if (AverageTimesValue <= 0)
                        {
                            counts = 1;
                        }
                        for (int i = 0; i < counts; i++)
                        {
                            if (_camera.Snapshot(out byte[]? byteImage) && byteImage is not null)
                            {
                                ImageProcessorFunc(byteImage, false);
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        testLogger.Error(ex.Message);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _interLockSnapshot, 0);
                    }
                });
            }
        }

        [RelayCommand]
        public void SelectSaveImagePath()
        {
            try
            {
                string str;
                if (string.IsNullOrEmpty(SelectPathValue))
                {
                    str = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                }
                else
                {
                    str = SelectPathValue;
                }
                var folderDialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select the save path",
                    InitialDirectory = str
                };

                if (folderDialog.ShowDialog() == true)
                {
                    SelectPathValue = folderDialog.FolderName;
                    var strRootPath = Path.GetPathRoot(SelectPathValue);
                    if (strRootPath != SelectPathValue)
                    {
                        SelectPathValue += "\\";
                    }

                }
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }

        }

        [RelayCommand]
        public void SaveImage()
        {
            if (string.IsNullOrEmpty(SelectPathValue))
            {
                MessageBox.Show($"The path for saving the image is empty", "Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(SelectPathValue))
            {
                MessageBox.Show($"The path for saving the image is empty", "Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Task.Run(() =>
            {
                if (Interlocked.Exchange(ref _interLockSnapshot, 1) == 0)
                {
                    try
                    {
                        _camera.Snapshot(out byte[]? byteImage);
                        if (byteImage is not null)
                        {
                            ImageProcessorFunc(byteImage, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        testLogger.Error(ex.Message, ex);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _interLockSnapshot, 0);
                    }
                }
            });
        }

        [RelayCommand]
        public void SaveConfig()
        {
            try
            {
                if (string.IsNullOrEmpty(SNValue))
                {
                    MessageBox.Show($"TSNValue is empty", "Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (ExposureTimeValue < _exposureTimeMin || ExposureTimeValue > _exposureTimeMax)
                {
                    MessageBox.Show($"The range of exposureTime is ({_exposureTimeMin},{_exposureTimeMax})", "Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (GlobalGainValue < _gainMin || GlobalGainValue > _gainMax)
                {
                    MessageBox.Show($"The range of GlobalGain is ({_exposureTimeMin},{_exposureTimeMax})", "Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_camera.GetCameraStartSatuts())
                {
                    _camera.SetCameraGain(GlobalGainValue);
                    _camera.SetCameraExposureTime(ExposureTimeValue);
                }
                string[] Type =
                {
                    SNValue,
                    SelectPathValue,
                    ExposureTimeValue.ToString(),
                    GlobalGainValue.ToString(),
                    CoefficientValue.ToString(),
                    AverageTimesValue.ToString(),
                };
                System.Xml.Serialization.XmlSerializer ser = new System.Xml.Serialization.XmlSerializer(typeof(string[]));
                System.IO.FileStream fs = new(_configName, System.IO.FileMode.Create);
                ser.Serialize(fs, Type);
                fs.Close();
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }
        }
        #endregion
    }

    public class ExposureTimeValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (int.TryParse(value.ToString(), out int intValue)
                && intValue > 0)
            {
                return new ValidationResult(true, null);
            }
            return new ValidationResult(false, "");
        }
    }

    public class GlobalGainValueValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (int.TryParse(value.ToString(), out int intValue)
                && intValue > 0)
            {
                return new ValidationResult(true, null);
            }
            return new ValidationResult(false, "");
        }
    }


    public struct CALLBACK_INFO
    {
        public List<DataPoint>? XDataOriginal;
        public List<DataPoint>? XDataShaping;
        public List<DataPoint>? YDataOriginal;
        public List<DataPoint>? YDataShaping;
        public List<DataPoint>? LongAxisOriginal;
        public List<DataPoint>? LongAxisShaping;
        public List<DataPoint>? ShortAxisOriginal;
        public List<DataPoint>? ShortAxisShaping;
        public List<System.Windows.Point>? BeamCountor;
        public Point2d Center;
        public Point2d PointRadius;
        public Point2d AxisSize;
        public double TiltAngle;
    }



}


