namespace VegaBeamTool.Camera
{
    public interface ICameraDevice
    {
        void SetCameraCom(int nDeviceIndex);
        void SetCameraSN(string strSN);
        void SetCameraMac(string strMac);
        bool Snapshot(out byte[]? byteImage);
        bool Capture();
        void StopCapture();
        int GetCameraGain();
        int GetCameraGainMin();
        int GetCameraGainMax();
        int SetCameraGain(int gains);
        int SetCameraColorMode(string str);
        int GetCameraColorMode();
        int GetCameraExposureTime();
        int GetCameraExposureTimeMin();
        int GetCameraExposureTimeMax();
        int SetCameraExposureTime(int times);
        public void RegisterCallbackBitmap(CallbackBitmapDelegate<byte[], bool> callbackBitmap);
        public void UnRegisterCallbackBitmap(CallbackBitmapDelegate<byte[], bool> callbackBitmap);

        public delegate bool CallbackBitmapDelegate<T, T1>(T bitmapImage, T1 isSaveImage);
        public bool GetCameraStartSatuts();
        public int ImageWidth { set; get; }
        public int ImageHeight { set; get; }

        /// <summary>该实现对应的厂商，用于界面区分差异化功能。</summary>
        CameraVendor Vendor { get; }

        /// <summary>扫描当前接入的相机。相机已打开时不应调用。</summary>
        IReadOnlyList<CameraDeviceInfo> EnumerateDevices();

        /// <summary>指定后续 <c>Start()</c> 要打开的设备，各厂商自行决定用 SN 还是索引。</summary>
        void SelectDevice(CameraDeviceInfo device);
    }
}
