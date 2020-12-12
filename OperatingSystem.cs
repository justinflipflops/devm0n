using System.Runtime.InteropServices;

namespace devm0n
{    
    public static class OperatingSystem
    {
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static OperatingSystemType Type {
            get {
                if (IsWindows && !IsMacOS && !IsLinux)
                    return OperatingSystemType.Windows;
                else if (!IsWindows && IsMacOS && !IsLinux)
                    return OperatingSystemType.macOS;
                else
                    return OperatingSystemType.Linux;
            }
        }
    }
    public enum OperatingSystemType
    {
        Windows,
        macOS,
        Linux
    }
}