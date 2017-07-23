using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Simple.OData.Client
{
    class Session : ISession
    {
        private readonly AdapterFactory _adapterFactory;
        private IODataAdapter _adapter;
        private HttpConnection _httpConnection;

        public ODataClientSettings Settings { get; private set; }
        public MetadataCache MetadataCache { get; private set; }
        public IPluralizer Pluralizer { get; internal set; }

        private Session(Uri baseUri, string metadataString)
        {
            _adapterFactory = new AdapterFactory(this);

            this.Settings = new ODataClientSettings
            {
                BaseUri = baseUri,
                MetadataDocument = metadataString
            };
            this.MetadataCache = MetadataCache.Instances.GetOrAdd(baseUri.AbsoluteUri, new MetadataCache());
            this.Pluralizer = new SimplePluralizer();
        }

        private Session(ODataClientSettings settings)
        {
            if (settings.BaseUri == null || string.IsNullOrEmpty(settings.BaseUri.AbsoluteUri))
                throw new InvalidOperationException("Unable to create client session with no URI specified");

            _adapterFactory = new AdapterFactory(this);

            this.Settings = settings;
            this.MetadataCache = MetadataCache.Instances.GetOrAdd(this.Settings.BaseUri.AbsoluteUri, new MetadataCache());
            this.Pluralizer = new SimplePluralizer();
        }

        public void Dispose()
        {
            lock (this)
            {
                if (_httpConnection != null)
                {
                    _httpConnection.Dispose();
                    _httpConnection = null;
                }
            }
        }

        public void Trace(string message, params object[] messageParams)
        {
            if (this.Settings.OnTrace != null)
            {
                this.Settings.OnTrace(message, messageParams);
            }
        }

        public void ClearMetadataCache()
        {
            // NB Locks internally
            this.MetadataCache.Clear();
        }

        public async Task<IODataAdapter> ResolveAdapterAsync(CancellationToken cancellationToken)
        {
            if (this.Settings.PayloadFormat == ODataPayloadFormat.Unspecified)
                this.Settings.PayloadFormat = this.Adapter.DefaultPayloadFormat;

            return this.Adapter;
        }

        public IODataAdapter Adapter
        {
            get
            {
                if (_adapter == null)
                {
                    lock (this)
                    {
                        if (_adapter == null)
                            _adapter = CreateAdapter();
                    }
                }
                return _adapter;
            }
        }

        public IMetadata Metadata
        {
            get { return this.Adapter.GetMetadata(); }
        }

        public HttpConnection GetHttpConnection()
        {
            if (_httpConnection == null)
            {
                lock (this)
                {
                    if (_httpConnection == null)
                        _httpConnection = new HttpConnection(this.Settings);
                }
            }

            return _httpConnection;
        }

        internal static Session FromSettings(ODataClientSettings settings)
        {
            return new Session(settings);
        }

        internal static Session FromMetadata(Uri baseUri, string metadataString)
        {
            return new Session(baseUri, metadataString);
        }

        private async Task<HttpResponseMessage> SendMetadataRequestAsync(CancellationToken cancellationToken)
        {
            var request = new ODataRequest(RestVerbs.Get, this, ODataLiteral.Metadata);
            return await new RequestRunner(this).ExecuteRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private IODataAdapter CreateAdapter()
        {
            if (!this.MetadataCache.IsResolved())
            {
                // Ok, ensure we only do this once per uri
                lock (this.MetadataCache)
                {
                    // Check again now we have a lock
                    if (!this.MetadataCache.IsResolved())
                    {
                        if (!string.IsNullOrEmpty(this.Settings.MetadataDocument))
                        {
                            this.MetadataCache.SetMetadataDocument(this.Settings.MetadataDocument);
                        }
                        else
                        {
                            var response = SendMetadataRequestAsync(CancellationToken.None).GetAwaiter().GetResult();
                            // NB Can't do an async retrieval as we are in a critical section
                            Task.WaitAll(MetadataCache.SetMetadataDocument(response));
                        }
                    }
                }
            }

            // Will be populated by the time we get here - unless we've flushed the cache on a different thread!
            return _adapterFactory.CreateAdapter(this.MetadataCache);
        }
    }
}
