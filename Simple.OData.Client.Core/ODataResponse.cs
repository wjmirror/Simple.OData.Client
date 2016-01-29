using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using Simple.OData.Client.Extensions;

#pragma warning disable 1591

namespace Simple.OData.Client
{
    public class ODataResponse
    {
        public int StatusCode { get; private set; }
        public IEnumerable<IDictionary<string, object>> Entries { get; private set; }
        public IDictionary<string, object> Entry { get; private set; }
        public ODataFeedAnnotations Annotations { get; private set; }
        public IList<ODataResponse> Batch { get; private set; }
        public Exception Exception { get; private set; }
        public string DynamicPropertiesContainerName { get; private set; }

        private ODataResponse()
        {
        }

        public IEnumerable<IDictionary<string, object>> AsEntries()
        {
            if (this.Entries != null)
            {
                return this.Entries.Any() && this.Entries.First().ContainsKey(FluentCommand.ResultLiteral)
                    ? this.Entries.Select(ExtractDictionary)
                    : this.Entries;
            }
            else
            {
                return (this.Entry != null
                ? new[] { ExtractDictionary(this.Entry) }
                : new IDictionary<string, object>[] { });
            }
        }

        public IEnumerable<T> AsEntries<T>(string dynamicPropertiesContainerName) where T : class
        {
            return this.AsEntries().Select(x => x.ToObject<T>(dynamicPropertiesContainerName));
        }

        public IDictionary<string, object> AsEntry()
        {
            var result = AsEntries();

            return result != null
                ? result.FirstOrDefault()
                : null;
        }

        public T AsEntry<T>(string dynamicPropertiesContainerName) where T : class
        {
            return this.AsEntry().ToObject<T>(dynamicPropertiesContainerName);
        }

        public T AsScalar<T>()
        {
            Func<IDictionary<string, object>, object> extractScalar = x => (x == null) || !x.Any() ? null : x.Values.First();
            var result = this.AsEntry();
            var value = result == null ? null : extractScalar(result);

            return value == null 
                ? default(T) 
                : (T)Utils.Convert(value, typeof(T));
        }

        public T[] AsArray<T>()
        {
            return this.AsEntries()
                .SelectMany(x => x.Values)
                .Select(x => (T)Utils.Convert(x, typeof(T)))
                .ToArray();
        }

        public static ODataResponse FromFeed(IEnumerable<IDictionary<string, object>> entries, ODataFeedAnnotations annotations = null)
        {
            return new ODataResponse
            {
                Entries = entries,
                Annotations = annotations,
            };
        }

        public static ODataResponse FromEntry(IDictionary<string, object> entry)
        {
            return new ODataResponse
            {
                Entry = entry,
            };
        }

        public static ODataResponse FromCollection(IList<object> collection)
        {
            return new ODataResponse
            {
                Entries = collection.Select(x => new Dictionary<string, object>() { { FluentCommand.ResultLiteral, x } }),
            };
        }

        public static ODataResponse FromBatch(IList<ODataResponse> batch)
        {
            return new ODataResponse
            {
                Batch = batch,
            };
        }

        public static ODataResponse FromStatusCode(int statusCode, Exception exception = null)
        {
            return new ODataResponse
            {
                StatusCode = statusCode,
                Exception = exception,
            };
        }

        public static ODataResponse FromStatusCode(int statusCode, Stream responseStream)
        {
            if (statusCode >= (int) HttpStatusCode.BadRequest)
            {
                var responseContent = Utils.StreamToString(responseStream, true);
                return new ODataResponse
                {
                    StatusCode = statusCode,
                    Exception = WebRequestException.CreateFromStatusCode((HttpStatusCode)statusCode, responseContent),
                };
            }
            else
            {
                return new ODataResponse
                {
                    StatusCode = statusCode,
                };
            }
        }

        private IDictionary<string, object> ExtractDictionary(IDictionary<string, object> value)
        {
            if (value != null && value.Keys.Count == 1 &&
                value.ContainsKey(FluentCommand.ResultLiteral) && 
                value.Values.First() is IDictionary<string, object>)
            {
                return value.Values.First() as IDictionary<string, object>;
            }
            else
            {
                return value;
            }
        }
    }
}