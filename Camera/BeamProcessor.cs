using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;
using OpenCvSharp;

namespace BeamProcessor
{
    public class BeamProcessor
    {
        // todo 如需变动找算法-太波
        public static BeamParameters GetBeamSpotParameters(Mat image16bit, Mat image8bit, List<RecordItems> listRecordCenter, out Mat resultImage)
        {
            BeamParameters beamParams = new BeamParameters();
            Cv2.MinMaxIdx(image16bit, out double minGraycale, out double maxGraycale);
            beamParams.MaxGrayscale = (int)maxGraycale;

            int rows = image16bit.Rows;
            int cols = image16bit.Cols;

            // 高斯模糊去除噪点
            Mat imageBlured16bit = new Mat(rows, cols, MatType.CV_16UC1);
            Cv2.GaussianBlur(image16bit, imageBlured16bit, new Size(3, 3), 0);

            // 二值化
            Mat imageBina = new Mat(rows, cols, MatType.CV_8UC1);
            Mat imageBlured8bit = new Mat(rows, cols, MatType.CV_8UC1);
            imageBlured16bit.ConvertTo(imageBlured8bit, MatType.CV_8UC1, 1.0 / 256.0);
            Cv2.Threshold(imageBlured8bit, imageBina, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            // 寻找轮廓
            Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(imageBina, out contours, out hierarchy, RetrievalModes.List, ContourApproximationModes.ApproxSimple);


            resultImage = new Mat();
            Cv2.ApplyColorMap(image8bit, resultImage, ColormapTypes.Jet);

            // 筛选轮廓
            bool isBeamFound = false;
            if (contours.Length > 0)
            {
                var beamCountor = contours.MaxBy(c => c.Length);
                if (beamCountor != null && beamCountor.Length > 6)
                {
                    RotatedRect fittedEllipse = Cv2.FitEllipse(beamCountor);
                    beamParams.RotatedRect = fittedEllipse;
                    beamParams.BeamCountor = beamCountor;
                    isBeamFound = true;
                }
            }

            if (!isBeamFound)
            {
                return beamParams;
            }
            double sampleRadius = Math.Max(beamParams.RotatedRect.Size.Width, beamParams.RotatedRect.Size.Height);

            // 膨胀拓宽光斑区域
            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 9));
            Mat imageDilated = new Mat(rows, cols, MatType.CV_8UC1);
            Cv2.Dilate(imageBina, imageDilated, kernel);

            // 联通区分析
            Mat labelMat = new Mat(rows, cols, MatType.CV_8UC1);
            Mat stats = new Mat();
            Mat centroids = new Mat();
            int labelNum = Cv2.ConnectedComponentsWithStats(imageDilated, labelMat, stats, centroids, PixelConnectivity.Connectivity8);

            if (labelNum < 2)
            {
                return beamParams;
            }

            // 确定光斑区域
            int maxAreaIndex = 0;
            int maxArea = 0;
            for (int i = 1; i < stats.Rows; i++)
            {
                int area = stats.At<int>(i, 4);
                if (area > maxArea)
                {
                    maxArea = stats.At<int>(i, 4);
                    maxAreaIndex = i;
                }
            }
            int rowIndex = stats.At<int>(maxAreaIndex, 1);
            int beamHeight = stats.At<int>(maxAreaIndex, 3);
            int colIndex = stats.At<int>(maxAreaIndex, 0);
            int beamWidth = stats.At<int>(maxAreaIndex, 2);

            int rowStart = rowIndex + beamHeight / 2 - 2 * beamHeight;
            int rowEnd = rowIndex + beamHeight / 2 + 2 * beamHeight;
            int colStart = colIndex + beamWidth / 2 - 4 * beamWidth;
            int colEnd = colIndex + beamWidth / 2 + 4 * beamWidth;

            // 对光斑区域进行采样
            List<Point3d> samples = new List<Point3d>();
            for (int r = rowStart; r < rowEnd; r++)
            {
                for (int c = colStart; c < colEnd; c++)
                {
                    if (r < 0 || r >= rows || c < 0 || c >= cols)
                        continue;

                    samples.Add(new Point3d(c, r, image16bit.At<ushort>(r, c)));
                }
            }

            // 初始参数估计
            // [A, mu_x, mu_y, sigma_x, sigma_y, theta, noise]
            double A0 = samples.Select(p => p.Z).Max();
            double mu_x0 = (colStart + colEnd) / 2.0;
            double mu_y0 = (rowStart + rowEnd) / 2.0;
            double sigma_x0 = (colEnd - colStart) / 4.0;
            double sigma_y0 = (rowEnd - rowStart) / 4.0;
            double theta0 = 0.0;
            double noise0 = samples.Select(p => p.Z).Min();
            var initGuess = new double[] { A0, mu_x0, mu_y0, sigma_x0, sigma_y0, theta0, noise0 };

            // 进行2D高斯拟合
            FitGaussHelper.FitGauss2D(samples, initGuess, beamParams);

            // 进行剖面采样
            var rectPoints = beamParams.RotatedRect.Points();
            BeamProfileLine line1 = new BeamProfileLine();
            BeamProfileLine line2 = new BeamProfileLine();
            BeamProfileLine lineV = new BeamProfileLine();
            BeamProfileLine lineH = new BeamProfileLine();

            // line1: point0-point1
            if (Math.Abs(rectPoints[0].X - rectPoints[1].X) > 1e-6)
            {
                double radius = 1.5 * Math.Sqrt(Math.Pow(rectPoints[0].X - rectPoints[1].X, 2) + Math.Pow(rectPoints[0].Y - rectPoints[1].Y, 2));
                double k = (rectPoints[0].Y - rectPoints[1].Y) / (rectPoints[0].X - rectPoints[1].X);
                double b = beamParams.CenterY - k * beamParams.CenterX;

                if (Math.Abs(k) < 1)
                {
                    var x1 = beamParams.CenterX - radius < 0 ? 0 : beamParams.CenterX - radius;
                    var x2 = beamParams.CenterX + radius >= cols - 1 ? cols - 1 : beamParams.CenterX + radius;
                    var y1 = k * x1 + b;
                    var y2 = k * x2 + b;
                    line1.P1 = new Point2d(x1, y1);
                    line1.P2 = new Point2d(x2, y2);
                    line1.GetProfile(ref imageBlured16bit, k, b);
                }
                else
                {
                    var y1 = beamParams.CenterY - radius < 0 ? 0 : beamParams.CenterY - radius;
                    var y2 = beamParams.CenterY + radius >= rows - 1 ? rows - 1 : beamParams.CenterY + radius;
                    var x1 = (y1 - b) / k;
                    var x2 = (y2 - b) / k;
                    line1.P1 = new Point2d(x1, y1);
                    line1.P2 = new Point2d(x2, y2);
                    line1.GetProfile(ref imageBlured16bit, k, b);
                }

                line1.FitGaussianCurve();
            }

            // line2: point1-point2
            if (Math.Abs(rectPoints[1].X - rectPoints[2].X) > 1e-6)
            {
                double radius = 1.5 * Math.Sqrt(Math.Pow(rectPoints[1].X - rectPoints[2].X, 2) + Math.Pow(rectPoints[1].Y - rectPoints[2].Y, 2));
                double k = (rectPoints[1].Y - rectPoints[2].Y) / (rectPoints[1].X - rectPoints[2].X);
                double b = beamParams.CenterY - k * beamParams.CenterX;

                if (Math.Abs(k) < 1)
                {
                    var x1 = beamParams.CenterX - radius < 0 ? 0 : beamParams.CenterX - radius;
                    var x2 = beamParams.CenterX + radius >= cols - 1 ? cols - 1 : beamParams.CenterX + radius;
                    var y1 = k * x1 + b;
                    var y2 = k * x2 + b;
                    line2.P1 = new Point2d(x1, y1);
                    line2.P2 = new Point2d(x2, y2);
                    line2.GetProfile(ref imageBlured16bit, k, b);
                }
                else
                {
                    var y1 = beamParams.CenterY - radius < 0 ? 0 : beamParams.CenterY - radius;
                    var y2 = beamParams.CenterY + radius >= rows - 1 ? rows - 1 : beamParams.CenterY + radius;
                    var x1 = (y1 - b) / k;
                    var x2 = (y2 - b) / k;
                    line2.P1 = new Point2d(x1, y1);
                    line2.P2 = new Point2d(x2, y2);
                    line2.GetProfile(ref imageBlured16bit, k, b);
                }
                line2.FitGaussianCurve();
            }

            lineH.P1 = new Point2d(beamParams.CenterX - sampleRadius, beamParams.CenterY);
            lineH.P2 = new Point2d(beamParams.CenterX + sampleRadius, beamParams.CenterY);
            lineV.P1 = new Point2d(beamParams.CenterX, beamParams.CenterY - sampleRadius);
            lineV.P2 = new Point2d(beamParams.CenterX, beamParams.CenterY + sampleRadius);

            lineH.GetHorVProfile(ref imageBlured16bit, Direction.Horizontal);
            lineV.GetHorVProfile(ref imageBlured16bit, Direction.Vertical);

            lineH.FitGaussianCurve();
            lineV.FitGaussianCurve();

            beamParams.LineH = lineH;
            beamParams.LineV = lineV;
            beamParams.LineA = line1.Radius > line2.Radius ? line1 : line2;
            beamParams.LineB = line1.Radius > line2.Radius ? line2 : line1;

            double length = 8;
            foreach (var cur in listRecordCenter)
            {
                Cv2.Line(resultImage, new Point(cur.CenterX - length / 2, cur.CenterY - length / 2),
                    new Point(cur.CenterX + length / 2, cur.CenterY + length / 2),
                    Scalar.FromRgb(cur.ColorRed, cur.ColorGreen, cur.ColorBlue), 1);

                Cv2.Line(resultImage, new Point(cur.CenterX - length / 2, cur.CenterY + length / 2),
                    new Point(cur.CenterX + length / 2, cur.CenterY - length / 2),
                    Scalar.FromRgb(cur.ColorRed, cur.ColorGreen, cur.ColorBlue), 1);
            }

            return beamParams;
        }

    }

    public enum Direction
    {
        Horizontal,
        Vertical
    }
    public class BeamProfileLine
    {
        public int SampleCount { get; set; }
        public Point2d P1 { get; set; }
        public Point2d P2 { get; set; }
        public Point2d[] ProfilePoints { get; set; }
        public Point2d[] DstCurvePoints { get; set; }
        public double Radius { get; set; }
        public void GetProfile(ref Mat image, double k, double b)
        {
            SampleCount = 100;
            ProfilePoints = new Point2d[SampleCount];
            if (Math.Abs(k) < 1)
            {
                double dx = (P2.X - P1.X) / SampleCount;
                for (int i = 0; i < SampleCount; i++)
                {
                    double x = P1.X + i * dx;
                    double y = k * x + b;
                    double l = Math.Sqrt(Math.Pow(i * dx, 2) + Math.Pow(k * i * dx, 2));
                    int x_int = (int)x;
                    int y_int = (int)y;
                    if (y_int + 1 >= image.Rows || x_int + 1 >= image.Cols || y_int < 0 || x_int < 0)
                    {
                        ProfilePoints[i] = new Point2d(l, 0);
                        continue;
                    }
                    double u = x - x_int;
                    double v = y - y_int;
                    double a1 = (1 - u) * (1 - v);
                    double a2 = (1 - u) * v;
                    double a3 = u * (1 - v);
                    double a4 = u * v;
                    var v1 = image.At<ushort>(y_int, x_int);
                    var v2 = image.At<ushort>(y_int + 1, x_int);
                    var v3 = image.At<ushort>(y_int, x_int + 1);
                    var v4 = image.At<ushort>(y_int + 1, x_int + 1);
                    double grayValue = a1 * v1 + a2 * v2 + a3 * v3 + a4 * v4;
                    ProfilePoints[i] = new Point2d(l, grayValue);
                }
            }
            else
            {
                double dy = (P2.Y - P1.Y) / SampleCount;
                for (int i = 0; i < SampleCount; i++)
                {
                    double y = P1.Y + i * dy;
                    double x = (y - b) / k;
                    double l = Math.Sqrt(Math.Pow(i * dy, 2) + Math.Pow((i * dy) / k, 2));
                    int x_int = (int)x;
                    int y_int = (int)y;
                    if (y_int + 1 >= image.Rows || x_int + 1 >= image.Cols || y_int < 0 || x_int < 0)
                    {
                        ProfilePoints[i] = new Point2d(l, 0);
                        continue;
                    }
                    double u = x - x_int;
                    double v = y - y_int;
                    double a1 = (1 - u) * (1 - v);
                    double a2 = (1 - u) * v;
                    double a3 = u * (1 - v);
                    double a4 = u * v;
                    var v1 = image.At<ushort>(y_int, x_int);
                    var v2 = image.At<ushort>(y_int + 1, x_int);
                    var v3 = image.At<ushort>(y_int, x_int + 1);
                    var v4 = image.At<ushort>(y_int + 1, x_int + 1);
                    double grayValue = a1 * v1 + a2 * v2 + a3 * v3 + a4 * v4;
                    ProfilePoints[i] = new Point2d(l, grayValue);
                }
            }
        }
        public void GetHorVProfile(ref Mat image, Direction direction)
        {
            if (direction == Direction.Horizontal)
            {
                int row = (int)Math.Round(P1.Y);
                int colStart = (int)Math.Round(P1.X);
                int colEnd = (int)Math.Round(P2.X);
                SampleCount = colEnd - colStart;
                ProfilePoints = new Point2d[SampleCount];
                for (int c = colStart; c < colEnd; c++)
                {
                    if (c < 0 || c >= image.Cols || row < 0 || row >= image.Rows)
                    {
                        ProfilePoints[c - colStart] = new Point2d(c - colStart, 0);
                        continue;
                    }
                    double grayValue = image.At<ushort>(row, c);
                    ProfilePoints[c - colStart] = new Point2d(c - colStart, grayValue);
                }
            }
            if (direction == Direction.Vertical)
            {
                int col = (int)Math.Round(P1.X);
                int rowStart = (int)Math.Round(P1.Y);
                int rowEnd = (int)Math.Round(P2.Y);
                SampleCount = rowEnd - rowStart;
                ProfilePoints = new Point2d[SampleCount];
                for (int r = rowStart; r < rowEnd; r++)
                {
                    if (col < 0 || col >= image.Cols || r < 0 || r >= image.Rows)
                    {
                        ProfilePoints[r - rowStart] = new Point2d(r - rowStart, 0);
                        continue;
                    }
                    double grayValue = image.At<ushort>(r, col);
                    ProfilePoints[r - rowStart] = new Point2d(r - rowStart, grayValue);
                }
            }
        }

        public void FitGaussianCurve()
        {
            var maxValue = ProfilePoints.Select(p => p.Y).Max();
            double[] initialParams = { maxValue, ProfilePoints[ProfilePoints.Length / 2].X, (ProfilePoints[^1].X - ProfilePoints[0].X) / 4, 0 };
            Point2d[] dstCurveData = new Point2d[SampleCount];
            for (int i = 0; i < SampleCount; i++)
            {
                dstCurveData[i].X = ProfilePoints[i].X;
            }
            Radius = FitGaussHelper.GaussianCurveFitting(ProfilePoints, initialParams, ref dstCurveData);
            DstCurvePoints = dstCurveData;
        }
    }
    public class BeamParameters
    {
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double WidthMajor { get; set; }
        public double WidthMinor { get; set; }
        public double AzimuthAngle { get; set; }
        public RotatedRect RotatedRect { get; set; }
        public int MaxGrayscale { get; set; }
        public double Noise { get; set; }
        public int IterationCount { get; set; }
        public string ExitCondition { get; set; } = "None";
        public string ErrorMsg { get; set; } = "None";
        public BeamProfileLine LineA { get; set; }
        public BeamProfileLine LineB { get; set; }
        public BeamProfileLine LineH { get; set; }
        public BeamProfileLine LineV { get; set; }
        public Point[]? BeamCountor { get; set; }
    }

    public static class FitGaussHelper
    {
        private static double GaussianFunc(double amp, double avg, double sigma, double x)
        {
            return amp * Math.Exp(-0.5 * Math.Pow((x - avg) / sigma, 2));
        }

        public static double GaussianCurveFitting(Point2d[] dataPoints, double[] initialParams, ref Point2d[] dstCurveData)
        {
            double[] xData = dataPoints.Select(p => p.X).ToArray();
            double[] yData = dataPoints.Select(p => p.Y).ToArray();
            var result = Fit.Curve(xData, yData, GaussianFunc, initialParams[0], initialParams[1], initialParams[2], 1e-9, 1000);

            double amp = result.P0;
            double avg = result.P1;
            double sigma = result.P2;
            for (int i = 0; i < dstCurveData.Length; i++)
            {
                dstCurveData[i].Y = amp * Math.Exp(-Math.Pow(dstCurveData[i].X - avg, 2) / (2 * sigma * sigma));
            }
            double radius = Math.Sqrt(-2 * sigma * sigma * Math.Log(0.1353));
            return radius;
        }

        public static void FitGauss2D(List<Point3d> samples, double[] initGuess, BeamParameters beamParams)
        {
            Vector<double> initialGuess = Vector<double>.Build.Dense(initGuess);

            // 构造残差函数
            Func<Vector<double>, Vector<double>, Vector<double>> residualFunc = (parameters, unused_things) =>
            {
                double A = parameters[0];
                double mu_x = parameters[1];
                double mu_y = parameters[2];
                double sigma_x = parameters[3];
                double sigma_y = parameters[4];
                double theta = parameters[5];
                double noise = parameters[6];

                double eps = 1e-8;
                double sigma_x_sq = sigma_x * sigma_x + eps;
                double sigma_y_sq = sigma_y * sigma_y + eps;

                double cos_theta = Math.Cos(theta);
                double sin_theta = Math.Sin(theta);
                double sin_2theta = Math.Sin(2.0 * theta);

                // 预计算系数 a, b, c
                double a = (cos_theta * cos_theta) / (2.0 * sigma_x_sq) +
                           (sin_theta * sin_theta) / (2.0 * sigma_y_sq);

                double b = -(sin_2theta) / (4.0 * sigma_x_sq) +
                           (sin_2theta) / (4.0 * sigma_y_sq);

                double c = (sin_theta * sin_theta) / (2.0 * sigma_x_sq) +
                           (cos_theta * cos_theta) / (2.0 * sigma_y_sq);

                double[] residuals = new double[samples.Count];

                for (int i = 0; i < samples.Count; i++)
                {
                    double dx = samples[i].X - mu_x;
                    double dy = samples[i].Y - mu_y;

                    double exponent = a * dx * dx + 2.0 * b * dx * dy + c * dy * dy;

                    // 防止指数溢出
                    if (exponent > 700) exponent = 700;

                    double predicted = A * Math.Exp(-exponent) + noise;
                    residuals[i] = predicted - samples[i].Z;
                }

                return Vector<double>.Build.Dense(residuals);
            };

            var objective = ObjectiveFunction.NonlinearModel(residualFunc, Vector<double>.Build.Dense(samples.Count), Vector<double>.Build.Dense(samples.Count), accuracyOrder: 2);

            // 执行非线性最小二乘优化 (Levenberg-Marquardt)
            try
            {
                var solver = new LevenbergMarquardtMinimizer();
                var result = solver.FindMinimum(objective, initialGuess);
                if (result.ReasonForExit != ExitCondition.Converged)
                {
                    beamParams.ExitCondition = result.ReasonForExit.ToString();
                }
                var finalParams = result.MinimizingPoint.ToArray();

                if (finalParams != null)
                {
                    double A = finalParams[0];
                    double mu_x = finalParams[1];
                    double mu_y = finalParams[2];
                    double sigma_x = finalParams[3];
                    double sigma_y = finalParams[4];
                    double theta = finalParams[5];
                    double noise = finalParams[6];

                    // 计算长轴、短轴和角度 
                    double fwhm_factor = 4.0;
                    double majorWidth, minorWidth, angleDegree;

                    if (Math.Abs(sigma_x) > Math.Abs(sigma_y))
                    {
                        majorWidth = fwhm_factor * Math.Abs(sigma_x);
                        minorWidth = fwhm_factor * Math.Abs(sigma_y);
                    }
                    else
                    {
                        majorWidth = fwhm_factor * Math.Abs(sigma_y);
                        minorWidth = fwhm_factor * Math.Abs(sigma_x);
                    }

                    // 弧度转角度
                    angleDegree = theta * (180.0 / Math.PI);

                    beamParams.WidthMajor = majorWidth;
                    beamParams.WidthMinor = minorWidth;
                    beamParams.AzimuthAngle = angleDegree;
                    beamParams.CenterX = mu_x;
                    beamParams.CenterY = mu_y;
                    beamParams.Noise = noise;
                    beamParams.IterationCount = result.Iterations;
                }
            }
            catch (Exception ex)
            {
                beamParams.ErrorMsg = ex.Message;
            }
        }
    }
    public class RecordItems
    {
        public int SNoIndex { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }

        public int ColorRed { get; set; }
        public int ColorGreen { get; set; }
        public int ColorBlue { get; set; }

    }
}
