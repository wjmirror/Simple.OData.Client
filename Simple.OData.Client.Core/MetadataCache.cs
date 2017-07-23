using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;

namespace Simple.OData.Client
{
    class MetadataCache
    {
        public static readonly SimpleDictionary<string, MetadataCache> Instances = new SimpleDictionary<string, MetadataCache>();

        private string _metadataDocument;

        public bool IsResolved()
        {
            return _metadataDocument != null;
        }

        public string MetadataDocument
        {
            get
            {
                if (_metadataDocument == null)
                    throw new InvalidOperationException("Service metadata is not resolved");

                return _metadataDocument;
            }
        }

        public IList<string> ProtocolVersions { get; private set; }

        public static void ClearAll()
        {
            foreach (var md in Instances.Values)
            {
                md.Clear();
            }
        }

        public void Clear()
        {
            lock (this)
            {
                _metadataDocument = null;
                ProtocolVersions = new List<string>();
            }
        }

        public void SetMetadataDocument(string metadataString)
        {
            _metadataDocument = metadataString;

            ProtocolVersions = new List<string>{ GetMetadataProtocolVersion(metadataString) };
        }

        public async Task SetMetadataDocument(HttpResponseMessage response)
        {
            _metadataDocument = await GetMetadataDocumentAsync(response).ConfigureAwait(false);

            var x = await GetSupportedProtocolVersionsAsync(response).ConfigureAwait(false);
            ProtocolVersions = new List<string>(x);
        }

        private async Task<string> GetMetadataDocumentAsync(HttpResponseMessage response)
        {
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private async Task<IEnumerable<string>> GetSupportedProtocolVersionsAsync(HttpResponseMessage response)
        {
            IEnumerable<string> headerValues;
            if (response.Headers.TryGetValues(HttpLiteral.DataServiceVersion, out headerValues) ||
                response.Headers.TryGetValues(HttpLiteral.ODataVersion, out headerValues))
            {
                return headerValues.SelectMany(x => x.Split(';')).Where(x => x.Length > 0);
            }
            else
            {
                try
                {
                    var metadataString = await GetMetadataDocumentAsync(response).ConfigureAwait(false);
                    var protocolVersion = GetMetadataProtocolVersion(metadataString);
                    return new[] { protocolVersion };
                }
                catch (Exception)
                {
                    throw new InvalidOperationException("Unable to identify OData protocol version");
                }
            }
        }

        private string GetMetadataProtocolVersion(string metadataString)
        {
            var reader = XmlReader.Create(new StringReader(metadataString));
            reader.MoveToContent();

            var protocolVersion = reader.GetAttribute("Version");

            if (protocolVersion == ODataProtocolVersion.V1 ||
                protocolVersion == ODataProtocolVersion.V2 ||
                protocolVersion == ODataProtocolVersion.V3)
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        var version = reader.GetAttribute("m:" + HttpLiteral.MaxDataServiceVersion);
                        if (string.IsNullOrEmpty(version))
                            version = reader.GetAttribute("m:" + HttpLiteral.DataServiceVersion);
                        if (!string.IsNullOrEmpty(version) && string.Compare(version, protocolVersion, StringComparison.Ordinal) > 0)
                            protocolVersion = version;

                        break;
                    }
                }
            }

            return protocolVersion;
        }
    }
}
