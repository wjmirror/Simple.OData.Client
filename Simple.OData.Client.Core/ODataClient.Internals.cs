using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Simple.OData.Client.Extensions;

namespace Simple.OData.Client
{
    public partial class ODataClient
    {
        private async Task<IDictionary<string, object>> ExecuteInsertEntryAsync(FluentCommand command, bool resultRequired, CancellationToken cancellationToken)
        {
            var entryData = command.CommandData;
            var commandText = await command.GetCommandTextAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            var request = await _session.Adapter.GetRequestWriter(_lazyBatchWriter)
                .CreateInsertRequestAsync(command.QualifiedEntityCollectionName, commandText, entryData, resultRequired).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            var result = await ExecuteRequestWithResultAsync(request, cancellationToken,
                x => x.AsEntry(), () => null, () => request.EntryData).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            var keyNames = _session.Metadata.GetDeclaredKeyPropertyNames(commandText);
            if (result == null && resultRequired && Utils.AllMatch(keyNames, entryData.Keys, _session.Pluralizer))
            {
                result = await this.GetEntryAsync(commandText, entryData, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
            }

            return result;
        }

        private async Task<IDictionary<string, object>> ExecuteUpdateEntryAsync(FluentCommand command, bool resultRequired, CancellationToken cancellationToken)
        {
            AssertHasKey(command);

            var collectionName = command.QualifiedEntityCollectionName;
            var entryKey = command.HasKey ? command.KeyValues : command.FilterAsKey;
            var entryData = command.CommandData;
            var entryIdent = await FormatEntryKeyAsync(command, cancellationToken).ConfigureAwait(false);

            var request = await _session.Adapter.GetRequestWriter(_lazyBatchWriter).CreateUpdateRequestAsync(collectionName, entryIdent, entryKey, entryData, resultRequired).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            var result = await ExecuteRequestWithResultAsync(request, cancellationToken,
                x => x.AsEntry(), () => null, () => request.EntryData).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            if (result == null && resultRequired)
            {
                try
                {
                    result = await GetUpdatedResult(command, cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
                }
                catch (Exception)
                {
                }
            }

            var entityCollection = _session.Metadata.GetEntityCollection(collectionName);
            var entryDetails = _session.Metadata.ParseEntryDetails(entityCollection.Name, entryData);

            var removedLinks = entryDetails.Links
                .SelectMany(x => x.Value.Where(y => y.LinkData == null))
                .Select(x => _session.Metadata.GetNavigationPropertyExactName(entityCollection.Name, x.LinkName))
                .ToList();

            foreach (var associationName in removedLinks)
            {
                try
                {
                    await UnlinkEntryAsync(collectionName, entryKey, associationName, cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
                }
                catch (Exception)
                {
                }
            }

            return result;
        }

        private async Task<IDictionary<string, object>> GetUpdatedResult(FluentCommand command, CancellationToken cancellationToken)
        {
            var entryKey = command.HasKey ? command.KeyValues : command.FilterAsKey;
            var entryData = command.CommandData;

            var updatedKey = entryKey.Where(x => !entryData.ContainsKey(x.Key)).ToIDictionary();
            foreach (var item in entryData.Where(x => entryKey.ContainsKey(x.Key)))
            {
                updatedKey.Add(item);
            }
            var updatedCommand = new FluentCommand(command).Key(updatedKey);
            return await FindEntryAsync(await updatedCommand.GetCommandTextAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }

        private async Task<IEnumerable<IDictionary<string, object>>> ExecuteUpdateEntriesAsync(FluentCommand command, bool resultRequired, CancellationToken cancellationToken)
        {
            return await IterateEntriesAsync(
                command, resultRequired,
                async (x, y, z, w) => await UpdateEntryAsync(x, y, z, w, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task ExecuteDeleteEntryAsync(FluentCommand command, CancellationToken cancellationToken)
        {
            var collectionName = command.QualifiedEntityCollectionName;
            var entryIdent = await FormatEntryKeyAsync(command, cancellationToken).ConfigureAwait(false);

            var request = await _session.Adapter.GetRequestWriter(_lazyBatchWriter)
                .CreateDeleteRequestAsync(collectionName, entryIdent).ConfigureAwait(false);
            if (!IsBatchRequest)
            {
                using (await _requestRunner.ExecuteRequestAsync(request, cancellationToken).ConfigureAwait(false))
                {
                }
            }
        }

        private async Task<int> ExecuteDeleteEntriesAsync(FluentCommand command, CancellationToken cancellationToken)
        {
            return await IterateEntriesAsync(
                command,
                async (x, y) => await DeleteEntryAsync(x, y, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task ExecuteLinkEntryAsync(FluentCommand command, string linkName, IDictionary<string, object> linkedEntryKey, CancellationToken cancellationToken)
        {
            AssertHasKey(command);

            var collectionName = command.QualifiedEntityCollectionName;
            var entryKey = command.HasKey ? command.KeyValues : command.FilterAsKey;

            var entryIdent = await FormatEntryKeyAsync(collectionName, entryKey, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            var linkedCollection = _session.Metadata.GetNavigationPropertyPartnerTypeName(collectionName, linkName);
            var linkIdent = await FormatEntryKeyAsync(linkedCollection, linkedEntryKey, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            var request = await _session.Adapter.GetRequestWriter(_lazyBatchWriter)
                .CreateLinkRequestAsync(collectionName, linkName, entryIdent, linkIdent).ConfigureAwait(false);

            if (!IsBatchRequest)
            {
                using (await _requestRunner.ExecuteRequestAsync(request, cancellationToken).ConfigureAwait(false))
                {
                }
            }
        }

        private async Task ExecuteUnlinkEntryAsync(FluentCommand command, string linkName, IDictionary<string, object> linkedEntryKey, CancellationToken cancellationToken)
        {
            AssertHasKey(command);

            var collectionName = command.QualifiedEntityCollectionName;
            var entryKey = command.HasKey ? command.KeyValues : command.FilterAsKey;

            var entryIdent = await FormatEntryKeyAsync(collectionName, entryKey, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            string linkIdent = null;
            if (linkedEntryKey != null)
            {
                var linkedCollection = _session.Metadata.GetNavigationPropertyPartnerTypeName(collectionName, linkName);
                linkIdent = await FormatEntryKeyAsync(linkedCollection, linkedEntryKey, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
            }

            var request = await _session.Adapter.GetRequestWriter(_lazyBatchWriter)
                .CreateUnlinkRequestAsync(collectionName, linkName, entryIdent, linkIdent).ConfigureAwait(false);

            if (!IsBatchRequest)
            {
                using (await _requestRunner.ExecuteRequestAsync(request, cancellationToken).ConfigureAwait(false))
                {
                }
            }
        }

        private async Task<IEnumerable<IDictionary<string, object>>> ExecuteFunctionAsync(FluentCommand command, CancellationToken cancellationToken)
        {
            var commandText = await command.GetCommandTextAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            var request = await _session.Adapter.GetRequestWriter(_lazyBatchWriter)
                .CreateFunctionRequestAsync(commandText, command.FunctionName).ConfigureAwait(false);

            return await ExecuteRequestWithResultAsync(request, cancellationToken,
                x => x.AsEntries(),
                () => new IDictionary<string, object>[] { }).ConfigureAwait(false);
        }

        private async Task<IEnumerable<IDictionary<string, object>>> ExecuteActionAsync(FluentCommand command, CancellationToken cancellationToken)
        {
            var commandText = await command.GetCommandTextAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            var request = await _session.Adapter.GetRequestWriter(_lazyBatchWriter)
                .CreateActionRequestAsync(commandText, command.ActionName, command.CommandData, true).ConfigureAwait(false);

            return await ExecuteRequestWithResultAsync(request, cancellationToken,
                x => x.AsEntries(),
                () => new IDictionary<string, object>[] { }).ConfigureAwait(false);
        }

        private async Task ExecuteBatchActionsAsync(IList<Func<IODataClient, Task>> actions, CancellationToken cancellationToken)
        {
            if (!actions.Any())
                return;

            var responseIndexes = new List<int>();
            var request = await _lazyBatchWriter.Value.CreateBatchRequestAsync(this, actions, responseIndexes).ConfigureAwait(false);
            if (request != null)
            {
                // Execute batch and get response
                using (var response = await _requestRunner.ExecuteRequestAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var responseReader = _session.Adapter.GetResponseReader();
                    var batchResponse = await responseReader.GetResponseAsync(response, _session.Settings.IncludeAnnotationsInResults).ConfigureAwait(false);

                    // Replay batch operations to assign results
                    await responseReader.AssignBatchActionResultsAsync(this, batchResponse, actions, responseIndexes).ConfigureAwait(false);
                }
            }
        }

        private async Task<T> ExecuteRequestWithResultAsync<T>(ODataRequest request, CancellationToken cancellationToken,
            Func<ODataResponse, T> createResult, Func<T> createEmptyResult, Func<T> createBatchResult = null)
        {
            if (IsBatchRequest)
                return createBatchResult != null 
                    ? createBatchResult() 
                    : createEmptyResult != null
                    ? createEmptyResult() 
                    : default(T);

            try
            {
                using (var response = await _requestRunner.ExecuteRequestAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NoContent &&
                        (request.Method == RestVerbs.Get || request.ResultRequired))
                    {
                        var responseReader = _session.Adapter.GetResponseReader();
                        return createResult(await responseReader.GetResponseAsync(response, _session.Settings.IncludeAnnotationsInResults).ConfigureAwait(false));
                    }
                    else
                    {
                        return default(T);
                    }
                }
            }
            catch (WebRequestException ex)
            {
                if (_settings.IgnoreResourceNotFoundException && ex.Code == HttpStatusCode.NotFound)
                    return createEmptyResult != null ? createEmptyResult() : default(T);
                else
                    throw;
            }
        }

        private async Task<IEnumerable<IDictionary<string, object>>> IterateEntriesAsync(
            FluentCommand command, bool resultRequired,
            Func<string, IDictionary<string, object>, IDictionary<string, object>, bool, Task<IDictionary<string, object>>> funcAsync, CancellationToken cancellationToken)
        {
            var collectionName = command.QualifiedEntityCollectionName;
            var entryData = command.CommandData;
            var commandText = await command.GetCommandTextAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<IDictionary<string, object>> result = null;
            var client = new ODataClient(_settings);
            var entries = await client.FindEntriesAsync(commandText, cancellationToken).ConfigureAwait(false);
            if (entries != null)
            {
                var entryList = entries.ToList();
                var resultList = new List<IDictionary<string, object>>();
                foreach (var entry in entryList)
                {
                    resultList.Add(await funcAsync(collectionName, entry, entryData, resultRequired).ConfigureAwait(false));
                    if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
                }
                result = resultList;
            }

            return result;
        }

        private async Task<int> IterateEntriesAsync(FluentCommand command,
            Func<string, IDictionary<string, object>, Task> funcAsync, CancellationToken cancellationToken)
        {
            var collectionName = command.QualifiedEntityCollectionName;
            var commandText = await command.GetCommandTextAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();

            var result = 0;
            var client = new ODataClient(_settings);
            var entries = await client.FindEntriesAsync(commandText, cancellationToken).ConfigureAwait(false);
            if (entries != null)
            {
                var entryList = entries.ToList();
                foreach (var entry in entryList)
                {
                    await funcAsync(collectionName, entry).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
                    ++result;
                }
            }
            return result;
        }

        private void RemoveSystemProperties(IDictionary<string, object> entryData)
        {
            if (_settings.IncludeAnnotationsInResults && entryData.ContainsKey(FluentCommand.ResourceTypeLiteral))
            {
                var removeKeys = new List<string>();
                foreach (var entry in entryData)
                {
                    if (entry.Key == FluentCommand.ResourceTypeLiteral)
                        removeKeys.Add(entry.Key);
                    if (entry.Key == FluentCommand.AnnotationsLiteral || entry.Key.StartsWith(FluentCommand.AnnotationsLiteral + "_"))
                        removeKeys.Add(entry.Key);
                }
                foreach (var key in removeKeys)
                {
                    entryData.Remove(key);
                }
            }
        }

        private void AssertHasKey(FluentCommand command)
        {
            if (!command.HasKey && command.FilterAsKey == null)
                throw new InvalidOperationException("No entry key specified.");
        }

        private async Task<string> FormatEntryKeyAsync(string collection, IDictionary<string, object> entryKey, CancellationToken cancellationToken)
        {
            var entryIdent = await GetFluentClient()
                .For(collection)
                .Key(entryKey)
                .GetCommandTextAsync(cancellationToken).ConfigureAwait(false);

            return entryIdent;
        }

        private async Task<string> FormatEntryKeyAsync(FluentCommand command, CancellationToken cancellationToken)
        {
            var entryIdent = command.HasKey
                ? await command.GetCommandTextAsync(cancellationToken)
.ConfigureAwait(false) : await (new FluentCommand(command).Key(command.FilterAsKey).GetCommandTextAsync(cancellationToken)).ConfigureAwait(false);

            return entryIdent;
        }
    }
}
