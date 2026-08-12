using Client.Util;
using System.IO;
using System.Runtime.InteropServices;
using TUCAMERA;

namespace VegaBeamTool.Camera.Tucsen
{
    /// <summary>
    /// TUCAM_Api_Init/Uninit 是进程级的，且接入相机的数量只在 Init 时确定，
    /// 因此重新扫描设备必须走一次 Uninit + Init。这里集中管理这段全局状态。
    /// </summary>
    internal static class TucsenApi
    {
        private const int ApiInitTimeoutMs = 5000;
        private const string ConfigDirectoryName = "TUCamConfig";

        private static readonly object _lockApi = new();
        private static bool _bInitialized;
        private static uint _uiCameraCount;
        private static IntPtr _ptrConfigPath = IntPtr.Zero;

        /// <summary>幂等初始化，已初始化时直接返回缓存的相机数量。</summary>
        public static bool Initialize(out uint uiCameraCount)
        {
            lock (_lockApi)
            {
                if (_bInitialized)
                {
                    uiCameraCount = _uiCameraCount;
                    return true;
                }
                return InitializeUnsafe(out uiCameraCount);
            }
        }

        /// <summary>强制重新枚举。调用方必须保证此刻没有已打开的相机。</summary>
        public static bool Refresh(out uint uiCameraCount)
        {
            lock (_lockApi)
            {
                ShutdownUnsafe();
                return InitializeUnsafe(out uiCameraCount);
            }
        }

        public static void Shutdown()
        {
            lock (_lockApi)
            {
                ShutdownUnsafe();
            }
        }

        private static bool InitializeUnsafe(out uint uiCameraCount)
        {
            uiCameraCount = 0;
            TucsenSdkLoader.EnsureResolverRegistered();

            try
            {
                _ptrConfigPath = Marshal.StringToHGlobalAnsi(EnsureConfigDirectory());
                TUCAM_INIT initParam = new()
                {
                    uiCamCount = 0,
                    pstrConfigPath = _ptrConfigPath,
                };

                TUCAMRET ret = TUCamera.TUCAM_Api_Init(ref initParam, ApiInitTimeoutMs);
                if (TUCAMRET.TUCAMRET_SUCCESS != ret)
                {
                    testLogger.Error($"TUCAM_Api_Init failed, ret:0x{(uint)ret:X}");
                    FreeConfigPath();
                    return false;
                }

                _bInitialized = true;
                _uiCameraCount = initParam.uiCamCount;
                uiCameraCount = _uiCameraCount;
                testLogger.Info($"TUCAM_Api_Init success, camera count:{_uiCameraCount}");
                return true;
            }
            catch (DllNotFoundException ex)
            {
                testLogger.Error($"{TucsenSdkLoader.NativeLibraryName} not found, please install the Tucsen SDK.", ex);
                FreeConfigPath();
                return false;
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
                FreeConfigPath();
                return false;
            }
        }

        private static void ShutdownUnsafe()
        {
            if (!_bInitialized)
            {
                FreeConfigPath();
                return;
            }

            try
            {
                TUCamera.TUCAM_Api_Uninit();
            }
            catch (Exception ex)
            {
                testLogger.Error(ex.Message, ex);
            }

            _bInitialized = false;
            _uiCameraCount = 0;
            FreeConfigPath();
        }

        /// <summary>SDK 在 Init 之后仍可能引用该路径，直到 Uninit 之后才释放。</summary>
        private static void FreeConfigPath()
        {
            if (IntPtr.Zero == _ptrConfigPath)
            {
                return;
            }
            Marshal.FreeHGlobal(_ptrConfigPath);
            _ptrConfigPath = IntPtr.Zero;
        }

        private static string EnsureConfigDirectory()
        {
            string strConfigPath = Path.Combine(AppContext.BaseDirectory, ConfigDirectoryName);
            Directory.CreateDirectory(strConfigPath);
            return strConfigPath;
        }
    }
}
