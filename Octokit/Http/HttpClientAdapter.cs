﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Octokit.Internal
{
    /// <summary>
    /// Generic Http client. Useful for those who want to swap out System.Net.HttpClient with something else.
    /// </summary>
    /// <remarks>
    /// Most folks won't ever need to swap this out. But if you're trying to run this on Windows Phone, you might.
    /// </remarks>
    public class HttpClientAdapter : IHttpClient
    {
        readonly IWebProxy webProxy;

        public HttpClientAdapter() { }

        public HttpClientAdapter(IWebProxy webProxy)
        {
            this.webProxy = webProxy;
        }

        public async Task<IResponse<T>> Send<T>(IRequest request)
        {
            Ensure.ArgumentNotNull(request, "request");

            using (var httpOptions = new WebRequestHandler())
            {
                httpOptions.AllowAutoRedirect = request.AllowAutoRedirect;
                httpOptions.CachePolicy = new RequestCachePolicy(RequestCacheLevel.Revalidate);

                // Go read http://connect.microsoft.com/VisualStudio/feedback/details/492544 and then have a good cry
                httpOptions.AutomaticDecompression = DecompressionMethods.None;
                if (httpOptions.SupportsProxy && webProxy != null)
                {
                    httpOptions.UseProxy = true;
                    httpOptions.Proxy = webProxy;
                }

                using (var http = new HttpClient(httpOptions))
                {
                    http.BaseAddress = request.BaseAddress;
                    http.Timeout = request.Timeout;

                    using (var requestMessage = BuildRequestMessage(request))
                    {
                        // Make the request
                        var responseMessage = await http.SendAsync(requestMessage, HttpCompletionOption.ResponseContentRead)
                                                        .ConfigureAwait(false);
                        return await BuildResponse<T>(responseMessage).ConfigureAwait(false);
                    }
                }
            }
        }

        protected async virtual Task<IResponse<T>> BuildResponse<T>(HttpResponseMessage responseMessage)
        {
            Ensure.ArgumentNotNull(responseMessage, "responseMessage");

            string responseBody = null;
            string contentType = null;
            using (var content = responseMessage.Content)
            {
                if (content != null)
                {
                    responseBody = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
                    contentType = GetContentType(content);
                }
            }

            var response = new ApiResponse<T>
            {
                Body = responseBody,
                StatusCode = responseMessage.StatusCode,
                ContentType = contentType
            };

            foreach (var h in responseMessage.Headers)
            {
                response.Headers.Add(h.Key, h.Value.First());
            }

            return response;
        }

        protected virtual HttpRequestMessage BuildRequestMessage(IRequest request)
        {
            Ensure.ArgumentNotNull(request, "request");
            HttpRequestMessage requestMessage = null;
            try
            {
                requestMessage = new HttpRequestMessage(request.Method, request.Endpoint);
                foreach (var header in request.Headers)
                {
                    requestMessage.Headers.Add(header.Key, header.Value);
                }
                var httpContent = request.Body as HttpContent;
                if (httpContent != null)
                {
                    requestMessage.Content = httpContent;
                }

                var body = request.Body as string;
                if (body != null)
                {
                    requestMessage.Content = new StringContent(body, Encoding.UTF8, request.ContentType);
                }

                var bodyStream = request.Body as Stream;
                if (bodyStream != null)
                {
                    requestMessage.Content = new StreamContent(bodyStream);
                    requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
                }
            }
            catch (Exception)
            {
                if (requestMessage != null)
                {
                    requestMessage.Dispose();
                }
                throw;
            }

            return requestMessage;
        }

        static string GetContentType(HttpContent httpContent)
        {
            if (httpContent.Headers != null && httpContent.Headers.ContentType != null)
            {
                return httpContent.Headers.ContentType.MediaType;
            }
            return null;
        }
    }
}
