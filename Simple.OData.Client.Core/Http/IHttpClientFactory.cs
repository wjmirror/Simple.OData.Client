using System;
using System.Net.Http;

namespace Simple.OData.Client.Core.Http
{
    /// <summary>
    /// Manages <see cref="HttpClient"/> instances.
    /// </summary>
    public interface IHttpClientFactory
    {
        /// <summary>
        /// Clear the <see cref="HttpClient"/> cache.
        /// </summary>
        void Clear();

        /// <summary>
        /// Clear the <see cref="HttpClient"/> cache for a specific URI
        /// </summary>
        /// <param name="uri">URI to use</param>
        void Clear(Uri uri);

        /// <summary>
        /// Create a <see cref="HttpClient"/> with handler configured using <see cref="ODataClientSettings.OnCreateMessageHandler"/>
        /// <para>
        /// Will be cached if <see cref="ODataClientSettings.RenewHttpConnection"/> is false.
        /// </para>
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        HttpClient Create(ODataClientSettings settings);

        /// <summary>
        /// Get a <see cref="HttpClient"/> for a specific URI
        /// </summary>
        /// <param name="uri"></param>
        /// <returns>HttpClient if found, otherwise null.</returns>
        HttpClient Get(Uri uri);

        /// <summary>
        /// Release the <see cref="HttpClient"/>, unless its in the cache
        /// </summary>
        /// <param name="httpClient"></param>
        void Release(HttpClient httpClient);
    }
}