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

    }
}
