﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Exceptionless.Json;
using HtmlAgilityPack;
using KryBot.Core.Cookies;
using KryBot.Core.Giveaways;
using RestSharp;  
using KryBot.Core.Json.UseGamble;

namespace KryBot.Core.Sites
{
	public class UseGamble
	{
		public UseGamble()
		{
			Cookies = new UseGambleCookie();
			Giveaways = new List<UseGambleGiveaway>();
		}

		public bool Enabled { get; set; }
		public int Points { get; set; }
		public int MaxJoinValue { get; set; } = 30;
		public int PointsReserv { get; set; }
		public UseGambleCookie Cookies { get; set; }
		public List<UseGambleGiveaway> Giveaways { get; set; }

		public void Logout()
		{
			Cookies = new UseGambleCookie();
			Enabled = false;
		}

		#region JoinGiveaway

		private Log JoinGiveaway(UseGambleGiveaway giveaway)
		{
			Thread.Sleep(400);
			if (giveaway.Code != null)
			{
				var list = new List<HttpHeader>();
				var header = new HttpHeader
				{
					Name = "X-Requested-With",
					Value = "XMLHttpRequest"
				};
				list.Add(header);

				var response = Web.Post(Links.UseGambleJoin,
					Generate.PostData_UseGamble(giveaway.Code), list,
					Cookies.Generate());
				var jresponse =
					JsonConvert.DeserializeObject<JsonJoin>(response.RestResponse.Content.Replace(".", ""));
				if (jresponse.Error == 0)
				{
					Points = jresponse.target_h.my_coins;
					return Messages.GiveawayJoined("UseGamble", giveaway.Name, giveaway.Price,
						jresponse.target_h.my_coins);
				}
				return Messages.GiveawayNotJoined("UseGamble", giveaway.Name, "Error");
			}
			return null;
		}

		public async Task<Log> Join(int index)
		{
			var task = new TaskCompletionSource<Log>();
			await Task.Run(() =>
			{
				var result = JoinGiveaway(Giveaways[index]);
				task.SetResult(result);
			});

			return task.Task.Result;
		}

		#endregion

		#region Parse

		private Log GetProfile()
		{
			var response = Web.Get(Links.UseGamble, Cookies.Generate());

			if (response.RestResponse.Content != string.Empty)
			{
				var htmlDoc = new HtmlDocument();
				htmlDoc.LoadHtml(response.RestResponse.Content);

				var points = htmlDoc.DocumentNode.SelectSingleNode("//span[@class='my_coins']");
				var profileLink = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='mp-wrap']/a[1]");
				if (points != null && profileLink != null)
				{
					Points = int.Parse(points.InnerText);
					return Messages.ParseProfile("UseGamble", Points, profileLink.InnerText);
				}
			}
			return Messages.ParseProfileFailed("UseGamble");
		}

		public async Task<Log> CheckLogin()
		{
			var task = new TaskCompletionSource<Log>();
			await Task.Run(() =>
			{
				var result = GetProfile();
				task.SetResult(result);
			});

			return task.Task.Result;
		}

		private Log WonParse()
		{
			var response = Web.Get(Links.UseGambleWon, Cookies.Generate());

			if (response.RestResponse.Content != string.Empty)
			{
				var htmlDoc = new HtmlDocument();
				htmlDoc.LoadHtml(response.RestResponse.Content);

				var nodes = htmlDoc.DocumentNode.SelectNodes("//tr[@class='gray']");
				if (nodes != null)
				{
					for (var i = 0; i < nodes.Count; i++)
					{
						var content = nodes[i].SelectSingleNode("//tr/td[2]").InnerText;
						if (!content.Contains("you've won the Giveaway"))
						{
							nodes.Remove(nodes[i]);
							i--;
						}
					}
					return Messages.GiveawayHaveWon("UseGamble", nodes.Count, Links.UseGambleWon);
				}
			}
			return null;
		}

		public async Task<Log> CheckWon()
		{
			var task = new TaskCompletionSource<Log>();
			await Task.Run(() =>
			{
				var result = WonParse();
				task.SetResult(result);
			});

			return task.Task.Result;
		}

		private Log LoadGiveaways(Blacklist blackList)
		{
			Giveaways?.Clear();

			var pages = 1;

			for (var i = 0; i < pages; i++)
			{
				if (pages != 1)
				{
					var headerList = new List<HttpHeader>();
					var header = new HttpHeader
					{
						Name = "X-Requested-With",
						Value = "XMLHttpRequest"
					};
					headerList.Add(header);

					var jsonresponse = Web.Post(Links.UseGambleGaPage,
						Generate.PageData_UseGamble(i + 1), headerList,
						Cookies.Generate());
					if (jsonresponse.RestResponse.Content != string.Empty)
					{
						var data = jsonresponse.RestResponse.Content.Replace("\\", "");
						var htmlDoc = new HtmlDocument();
						htmlDoc.LoadHtml(data);

						var nodes = htmlDoc.DocumentNode.SelectNodes("//div[@class='giveaway_container']");
						AddGiveaways(nodes);
					}
				}
				else
				{
					var response = Web.Get(Links.UseGambleGiveaways, Cookies.Generate());

					if (response.RestResponse.Content != string.Empty)
					{
						var htmlDoc = new HtmlDocument();
						htmlDoc.LoadHtml(response.RestResponse.Content);

						var count =
							htmlDoc.DocumentNode.SelectNodes("//div[@class='nPagin']//div[@class='pagin']/span");
						if (count != null)
						{
							pages = int.Parse(htmlDoc.DocumentNode.
								SelectSingleNode($"//div[@class='nPagin']//div[@class='pagin']/span[{count.Count - 1}]")
								.InnerText);
						}

						var nodes =
							htmlDoc.DocumentNode.SelectNodes("//div[@id='normal']/div[@class='giveaway_container']");
						AddGiveaways(nodes);
					}
				}
			}

			if (Giveaways == null)
			{
				return Messages.ParseGiveawaysEmpty("UseGamble");
			}

			Tools.RemoveBlacklistedGames(Giveaways, blackList);

			return Messages.ParseGiveawaysFoundMatchGiveaways("UseGamble", Giveaways.Count.ToString());
		}

		public async Task<Log> LoadGiveawaysAsync(Blacklist blackList)
		{
			var task = new TaskCompletionSource<Log>();
			await Task.Run(() =>
			{
				var result = LoadGiveaways(blackList);
				task.SetResult(result);
			});

			return task.Task.Result;
		}

		private void AddGiveaways(HtmlNodeCollection nodes)
		{
			if (nodes != null)
			{
				foreach (var node in nodes)
				{
					var name = node.SelectSingleNode(".//div[@class='giveaway_name']");
					var storeId = node.SelectSingleNode(".//a[@class='steam-icon']");
					if (name != null && storeId != null)
					{
						var spGiveaway = new UseGambleGiveaway
						{
							Name = name.InnerText,
							StoreId = storeId.Attributes["href"].Value.Split('/')[4]
						};

						var price = node.SelectSingleNode(".//span[@class='coin-white-icon']");
						var code = node.SelectSingleNode(".//div[@class='ga_join_btn ga_coin_join']");
						if (price != null && code != null)
						{
							spGiveaway.Price = int.Parse(price.InnerText);
							spGiveaway.Code = code.Attributes["onclick"].Value.Split('\'')[5].Replace("ga:", "");

							var iconsBlock = node.SelectSingleNode(".//div[@class='giveaway_iconbar']");
							var icons = iconsBlock?.SelectNodes(".//span");
							if (icons != null)
							{
								foreach (var icon in icons)
								{
									if (icon.Attributes["class"].Value.Contains("region"))
									{
										spGiveaway.Region = icon.Attributes["class"].Value.Split('-')[1];
									}
								}
							}

							if (spGiveaway.Price <= Points &&
							    spGiveaway.Price <= MaxJoinValue)
							{
								Giveaways?.Add(spGiveaway);
							}
						}
					}
				}
			}
		}

		#endregion
	}
}