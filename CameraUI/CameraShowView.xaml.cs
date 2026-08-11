using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
namespace VegaBeamTool.CameraUI
{
    /// <summary>
    /// CameraShow.xaml 的交互逻辑
    /// </summary>
    public partial class CameraShowView : UserControl
    {
        public CameraShowView()
        {
            InitializeComponent();
            InitializeEvents();
        }


        private Bitmap _bitmap;
        private List<System.Windows.Point>? _contourPoints;
        public void UpdateBitmap(List<System.Windows.Point>? beamCountor)
        {
            if (beamCountor is not null)
            {
                _contourPoints = beamCountor;
            }
            DrawContour();
        }


        private Point _lastMousePosition;
        private bool _isDragging = false;
        private double _scale = 1.0;
        private const double ScaleFactor = 0.05;
        private const double MinScale = 0.1;
        private const double MaxScale = 50.0;

        private void InitializeEvents()
        {
            DisplayImage.MouseWheel += OnMouseWheel;
            DisplayImage.MouseLeftButtonDown += OnMouseLeftButtonDown;
            DisplayImage.MouseLeftButtonUp += OnMouseLeftButtonUp;
            DisplayImage.MouseMove += OnMouseMove;
            DisplayImage.MouseLeave += OnMouseLeave;
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point mousePos = e.GetPosition(this);

            TransformGroup? transformGroup = DisplayImage.RenderTransform as TransformGroup;
            if (transformGroup == null)
            {
                transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(_scale, _scale));
                transformGroup.Children.Add(new TranslateTransform(0, 0));
            }

            ScaleTransform? scaleTransform = null;
            TranslateTransform? translateTransform = null;

            foreach (Transform transform in transformGroup.Children)
            {
                if (transform is ScaleTransform scaleTf)
                {
                    scaleTransform = scaleTf;
                }
                else if (transform is TranslateTransform translateTf)
                {
                    translateTransform = translateTf;
                }
            }

            if (scaleTransform == null)
            {
                scaleTransform = new ScaleTransform(_scale, _scale);
                transformGroup.Children.Add(scaleTransform);
            }

            if (translateTransform == null)
            {
                translateTransform = new TranslateTransform(0, 0);
                transformGroup.Children.Add(translateTransform);
            }

            Point relativePoint = new Point(
                (mousePos.X - translateTransform.X) / scaleTransform.ScaleX,
                (mousePos.Y - translateTransform.Y) / scaleTransform.ScaleY
            );

            double oldScale = _scale;
            if (e.Delta > 0)
            {
                _scale += ScaleFactor;
            }
            else
            {
                _scale -= ScaleFactor;
            }
            _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale));

            scaleTransform.ScaleX = _scale;
            scaleTransform.ScaleY = _scale;

            translateTransform.X = mousePos.X - relativePoint.X * _scale;
            translateTransform.Y = mousePos.Y - relativePoint.Y * _scale;

            DisplayImage.RenderTransform = transformGroup;
            DrawContour();
            e.Handled = true;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _lastMousePosition = e.GetPosition(this);
                _isDragging = true;
                DisplayImage.CaptureMouse();
                DisplayImage.Cursor = Cursors.Hand;
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            DisplayImage.ReleaseMouseCapture();
            DisplayImage.Cursor = Cursors.Cross;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPosition = e.GetPosition(this);
                Vector delta = currentPosition - _lastMousePosition;

                TransformGroup? transformGroup = DisplayImage.RenderTransform as TransformGroup;
                if (transformGroup == null)
                {
                    transformGroup = new TransformGroup();
                    transformGroup.Children.Add(new ScaleTransform(_scale, _scale));
                    transformGroup.Children.Add(new TranslateTransform(0, 0));
                }

                TranslateTransform? translateTransform = null;
                foreach (Transform transform in transformGroup.Children)
                {
                    if (transform is TranslateTransform)
                    {
                        translateTransform = transform as TranslateTransform;
                        break;
                    }
                }

                if (translateTransform == null)
                {
                    translateTransform = new TranslateTransform();
                    transformGroup.Children.Add(translateTransform);
                }

                translateTransform.X += delta.X;
                translateTransform.Y += delta.Y;

                DisplayImage.RenderTransform = transformGroup;
                _lastMousePosition = currentPosition;

                DrawContour();
            }
            else
            {
                UpdatePixelInfo(e);
            }
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            this.Cursor = Cursors.Arrow;
            PixelInfoText.Visibility = Visibility.Collapsed;
        }

        private void UpdatePixelInfo(MouseEventArgs e)
        {
            if (!_isDragging)
                DisplayImage.Cursor = Cursors.Cross;

            if (DisplayImage.Source is not BitmapSource bitmapSource)
            {
                PixelInfoText.Visibility = Visibility.Collapsed;
                return;
            }

            Point mousePos = e.GetPosition(DisplayImage);

            if (!(bitmapSource.Width > 0) || !(bitmapSource.Height > 0))
            {
                PixelInfoText.Visibility = Visibility.Collapsed;
                return;
            }

            int pixelX = (int)(mousePos.X * bitmapSource.PixelWidth / bitmapSource.Width);
            int pixelY = (int)(mousePos.Y * bitmapSource.PixelHeight / bitmapSource.Height);

            if (pixelX < 0 || pixelX >= bitmapSource.PixelWidth ||
                pixelY < 0 || pixelY >= bitmapSource.Height)
            {
                PixelInfoText.Visibility = Visibility.Collapsed;
                return;
            }

            ushort? rawValue = null;
            if (DataContext is CameraShowViewModel viewModel)
            {
                rawValue = viewModel.GetRawPixelValue(pixelX, pixelY);
            }

            string valueText = rawValue.HasValue ? rawValue.Value.ToString() : "N/A";
            PixelInfoText.Text = $"X: {pixelX}  Y: {pixelY}  Pixel: {valueText}";
            PixelInfoText.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

            Point mousePosInContainer = e.GetPosition(ImageContainerGrid);
            double textX = mousePosInContainer.X + 10;
            double textY = mousePosInContainer.Y - PixelInfoText.DesiredSize.Height - 5;

            if (textX + PixelInfoText.DesiredSize.Width > ImageContainerGrid.ActualWidth)
                textX = mousePosInContainer.X - PixelInfoText.DesiredSize.Width - 10;

            if (textY < 0)
                textY = mousePosInContainer.Y + 20;

            Canvas.SetLeft(PixelInfoText, textX);
            Canvas.SetTop(PixelInfoText, textY);
            PixelInfoText.Visibility = Visibility.Visible;
        }

        private void DrawContour()
        {
            ContourCanvas.Children.Clear();

            if (_contourPoints == null || _contourPoints.Count < 2)
                return;

            var polygon = new System.Windows.Shapes.Polygon
            {
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 1,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = System.Windows.Media.Brushes.Transparent
            };

            var scaledPoints = new PointCollection();
            foreach (var point in _contourPoints)
            {
                Point transformedPoint = ApplyTransform(point);
                scaledPoints.Add(transformedPoint);
            }

            polygon.Points = scaledPoints;
            ContourCanvas.Children.Add(polygon);
        }

        private Point ApplyTransform(Point originalPoint)
        {
            Transform transform = DisplayImage.RenderTransform;
            if (transform == null)
                return originalPoint;
            double x = originalPoint.X * (96.0 / 144);
            double y = originalPoint.Y * (96.0 / 144);

            return transform.Transform(new Point(x, y));
        }

        public void ResetView()
        {
            _scale = 1.0;
            DisplayImage.RenderTransform = new ScaleTransform(1, 1);
            DrawContour();
        }
    }
}