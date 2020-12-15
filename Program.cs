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


namespace devm0n
{
    public class Program
    {
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


        public static void Main(bool BuildDefaultGlobalConfig=false, bool BuildDefaultDeviceConfig=false, bool BuildDefaultGroupConfig=false, FileInfo GlobalSettingsConfigFile=null, FileInfo DeviceSettingsConfigFile=null, FileInfo GroupSettingsConfigFile=null)
        {
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

            LoggerConfiguration _LoggerConfiguration = new LoggerConfiguration().Enrich.FromLogContext()
                .WriteTo.File($"{global_settings_configuration_directory}logs/.log",retainedFileCountLimit:14, rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Day)
                .WriteTo.Console();
            
            if (System.Diagnostics.Debugger.IsAttached)
                _LoggerConfiguration = _LoggerConfiguration.MinimumLevel.Debug();

            Log.Logger = _LoggerConfiguration.CreateLogger();
            try {
                Log.Debug($"Loading application configuration file {global_settings_configuration_full_path}");
                _Configuration = new Configuration(global_settings_configuration_full_path, device_settings_configuration_full_path, group_settings_configuration_full_path);
                Log.Information($"Loaded application configuration file {global_settings_configuration_full_path}");
            }
            catch(Exception _ex) {
                Log.Error(_ex, $"Error loading application configuration file {global_settings_configuration_full_path}");
                return;
            }
            Log.Information($"Application starting.");
            CreateHostBuilder().UseSerilog().Build().Run();
            Log.Information($"Application shut down.");
        }

        public static IHostBuilder CreateHostBuilder()
        {
            Log.Debug($"Adding device monitors.");
            IHostBuilder _builder = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                foreach (DeviceConfiguration _device in _Configuration.Devices)
                {
                    services.AddSingleton<IHostedService>(sp => new DeviceMonitor(_Configuration.Global, _device, _Configuration.Groups));
                    Log.Debug($"Added device monitor for {_device.Name}.");
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
