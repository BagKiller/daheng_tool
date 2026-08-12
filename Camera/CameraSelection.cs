namespace VegaBeamTool.Camera
{
    /// <summary>
    /// 工程支持的相机厂商/型号。新增相机型号时在此追加一项，并在 <see cref="CameraFactory"/> 注册实现。
    /// </summary>
    public enum CameraVendor
    {
        DahengMercury3 = 0,
        TucsenLiraUV = 1,
    }

    /// <summary>
    /// 型号下拉框的候选项。
    /// </summary>
    public sealed class CameraModelOption
    {
        public CameraModelOption(CameraVendor vendor, string displayName)
        {
            Vendor = vendor;
            DisplayName = displayName;
        }

        public CameraVendor Vendor { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// 一台已枚举到的物理相机。大恒依据 <see cref="SerialNumber"/> 打开，Tucsen 依据 <see cref="Index"/> 打开。
    /// </summary>
    public sealed class CameraDeviceInfo
    {
        public CameraVendor Vendor { get; init; }

        public int Index { get; init; }

        public string SerialNumber { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    public static class CameraFactory
    {
        public static IReadOnlyList<CameraModelOption> SupportedModels { get; } =
        [
            new CameraModelOption(CameraVendor.DahengMercury3, "Daheng Mercury3"),
            new CameraModelOption(CameraVendor.TucsenLiraUV, "Tucsen LiraUV"),
        ];

        public static CameraBase Create(CameraVendor vendor) => vendor switch
        {
            CameraVendor.DahengMercury3 => new Mercury3Camera(),
            CameraVendor.TucsenLiraUV => new TucsenCamera(),
            _ => throw new NotSupportedException($"Unsupported camera vendor: {vendor}"),
        };

        public static CameraModelOption GetModelOption(CameraVendor vendor)
            => SupportedModels.FirstOrDefault(model => model.Vendor == vendor) ?? SupportedModels[0];

        public static CameraVendor ParseVendor(string? value)
            => Enum.TryParse(value, out CameraVendor vendor) && Enum.IsDefined(vendor)
                ? vendor
                : CameraVendor.DahengMercury3;
    }
}
