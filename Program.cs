using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.CommandLine.DragonFruit;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.FileProviders;
using LicenseOTC;

namespace devm0n
{
    public class Program
    {
        private static IHost _Host = null;
        private static License _license = null;
        private static byte[] _publicKey = new byte[0];
        private static Configuration _Configuration = new Configuration();
        private static string global_settings_configuration_directory = string.Empty;
        private static string global_settings_configuration_full_path = string.Empty;
        private static string global_settings_configuration_name =  (System.Diagnostics.Debugger.IsAttached ? "global.development.config" : "global.config");

        private static string device_settings_configuration_directory = string.Empty;
        private static string device_settings_configuration_full_path = string.Empty;
        private static string device_settings_configuration_name =  (System.Diagnostics.Debugger.IsAttached ? "devices.development.config" : "devices.config");

        private static string group_settings_configuration_directory = string.Empty;
        private static string group_settings_configuration_full_path = string.Empty;
        private static string group_settings_configuration_name =  (System.Diagnostics.Debugger.IsAttached ? "groups.development.config" : "groups.config");

        private static byte[] LoadPublicKey()
        {
            byte[] _publicKeyBuffer = null;
            EmbeddedFileProvider _EmbeddedFileProvider = new EmbeddedFileProvider(System.Reflection.Assembly.GetExecutingAssembly());
            IFileInfo _EmbeddedFileInfo = _EmbeddedFileProvider.GetFileInfo("license.pub");
            using (Stream _Stream = _EmbeddedFileInfo.CreateReadStream())
            {
                using (MemoryStream _memStream = new MemoryStream((int)_EmbeddedFileInfo.Length))
                {
                    _Stream.CopyTo(_memStream);
                    _publicKeyBuffer = _memStream.ToArray();
                    return _publicKeyBuffer;
                }
            }
        }
        public static void Main(bool BuildDefaultGlobalConfig=false, bool BuildDefaultDeviceConfig=false, bool BuildDefaultGroupConfig=false, FileInfo GlobalSettingsConfigFile=null, FileInfo DeviceSettingsConfigFile=null, FileInfo GroupSettingsConfigFile=null)
        {
            _publicKey = LoadPublicKey();
            global_settings_configuration_directory = $"{System.IO.Path.GetDirectoryName(new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath)}/";
            device_settings_configuration_directory = $"{System.IO.Path.GetDirectoryName(new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath)}/";
            group_settings_configuration_directory = $"{System.IO.Path.GetDirectoryName(new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath)}/";
            if (BuildDefaultGlobalConfig)
            {
                Console.WriteLine(_Configuration.BuildGlobalDefault());
                return;
            }
            if (BuildDefaultDeviceConfig)
            {
                Console.WriteLine(_Configuration.BuildDeviceDefault());
                return;
            }
            if (BuildDefaultGroupConfig)
            {
                Console.WriteLine(_Configuration.BuildGroupDefault());
                return;
            }

            if (GlobalSettingsConfigFile?.DirectoryName != null && GlobalSettingsConfigFile?.Name != null) {
                global_settings_configuration_directory = $"{GlobalSettingsConfigFile?.DirectoryName}/";
                global_settings_configuration_name = GlobalSettingsConfigFile?.Name;
            }
            global_settings_configuration_full_path = $"{global_settings_configuration_directory}{global_settings_configuration_name}";

            LoggerConfiguration _LoggerConfiguration = new LoggerConfiguration().Enrich.FromLogContext()
                .WriteTo.File($"{global_settings_configuration_directory}logs/.log",retainedFileCountLimit:14, rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Day)
                .WriteTo.Console();
            if (System.Diagnostics.Debugger.IsAttached)
                _LoggerConfiguration = _LoggerConfiguration.MinimumLevel.Debug();

            Log.Logger = _LoggerConfiguration.CreateLogger();

            try {
                _license = new License($"{System.IO.Path.GetDirectoryName(new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath)}/{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.lic");
            }
            catch(Exception _ex)
            {  
               Log.Error(_ex,"Unable to load license file from disk."); 
               return;
            }
            if (!_license.CheckSignature(_publicKey))
            {
                Log.Error("License file contains invalid signature.");
                return;
            }
            if (DeviceSettingsConfigFile?.DirectoryName != null && DeviceSettingsConfigFile?.Name != null) {
                device_settings_configuration_directory = $"{DeviceSettingsConfigFile?.DirectoryName}/";
                device_settings_configuration_name = DeviceSettingsConfigFile?.Name;
            }
            device_settings_configuration_full_path = $"{device_settings_configuration_directory}{device_settings_configuration_name}";

            if (GroupSettingsConfigFile?.DirectoryName != null && GroupSettingsConfigFile?.Name != null) {
                group_settings_configuration_directory = $"{GroupSettingsConfigFile?.DirectoryName}/";
                group_settings_configuration_name = GroupSettingsConfigFile?.Name;
            }
            group_settings_configuration_full_path = $"{group_settings_configuration_directory}{group_settings_configuration_name}";

            try {
                Log.Debug($"Loading application configuration file {global_settings_configuration_full_path}");
                _Configuration = new Configuration(global_settings_configuration_full_path, device_settings_configuration_full_path, group_settings_configuration_full_path);
                Log.Information($"Loaded application configuration file {global_settings_configuration_full_path}");
            }
            catch(Exception _ex) {
                Log.Error(_ex, $"Error loading application configuration file {global_settings_configuration_full_path}");
                return;
            }
            if (_Configuration.Devices.Count() > Convert.ToInt32(_license.Features["MaxDevices"]))
            {
                Log.Error("You have exceeded your device limit. Please adjust your configuration or purchase additional licensing.");
                return;
            }
            if (_Configuration.Groups.Count() > Convert.ToInt32(_license.Features["MaxGroups"]))
            {
                Log.Error("You have exceeded your group limit. Please adjust your configuration or purchase additional licensing.");
                return;
            }
            Log.Information($"Application starting.");
            _Host = CreateHostBuilder().UseSerilog().Build();
            Task.Run(LicenseVerification);
            _Host.Run();
            Log.Information($"Application shut down.");
        }

        public static async Task LicenseVerification()
        {
            bool _shutdown = false;
            DateTime _startTime = DateTime.UtcNow;
            while(true)
            {
                if (!_license.CheckSignature(_publicKey))
                {
                    Log.Error("License Verification Failed, Invalid Signature.");
                    _shutdown = true;
                }
                if (_license.IsRuntimeExceeded())
                {
                    Log.Error("License Verification Failed, Trial Runtime Exceeded(1h).");
                    _shutdown = true;
                }
                if (_license.IsLicenseExpired())
                {
                    Log.Error("License Verification Failed, License Expired.");
                    _shutdown = true;
                }

                if (_shutdown)
                {
                    await _Host.StopAsync(new TimeSpan(0,0,1,0,0));
                    return;
                }
                System.Threading.Thread.Sleep(new TimeSpan(0,0,1,0,0));
            }
        }
        public static IHostBuilder CreateHostBuilder()
        {
            Log.Debug($"Adding device monitors.");
            IHostBuilder _builder = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                foreach (DeviceConfiguration _device in _Configuration.Devices)
                {
                    if (_device.Enabled)
                    {
                        services.AddSingleton<IHostedService>(sp => new DeviceMonitor(_license, _Configuration.Global, _device, _Configuration.Groups));
                        Log.Debug($"Added device monitor for {_device.Name}.");
                    }
                    else
                        Log.Debug($"Skipped device monitor for {_device.Name}, disabled.");
                }
            });
            Log.Debug($"Device monitors added.");
            IHostBuilder _result = null;
            Log.Debug($"Detected {OperatingSystem.Type} operating system.");
            switch(OperatingSystem.Type)
            {
                case OperatingSystemType.Linux:
                    _result = _builder.UseSystemd();
                    break;
                case OperatingSystemType.Windows:
                    _result = _builder.UseWindowsService();
                    break;
                case OperatingSystemType.macOS:
                default:
                    _result = _builder.UseConsoleLifetime();
                    break;
            }
            return _result;
        }
    }
}
