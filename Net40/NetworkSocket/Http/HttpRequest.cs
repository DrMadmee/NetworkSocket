﻿using NetworkSocket.Exceptions;
using NetworkSocket.Util;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NetworkSocket.Http
{
    /// <summary>
    /// 表示Http请求信息
    /// </summary>
    public class HttpRequest
    {
        /// <summary>
        /// 获取请求的头信息
        /// </summary>
        public HttpHeader Headers { get; private set; }

        /// <summary>
        /// 获取Query
        /// </summary>
        public HttpNameValueCollection Query { get; private set; }

        /// <summary>
        /// 获取Form 
        /// </summary>
        public HttpNameValueCollection Form { get; private set; }

        /// <summary>
        /// 获取请求的文件
        /// </summary>
        public HttpFile[] Files { get; private set; }

        /// <summary>
        /// 获取Post的内容
        /// </summary>
        public byte[] Body { get; private set; }

        /// <summary>
        /// 获取请求方法
        /// </summary>
        public HttpMethod HttpMethod { get; private set; }

        /// <summary>
        /// 获取请求路径
        /// </summary>
        public string Path { get; private set; }

        /// <summary>
        /// 获取请求的Uri
        /// </summary>
        public Uri Url { get; private set; }

        /// <summary>
        /// 获取监听的本地IP和端口
        /// </summary>
        public EndPoint LocalEndPoint { get; private set; }

        /// <summary>
        /// 获取远程端的IP和端口
        /// </summary>
        public EndPoint RemoteEndPoint { get; private set; }

        /// <summary>
        /// Http请求信息
        /// </summary>
        private HttpRequest()
        {
        }

        /// <summary>
        /// 从Query和Form获取请求参数的值
        /// 多个值会以逗号分隔
        /// </summary>
        /// <param name="key">键</param>
        /// <returns></returns>
        public string this[string key]
        {
            get
            {
                if (this.Query.ContainsKey(key))
                {
                    return this.Query[key];
                }
                else
                {
                    return this.Form[key];
                }
            }
        }

        /// <summary>
        /// 从Query和Form获取请求参数的值
        /// </summary>
        /// <param name="key">键</param>
        /// <returns></returns>
        public IList<string> GetValues(string key)
        {
            var queryValues = this.Query.GetValues(key);
            var formValues = this.Form.GetValues(key);

            var list = new List<string>();
            if (queryValues != null)
            {
                list.AddRange(queryValues);
            }
            if (formValues != null)
            {
                list.AddRange(formValues);
            }
            return list;
        }

        /// <summary>
        /// 获取是否为Websocket请求
        /// </summary>
        /// <returns></returns>
        public bool IsWebsocketRequest()
        {
            if (this.HttpMethod != Http.HttpMethod.GET)
            {
                return false;
            }
            if (StringEquals(this.Headers.TryGet<string>("Connection"), "Upgrade") == false)
            {
                return false;
            }
            if (this.Headers.TryGet<string>("Upgrade") == null)
            {
                return false;
            }
            if (StringEquals(this.Headers.TryGet<string>("Sec-WebSocket-Version"), "13") == false)
            {
                return false;
            }
            if (this.Headers.TryGet<string>("Sec-WebSocket-Key") == null)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 是否为ajax请求
        /// </summary>
        /// <returns></returns>
        public bool IsAjaxRequest()
        {
            return this["X-Requested-With"] == "XMLHttpRequest" || this.Headers["X-Requested-With"] == "XMLHttpRequest";
        }

        /// <summary>
        /// 是否为event-stream请求
        /// </summary>
        /// <returns></returns>
        public bool IsEventStreamRequest()
        {
            return StringEquals(this.Headers["Accept"], "text/event-stream");
        }

        /// <summary>
        /// Content-Type是否为
        /// application/x-www-form-urlencoded
        /// </summary>
        /// <returns></returns>
        public bool IsApplicationFormRequest()
        {
            var contentType = this.Headers["Content-Type"];
            return StringEquals(contentType, "application/x-www-form-urlencoded");
        }

        /// <summary>
        /// Content-Type是否为
        /// multipart/form-data
        /// </summary>
        /// <returns></returns>
        public bool IsMultipartFormRequest(out string boundary)
        {
            var contentType = this.Headers["Content-Type"];
            if (string.IsNullOrEmpty(contentType) == false)
            {
                if (Regex.IsMatch(contentType, "multipart/form-data", RegexOptions.IgnoreCase))
                {
                    var match = Regex.Match(contentType, "(?<=boundary=).+");
                    boundary = match.Value;
                    return match.Success;
                }
            }

            boundary = null;
            return false;
        }

        /// <summary>
        /// 获取是否相等
        /// 不区分大小写
        /// </summary>
        /// <param name="value1"></param>
        /// <param name="value2"></param>
        /// <returns></returns>
        private static bool StringEquals(string value1, string value2)
        {
            return string.Equals(value1, value2, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 解析连接请求信息
        /// 如果数据未完整则返回null
        /// </summary>
        /// <param name="context">上下文</param>   
        /// <param name="request">http请求</param>
        /// <exception cref="HttpException"></exception>
        /// <returns></returns>
        public static bool Parse(IContenxt context, out HttpRequest request)
        {
            var headerLength = 0;
            request = null;

            if (Protocol.IsHttp(context.Stream, out headerLength) == false)
            {
                return false;
            }

            if (headerLength <= 0)
            {
                return true;
            }

            context.Stream.Position = 0;
            var headerString = context.Stream.ReadString(headerLength, Encoding.ASCII);
            const string pattern = @"^(?<method>[^\s]+)\s(?<path>[^\s]+)\sHTTP\/1\.1\r\n" +
                @"((?<field_name>[^:\r\n]+):\s(?<field_value>[^\r\n]*)\r\n)+" +
                @"\r\n";

            var match = Regex.Match(headerString, pattern, RegexOptions.IgnoreCase);
            if (match.Success == false)
            {
                return false;
            }

            var httpMethod = GetHttpMethod(match.Groups["method"].Value);
            var httpHeader = HttpHeader.Parse(match.Groups["field_name"].Captures, match.Groups["field_value"].Captures);
            var contentLength = httpHeader.TryGet<int>("Content-Length");

            if (httpMethod == HttpMethod.POST && context.Stream.Length - headerLength < contentLength)
            {
                return true; // 数据未完整
            }

            request = new HttpRequest
            {
                LocalEndPoint = context.Session.LocalEndPoint,
                RemoteEndPoint = context.Session.RemoteEndPoint,
                HttpMethod = httpMethod,
                Headers = httpHeader
            };

            var scheme = context.Session.IsSecurity ? "https" : "http";
            var url = string.Format("{0}://{1}{2}", scheme, context.Session.LocalEndPoint, match.Groups["path"].Value);
            request.Url = new Uri(url);
            request.Path = request.Url.AbsolutePath;
            request.Query = HttpNameValueCollection.Parse(request.Url.Query.TrimStart('?'));

            switch (httpMethod)
            {
                case HttpMethod.GET:
                    request.Body = new byte[0];
                    request.Form = new HttpNameValueCollection();
                    request.Files = new HttpFile[0];
                    break;

                default:
                    request.Body = context.Stream.ReadArray(contentLength);
                    context.Stream.Position = headerLength;
                    HttpRequest.GeneratePostFormAndFiles(request, context.Stream);
                    break;
            }
            context.Stream.Clear(headerLength + contentLength);
            return true;
        }


        /// <summary>
        /// 获取http方法
        /// </summary>
        /// <param name="method">方法字符串</param>
        /// <exception cref="HttpException"></exception>
        /// <returns></returns>
        private static HttpMethod GetHttpMethod(string method)
        {
            var httpMethod = HttpMethod.GET;
            if (Enum.TryParse<HttpMethod>(method, true, out httpMethod))
            {
                return httpMethod;
            }
            throw new HttpException(501, "不支持的http方法：" + method);
        }

        /// <summary>
        /// 生成Post得到的表单和文件
        /// </summary>
        /// <param name="request"></param>
        /// <param name="stream"></param>      
        private static void GeneratePostFormAndFiles(HttpRequest request, INsStream stream)
        {
            var boundary = default(string);
            if (request.IsApplicationFormRequest() == true)
            {
                HttpRequest.GenerateApplicationForm(request);
            }
            else if (request.IsMultipartFormRequest(out boundary) == true)
            {
                if (request.Body.Length >= boundary.Length)
                {
                    HttpRequest.GenerateMultipartFormAndFiles(request, stream, boundary);
                }
            }


            if (request.Form == null)
            {
                request.Form = new HttpNameValueCollection();
            }

            if (request.Files == null)
            {
                request.Files = new HttpFile[0];
            }
        }

        /// <summary>
        /// 生成一般表单的Form
        /// </summary>
        /// <param name="request"></param>
        private static void GenerateApplicationForm(HttpRequest request)
        {
            var body = Encoding.UTF8.GetString(request.Body);
            request.Form = HttpNameValueCollection.Parse(body);
            request.Files = new HttpFile[0];
        }

        /// <summary>
        /// 生成表单和文件
        /// </summary>
        /// <param name="request"></param>
        /// <param name="stream"></param>   
        /// <param name="boundary">边界</param>
        private static void GenerateMultipartFormAndFiles(HttpRequest request, INsStream stream, string boundary)
        {
            var boundaryBytes = Encoding.ASCII.GetBytes("\r\n--" + boundary);
            var maxPosition = stream.Length - Encoding.ASCII.GetBytes("--\r\n").Length;

            var files = new List<HttpFile>();
            var form = new HttpNameValueCollection();

            stream.Position = stream.Position + boundaryBytes.Length;
            while (stream.Position < maxPosition)
            {
                var headLength = stream.IndexOf(Protocol.DoubleCrlf) + Protocol.DoubleCrlf.Length;
                if (headLength < Protocol.DoubleCrlf.Length)
                {
                    break;
                }

                var head = stream.ReadString(headLength, Encoding.UTF8);
                var bodyLength = stream.IndexOf(boundaryBytes);
                if (bodyLength < 0)
                {
                    break;
                }

                var mHead = new MultipartHead(head);
                if (mHead.IsFile == true)
                {
                    var bytes = stream.ReadArray(bodyLength);
                    var file = new HttpFile(mHead, bytes);
                    files.Add(file);
                }
                else
                {
                    var byes = stream.ReadArray(bodyLength);
                    var value = HttpUtility.UrlDecode(byes, Encoding.UTF8);
                    form.Add(mHead.Name, value);
                }
                stream.Position = stream.Position + boundaryBytes.Length;
            }

            request.Form = form;
            request.Files = files.ToArray();
        }
    }
}

