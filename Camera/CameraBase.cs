using static VegaBeamTool.Camera.ICameraDevice;

namespace VegaBeamTool.Camera
{
    public abstract class CameraBase : ICameraDevice, IDisposable
    {

        public abstract void Start();

        public abstract void Stop();

        protected abstract void InitDevice();

        protected abstract void UninitDevice();
        public abstract void SetCameraCom(int nDeviceIndex);
        public abstract void SetCameraSN(string strSN);
        public abstract void SetCameraMac(string strMac);
        public abstract bool Snapshot(out byte[]? byteImage);
        public abstract bool Capture();
        public abstract void StopCapture();
        public abstract int SetCameraGain(int gains);
        public abstract int GetCameraGain();
        public abstract int GetCameraGainMin();
        public abstract int GetCameraGainMax();
        public abstract int SetCameraColorMode(string strColorMode);
        public abstract int GetCameraColorMode();
        public abstract int SetCameraExposureTime(int times);
        public abstract int GetCameraExposureTime();
        public abstract int GetCameraExposureTimeMin();
        public abstract int GetCameraExposureTimeMax();
        public abstract bool GetCameraStartSatuts();
        public abstract void RegisterCallbackBitmap(CallbackBitmapDelegate<byte[], bool> callbackBitmap);
        public abstract void UnRegisterCallbackBitmap(CallbackBitmapDelegate<byte[], bool> callbackBitmap);
        public abstract CameraVendor Vendor { get; }
        public abstract IReadOnlyList<CameraDeviceInfo> EnumerateDevices();
        public abstract void SelectDevice(CameraDeviceInfo device);
        public int ImageWidth { set; get; } = 2484;
        public int ImageHeight { set; get; } = 2484;

        /// <summary>厂商运行库是否就绪，未就绪时界面应提示先安装 SDK 而不是报"打开失败"。</summary>
        public virtual bool IsSdkAvailable => true;

        /// <summary>SDK 缺失时给用户看的补救说明。</summary>
        public virtual string SdkMissingHint => string.Empty;

        /// <summary>
        /// 释放厂商 SDK 资源。切换相机型号时上层必须调用，否则 SDK 句柄不会归还。
        /// </summary>
        public virtual void Dispose() => GC.SuppressFinalize(this);
    }
}
