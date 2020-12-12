using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Xml.Serialization;
using System.Net.Http;
using System.IO;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Text;
using Twilio;
using Twilio.Types;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;

namespace devm0n
{
    public class DeviceMonitor : BackgroundService
    {
        private readonly GlobalConfiguration _Global;
        private readonly DeviceConfiguration _Device;
        private readonly HttpClient _HttpClient;
        private readonly HttpClientHandler _HttpClientHandler;
        private readonly SendGridClient _SendGridClient;
        private readonly TwilioRestClient _TwilioClient;
        private readonly XmlSerializer _XmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(DeviceState));
        private DeviceState last_DeviceState = null;
        public DeviceMonitor(GlobalConfiguration Global, DeviceConfiguration Device)
        {
            if (Global == null || Device == null)
                throw new ArgumentNullException("DeviceMonitor Constructor");
            _Global = Global;
            _Device = Device;
            _HttpClientHandler = new HttpClientHandler();
            _HttpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _HttpClient = new HttpClient(_HttpClientHandler);
            _SendGridClient = new SendGridClient(_Global.SendGrid.ApiKey);
            _TwilioClient = new TwilioRestClient(_Global.Twilio.AccountSid,_Global.Twilio.AuthToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Log.Information($"Worker[{_Device.Address}:{_Device.Port}] polling.");
                try 
                {
                    string _HttpRequestUrl = _Device.GetRequestUrl();
                    HttpResponseMessage _httpResponse = await _HttpClient.GetAsync(_HttpRequestUrl, HttpCompletionOption.ResponseHeadersRead);
                    _httpResponse.EnsureSuccessStatusCode();
                    DeviceState current_DeviceState = null;
                    try
                    {
                        if (_httpResponse.Content is object)
                        {
                            Stream _httpResponseStream = await _httpResponse.Content.ReadAsStreamAsync();
                            current_DeviceState = (DeviceState)_XmlSerializer.Deserialize(_httpResponseStream);
                        }
                    }
                    finally
                    {
                        _httpResponse.Dispose();
                    }
                    Log.Information($"Worker[{_Device.Address}:{_Device.Port}] polled.");
                    if (current_DeviceState is object)
                    {
                        if (last_DeviceState is null) { last_DeviceState = current_DeviceState; }
                        // intensive and slow processing of books list. We don't want this to delay releasing the connection.
                        if (last_DeviceState == current_DeviceState)
                            Log.Debug($"Worker[{_Device.Address}:{_Device.Port}] state not changed.");
                        else
                        {
                            Log.Debug($"Worker[{_Device.Address}:{_Device.Port}] state changed.");
                            if (_Device.NotificationMethod.Enabled)
                            {
                                if (_Device.NotificationMethod.Type == NotificationType.EMAIL) // sendgrid
                                {
                                    Log.Debug($"Worker[{_Device.Address}:{_Device.Port}] sending notification via SendGrid");
                                    EmailAddress _from = new EmailAddress(_Global.SendGrid.EmailAddress);
                                    EmailAddress _to = new EmailAddress(_Device.NotificationMethod.Address);
                                    string _subject = $"Device {_Device.Address}:{_Device.Port} state changed.";
                                    string _plaintextContent= await DeviceStateToXML(_Device,current_DeviceState);
                                    string _htmlContent = await DeviceStateToHTML(_Device,current_DeviceState);
                                    try {
                                        SendGridMessage _sgMessage = MailHelper.CreateSingleEmail(_from, _to, _subject, _plaintextContent, _htmlContent);
                                        Response _sgResponse = await _SendGridClient.SendEmailAsync(_sgMessage);
                                        if (_sgResponse.StatusCode == System.Net.HttpStatusCode.Accepted)
                                            Log.Information($"Worker[{_Device.Address}:{_Device.Port}] sent notification via SendGrid");
                                        else
                                            throw new HttpRequestException($"{_sgResponse.StatusCode}");
                                    }
                                    catch(Exception _SendGridException)
                                    {
                                        Log.Error(_SendGridException, $"Worker[{_Device.Address}:{_Device.Port}] failed to send notification via SendGrid");
                                    }
                                }
                                else if (_Device.NotificationMethod.Type == NotificationType.SMS) // twilio
                                {
                                    Log.Debug($"Worker[{_Device.Address}:{_Device.Port}] sending notification via Twilio");
                                    PhoneNumber _from = new PhoneNumber(_Global.Twilio.PhoneNumber);
                                    PhoneNumber _to = new PhoneNumber(_Device.NotificationMethod.Address);
                                    string _smsContent = "State Changed\r\n";
                                    _smsContent = _smsContent + await DeviceStateToSMS(_Device,current_DeviceState);
                                    try {
                                        MessageResource _twMessage = await MessageResource.CreateAsync(to: _to, from: _from, body: _smsContent, client: _TwilioClient);
                                        if (_twMessage.Status == MessageResource.StatusEnum.Queued)
                                            Log.Information($"Worker[{_Device.Address}:{_Device.Port}] sent notification via Twilio");
                                        else
                                            throw new HttpRequestException($"{_twMessage.Status}");
                                    }
                                    catch(Exception _TwilioException)
                                    {
                                        Log.Error(_TwilioException, $"Worker[{_Device.Address}:{_Device.Port}] failed to send notification via Twilio");
                                    }
                                }
                            }
                            last_DeviceState = current_DeviceState; // save current state
                            Log.Debug($"Worker[{_Device.Address}:{_Device.Port}] internal state updated.");
                        }
                    }
                }
                catch(Exception _Exception)
                {
                    Log.Error(_Exception,$"Worker[{_Device.Address}:{_Device.Port}] polling error.");
                }
                TimeSpan _jitter = _Device.PollInterval.Next();
                Log.Debug($"Worker[{_Device.Address}:{_Device.Port}] sleeping for {_jitter}");
                await Task.Delay(_jitter, stoppingToken);
            }
        }

        private async Task<string> DeviceStateToXML(DeviceConfiguration _Device, DeviceState _this)
        {
            string _result = string.Empty;
            using (MemoryStream _MemoryBuffer = new MemoryStream())
            {
                using (StreamWriter _writer = new StreamWriter(_MemoryBuffer))
                {
                    _XmlSerializer.Serialize(_writer, _this);
                    _MemoryBuffer.Position=0;
                    using (StreamReader _reader = new StreamReader(_MemoryBuffer))
                    {
                        _result = _reader.ReadToEnd();
                        _reader.Close();
                    }
                }
            }
            return _result;
        }

        private async Task<string> DeviceStateToSMS(DeviceConfiguration _Device, DeviceState _this)
        {
            StringBuilder _builder = new StringBuilder();
            _builder.AppendLine($"{_Device.Address}:{_Device.Port}\r");
            _builder.AppendLine(new String('-',_Device.Address.Length)+"\r");
            _builder.AppendLine($"Input State 0: {_this.InputState0}\r");
            _builder.AppendLine($"Input State 1: {_this.InputState1}\r");
            _builder.AppendLine($"Input State 2: {_this.InputState2}\r");
            _builder.AppendLine($"Input State 3: {_this.InputState3}\r");
            _builder.AppendLine($"Input State 4: {_this.InputState4}\r");
            _builder.AppendLine($"Input State 5: {_this.InputState5}\r");
            _builder.AppendLine($"Input State 6: {_this.InputState6}\r");
            _builder.AppendLine($"Input State 7: {_this.InputState7}\r");
            _builder.AppendLine($"Power Up Flag: {_this.PowerupFlag}\r");
            string tmp_result = _builder.ToString();
            _builder.Clear();
            string hdr_ftr = new String('-',await GetLongestLine(tmp_result));
            _builder.AppendLine(hdr_ftr);
            _builder.Append(tmp_result);
            _builder.AppendLine();
            _builder.Append(hdr_ftr);
            return _builder.ToString();
        }
        private async Task<int> GetLongestLine(string _multiline)
        {
            int _longest_line=0;
            foreach(string _line in _multiline.Split("\r\n"))
            {
                int _cur_length = _line.Length;
                if (_cur_length>_longest_line)
                    _longest_line=_cur_length;
            }
            return _longest_line;
        }
        private async Task<string> DeviceStateToHTML(DeviceConfiguration _Device, DeviceState _this)
        {
            StringBuilder _builder = new StringBuilder();
            _builder.Append("<html xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w=\"urn:schemas-microsoft-com:office:word\" xmlns:m=\"http://schemas.microsoft.com/office/2004/12/omml\" xmlns=\"http://www.w3.org/TR/REC-html40\">\r\n");
            _builder.Append("<head>\r\n<meta http-equiv=\"Content-Type\" content=\"text/html; charset=us-ascii\">\r\n<meta name=\"Generator\" content=\"Microsoft Word 15 (filtered medium)\">\r\n<style><!--\r\n/* Font Definitions */\r\n@font-face\r\n");
            _builder.Append("\t{font-family:\"Cambria Math\";\r\n\tpanose-1:2 4 5 3 5 4 6 3 2 4;}\r\n@font-face\r\n\t{font-family:Calibri;\r\n\tpanose-1:2 15 5 2 2 2 4 3 2 4;}\r\n@font-face\r\n\t{font-family:Verdana;\r\n\tpanose-1:2 11 6 4 3 5 4 4 2 4;}\r\n");
            _builder.Append("/* Style Definitions */\r\np.MsoNormal, li.MsoNormal, div.MsoNormal\r\n\t{margin:0in;\r\n\tmargin-bottom:.0001pt;\r\n\tfont-size:11.0pt;\r\n\tfont-family:\"Calibri\",sans-serif;}\r\nspan.EmailStyle17\r\n\t{mso-style-type:personal-compose;\r\n");
            _builder.Append("\tfont-family:\"Calibri\",sans-serif;\r\n\tcolor:windowtext;}\r\n.MsoChpDefault\r\n\t{mso-style-type:export-only;\r\n\tfont-family:\"Calibri\",sans-serif;}\r\n@page WordSection1\r\n\t{size:8.5in 11.0in;\r\n\tmargin:1.0in 1.0in 1.0in 1.0in;}\r\n");
            _builder.Append("div.WordSection1\r\n\t{page:WordSection1;}\r\n--></style><!--[if gte mso 9]><xml>\r\n<o:shapedefaults v:ext=\"edit\" spidmax=\"1026\" />\r\n</xml><![endif]--><!--[if gte mso 9]><xml>\r\n<o:shapelayout v:ext=\"edit\">\r\n");
            _builder.Append("<o:idmap v:ext=\"edit\" data=\"1\" />\r\n</o:shapelayout></xml><![endif]-->\r\n</head>\r\n<body lang=\"EN-US\" link=\"#0563C1\" vlink=\"#954F72\">\r\n<div class=\"WordSection1\">\r\n<div align=\"center\">\r\n");
            _builder.Append("<table class=\"MsoNormalTable\" border=\"1\" cellpadding=\"0\" style=\"background:#3B5FA6\">\r\n<tbody>\r\n<tr>\r\n<td colspan=\"2\" style=\"background:#7799EE;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"mso-margin-top-alt:auto;mso-margin-bottom-alt:auto;text-align:center\">\r\n");
            _builder.Append($"<b><span style=\"font-size:24.0pt;font-family:&quot;Verdana&quot;,sans-serif;color:black\">{_Device.Address}:{_Device.Port}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">Input State 0<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_this.InputState0}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">Input State 1<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_this.InputState1}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">Input State 2<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_this.InputState2}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">Input State 3<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_this.InputState3}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">Input State 4<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_this.InputState4}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">Input State 5<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_this.InputState5}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">Input State 6<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_this.InputState6}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">Input State 7<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_this.InputState7}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append("<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">Power Up Flag<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
            _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_this.PowerupFlag}<o:p></o:p></span></b></p>\r\n");
            _builder.Append("</td>\r\n</tr>\r\n</tbody>\r\n</table>\r\n</div>\r\n<p class=\"MsoNormal\"><o:p>&nbsp;</o:p></p>\r\n</div>\r\n</body>\r\n</html>\r\n");
            return _builder.ToString();
        }
    }
}
