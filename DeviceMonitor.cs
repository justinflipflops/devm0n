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
using System.Net.Mail;
using System.Net;
using System.Net.Mime;
using System.Xml;

namespace devm0n
{
    public class DeviceMonitor : BackgroundService
    {
        private readonly GlobalConfiguration _Global;
        private readonly DeviceConfiguration _Device;
        private readonly Dictionary<string,GroupConfiguration> _Groups;
        private readonly HttpClient _HttpClient;
        private readonly HttpClientHandler _HttpClientHandler;
        private readonly SendGridClient _SendGridClient;
        private readonly TwilioRestClient _TwilioClient;
        private readonly SmtpClient _SmtpClient;
        private readonly XmlSerializer _XmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(DeviceState));
        private Dictionary<string,string> last_DeviceStateDict = null;
        private UInt64 poll_totalCount = 0;
        private UInt64 poll_changeCount = 0;
        private UInt64 poll_nochangeCount = 0;
        private CancellationToken _stoppingToken;
        public DeviceMonitor(GlobalConfiguration Global, DeviceConfiguration Device, Dictionary<string,GroupConfiguration> Groups)
        {
            if (Global == null || Device == null)
                throw new ArgumentNullException("DeviceMonitor Constructor");
            _Global = Global;
            _Device = Device;
            _Groups = Groups;
            _HttpClientHandler = new HttpClientHandler();
            _HttpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _HttpClient = new HttpClient(_HttpClientHandler);
            _SendGridClient = new SendGridClient(_Global.SendGrid.ApiKey);
            _TwilioClient = new TwilioRestClient(_Global.Twilio.AccountSid,_Global.Twilio.AuthToken);
            _SmtpClient = new SmtpClient(_Global.Smtp.Address,_Global.Smtp.Port);
            _SmtpClient.UseDefaultCredentials = false;
            _SmtpClient.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network;
            _SmtpClient.EnableSsl = _Global.Smtp.UseSSL;
            if (_Global.Smtp.UseAuth)
                _SmtpClient.Credentials = new NetworkCredential(_Global.Smtp.Credential.Username,_Global.Smtp.Credential.Password);
            System.Net.ServicePointManager.ServerCertificateValidationCallback = (message, cert, chain, errors) => true;
        }
        protected override async Task ExecuteAsync(CancellationToken StoppingToken)
        {
            _stoppingToken = StoppingToken;
            while (!_stoppingToken.IsCancellationRequested)
            {
                Log.Debug($"Monitor[{_Device.Name}] polling.");
                try 
                {
                    string _HttpRequestUrl = _Device.GetRequestUrl();
                    HttpResponseMessage _httpResponse = await _HttpClient.GetAsync(_HttpRequestUrl, HttpCompletionOption.ResponseHeadersRead);
                    _httpResponse.EnsureSuccessStatusCode();
                    Dictionary<string,string> current_DeviceStateDict = new Dictionary<string, string>();
                    try
                    {
                        if (_httpResponse.Content is object)
                        {
                            string _XmlDocumentData = string.Empty;
                            using (Stream _httpResponseStream = await _httpResponse.Content.ReadAsStreamAsync())
                                using (StreamReader _httpResponseStreamReader = new StreamReader(_httpResponseStream))
                                    _XmlDocumentData = await _httpResponseStreamReader.ReadToEndAsync();
                            current_DeviceStateDict = await XmlToDictionary(_XmlDocumentData);
                        }
                    }
                    finally
                    {
                        _httpResponse.Dispose();
                    }
                    poll_totalCount++;
                    if (current_DeviceStateDict.Count > 0)
                    {
                        if (last_DeviceStateDict is null) { last_DeviceStateDict = current_DeviceStateDict; }
                        // intensive and slow processing of books list. We don't want this to delay releasing the connection.
                        Dictionary<string,string> compare_DeviceStateDict = await DictionaryDiff(last_DeviceStateDict,current_DeviceStateDict);
                        if (compare_DeviceStateDict.Count > 0)
                        {
                            Log.Debug($"Monitor[{_Device.Name}] full state change detected.");
                            ProcessStateChange(_Device, compare_DeviceStateDict);
                        }
                        else
                            poll_nochangeCount++;
                        Log.Information($"Monitor[{_Device.Name}] polled. total: {poll_totalCount} - no change: {poll_nochangeCount} - change: {poll_changeCount}");
                        last_DeviceStateDict = current_DeviceStateDict; // save current state
                        Log.Debug($"Monitor[{_Device.Name}] internal state updated.");
                    }
                }
                catch(Exception _Exception)
                {
                    Log.Error(_Exception,$"Monitor[{_Device.Name}] polling error.");
                }
                TimeSpan _jitter = _Device.PollInterval.Next();
                Log.Debug($"Monitor[{_Device.Name}] sleeping for {_jitter}");
                await Task.Delay(_jitter, _stoppingToken);
            }
        }

        private async Task ProcessStateChange(DeviceConfiguration _Device, Dictionary<string,string> _Changes)
        {
            bool _changeDetected = false;
            Log.Debug($"Monitor[{_Device.Name}] processing state change.");
            foreach(DeviceFieldConfiguration _Field in _Device.Fields)
            {
                if (_Field.Enabled)
                {
                    if (_Changes.ContainsKey(_Field.Name))
                    {
                        _changeDetected = true;
                        if (_Groups.ContainsKey(_Field.Group))
                        {
                            GroupConfiguration _Group = _Groups[_Field.Group];
                            if (_Group.Enabled)
                            {
                                foreach(NotificationMethodConfiguration _Method in _Group.NotificationMethods)
                                {
                                    if (_Method.Enabled)
                                    {
                                        if (_Method.Type == NotificationType.SENDGRID) {
                                                Log.Debug($"Monitor[{_Device.Name}] sending notification via SendGrid to {_Method.Address}");
                                                EmailAddress _from = new EmailAddress(_Global.SendGrid.EmailAddress);
                                                EmailAddress _to = new EmailAddress(_Method.Address);
                                                string _subject = $"Device {_Device.Name} state changed.";
                                                string _plaintextContent= await DeviceStateToText(_Device,_Changes);
                                                string _htmlContent = await DeviceStateToHTML(_Device,_Changes);
                                                try {
                                                    SendGridMessage _sgMessage = MailHelper.CreateSingleEmail(_from, _to, _subject, _plaintextContent, _htmlContent);
                                                    Response _sgResponse = await _SendGridClient.SendEmailAsync(_sgMessage);
                                                    if (_sgResponse.StatusCode == System.Net.HttpStatusCode.Accepted)
                                                        Log.Debug($"Monitor[{_Device.Name}] sent notification via SendGrid to {_Method.Address}");
                                                    else
                                                        throw new HttpRequestException($"{_sgResponse.StatusCode}");
                                                }
                                                catch(Exception _SendGridException)
                                                {
                                                    Log.Error(_SendGridException, $"Monitor[{_Device.Name}] failed to send notification via SendGrid to {_Method.Address}");
                                                }
                                        }
                                        else if (_Method.Type == NotificationType.TWILIO) {
                                                Log.Debug($"Monitor[{_Device.Name}] sending notification via Twilio to {_Method.Address}");
                                                PhoneNumber _from = new PhoneNumber(_Global.Twilio.PhoneNumber);
                                                PhoneNumber _to = new PhoneNumber(_Method.Address);
                                                string _smsContent = "State Changed\r\n";
                                                _smsContent = _smsContent + await DeviceStateToText(_Device,_Changes);
                                                try {
                                                    MessageResource _twMessage = await MessageResource.CreateAsync(to: _to, from: _from, body: _smsContent, client: _TwilioClient);
                                                    if (_twMessage.Status == MessageResource.StatusEnum.Queued)
                                                        Log.Information($"Monitor[{_Device.Name}] sent notification via Twilio to {_Method.Address}");
                                                    else
                                                        throw new HttpRequestException($"{_twMessage.Status}");
                                                }
                                                catch(Exception _TwilioException)
                                                {
                                                    Log.Error(_TwilioException, $"Monitor[{_Device.Name}] failed to send notification via Twilio to {_Method.Address}");
                                                }
                                        }
                                        else if (_Method.Type == NotificationType.SMTP) {
                                            Log.Debug($"Monitor[{_Device.Name}] sending notification via Smtp");
                                            MailAddress _from = new MailAddress(_Global.Smtp.EmailAddress);
                                            MailAddress _to = new MailAddress(_Method.Address);
                                            string _subject = $"Device {_Device.Name} state changed.";
                                            string _plaintextContent= await DeviceStateToText(_Device,_Changes);
                                            string _htmlContent = await DeviceStateToHTML(_Device,_Changes);
                                            try {

                                                MailMessage _smtpMessage = new MailMessage(_from,_to);
                                                _smtpMessage.BodyEncoding = Encoding.UTF8;
                                                _smtpMessage.SubjectEncoding = Encoding.UTF8;
                                                _smtpMessage.Subject = _subject;
                                                _smtpMessage.Body = _plaintextContent;
                                                AlternateView _htmlView = AlternateView.CreateAlternateViewFromString(_htmlContent,Encoding.UTF8, MediaTypeNames.Text.Html);
                                                _htmlView.ContentType = new System.Net.Mime.ContentType(MediaTypeNames.Text.Html);
                                                _smtpMessage.AlternateViews.Add(_htmlView);
                                                await _SmtpClient.SendMailAsync(_smtpMessage,_stoppingToken);
                                                Log.Information($"Monitor[{_Device.Name}] sent notification via Smtp");
                                            }
                                            catch(Exception _SmtpException)
                                            {
                                                Log.Error(_SmtpException, $"Monitor[{_Device.Name}] failed to send notification via Smtp");
                                            }
                                        }
                                        else
                                        {
                                            Log.Warning($"Monitor[{_Device.Name}] detected unknown notification of type {_Method.Type} to {_Method.Address}.");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (_changeDetected)
                poll_changeCount++;
            else
                poll_nochangeCount++;
            Log.Debug($"Monitor[{_Device.Name}] processed state change.");           
        }
        private async Task<Dictionary<string,string>> DictionaryDiff(Dictionary<string,string> _dict1, Dictionary<string,string> _dict2)
        {
            Dictionary<string,string> _result = new  Dictionary<string, string>();

            foreach(KeyValuePair<string,string> _item in _dict1)
                if (_dict2.ContainsKey(_item.Key))
                    if (_dict2[_item.Key] != _item.Value)
                        _result.Add(_item.Key,_item.Value);

            foreach(KeyValuePair<string,string> _item in _dict2)
                if (_dict1.ContainsKey(_item.Key))
                    if (_dict1[_item.Key] != _item.Value)
                        if (!_result.ContainsKey(_item.Key))
                            _result.Add(_item.Key,_item.Value);
    
            return _result; 
        }
        private async Task<Dictionary<string,string>> XmlToDictionary(string _XmlDocumentData)
        {
            Dictionary<string, string> _XmlDictionary = new Dictionary<string, string>();
            try {
                XmlDocument _XmlDocument = new XmlDocument();
                _XmlDocument.LoadXml(_XmlDocumentData);
                foreach (XmlNode _XmlNode in _XmlDocument.SelectNodes("/datavalues/*"))
                {
                    _XmlDictionary[_XmlNode.Name] = _XmlNode.InnerText;
                } 
                return _XmlDictionary;
            }
            catch
            {
                return null;
            }
        }
        private async Task<string> DeviceStateToText(DeviceConfiguration _Device, Dictionary<string,string> _Changes)
        {
            StringBuilder _builder = new StringBuilder();
            _builder.AppendLine(new String('-',_Device.Name.Length)+"\r");
            foreach(KeyValuePair<string,string> _Change in _Changes)
            {
                _builder.AppendLine($"{_Change.Key}: {_Change.Value}\r");
            }
            string tmp_result = _builder.ToString();
            _builder.Clear();
            int _lineLength = await GetLongestLine(tmp_result);
            if (_Device.Name.Length > _lineLength)
                _lineLength = _Device.Name.Length;
            string hdr_ftr = new String('-',_lineLength);
            _builder.AppendLine(hdr_ftr);
            _builder.AppendLine($"{_Device.Name}");
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
        private async Task<string> DeviceStateToHTML(DeviceConfiguration _Device, Dictionary<string,string> _Changes)
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
            _builder.Append($"<b><span style=\"font-size:24.0pt;font-family:&quot;Verdana&quot;,sans-serif;color:black\">{_Device.Name}<o:p></o:p></span></b></p>\r\n</td>\r\n</tr>\r\n");
            foreach(KeyValuePair<string,string> _Change in _Changes)
            {
                _builder.Append("<tr>\r\n<td style=\"padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
                _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_Change.Key}<o:p></o:p></span></b></p>\r\n");
                _builder.Append("</td>\r\n<td style=\"background:gray;padding:3.0pt 3.0pt 3.0pt 3.0pt\">\r\n");
                _builder.Append($"<p class=\"MsoNormal\" align=\"center\" style=\"text-align:center\"><b><span style=\"font-size:10.5pt;font-family:&quot;Verdana&quot;,sans-serif;color:white\">{_Change.Value}<o:p></o:p></span></b></p>\r\n");
                _builder.Append("</td>\r\n</tr>\r\n");
            }
            _builder.Append("</tbody>\r\n</table>\r\n</div>\r\n<p class=\"MsoNormal\"><o:p>&nbsp;</o:p></p>\r\n</div>\r\n</body>\r\n</html>\r\n");
            return _builder.ToString();
        }
    }
}
