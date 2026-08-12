using Client.Util;
using System.Runtime.InteropServices;
using TUCAMERA;
using VegaBeamTool.Camera.Tucsen;
using static VegaBeamTool.Camera.ICameraDevice;

namespace VegaBeamTool.Camera
{
    /// <summary>
    /// Tucsen(TUCam) 相机实现，对外行为与 <see cref="Mercury3Camera"/> 对齐：
    /// 曝光时间统一以微秒进出，图像统一以 16bit 单色小端字节流回调，
    /// 这样 BeamProcessor 与整个界面层无需感知相机型号。
    /// </summary>
    public class TucsenCamera : CameraBase
    {
        #region public对外接口
        public TucsenCamera() => TucsenSdkLoader.EnsureResolverRegistered();

        public override CameraVendor Vendor => CameraVendor.TucsenLiraUV;

        public override bool IsSdkAvailable => TucsenSdkLoader.TryLocateSdk(out _);

        public override string SdkMissingHint =>
            $"{TucsenSdkLoader.NativeLibraryName} not found.\r\n" +
            "Please install the Tucsen TUCam SDK (x64), or set the environment variable TUCAM_SDK_PATH " +
            "to the directory containing TUCam.dll.";

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

        public override void SetCameraCom(int nDeviceIndex) => _nDeviceIndex = nDeviceIndex;

        public override void SetCameraSN(string strSN) => _strSN = strSN;

        // Tucsen 走 USB/CXP 接口，没有 GigE MAC 的概念
        public override void SetCameraMac(string strMac) { }

        public override void SelectDevice(CameraDeviceInfo device)
        {
            _nDeviceIndex = device.Index;
            _strSN = device.SerialNumber;
        }

        public override bool GetCameraStartSatuts() => _bIsOpen;

        public override IReadOnlyList<CameraDeviceInfo> EnumerateDevices()
        {
            List<CameraDeviceInfo> listDevice = [];

            lock (_cameraLock)
            {
                if (_bIsOpen)
                {
                    testLogger.Warn("Cannot enumerate Tucsen devices while a camera is open.");
                    return listDevice;
                }

                if (!TucsenApi.Refresh(out uint uiCameraCount))
                {
                    return listDevice;
                }

                for (uint uiIndex = 0; uiIndex < uiCameraCount; uiIndex++)
                {
                    string strModel = ReadDeviceModel(uiIndex);
                    listDevice.Add(new CameraDeviceInfo
                    {
                        Vendor = CameraVendor.TucsenLiraUV,
                        Index = (int)uiIndex,
                        // SN 需要打开设备后经寄存器读取，枚举阶段拿不到
                        SerialNumber = string.Empty,
                        Model = strModel,
                        DisplayName = $"[{uiIndex}] {strModel}",
                    });
                }
            }

            return listDevice;
        }

        public override bool Snapshot(out byte[]? byteImage)
        {
            byteImage = null;

            if (!_bIsOpen)
            {
                testLogger.Error("Snapshot fail, TucsenCamera is closed.");
                return false;
            }

            StopCapture();

            int nReGetCount = 3;
            do
            {
                if (GetImageByWaitForFrame(out byteImage))
                {
                    return true;
                }
                nReGetCount--;
                Thread.Sleep(50);
            }
            while (nReGetCount > 0);

            return false;
        }

        public override bool Capture()
        {
            if (Interlocked.Exchange(ref _captureInterLock, 1) != 0)
            {
                return false;
            }

            try
            {
                if (!_bIsOpen)
                {
                    return false;
                }

                CancellationTokenSource curTokenSource;
                lock (_captureLock)
                {
                    if (_bIsSnap)
                    {
                        return true;
                    }
                    _bIsSnap = true;
                    _cancellationTokenSource = new();
                    curTokenSource = _cancellationTokenSource;
                }

                _ = Task.Run(() =>
                {
                    try
                    {
                        // WaitForFrame 自身阻塞到新帧到达，无需额外节流
                        while (!curTokenSource.IsCancellationRequested && _bIsOpen)
                        {
                            if (GetImageByWaitForFrame(out byte[]? byteImage) && null != byteImage)
                            {
                                _ = (OnCallbackBitmap?.Invoke(byteImage, false));
                            }
                            else
                            {
                                Thread.Sleep(50);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        testLogger.Error(ex.Message, ex);
                        StopCapture();
                    }
                    finally
                    {
                        lock (_captureLock)
                        {
                            curTokenSource.Dispose();
                        }
                    }
                });
                return true;
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
                return false;
            }
            finally
            {
                _ = Interlocked.Exchange(ref _captureInterLock, 0);
            }
        }

        public override void StopCapture()
        {
            if (!_bIsOpen)
            {
                return;
            }

            lock (_captureLock)
            {
                if (!_bIsSnap)
                {
                    return;
                }
                _bIsSnap = false;
                _cancellationTokenSource?.Cancel();
            }

            // 必须在 _cameraLock 之外唤醒阻塞中的 WaitForFrame，否则采集线程会一直占着锁
            AbortWait();
        }

        public override int SetCameraGain(int nGain)
        {
            lock (_cameraLock)
            {
                if (!_bIsOpen
                    || !TryGetPropertyAttr(TUCAM_IDPROP.TUIDP_GLOBALGAIN, out TUCAM_PROP_ATTR attr))
                {
                    return -1;
                }

                double dGain = Math.Clamp(nGain, attr.dbValMin, attr.dbValMax);
                TUCAMRET ret = TUCamera.TUCAM_Prop_SetValue(_hCamera, (int)TUCAM_IDPROP.TUIDP_GLOBALGAIN, dGain);
                if (!IsSuccess(ret))
                {
                    testLogger.Error($"Set TUIDP_GLOBALGAIN failed, ret:0x{(uint)ret:X}");
                    return -1;
                }
                return 0;
            }
        }

        public override int GetCameraGain() => GetPropertyAsInt(TUCAM_IDPROP.TUIDP_GLOBALGAIN, PropertyBound.Current);

        public override int GetCameraGainMin() => GetPropertyAsInt(TUCAM_IDPROP.TUIDP_GLOBALGAIN, PropertyBound.Min);

        public override int GetCameraGainMax() => GetPropertyAsInt(TUCAM_IDPROP.TUIDP_GLOBALGAIN, PropertyBound.Max);

        /// <summary>入参为微秒，与大恒实现保持一致；TUCam 属性本身以毫秒为单位。</summary>
        public override int SetCameraExposureTime(int nTimes)
        {
            lock (_cameraLock)
            {
                if (!_bIsOpen
                    || !TryGetPropertyAttr(TUCAM_IDPROP.TUIDP_EXPOSURETM, out TUCAM_PROP_ATTR attr))
                {
                    return -1;
                }

                double dExposureMs = Math.Clamp(nTimes / MicrosecondsPerMillisecond, attr.dbValMin, attr.dbValMax);
                TUCAMRET ret = TUCamera.TUCAM_Prop_SetValue(_hCamera, (int)TUCAM_IDPROP.TUIDP_EXPOSURETM, dExposureMs);
                if (!IsSuccess(ret))
                {
                    testLogger.Error($"Set TUIDP_EXPOSURETM failed, ret:0x{(uint)ret:X}");
                    return -1;
                }

                _nExposureTimeUs = ToMicroseconds(dExposureMs);
                return 0;
            }
        }

        public override int GetCameraExposureTime()
            => ToMicroseconds(GetPropertyAsDouble(TUCAM_IDPROP.TUIDP_EXPOSURETM, PropertyBound.Current));

        public override int GetCameraExposureTimeMin()
            => ToMicroseconds(GetPropertyAsDouble(TUCAM_IDPROP.TUIDP_EXPOSURETM, PropertyBound.Min));

        public override int GetCameraExposureTimeMax()
            => ToMicroseconds(GetPropertyAsDouble(TUCAM_IDPROP.TUIDP_EXPOSURETM, PropertyBound.Max));

        /// <summary>大恒用 "Mono8"/"Mono12" 这类像素格式名，这里换算成 TUCam 的位深档位。</summary>
        public override int SetCameraColorMode(string strColorMode)
        {
            lock (_cameraLock)
            {
                if (!_bIsOpen
                    || !TryGetCapabilityAttr(TUCAM_IDCAPA.TUIDC_BITOFDEPTH, out TUCAM_CAPA_ATTR attr))
                {
                    return -1;
                }

                int nValue = ParseDepthBits(strColorMode) > 8 ? attr.nValMax : attr.nValMin;
                TUCAMRET ret = TUCamera.TUCAM_Capa_SetValue(_hCamera, (int)TUCAM_IDCAPA.TUIDC_BITOFDEPTH, nValue);
                if (!IsSuccess(ret))
                {
                    testLogger.Error($"Set TUIDC_BITOFDEPTH failed, ret:0x{(uint)ret:X}");
                    return -1;
                }
                return 1;
            }
        }

        /// <summary>返回当前每像素有效位数，与大恒实现语义一致。</summary>
        public override int GetCameraColorMode()
        {
            lock (_cameraLock)
            {
                if (!_bIsOpen)
                {
                    return -1;
                }
                return 2 == _frame.ucElemBytes ? 16 : 8;
            }
        }

        public override void RegisterCallbackBitmap(CallbackBitmapDelegate<byte[], bool> callbackBitmap)
            => OnCallbackBitmap += callbackBitmap;

        public override void UnRegisterCallbackBitmap(CallbackBitmapDelegate<byte[], bool> callbackBitmap)
            => OnCallbackBitmap -= callbackBitmap;

        public override void Dispose()
        {
            Stop();
            TucsenApi.Shutdown();
            base.Dispose();
        }
        #endregion


        #region protected/private
        protected override void InitDevice()
        {
            lock (_cameraLock)
            {
                if (_bIsOpen)
                {
                    testLogger.Debug("Camera is Open");
                    return;
                }

                if (!TucsenApi.Initialize(out uint uiCameraCount))
                {
                    return;
                }

                if (uiCameraCount < 1)
                {
                    testLogger.Error("Get Tucsen device counts 0");
                    return;
                }

                if (_nDeviceIndex < 0 || _nDeviceIndex >= uiCameraCount)
                {
                    testLogger.Error($"Invalid Tucsen device index {_nDeviceIndex}, detected {uiCameraCount} camera(s).");
                    return;
                }

                TUCAM_OPEN openParam = new()
                {
                    uiIdxOpen = (uint)_nDeviceIndex,
                    hIdxTUCam = IntPtr.Zero,
                };

                TUCAMRET ret = TUCamera.TUCAM_Dev_Open(ref openParam);
                if (!IsSuccess(ret) || IntPtr.Zero == openParam.hIdxTUCam)
                {
                    testLogger.Error($"TUCAM_Dev_Open failed, index:{_nDeviceIndex}, ret:0x{(uint)ret:X}");
                    return;
                }

                _hCamera = openParam.hIdxTUCam;
                _strModel = ReadCameraModel();
                _strSN = ReadCameraSerialNumber();
                testLogger.Info($"Opened Tucsen camera [{_nDeviceIndex}] {_strModel}, SN:{_strSN}");

                ConfigureForBeamAnalysis();

                if (!AllocateBuffer())
                {
                    CloseHandleUnsafe();
                    return;
                }

                ret = TUCamera.TUCAM_Cap_Start(_hCamera, (uint)TUCAM_CAPTURE_MODES.TUCCM_SEQUENCE);
                if (!IsSuccess(ret))
                {
                    testLogger.Error($"TUCAM_Cap_Start failed, ret:0x{(uint)ret:X}");
                    _ = TUCamera.TUCAM_Buf_Release(_hCamera);
                    CloseHandleUnsafe();
                    return;
                }

                _bIsCapStarted = true;
                _bIsOpen = true;
            }

            // Buf_Alloc 未必填好分辨率，取一帧确定真实的宽高与像素字节数
            _ = GetImageByWaitForFrame(out _);
        }

        protected override void UninitDevice()
        {
            if (!_bIsOpen)
            {
                return;
            }

            StopCapture();
            AbortWait();

            lock (_cameraLock)
            {
                if (!_bIsOpen)
                {
                    return;
                }

                try
                {
                    if (_bIsCapStarted)
                    {
                        _ = TUCamera.TUCAM_Cap_Stop(_hCamera);
                        _bIsCapStarted = false;
                    }
                    _ = TUCamera.TUCAM_Buf_Release(_hCamera);
                }
                catch (Exception ex)
                {
                    testLogger.Error(ex.Message, ex);
                }

                CloseHandleUnsafe();
                _frame = default;
                _bIsOpen = false;
            }
        }

        private void ConfigureForBeamAnalysis()
        {
            // 自动曝光开着时手动曝光值不生效
            SetCapability(TUCAM_IDCAPA.TUIDC_ATEXPOSURE, 0);

            // 光束分析链路按 16bit 单色处理，这里选相机支持的最高位深
            if (TryGetCapabilityAttr(TUCAM_IDCAPA.TUIDC_BITOFDEPTH, out TUCAM_CAPA_ATTR depthAttr))
            {
                SetCapability(TUCAM_IDCAPA.TUIDC_BITOFDEPTH, depthAttr.nValMax);
            }

            // 自由运行采集，与大恒的 AcquisitionStart 行为对齐
            TUCAM_TRIGGER_ATTR triggerAttr = new();
            if (IsSuccess(TUCamera.TUCAM_Cap_GetTrigger(_hCamera, ref triggerAttr)))
            {
                triggerAttr.nTgrMode = (int)TUCAM_CAPTURE_MODES.TUCCM_SEQUENCE;
                _ = TUCamera.TUCAM_Cap_SetTrigger(_hCamera, triggerAttr);
            }

            _nExposureTimeUs = ToMicroseconds(GetPropertyAsDoubleUnsafe(TUCAM_IDPROP.TUIDP_EXPOSURETM));
        }

        private bool AllocateBuffer()
        {
            _frame = new TUCAM_FRAME
            {
                pBuffer = IntPtr.Zero,
                ucFormatGet = (byte)TUFRM_FORMATS.TUFRM_FMT_USUAl,
                uiRsdSize = 1,
            };

            TUCAMRET ret = TUCamera.TUCAM_Buf_Alloc(_hCamera, ref _frame);
            if (!IsSuccess(ret))
            {
                testLogger.Error($"TUCAM_Buf_Alloc failed, ret:0x{(uint)ret:X}");
                return false;
            }

            if (_frame.usWidth > 0 && _frame.usHeight > 0)
            {
                UpdateImageSize(_frame.usWidth, _frame.usHeight);
            }
            return true;
        }

        private bool GetImageByWaitForFrame(out byte[]? byteImage)
        {
            byteImage = null;

            lock (_cameraLock)
            {
                if (!_bIsOpen)
                {
                    return false;
                }

                TUCAMRET ret = TUCAM_Buf_WaitForFrameTimeout(_hCamera, ref _frame, GetFrameTimeoutMs());
                if (!IsSuccess(ret))
                {
                    testLogger.Warn($"TUCAM_Buf_WaitForFrame failed, ret:0x{(uint)ret:X}");
                    return false;
                }

                return TryCopyFrameAsMono16(out byteImage);
            }
        }

        /// <summary>把 TUCam 帧统一整形成下游要求的 16bit 单色小端字节流。</summary>
        private bool TryCopyFrameAsMono16(out byte[]? byteImage)
        {
            byteImage = null;

            int nWidth = _frame.usWidth;
            int nHeight = _frame.usHeight;
            if (nWidth <= 0 || nHeight <= 0 || IntPtr.Zero == _frame.pBuffer)
            {
                return false;
            }

            UpdateImageSize(nWidth, nHeight);

            int nPixelCount = nWidth * nHeight;
            IntPtr ptrSource = IntPtr.Add(_frame.pBuffer, _frame.usHeader);
            byte[] byteResult = new byte[nPixelCount * MonoBytesPerPixel];

            if (1 == _frame.ucChannels && 2 == _frame.ucElemBytes)
            {
                Marshal.Copy(ptrSource, byteResult, 0, Math.Min(byteResult.Length, (int)_frame.uiImgSize));
            }
            else if (1 == _frame.ucChannels && 1 == _frame.ucElemBytes)
            {
                // 相机工作在 8bit 时补齐高字节，灰度量级保持 0~255 以便像素值显示直观
                byte[] byteRaw = new byte[nPixelCount];
                Marshal.Copy(ptrSource, byteRaw, 0, Math.Min(byteRaw.Length, (int)_frame.uiImgSize));
                for (int nPixel = 0; nPixel < nPixelCount; nPixel++)
                {
                    byteResult[nPixel * MonoBytesPerPixel] = byteRaw[nPixel];
                }
            }
            else
            {
                LogUnsupportedFormat();
                return false;
            }

            byteImage = byteResult;
            return true;
        }

        private void LogUnsupportedFormat()
        {
            if (_bUnsupportedFormatLogged)
            {
                return;
            }
            _bUnsupportedFormatLogged = true;
            testLogger.Error($"Unsupported Tucsen frame format: channels={_frame.ucChannels}, elemBytes={_frame.ucElemBytes}. " +
                             "Beam analysis requires a monochrome 8/16bit stream.");
        }

        private void UpdateImageSize(int nWidth, int nHeight)
        {
            if (ImageWidth == nWidth && ImageHeight == nHeight)
            {
                return;
            }
            ImageWidth = nWidth;
            ImageHeight = nHeight;
        }

        /// <summary>长曝光下不能用固定超时，否则会被误判成取图失败。</summary>
        private int GetFrameTimeoutMs()
        {
            long lExposureMs = _nExposureTimeUs / (long)MicrosecondsPerMillisecond;
            return (int)Math.Clamp(lExposureMs * 2 + MinFrameTimeoutMs, MinFrameTimeoutMs, MaxFrameTimeoutMs);
        }

        private void AbortWait()
        {
            if (IntPtr.Zero == _hCamera)
            {
                return;
            }

            try
            {
                _ = TUCamera.TUCAM_Buf_AbortWait(_hCamera);
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }
        }

        private void CloseHandleUnsafe()
        {
            if (IntPtr.Zero == _hCamera)
            {
                return;
            }

            try
            {
                _ = TUCamera.TUCAM_Dev_Close(_hCamera);
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }
            _hCamera = IntPtr.Zero;
        }

        private void SetCapability(TUCAM_IDCAPA idCapa, int nValue)
        {
            TUCAMRET ret = TUCamera.TUCAM_Capa_SetValue(_hCamera, (int)idCapa, nValue);
            if (!IsSuccess(ret))
            {
                // 部分型号不支持某些能力项，属于正常情况，只记录不阻断
                testLogger.Warn($"Set capability {idCapa} to {nValue} failed, ret:0x{(uint)ret:X}");
            }
        }

        private bool TryGetCapabilityAttr(TUCAM_IDCAPA idCapa, out TUCAM_CAPA_ATTR attr)
        {
            attr = new TUCAM_CAPA_ATTR { idCapa = (int)idCapa };
            TUCAMRET ret = TUCamera.TUCAM_Capa_GetAttr(_hCamera, ref attr);
            if (!IsSuccess(ret))
            {
                testLogger.Warn($"TUCAM_Capa_GetAttr({idCapa}) failed, ret:0x{(uint)ret:X}");
                return false;
            }
            return true;
        }

        private bool TryGetPropertyAttr(TUCAM_IDPROP idProp, out TUCAM_PROP_ATTR attr)
        {
            attr = new TUCAM_PROP_ATTR { idProp = (int)idProp, nIdxChn = 0 };
            TUCAMRET ret = TUCamera.TUCAM_Prop_GetAttr(_hCamera, ref attr);
            if (!IsSuccess(ret))
            {
                testLogger.Warn($"TUCAM_Prop_GetAttr({idProp}) failed, ret:0x{(uint)ret:X}");
                return false;
            }
            return true;
        }

        private double GetPropertyAsDouble(TUCAM_IDPROP idProp, PropertyBound bound)
        {
            lock (_cameraLock)
            {
                if (!_bIsOpen)
                {
                    return InvalidValue;
                }

                if (PropertyBound.Current == bound)
                {
                    return GetPropertyAsDoubleUnsafe(idProp);
                }

                if (!TryGetPropertyAttr(idProp, out TUCAM_PROP_ATTR attr))
                {
                    return InvalidValue;
                }
                return PropertyBound.Min == bound ? attr.dbValMin : attr.dbValMax;
            }
        }

        private double GetPropertyAsDoubleUnsafe(TUCAM_IDPROP idProp)
        {
            double dValue = 0.0;
            TUCAMRET ret = TUCamera.TUCAM_Prop_GetValue(_hCamera, (int)idProp, ref dValue);
            if (!IsSuccess(ret))
            {
                testLogger.Warn($"TUCAM_Prop_GetValue({idProp}) failed, ret:0x{(uint)ret:X}");
                return InvalidValue;
            }
            return dValue;
        }

        private int GetPropertyAsInt(TUCAM_IDPROP idProp, PropertyBound bound)
        {
            double dValue = GetPropertyAsDouble(idProp, bound);
            return dValue < 0.0 ? -1 : ClampToInt(dValue);
        }

        private string ReadCameraModel()
        {
            TUCAM_VALUE_INFO info = new()
            {
                nID = (int)TUCAM_IDINFO.TUIDI_CAMERA_MODEL,
                nTextSize = TextBufferSize,
            };

            TUCAMRET ret = TUCamera.TUCAM_Dev_GetInfo(_hCamera, ref info);
            return IsSuccess(ret) ? Marshal.PtrToStringAnsi(info.pText) ?? UnknownModel : UnknownModel;
        }

        private static string ReadDeviceModel(uint uiIndex)
        {
            TUCAM_VALUE_INFO info = new()
            {
                nID = (int)TUCAM_IDINFO.TUIDI_CAMERA_MODEL,
                nTextSize = TextBufferSize,
            };

            TUCAMRET ret = TUCamera.TUCAM_Dev_GetInfoEx(uiIndex, ref info);
            return IsSuccess(ret) ? Marshal.PtrToStringAnsi(info.pText) ?? UnknownModel : UnknownModel;
        }

        private string ReadCameraSerialNumber()
        {
            IntPtr ptrBuffer = Marshal.AllocHGlobal(TextBufferSize);
            try
            {
                Marshal.Copy(new byte[TextBufferSize], 0, ptrBuffer, TextBufferSize);
                TUCAM_REG_RW regRW = new()
                {
                    nRegType = (int)TUREG_FORMATS.TUREG_SN,
                    pBuf = ptrBuffer,
                    nBufSize = TextBufferSize,
                };

                TUCAMRET ret = TUCamera.TUCAM_Reg_Read(_hCamera, regRW);
                return IsSuccess(ret) ? Marshal.PtrToStringAnsi(ptrBuffer)?.Trim() ?? string.Empty : string.Empty;
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
                return string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(ptrBuffer);
            }
        }

        private static bool IsSuccess(TUCAMRET ret) => TUCAMRET.TUCAMRET_SUCCESS == ret;

        private static int ToMicroseconds(double dMilliseconds)
            => dMilliseconds < 0.0 ? -1 : ClampToInt(dMilliseconds * MicrosecondsPerMillisecond);

        private static int ClampToInt(double dValue)
            => dValue >= int.MaxValue ? int.MaxValue : (int)Math.Round(dValue);

        private static int ParseDepthBits(string strColorMode)
        {
            if (string.IsNullOrEmpty(strColorMode))
            {
                return 16;
            }
            if (strColorMode.Contains("16") || strColorMode.Contains("14") || strColorMode.Contains("12") || strColorMode.Contains("10"))
            {
                return 16;
            }
            return strColorMode.Contains('8') ? 8 : 16;
        }

        /// <summary>
        /// 官方封装的 TUCAM_Buf_WaitForFrame 漏掉了原生的超时参数，Cdecl 下会读到栈上的随机值。
        /// 这里显式声明带超时的版本。
        /// </summary>
        [DllImport(TucsenSdkLoader.NativeLibraryName, EntryPoint = "TUCAM_Buf_WaitForFrame", CallingConvention = CallingConvention.Cdecl)]
        private static extern TUCAMRET TUCAM_Buf_WaitForFrameTimeout(IntPtr hTUCam, ref TUCAM_FRAME pFrame, int nTimeOut);

        private enum PropertyBound
        {
            Current,
            Min,
            Max,
        }
        #endregion


        #region 成员变量
        private const double MicrosecondsPerMillisecond = 1000.0;
        private const int MonoBytesPerPixel = 2;
        private const int MinFrameTimeoutMs = 1000;
        private const int MaxFrameTimeoutMs = 60000;
        private const int TextBufferSize = 64;
        private const int InvalidValue = -1;
        private const string UnknownModel = "Unknown";

        private IntPtr _hCamera = IntPtr.Zero;
        private TUCAM_FRAME _frame;

        private int _nDeviceIndex = -1;
        private string _strSN = string.Empty;
        private string _strModel = string.Empty;
        private int _nExposureTimeUs;

        private bool _bIsOpen;
        private bool _bIsSnap;
        private bool _bIsCapStarted;
        private bool _bUnsupportedFormatLogged;

        private int _captureInterLock = 0;

        private readonly object _cameraLock = new();
        private readonly object _captureLock = new();

        private event CallbackBitmapDelegate<byte[], bool>? OnCallbackBitmap;

        private CancellationTokenSource? _cancellationTokenSource;
        #endregion
    }
}
