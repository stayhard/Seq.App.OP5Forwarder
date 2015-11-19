using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Seq.Apps;
using Seq.Apps.LogEvents;
using Serilog.Events;

namespace Seq.App.OP5Forwarder
{
    [SeqApp("OP5Forwarder", Description = "Description")]
    public class OP5ForwarderReactor : Reactor, ISubscribeTo<LogEventData>
    {

        private readonly HttpClient _client;

        [SeqAppSetting(DisplayName = "Username")]
        public string Username { get; set; }

        [SeqAppSetting(DisplayName = "Password", InputType = SettingInputType.Password)]
        public string Password { get; set; }

        [SeqAppSetting(DisplayName = "OP5 Hostname")]
        public string Op5Host { get; set; }

        [SeqAppSetting(DisplayName = "OP5 Api Endpoint")]
        public string Op5ApiEndpoint { get; set; }

        public OP5ForwarderReactor()
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            _client = new HttpClient();
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            var credentials = $"{Username}:{Password}";
            var basicAuth = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials))}";
            _client.DefaultRequestHeaders.Add("Authorization", basicAuth);

            Op5ApiEndpoint += "/api/command/PROCESS_SERVICE_CHECK_RESULT?format=json";
        }

        public void On(Event<LogEventData> evt)
        {
            if (!evt.Data.Properties.ContainsKey("Service") ||
                !evt.Data.Properties.ContainsKey("Status") ||
                !evt.Data.Properties.ContainsKey("Message"))
            {
                Log.ForContext("Seq app instance", Host.InstanceName).Error("Malformed seq event {0} in the signal", evt.Id);
                return;
            }

            var msg = new Dictionary<string, string>
            {
                { "host_name", Op5Host},
                { "service_description", evt.Data.Properties["Service"].ToString() },
                { "status_code", evt.Data.Properties["Status"].ToString() },
                { "plugin_output", evt.Data.Properties["Message"].ToString() }
            };

            SendMessage(msg);
        }

        private async void SendMessage(Dictionary<string, string> msg)
        {
            var content = new StringContent(JsonConvert.SerializeObject(msg), Encoding.UTF8, "application/json");

            try
            {
                var post = await _client.PostAsync(Op5ApiEndpoint, content);

                if (!(post.StatusCode == HttpStatusCode.Created || post.StatusCode == HttpStatusCode.OK))
                {
                    var responseMsg = await post.Content.ReadAsStringAsync();
                    Log.ForContext("Seq app instance", Host.InstanceName).Error("Received wrong status code from OP5 {0}, reason {1}", post.StatusCode, responseMsg);
                }
            }
            catch (Exception ex)
            {
                Log.ForContext("Seq app instance", Host.InstanceName).Error(ex, "Exception");
            }
        }
    }
}
