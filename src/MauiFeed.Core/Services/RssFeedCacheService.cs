﻿// <copyright file="RssFeedCacheService.cs" company="Drastic Actions">
// Copyright (c) Drastic Actions. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using MauiFeed.Models;
using Microsoft.EntityFrameworkCore;

namespace MauiFeed.Services
{
    /// <summary>
    /// Rss Feed Cache Service.
    /// </summary>
    public class RssFeedCacheService
    {
        private FeedService rssService;
        private DatabaseContext databaseContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="RssFeedCacheService"/> class.
        /// </summary>
        /// <param name="rssService">Rss Service.</param>
        /// <param name="databaseContext">Database Context.</param>
        public RssFeedCacheService(FeedService rssService, DatabaseContext databaseContext)
        {
            this.rssService = rssService;
            this.databaseContext = databaseContext;
        }

        /// <summary>
        /// Initial Feed.
        /// </summary>
        /// <param name="uri">Uri.</param>
        /// <returns>FeedListItem.</returns>
        public async Task<FeedListItem> RetrieveFeedAsync(string uri)
            => await this.RetrieveFeedAsync(new Uri(uri));

        /// <summary>
        /// Initial Feed.
        /// </summary>
        /// <param name="uri">Uri.</param>
        /// <returns>FeedListItem.</returns>
        public async Task<FeedListItem> RetrieveFeedAsync(Uri uri)
        {
            var feedItem = await this.databaseContext.FeedListItems!.FirstOrDefaultAsync(n => n.Uri == uri);
            if (feedItem is null)
            {
                feedItem = new FeedListItem() { Uri = uri };
            }

            return await this.RefreshFeedAsync(feedItem);
        }

        /// <summary>
        /// Refresh Feeds Async.
        /// </summary>
        /// <param name="progress">Optional Progress Marker.</param>
        /// <returns>Task.</returns>
        public async Task RefreshFeedsAsync(IProgress<RssCacheFeedUpdate>? progress = default)
        {
            var feeds = await this.databaseContext.FeedListItems!.ToListAsync();
            await this.RefreshFeedsAsync(feeds, progress);
        }

        /// <summary>
        /// Refresh Feeds Async.
        /// </summary>
        /// <param name="feeds">Feeds.</param>
        /// <param name="progress">Optional Progress Marker.</param>
        /// <returns>Task.</returns>
        public async Task RefreshFeedsAsync(List<FeedListItem> feeds, IProgress<RssCacheFeedUpdate>? progress = default)
        {
            var count = feeds.Count;
            int current = 0;

            ConcurrentBag<Tuple<FeedListItem, IList<FeedItem>>> results = new ConcurrentBag<Tuple<FeedListItem, IList<FeedItem>>>();

            await Parallel.ForEachAsync(feeds, async (i, c) =>
            {
                FeedListItem? item = i;
                (var feed, var feedListItems) = await this.rssService.ReadFeedAsync(item);
                if (feed is not null)
                {
                    results.Add(new Tuple<FeedListItem, IList<FeedItem>>(feed, feedListItems!));
                }

                current = current + 1;
                progress?.Report(new RssCacheFeedUpdate(current, feeds.Count, feed));
            });

            IEnumerable<FeedListItem> feedResults = results.Select(n => n.Item1);
            IEnumerable<FeedItem> feedItemResults = results.SelectMany(n => n.Item2);
            var result = await this.databaseContext.RefreshFeedsAsync(feedResults, feedItemResults);
            progress?.Report(new RssCacheFeedUpdate());
        }

        /// <summary>
        /// Refresh Feed.
        /// </summary>
        /// <param name="item">FeedListItem to update.</param>
        /// <returns>Task.</returns>
        public async Task<FeedListItem> RefreshFeedAsync(FeedListItem item)
        {
            (var feed, var feedListItems) = await this.rssService.ReadFeedAsync(item);
            var result = this.databaseContext.AddOrUpdateFeedListItemAsync(feed, feedListItems);
            return feed!;
        }
    }
}
