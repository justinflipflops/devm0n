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
        private static string settings_configuration_directory = string.Empty;
        private static string settings_configuration_full_path = string.Empty;
        private static string settings_configuration_name =  (System.Diagnostics.Debugger.IsAttached ? "development.config" : "production.config");
        public static void Main(bool CreateDefaultConfig=false, FileInfo SettingsConfigFile=null)
        {
            settings_configuration_directory = $"{System.IO.Path.GetDirectoryName(new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath)}/";
            if (CreateDefaultConfig)
            {
                Console.WriteLine(_Configuration.BuildDefault());
                return;
            }
            if (SettingsConfigFile?.DirectoryName != null && SettingsConfigFile?.Name != null) {
                settings_configuration_directory = $"{SettingsConfigFile?.DirectoryName}/";
                settings_configuration_name = SettingsConfigFile?.Name;
            }
            settings_configuration_full_path = $"{settings_configuration_directory}{settings_configuration_name}";
            LoggerConfiguration _LoggerConfiguration = new LoggerConfiguration().Enrich.FromLogContext()
                .WriteTo.File($"{settings_configuration_directory}logs/.log", fileSizeLimitBytes: null, rollingInterval: RollingInterval.Day)
                .WriteTo.Console();
            
            if (System.Diagnostics.Debugger.IsAttached)
                _LoggerConfiguration = _LoggerConfiguration.MinimumLevel.Debug();

            Log.Logger = _LoggerConfiguration.CreateLogger();
            try {
                Log.Debug($"Loading application configuration file {settings_configuration_full_path}");
                _Configuration = new Configuration(settings_configuration_full_path);
                Log.Information($"Loaded application configuration file {settings_configuration_full_path}");
            }
            catch(Exception _ex) {
                Log.Error(_ex, $"Error loading application configuration file {settings_configuration_full_path}");
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
                    services.AddSingleton<IHostedService>(sp => new DeviceMonitor(_Configuration.Global, _device));
                    Log.Debug($"Added device monitor for {_device.Address}.");
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
