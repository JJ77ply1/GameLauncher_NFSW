﻿using Flurl;
using Flurl.Http;
using Flurl.Http.Content;
using Nancy;
using Nancy.Bootstrapper;
using Nancy.Extensions;
using Nancy.Responses;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UrlFlurl = Flurl.Url;
using GameLauncher.App.Classes.LauncherCore.RPC;
using GameLauncher.App.Classes.LauncherCore.Client;
using GameLauncher.App.Classes.LauncherCore.Support;
using GameLauncher.App.Classes.LauncherCore.Logger;
using GameLauncher.App.Classes.LauncherCore.Visuals;
using GameLauncher.App.Classes.InsiderKit;

namespace GameLauncher.App.Classes.LauncherCore.Proxy
{
    public class ProxyHandler : IApplicationStartup
    {
        private readonly UTF8Encoding UTF8 = new UTF8Encoding(false);

        public void Initialize(IPipelines pipelines)
        {
            pipelines.BeforeRequest += ProxyRequest;
            pipelines.OnError += OnError;
        }

        private TextResponse OnError(NancyContext context, Exception Error)
        {
            Log.Error("PROXY HANDLER: " + context.Request.Path);
            LogToFileAddons.OpenLog("PROXY HANDLER", null, Error, null, true);

            CommunicationLog.RecordEntry(ServerProxy.Instance.GetServerName(), "LAUNCHER", CommunicationLogEntryType.Error,
                new CommunicationLogLauncherError(Error.Message, context.Request.Path, context.Request.Method));

            context.Request.Dispose();

            return new TextResponse(HttpStatusCode.BadRequest, Error.Message);
        }

        private async Task<Response> ProxyRequest(NancyContext context, CancellationToken cancellationToken)
        {
            string path = Strings.Encode(context.Request.Path);
            string method = Strings.Encode(context.Request.Method.ToUpperInvariant());

            if (!path.StartsWith("/nfsw/Engine.svc"))
            {
                Log.Error("PROXY HANDLER: Invalid Request: " + path);
                return "SBRW Launcher Version: " + Theming.PrivacyRPCBuild + "\nBuild Date: " + InsiderInfo.BuildNumberOnly();
            }
            else
            {
                path = path.Substring("/nfsw/Engine.svc".Length);

                UrlFlurl resolvedUrl = new UrlFlurl(ServerProxy.Instance.GetServerUrl()).AppendPathSegment(path, false);

                foreach (var queryParamName in context.Request.Query)
                {
                    resolvedUrl = resolvedUrl.SetQueryParam(queryParamName, context.Request.Query[queryParamName],
                        NullValueHandling.Ignore);
                }

                IFlurlRequest request = resolvedUrl.AllowAnyHttpStatus();

                foreach (var header in context.Request.Headers)
                {
                    /* Don't send Content-Length for GET requests - HeyItsLeo */
                    if (method == "GET" && header.Key.ToLowerInvariant() == "content-length")
                    {
                        continue;
                    }

                    request = request.WithHeader
                        (header.Key, (header.Key == "Host") ? resolvedUrl.ToUri().Host : ((header.Value != null) ? header.Value.First() : string.Empty));
                }

                string requestBody = (method != "GET") ? context.Request.Body.AsString(UTF8) : string.Empty;

                CommunicationLog.RecordEntry(ServerProxy.Instance.GetServerName(), "SERVER", CommunicationLogEntryType.Request,
                    new CommunicationLogRequest(requestBody, resolvedUrl.ToString(), method));

                IFlurlResponse responseMessage;

                if (path == "/event/arbitration" && !string.IsNullOrWhiteSpace(requestBody))
                {
                    requestBody = Strings.Encode(
                    requestBody.Replace("</TopSpeed>", "</TopSpeed><Konami>" + Convert.ToInt32(AntiCheat.cheats_detected) + "</Konami>"));
                    foreach (var header in context.Request.Headers)
                    {
                        if (header.Key.ToLowerInvariant() == "content-length")
                        {
                            int KonamiCode = Convert.ToInt32(header.Value.First()) +
                                ("<Konami>" + Convert.ToInt32(AntiCheat.cheats_detected) + "</Konami>").Length;
                            request = request.WithHeader(header.Key, KonamiCode);
                        }
                    }
                }

                switch (method)
                {
                    case "GET":
                        responseMessage = await request.GetAsync(cancellationToken);
                        break;
                    case "POST":
                        responseMessage = await request.PostAsync(new CapturedStringContent(requestBody),
                            cancellationToken);
                        break;
                    case "PUT":
                        responseMessage = await request.PutAsync(new CapturedStringContent(requestBody),
                            cancellationToken);
                        break;
                    case "DELETE":
                        responseMessage = await request.DeleteAsync(cancellationToken);
                        break;
                    default:
                        Log.Error("PROXY HANDLER: Cannot handle request method " + method);
                        responseMessage = null;
                        break;
                }

                string responseBody = Strings.Encode(await responseMessage.GetStringAsync());

                if (path == "/User/GetPermanentSession")
                {
                    responseBody = CleanFromUnknownChars(responseBody);
                }

                int statusCode = responseMessage.StatusCode;

                DiscordGamePresence.HandleGameState(path, responseBody, context.Request.Query);

                TextResponse Response = new TextResponse(responseBody,
                    responseMessage.ResponseMessage.Content.Headers.ContentType?.MediaType ?? "application/xml;charset=UTF-8")
                {
                    StatusCode = (HttpStatusCode)statusCode
                };

                CommunicationLog.RecordEntry(ServerProxy.Instance.GetServerName(), "SERVER", CommunicationLogEntryType.Response,
                    new CommunicationLogResponse(responseBody, resolvedUrl.ToString(), method));

                return Response;
            }
        }

        private static string CleanFromUnknownChars(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) { return null;  }

            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if
                 (
                  (int)c >= 48 && (int)c <= 57 ||
                  (int)c == 60 || (int)c == 62 ||
                  (int)c >= 65 && (int)c <= 90 ||
                  (int)c >= 97 && (int)c <= 122 ||
                  (int)c == 47 || (int)c == 45 ||
                  (int)c == 46
                 )
                {
                    sb.Append(c);
                }
            }

            return Strings.Encode(sb.ToString(0, sb.Length));
        }
    }
}
