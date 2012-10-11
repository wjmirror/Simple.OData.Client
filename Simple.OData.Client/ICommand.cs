﻿using System.Collections.Generic;
using System.Linq;

namespace Simple.OData.Client
{
    public interface ICommand
    {
        IClientWithCommand From(string collectionName);
        IClientWithCommand Get(IDictionary<string, object> entryKey);
        IClientWithCommand Filter(string filter);
        IClientWithCommand Skip(int count);
        IClientWithCommand Top(int count);
        IClientWithCommand Expand(IEnumerable<string> associations);
        IClientWithCommand Expand(params string[] associations);
        IClientWithCommand Select(IEnumerable<string> columns);
        IClientWithCommand Select(params string[] columns);
        IClientWithCommand OrderBy(IEnumerable<string> columns, bool descending = false);
        IClientWithCommand OrderBy(params string[] columns);
        IClientWithCommand OrderByDescending(IEnumerable<string> columns);
        IClientWithCommand OrderByDescending(params string[] columns);
        IClientWithCommand NavigateTo(string linkName);
        IClientWithCommand Function(string functionName);
        IClientWithCommand Parameters(IDictionary<string, object> parameters);
    }
}
