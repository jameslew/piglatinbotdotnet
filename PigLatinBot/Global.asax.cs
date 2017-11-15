using Microsoft.Azure;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Connector;
using Microsoft.IdentityModel.Protocols;
using Microsoft.WindowsAzure.Storage;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Routing;

namespace PigLatinBot
{
    public class WebApiApplication : System.Web.HttpApplication
    {

        CancellationTokenSource _getTokenAsyncCancellation = new CancellationTokenSource();

        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);

            var appID = ConfigurationManager.AppSettings["MicrosoftAppId"];
            var appPassword = ConfigurationManager.AppSettings["MicrosoftAppPassword"];
            Trace.WriteLine("Application Started");

            if (!string.IsNullOrEmpty(appID) && !string.IsNullOrEmpty(appPassword))
            {
                var credentials = new MicrosoftAppCredentials(appID, appPassword);

                Task.Factory.StartNew(async () =>
                {
                    while (!_getTokenAsyncCancellation.IsCancellationRequested)
                    {
                        try
                        {
                            var token = await credentials.GetTokenAsync().ConfigureAwait(false);
                        }
                        catch (MicrosoftAppCredentials.OAuthException ex)
                        {
                            Trace.TraceError(ex.Message);
                        }
                        await Task.Delay(TimeSpan.FromMinutes(30), _getTokenAsyncCancellation.Token).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            }
        }

        protected void Application_End()
        {
            _getTokenAsyncCancellation.Cancel();
        }

    }
}
