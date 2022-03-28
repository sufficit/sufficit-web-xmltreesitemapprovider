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
                cache.Remove(item.Key);
            }
        }

        public void Clear(HttpContext context)
        {
            var prefix = GetPrefix(context);
            foreach (var item in cache)
            {   
                if(item.Key.StartsWith(prefix))
                    cache.Remove(item.Key);
            }
        }

        private string GetKey(HttpContext context, SiteMapNode node) =>          
            GetPrefix(context) + "//" + node.Key;


        /// <summary>
        /// Important to know when refresh individual cache
        /// If user id or name changes, use another indexes
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private string GetPrefix(HttpContext context)
        {            
            string prefix = string.Empty;
            if (context?.Session != null)
                prefix += "//" + context.Session.SessionID;

            if (context?.User?.Identity != null)
                prefix += "//" + context.User.Identity.Name;

            return prefix;
        }
    }
}
