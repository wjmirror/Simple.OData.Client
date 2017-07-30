using System;
#if NET40
using System.Collections.Concurrent;
#else
using System.Collections.Generic;
#endif
using System.Net.Http;

namespace Simple.OData.Client.Core.Http
{
    /// <summary>
    /// Creates and potentially caches <see cref="HttpClient"/> instances.
    /// </summary>
    public class HttpClientFactory : IHttpClientFactory
    {
#if NET40
        static readonly ConcurrentDictionary<string, HttpClient> _instances = new ConcurrentDictionary<string, HttpClient>();
#else
        static readonly object syncLock = new object();
        static readonly IDictionary<string, HttpClient> _instances = new Dictionary<string, HttpClient>();
#endif

        public HttpClient Create(ODataClientSettings settings)
        {
            return OnCreate(settings);

            if (settings.RenewHttpConnection)
            {
                // Just return a local value and don't cache it
                return OnCreate(settings);
            }

            // Key it off the URI as we might have different HttpHandlers/BeforeRequest for each OData service.
            var key = Key(settings.BaseUri);
#if NET40
            return _instances.GetOrAdd(key, x => OnCreate(settings));
#else
            lock (syncLock)
            {
                HttpClient found;
                if (!_instances.TryGetValue(key, out found))
                {
                    _instances[key] = found = OnCreate(settings);
                }

                return found;
            }
#endif
        }

        public void Clear()
        {
#if NET40
            _instances.Clear();
#else
            lock (syncLock)
            {
                _instances.Clear();
            }
#endif
        }

        public void Clear(Uri uri)
        {
#if NET40
            HttpClient ignored;
            _instances.TryRemove(Key(uri), out ignored);
#else
            lock (syncLock)
            {
                _instances.Remove(Key(uri));
            }
#endif
        }

        public HttpClient Get(Uri uri)
        {
            HttpClient found;
#if NET40
            _instances.TryGetValue(Key(uri), out found);
#else
            lock (syncLock)
            {
                _instances.TryGetValue(Key(uri), out found);
            }
#endif

            return found;
        }

        public virtual void Release(HttpClient httpClient)
        {
            var cached = Get(httpClient.BaseAddress);
            if (httpClient != cached)
            {
                // Only dispose if we're not in the cache.
                httpClient.Dispose();
            }
        }

        protected virtual HttpClient OnCreate(ODataClientSettings settings)
        {
            var messageHandler = CreateMessageHandler(settings);
            var client = new HttpClient(messageHandler);
            if (settings.RequestTimeout >= TimeSpan.FromMilliseconds(1))
            {
                client.Timeout = settings.RequestTimeout;
            }

            return client;
        }

        protected virtual HttpMessageHandler CreateMessageHandler(ODataClientSettings settings)
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

        private string Key(Uri uri)
        {
            return uri == null ? string.Empty : uri.ToString();
        }
    }
}