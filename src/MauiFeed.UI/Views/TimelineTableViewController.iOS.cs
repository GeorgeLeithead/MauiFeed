using System.Text.RegularExpressions;
using Drastic.PureLayout;
using Drastic.Services;
using Drastic.Tools;
using MauiFeed.Models;
using MauiFeed.Services;
using MauiFeed.UI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MauiFeed.UI.Views;

    /// <summary>
    /// Timeline Table View Controller.
    /// </summary>
    public class TimelineTableViewController : UIViewController
    {
        private SidebarItem? sidebarItem;
        private MainUIViewController controller;
        private DatabaseContext database;
        private FeedItem? selectedItem;
        private IErrorHandlerService errorHandler;
        private RssTableView tableView;

        /// <summary>
        /// Initializes a new instance of the <see cref="TimelineTableViewController"/> class.
        /// </summary>
        /// <param name="controller">Root View Controller.</param>
        public TimelineTableViewController(MainUIViewController controller, IServiceProvider provider)
        {
            this.controller = controller;
            this.database = (DatabaseContext)provider.GetRequiredService<DatabaseContext>()!;
            this.errorHandler = (IErrorHandlerService)provider.GetRequiredService<IErrorHandlerService>()!;
            this.tableView = new RssTableView(this.View!.Frame, UITableViewStyle.Plain);
            this.View!.AddSubview(this.tableView);
            this.tableView.TranslatesAutoresizingMaskIntoConstraints = false;
            this.tableView.AutoPinEdgesToSuperviewEdges();
        }

        /// <summary>
        /// Gets or sets the sidebar item.
        /// </summary>
        public SidebarItem? SidebarItem
        {
            get
            {
                return this.sidebarItem;
            }

            set
            {
                this.sidebarItem = value;
                this.UpdateFeed();
            }
        }

        /// <summary>
        /// Gets or sets the selected item.
        /// </summary>
        public FeedItem? SelectedItem
        {
            get
            {
                return this.selectedItem;
            }

            set
            {
                this.selectedItem = value;
                this.UpdateSelectedFeedItemAsync().FireAndForgetSafeAsync(this.errorHandler);
            }
        }

        public IList<FeedItem> Items => this.sidebarItem?.Query?.ToList() ?? new List<FeedItem>();

        /// <summary>
        /// Gets a value indicating whether to show the icon.
        /// </summary>
        public bool ShowIcon
        {
            get
            {
                // If it's a smart filter or folder, always show the icon.
                if (this.sidebarItem?.SidebarItemType != SidebarItemType.FeedListItem)
                {
                    return true;
                }

                var feed = this.Items.Select(n => n.Feed).Distinct() ?? new List<FeedListItem>();
                return feed.Count() > 1;
            }
        }

        /// <summary>
        /// Update the selected feed item, if it exists.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task UpdateSelectedFeedItemAsync()
        {
            if (this.SelectedItem is null)
            {
                return;
            }

            this.database.FeedItems!.Update(this.SelectedItem);
            await this.database.SaveChangesAsync();

            // this.controller.Sidebar.UpdateSidebar();
        }

        /// <summary>
        /// Update the given feed.
        /// </summary>
        public void UpdateFeed()
        {
            var items = this.Items ?? new List<FeedItem>();
            this.tableView.Source = new TableSource(this, items.ToArray());
            this.tableView.ReloadData();
        }

        /// <summary>
        /// Set the given feed item.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="path">Path.</param>
        public void SelectFeedItem(FeedItem item, NSIndexPath? path = default)
        {
            item.IsRead = true;
            this.SelectedItem = item;
            if (path is not null)
            {
                var cell = (RssItemViewCell)this.tableView.CellAt(path)!;
                cell.UpdateIsRead();
            }

            // this.controller.Webview.SetFeedItem(this.SelectedItem);
        }

        private class RssTableView : UITableView, IUITableViewDelegate
        {
            public RssTableView(CGRect rect, UITableViewStyle style)
                : base(rect, style)
            {
                this.RowHeight = UITableView.AutomaticDimension;
                this.Delegate = this;
            }
        }

        private class TableSource : UITableViewSource
        {
            private FeedItem[] tableItems;
            private TimelineTableViewController controller;
            private bool showIcon;

            public TableSource(TimelineTableViewController controller, FeedItem[] items)
            {
                this.controller = controller;
                this.tableItems = items;
                this.showIcon = this.controller.ShowIcon;
            }

            public override nint RowsInSection(UITableView tableview, nint section)
            {
                return this.tableItems.Length;
            }

            public override nfloat GetHeightForRow(UITableView tableView, NSIndexPath indexPath)
            {
                return 100f;
            }

            public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
            {
                RssItemViewCell? cell = tableView.DequeueReusableCell(RssItemViewCell.PublicReuseIdentifier) as RssItemViewCell;
                FeedItem item = this.tableItems[indexPath.Row];

                if (cell == null)
                {
                    cell = new RssItemViewCell(item, this.showIcon);
                }
                else
                {
                    cell.SetupCell(item, this.showIcon);
                }

                return cell;
            }

            public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
            {
                var item = this.tableItems[indexPath.Row];
                this.controller.SelectFeedItem(item, indexPath);
            }
        }

        private class RssItemViewCell : UITableViewCell
        {
            private FeedItem item;

            private UIView hasSeenHolder = new UIView();
            private UIView iconHolder = new UIView();
            private UIView feedHolder = new UIView();

            private UIView content = new UIView();

            private UIImageView hasSeenIcon = new UIImageView();
            private UIImageView icon = new UIImageView();
            private UILabel title = new UILabel() { Lines = 3, Font = UIFont.PreferredHeadline, TextAlignment = UITextAlignment.Left };
            private UILabel description = new UILabel() { Lines = 2, Font = UIFont.PreferredSubheadline, TextAlignment = UITextAlignment.Left };
            private UILabel releaseDate = new UILabel() { Lines = 1, Font = UIFont.PreferredCaption1, TextAlignment = UITextAlignment.Right };
            private UILabel author = new UILabel() { Lines = 1, Font = UIFont.PreferredCaption1, TextAlignment = UITextAlignment.Left };

            public RssItemViewCell(FeedItem info, bool showIcon = false, UITableViewCellStyle style = UITableViewCellStyle.Default)
          : base(style, PublicReuseIdentifier)
            {
                this.item = info;
                this.icon.Layer.CornerRadius = 5;
                this.icon.Layer.MasksToBounds = true;
                #if IOS
                this.title.Lines = 2;
                this.description.Lines = 1;
                #endif
                this.SetupUI();
                this.SetupLayout();
                this.SetupCell(info, showIcon);
            }

            /// <summary>
            /// Gets the Reuse Identifier.
            /// </summary>
            public static NSString PublicReuseIdentifier => new NSString("rssItemCell");

            public void SetupUI()
            {
                this.ContentView.AddSubview(this.content);

                this.content.AddSubview(this.hasSeenHolder);
                this.content.AddSubview(this.iconHolder);
                this.content.AddSubview(this.feedHolder);

                this.hasSeenHolder.AddSubview(this.hasSeenIcon);

                this.iconHolder.AddSubview(this.icon);

                this.feedHolder.AddSubview(this.title);
                this.feedHolder.AddSubview(this.description);
                this.feedHolder.AddSubview(this.author);
                this.feedHolder.AddSubview(this.releaseDate);

                this.hasSeenIcon.Image = UIImage.GetSystemImage("circle.fill");
            }

            public void SetupLayout()
            {
                this.content.AutoPinEdgesToSuperviewEdges();

                this.hasSeenHolder.AutoPinEdgesToSuperviewEdgesExcludingEdge(UIEdgeInsets.Zero, ALEdge.Right);
                this.hasSeenHolder.AutoPinEdge(ALEdge.Right, ALEdge.Left, this.iconHolder);
                this.hasSeenHolder.AutoSetDimension(ALDimension.Width, 25f);

                this.iconHolder.AutoPinEdge(ALEdge.Left, ALEdge.Right, this.hasSeenHolder);
                this.iconHolder.AutoPinEdge(ALEdge.Right, ALEdge.Left, this.feedHolder);
                this.iconHolder.AutoPinEdge(ALEdge.Top, ALEdge.Top, this.content);
                this.iconHolder.AutoPinEdge(ALEdge.Bottom, ALEdge.Bottom, this.content);
                this.iconHolder.AutoSetDimension(ALDimension.Width, 60f);

                this.hasSeenIcon.AutoCenterInSuperview();
                this.hasSeenIcon.AutoSetDimensionsToSize(new CGSize(12, 12));

                this.icon.AutoCenterInSuperview();
                this.icon.AutoSetDimensionsToSize(new CGSize(50f, 50f));

                this.feedHolder.AutoPinEdgesToSuperviewEdgesExcludingEdge(new UIEdgeInsets(top: 0f, left: 0f, bottom: 0f, right: 0f), ALEdge.Left);
                this.feedHolder.AutoPinEdge(ALEdge.Left, ALEdge.Right, this.iconHolder);

                this.title.AutoPinEdge(ALEdge.Top, ALEdge.Top, this.feedHolder, 5f);
                this.title.AutoPinEdge(ALEdge.Right, ALEdge.Right, this.feedHolder, -15f);
                this.title.AutoPinEdge(ALEdge.Left, ALEdge.Left, this.feedHolder, 10f);

                this.description.AutoPinEdge(ALEdge.Top, ALEdge.Bottom, this.title, 0);
                this.description.AutoPinEdge(ALEdge.Right, ALEdge.Right, this.title);
                this.description.AutoPinEdge(ALEdge.Left, ALEdge.Left, this.title);

                this.author.AutoPinEdge(ALEdge.Bottom, ALEdge.Bottom, this.content, -5);
                this.author.AutoPinEdge(ALEdge.Left, ALEdge.Left, this.title);
                this.author.AutoPinEdge(ALEdge.Right, ALEdge.Left, this.releaseDate);

                this.releaseDate.AutoPinEdge(ALEdge.Bottom, ALEdge.Bottom, this.content,　-5f);
                this.releaseDate.AutoPinEdge(ALEdge.Right, ALEdge.Right, this.title);
                this.releaseDate.AutoPinEdge(ALEdge.Left, ALEdge.Right, this.author);
            }

            public void SetupCell(FeedItem item, bool showIcon)
            {
                this.item = item;

                this.icon.Image = UIImage.LoadFromData(NSData.FromArray(item.Feed!.ImageCache!))!.WithRoundedCorners(5f);
                this.title.Text = item.Title;
                this.author.Text = item.Author;

                var htmlString = !string.IsNullOrEmpty(item.Description) ? item.Description : item.Content;

                // We don't want to render the HTML, we just want to get the raw text out.
                this.description.Text = Regex.Replace(htmlString ?? string.Empty, "<[^>]*>", string.Empty)!.Trim();

                this.releaseDate.Text = item.PublishingDate?.ToShortDateString();

                this.UpdateIsRead();
            }

            public void UpdateIsRead()
            {
                if (this.item?.IsFavorite ?? false)
                {
                    this.InvokeOnMainThread(() => this.hasSeenIcon.Image = UIImage.GetSystemImage("circle.fill")!.ApplyTintColor(UIColor.Yellow));
                }
                else
                {
                    this.InvokeOnMainThread(() => this.hasSeenIcon.Image = this.item?.IsRead ?? false ? UIImage.GetSystemImage("circle") : UIImage.GetSystemImage("circle.fill"));
                }
            }
        }
    }

    /// <summary>
    /// UIImage Extensions.
    /// </summary>
    public static class UIImageExtensions
    {
        /// <summary>
        /// Convert a UIImage to one with rounded corners.
        /// </summary>
        /// <param name="image">Image.</param>
        /// <param name="radius">Radius.</param>
        /// <returns>Image with rounded corners.</returns>
        public static UIImage WithRoundedCorners(this UIImage image, nfloat? radius = null)
        {
            var maxRadius = Math.Min(image.Size.Width, image.Size.Height) / 2;
            var cornerRadius = radius.HasValue && radius.Value > 0 && radius.Value <= maxRadius
                ? radius.Value
                : maxRadius;

            UIGraphics.BeginImageContextWithOptions(image.Size, false, image.CurrentScale);
            CGRect rect = new CGRect(CGPoint.Empty, image.Size);
            UIBezierPath path = UIBezierPath.FromRoundedRect(rect, (nfloat)cornerRadius);
            path.AddClip();
            image.Draw(rect);
            UIImage roundedImage = UIGraphics.GetImageFromCurrentImageContext();
            UIGraphics.EndImageContext();

            return roundedImage;
        }
    }