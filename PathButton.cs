using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VegaBeamTool
{
    internal class PathButton : Button
    {
        public static readonly DependencyProperty PathDataProperty =
         DependencyProperty.Register(
             "PathData",
             typeof(Geometry),
             typeof(PathButton),
             new PropertyMetadata(null, OnPathDataChanged));

        public Geometry PathData
        {
            get { return (Geometry)GetValue(PathDataProperty); }
            set { SetValue(PathDataProperty, value); }
        }
        private static void OnPathDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = d as PathButton;
            if (button != null)
            {
                button.InvalidateVisual();
            }
        }
        protected override void OnRender(DrawingContext drawingContext)
        {
            // base.OnRender(drawingContext);

            if (PathData != null)
            {
                double scaleX = ActualWidth / PathData.Bounds.Width;
                double scaleY = ActualHeight / PathData.Bounds.Height;
                var transform = new ScaleTransform(scaleX, scaleY);
                var transformedGeometry = PathData.CloneCurrentValue();
                transformedGeometry.Transform = transform;
                drawingContext.DrawGeometry(Background, new Pen(BorderBrush, BorderThickness.Left), transformedGeometry);
                //drawingContext.DrawGeometry(Background, new Pen(BorderBrush, BorderThickness.Left), PathData);
            }
            if (Content != null)
            {
                var text = Content.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    var formattedText = new FormattedText(
                        text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
                        FontSize,
                        Foreground,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    double textWidth = formattedText.Width;
                    double textHeight = formattedText.Height;
                    double x = (ActualWidth - textWidth) / 2;
                    double y = (ActualHeight - textHeight) / 2;
                    drawingContext.DrawText(formattedText, new Point(x, y));
                }
            }
        }

        static PathButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(PathButton), new FrameworkPropertyMetadata(typeof(PathButton)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            //  BorderBrush = Brushes.Transparent;
        }
    }


}

