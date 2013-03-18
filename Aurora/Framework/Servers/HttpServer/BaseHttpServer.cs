/*
 * Copyright (c) Contributors, http://aurora-sim.org/, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Xml;
using Nwc.XmlRpc;
using OpenMetaverse.StructuredData;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

namespace Aurora.Framework.Servers.HttpServer
{
    public class BaseHttpServer : IHttpServer
    {
        public volatile bool HTTPDRunning = false;

        protected HttpListenerManager m_internalServer;
        protected Dictionary<string, XmlRpcMethod> m_rpcHandlers = new Dictionary<string, XmlRpcMethod>();
        protected Dictionary<string, bool> m_rpcHandlersKeepAlive = new Dictionary<string, bool>();
        protected Dictionary<string, LLSDMethod> m_llsdHandlers = new Dictionary<string, LLSDMethod>();
        protected Dictionary<string, IStreamedRequestHandler> m_streamHandlers = new Dictionary<string, IStreamedRequestHandler>();
        protected Dictionary<string, GenericHTTPMethod> m_HTTPHandlers = new Dictionary<string, GenericHTTPMethod>();
        protected Dictionary<string, IStreamedRequestHandler> m_HTTPStreamHandlers = new Dictionary<string, IStreamedRequestHandler>();

        protected Dictionary<string, PollServiceEventArgs> m_pollHandlers = new Dictionary<string, PollServiceEventArgs>();

        protected bool m_isSecure;
        protected uint m_port;
        protected string m_hostName;
        protected int NotSocketErrors;

        protected IPAddress m_listenIPAddress = IPAddress.Any;

        internal PollServiceRequestManager PollServiceManager { get { return m_PollServiceManager; } }

        private PollServiceRequestManager m_PollServiceManager;

        public uint Port
        {
            get { return m_port; }
        }

        public bool Secure
        {
            get { return m_isSecure; }
        }

        public IPAddress ListenIPAddress
        {
            get { return m_listenIPAddress; }
            set { m_listenIPAddress = value; }
        }

        public string HostName
        {
            get { return m_hostName; }
            set { m_hostName = value; }
        }

        public string FullHostName
        {
            get
            {
                string protocol = "http://";
                if (Secure)
                    protocol = "https://";
                return protocol + m_hostName;
            }
        }

        /// <summary>
        /// A well-formed URI for the host region server (namely "http://ExternalHostName:Port)
        /// </summary>
        public string ServerURI
        {
            get
            {
                string protocol = "http://";
                if (Secure)
                    protocol = "https://";
                return protocol + m_hostName + ":" + m_port.ToString();
            }
        }

        public BaseHttpServer(uint port, string hostName, bool isSecure)
        {
            m_hostName = hostName;
            m_port = port;
            m_isSecure = isSecure;
        }

        /// <summary>
        /// Add a stream handler to the http server.  If the handler already exists, then nothing happens.
        /// </summary>
        /// <param name="handler"></param>
        public void AddStreamHandler(IStreamedRequestHandler handler)
        {
            string httpMethod = handler.HttpMethod;
            string path = handler.Path;
            string handlerKey = GetHandlerKey(httpMethod, path);

            lock (m_streamHandlers)
            {
                if (!m_streamHandlers.ContainsKey(handlerKey))
                {
                    // MainConsole.Instance.DebugFormat("[BASE HTTP SERVER]: Adding handler key {0}", handlerKey);
                    m_streamHandlers.Add(handlerKey, handler);
                }
            }
        }

        internal static string GetHandlerKey(string httpMethod, string path)
        {
            return httpMethod + ":" + path;
        }

        #region Add Handlers

        public bool AddXmlRPCHandler(string method, XmlRpcMethod handler)
        {
            return AddXmlRPCHandler(method, handler, true);
        }

        public bool AddXmlRPCHandler(string method, XmlRpcMethod handler, bool keepAlive)
        {
            lock (m_rpcHandlers)
            {
                m_rpcHandlers[method] = handler;
                m_rpcHandlersKeepAlive[method] = keepAlive; // default
            }

            return true;
        }

        public bool AddHTTPHandler(string methodName, GenericHTTPMethod handler)
        {
            //MainConsole.Instance.DebugFormat("[BASE HTTP SERVER]: Registering {0}", methodName);

            lock (m_HTTPHandlers)
            {
                if (!m_HTTPHandlers.ContainsKey(methodName))
                {
                    m_HTTPHandlers.Add(methodName, handler);
                    return true;
                }
            }

            //must already have a handler for that path so return false
            return false;
        }

        public bool AddHTTPHandler(IStreamedRequestHandler handler)
        {
            //MainConsole.Instance.DebugFormat("[BASE HTTP SERVER]: Registering {0}", methodName);

            lock (m_HTTPStreamHandlers)
            {
                if (!m_HTTPStreamHandlers.ContainsKey(handler.Path))
                {
                    m_HTTPStreamHandlers.Add(handler.Path, handler);
                    return true;
                }
            }

            //must already have a handler for that path so return false
            return false;
        }

        public bool AddPollServiceHTTPHandler(string methodName, GenericHTTPMethod handler, PollServiceEventArgs args)
        {
            bool pollHandlerResult = false;
            lock (m_pollHandlers)
            {
                if (!m_pollHandlers.ContainsKey(methodName))
                {
                    m_pollHandlers.Add(methodName, args);
                    pollHandlerResult = true;
                }
            }

            if (pollHandlerResult)
                return AddHTTPHandler(methodName, handler);

            return false;
        }

        public bool AddLLSDHandler(string path, LLSDMethod handler)
        {
            lock (m_llsdHandlers)
            {
                if (!m_llsdHandlers.ContainsKey(path))
                {
                    m_llsdHandlers.Add(path, handler);
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Finding Handlers

        /// <summary>
        /// Checks if we have an Exact path in the LLSD handlers for the path provided
        /// </summary>
        /// <param name="path">URI of the request</param>
        /// <returns>true if we have one, false if not</returns>
        internal bool DoWeHaveALLSDHandler(string path)
        {
            string[] pathbase = path.Split('/');
            string searchquery = "/";

            if (pathbase.Length < 1)
                return false;

            for (int i = 1; i < pathbase.Length; i++)
            {
                searchquery += pathbase[i];
                if (pathbase.Length - 1 != i)
                    searchquery += "/";
            }

            string bestMatch = null;

            foreach (string pattern in m_llsdHandlers.Keys)
                if (searchquery.StartsWith(pattern) && searchquery.Length >= pattern.Length)
                    bestMatch = pattern;

            // extra kicker to remove the default XMLRPC login case..  just in case..
            if (path != "/" && bestMatch == "/" && searchquery != "/")
                return false;

            if (path == "/")
                return false;

            return !String.IsNullOrEmpty(bestMatch);
        }

        internal bool TryGetStreamHandler(string handlerKey, out IStreamedRequestHandler streamHandler)
        {
            string bestMatch = null;

            lock (m_streamHandlers)
            {
                if (m_streamHandlers.TryGetValue(handlerKey, out streamHandler))
                    return true;
                foreach (string pattern in m_streamHandlers.Keys)
                {
                    if (handlerKey.StartsWith(pattern))
                    {
                        if (String.IsNullOrEmpty(bestMatch) || pattern.Length > bestMatch.Length)
                        {
                            bestMatch = pattern;
                        }
                    }
                }

                if (String.IsNullOrEmpty(bestMatch))
                {
                    streamHandler = null;
                    return false;
                }
                streamHandler = m_streamHandlers[bestMatch];
                return true;
            }
        }

        internal bool TryGetPollServiceHTTPHandler(string handlerKey, out PollServiceEventArgs oServiceEventArgs)
        {
            string bestMatch = null;

            lock (m_pollHandlers)
            {
                if (m_pollHandlers.TryGetValue(handlerKey, out oServiceEventArgs))
                    return true;
                foreach (string pattern in m_pollHandlers.Keys)
                {
                    if (handlerKey.StartsWith(pattern))
                    {
                        if (String.IsNullOrEmpty(bestMatch) || pattern.Length > bestMatch.Length)
                        {
                            bestMatch = pattern;
                        }
                    }
                }

                if (String.IsNullOrEmpty(bestMatch))
                {
                    oServiceEventArgs = null;
                    return false;
                }
                oServiceEventArgs = m_pollHandlers[bestMatch];
                return true;
            }
        }

        internal bool TryGetHTTPHandler(string handlerKey, out GenericHTTPMethod HTTPHandler)
        {
            //            MainConsole.Instance.DebugFormat("[BASE HTTP HANDLER]: Looking for HTTP handler for {0}", handlerKey);

            string bestMatch = null;

            lock (m_HTTPHandlers)
            {
                if (m_HTTPHandlers.TryGetValue(handlerKey, out HTTPHandler))
                    return true;
                foreach (string pattern in m_HTTPHandlers.Keys)
                {
                    if (handlerKey.StartsWith(pattern))
                    {
                        if (String.IsNullOrEmpty(bestMatch) || pattern.Length > bestMatch.Length)
                        {
                            bestMatch = pattern;
                        }
                    }
                }

                if (String.IsNullOrEmpty(bestMatch))
                {
                    HTTPHandler = null;
                    return false;
                }
                HTTPHandler = m_HTTPHandlers[bestMatch];
                return true;
            }
        }

        /// <summary>
        /// Checks if we have an Exact path in the HTTP handlers for the path provided
        /// </summary>
        /// <param name="path">URI of the request</param>
        /// <returns>true if we have one, false if not</returns>
        internal bool DoWeHaveAHTTPHandler(string path)
        {
            string[] pathbase = path.Split('/');
            string searchquery = "/";

            if (pathbase.Length < 1)
                return false;

            for (int i = 1; i < pathbase.Length; i++)
            {
                searchquery += pathbase[i];
                if (pathbase.Length - 1 != i)
                    searchquery += "/";
            }

            string bestMatch = null;

            //MainConsole.Instance.DebugFormat("[BASE HTTP HANDLER]: Checking if we have an HTTP handler for {0}", searchquery);

            lock (m_HTTPHandlers)
            {
                foreach (string pattern in m_HTTPHandlers.Keys)
                {
                    if (searchquery.StartsWith(pattern) && searchquery.Length >= pattern.Length)
                    {
                        bestMatch = pattern;
                    }
                }

                // extra kicker to remove the default XMLRPC login case..  just in case..
                if (path == "/")
                    return false;

                if (!String.IsNullOrEmpty(bestMatch))
                    return true;
            }
            lock (m_HTTPStreamHandlers)
            {
                foreach (string pattern in m_HTTPStreamHandlers.Keys)
                {
                    if (searchquery.StartsWith(pattern) && searchquery.Length >= pattern.Length)
                    {
                        bestMatch = pattern;
                    }
                }

                return !String.IsNullOrEmpty(bestMatch);
            }
        }

        internal bool TryGetLLSDHandler(string path, out LLSDMethod llsdHandler)
        {
            llsdHandler = null;
            // Pull out the first part of the path
            // splitting the path by '/' means we'll get the following return..
            // {0}/{1}/{2}
            // where {0} isn't something we really control 100%

            string[] pathbase = path.Split('/');
            string searchquery = "/";

            if (pathbase.Length < 1)
                return false;

            for (int i = 1; i < pathbase.Length; i++)
            {
                searchquery += pathbase[i];
                if (pathbase.Length - 1 != i)
                    searchquery += "/";
            }

            // while the matching algorithm below doesn't require it, we're expecting a query in the form
            //
            //   [] = optional
            //   /resource/UUID/action[/action]
            //
            // now try to get the closest match to the reigstered path
            // at least for OGP, registered path would probably only consist of the /resource/

            string bestMatch = null;

            foreach (string pattern in m_llsdHandlers.Keys)
            {
                if (searchquery.ToLower().StartsWith(pattern.ToLower()))
                {
                    if (String.IsNullOrEmpty(bestMatch) || searchquery.Length > bestMatch.Length)
                    {
                        // You have to specifically register for '/' and to get it, you must specificaly request it
                        //
                        if (pattern == "/" && searchquery == "/" || pattern != "/")
                            bestMatch = pattern;
                    }
                }
            }

            if (String.IsNullOrEmpty(bestMatch))
            {
                llsdHandler = null;
                return false;
            }
            llsdHandler = m_llsdHandlers[bestMatch];
            return true;
        }

        internal bool TryGetXMLHandler(string methodName, out XmlRpcMethod handler)
        {
            lock (m_rpcHandlers)
                return m_rpcHandlers.TryGetValue(methodName, out handler);
        }

        internal bool GetXMLHandlerIsKeepAlive(string methodName)
        {
            return m_rpcHandlersKeepAlive[methodName];
        }

        public XmlRpcMethod GetXmlRPCHandler(string method)
        {
            lock (m_rpcHandlers)
            {
                if (m_rpcHandlers.ContainsKey(method))
                {
                    return m_rpcHandlers[method];
                }
                return null;
            }
        }

        internal bool TryGetStreamHTTPHandler(string handlerKey, out IStreamedRequestHandler handle)
        {
            string bestMatch = null;

            lock (m_HTTPStreamHandlers)
            {
                if (m_HTTPStreamHandlers.TryGetValue(handlerKey, out handle))
                    return true;
                foreach (string pattern in m_HTTPStreamHandlers.Keys)
                {
                    if (handlerKey.StartsWith(pattern))
                    {
                        if (String.IsNullOrEmpty(bestMatch) || pattern.Length > bestMatch.Length)
                        {
                            bestMatch = pattern;
                        }
                    }
                }

                if (String.IsNullOrEmpty(bestMatch))
                {
                    handle = null;
                    return false;
                }
                handle = m_HTTPStreamHandlers[bestMatch];
                return true;
            }
        }

        internal bool TryGetHTTPHandlerPathBased(string path, out GenericHTTPMethod httpHandler)
        {
            httpHandler = null;
            // Pull out the first part of the path
            // splitting the path by '/' means we'll get the following return..
            // {0}/{1}/{2}
            // where {0} isn't something we really control 100%

            string[] pathbase = path.Split('/');
            string searchquery = "/";

            if (pathbase.Length < 1)
                return false;

            for (int i = 1; i < pathbase.Length; i++)
            {
                searchquery += pathbase[i];
                if (pathbase.Length - 1 != i)
                    searchquery += "/";
            }

            // while the matching algorithm below doesn't require it, we're expecting a query in the form
            //
            //   [] = optional
            //   /resource/UUID/action[/action]
            //
            // now try to get the closest match to the reigstered path
            // at least for OGP, registered path would probably only consist of the /resource/

            string bestMatch = null;

            //            MainConsole.Instance.DebugFormat(
            //                "[BASE HTTP HANDLER]: TryGetHTTPHandlerPathBased() looking for HTTP handler to match {0}", searchquery);

            lock (m_HTTPHandlers)
            {
                foreach (string pattern in m_HTTPHandlers.Keys)
                {
                    if (searchquery.ToLower().StartsWith(pattern.ToLower()))
                    {
                        if (String.IsNullOrEmpty(bestMatch) || searchquery.Length > bestMatch.Length)
                        {
                            // You have to specifically register for '/' and to get it, you must specifically request it
                            if (pattern == "/" && searchquery == "/" || pattern != "/")
                                bestMatch = pattern;
                        }
                    }
                }

                if (String.IsNullOrEmpty(bestMatch))
                {
                    httpHandler = null;
                    return false;
                }
                if (bestMatch == "/" && searchquery != "/")
                    return false;

                httpHandler = m_HTTPHandlers[bestMatch];
                return true;
            }
        }

        #endregion

        #region 400 and 500 responses

        private string GetHTTP404(string host)
        {
            string file = Path.Combine(".", "http_404.html");
            if (!File.Exists(file))
                return getDefaultHTTP404(host);

            StreamReader sr = File.OpenText(file);
            string result = sr.ReadToEnd();
            sr.Close();
            return result;
        }

        private string GetHTTP500()
        {
            string file = Path.Combine(".", "http_500.html");
            if (!File.Exists(file))
                return getDefaultHTTP500();

            StreamReader sr = File.OpenText(file);
            string result = sr.ReadToEnd();
            sr.Close();
            return result;
        }

        // Fallback HTTP responses in case the HTTP error response files don't exist
        private static string getDefaultHTTP404(string host)
        {
            return "<HTML><HEAD><TITLE>404 Page not found</TITLE><BODY><BR /><H1>Ooops!</H1><P>The page you requested has been obsconded with by knomes. Find hippos quick!</P><P>If you are trying to log-in, your link parameters should have: &quot;-loginpage http://" + host + "/?method=login -loginuri http://" + host + "/&quot; in your link </P></BODY></HTML>";
        }

        private static string getDefaultHTTP500()
        {
            return "<HTML><HEAD><TITLE>500 Internal Server Error</TITLE><BODY><BR /><H1>Ooops!</H1><P>The server you requested is overun by knomes! Find hippos quick!</P></BODY></HTML>";
        }

        #endregion

        private void OnRequest(HttpListenerContext context)
        {
            try
            {
                PollServiceEventArgs psEvArgs;

                if (TryGetPollServiceHTTPHandler(context.Request.Url.AbsolutePath, out psEvArgs))
                {
                    PollServiceHttpRequest psreq = new PollServiceHttpRequest(psEvArgs, context);

                    if (psEvArgs.Request != null)
                    {
                        OSHttpRequest req = new OSHttpRequest(context);

                        Stream requestStream = req.InputStream;

                        Encoding encoding = Encoding.UTF8;
                        StreamReader reader = new StreamReader(requestStream, encoding);

                        string requestBody = reader.ReadToEnd();

                        Hashtable keysvals = new Hashtable();
                        Hashtable headervals = new Hashtable();

                        string[] querystringkeys = req.QueryString.AllKeys;
                        string[] rHeaders = req.Headers.AllKeys;

                        keysvals.Add("body", requestBody);
                        keysvals.Add("uri", req.RawUrl);
                        keysvals.Add("content-type", req.ContentType);
                        keysvals.Add("http-method", req.HttpMethod);

                        foreach (string queryname in querystringkeys)
                        {
                            keysvals.Add(queryname, req.QueryString[queryname]);
                        }

                        foreach (string headername in rHeaders)
                        {
                            headervals[headername] = req.Headers[headername];
                        }

                        keysvals.Add("headers", headervals);
                        keysvals.Add("querystringkeys", querystringkeys);

                        psEvArgs.Request(psreq.RequestID, keysvals);
                    }

                    m_PollServiceManager.Enqueue(psreq);
                }
                else
                {
                    HandleRequest(context);
                }
            }
            catch (Exception e)
            {
                MainConsole.Instance.Error(String.Format("[BASE HTTP SERVER]: OnRequest() failed: {0} ", e.Message), e);
            }
        }

        /// <summary>
        /// This methods is the start of incoming HTTP request handling.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="response"></param>
        public virtual void HandleRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            OSHttpRequest req = new OSHttpRequest(context);
            OSHttpResponse resp = new OSHttpResponse(context);
            if (request.HttpMethod == String.Empty) // Can't handle empty requests, not wasting a thread
            {
                try
                {
                    SendHTML500(response);
                }
                catch
                {
                }

                return;
            }

            string requestMethod = request.HttpMethod;
            string uriString = request.RawUrl;
            string remoteIP = request.RemoteEndPoint.ToString();
            int requestStartTick = Environment.TickCount;

            // Will be adjusted later on.
            int requestEndTick = requestStartTick;

            IStreamedRequestHandler requestHandler = null;

            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US", true);

                //response.KeepAlive = true;
                response.SendChunked = false;

                string path = request.RawUrl;
                string handlerKey = GetHandlerKey(request.HttpMethod, path);
                byte[] buffer = null;

                if (TryGetStreamHandler(handlerKey, out requestHandler))
                {
                    response.ContentType = requestHandler.ContentType; // Lets do this defaulting before in case handler has varying content type.

                    buffer = requestHandler.Handle(path, request.InputStream, req, resp);
                }
                else
                {
                    switch (request.ContentType)
                    {
                        case null:
                        case "text/html":
                            buffer = HandleHTTPRequest(req, resp);
                            break;

                        case "application/llsd+xml":
                        case "application/xml+llsd":
                        case "application/llsd+json":
                            buffer = HandleLLSDRequests(req, resp);
                            break;

                        case "text/xml":
                        case "application/xml":
                        case "application/json":
                        default:
                            //MainConsole.Instance.Info("[Debug BASE HTTP SERVER]: in default handler");
                            // Point of note..  the DoWeHaveA methods check for an EXACT path
                            //                        if (request.RawUrl.Contains("/CAPS/EQG"))
                            //                        {
                            //                            int i = 1;
                            //                        }
                            //MainConsole.Instance.Info("[Debug BASE HTTP SERVER]: Checking for LLSD Handler");
                            if (DoWeHaveALLSDHandler(request.RawUrl))
                            {
                                buffer = HandleLLSDRequests(req, resp);
                            }
                            else if (GetXmlRPCHandler(request.RawUrl) != null)
                            {
                                // generic login request.
                                buffer = HandleXmlRpcRequests(req, resp);
                            }
                            //                        MainConsole.Instance.DebugFormat("[BASE HTTP SERVER]: Checking for HTTP Handler for request {0}", request.RawUrl);
                            else if (DoWeHaveAHTTPHandler(request.RawUrl))
                            {
                                buffer = HandleHTTPRequest(req, resp);
                            }
                            else
                            {
                                // generic login request.
                                buffer = HandleXmlRpcRequests(req, resp);
                            }

                            break;
                    }
                }

                request.InputStream.Close();

                if (buffer != null)
                {
                    if (!response.SendChunked)
                        response.ContentLength64 = buffer.LongLength;

                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }

                response.OutputStream.Close();
                response.Close();

                // Do not include the time taken to actually send the response to the caller in the measurement
                // time.  This is to avoid logging when it's the client that is slow to process rather than the
                // server
                requestEndTick = Environment.TickCount;

                //response.FreeContext();
            }
            catch (SocketException e)
            {
                // At least on linux, it appears that if the client makes a request without requiring the response,
                // an unconnected socket exception is thrown when we close the response output stream.  There's no
                // obvious way to tell if the client didn't require the response, so instead we'll catch and ignore
                // the exception instead.
                //
                // An alternative may be to turn off all response write exceptions on the HttpListener, but let's go
                // with the minimum first
                MainConsole.Instance.WarnFormat("[BASE HTTP SERVER]: HandleRequest threw {0}.\nNOTE: this may be spurious on Linux ", e.ToString());
            }
            catch (IOException e)
            {
                MainConsole.Instance.ErrorFormat("[BASE HTTP SERVER]: HandleRequest() threw {0} ", e.ToString());
            }
            catch (Exception e)
            {
                MainConsole.Instance.ErrorFormat("[BASE HTTP SERVER]: HandleRequest() threw {0} ", e.ToString());
                response.Close();
            }
            finally
            {
                // Every month or so this will wrap and give bad numbers, not really a problem
                // since its just for reporting
                int tickdiff = requestEndTick - requestStartTick;
                if (tickdiff > 3000 && requestHandler != null)
                {
                    MainConsole.Instance.InfoFormat(
                        "[BASE HTTP SERVER]: Slow handling of {0} {1} from {2} took {3}ms",
                        requestMethod,
                        uriString,
                        remoteIP,
                        tickdiff);
                }
                else if (MainConsole.Instance.IsEnabled(log4net.Core.Level.Trace))
                {
                    MainConsole.Instance.InfoFormat(
                        "[BASE HTTP SERVER]: Handling {0} {1} from {2} took {3}ms",
                        requestMethod,
                        uriString,
                        remoteIP,
                        tickdiff);
                }
            }
        }

        public byte[] HandleHTTPRequest(OSHttpRequest request, OSHttpResponse response)
        {
            //            MainConsole.Instance.DebugFormat(
            //                "[BASE HTTP SERVER]: HandleHTTPRequest for request to {0}, method {1}",
            //                request.RawUrl, request.HttpMethod);

            switch (request.HttpMethod)
            {
                case "OPTIONS":
                    response.StatusCode = (int)OSHttpStatusCode.SuccessOk;
                    return null;

                default:
                    return HandleContentVerbs(request, response);
            }
        }

        /// <summary>
        /// Try all the registered xmlrpc handlers when an xmlrpc request is received.
        /// Sends back an XMLRPC unknown request response if no handler is registered for the requested method.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="response"></param>
        private byte[] HandleXmlRpcRequests(OSHttpRequest request, OSHttpResponse response)
        {
            Stream requestStream = request.InputStream;

            Encoding encoding = Encoding.UTF8;
            StreamReader reader = new StreamReader(requestStream, encoding);

            string requestBody = reader.ReadToEnd();
            reader.Close();
            requestStream.Close();
            //MainConsole.Instance.Debug(requestBody);
            requestBody = requestBody.Replace("<base64></base64>", "");
            string responseString = String.Empty;
            XmlRpcRequest xmlRprcRequest = null;

            try
            {
                xmlRprcRequest = (XmlRpcRequest)(new XmlRpcRequestDeserializer()).Deserialize(requestBody);
            }
            catch (XmlException e)
            {
                MainConsole.Instance.WarnFormat(
                        "[BASE HTTP SERVER]: Got XMLRPC request with invalid XML from {0}.  XML was '{1}'.  Sending blank response.  Exception: {2}",
                        request.RemoteIPEndPoint, requestBody, e.ToString());
            }

            if (xmlRprcRequest != null)
            {
                string methodName = xmlRprcRequest.MethodName;
                if (methodName != null)
                {
                    xmlRprcRequest.Params.Add(request.RemoteIPEndPoint); // Param[1]
                    XmlRpcResponse xmlRpcResponse;

                    XmlRpcMethod method;
                    bool methodWasFound;
                    bool keepAlive = false;
                    lock (m_rpcHandlers)
                    {
                        methodWasFound = m_rpcHandlers.TryGetValue(methodName, out method);
                        if (methodWasFound)
                            keepAlive = m_rpcHandlersKeepAlive[methodName];
                    }

                    if (methodWasFound)
                    {
                        xmlRprcRequest.Params.Add(request.Url); // Param[2]

                        string xff = "X-Forwarded-For";
                        string xfflower = xff.ToLower();
                        foreach (string s in request.Headers.AllKeys)
                        {
                            if (s != null && s.Equals(xfflower))
                            {
                                xff = xfflower;
                                break;
                            }
                        }
                        xmlRprcRequest.Params.Add(request.Headers.Get(xff)); // Param[3]

                        try
                        {
                            xmlRpcResponse = method(xmlRprcRequest, request.RemoteIPEndPoint);
                        }
                        catch (Exception e)
                        {
                            string errorMessage
                                = String.Format(
                                    "Requested method [{0}] from {1} threw exception: {2} {3}",
                                    methodName, request.RemoteIPEndPoint.Address, e.Message, e.StackTrace);

                            MainConsole.Instance.ErrorFormat("[BASE HTTP SERVER]: {0}", errorMessage);

                            // if the registered XmlRpc method threw an exception, we pass a fault-code along
                            xmlRpcResponse = new XmlRpcResponse();

                            // Code probably set in accordance with http://xmlrpc-epi.sourceforge.net/specs/rfc.fault_codes.php
                            xmlRpcResponse.SetFault(-32603, errorMessage);
                        }

                        // if the method wasn't found, we can't determine KeepAlive state anyway, so lets do it only here
                        response.KeepAlive = keepAlive;
                    }
                    else
                    {
                        xmlRpcResponse = new XmlRpcResponse();

                        // Code set in accordance with http://xmlrpc-epi.sourceforge.net/specs/rfc.fault_codes.php
                        xmlRpcResponse.SetFault(
                            XmlRpcErrorCodes.SERVER_ERROR_METHOD,
                            String.Format("Requested method [{0}] not found", methodName));
                    }

                    response.ContentType = "text/xml";
                    responseString = XmlRpcResponseSerializer.Singleton.Serialize(xmlRpcResponse);
                }
                else
                {
                    //HandleLLSDRequests(request, response);
                    response.ContentType = "text/plain";
                    response.StatusCode = 404;
                    response.StatusDescription = "Not Found";
                    response.ProtocolVersion = "HTTP/1.0";
                    responseString = "Not found";
                    response.KeepAlive = false;

                    MainConsole.Instance.ErrorFormat(
                        "[BASE HTTP SERVER]: Handler not found for http request {0} {1}",
                        request.HttpMethod, request.Url.PathAndQuery);
                }
            }

            byte[] buffer = Encoding.UTF8.GetBytes(responseString);

            response.SendChunked = false;
            response.ContentLength64 = buffer.Length;
            response.ContentEncoding = Encoding.UTF8;

            return buffer;
        }

        private byte[] HandleLLSDRequests(OSHttpRequest request, OSHttpResponse response)
        {
            //MainConsole.Instance.Warn("[BASE HTTP SERVER]: We've figured out it's a LLSD Request");
            Stream requestStream = request.InputStream;

            Encoding encoding = Encoding.UTF8;
            StreamReader reader = new StreamReader(requestStream, encoding);

            string requestBody = reader.ReadToEnd();
            reader.Close();
            requestStream.Close();

            //MainConsole.Instance.DebugFormat("[OGP]: {0}:{1}", request.RawUrl, requestBody);
            response.KeepAlive = true;

            OSD llsdRequest = null;
            OSD llsdResponse = null;

            bool LegacyLLSDLoginLibOMV = (requestBody.Contains("passwd") && requestBody.Contains("mac") && requestBody.Contains("viewer_digest"));

            if (requestBody.Length == 0)
            // Get Request
            {
                requestBody = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><llsd><map><key>request</key><string>get</string></map></llsd>";
            }
            try
            {
                llsdRequest = OSDParser.Deserialize(requestBody);
            }
            catch (Exception ex)
            {
                MainConsole.Instance.Warn("[BASE HTTP SERVER]: Error - " + ex.Message);
            }

            if (llsdRequest != null)// && m_defaultLlsdHandler != null)
            {
                LLSDMethod llsdhandler = null;

                if (TryGetLLSDHandler(request.RawUrl, out llsdhandler) && !LegacyLLSDLoginLibOMV)
                {
                    // we found a registered llsd handler to service this request
                    llsdResponse = llsdhandler(request.RawUrl, llsdRequest, request.RemoteIPEndPoint);
                }
                else
                {
                    //Attempt to use the base one
                    if (LegacyLLSDLoginLibOMV && TryGetLLSDHandler("/", out llsdhandler))
                    {
                        llsdResponse = llsdhandler(request.RawUrl, llsdRequest, request.RemoteIPEndPoint);
                    }
                    else
                        // we didn't find a registered llsd handler to service this request
                        llsdResponse = GenerateNoLLSDHandlerResponse();
                }
            }
            else
            {
                llsdResponse = GenerateNoLLSDHandlerResponse();
            }

            byte[] buffer = new byte[0];

            if (llsdResponse.ToString() == "shutdown404!")
            {
                response.ContentType = "text/plain";
                response.StatusCode = 404;
                response.StatusDescription = "Not Found";
                response.ProtocolVersion = "HTTP/1.0";
                buffer = Encoding.UTF8.GetBytes("Not found");
            }
            else
            {
                // Select an appropriate response format
                buffer = BuildLLSDResponse(request, response, llsdResponse);
            }

            response.SendChunked = false;
            response.ContentLength64 = buffer.Length;
            response.ContentEncoding = Encoding.UTF8;
            response.KeepAlive = true;

            return buffer;
        }
        private OSDMap GenerateNoLLSDHandlerResponse()
        {
            OSDMap map = new OSDMap();
            map["reason"] = OSD.FromString("LLSDRequest");
            map["message"] = OSD.FromString("No handler registered for LLSD Requests");
            map["login"] = OSD.FromString("false");
            return map;
        }

        private byte[] BuildLLSDResponse(OSHttpRequest request, OSHttpResponse response, OSD llsdResponse)
        {
            if (request.AcceptTypes != null && request.AcceptTypes.Length > 0)
            {
                foreach (string strAccept in request.AcceptTypes)
                {
                    switch (strAccept)
                    {
                        case "application/llsd+xml":
                        case "application/xml":
                        case "text/xml":
                            response.ContentType = strAccept;
                            return OSDParser.SerializeLLSDXmlBytes(llsdResponse);
                        case "application/llsd+json":
                        case "application/json":
                            response.ContentType = strAccept;
                            return Encoding.UTF8.GetBytes(OSDParser.SerializeJsonString(llsdResponse));
                    }
                }
            }

            if (!String.IsNullOrEmpty(request.ContentType))
            {
                switch (request.ContentType)
                {
                    case "application/llsd+xml":
                    case "application/xml":
                    case "text/xml":
                        response.ContentType = request.ContentType;
                        return OSDParser.SerializeLLSDXmlBytes(llsdResponse);
                    case "application/llsd+json":
                    case "application/json":
                        response.ContentType = request.ContentType;
                        return Encoding.UTF8.GetBytes(OSDParser.SerializeJsonString(llsdResponse));
                }
            }

            // response.ContentType = "application/llsd+json";
            // return Util.UTF8.GetBytes(OSDParser.SerializeJsonString(llsdResponse));
            response.ContentType = "application/llsd+xml";
            return OSDParser.SerializeLLSDXmlBytes(llsdResponse);
        }

        private byte[] HandleContentVerbs(OSHttpRequest request, OSHttpResponse response)
        {
            //            MainConsole.Instance.DebugFormat("[BASE HTTP SERVER]: HandleContentVerbs for request to {0}", request.RawUrl);

            // This is a test.  There's a workable alternative..  as this way sucks.
            // We'd like to put this into a text file parhaps that's easily editable.
            //
            // For this test to work, I used the following secondlife.exe parameters
            // "C:\Program Files\SecondLifeWindLight\SecondLifeWindLight.exe" -settings settings_windlight.xml -channel "Second Life WindLight"  -set SystemLanguage en-us -loginpage http://10.1.1.2:8002/?show_login_form=TRUE -loginuri http://10.1.1.2:8002 -user 10.1.1.2
            //
            // Even after all that, there's still an error, but it's a start.
            //
            // I depend on show_login_form being in the secondlife.exe parameters to figure out
            // to display the form, or process it.
            // a better way would be nifty.

            byte[] buffer;

            Encoding encoding = Encoding.UTF8;
            Hashtable keysvals = new Hashtable();
            Hashtable headervals = new Hashtable();

            Hashtable requestVars = new Hashtable();

            string host = String.Empty;

            string[] querystringkeys = request.QueryString.AllKeys;
            string[] rHeaders = request.Headers.AllKeys;

            keysvals.Add("uri", request.RawUrl);
            keysvals.Add("content-type", request.ContentType);
            keysvals.Add("http-method", request.HttpMethod);

            foreach (string queryname in querystringkeys)
            {
                //                MainConsole.Instance.DebugFormat(
                //                    "[BASE HTTP SERVER]: Got query paremeter {0}={1}", queryname, request.QueryString[queryname]);
                keysvals.Add(queryname, request.QueryString[queryname]);
                requestVars.Add(queryname, keysvals[queryname]);
            }

            foreach (string headername in rHeaders)
            {
                //                MainConsole.Instance.Debug("[BASE HTTP SERVER]: " + headername + "=" + request.Headers[headername]);
                headervals[headername] = request.Headers[headername];
            }

            if (headervals.Contains("Host"))
            {
                host = (string)headervals["Host"];
            }

            keysvals.Add("headers", headervals);
            keysvals.Add("querystringkeys", querystringkeys);
            keysvals.Add("requestvars", requestVars);
            //            keysvals.Add("form", request.Form);

            if (keysvals.Contains("method"))
            {
                //                MainConsole.Instance.Debug("[BASE HTTP SERVER]: Contains Method");
                string method = (string)keysvals["method"];
                //                MainConsole.Instance.Debug("[BASE HTTP SERVER]: " + requestBody);
                GenericHTTPMethod requestprocessor;
                IStreamedRequestHandler streamProcessor;
                if (TryGetHTTPHandler(method, out requestprocessor) ||
                    TryGetHTTPHandler(request.RawUrl, out requestprocessor))
                {
                    Stream requestStream = request.InputStream;

                    StreamReader reader = new StreamReader(requestStream, encoding);

                    string requestBody = reader.ReadToEnd();
                    // avoid warning for now
                    reader.ReadToEnd();
                    reader.Close();
                    requestStream.Close();
                    keysvals.Add("body", requestBody);

                    Hashtable responsedata1 = requestprocessor(keysvals);
                    buffer = DoHTTPGruntWork(responsedata1, response);

                    //SendHTML500(response);
                }
                else if (TryGetStreamHTTPHandler(method, out streamProcessor) ||
                    TryGetStreamHTTPHandler(request.RawUrl, out streamProcessor))
                {
                    buffer = streamProcessor.Handle(request.RawUrl, request.InputStream, request, response);
                }
                else
                {
                    //                    MainConsole.Instance.Warn("[BASE HTTP SERVER]: Handler Not Found");
                    buffer = SendHTML404(response, host);
                }
            }
            else
            {
                GenericHTTPMethod requestprocessor;
                IStreamedRequestHandler streamProcessor;
                bool foundHandler = TryGetHTTPHandlerPathBased(request.RawUrl, out requestprocessor);
                if (foundHandler)
                {
                    Stream requestStream = request.InputStream;

                    StreamReader reader = new StreamReader(requestStream, encoding);

                    string requestBody = reader.ReadToEnd();
                    // avoid warning for now
                    reader.ReadToEnd();
                    reader.Close();
                    requestStream.Close();
                    keysvals.Add("body", requestBody);

                    Hashtable responsedata2 = requestprocessor(keysvals);
                    buffer = DoHTTPGruntWork(responsedata2, response);

                    //SendHTML500(response);
                }
                else if (TryGetStreamHTTPHandler(request.Url.AbsolutePath, out streamProcessor))
                {
                    buffer = streamProcessor.Handle(request.Url.AbsolutePath, request.InputStream, request, response);
                }
                else
                {
                    //                    MainConsole.Instance.Warn("[BASE HTTP SERVER]: Handler Not Found2");
                    buffer = SendHTML404(response, host);
                }
            }

            return buffer;
        }

        public byte[] DoHTTPGruntWork(Hashtable responsedata, OSHttpResponse response)
        {
            //MainConsole.Instance.Info("[BASE HTTP SERVER]: Doing HTTP Grunt work with response");
            int responsecode = (int)responsedata["int_response_code"];
            string responseString = (string)responsedata["str_response_string"];
            string contentType = (string)responsedata["content_type"];

            if (responsedata.ContainsKey("error_status_text"))
            {
                response.StatusDescription = (string)responsedata["error_status_text"];
            }
            if (responsedata.ContainsKey("http_protocol_version"))
            {
                response.ProtocolVersion = (string)responsedata["http_protocol_version"];
            }

            if (responsedata.ContainsKey("keepalive"))
            {
                bool keepalive = (bool)responsedata["keepalive"];
                response.KeepAlive = keepalive;

            }

            //if (responsedata.ContainsKey("reusecontext"))
            //    response.ReuseContext = (bool)responsedata["reusecontext"];

            // Cross-Origin Resource Sharing with simple requests
            if (responsedata.ContainsKey("access_control_allow_origin"))
                response.AddHeader("Access-Control-Allow-Origin", (string)responsedata["access_control_allow_origin"]);

            //Even though only one other part of the entire code uses HTTPHandlers, we shouldn't expect this
            //and should check for NullReferenceExceptions

            if (string.IsNullOrEmpty(contentType))
            {
                contentType = "text/html";
            }

            // The client ignores anything but 200 here for web login, so ensure that this is 200 for that

            response.StatusCode = responsecode;

            if (responsecode == (int)OSHttpStatusCode.RedirectMovedPermanently)
            {
                response.RedirectLocation = (string)responsedata["str_redirect_location"];
                response.StatusCode = responsecode;
            }

            response.AddHeader("Content-Type", contentType);

            byte[] buffer;

            if (!(contentType.Contains("image")
                || contentType.Contains("x-shockwave-flash")
                || contentType.Contains("application/x-oar")
                || contentType.Contains("application/vnd.ll.mesh")))
            {
                // Text
                buffer = Encoding.UTF8.GetBytes(responseString);
            }
            else
            {
                // Binary!
                buffer = Convert.FromBase64String(responseString);
            }

            response.SendChunked = false;
            response.ContentLength64 = buffer.Length;
            response.ContentEncoding = Encoding.UTF8;

            return buffer;
        }

        public byte[] SendHTML404(OSHttpResponse response, string host)
        {
            // I know this statuscode is dumb, but the client doesn't respond to 404s and 500s
            response.StatusCode = 404;
            response.AddHeader("Content-type", "text/html");

            string responseString = GetHTTP404(host);
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);

            response.SendChunked = false;
            response.ContentLength64 = buffer.Length;
            response.ContentEncoding = Encoding.UTF8;

            return buffer;
        }

        public byte[] SendHTML500(HttpListenerResponse response)
        {
            // I know this statuscode is dumb, but the client doesn't respond to 404s and 500s
            response.StatusCode = (int)OSHttpStatusCode.SuccessOk;
            response.AddHeader("Content-type", "text/html");

            string responseString = GetHTTP500();
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);

            response.SendChunked = false;
            response.ContentLength64 = buffer.Length;
            response.ContentEncoding = Encoding.UTF8;

            return buffer;
        }

        public void Start()
        {
            MainConsole.Instance.InfoFormat(
                "[BASE HTTP SERVER]: Starting {0} server on port {1}", Secure ? "HTTPS" : "HTTP", Port);

            try
            {
                //m_httpListener = new HttpListener();

                NotSocketErrors = 0;
                m_internalServer = new HttpListenerManager(10, Secure);
                m_internalServer.ProcessRequest += OnRequest;

                m_internalServer.Start(m_port);

                // Long Poll Service Manager with 3 worker threads a 25 second timeout for no events
                m_PollServiceManager = new PollServiceRequestManager(this, 3, 25000);
                m_PollServiceManager.Start();
                HTTPDRunning = true;
            }
            catch (Exception e)
            {
                if (e is HttpListenerException && ((HttpListenerException)e).Message == "Access is denied")
                    MainConsole.Instance.Error("[BASE HTTP SERVER]: You must run this program as an administrator.");
                else
                {
                    MainConsole.Instance.Error("[BASE HTTP SERVER]: Error - " + e.Message);
                    MainConsole.Instance.Error("[BASE HTTP SERVER]: Tip: Do you have permission to listen on port " + m_port + "?");
                }

                // We want this exception to halt the entire server since in current configurations we aren't too
                // useful without inbound HTTP.
                throw e;
            }
        }

        public void Stop()
        {
            HTTPDRunning = false;
            try
            {
                m_PollServiceManager.Stop();
                m_internalServer.Stop();
            }
            catch (NullReferenceException)
            {
                MainConsole.Instance.Warn("[BASE HTTP SERVER]: Null Reference when stopping HttpServer.");
            }
        }

        #region Remove Handlers

        public void RemoveStreamHandler(string httpMethod, string path)
        {
            string handlerKey = GetHandlerKey(httpMethod, path);

            //MainConsole.Instance.DebugFormat("[BASE HTTP SERVER]: Removing handler key {0}", handlerKey);

            lock (m_streamHandlers) m_streamHandlers.Remove(handlerKey);
        }

        public void RemoveHTTPHandler(string httpMethod, string path)
        {
            lock (m_HTTPHandlers)
            {
                if (httpMethod == "")
                {
                    m_HTTPHandlers.Remove(path);
                    return;
                }

                m_HTTPHandlers.Remove(GetHandlerKey(httpMethod, path));
            }
        }

        public void RemovePollServiceHTTPHandler(string httpMethod, string path)
        {
            lock (m_pollHandlers)
            {
                if (m_pollHandlers.ContainsKey(path))
                {
                    m_pollHandlers.Remove(path);
                }
            }

            RemoveHTTPHandler(httpMethod, path);
        }

        public void RemoveXmlRPCHandler(string method)
        {
            lock (m_rpcHandlers)
            {
                if (m_rpcHandlers.ContainsKey(method))
                {
                    m_rpcHandlers.Remove(method);
                }
            }
        }

        public bool RemoveLLSDHandler(string path, LLSDMethod handler)
        {
            try
            {
                if (handler == m_llsdHandlers[path])
                {
                    m_llsdHandlers.Remove(path);
                    return true;
                }
            }
            catch (KeyNotFoundException)
            {
                // This is an exception to prevent crashing because of invalid code
            }

            return false;
        }

        #endregion
    }
}
