using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Data.Edm;
using Microsoft.Data.OData;

#pragma warning disable 1591

namespace Simple.OData.Client.V3.Adapter
{
    public class ResponseReader : ResponseReaderBase
    {
        private readonly IEdmModel _model;
        private bool _hasResponse = false;

        public ResponseReader(ISession session, IEdmModel model)
            : base(session)
        {
            _model = model;
        }

        public override Task<ODataResponse> GetResponseAsync(HttpResponseMessage responseMessage, bool includeAnnotationsInResults = false)
        {
            return GetResponseAsync(new ODataResponseMessage(responseMessage), includeAnnotationsInResults);
        }

#if SILVERLIGHT
        public async Task<ODataResponse> GetResponseAsync(IODataResponseMessage responseMessage, bool includeAnnotationsInResults = false)
#else
        public async Task<ODataResponse> GetResponseAsync(IODataResponseMessageAsync responseMessage, bool includeAnnotationsInResults = false)
#endif
        {
            var readerSettings = new ODataMessageReaderSettings();
            readerSettings.MessageQuotas.MaxReceivedMessageSize = Int32.MaxValue;
            readerSettings.ShouldIncludeAnnotation = x => _session.Settings.IncludeAnnotationsInResults;
            using (var messageReader = new ODataMessageReader(responseMessage, readerSettings, _model))
            {
                var payloadKind = messageReader.DetectPayloadKind();
                if (payloadKind.Any(x => x.PayloadKind != ODataPayloadKind.Property))
                    _hasResponse = true;

                if (payloadKind.Any(x => x.PayloadKind == ODataPayloadKind.Error))
                {
                    return ODataResponse.FromStatusCode(responseMessage.StatusCode);
                }
                else if (payloadKind.Any(x => x.PayloadKind == ODataPayloadKind.Value))
                {
                    if (payloadKind.Any(x => x.PayloadKind == ODataPayloadKind.Collection))
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
#if SILVERLIGHT
                        var stream = responseMessage.GetStream();
#else
                        var stream = await responseMessage.GetStreamAsync();
#endif
                        var text = Client.Utils.StreamToString(stream, responseMessage is ODataBatchOperationResponseMessage);
                        return ODataResponse.FromFeed(new[] { new Dictionary<string, object>()
                        {
                            { FluentCommand.ResultLiteral, text }
                        } });
                    }
                }
                else if (payloadKind.Any(x => x.PayloadKind == ODataPayloadKind.Batch))
                {
                    return await ReadResponse(messageReader.CreateODataBatchReader(), includeAnnotationsInResults);
                }
                else if (payloadKind.Any(x => x.PayloadKind == ODataPayloadKind.Feed))
                {
                    return ReadResponse(messageReader.CreateODataFeedReader(), includeAnnotationsInResults);
                }
                else if (payloadKind.Any(x => x.PayloadKind == ODataPayloadKind.Collection))
                {
                    return ReadResponse(messageReader.CreateODataCollectionReader(), includeAnnotationsInResults);
                }
                else if (payloadKind.Any(x => x.PayloadKind == ODataPayloadKind.Property))
                {
                    var property = messageReader.ReadProperty();
                    if (property.Value != null && (property.Value.GetType() != typeof(string) || !string.IsNullOrEmpty(property.Value.ToString())))
                        _hasResponse = true;

                    if (_hasResponse)
                    {
                        return ODataResponse.FromFeed(new[] { new Dictionary<string, object>()
                        {
                            { property.Name ?? FluentCommand.ResultLiteral, GetPropertyValue(property.Value) }
                        } });
                    }
                    else
                    {
                        return ODataResponse.FromFeed(new Dictionary<string, object>[] {});
                    }
                }
                else
                {
                    return ReadResponse(messageReader.CreateODataEntryReader(), includeAnnotationsInResults);
                }
            }
        }

        private async Task<ODataResponse> ReadResponse(ODataBatchReader odataReader, bool includeAnnotationsInResults)
        {
            var batch = new List<ODataResponse>();

            while (odataReader.Read())
            {
                switch (odataReader.State)
                {
                    case ODataBatchReaderState.ChangesetStart:
                        break;
                    case ODataBatchReaderState.Operation:
                        var operationMessage = odataReader.CreateOperationResponseMessage();
                        if (operationMessage.StatusCode == (int)HttpStatusCode.NoContent)
                            batch.Add(ODataResponse.FromStatusCode(operationMessage.StatusCode));
                        else if (operationMessage.StatusCode >= (int)HttpStatusCode.BadRequest)
                            batch.Add(ODataResponse.FromStatusCode(
                                operationMessage.StatusCode,
#if SILVERLIGHT
                                operationMessage.GetStream()));
#else
                                await operationMessage.GetStreamAsync()));
#endif
                        else
                            batch.Add(await GetResponseAsync(operationMessage));
                        break;
                    case ODataBatchReaderState.ChangesetEnd:
                        break;
                }
            }

            return ODataResponse.FromBatch(batch);
        }

        private ODataResponse ReadResponse(ODataCollectionReader odataReader, bool includeAnnotationsInResults)
        {
            var collection = new List<object>();

            while (odataReader.Read())
            {
                if (odataReader.State == ODataCollectionReaderState.Completed)
                    break;

                switch (odataReader.State)
                {
                    case ODataCollectionReaderState.CollectionStart:
                        break;

                    case ODataCollectionReaderState.Value:
                        collection.Add(GetPropertyValue(odataReader.Item));
                        break;

                    case ODataCollectionReaderState.CollectionEnd:
                        break;
                }
            }

            return ODataResponse.FromCollection(collection);
        }

        private ODataResponse ReadResponse(ODataReader odataReader, bool includeAnnotationsInResults)
        {
            ResponseNode rootNode = null;
            var nodeStack = new Stack<ResponseNode>();

            while (odataReader.Read())
            {
                if (odataReader.State == ODataReaderState.Completed)
                    break;

                switch (odataReader.State)
                {
                    case ODataReaderState.FeedStart:
                        StartFeed(nodeStack, CreateAnnotations(odataReader.Item as ODataFeed));
                        break;

                    case ODataReaderState.FeedEnd:
                        EndFeed(nodeStack, CreateAnnotations(odataReader.Item as ODataFeed), ref rootNode);
                        break;

                    case ODataReaderState.EntryStart:
                        StartEntry(nodeStack);
                        break;

                    case ODataReaderState.EntryEnd:
                        EndEntry(nodeStack, ref rootNode, odataReader.Item, includeAnnotationsInResults);
                        break;

                    case ODataReaderState.NavigationLinkStart:
                        StartNavigationLink(nodeStack, (odataReader.Item as ODataNavigationLink).Name);
                        break;

                    case ODataReaderState.NavigationLinkEnd:
                        EndNavigationLink(nodeStack, includeAnnotationsInResults);
                        break;
                }
            }

            return rootNode.Feed != null
                ? ODataResponse.FromFeed(rootNode.Feed, rootNode.FeedAnnotations)
                : ODataResponse.FromEntry(rootNode.Entry);
        }

        protected override void ConvertEntry(ResponseNode entryNode, object entry, bool includeAnnotationsInResults)
        {
            if (entry != null)
            {
                var odataEntry = entry as Microsoft.Data.OData.ODataEntry;
                foreach (var property in odataEntry.Properties)
                {
                    entryNode.Entry.Add(property.Name, GetPropertyValue(property.Value));
                }
                if (includeAnnotationsInResults)
                {
                    var resourceType = odataEntry.TypeName;
                    entryNode.Entry.Add(FluentCommand.ResourceTypeLiteral, resourceType.Split('.').Last());
                    var annotations = CreateAnnotations(odataEntry);
                    entryNode.Entry.Add(FluentCommand.AnnotationsLiteral, annotations);
                }
            }
        }

        private ODataFeedAnnotations CreateAnnotations(ODataFeed feed)
        {
            return new ODataFeedAnnotations()
            {
                Id = feed.Id,
                Count = feed.Count,
                DeltaLink = feed.DeltaLink,
                NextPageLink = feed.NextPageLink,
                InstanceAnnotations = feed.InstanceAnnotations,
            };
        }

        private ODataEntryAnnotations CreateAnnotations(Microsoft.Data.OData.ODataEntry odataEntry)
        {
            string id = null;
            Uri readLink = null;
            Uri editLink = null;
            if (_session.Adapter.GetMetadata().IsTypeWithId(odataEntry.TypeName))
            {
                id = odataEntry.Id;
                readLink = odataEntry.ReadLink;
                editLink = odataEntry.EditLink;
            }

            return new ODataEntryAnnotations
            {
                Id = id,
                TypeName = odataEntry.TypeName,
                ReadLink = readLink,
                EditLink = editLink,
                ETag = odataEntry.ETag,
                AssociationLinks = odataEntry.AssociationLinks == null
                    ? null
                    : new List<ODataEntryAnnotations.AssociationLink>(
                    odataEntry.AssociationLinks.Select(x => new ODataEntryAnnotations.AssociationLink
                    {
                        Name = x.Name,
                        Uri = x.Url,
                    })),
                MediaResource = CreateAnnotations(odataEntry.MediaResource),
                InstanceAnnotations = odataEntry.InstanceAnnotations,
            };
        }

        private ODataMediaAnnotations CreateAnnotations(ODataStreamReferenceValue value)
        {
            return value == null ? null : new ODataMediaAnnotations
            {
                ContentType = value.ContentType,
                ReadLink = value.ReadLink,
                EditLink = value.EditLink,
                ETag = value.ETag,
            };
        }

        private object GetPropertyValue(object value)
        {
            if (value is ODataComplexValue)
            {
                return (value as ODataComplexValue).Properties.ToDictionary(
                    x => x.Name, x => GetPropertyValue(x.Value));
            }
            else if (value is ODataCollectionValue)
            {
                return (value as ODataCollectionValue).Items.Cast<object>()
                    .Select(GetPropertyValue).ToList();
            }
            else
            {
                return value;
            }
        }
    }
}