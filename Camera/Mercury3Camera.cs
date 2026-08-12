using Client.Util;
using GxIAPINET;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using static VegaBeamTool.Camera.ICameraDevice;
namespace VegaBeamTool.Camera
{
    public class Mercury3Camera : CameraBase
    {
        #region public对外接口
        public Mercury3Camera() => Init();
        public override void Start()
        {
            try
            {
                InitDevice();
            }
            catch (Exception ex)
            {
                Stop();
                testLogger.Error(ex.Message, ex);
            }
        }

        public override void Stop()
        {
            try
            {
                UninitDevice();
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }
        }

        public override void SetCameraCom(int nDeviceIndex) { }
        public override void SetCameraSN(string strSN) => _strSN = strSN;
        public override void SetCameraMac(string strMac) => _strMac = strMac;

        public override CameraVendor Vendor => CameraVendor.DahengMercury3;

        public override IReadOnlyList<CameraDeviceInfo> EnumerateDevices()
        {
            List<CameraDeviceInfo> listDevice = [];
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGxFactory)
                    {
                        return listDevice;
                    }

                    _listIGXDeviceInfo.Clear();
                    _cameraControl.IGxFactory.UpdateAllDeviceList(200, _listIGXDeviceInfo);

                    for (int nIndex = 0; nIndex < _listIGXDeviceInfo.Count; nIndex++)
                    {
                        IGXDeviceInfo objDeviceInfo = _listIGXDeviceInfo[nIndex];
                        string strSN = objDeviceInfo.GetSN();
                        string strModel = objDeviceInfo.GetModelName();
                        listDevice.Add(new CameraDeviceInfo
                        {
                            Vendor = CameraVendor.DahengMercury3,
                            Index = nIndex,
                            SerialNumber = strSN,
                            Model = strModel,
                            DisplayName = $"[{nIndex}] {strModel} ({strSN})",
                        });
                    }
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
            }
            return listDevice;
        }

        public override void SelectDevice(CameraDeviceInfo device)
        {
            _strSN = device.SerialNumber;
            _nComId = device.Index;
        }

        public override void Dispose()
        {
            Stop();
            UnInit();
            base.Dispose();
        }

        public override bool GetCameraStartSatuts() => _cameraControl.IsOpen;
        public override bool Snapshot(out byte[]? byteImage)
        {
            byteImage = null;
            bool bReturn = false;
            try
            {
                if (!_cameraControl.IsOpen)
                {
                    testLogger.Error("Snapshot fail, Mercury3Camera is closed.");
                    return bReturn;
                }

                StopCapture();

                int nReGetCount = 3;
                do
                {
                    if (GetImageByDQBuf(out byteImage))
                    {
                        bReturn = true;
                        break;
                    }
                    nReGetCount--;
                    Thread.Sleep(50);
                }
                while (nReGetCount > 0);

            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                bReturn = false;
            }
            return bReturn;
        }


        private bool GetImageByDQBuf(out byte[]? byteImage)
        {
            byteImage = null;
            bool bReturn = false;
            lock (_cameraLock)
            {
                IFrameData? objImageData = _cameraControl.IGXStream?.DQBuf(1000);

                if (null != objImageData && objImageData.GetStatus() == GX_FRAME_STATUS_LIST.GX_FRAME_STATUS_SUCCESS)
                {
                    if (FrameCallback(objImageData)
                        && _byteMonoBuffer is not null)
                    {
                        byteImage = new byte[_byteMonoBuffer.Length];
                        Array.Copy(_byteMonoBuffer, byteImage, _byteMonoBuffer.Length);
                        bReturn = true;
                    }
                    else
                    {
                        testLogger.Warn("Failed to process image callback");
                        bReturn = false;
                    }
                    _cameraControl.IGXStream?.QBuf(objImageData);
                }
                else
                {
                    testLogger.Warn($"Failed to get IFrameData, Status:{objImageData?.GetStatus()}");
                    bReturn = false;
                }
            }
            return bReturn;
        }

        public override bool Capture()
        {
            if (Interlocked.Exchange(ref _captureInterLock, 1) == 0)
            {
                try
                {
                    if (!_cameraControl.IsOpen)
                    {
                        return false;
                    }

                    CancellationTokenSource? curTokenSource;
                    lock (_captureLock)
                    {
                        if (_cameraControl.IsSnap)
                        {
                            return true;
                        }
                        _cameraControl.IsSnap = true;
                        _cancellationTokenSource = new();
                        curTokenSource = _cancellationTokenSource;
                    }

                    _ = Task.Run(() =>
                    {
                        try
                        {
                            while (!curTokenSource.IsCancellationRequested && _cameraControl.IsOpen)
                            {
                                if (GetImageByDQBuf(out byte[]? byteImage) && null != byteImage)
                                {
                                    _ = (OnCallbackBitmap?.Invoke(byteImage, false));
                                }
                                Thread.Sleep(50);
                            }
                        }
                        catch (CGalaxyException objError)
                        {
                            testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                            StopCapture();
                        }
                        finally
                        {
                            lock (_captureLock)
                            {
                                curTokenSource?.Dispose();
                                curTokenSource = null;
                            }

                        }
                    });
                    return true;
                }
                catch (CGalaxyException objError)
                {
                    testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                    return false;
                }
                finally
                {
                    _ = Interlocked.Exchange(ref _captureInterLock, 0);
                }
            }
            return false;
        }

        public override void StopCapture()
        {
            try
            {
                if (!_cameraControl.IsOpen)
                {
                    return;
                }

                lock (_captureLock)
                {
                    if (!_cameraControl.IsSnap)
                    {
                        return;
                    }
                    _cameraControl.IsSnap = false;
                    _cancellationTokenSource?.Cancel();
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
            }
        }

        public override int SetCameraGain(int nGain)
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXFeatureControl)
                    {
                        return -1;
                    }

                    double dMinGain = _cameraControl.IGXFeatureControl.GetFloatFeature("Gain").GetMin();
                    double dMaxGain = _cameraControl.IGXFeatureControl.GetFloatFeature("Gain").GetMax();
                    double dGain = (double)nGain;

                    if (dGain > dMaxGain)
                    {
                        dGain = dMaxGain;
                    }

                    if (dGain < dMinGain)
                    {
                        dGain = dMinGain;
                    }
                    _cameraControl.IGXFeatureControl.GetFloatFeature("Gain").SetValue(dGain);

                }
                return 0;
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }

        }

        public override int GetCameraGain()
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXFeatureControl)
                    {
                        return -1;
                    }
                    return (int)_cameraControl.IGXFeatureControl.GetFloatFeature("Gain").GetValue();
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }
        }

        public override int GetCameraGainMin()
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXFeatureControl)
                    {
                        return -1;
                    }
                    return (int)_cameraControl.IGXFeatureControl.GetFloatFeature("Gain").GetMin();
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }
        }

        public override int GetCameraGainMax()
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXFeatureControl)
                    {
                        return -1;
                    }
                    return (int)_cameraControl.IGXFeatureControl.GetFloatFeature("Gain").GetMax();
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }
        }

        public override int SetCameraColorMode(string strColorModes)
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXDevice)
                    {
                        return -1;
                    }
                    // uint nPixelFormatValue = 0;
                    _cameraControl.IGXDevice.GetRemoteFeatureControl().GetEnumFeature("PixelFormat").SetValue(strColorModes);
                    return 1;
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }
        }

        public override int GetCameraColorMode()
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXDevice)
                    {
                        return -1;
                    }
                    uint nPixelFormatValue = 0;
                    string strPixelFormat = _cameraControl.IGXDevice.GetRemoteFeatureControl().GetEnumFeature("PixelFormat").GetValue();
                    GetConvertPixelFormat(strPixelFormat, ref nPixelFormatValue);

                    if ((uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO8 == nPixelFormatValue)
                    {
                        return 8;
                    }
                    else if ((uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12 == nPixelFormatValue)
                    {
                        return 12;
                    }
                    else if ((uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO16 == nPixelFormatValue)
                    {
                        return 16;
                    }

                    return -1;
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }
        }


        public override int SetCameraExposureTime(int nTimes)
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXFeatureControl)
                    {
                        return -1;
                    }

                    double dMinTimes = _cameraControl.IGXFeatureControl.GetFloatFeature("ExposureTime").GetMin();
                    double dMaxTimes = _cameraControl.IGXFeatureControl.GetFloatFeature("ExposureTime").GetMax();
                    double dTimes = (double)nTimes;

                    if (dTimes > dMaxTimes)
                    {
                        dTimes = dMaxTimes;
                    }

                    if (dTimes < dMinTimes)
                    {
                        dTimes = dMinTimes;
                    }
                    _cameraControl.IGXFeatureControl.GetFloatFeature("ExposureTime").SetValue(dTimes);

                }
                return 0;
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }
        }


        public override int GetCameraExposureTime()
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXFeatureControl)
                    {
                        return -1;
                    }
                    return (int)_cameraControl.IGXFeatureControl.GetFloatFeature("ExposureTime").GetValue();
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }
        }

        public override int GetCameraExposureTimeMin()
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXFeatureControl)
                    {
                        return -1;
                    }
                    return (int)_cameraControl.IGXFeatureControl.GetFloatFeature("ExposureTime").GetMin();
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }
        }

        public override int GetCameraExposureTimeMax()
        {
            try
            {
                lock (_cameraLock)
                {
                    if (null == _cameraControl.IGXFeatureControl)
                    {
                        return -1;
                    }
                    return (int)_cameraControl.IGXFeatureControl.GetFloatFeature("ExposureTime").GetMax();
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return -1;
            }
        }

        public override void RegisterCallbackBitmap(CallbackBitmapDelegate<byte[], bool> callbackBitmap) => OnCallbackBitmap += callbackBitmap;

        public override void UnRegisterCallbackBitmap(CallbackBitmapDelegate<byte[], bool> callbackBitmap) => OnCallbackBitmap -= callbackBitmap;
        #endregion


        #region protected/private
        private void Init()
        {
            try
            {
                _cameraControl?.IGxFactory?.Init();
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
            }

        }

        private void UnInit()
        {
            try
            {
                _cameraControl?.IGxFactory?.Uninit();
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
            }
        }

        protected override void InitDevice()
        {
            if (null == _cameraControl
                || null == _cameraControl.IGxFactory
                || string.IsNullOrEmpty(_strSN))
            {
                return;
            }

            try
            {
                lock (_cameraLock)
                {
                    if (_cameraControl.IsOpen)
                    {
                        testLogger.Debug("Camera is Open");
                        return;
                    }

                    _listIGXDeviceInfo.Clear();
                    _cameraControl.IGxFactory.UpdateAllDeviceList(10, _listIGXDeviceInfo);

                    if (_listIGXDeviceInfo.Count < 1)
                    {
                        testLogger.Error($"Get Mercury3Camera device counts{_listIGXDeviceInfo.Count}");
                        return;
                    }
                    _cameraControl.CameraSN = _strSN;
                    _cameraControl.IGXDevice = _cameraControl.IGxFactory.OpenDeviceBySN(_cameraControl.CameraSN, GX_ACCESS_MODE.GX_ACCESS_EXCLUSIVE);

                    if (null == _cameraControl.IGXDevice
                        || _cameraControl.IGXDevice.GetStreamCount() < 1)
                    {
                        testLogger.Error($"IGXDevice is null || GetStreamCount < 1");
                        return;
                    }

                    _cameraControl.IGXFeatureControl = _cameraControl.IGXDevice.GetRemoteFeatureControl();
                    _cameraControl.IGXStream = _cameraControl.IGXDevice.OpenStream(0);



                    _cameraControl.IImageProcessConfig = _cameraControl.IGXDevice.CreateImageProcessConfig();

                    if (null == _cameraControl.IGXStream
                        || null == _cameraControl.IGXFeatureControl)
                    {
                        testLogger.Error("IGXStream is null || IGXFeature Control is null");
                        return;
                    }
                    _cameraControl.IGXStreamFeatureControl = _cameraControl.IGXStream.GetFeatureControl();

                    // 每次取最新一帧
                    _cameraControl.IGXStreamFeatureControl?.GetEnumFeature("StreamBufferHandlingMode").SetValue(STREAM_BUFFER_HANDING_MODE.NewestOnly);

                    if (IntPtr.Zero != _ptrOutBuffer)
                    {
                        ReleaseBuffer();
                    }

                    _cameraControl.IGXImageFormatConvert = _cameraControl.IGxFactory.CreateImageFormatConvert();

                    ImageWidth = (int)_cameraControl.IGXDevice.GetRemoteFeatureControl().GetIntFeature("Width").GetValue();
                    ImageHeight = (int)_cameraControl.IGXDevice.GetRemoteFeatureControl().GetIntFeature("Height").GetValue();

                    SetCameraColorMode("Mono12");
                    _imageByte = 2;
                    CreateBuffer(ImageWidth, ImageHeight);

                    if (GX_DEVICE_CLASS_LIST.GX_DEVICE_CLASS_GEV == _cameraControl.IGXDevice.GetDeviceInfo().GetDeviceClass()
                        && _cameraControl.IGXFeatureControl.IsImplemented("GevSCPSPacketSize"))
                    {
                        // 获取当前网络环境的最优包长值
                        uint nPacketSize = _cameraControl.IGXStream.GetOptimalPacketSize();
                        // 将最优包长值设置为当前设备的流通道包长值
                        _cameraControl.IGXFeatureControl.GetIntFeature("GevSCPSPacketSize").SetValue(nPacketSize);
                    }


                    _cameraControl.IGXStream?.StartGrab();
                    _cameraControl.IGXFeatureControl?.GetCommandFeature("AcquisitionStart").Execute();
                    _cameraControl.IsOpen = true;

                }
            }
            catch (CGalaxyException objError)
            {
                if ((int)GX_STATUS_LIST.GX_STATUS_INVALID_ACCESS == objError.GetErrorCode()
                    && !string.IsNullOrEmpty(_cameraControl.CameraMac))
                {
                    // _cameraControl.IGxFactory?.GigEResetDevice(_cameraControl.CameraMac, GX_RESET_DEVICE_MODE.GX_MANUFACTURER_SPECIFIC_RESET);
                    // 开发文档建议 1s 实测1s不行
                    Thread.Sleep(6000);
                }
                _cameraControl.IsOpen = false;
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
            }
        }



        protected override void UninitDevice()
        {
            if (null == _cameraControl
                || !_cameraControl.IsOpen)
            {
                return;
            }

            StopCapture();

            lock (_cameraLock)
            {
                if (!_cameraControl.IsOpen)
                {
                    return;
                }

                try
                {
                    _cameraControl.IGXFeatureControl?.GetCommandFeature("AcquisitionStop").Execute();
                    _cameraControl.IGXFeatureControl = null;
                }
                catch (CGalaxyException objError)
                {
                    testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                }

                try
                {
                    if (null != _cameraControl.IGXStream)
                    {
                        _cameraControl.IGXStream.StopGrab();
                        _cameraControl.IGXStream.Close();
                        _cameraControl.IGXStream = null;
                    }

                    _cameraControl.IGXStreamFeatureControl = null;
                }
                catch (CGalaxyException objError)
                {
                    testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                }


                try
                {
                    if (null != _cameraControl.IGXDevice)
                    {
                        _cameraControl.IGXDevice.Close();
                        _cameraControl.IGXDevice = null;
                    }
                }
                catch (CGalaxyException objError)
                {
                    testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                }

                ReleaseBuffer();
                _cameraControl.IsOpen = false;
            }
        }

        private bool FrameCallback(IBaseData objIFrameData)
        {
            try
            {
                if (null == objIFrameData
                    || null == _byteMonoBuffer
                    || null == _cameraControl
                    || null == _cameraControl.IGXImageFormatConvert)
                {
                    return false;
                }
                lock (_callbackImageLock)
                {
                    UpdateBufferSize(objIFrameData);
                    Marshal.Copy(objIFrameData.GetBuffer(), _byteMonoBuffer, 0, ImageWidth * _imageByte * ImageHeight);
                    return true;
                    /*if (null != _captureBitmap)
                    {
                        return UpdateBitmap(ref _captureBitmap, _byteMonoBuffer);
                    }
                    else
                    {
                        return false;
                    }*/
                }
            }
            catch (CGalaxyException objError)
            {
                testLogger.Error("error code:" + objError.GetErrorCode().ToString() + "error Message:" + objError.Message);
                return false;
            }

        }

        private void ConvertImageFormat(IBaseData objIFrameData, GX_PIXEL_FORMAT_ENTRY emPixelFormat, GX_VALID_BIT_LIST emValidBits)
        {
            if (null == objIFrameData
                    || null == _byteMonoBuffer
                    || null == _cameraControl
                    || null == _cameraControl.IGXImageFormatConvert)
            {
                return;
            }
            // 设置目标像素格式
            _cameraControl.IGXImageFormatConvert.SetDstFormat(emPixelFormat);

            // 获取目标像素格式Buffer大小
            UInt64 i64DstBufferize = _cameraControl.IGXImageFormatConvert.GetBufferSizeForConversion(objIFrameData);

            // 设置目标有效位数
            _cameraControl.IGXImageFormatConvert.SetValidBits(emValidBits);

            // 进行图像格式转换
            _cameraControl.IGXImageFormatConvert.Convert(objIFrameData, _ptrOutBuffer, i64DstBufferize, false);

            Marshal.Copy(_ptrOutBuffer, _byteMonoBuffer, 0, ImageWidth * ImageHeight);
        }

        private void CreateBuffer(int nWidth, int nHeight)
        {
            try
            {
                lock (_bufferLock)
                {
                    if (8 == GetCameraColorMode())
                    {
                        _imageByte = 1;
                        _ptrOutBuffer = Marshal.AllocCoTaskMem(nWidth * _imageByte * nHeight);
                        _byteMonoBuffer = new byte[nWidth * _imageByte * nHeight];
                        // CreateBitmap(out _captureBitmap, nWidth, nHeight, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);

                    }
                    else if (12 == GetCameraColorMode())
                    {
                        _imageByte = 2;
                        _ptrOutBuffer = Marshal.AllocCoTaskMem(nWidth * _imageByte * nHeight);
                        _byteMonoBuffer = new byte[nWidth * _imageByte * nHeight];
                        // CreateBitmap(out _captureBitmap, nWidth, nHeight, System.Drawing.Imaging.PixelFormat.Format16bppGrayScale);

                    }
                }
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }

        }

        private void ReleaseBuffer()
        {
            try
            {
                lock (_bufferLock)
                {
                    if (IntPtr.Zero != _ptrOutBuffer)
                    {
                        Marshal.FreeCoTaskMem(_ptrOutBuffer);
                        _ptrOutBuffer = IntPtr.Zero;
                    }

                    _byteMonoBuffer = null;
                    _captureBitmap?.Dispose();
                }
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }

        }

        private void CreateBitmap(out Bitmap bitmap, int nWidth, int nHeight, System.Drawing.Imaging.PixelFormat pixelFormat) => bitmap = new Bitmap(nWidth, nHeight, pixelFormat);

        private bool UpdateBitmap(ref Bitmap bitmap, byte[] byBuffer)
        {
            if (bitmap is null)
            {
                return false;
            }
            BitmapData? bmpData = null;
            try
            {
                bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
                IntPtr ptrBmp = bmpData.Scan0;

                if (byBuffer.Length <= bmpData.Stride * bitmap.Height)
                {
                    Marshal.Copy(byBuffer, 0, ptrBmp, bmpData.Stride * bitmap.Height);
                    return true;
                }

                /*if (bitmap.Width == bmpData.Stride)
                {
                    Marshal.Copy(byBuffer, 0, ptrBmp, bmpData.Stride * bitmap.Height);
                }
                else
                {
                    for (int i = 0; i < bitmap.Height; ++i)
                    {
                        Marshal.Copy(byBuffer, i * bmpData.Stride, new IntPtr(ptrBmp.ToInt64() + i * bmpData.Stride), bmpData.Stride);
                    }
                }*/


                return false;
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
                return false;
            }
            finally
            {
                if (bmpData is not null)
                {
                    bitmap.UnlockBits(bmpData);
                }
            }

        }

        private void UpdateBufferSize(IBaseData objIBaseData)
        {
            if (null != objIBaseData)
            {
                if (ImageWidth != (int)objIBaseData.GetWidth()
                    || ImageHeight != (int)objIBaseData.GetHeight())
                {
                    ImageWidth = (int)objIBaseData.GetWidth();
                    ImageHeight = (int)objIBaseData.GetHeight();

                    ReleaseBuffer();
                    CreateBuffer(ImageWidth, ImageHeight);
                }
            }
        }


        private GX_VALID_BIT_LIST GetBestValudBit(GX_PIXEL_FORMAT_ENTRY emPixelFormatEntry)
        {
            GX_VALID_BIT_LIST emValidBits = GX_VALID_BIT_LIST.GX_BIT_0_7;
            switch (emPixelFormatEntry)
            {
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO8:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR8:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG8:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB8:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG8:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_RGB8:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BGR8:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_R8:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_G8:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_B8:
                    {
                        emValidBits = GX_VALID_BIT_LIST.GX_BIT_0_7;
                        break;
                    }
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10_PACKED:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR10:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG10:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB10:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG10:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG10_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG10_PACKED:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB10_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB10_PACKED:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR10_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR10_PACKED:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG10_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG10_PACKED:
                    {
                        emValidBits = GX_VALID_BIT_LIST.GX_BIT_2_9;
                        break;
                    }
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12_PACKED:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR12:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG12:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB12:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG12:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG12_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG12_PACKED:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB12_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB12_PACKED:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR12_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR12_PACKED:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG12_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG12_PACKED:
                    {
                        emValidBits = GX_VALID_BIT_LIST.GX_BIT_4_11;
                        break;
                    }
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR14:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG14:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB14:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG14:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG14_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB14_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR14_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG14_P:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14_P:
                    {
                        emValidBits = GX_VALID_BIT_LIST.GX_BIT_6_13;
                        break;
                    }
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO16:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR16:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG16:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB16:
                case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG16:
                    {
                        emValidBits = GX_VALID_BIT_LIST.GX_BIT_8_15;
                        break;
                    }
                default:
                    break;
            }
            return emValidBits;
        }

        private void GetConvertPixelFormat(string strPixelFormat, ref uint nPixelFormatValue) => nPixelFormatValue = strPixelFormat switch
        {
            "Mono8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO8,
            "BayerRG8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG8,
            "BayerGB8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB8,
            "BayerGR8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR8,
            "BayerBG8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG8,
            "RGB8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_RGB8,
            "BGR8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BGR8,
            "Mono10" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10,
            "Mono10_Packed" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10_PACKED,
            "Mono10_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10_P,
            "BayerRG10" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG10,
            "BayerRG10_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG10_P,
            "BayerRG10_Packet" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG10_PACKED,
            "BayerGB10" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB10,
            "BayerGB10_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB10_P,
            "BayerGB10_Packet" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB10_PACKED,
            "BayerGR10" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR10,
            "BayerGR10_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR10_P,
            "BayerGR10_Packet" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR10_PACKED,
            "BayerBG10" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG10,
            "BayerBG10_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG10_P,
            "BayerBG10_Packet" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG10_PACKED,
            "Mono12" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12,
            "Mono12_Packed" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12_PACKED,
            "Mono12_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12_P,
            "BayerRG12" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG12,
            "BayerRG12_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG12_P,
            "BayerRG12_Packet" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG12_PACKED,
            "BayerGB12" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB12,
            "BayerGB12_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB12_P,
            "BayerGB12_Packet" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB12_PACKED,
            "BayerGR12" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR12,
            "BayerGR12_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR12_P,
            "BayerGR12_Packet" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR12_PACKED,
            "BayerBG12" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG12,
            "BayerBG12_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG12_P,
            "BayerBG12_Packet" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG12_PACKED,
            "Mono14" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14,
            "Mono14_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14_P,
            "BayerRG14" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG14,
            "BayerRG14_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG14_P,
            "BayerGB14" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB14,
            "BayerGB14_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB14_P,
            "BayerGR14" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR14,
            "BayerGR14_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR14_P,
            "BayerBG14" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG14,
            "BayerBG14_P" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG14_P,
            "Mono16" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO16,
            "BayerRG16" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_RG16,
            "BayerGB16" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GB16,
            "BayerGR16" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_GR16,
            "BayerBG16" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BAYER_BG16,
            "R8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_R8,
            "B8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_B8,
            "G8" => (uint)GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_G8,
            _ => 0,
        };

        #endregion


        #region 成员变量
        private GxCameraControl _cameraControl = new()
        {
            IGxFactory = IGXFactory.GetInstance()
        };

        private readonly List<IGXDeviceInfo> _listIGXDeviceInfo = [];

        private int _nComId = -1;
        private string? _strSN;
        private string? _strMac;

        private int _captureInterLock = 0;

        private readonly object _cameraLock = new();

        private readonly object _callbackImageLock = new();

        private readonly object _bufferLock = new();

        private readonly object _captureLock = new();

        private byte[]? _byteMonoBuffer;

        private IntPtr _ptrOutBuffer = IntPtr.Zero;

        private Bitmap? _captureBitmap;
        private int _imageByte = 2;

        private const uint PIXEL_FORMATE_BIT = 0x00FF0000;          ///<用于与当前的数据格式进行与运算得到当前的数据位数

        private const uint GX_PIXEL_8BIT = 0x00080000;              ///<8位数据图像格式


        private event CallbackBitmapDelegate<byte[], bool>? OnCallbackBitmap;

        private CancellationTokenSource? _cancellationTokenSource;

        private static class STREAM_BUFFER_HANDING_MODE
        {
            public static string OldestFist = "OldestFirst";                      // 默认值。图像缓冲区遵守先进先出的原则，
                                                                                  // 所有的缓冲区全部填满后，新的图像数据会被丢弃，
                                                                                  // 直到用户完成已经填满图像数据的缓冲区处理。

            public static string OldestFirstOverwrite = "OldestFirstOverwrite";   //  同样遵守先进先出的原则。与OldestFirst模式的区别是，
                                                                                  //  当所有的缓冲区全部填满后,SDK将主动丢弃缓冲区中时间戳最旧的一帧图像缓冲区.

            public static string NewestOnly = "NewestOnly";                       // 该模式下用户拿到的始终是SDK接收到的最新图。
                                                                                  // SDK每接收到一帧新的图像数据,就会主动丢弃旧时间戳的图像，
                                                                                  // 因此当图像处理不及时或者速度较慢时,就会出现丢帧。
        }
        #endregion
    }



    public class GxCameraControl
    {
        public IGXFactory? IGxFactory;
        public IGXDevice? IGXDevice;                                                                  /// 设备对像
        public IGXStream? IGXStream;                                                                  /// 流对像
        public IGXFeatureControl? IGXFeatureControl;                                                  /// 远端设备属性控制器对像
        public IGXFeatureControl? IGXStreamFeatureControl;                                            /// 流层属性控制器对象
        public IImageProcessConfig? IImageProcessConfig;
        public IGXImageFormatConvert? IGXImageFormatConvert;                                          /// IGXImageFormatConvert对象，仅供图像格式转换使用
        public string CameraDisplayName = string.Empty;                                               /// 设备显示名称
        public string CameraSN = string.Empty;                                                        /// 序列号
        public string CameraMac = string.Empty;                                                       /// Mac地址
        public bool IsColorFilter = false;                                                            /// 判断是否为彩色相机
        public bool IsOpen = false;                                                                   /// 相机已打开标志
        public bool IsSnap = false;                                                                   /// 相机正在采集标志
        public GX_DEVICE_CLASS_LIST CameraDeviceType = GX_DEVICE_CLASS_LIST.GX_DEVICE_CLASS_UNKNOWN;  /// 设备类型
    }
}
