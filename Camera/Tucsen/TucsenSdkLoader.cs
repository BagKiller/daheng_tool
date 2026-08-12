using Client.Util;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VegaBeamTool.Camera.Tucsen
{
    /// <summary>
    /// TUCam.dll 随 Tucsen SDK 安装到 Program Files 且不在 PATH 中，默认的 DllImport 查找会失败。
    /// 这里注册解析器按候选目录显式加载；用绝对路径加载可让 TUCam.dll 的同目录依赖
    /// （msvcr120 / msvcp120 / tuimgcv_*）一并解析到。
    /// </summary>
    internal static class TucsenSdkLoader
    {
        public const string NativeLibraryName = "TUCam.dll";

        private static readonly object _lockRegister = new();
        private static bool _bRegistered;

        public static void EnsureResolverRegistered()
        {
            lock (_lockRegister)
            {
                if (_bRegistered)
                {
                    return;
                }
                NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), Resolve);
                _bRegistered = true;
            }
        }

        /// <summary>供界面提示使用：SDK 是否已安装。</summary>
        public static bool TryLocateSdk(out string strDirectory)
        {
            foreach (string strCandidate in GetCandidateDirectories())
            {
                if (File.Exists(Path.Combine(strCandidate, NativeLibraryName)))
                {
                    strDirectory = strCandidate;
                    return true;
                }
            }
            strDirectory = string.Empty;
            return false;
        }

        private static IntPtr Resolve(string strLibraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(strLibraryName, NativeLibraryName, StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            foreach (string strDirectory in GetCandidateDirectories())
            {
                string strFullPath = Path.Combine(strDirectory, NativeLibraryName);
                if (!File.Exists(strFullPath))
                {
                    continue;
                }

                if (NativeLibrary.TryLoad(strFullPath, out IntPtr ptrHandle))
                {
                    testLogger.Info($"Loaded {NativeLibraryName} from {strFullPath}");
                    return ptrHandle;
                }
                testLogger.Warn($"Found {strFullPath} but failed to load it.");
            }

            // 交回默认解析流程，让 CLR 给出更明确的异常信息
            return IntPtr.Zero;
        }

        private static IEnumerable<string> GetCandidateDirectories()
        {
            string? strEnvPath = Environment.GetEnvironmentVariable("TUCAM_SDK_PATH");
            if (!string.IsNullOrWhiteSpace(strEnvPath))
            {
                yield return strEnvPath;
            }

            string strBaseDirectory = AppContext.BaseDirectory;
            yield return Path.Combine(strBaseDirectory, "TUCam", "x64");
            yield return strBaseDirectory;

            string[] strProgramRoots =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ];

            foreach (string strRoot in strProgramRoots)
            {
                if (string.IsNullOrEmpty(strRoot))
                {
                    continue;
                }
                yield return Path.Combine(strRoot, "TUCam_SDK", "runtime", "x64");
                yield return Path.Combine(strRoot, "TUCam_SDK", "runtime", "x64_all");
            }
        }
    }
}
