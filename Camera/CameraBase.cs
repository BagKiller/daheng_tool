using static VegaBeamTool.Camera.ICameraDevice;

namespace VegaBeamTool.Camera
{
    public abstract class CameraBase : ICameraDevice
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
        public int ImageWidth { set; get; } = 2484;
        public int ImageHeight { set; get; } = 2484;
    }
}
