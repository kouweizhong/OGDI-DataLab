﻿using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Ogdi.DataServices.Helper;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;

namespace Ogdi.DataServices.v1
{
    public abstract class AbstractHttpHandler : IHttpHandler
    {
        #region Render variables

        // Setup namespaces
        protected static readonly XNamespace _nsAtom = XNamespace.Get("http://www.w3.org/2005/Atom");
        protected static readonly XNamespace _nsm = XNamespace.Get("http://schemas.microsoft.com/ado/2007/08/dataservices/metadata");
        protected static readonly XNamespace _nsd = XNamespace.Get("http://schemas.microsoft.com/ado/2007/08/dataservices");
        
        // Setup namespace specific names
        protected readonly XName _entryXName = _nsAtom + "entry";
        protected readonly XName _contentXName = _nsAtom + "content";
        protected readonly XName _propertiesXName = _nsm + "properties";
        protected readonly XName _linkXName = _nsAtom + "link";
        protected readonly XName _idXName = _nsAtom + "id";
        protected readonly XName _categoryXName = _nsAtom + "category";
        protected readonly XName _kmlSnippetXName = _nsd + "kmlsnippet";
        protected readonly XName _rdfSnippetXName = _nsd + "rdfsnippet";
        protected readonly XName _storageAccountNameXName = _nsd + "storageaccountname";
        protected readonly XName _storageAccountKeyXName = _nsd + "storageaccountkey";

        // Setup content types
        protected const string _xmlContentType = "application/xml;charset=utf-8";
        protected const string _atomXmlContentType = "application/atom+xml;charset=utf-8";

        #endregion

        protected const int MaxAzureTableResult = 1000;
        protected const string DataServiceVersion = "2.0";
        protected readonly string[] SupportedCommandList = { "$skiptoken", "$orderby", "$top", "$skip", "$filter", "$select", "$format" };
        protected readonly string[] CustomQueryString = { "$skiptoken", "$orderby", "$top", "$skip", "$filter" };
        protected readonly string[] CustomFilterQueryString = { "substringof", "endswith", "startswith", "length", "indexof", "replace", "substring", "tolower", "toupper", "trim", "concat", "day", "hour", "minute", "month", "second", "year", "round", "floor", "ceiling" };

        public string OgdiAlias { get; set; }
        public string EntitySet { get; set; }
        public string Remainder { get; set; }

        protected CloudStorageAccount _Account;
        protected HttpContext _HttpContext;
        protected string _AzureTableUrlToReplace;
        protected string _AzureTableUrlReplacement;

        protected List<DynamicQueryable.DynamicProperty> _ColumnsInformation;

        protected QueryString _QueryString;
        protected TopQuery _TopQuery;
        protected SkipQuery _SkipQuery;
        protected OrderbyQuery _OrderbyQuery;
        protected FilterQuery _FilterQuery;
        protected Pagination _Pagination;

        public bool IsReusable
        {
            get { return false; }
        }

        public virtual void ProcessRequest(HttpContext HttpContext)
        {
            if (!this.IsHttpGet(HttpContext))
            {
                this.RespondForbidden(HttpContext);
                return;
            }

            _HttpContext = HttpContext;
            _HttpContext.Response.Headers["DataServiceVersion"] = DataServiceVersion;
            _HttpContext.Response.CacheControl = "no-cache";

            _QueryString = new QueryString();

            this.CheckDataServiceHeader("DataServiceVersion");
            this.CheckDataServiceHeader("MaxDataServiceVersion");
        }

        private void CheckDataServiceHeader(string headerKey)
        {
            string dataServiceVersion = _HttpContext.Request.Headers[headerKey];
            if (!string.IsNullOrEmpty(dataServiceVersion))
            {
                try
                {
                    string[] parts = dataServiceVersion.Split(new char[] { ';' });
                    if (!Regex.IsMatch(parts[0], @"^[0-9]+\.[0-9]+$"))
                    {
                        this.RespondMethodNotAllowed(_HttpContext);
                    }
                    if (headerKey.Equals("DataServiceVersion") && double.Parse(parts[0], NumberStyles.AllowDecimalPoint, NumberFormatInfo.InvariantInfo) > double.Parse(DataServiceVersion, NumberStyles.AllowDecimalPoint, NumberFormatInfo.InvariantInfo))
                    {
                        this.RespondMethodNotAllowed(_HttpContext);
                    }
                    if (headerKey.Equals("MaxDataServiceVersion") && double.Parse(parts[0], NumberStyles.AllowDecimalPoint, NumberFormatInfo.InvariantInfo) < double.Parse(DataServiceVersion, NumberStyles.AllowDecimalPoint, NumberFormatInfo.InvariantInfo))
                    {
                        this.RespondMethodNotAllowed(_HttpContext);
                    }
                }
                catch (Exception)
                {
                    this.RespondMethodNotAllowed(_HttpContext);
                }
            }
        }

        protected abstract void Render(XElement feed);

        protected XElement GetFeed(string uriPath, NameValueCollection additionalQueryStrings = null, bool handleQueryString = true)
        {
            if (_Account == null)
            {
                throw new ArgumentNullException("Storage account is not properly configured");
            }

            // Initializing
            this.InitializeQueryVariables();

            // Build a correct URI
            UriBuilder uri = this.BuildURI(uriPath, additionalQueryStrings, handleQueryString);

            // Get feed from Azure Table Storage
            XElement feed = this.GetFeedFromAzure(uri, handleQueryString);

            // Handle next requests
            if (!string.IsNullOrEmpty(_Pagination.NextPartitionKey) && !string.IsNullOrEmpty(_Pagination.NextRowKey))
            {
                this.GetNextFeed(uri, feed, handleQueryString);
            }

            // Order feed if $orderby parameter has been specified
            if (!string.IsNullOrEmpty(_OrderbyQuery.Value))
            {
                this.OrderFeed(feed);
                // Apply $top value
                feed.Elements(_entryXName).Skip(_TopQuery.Value + _SkipQuery.Value).Remove();
            }

            // Filter feed if $filter parameter has been specified
            if (_FilterQuery.Values.Count > 0)
            {
                this.FilterFeed(feed);
            }

            // Apply $skip value
            if (_SkipQuery.Value > 0)
            {
                feed.Elements(_entryXName).Take(_SkipQuery.Value).Remove();
            }

            // Insert next page link
            if (!string.IsNullOrEmpty(_Pagination.NextPartitionKey) && !string.IsNullOrEmpty(_Pagination.NextRowKey))
            {
                string paginationLink = this.GetPaginationLink();
                XElement pagination = new XElement(_linkXName, new XAttribute("rel", "next"), new XAttribute("href", paginationLink));
                feed.Add(pagination);

                _HttpContext.Response.Headers["x-ms-continuation-NextPartitionKey"] = _Pagination.NextPartitionKey;
                _HttpContext.Response.Headers["x-ms-continuation-NextRowKey"] = _Pagination.NextRowKey;
            }

            return feed;
        }

        #region Initialize query

        private void InitializeQueryVariables()
        {
            _TopQuery = new TopQuery(MaxAzureTableResult);
            _SkipQuery = new SkipQuery();
            _OrderbyQuery = new OrderbyQuery();
            _FilterQuery = new FilterQuery();
            _Pagination = new Pagination();
        }

        private UriBuilder BuildURI(string uriPath, NameValueCollection additionalQueryStrings, bool handleQueryString)
        {
            UriBuilder uri = new UriBuilder(_Account.TableEndpoint.AbsoluteUri);

            // Add URI path
            if (!string.IsNullOrEmpty(uriPath))
            {
                uri.Path = uriPath;
            }

            // Query string validation
            foreach (string key in _HttpContext.Request.QueryString.AllKeys)
            {
                if (key.StartsWith("$") && !SupportedCommandList.Contains(key))
                {
                    this.RespondBadRequest(_HttpContext);
                }
            }

            // Add URI query string
            if (handleQueryString)
            {
                this.ParseQueryString(additionalQueryStrings);
            }

            return uri;
        }

        private void ParseQueryString(NameValueCollection manualQueryString)
        {
            // Handle normal fields
            foreach (string key in _HttpContext.Request.QueryString.AllKeys)
            {
                if (!CustomQueryString.Contains(key))
                {
                    _QueryString.Add(key, _HttpContext.Request.QueryString[key]);
                }
            }

            // Handle manual fields
            if (manualQueryString != null)
            {
                foreach (string key in manualQueryString.AllKeys)
                {
                    _QueryString.Add(key, manualQueryString[key]);
                }
            }

            this.HandleOrderby();
            this.HandleSkip();
            this.HandleTop();
            this.HandleSkiptoken();
            this.HandleFilter();
        }

        private void HandleOrderby()
        {
            string orderby = _HttpContext.Request.QueryString["$orderby"];
            if (!string.IsNullOrEmpty(orderby))
            {
                _OrderbyQuery.Value = orderby;
            }
        }

        private void HandleSkip()
        {
            string skip = _HttpContext.Request.QueryString["$skip"];
            if (!string.IsNullOrEmpty(skip))
            {
                try
                {
                    int skipValue = int.Parse(skip);
                    if (skipValue > 0)
                    {
                        _SkipQuery.Value = skipValue;
                    }
                }
                catch (Exception) { }
            }
        }

        private void HandleTop()
        {
            string top = _HttpContext.Request.QueryString["$top"];
            if (!string.IsNullOrEmpty(top))
            {
                if (top.Equals("all"))
                {
                    _TopQuery.All = true;
                }
                else
                {
                    try
                    {
                        int topValue = int.Parse(top);
                        if (topValue > 0)
                        {
                            _TopQuery.Value = topValue;
                        }
                    }
                    catch (Exception) { }
                }
            }

            if (string.IsNullOrEmpty(_OrderbyQuery.Value) && _TopQuery.Value + _SkipQuery.Value < MaxAzureTableResult)
            {
                _QueryString.Add("$top", (_TopQuery.Value + _SkipQuery.Value).ToString());
            }
        }

        private void HandleSkiptoken()
        {
            string nextPartitionKey = null;
            string nextRowKey = null;

            string skiptoken = _HttpContext.Request.QueryString["$skiptoken"];
            if (!string.IsNullOrEmpty(skiptoken))
            {
                string[] items = skiptoken.Substring(1, skiptoken.Length - 2).Split(new Char[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string item in items)
                {
                    string[] keyVal = item.Split(new Char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                    if (keyVal.Length == 2 && keyVal[0].Equals("NextPartitionKey"))
                    {
                        nextPartitionKey = keyVal[1];
                    }
                    else if (keyVal.Length == 2 && keyVal[0].Equals("NextRowKey"))
                    {
                        nextRowKey = keyVal[1];
                    }
                }
            }

            if (!string.IsNullOrEmpty(nextPartitionKey) && !string.IsNullOrEmpty(nextRowKey))
            {
                _QueryString.Add("NextPartitionKey", nextPartitionKey);
                _QueryString.Add("NextRowKey", nextRowKey);
            }
        }

        private void HandleFilter()
        {
            if (!string.IsNullOrEmpty(_HttpContext.Request.QueryString["$filter"]))
            {
                foreach (string filter in _HttpContext.Request.QueryString.GetValues("$filter"))
                {
                    if (!string.IsNullOrEmpty(filter))
                    {
                        if (!string.IsNullOrEmpty(EntitySet) && this.IsCustomFilterQueryString(filter))
                        {
                            _FilterQuery.Values.Add(filter.Replace('\'', '"'));
                        }
                        else
                        {
                            _QueryString.Add("$filter", filter);
                        }
                    }
                }
            }
        }

        #endregion

        #region Perform query

        private XElement GetFeedFromAzure(UriBuilder uri, bool handleQueryString)
        {
            if (handleQueryString)
            {
                uri.Query = _QueryString.ToString();
            }

            WebRequest request = HttpWebRequest.Create(uri.ToString());

            request.Headers["x-ms-version"] = "2011-08-18";
            request.Headers["DataServiceVersion"] = "2.0";
            request.Headers["MaxDataServiceVersion"] = "2.0";

            // Sign request for Azure authentication
            _Account.Credentials.SignRequestLite((HttpWebRequest)request);

            try
            {
                WebResponse response = request.GetResponse();
                XElement feed = XElement.Load(response.GetResponseStream());

                _HttpContext.Response.Headers["x-ms-request-id"] = response.Headers["x-ms-request-id"];

                if (!string.IsNullOrEmpty(response.Headers["x-ms-continuation-NextPartitionKey"]) && !string.IsNullOrEmpty(response.Headers["x-ms-continuation-NextRowKey"]))
                {
                    _Pagination.NextPartitionKey = response.Headers["x-ms-continuation-NextPartitionKey"];
                    _Pagination.NextRowKey = response.Headers["x-ms-continuation-NextRowKey"];
                }
                else
                {
                    _Pagination.NextPartitionKey = null;
                    _Pagination.NextRowKey = null;
                }

                return feed;
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                _HttpContext.Response.StatusCode = (int)response.StatusCode;
                _HttpContext.Response.End();
            }

            return null;
        }

        private void GetNextFeed(UriBuilder uri, XElement feed, bool handleQueryString)
        {
            int currentRow = MaxAzureTableResult;

            while (_TopQuery.All || currentRow < _TopQuery.Value + _SkipQuery.Value || !string.IsNullOrEmpty(_OrderbyQuery.Value))
            {
                _QueryString["NextPartitionKey"] = _Pagination.NextPartitionKey;
                _QueryString["NextRowKey"] = _Pagination.NextRowKey;

                if (!_TopQuery.All && _TopQuery.Value + _SkipQuery.Value > MaxAzureTableResult && string.IsNullOrEmpty(_OrderbyQuery.Value))
                {
                    int top = MaxAzureTableResult;
                    if ((_TopQuery.Value + _SkipQuery.Value) - currentRow < MaxAzureTableResult)
                    {
                        top = (_TopQuery.Value + _SkipQuery.Value) - currentRow;
                    }
                    _QueryString["$top"] = top.ToString();
                }

                XElement tmpFeed = this.GetFeedFromAzure(uri, handleQueryString);

                feed.Add(tmpFeed.Descendants(_entryXName).ToList());

                if (string.IsNullOrEmpty(_Pagination.NextPartitionKey) || string.IsNullOrEmpty(_Pagination.NextRowKey))
                {
                    break;
                }

                currentRow += MaxAzureTableResult;
            }
        }

        /*
         * Old sort function
        private void SortFeed(XElement feed)
        {
            try
            {
                List<XElement> sortedEntries;
                if (_OrderbyQuery.Asc)
                {
                    sortedEntries = (from x in feed.Descendants(_entryXName)
                                     orderby (string)x.Element(_contentXName).Element(_propertiesXName).Element(_nsd + _OrderbyQuery.Value) ascending
                                     select x).ToList();
                }
                else
                {
                    sortedEntries = (from x in feed.Descendants(_entryXName)
                                     orderby (string)x.Element(_contentXName).Element(_propertiesXName).Element(_nsd + _OrderbyQuery.Value) descending
                                     select x).ToList();
                }

                // Update entries in feed
                XElement[] newEntries = new XElement[sortedEntries.Count];
                sortedEntries.CopyTo(newEntries);

                List<XElement> entries = newEntries.ToList();

                feed.Descendants(_entryXName).Remove();
                feed.Add(entries);
            }
            catch (Exception) { }
        }
        */

        private void OrderFeed(XElement feed)
        {
            IQueryable<XElement> entries = feed.Descendants(_entryXName).AsQueryable();

            if (!string.IsNullOrEmpty(_OrderbyQuery.Value))
            {
                entries = this.ProcessOrder(_OrderbyQuery.Value, entries);
            }

            // Update entries in feed
            List<XElement> tmpEntries = entries.ToList();

            XElement[] newEntries = new XElement[tmpEntries.Count];
            tmpEntries.CopyTo(newEntries);

            feed.Descendants(_entryXName).Remove();
            feed.Add(newEntries.ToList());
        }

        private IQueryable<XElement> ProcessOrder(string orderOption, IQueryable<XElement> iQueryable)
        {
            IQueryable<XElement> source = iQueryable;
            ParameterExpression[] it = new ParameterExpression[] { Expression.Parameter(source.ElementType, "") };

            DynamicQueryable.OGDIExpressionParser parser = new DynamicQueryable.OGDIExpressionParser(it, orderOption, null, ColumnsInformation.ToArray());

            IEnumerable<DynamicQueryable.DynamicOrdering> orderings = parser.ParseOrdering();
            Expression queryExpr = source.Expression;
            string methodAsc = "OrderBy";
            string methodDesc = "OrderByDescending";
            foreach (DynamicQueryable.DynamicOrdering o in orderings)
            {
                queryExpr = Expression.Call(
                    typeof(Queryable), o.Ascending ? methodAsc : methodDesc,
                    new Type[] { source.ElementType, o.Selector.Type },
                    queryExpr, Expression.Quote(Expression.Lambda(o.Selector, it)));
                methodAsc = "ThenBy";
                methodDesc = "ThenByDescending";
            }

            IQueryable<XElement> result = (IQueryable<XElement>)source.Provider.CreateQuery(queryExpr);

            return result;
        }

        private void FilterFeed(XElement feed)
        {
            IQueryable<XElement> entries = feed.Descendants(_entryXName).AsQueryable();

            if (_FilterQuery.Values != null)
            {
                foreach (string filter in _FilterQuery.Values)
                {
                    entries = this.ProcessFilter(filter, entries);
                }
            }

            // Update entries in feed
            List<XElement> tmpEntries = entries.ToList();

            XElement[] newEntries = new XElement[tmpEntries.Count];
            tmpEntries.CopyTo(newEntries);

            feed.Descendants(_entryXName).Remove();
            feed.Add(newEntries.ToList());
        }

        private IQueryable<XElement> ProcessFilter(string filterOption, IQueryable<XElement> iQueryable)
        {
            IQueryable<XElement> source = iQueryable;
            ParameterExpression[] it = new ParameterExpression[] { Expression.Parameter(source.ElementType, "") };

            DynamicQueryable.OGDIExpressionParser parser = new DynamicQueryable.OGDIExpressionParser(it, filterOption, null, ColumnsInformation.ToArray());

            LambdaExpression lambda = Expression.Lambda(parser.Parse(typeof(bool)), it);

            IQueryable<XElement> result = (IQueryable<XElement>)source.Provider.CreateQuery(
                Expression.Call(
                    typeof(Queryable), "Where",
                    new Type[] { source.ElementType },
                    source.Expression, Expression.Quote(lambda)));

            return result;
        }

        private string GetPaginationLink()
        {
            NameValueCollection queryString = new NameValueCollection();

            foreach (string key in _HttpContext.Request.QueryString.AllKeys)
            {
                if (!string.IsNullOrEmpty(key) && !key.Equals("$skiptoken"))
                {
                    queryString.Add(key, _HttpContext.Request.QueryString[key]);
                }
            }

            queryString.Add("$skiptoken", string.Format("'NextPartitionKey={1}{0}NextRowKey={2}'", _HttpContext.Server.UrlEncode("&"), _Pagination.NextPartitionKey, _Pagination.NextRowKey));

            StringBuilder finalUrl = new StringBuilder(string.Format("{0}://{1}:{2}{3}",
                _HttpContext.Request.Url.Scheme,
                _HttpContext.Request.Url.Host,
                _HttpContext.Request.Url.Port,
                _HttpContext.Request.Path));

            if (queryString.Count > 0)
            {
                finalUrl.Append("?");
                bool first = true;
                foreach (string key in queryString.AllKeys)
                {
                    if (!string.IsNullOrEmpty(queryString[key]))
                    {
                        if (!first)
                        {
                            finalUrl.Append("&");
                        }
                        finalUrl.AppendFormat("{0}={1}", key, queryString[key]);
                        first = false;
                    }
                }
            }

            return finalUrl.ToString();
        }

        #endregion

        #region Utils

        private bool IsCustomFilterQueryString(string filter)
        {
            foreach (string customFilter in CustomFilterQueryString)
            {
                if (filter.Contains(string.Format("{0}(", customFilter)))
                {
                    return true;
                }
            }

            return false;
        }

        private List<TableColumnsMetadataEntity> GetColumnsMetadata()
        {
            CloudTableClient clientTable = new CloudTableClient(_Account.TableEndpoint.ToString(), _Account.Credentials);
            TableServiceContext serviceContextTable = clientTable.GetDataServiceContext();

            string query = string.Format("TableColumnsMetadata?$filter=entityset eq '{0}'", EntitySet);
            List<TableColumnsMetadataEntity> resultsQuery = serviceContextTable.Execute<TableColumnsMetadataEntity>(new Uri(query, UriKind.Relative)).ToList();

            return resultsQuery;
        }

        private List<DynamicQueryable.DynamicProperty> GetColumnsProperties(List<string> columnsNames)
        {
            List<DynamicQueryable.DynamicProperty> columnsList = new List<DynamicQueryable.DynamicProperty>();

            string uri = string.Format("{0}EntityMetadata(PartitionKey='{1}', RowKey='{1}Item')", _Account.TableEndpoint.AbsoluteUri, EntitySet);

            WebRequest request = HttpWebRequest.Create(uri);

            // Sign request for Azure authentication
            _Account.Credentials.SignRequestLite((HttpWebRequest)request);

            try
            {
                WebResponse response = request.GetResponse();
                XElement feed = XElement.Load(response.GetResponseStream());

                foreach (var e in feed.Elements(_contentXName).Elements(_propertiesXName))
                {
                    if (e != null)
                    {
                        foreach (string currentName in columnsNames)
                        {
                            string columnType = e.Element(_nsd + currentName.ToLower()).Value;
                            Type currentType = Type.GetType(columnType);
                            DynamicQueryable.DynamicProperty currentColumn = new DynamicQueryable.DynamicProperty(currentName, currentType);
                            columnsList.Add(currentColumn);
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                _HttpContext.Response.StatusCode = (int)response.StatusCode;
                _HttpContext.Response.End();
            }

            return columnsList;
        }

        protected List<DynamicQueryable.DynamicProperty> ColumnsInformation
        {
            get
            {
                if (_ColumnsInformation == null)
                {
                    List<TableColumnsMetadataEntity> queryResults = this.GetColumnsMetadata();

                    List<string> columnsNames = new List<string>();
                    foreach (TableColumnsMetadataEntity currentColumn in queryResults)
                    {
                        columnsNames.Add(currentColumn.column.ToLower());
                    }

                    _ColumnsInformation = this.GetColumnsProperties(columnsNames);
                }

                return _ColumnsInformation;
            }
        }

        protected string ReplaceAzureUrlInString(string xmlString)
        {
            // The xml payload returned from Table Storage data service has urls
            // that point back to Table Storage.  We need to replace the urls with the
            // proper urls for our public service.
            return xmlString.Replace(_AzureTableUrlToReplace, _AzureTableUrlReplacement);
        }

        #endregion
    }

    #region RDF TableColumnsMetadata

    class TableColumnsMetadataDataServiceContext : TableServiceContext
    {
        internal TableColumnsMetadataDataServiceContext(string baseAddress,
               StorageCredentials credentials)
            : base(baseAddress, credentials)
        { }

        internal const string TableColumnsMetadataTableName = "TableColumnsMetadata";

        public IQueryable<TableColumnsMetadataEntity> TableColumnsMetadataTable
        {
            get
            {
                return this.CreateQuery<TableColumnsMetadataEntity>(TableColumnsMetadataTableName);
            }
        }
    }

    public class TableColumnsMetadataEntity : TableServiceEntity
    {

        public TableColumnsMetadataEntity()
            : base()
        {
            PartitionKey = Guid.NewGuid().ToString();
            RowKey = String.Empty;
        }

        public TableColumnsMetadataEntity(string partitionKey, string rowKey)
            : base(partitionKey, rowKey)
        { }

        public string entityset { get; set; }
        public string column { get; set; }
        public string columnnamespace { get; set; }
        public string columndescription { get; set; }
    }

    #endregion
}