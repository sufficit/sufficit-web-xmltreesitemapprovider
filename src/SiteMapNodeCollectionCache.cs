using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.Caching;
using System.Collections.Specialized;
using System.Web;

namespace Sufficit.Web
{
    public class SiteMapNodeCollectionCache
    {
        private readonly MemoryCache cache;
        private TimeSpan defaultExpiration; 

        public SiteMapNodeCollectionCache()
        {
            cache = new MemoryCache(nameof(SiteMapNodeCollectionCache), null);
            defaultExpiration = TimeSpan.FromHours(5);
        }

        public IEnumerable<T> OfType<T>()
        {
            foreach(var item in cache)
            {
                if(item.Value != null && item.Value is T value)
                    yield return value;
            }
        }

        public bool Contains(HttpContext context, SiteMapNode node)
        {
            string key = GetKey(context, node);
            return cache.Contains(key);
        }

        public SiteMapNodeCollection GetValue(HttpContext context, SiteMapNode node)
        {
            string key = GetKey(context, node);
            return cache[key] as SiteMapNodeCollection;
        }

        public bool Add(HttpContext context, SiteMapNode node, SiteMapNodeCollection obj)
        {
            string key = GetKey(context, node);
            var policy = new CacheItemPolicy() { SlidingExpiration = defaultExpiration };
            return cache.Add(key, obj, policy);
        }

        public object Remove(HttpContext context, SiteMapNode node)
        {
            string key = GetKey(context, node);
            return cache.Remove(key);
        }

        public void Clear()
        {
            foreach (var item in cache)
            {
                cache.Remove(item.Key, CacheEntryRemovedReason.Removed);
            }
        }

        private string GetKey(HttpContext context, SiteMapNode node)
        {
            return context.Session.SessionID + "//" + node.Key;
        }
    }
}
