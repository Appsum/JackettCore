﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using JackettCore.Models;
using JackettCore.Models.IndexerConfig;
using JackettCore.Services;
using JackettCore.Utils;
using JackettCore.Utils.Clients;
using Newtonsoft.Json.Linq;

namespace JackettCore.Indexers
{
	public class Hounddawgs : BaseIndexer, IIndexer
	{
		private string LoginUrl { get { return SiteLink + "login.php"; } }
		private string SearchUrl { get { return SiteLink + "torrents.php"; } }

		new NxtGnConfigurationData configData
		{
			get { return (NxtGnConfigurationData)base.configData; }
			set { base.configData = value; }
		}

		public Hounddawgs(IIndexerManagerService i, Logger l, IWebClient c, IProtectionService ps)
			: base(name: "Hounddawgs",
				description: "A danish closed torrent tracker",
				link: "https://hounddawgs.org/",
				caps: TorznabUtil.CreateDefaultTorznabTVCaps(),
				manager: i,
				client: c,
				logger: l,
				p: ps,
				configData: new NxtGnConfigurationData())
		{
			AddCategoryMapping(92, TorznabCatType.TV);
			AddCategoryMapping(92, TorznabCatType.TVHD);
			AddCategoryMapping(92, TorznabCatType.TVWEBDL);

			AddCategoryMapping(93, TorznabCatType.TVSD);
			AddCategoryMapping(93, TorznabCatType.TV);

			AddCategoryMapping(57, TorznabCatType.TV);
			AddCategoryMapping(57, TorznabCatType.TVHD);
			AddCategoryMapping(57, TorznabCatType.TVWEBDL);

			AddCategoryMapping(74, TorznabCatType.TVSD);
			AddCategoryMapping(74, TorznabCatType.TV);

		}

		public async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
		{
			configData.LoadValuesFromJson(configJson);
			var pairs = new Dictionary<string, string> {
				{ "username", configData.Username.Value },
				{ "password", configData.Password.Value },
				{ "keeplogged", "1" },
				{ "login", "Login" }

			};
			// Get inital cookies
            var response = await RequestLoginAndFollowRedirect(LoginUrl, pairs, null, true, null, "https://hounddawgs.org/");

			await ConfigureIfOK(response.Cookies, response.Content != null && response.Content.Contains("Velkommen til"), () =>
				{
					CQ dom = response.Content;
					var messageEl = dom["inputs"];
					var errorMessage = messageEl.Text().Trim();
					throw new ExceptionWithConfigData(errorMessage, configData);
				});
			return IndexerConfigurationStatus.RequiresTesting;
		}

		public async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
		{
			var releases = new List<ReleaseInfo>();
			var episodeSearchUrl = string.Format("{0}?&searchstr={1}", SearchUrl, HttpUtility.UrlEncode(query.GetQueryString()));
			var results = await RequestStringWithCookiesAndRetry(episodeSearchUrl);
			if (results.Content.Contains("Din søgning gav intet resultat."))
			{
				return releases;
			}
			try
			{
				CQ dom = results.Content;

				var rows = dom["#torrent_table > tbody > tr"].ToArray();

				foreach (var row in rows.Skip(1))
				{
					var release = new ReleaseInfo();
					release.MinimumRatio = 1;
					release.MinimumSeedTime = 172800;

					var seriesCats = new[] { 92, 93, 57, 74 };
					var qCat = row.ChildElements.ElementAt(0).ChildElements.ElementAt(0).Cq();
					var catUrl = qCat.Attr("href");
					var cat = catUrl.Substring(catUrl.LastIndexOf('[') + 1);
					var catNo = int.Parse(cat.Trim(']'));
					if (seriesCats.Contains(catNo))
						release.Category = TorznabCatType.TV.ID;
					else
						continue;

					var qAdded = row.ChildElements.ElementAt(4).ChildElements.ElementAt(0).Cq();
					var addedStr = qAdded.Attr("title");
					release.PublishDate = DateTime.ParseExact(addedStr, "MMM dd yyyy, HH:mm", CultureInfo.InvariantCulture);

					var qLink = row.ChildElements.ElementAt(1).ChildElements.ElementAt(2).Cq();
					release.Title = qLink.Text().Trim();
					release.Description = release.Title;

					release.Comments = new Uri(SiteLink + qLink.Attr("href"));
					release.Guid = release.Comments;

					var qDownload = row.ChildElements.ElementAt(1).ChildElements.ElementAt(1).ChildElements.ElementAt(0).Cq();
					release.Link = new Uri(SiteLink + qDownload.Attr("href"));

					var sizeStr = row.ChildElements.ElementAt(5).Cq().Text();
					release.Size = ReleaseInfo.GetBytes(sizeStr);

					release.Seeders = ParseUtil.CoerceInt(row.ChildElements.ElementAt(6).Cq().Text());
					release.Peers = ParseUtil.CoerceInt(row.ChildElements.ElementAt(7).Cq().Text()) + release.Seeders;

					releases.Add(release);
				}
			}
			catch (Exception ex)
			{
				OnParseError(results.Content, ex);
			}

			return releases;
		}
		public class NxtGnConfigurationData : ConfigurationData
		{
			public NxtGnConfigurationData()
			{
				Username = new StringItem { Name = "Username" };
				Password = new StringItem { Name = "Password" };
			}
			public StringItem Username { get; private set; }
			public StringItem Password { get; private set; }
		}
	}
}
