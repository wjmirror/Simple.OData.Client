using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

#pragma warning disable 1591

namespace Simple.OData.Client
{
    public abstract class ResponseReaderBase : IResponseReader
    {
        protected readonly ISession _session;

        protected ResponseReaderBase(ISession session)
        {
            _session = session;
        }

        public abstract Task<ODataResponse> GetResponseAsync(HttpResponseMessage responseMessage, bool includeAnnotationsInResults = false);

        public async Task AssignBatchActionResultsAsync(IODataClient client,
            ODataResponse batchResponse, IList<Func<IODataClient, Task>> actions, IList<int> responseIndexes)
        {
            var exceptions = new List<Exception>();
            for (var actionIndex = 0; actionIndex < actions.Count && !exceptions.Any(); actionIndex++)
            {
                var responseIndex = responseIndexes[actionIndex];
                if (responseIndex >= 0 && responseIndex < batchResponse.Batch.Count)
                {
                    var actionResponse = batchResponse.Batch[responseIndex];
                    if (actionResponse.Exception != null)
                    {
                        exceptions.Add(actionResponse.Exception);
                    }
                    else if (actionResponse.StatusCode >= (int)HttpStatusCode.BadRequest)
                    {
                        exceptions.Add(WebRequestException.CreateFromStatusCode((HttpStatusCode)actionResponse.StatusCode));
                    }
                    else
                    {
                        await actions[actionIndex](new ODataClient(client as ODataClient, actionResponse)).ConfigureAwait(false);
                    }
                }
            }

            if (exceptions.Any())
            {
                throw exceptions.First();
            }
        }

        protected abstract void ConvertEntry(ResponseNode entryNode, object entry, bool includeAnnotationsInResults);

        protected void StartFeed(Stack<ResponseNode> nodeStack, ODataFeedAnnotations feedAnnotations)
        {
            nodeStack.Push(new ResponseNode
            {
                Feed = new List<IDictionary<string, object>>(),
                FeedAnnotations = feedAnnotations,
            });
        }

        protected void EndFeed(Stack<ResponseNode> nodeStack, ODataFeedAnnotations feedAnnotations, ref ResponseNode rootNode)
        {
            var feedNode = nodeStack.Pop();
            if (feedNode.FeedAnnotations == null)
                feedNode.FeedAnnotations = feedAnnotations;
            else
                feedNode.FeedAnnotations.Merge(feedAnnotations);

            var entries = feedNode.Feed;
            if (nodeStack.Any())
            {
                var parent = nodeStack.Peek();
                parent.Feed = entries;
                parent.FeedAnnotations = feedNode.FeedAnnotations;
            }
            else
            {
                rootNode = feedNode;
            }
        }

        protected void StartEntry(Stack<ResponseNode> nodeStack)
        {
            nodeStack.Push(new ResponseNode
            {
                Entry = new Dictionary<string, object>()
            });
        }

        protected void EndEntry(Stack<ResponseNode> nodeStack, ref ResponseNode rootNode, object entry, bool includeAnnotationsInResults)
        {
            var entryNode = nodeStack.Pop();
            ConvertEntry(entryNode, entry, includeAnnotationsInResults);

            if (nodeStack.Any())
            {
                if (nodeStack.Peek().Feed != null)
                    nodeStack.Peek().Feed.Add(entryNode.Entry);
                else
                    nodeStack.Peek().Entry = entryNode.Entry;
            }
            else
            {
                rootNode = entryNode;
            }
        }

        protected void StartNavigationLink(Stack<ResponseNode> nodeStack, string linkName)
        {
            nodeStack.Push(new ResponseNode
            {
                LinkName = linkName,
            });
        }

        protected void EndNavigationLink(Stack<ResponseNode> nodeStack, bool includeAnnotationsInResults)
        {
            var linkNode = nodeStack.Pop();
            var parent = nodeStack.Peek();
            if (linkNode.Value != null)
            {
                var linkValue = linkNode.Value;
                if (linkValue is IDictionary<string, object> && !(linkValue as IDictionary<string, object>).Any())
                    linkValue = null;
                parent.Entry.Add(linkNode.LinkName, linkValue);
            }
            if (includeAnnotationsInResults && linkNode.FeedAnnotations != null)
                parent.Entry.Add(string.Format("{0}_{1}", FluentCommand.AnnotationsLiteral, linkNode.LinkName), linkNode.FeedAnnotations);
        }
    }
}