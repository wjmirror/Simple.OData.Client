using System;
using System.Net.Http;
using Simple.OData.Client.Core.Http;

namespace Simple.OData.Client
{
    public class HttpConnection : IDisposable
    {
        public static IHttpClientFactory HttpClientFactory { get; set; }

        static HttpConnection()
        {
            HttpClientFactory = new HttpClientFactory();
        }

        private HttpClient _httpClient;

        public HttpClient HttpClient => _httpClient;

        public HttpConnection(ODataClientSettings settings)
        {
            _httpClient = HttpClientFactory.Create(settings);

            //var messageHandler = CreateMessageHandler(settings);
            //_httpClient = CreateHttpClient(settings, messageHandler);
        }

        public void Dispose()
        {
            if (_httpClient != null)
            {
                // Let the factory handle it as it made it
                HttpClientFactory.Release(_httpClient);
                _httpClient = null;
            }
        }

        private static HttpClient CreateHttpClient(ODataClientSettings settings, HttpMessageHandler messageHandler)
        {
            var client = new HttpClient(messageHandler);
            if (settings.RequestTimeout >= TimeSpan.FromMilliseconds(1))
            {
                client.Timeout = settings.RequestTimeout;
            }

            return client;
        }

        private static HttpMessageHandler CreateMessageHandler(ODataClientSettings settings)
        {
            if (settings.OnCreateMessageHandler != null)
            {
                return settings.OnCreateMessageHandler();
            }

            var clientHandler = new HttpClientHandler();

            // Perform this test to prevent failure to access Credentials/PreAuthenticate properties on SL5
            if (settings.Credentials != null)
            {
                clientHandler.Credentials = settings.Credentials;
                if (clientHandler.SupportsPreAuthenticate())
                {
                    clientHandler.PreAuthenticate = true;
                }
            }

            settings.OnApplyClientHandler?.Invoke(clientHandler);

            return clientHandler;
        }
    }
}