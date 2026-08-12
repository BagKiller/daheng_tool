using System.Runtime.InteropServices;
using TUCAMERA;
using VegaBeamTool.Camera.Tucsen;

namespace TucsenProbe;

internal static class Program
{
    private const int TextBufferSize = 64;

    [DllImport("TUCam.dll", EntryPoint = "TUCAM_Buf_WaitForFrame", CallingConvention = CallingConvention.Cdecl)]
    private static extern TUCAMRET WaitForFrameTimeout(IntPtr hTUCam, ref TUCAM_FRAME pFrame, int nTimeOut);

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("== step 1: locate SDK ==");
        if (TucsenSdkLoader.TryLocateSdk(out string strSdkDirectory))
        {
            Console.WriteLine($"  found TUCam.dll in: {strSdkDirectory}");
        }
        else
        {
            Console.WriteLine("  TUCam.dll NOT found in any candidate directory");
            return 1;
        }

        Console.WriteLine("== step 2: TUCAM_Api_Init ==");
        if (!TucsenApi.Initialize(out uint uiCameraCount))
        {
            Console.WriteLine("  Api_Init FAILED");
            return 2;
        }
        Console.WriteLine($"  camera count = {uiCameraCount}");

        if (0 == uiCameraCount)
        {
            Console.WriteLine("  no camera connected, stopping after init check");
            TucsenApi.Shutdown();
            return 0;
        }

        Console.WriteLine("== step 3: enumerate models (GetInfoEx) ==");
        for (uint uiIndex = 0; uiIndex < uiCameraCount; uiIndex++)
        {
            TUCAM_VALUE_INFO infoEnum = new()
            {
                nID = (int)TUCAM_IDINFO.TUIDI_CAMERA_MODEL,
                nTextSize = TextBufferSize,
            };
            TUCAMRET retEnum = TUCamera.TUCAM_Dev_GetInfoEx(uiIndex, ref infoEnum);
            Console.WriteLine($"  [{uiIndex}] ret=0x{(uint)retEnum:X} model={Marshal.PtrToStringAnsi(infoEnum.pText)}");
        }

        Console.WriteLine("== step 4: open camera 0 ==");
        TUCAM_OPEN openParam = new() { uiIdxOpen = 0, hIdxTUCam = IntPtr.Zero };
        TUCAMRET ret = TUCamera.TUCAM_Dev_Open(ref openParam);
        if (TUCAMRET.TUCAMRET_SUCCESS != ret || IntPtr.Zero == openParam.hIdxTUCam)
        {
            Console.WriteLine($"  Dev_Open FAILED ret=0x{(uint)ret:X}");
            TucsenApi.Shutdown();
            return 4;
        }
        IntPtr hCamera = openParam.hIdxTUCam;

        try
        {
            TUCAM_VALUE_INFO infoModel = new()
            {
                nID = (int)TUCAM_IDINFO.TUIDI_CAMERA_MODEL,
                nTextSize = TextBufferSize,
            };
            TUCamera.TUCAM_Dev_GetInfo(hCamera, ref infoModel);
            Console.WriteLine($"  model = {Marshal.PtrToStringAnsi(infoModel.pText)}");

            IntPtr ptrSn = Marshal.AllocHGlobal(TextBufferSize);
            Marshal.Copy(new byte[TextBufferSize], 0, ptrSn, TextBufferSize);
            TUCAM_REG_RW regRW = new()
            {
                nRegType = (int)TUREG_FORMATS.TUREG_SN,
                pBuf = ptrSn,
                nBufSize = TextBufferSize,
            };
            TUCAMRET retSn = TUCamera.TUCAM_Reg_Read(hCamera, regRW);
            Console.WriteLine($"  SN ret=0x{(uint)retSn:X} value={Marshal.PtrToStringAnsi(ptrSn)}");
            Marshal.FreeHGlobal(ptrSn);

            Console.WriteLine("== step 5: capabilities / properties ==");
            DumpCapability(hCamera, TUCAM_IDCAPA.TUIDC_BITOFDEPTH);
            DumpCapability(hCamera, TUCAM_IDCAPA.TUIDC_RESOLUTION);
            DumpCapability(hCamera, TUCAM_IDCAPA.TUIDC_ATEXPOSURE);
            DumpProperty(hCamera, TUCAM_IDPROP.TUIDP_EXPOSURETM);
            DumpProperty(hCamera, TUCAM_IDPROP.TUIDP_GLOBALGAIN);

            Console.WriteLine("== step 6: configure for 16bit mono ==");
            TUCamera.TUCAM_Capa_SetValue(hCamera, (int)TUCAM_IDCAPA.TUIDC_ATEXPOSURE, 0);
            TUCAM_CAPA_ATTR depthAttr = new() { idCapa = (int)TUCAM_IDCAPA.TUIDC_BITOFDEPTH };
            if (TUCAMRET.TUCAMRET_SUCCESS == TUCamera.TUCAM_Capa_GetAttr(hCamera, ref depthAttr))
            {
                TUCAMRET retDepth = TUCamera.TUCAM_Capa_SetValue(hCamera, (int)TUCAM_IDCAPA.TUIDC_BITOFDEPTH, depthAttr.nValMax);
                Console.WriteLine($"  set BITOFDEPTH={depthAttr.nValMax} ret=0x{(uint)retDepth:X}");
            }

            Console.WriteLine("== step 7: Buf_Alloc + Cap_Start ==");
            TUCAM_FRAME frame = new()
            {
                pBuffer = IntPtr.Zero,
                ucFormatGet = (byte)TUFRM_FORMATS.TUFRM_FMT_USUAl,
                uiRsdSize = 1,
            };
            ret = TUCamera.TUCAM_Buf_Alloc(hCamera, ref frame);
            Console.WriteLine($"  Buf_Alloc ret=0x{(uint)ret:X} w={frame.usWidth} h={frame.usHeight} elemBytes={frame.ucElemBytes} channels={frame.ucChannels}");
            if (TUCAMRET.TUCAMRET_SUCCESS != ret)
            {
                return 7;
            }

            ret = TUCamera.TUCAM_Cap_Start(hCamera, (uint)TUCAM_CAPTURE_MODES.TUCCM_SEQUENCE);
            Console.WriteLine($"  Cap_Start ret=0x{(uint)ret:X}");
            if (TUCAMRET.TUCAMRET_SUCCESS != ret)
            {
                TUCamera.TUCAM_Buf_Release(hCamera);
                return 8;
            }

            Console.WriteLine("== step 8: grab 3 frames ==");
            for (int nFrame = 0; nFrame < 3; nFrame++)
            {
                TUCAMRET retFrame = WaitForFrameTimeout(hCamera, ref frame, 3000);
                if (TUCAMRET.TUCAMRET_SUCCESS == retFrame)
                {
                    Console.WriteLine($"  frame {nFrame}: w={frame.usWidth} h={frame.usHeight} header={frame.usHeader} " +
                                      $"imgSize={frame.uiImgSize} elemBytes={frame.ucElemBytes} channels={frame.ucChannels} depth={frame.ucDepth}");
                    Console.WriteLine($"    expected mono16 buffer = {frame.usWidth * frame.usHeight * 2} bytes, matches imgSize: {frame.usWidth * frame.usHeight * 2 == frame.uiImgSize}");
                }
                else
                {
                    Console.WriteLine($"  frame {nFrame}: WaitForFrame FAILED ret=0x{(uint)retFrame:X}");
                }
            }

            TUCamera.TUCAM_Buf_AbortWait(hCamera);
            TUCamera.TUCAM_Cap_Stop(hCamera);
            TUCamera.TUCAM_Buf_Release(hCamera);
            Console.WriteLine("== done ==");
        }
        finally
        {
            TUCamera.TUCAM_Dev_Close(hCamera);
            TucsenApi.Shutdown();
        }

        return 0;
    }

    private static void DumpCapability(IntPtr hCamera, TUCAM_IDCAPA idCapa)
    {
        TUCAM_CAPA_ATTR attr = new() { idCapa = (int)idCapa };
        TUCAMRET ret = TUCamera.TUCAM_Capa_GetAttr(hCamera, ref attr);
        if (TUCAMRET.TUCAMRET_SUCCESS != ret)
        {
            Console.WriteLine($"  capa {idCapa}: not supported (ret=0x{(uint)ret:X})");
            return;
        }
        int nValue = 0;
        TUCamera.TUCAM_Capa_GetValue(hCamera, (int)idCapa, ref nValue);
        Console.WriteLine($"  capa {idCapa}: min={attr.nValMin} max={attr.nValMax} dft={attr.nValDft} step={attr.nValStep} current={nValue}");
    }

    private static void DumpProperty(IntPtr hCamera, TUCAM_IDPROP idProp)
    {
        TUCAM_PROP_ATTR attr = new() { idProp = (int)idProp, nIdxChn = 0 };
        TUCAMRET ret = TUCamera.TUCAM_Prop_GetAttr(hCamera, ref attr);
        if (TUCAMRET.TUCAMRET_SUCCESS != ret)
        {
            Console.WriteLine($"  prop {idProp}: not supported (ret=0x{(uint)ret:X})");
            return;
        }
        double dValue = 0.0;
        TUCamera.TUCAM_Prop_GetValue(hCamera, (int)idProp, ref dValue);
        Console.WriteLine($"  prop {idProp}: min={attr.dbValMin} max={attr.dbValMax} dft={attr.dbValDft} step={attr.dbValStep} current={dValue}");
    }
}
