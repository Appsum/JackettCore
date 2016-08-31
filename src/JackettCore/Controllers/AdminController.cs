﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using JackettCore.Indexers;
using JackettCore.Models;
using JackettCore.Services;
using JackettCore.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace JackettCore.Controllers
{
    [Route("[controller]")]
    //[JackettAuthorized]
    [ResponseCache(CacheProfileName = "Never")]
    public class AdminController : Controller
    {
        private readonly IConfigurationService _config;
        private readonly IIndexerManagerService _indexerService;
        private readonly IServerService _serverService;
        private readonly ISecuityService _securityService;
        private readonly IProcessService _processService;
        private readonly ICacheService _cacheService;
        private readonly ILogger _logger;
        private readonly ILogCacheService _logCache;
        private readonly IUpdateService _updater;

        public AdminController(IConfigurationService config, IIndexerManagerService indexerManagerService, IServerService serverService, ISecuityService s, IProcessService p, ICacheService c, ILogger l, ILogCacheService lc, IUpdateService u)
        {
            _config = config;
            _indexerService = i;
            _serverService = ss;
            _securityService = s;
            _processService = p;
            _cacheService = c;
            _logger = l;
            _logCache = lc;
            _updater = u;
        }

        private async Task<JToken> ReadPostDataJson()
        {
            var content = await Request.Content.ReadAsStringAsync();
            return JObject.Parse(content);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Logout()
        {
            var ctx = Request.GetOwinContext();
            var authManager = ctx.Authentication;
            authManager.SignOut("ApplicationCookie");
            return RedirectToAction("Dashboard","Admin");
        }

        [HttpGet]
        [HttpPost]
        [AllowAnonymous]
        public async Task<HttpResponseMessage> Dashboard()
        {
            if (Request.RequestUri.Query != null && Request.RequestUri.Query.Contains("logout"))
            {
                var file = GetFile("login.html");
                _securityService.Logout(file);
                return file;
            }


            if (_securityService.CheckAuthorised(Request))
            {
                return GetFile("index.html");

            }
            else
            {
                var formData = await Request.Content.ReadAsFormDataAsync();

                if (formData != null && _securityService.HashPassword(formData["password"]) == _serverService.Config.AdminPassword)
                {
                    var file = GetFile("index.html");
                    _securityService.Login(file);
                    return file;
                }
                else
                {
                    return GetFile("login.html");
                }
            }
        }

        [Route("set_admin_password")]
        [HttpPost]
        public async Task<IHttpActionResult> SetAdminPassword()
        {
            var jsonReply = new JObject();
            try
            {
                var postData = await ReadPostDataJson();
                var password = (string)postData["password"];
                if (string.IsNullOrEmpty(password))
                {
                    _serverService.Config.AdminPassword = string.Empty;
                }
                else
                {
                    _serverService.Config.AdminPassword = _securityService.HashPassword(password);
                }

                _serverService.SaveConfig();
                jsonReply["result"] = "success";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in SetAdminPassword");
                jsonReply["result"] = "error";
                jsonReply["error"] = ex.Message;
            }
            return Json(jsonReply);
        }

        [Route("get_config_form")]
        [HttpPost]
        public async Task<IHttpActionResult> GetConfigForm()
        {
            var jsonReply = new JObject();
            try
            {
                var postData = await ReadPostDataJson();
                var indexer = _indexerService.GetIndexer((string)postData["indexer"]);
                var config = await indexer.GetConfigurationForSetup();
                jsonReply["config"] = config.ToJson(null);
                jsonReply["caps"] = indexer.TorznabCaps.CapsToJson();
                jsonReply["name"] = indexer.DisplayName;
                jsonReply["result"] = "success";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in GetConfigForm");
                jsonReply["result"] = "error";
                jsonReply["error"] = ex.Message;
            }
            return Json(jsonReply);
        }

        [Route("configure_indexer")]
        [HttpPost]
        public async Task<IHttpActionResult> Configure()
        {
            var jsonReply = new JObject();
            IIndexer indexer = null;
            try
            {
                var postData = await ReadPostDataJson();
                string indexerString = (string)postData["indexer"];
                indexer = _indexerService.GetIndexer((string)postData["indexer"]);
                jsonReply["name"] = indexer.DisplayName;
                var configurationResult = await indexer.ApplyConfiguration(postData["config"]);
                if (configurationResult == IndexerConfigurationStatus.RequiresTesting)
                {
                    await _indexerService.TestIndexer((string)postData["indexer"]);
                }
                else if (configurationResult == IndexerConfigurationStatus.Failed)
                {
                    throw new Exception("Configuration Failed");
                }
                jsonReply["result"] = "success";
            }
            catch (Exception ex)
            {
                jsonReply["result"] = "error";
                jsonReply["error"] = ex.Message;
                var baseIndexer = indexer as BaseIndexer;
                if (null != baseIndexer)
                    baseIndexer.ResetBaseConfig();
                if (ex is ExceptionWithConfigData)
                {
                    jsonReply["config"] = ((ExceptionWithConfigData)ex).ConfigData.ToJson(null,false);
                }
                else
                {
                    _logger.Error(ex, "Exception in Configure");
                }
            }
            return Json(jsonReply);
        }

        [Route("get_indexers")]
        [HttpGet]
        public IHttpActionResult Indexers()
        {
            var jsonReply = new JObject();
            try
            {
                jsonReply["result"] = "success";
                JArray items = new JArray();

                foreach (var indexer in _indexerService.GetAllIndexers())
                {
                    var item = new JObject();
                    item["id"] = indexer.ID;
                    item["name"] = indexer.DisplayName;
                    item["description"] = indexer.DisplayDescription;
                    item["configured"] = indexer.IsConfigured;
                    item["site_link"] = indexer.SiteLink;
                    item["potatoenabled"] = indexer.TorznabCaps.Categories.Select(c => c.ID).Any(i => PotatoController.MOVIE_CATS.Contains(i));

                    var caps = new JObject();
                    foreach (var cap in indexer.TorznabCaps.Categories)
                        caps[cap.ID.ToString()] = cap.Name;
                    item["caps"] = caps;
                    items.Add(item);
                }
                jsonReply["items"] = items;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in get_indexers");
                jsonReply["result"] = "error";
                jsonReply["error"] = ex.Message;
            }
            return Json(jsonReply);
        }

        [Route("test_indexer")]
        [HttpPost]
        public async Task<IHttpActionResult> Test()
        {
            JToken jsonReply = new JObject();
            try
            {
                var postData = await ReadPostDataJson();
                string indexerString = (string)postData["indexer"];
                await _indexerService.TestIndexer(indexerString);
                jsonReply["name"] = _indexerService.GetIndexer(indexerString).DisplayName;
                jsonReply["result"] = "success";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in test_indexer");
                jsonReply["result"] = "error";
                jsonReply["error"] = ex.Message;
            }
            return Json(jsonReply);
        }

        [Route("delete_indexer")]
        [HttpPost]
        public async Task<IHttpActionResult> Delete()
        {
            var jsonReply = new JObject();
            try
            {
                var postData = await ReadPostDataJson();
                string indexerString = (string)postData["indexer"];
                _indexerService.DeleteIndexer(indexerString);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in delete_indexer");
                jsonReply["result"] = "error";
                jsonReply["error"] = ex.Message;
            }
            return Json(jsonReply);
        }

        [Route("trigger_update")]
        [HttpGet]
        public IHttpActionResult TriggerUpdates()
        {
            var jsonReply = new JObject();
            _updater.CheckForUpdatesNow();
            return Json(jsonReply);
        }

        [Route("get_jackett_config")]
        [HttpGet]
        public IHttpActionResult GetConfig()
        {
            var jsonReply = new JObject();
            try
            {
                var cfg = new JObject();
                cfg["port"] = _serverService.Config.Port;
                cfg["external"] = _serverService.Config.AllowExternal;
                cfg["api_key"] = _serverService.Config.APIKey;
                cfg["blackholedir"] = _serverService.Config.BlackholeDir;
                cfg["updatedisabled"] = _serverService.Config.UpdateDisabled;
                cfg["prerelease"] = _serverService.Config.UpdatePrerelease;
                cfg["password"] = string.IsNullOrEmpty(_serverService.Config.AdminPassword) ? string.Empty : _serverService.Config.AdminPassword.Substring(0, 10);
                cfg["logging"] = Startup.TracingEnabled;
                cfg["basepathoverride"] = _serverService.Config.BasePathOverride;


                jsonReply["config"] = cfg;
                jsonReply["app_version"] = _config.GetVersion();
                jsonReply["result"] = "success";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in get_jackett_config");
                jsonReply["result"] = "error";
                jsonReply["error"] = ex.Message;
            }
            return Json(jsonReply);
        }

        [Route("set_config")]
        [HttpPost]
        public async Task<IHttpActionResult> SetConfig()
        {
            var originalPort = Engine.Server.Config.Port;
            var originalAllowExternal = Engine.Server.Config.AllowExternal;
            var jsonReply = new JObject();
            try
            {
                var postData = await ReadPostDataJson();
                int port = (int)postData["port"];
                bool external = (bool)postData["external"];
                string saveDir = (string)postData["blackholedir"];
                bool updateDisabled = (bool)postData["updatedisabled"];
                bool preRelease = (bool)postData["prerelease"];
                bool logging = (bool)postData["logging"];
                string basePathOverride = (string)postData["basepathoverride"];

                Engine.Server.Config.UpdateDisabled = updateDisabled;
                Engine.Server.Config.UpdatePrerelease = preRelease;
                Engine.Server.Config.BasePathOverride = basePathOverride;
                Startup.BasePath = Engine.Server.BasePath();
                Engine.Server.SaveConfig();

                Engine.SetLogLevel(logging ? LogLevel.Debug : LogLevel.Info);
                Startup.TracingEnabled = logging;

                if (port != Engine.Server.Config.Port || external != Engine.Server.Config.AllowExternal)
                {

                    if (ServerUtil.RestrictedPorts.Contains(port))
                    {
                        jsonReply["result"] = "error";
                        jsonReply["error"] = "The port you have selected is restricted, try a different one.";
                        return Json(jsonReply);
                    }

                    // Save port to the config so it can be picked up by the if needed when running as admin below.
                    Engine.Server.Config.AllowExternal = external;
                    Engine.Server.Config.Port = port;
                    Engine.Server.SaveConfig();

                    // On Windows change the url reservations
                    if (System.Environment.OSVersion.Platform != PlatformID.Unix)
                    {
                        if (!ServerUtil.IsUserAdministrator())
                        {
                            try
                            {
                                _processService.StartProcessAndLog(Application.ExecutablePath, "--ReserveUrls", true);
                            }
                            catch
                            {
                                Engine.Server.Config.Port = originalPort;
                                Engine.Server.Config.AllowExternal = originalAllowExternal;
                                Engine.Server.SaveConfig();
                                jsonReply["result"] = "error";
                                jsonReply["error"] = "Failed to acquire admin permissions to reserve the new port.";
                                return Json(jsonReply);
                            }
                        }
                        else
                        {
                            _serverService.ReserveUrls(true);
                        }
                    }

                (new Thread(() =>
                {
                    Thread.Sleep(500);
                    _serverService.Stop();
                    Engine.BuildContainer();
                    Engine.Server.Initalize();
                    Engine.Server.Start();
                })).Start();
                }

                if (saveDir != Engine.Server.Config.BlackholeDir)
                {
                    if (!string.IsNullOrEmpty(saveDir))
                    {
                        if (!Directory.Exists(saveDir))
                        {
                            throw new Exception("Blackhole directory does not exist");
                        }
                    }

                    Engine.Server.Config.BlackholeDir = saveDir;
                    Engine.Server.SaveConfig();
                }

                jsonReply["result"] = "success";
                jsonReply["port"] = port;
                jsonReply["external"] = external;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in set_port");
                jsonReply["result"] = "error";
                jsonReply["error"] = ex.Message;
            }
            return Json(jsonReply);
        }

        [Route("GetCache")]
        [HttpGet]
        public List<TrackerCacheResult> GetCache()
        {
            var results = _cacheService.GetCachedResults();
            ConfigureCacheResults(results);
            return results;
        }


        private void ConfigureCacheResults(List<TrackerCacheResult> results)
        {
            var serverUrl = string.Format("{0}://{1}:{2}{3}", Request.RequestUri.Scheme, Request.RequestUri.Host, Request.RequestUri.Port, _serverService.BasePath());
            foreach (var result in results)
            {
                var link = result.Link;
                result.Link = _serverService.ConvertToProxyLink(link, serverUrl, result.TrackerId, "dl", result.Title + ".torrent");
                if (result.Link != null && result.Link.Scheme != "magnet" && !string.IsNullOrWhiteSpace(Engine.Server.Config.BlackholeDir))
                    result.BlackholeLink = _serverService.ConvertToProxyLink(link, serverUrl, result.TrackerId, "bh", string.Empty);

            }
        }

        [Route("GetLogs")]
        [HttpGet]
        public List<CachedLog> GetLogs()
        {
            return _logCache.Logs;
        }

        [Route("Search")]
        [HttpPost]
        public ManualSearchResult Search([FromBody]AdminSearch value)
        {
            var results = new List<TrackerCacheResult>();
            var query = new TorznabQuery()
            {
                SearchTerm = value.Query,
                Categories = value.Category == 0 ? new int[0] : new int[1] { value.Category }
            };

            query.ExpandCatsToSubCats();

            var trackers = _indexerService.GetAllIndexers().Where(t => t.IsConfigured).ToList();
            if (!string.IsNullOrWhiteSpace(value.Tracker))
            {
                trackers = trackers.Where(t => t.ID == value.Tracker).ToList();
            }

            if (value.Category != 0)
            {
                trackers = trackers.Where(t => t.TorznabCaps.Categories.Select(c => c.ID).Contains(value.Category)).ToList();
            }

            Parallel.ForEach(trackers.ToList(), indexer =>
            {
                try
                {
                    var searchResults = indexer.PerformQuery(query).Result;
                    searchResults = indexer.CleanLinks(searchResults);
                    _cacheService.CacheRssResults(indexer, searchResults);
                    searchResults = indexer.FilterResults(query, searchResults);

                    lock (results)
                    {
                        foreach (var result in searchResults)
                        {
                            var item = Mapper.Map<TrackerCacheResult>(result);
                            item.Tracker = indexer.DisplayName;
                            item.TrackerId = indexer.ID;
                            item.Peers = item.Peers - item.Seeders; // Use peers as leechers
                            results.Add(item);
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "An error occured during manual search on " + indexer.DisplayName + ":  " + e.Message);
                }
            });

            ConfigureCacheResults(results);

            if (trackers.Count > 1)
            {
                results = results.OrderByDescending(d => d.PublishDate).ToList();
            }

            var manualResult = new ManualSearchResult()
            {
                Results = results,
                Indexers = trackers.Select(t => t.DisplayName).ToList()
            };


            if (manualResult.Indexers.Count == 0)
                manualResult.Indexers = new List<string>() { "None" };

            _logger.Info(string.Format("Manual search for \"{0}\" on {1} with {2} results.", query.GetQueryString(), string.Join(", ", manualResult.Indexers), manualResult.Results.Count));
            return manualResult;
        }
    }
}
