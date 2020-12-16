using System;
using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Converters;

namespace devm0n
{
    public class Configuration
    {

        GlobalConfiguration _global = new GlobalConfiguration();
        DeviceConfiguration[] _device = new DeviceConfiguration[0];
        Dictionary<string,GroupConfiguration> _group = new Dictionary<string, GroupConfiguration>();
        public GlobalConfiguration Global { get { return _global; } set { _global = value; } }
        public DeviceConfiguration[] Devices { get { return _device; }set { _device = value; } }
        public Dictionary<string,GroupConfiguration> Groups { get {return _group; } set { _group = value; } }
        public Configuration()
        {
            _global = new GlobalConfiguration();
            _device = new DeviceConfiguration[0];
            _group = new Dictionary<string, GroupConfiguration>();
        }
        public Configuration(string GlobalPath, string DevicePath, string GroupPath)
        {
            try {
                if (File.Exists(GlobalPath))
                {
                    try {
                        //_global = JsonSerializer.Deserialize<GlobalConfiguration>(File.ReadAllText(GlobalPath), new JsonSerializerOptions() { WriteIndented = true, Converters = { new TimeSpanConverter() } }); 
                        _global = JsonConvert.DeserializeObject<GlobalConfiguration>(File.ReadAllText(GlobalPath));
                    }
                    catch(Exception _ex)
                    {
                        throw new FileLoadException("Failed to load Global configuration, see inner exception for details.", _ex);
                    }
                }
                else
                    throw new FileNotFoundException("Global configuration path does not exist.");

                if (File.Exists(DevicePath))
                {
                    try {
                        //_device = JsonSerializer.Deserialize<DeviceConfiguration[]>(File.ReadAllText(DevicePath), new JsonSerializerOptions() { WriteIndented = true, Converters = { new TimeSpanConverter() } }); 
                        _device = JsonConvert.DeserializeObject<DeviceConfiguration[]>(File.ReadAllText(DevicePath));
                    }
                    catch(Exception _ex)
                    {
                        throw new FileLoadException("Failed to load Device configuration, see inner exception for details.", _ex);
                    }
                }
                else
                    throw new FileNotFoundException("Device configuration path does not exist.");

                if (File.Exists(GroupPath))
                {
                    try {
                        _group = JsonConvert.DeserializeObject<Dictionary<string,GroupConfiguration>>(File.ReadAllText(GroupPath));
                        //_group = JsonSerializer.Deserialize<Dictionary<string,GroupConfiguration>>(File.ReadAllText(GroupPath), new JsonSerializerOptions() { WriteIndented = true, Converters = { new TimeSpanConverter() } });
                    }
                    catch(Exception _ex)
                    {
                        throw new FileLoadException("Failed to load Group configuration, see inner exception for details.", _ex);
                    }
                }
                else
                    throw new FileNotFoundException("Group configuration path does not exist.");
            }
            catch(Exception _ex) { throw new Exception("Failed to load configuration, see inner exception for details.",_ex); }
        }
        public string BuildGlobalDefault()
        {
            Global.Smtp.Address = $"smtp.server.com";
            Global.Smtp.Port = 25;
            Global.Smtp.UseAuth = false;
            Global.Smtp.UseSSL = false;
            Global.Smtp.EmailAddress = "from@address.com";
            Global.Smtp.Credential.Username = "username@maybe.com";
            Global.Smtp.Credential.Password = "password";
            Global.SendGrid.ApiKey = $"SENDGRID_API_KEY";
            Global.SendGrid.EmailAddress = $"from@address.com";
            Global.Twilio.AccountSid = $"TWILIO_ACCOUNT_SID";
            Global.Twilio.AuthToken = $"TWILIO_AUTH_TOKEN";
            Global.Twilio.PhoneNumber = $"+15615550100";
            try { return JsonConvert.SerializeObject(Global,Formatting.Indented); }
            catch { return null; }
        }
        public string BuildDeviceDefault()
        {
            Devices = new DeviceConfiguration[] { 
                new DeviceConfiguration() { 
                    Name = "Example 1",
                    Address = $"0.0.0.0",
                    Port = 80,
                    File = "stateFull.xml",
                    UseSSL = false,
                    PollInterval = new PollInterval(),
                    Fields = new DeviceFieldConfiguration[] {
                        new DeviceFieldConfiguration() {
                            Name = "input0state",
                            Enabled = true,
                            Group = "Group1",
                        },
                        new DeviceFieldConfiguration() {
                            Name = "input1state",
                            Enabled = true,
                            Group = "Group2",
                        }
                    }
                },
                new DeviceConfiguration() { 
                    Name = "Example 2",
                    Address = $"1.1.1.1",
                    Port = 443,
                    File = "state.xml",
                    UseSSL = true,
                    PollInterval = new PollInterval(),
                    Fields = new DeviceFieldConfiguration[] {
                        new DeviceFieldConfiguration() {
                            Name = "input0state",
                            Enabled = false,
                            Group = "Group1",
                        },
                        new DeviceFieldConfiguration() {
                            Name = "input1state",
                            Enabled = true,
                            Group = "Group2",
                        }
                    }
                }
            };
            try { return JsonConvert.SerializeObject(Devices,Formatting.Indented); }
            catch { return null; }
        } 

        public string BuildGroupDefault()
        {
            Groups = new Dictionary<string, GroupConfiguration>() {
                { 
                    "Group1", 
                    new GroupConfiguration() 
                    {
                        Enabled = true,
                        NotificationMethods = new NotificationMethodConfiguration[] 
                        {
                            new NotificationMethodConfiguration() {
                                Type = NotificationType.SENDGRID,
                                Enabled = true,
                                Address = "to@address.com"
                            },
                            new NotificationMethodConfiguration() {
                                Type = NotificationType.SMTP,
                                Enabled = false,
                                Address = "to@address.com"
                            }
                        }
                    }
                },
                { 
                    "Group2", 
                    new GroupConfiguration() 
                    {
                        Enabled = true,
                        NotificationMethods = new NotificationMethodConfiguration[] 
                        {
                            new NotificationMethodConfiguration() {
                                Type = NotificationType.TWILIO,
                                Enabled = true,
                                Address = "+15615551212"
                            },
                            new NotificationMethodConfiguration() {
                                Type = NotificationType.TWILIO,
                                Enabled = false,
                                Address = "+15615557777"
                            }
                        }
                    }
                }
            };
            try { return JsonConvert.SerializeObject(Groups, Formatting.Indented); }
            catch { return null; }
        }     
    }
    public class GlobalConfiguration
    {
        public SmtpConfiguration Smtp {get; set;}
        public SendGridConfiguration SendGrid { get; set; }
        public TwilioConfiguration Twilio { get; set; }
        public GlobalConfiguration()
        {
            Smtp = new SmtpConfiguration();
            SendGrid = new SendGridConfiguration();
            Twilio = new TwilioConfiguration();
        }
    }
    public class DeviceConfiguration
    {
        public bool Enabled {get; set;}
        public string Name {get; set;}
        public string Address { get; set; }
        public string File {get; set; }
        public int Port {get; set;}
        public bool UseSSL {get;set;}
        public PollInterval PollInterval { get; set; }
        public DeviceFieldConfiguration[] Fields { get; set; }
        public DeviceConfiguration() 
        {
            Enabled = false;
            Name = string.Empty;
            Address = string.Empty;
            File = string.Empty;
            PollInterval = new PollInterval();
            Fields = new DeviceFieldConfiguration[0];
        }
        public string GetRequestUrl()
        {
            StringBuilder _builder = new StringBuilder();
            if (UseSSL)
                _builder.Append($"https");
            else
                _builder.Append($"http");
            _builder.Append($"://");
            _builder.Append($"{Address}:{Port}");
            _builder.Append($"/{File}");
            return _builder.ToString();
        }
    }

    public class SmtpConfiguration
    {
        public string Address {get; set;}
        public int Port {get; set;}
        public bool UseAuth {get; set;}
        public bool UseSSL {get; set;} 
        public string EmailAddress {get; set;}
        public SmtpCredentialConfiguration Credential {get; set;}
        public SmtpConfiguration()
        {
            Address = string.Empty;
            EmailAddress = string.Empty;
            Credential = new SmtpCredentialConfiguration();
        }
    }
    public class SmtpCredentialConfiguration
    {
        public string Username {get;set;}
        public string Password {get;set;}
        public SmtpCredentialConfiguration()
        {
            Username = string.Empty;
            Password = string.Empty;
        }
    }
    public class SendGridConfiguration
    {
        public string ApiKey { get; set; }
        public string EmailAddress { get; set; }
        public SendGridConfiguration()
        {
            ApiKey = string.Empty;
            EmailAddress = string.Empty;
        }
    }
    public class TwilioConfiguration {
        public string AccountSid { get; set; }
        public string AuthToken { get; set; }
        public string PhoneNumber { get; set; }
        public TwilioConfiguration()
        {
            AccountSid = string.Empty;
            AuthToken = string.Empty;
            PhoneNumber = string.Empty;
        }
    }

    public class DeviceFieldConfiguration
    {
        public string Name {get; set;}
        public bool Enabled {get; set;}
        public string Group {get; set;}
        public DeviceFieldConfiguration()
        {
            Name = string.Empty;
            Group = string.Empty;
        }
    }

    public class GroupConfiguration
    {
        public bool Enabled {get; set;}
        public NotificationMethodConfiguration[] NotificationMethods {get; set;}
        public GroupConfiguration()
        {
            NotificationMethods = new NotificationMethodConfiguration[0];
        }
    }


    public class NotificationMethodConfiguration
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public NotificationType Type { get; set; }
        public string Address { get; set; }
        public bool Enabled { get; set; }
        public NotificationMethodConfiguration()
        {
            Address = string.Empty;
        }
    }
    public enum NotificationType
    {
        SENDGRID,
        TWILIO,
        SMTP
    }
}