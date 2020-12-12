using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.IO;
using System.Text;

namespace devm0n
{
    public class Configuration
    {
        public GlobalConfiguration Global { get ; set; }
        public DeviceConfiguration[] Devices { get; set; }
        public Configuration()
        {
            Global = new GlobalConfiguration();
            Devices = new DeviceConfiguration[0];
        }
        public Configuration(string Path)
        {
            try {
                if (File.Exists(Path))
                {
                    Configuration _configuration = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(Path), new JsonSerializerOptions() { WriteIndented = true, Converters = { new TimeSpanConverter() } }); 
                    Global = _configuration.Global;
                    Devices = _configuration.Devices;
                }
                else
                    throw new FileNotFoundException();
            }
            catch(Exception _ex) { throw _ex; }
        }
        public string BuildDefault()
        {
            Global.SendGrid.ApiKey = $"SENDGRID_API_KEY";
            Global.SendGrid.EmailAddress = $"from@address.com";
            Global.Twilio.AccountSid = $"TWILIO_ACCOUNT_SID";
            Global.Twilio.AuthToken = $"TWILIO_AUTH_TOKEN";
            Global.Twilio.PhoneNumber = $"+15615550100";
            Devices = new DeviceConfiguration[] { 
                new DeviceConfiguration() { 
                    Address = $"0.0.0.0",
                    Port = 80,
                    UseSSL = false,
                    PollInterval = new PollInterval(),
                    NotificationMethod = new NotificationMethodConfiguration() {
                        Enabled = true,
                        Type = NotificationType.EMAIL,
                        Address = $"to@address.com"
                    }
                },
                new DeviceConfiguration() { 
                    Address = $"1.1.1.1",
                    Port = 443,
                    UseSSL = true,
                    PollInterval = new PollInterval(),
                    NotificationMethod = new NotificationMethodConfiguration() {
                        Enabled = false,
                        Type = NotificationType.SMS,
                        Address = $"+15615551212"
                    }
                }
            };
            try { return JsonSerializer.Serialize(this, new JsonSerializerOptions() { WriteIndented = true, Converters = { new TimeSpanConverter() } }); }
            catch { return null; }
        }       
    }
    public class GlobalConfiguration
    {
        public SendGridConfiguration SendGrid { get; set; }
        public TwilioConfiguration Twilio { get; set; }
        public GlobalConfiguration()
        {
            SendGrid = new SendGridConfiguration();
            Twilio = new TwilioConfiguration();
        }
    }
    public class DeviceConfiguration
    {
        public string Address { get; set; }
        public int Port {get; set;}
        public bool UseSSL {get;set;}
        public PollInterval PollInterval { get; set; }
        public NotificationMethodConfiguration NotificationMethod { get; set; }
        public DeviceConfiguration() 
        {
            Address = string.Empty;
            PollInterval = new PollInterval();
            NotificationMethod = new NotificationMethodConfiguration();
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
            _builder.Append($"/stateFull.xml");
            return _builder.ToString();
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
    public class NotificationMethodConfiguration
    {
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
        EMAIL,
        SMS
    }
    public class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TimeSpan.Parse(reader.GetString(), CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString(format: null, CultureInfo.InvariantCulture));
        }
        public override bool CanConvert(Type typeToConvert)
        {
            return base.CanConvert(typeToConvert);
        }
    }
}