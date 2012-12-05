using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Configuration;
using System.Collections;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.ServiceModel.Syndication;
using System.Xml;
using MonoSoftware.MonoX;
using System.Web.UI.WebControls.WebParts;
using MonoSoftware.Web;
using MonoSoftware.MonoX.Caching;
using MonoSoftware.MonoX.Utilities;
using MonoSoftware.MonoX.Repositories;
using MonoSoftware.MonoX.DAL.EntityClasses;
using MonoSoftware.MonoX.DAL.HelperClasses;

namespace TwitterFeed
{
    public partial class TwitterFeedModule : BasePagedPart
    {
        #region Fields
        /// <summary>
        /// Twitter Feed SEO pager page number query string name.
        /// </summary>
        public static UrlParam<int> TwitterFeedPageNo = new UrlParam<int>("TwitterFeedPageNo");

        /// <summary>
        /// Tweets cache key.
        /// </summary>
        protected static string TweetsCacheKey = "Tweets";
        #endregion

        #region Properties
        private Guid _listId = Guid.Empty;
        /// <summary>
        /// Id of the list that is displayed in this module.
        /// </summary>
        [WebBrowsable(true), Personalizable(true), WebEditor(typeof(ListEditorPart))]
        [WebDescription("List to display")]
        [WebDisplayName("List to display")]
        public Guid ListId
        {
            get { return _listId; }
            set { _listId = value; }
        }

        private string _listName = string.Empty;
        /// <summary>
        /// Name of the list that is displayed in this module.
        /// </summary>
        public string ListName
        {
            get { return _listName; }
            set { _listName = value; }
        }

        /// <summary>
        /// Pager page size.
        /// </summary>
        [WebBrowsable(true), Personalizable(true)]
        [WebDescription("Pager page size")]
        [WebDisplayName("Pager page size")]
        public int PageSize
        {
            get { return pager.PageSize; }
            set { pager.PageSize = value; }
        }

        private bool _hideIfEmpty = false;
        /// <summary>
        /// Hide part if it doesn't contain any data.
        /// </summary>
        [WebBrowsable(true), Personalizable(true)]
        [WebDescription("Hide if empty")]
        [WebDisplayName("Hide if empty")]
        public bool HideIfEmpty
        {
            get { return _hideIfEmpty; }
            set { _hideIfEmpty = value; }
        }

        private int _interval = 10;
        /// <summary>
        /// Gets or sets the refresh interval.
        /// <para>
        /// Default refresh interval is 10 minutes.
        /// </para>
        /// </summary>
        [WebBrowsable(true), Personalizable(true)]
        [WebDescription("Tweet Refresh Interval")]
        [WebDisplayName("Tweet Refresh Interval")]
        public int Interval
        {
            get
            {
                return _interval;
            }
            set
            {
                _interval = value;
                this.CacheDuration = value * 60;
            }
        }

        /// <summary>
        /// Gets or sets Tweet count.
        /// </summary>
        [WebBrowsable(true), Personalizable(true)]
        [WebDescription("Tweet Display Count")]
        [WebDisplayName("Tweet Display Count")]
        public Int32? TweetsCount { get; set; }
        
        /// <summary>
        /// Gets or sets Twitter profile name.
        /// </summary>
        [WebBrowsable(true), Personalizable(true)]
        [WebDescription("Twitter Profile Name")]
        [WebDisplayName("Twitter Profile Name")]
        public string ProfileName { get; set; }

        
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor.
        /// </summary>
        public TwitterFeedModule()
        {
            this.CacheDuration = Interval * 60;
            Title = "Twitter Feed module";
            IsTemplated = true;
            ControlSpecificTemplateSubPath = "TwitterFeedTemplates";
            PagerQueryString = TwitterFeedPageNo.Name;
        }
        #endregion

        #region Page Events
        protected void Page_Init(object sender, EventArgs e)
        {
            InitPagerTemplate(pager, PagerQueryString, this.PagerQueryStringSuffix);
            pager.PageIndexChanged += new MonoSoftware.Web.Pager.PageChangedEventHandler(pager_PageIndexChanged);
            this.lvItems.ItemDataBound += new EventHandler<ListViewItemEventArgs>(lvItems_OnItemDataBound);
        }

        protected void Page_PreRender(object sender, EventArgs e)
        {
            if (this.HideIfEmpty || this.Visible)
                DataBind();
        } 
        #endregion

        #region Methods
        /// <summary>
        /// Apply web part property changes (Note: Overridden property still needs to be marked as <see cref="MonoSoftware.MonoX.WebPartApplyChangesAttribute"/>).
        /// <para>
        /// Note: Marked with <see cref="MonoSoftware.MonoX.WebPartApplyChangesAttribute"/> attribute so it is called from ApplyChanges event in the editor part to refresh the module appearance.
        /// </para>
        /// </summary>
        [WebPartApplyChanges]
        public override void ApplyChanges()
        {
            base.ApplyChanges();
            InitPagerTemplate(pager, this.PagerQueryString, this.PagerQueryStringSuffix);
            DataBind();
        }

        public override void DataBind()
        {
            //base.DataBind();   
            MonoXCacheManager cacheManager = MonoXCacheManager.GetInstance(TweetsCacheKey, this.CacheDuration);

            KeyValuePair<SyndicationFeed, int> bindContainer = cacheManager.Get<KeyValuePair<SyndicationFeed, int>>(ProfileName, TweetsCount);
            if (bindContainer.Value == 0)
            {
                try
                {
                    TweetsCount = TweetsCount.HasValue ? TweetsCount : 10;
                    var url = string.Format("http://api.twitter.com/1/statuses/user_timeline.rss?screen_name={0}&count={1}", ProfileName, TweetsCount);
                    SyndicationFeed feed = LoadFrom(url);
                    bindContainer = new KeyValuePair<SyndicationFeed, int>(feed, feed.Items.Count());
                    cacheManager.Store(bindContainer, ProfileName, TweetsCount);
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }

                try
                {
                    if (Page.User.Identity.IsAuthenticated)
                    {
                        //Save the Tweets to the DB
                        Guid listId = Guid.Empty;
                        if (!Guid.Empty.Equals(ListId))
                            listId = ListId;
                        else if (!String.IsNullOrEmpty(ListName))
                            listId = BaseMonoXRepository.GetInstance().GetFieldValue<Guid>(ListFields.Title, ListName, ListFields.Id);

                        if (Guid.Empty.Equals(listId) && !String.IsNullOrEmpty(ListName))
                        {
                            //Create a List
                            ListEntity list = ListRepository.GetInstance().CreateNewList();
                            list.Title = ListName;
                            list.UserId = SecurityUtility.GetUserId();
                            list.ListType = 0;
                            ListRepository.GetInstance().SaveEntity(list, true);
                            listId = list.Id;
                        }

                        if (!Guid.Empty.Equals(listId))
                        {
                            foreach (var item in bindContainer.Key.Items)
                            {
                                Guid urlId = BaseMonoXRepository.GetInstance().GetFieldValue<Guid>(ListItemLocalizationFields.ItemUrl, item.Id, ListItemLocalizationFields.Id);
                                if (!Guid.Empty.Equals(urlId)) break; //Suppose that we have imported upcoming tweets

                                ListItemEntity listItem = ListRepository.GetInstance().CreateNewListItem(listId);
                                listItem.DateCreated = Convert.ToDateTime(item.PublishDate.ToString());
                                listItem.ItemTitle = HtmlFormatTweet(item.Title.Text);
                                listItem.ItemUrl = item.Id;
                                ListRepository.GetInstance().SaveEntity(listItem);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            }

            //Note: We need to perform the in-memory paging 
            List<SyndicationItem> items = bindContainer.Key.Items.Skip(pager.CurrentPageIndex * pager.PageSize).Take(pager.PageSize).ToList();
            PagerUtility.BindPager(pager, DataBind, lvItems, items, bindContainer.Value);

            if (HideIfEmpty)
                this.Visible = (bindContainer.Value != 0);
        }

        protected void lvItems_OnItemDataBound(object sender, ListViewItemEventArgs e)
        {
            if (e.Item.ItemType == ListViewItemType.DataItem)
            {
                SyndicationItem listItem = ((ListViewDataItem)e.Item).DataItem as SyndicationItem;
                Hashtable tags = ParseTemplateTags(listItem);
                RenderTemplatedPart(e.Item, CurrentTemplateHtml, tags);
            }
        }

        protected virtual Hashtable ParseTemplateTags(SyndicationItem listItem)
        {
            Hashtable tags = new Hashtable(StringComparer.InvariantCultureIgnoreCase);
            tags.Add("<# Content #>", HtmlFormatTweet(listItem.Title.Text));
            tags.Add("<# PubDate #>", listItem.PublishDate.ToString());
            tags.Add("<# Url #>", listItem.Links.First().Uri.ToString());
            return tags;
        }

        void pager_PageIndexChanged(object source, MonoSoftware.Web.Pager.PageChangedEventArgs e)
        {
            pager.CurrentPageIndex = e.NewPageIndex;
            DataBind();
        } 

        /// <summary>
        /// Loads feed from the specified URL.
        /// </summary>
        /// <param name="url">URL of the feed.</param>
        /// <returns>Syndication feed.</returns>
        public static SyndicationFeed LoadFrom(String url)
        {
            if (String.IsNullOrEmpty(url))
                throw new ArgumentNullException();

            SyndicationFeed feed = null;
            using (XmlTextReader r = new XmlTextReader(url))
            {
                feed = SyndicationFeed.Load(r);
            }
            return feed;
        }

        /// <summary>
        /// Format the Tweet and prepare it for Html rendering.
        /// </summary>
        /// <param name="item">Tweet content</param>
        /// <returns>Html formatted Tweet</returns>
        protected virtual string HtmlFormatTweet(string item)
        {
            string newPost = item.Replace(String.Format("{0}:", ProfileName), String.Empty);

            List<KeyValuePair<string, string>> regExRules = new List<KeyValuePair<string, string>>();
            regExRules.Add(new KeyValuePair<string, string>(@"(http:\/\/([\w.]+\/?)\S*)", "<a href=\"$1\" target=\"_blank\">$1</a>"));
            regExRules.Add(new KeyValuePair<string, string>("(@\\w+)", "<a href=\"http://twitter.com/$1\" target=\"_blank\">$1</a> "));
            regExRules.Add(new KeyValuePair<string, string>("(#)(\\w+)", "<a href=\"http://search.twitter.com/search?q=$2\" target=\"_blank\">$1$2</a>"));

            foreach (var regExRule in regExRules)
            {
                Regex urlregex = new Regex(regExRule.Key, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                newPost = urlregex.Replace(newPost, regExRule.Value);
            }

            return newPost;
        }
        #endregion
    }
}