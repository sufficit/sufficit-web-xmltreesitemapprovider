using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml;
using System.Collections.Specialized;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Security.Permissions;
using System.Threading;
using System.Threading.Tasks;

namespace Sufficit.Web
{
    [AspNetHostingPermission(SecurityAction.Demand, Level = AspNetHostingPermissionLevel.Minimal)]
    public class XmlTreeSiteMapProvider : SiteMapProvider
    {
        private readonly SiteMapNodeCollectionCache _cache;
        private readonly ILogger _logger;
        private readonly List<string> _absolutePaths;

        protected string titulo;
        protected NameValueCollection atributos;
        private XmlDocument _mapadosite;
        private bool _debug;

        public XmlTreeSiteMapProvider(ILogger<XmlTreeSiteMapProvider> logger)
        {
            _logger = logger;

            _cache = new SiteMapNodeCollectionCache();
            _absolutePaths = new List<string>();
            _logger.LogDebug("logging system initialized, dependency injection");                        
        }


        #region CHANGING MONITOR

        private void CreateFileWatcher(string absolutePath)
        {
            _logger.LogDebug($"including a watcher to file: { absolutePath }");

            // Create a new FileSystemWatcher and set its properties.
            var watcher = new FileSystemWatcher();

            watcher.Path = Path.GetDirectoryName(absolutePath);
            watcher.Filter = Path.GetFileName(absolutePath);

            /* Watch for changes in LastAccess and LastWrite times, and 
               the renaming of files or directories. */
            watcher.NotifyFilter = NotifyFilters.LastWrite;

            // Add event handlers.
            watcher.Changed += OnChanged;

            // Begin watching.
            watcher.EnableRaisingEvents = true;
        }

        private object _lockChanges = new object();
        private DateTime lastChange;
        private CancellationTokenSource _cancellationTokenSource;
        private int _changeCounter;

        // Define the event handlers.
        private async void OnChanged(object source, FileSystemEventArgs e)
        {
            _changeCounter++;
            CancellationTokenSource cts = _cancellationTokenSource;
            var writeTime = File.GetLastWriteTime(e.FullPath);
            lock (_lockChanges) 
            {
                if (writeTime > lastChange)
                    lastChange = writeTime;
                else return;

                if (cts != null)
                {
                    if (!cts.IsCancellationRequested)
                        cts.Cancel();
                }

                cts = _cancellationTokenSource = new CancellationTokenSource();
            }
            
            // Waiting to avoid simultaneos executing proccess, avoid file read locking error
            await Task.Delay(_changeCounter * 200, cts.Token).ContinueWith((sourceTask) => {
                if (sourceTask.IsCanceled || cts.IsCancellationRequested) return;

                // Specify what is done when a file is changed, created, or deleted.
                _logger.LogInformation($"({writeTime.ToLongTimeString()}) file changed, path: { e.FullPath }, change type: { e.ChangeType }");

                if(Generate(atributos, cts.Token))
                    _changeCounter = 0;
            });
        }

        #endregion
        #region POPULATE AND GENERATE

        private bool Generate(NameValueCollection attributes, CancellationToken cancellationToken = default)
        {
            #region CREATING XML DOCUMENT

            var sitemapXml = new XmlDocument();
            XmlElement raiz = sitemapXml.CreateElement("siteMap", "http://schemas.microsoft.com/AspNet/SiteMap-File-1.0");
            XmlAttribute info = sitemapXml.CreateAttribute("debug");
            info.Value = string.Join(",", attributes.AllKeys.Select(key => key + "=" + attributes[key]));
            raiz.Attributes.Append(info);
            sitemapXml.AppendChild(raiz);
            XmlProcessingInstruction pi = sitemapXml.CreateProcessingInstruction("xml", "version=\"1.0\" encoding=\"utf-8\" ");
            sitemapXml.InsertBefore(pi, raiz);

            #endregion

            if (cancellationToken.IsCancellationRequested)
                return false;
                       
            XmlPopulate(ref sitemapXml, _absolutePaths, cancellationToken);

            var countCheck = sitemapXml.SelectNodes("//@id")?.Count;
            if (countCheck > 0 && !cancellationToken.IsCancellationRequested)
            {
                _mapadosite = sitemapXml;
                _logger.LogDebug($"sitemap generated with success, ({ countCheck }) nodes");

                // cleanup session cache
                _cache.Clear();

                if (_debug)

                    // Backuping generated file
                    _mapadosite.Save(System.AppDomain.CurrentDomain.BaseDirectory + "Web.sitemap.debug");
                return true;
            }

            return false;
        }

        protected void XmlPopulate(ref XmlDocument document, IEnumerable<string> files, CancellationToken cancellationToken = default)
        {
            if (!files.Any()) { 
                _logger.LogWarning("empty list files on populating");
                return;
            }

            int _contador = 0;
            foreach (string absolutePath in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                bool root;
                if (_contador == 0) { root = true; } else { root = false; }
                XmlDocument doc = new XmlDocument();
                try
                {
                    using (var fileStream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (fileStream.Length > 0)
                        {
                            doc.Load(fileStream);
                        }
                        else
                        {
                            _logger.LogWarning($"empty file on populating: { absolutePath }");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"error on populate with file: { absolutePath }");
                    continue;
                }

                if (doc.HasChildNodes)
                {
                    foreach (XmlElement ell in doc.GetElementsByTagName("siteMapNode"))
                    {
                        string stringURL = ell.GetAttribute("url");
                        if (!string.IsNullOrWhiteSpace(stringURL)) ell.SetAttribute("url", stringURL.ToLowerInvariant());
                        ell.SetAttribute("id", _contador.ToString());
                        _contador++;
                    }
                    if (root)
                    {
                        document["siteMap"].AppendChild(document.ImportNode(doc["siteMap"]["siteMapNode"], true));
                    }
                    else
                    {
                        document["siteMap"]["siteMapNode"].AppendChild(document.ImportNode(doc["siteMap"]["siteMapNode"], true));
                    }
                }
                else
                {
                    _logger.LogWarning($"trying to populate an empty document: { absolutePath }");
                }
            }
        }

        #endregion
            

        private string RelativeToAbsolutePath(string s)
        {
            return System.AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + Path.GetDirectoryName(s) + Path.GetFileName(s);
        }

        #region PUBLIC OVERRIDE VOID INITIALIZE ( STRING, NAMEVALUECOLLECTION )

        public override void Initialize(string name, NameValueCollection attributes)
        {
            #region INICIALIZANDO ATRIBUTOS

            var debugAtt = attributes["Debug"];
            if (debugAtt != null)            
                _debug = bool.Parse(debugAtt.ToString());
            
            #endregion

            titulo = name;
            atributos = attributes;
            base.Initialize(name, attributes);

            #region FONTE DE DADOS

            var sitemaps = new List<string>();
            if (!string.IsNullOrWhiteSpace(attributes["mapsList"]))
            {
                sitemaps.AddRange(attributes["mapsList"].Split(new char[] { ';', ',' }));
            }

            _absolutePaths.Clear();
            foreach (var relativePath in sitemaps)
            {
                var absolutePath = RelativeToAbsolutePath(relativePath);

                _absolutePaths.Add(absolutePath);

                // appending file watcher
                CreateFileWatcher(absolutePath);
            }

            #endregion

            Generate(attributes);
        }

        #endregion

        #region PUBLIC VIRTUAL SITEMAPNODE CREATENODEFROMXML ( XMLNODE )

        public virtual SiteMapNode CreateNodeFromXml(XmlNode node)
        {
            if (node != null)
            {
                string id, url, titulo, descricao; id = url = titulo = descricao = string.Empty;
                var roles = new HashSet<string>();

                if (node.Attributes["id"] != null) { id = node.Attributes["id"].Value; }
                if (node.Attributes["url"] != null) { url = node.Attributes["url"].Value; }
                if (node.Attributes["title"] != null) { titulo = node.Attributes["title"].Value; }
                if (node.Attributes["description"] != null) { descricao = node.Attributes["description"].Value; }
                if (node.Attributes["roles"] != null)
                {
                    foreach (var role in node.Attributes["roles"].Value.Split(new char[] { ';', ',' }))
                    {
                        if (!string.IsNullOrWhiteSpace(role))
                            roles.Add(role.Trim().ToLowerInvariant());
                    }
                }

                return new SiteMapNode(this, id, url, titulo, descricao, roles.ToArray(), null, null, null);
            }

            return null;
        }

        #endregion
        #region PUBLIC OVERRIDE SITEMAPNODE FINDSITEMAPNODE ( STRING )

        public override SiteMapNode FindSiteMapNode(string rawUrl)
        {
            string depuracao = string.Empty;
            string urlToLower = rawUrl.ToLowerInvariant();
            if (urlToLower.Contains('?'))
            {
                urlToLower = urlToLower.Split('?')[0];
                depuracao += "removendo itens da url após ? ;";
            }
            if (urlToLower.Contains('#'))
            {
                urlToLower = urlToLower.Split('#')[0];
                depuracao += "removendo itens da url após # ;";
            }
            SiteMapNode sitenode = null;

            XmlNodeList nodes = _mapadosite.SelectNodes("//*[contains(@url, '" + urlToLower + "')]");
            depuracao += nodes.Count + " nodes encontrados;";
            foreach (XmlNode node in nodes)
            {
                //depuracao += "PATH URL: " + Path.GetFullPath(urlToLower) + " ;;; PATH NODE: " + Path.GetFullPath(node.Attributes["url"].Value);            
                if (Path.GetFullPath(urlToLower) == Path.GetFullPath(node.Attributes["url"].Value))
                {
                    sitenode = CreateNodeFromXml(node);
                    break;
                }
            }

            _logger.LogTrace($"FindSiteMapNode: {urlToLower + " :: " + depuracao}");
            return sitenode;
        }

        #endregion
        #region PUBLIC OVERRIDE SITEMAPNODE FINDSITEMAPNODEFROMKEY ( STRING )

        public override SiteMapNode FindSiteMapNodeFromKey(string key)
        {
            return CreateNodeFromXml(_mapadosite.SelectSingleNode("//*[@id='" + key + "']"));
        }

        #endregion        
        #region PUBLIC OVERRIDE SITEMAPNODE GETPARENTNODE ( SITEMAPNODE )

        public override SiteMapNode GetParentNode(SiteMapNode node)
        {
            if (node.Key == "0") { return null; }
            return CreateNodeFromXml(_mapadosite.SelectSingleNode("//*[@id='" + node.Key + "']").ParentNode);
        }

        #endregion
        #region PUBLIC OVERRIDE BOOL ISACCESSIBLETOUSER ( HTTPCONTEXT, SITEMAPNODE )

        public override bool IsAccessibleToUser(HttpContext context, SiteMapNode node)
        {
            bool isAccessible = false;
            string reason = string.Empty;
            if (SecurityTrimmingEnabled)
            {
                if (node.Roles == null || node.Roles.Count == 0)
                {
                    isAccessible = true;
                    reason += "WebSiteMap Node sem roles configurado;";
                }
                else
                {
                    if (!context.Request.IsAuthenticated)
                    {
                        if (node.Roles.Contains("?"))
                        {
                            isAccessible = true;
                            reason += "Node contém ? não autenticado;";
                        }
                    }
                    else
                    {
                        if (node.Roles.Contains("*"))
                        {
                            isAccessible = true;
                            reason += "Node contém * qualquer usuário autenticado;";
                        }
                        else
                        {
                            // METODO GENERICO USANDO IDENTITY                        
                            foreach (string role in node.Roles)
                            {
                                if (context.User.IsInRole(role.ToLowerInvariant().Trim()))
                                {
                                    isAccessible = true;
                                    reason += "usuário pertence ao grupo: " + role.Trim() + ";";
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                isAccessible = true;
                reason += "Security Trimming não habilitado;";
            }

            _logger.LogTrace($"IsAccessibleToUser, node: { node?.Title }, authenticated: { context.Request.IsAuthenticated }, is accessible: { isAccessible }, reason: { reason }");
            return isAccessible;
        }

        #endregion

        #region ABSTRACT - PUBLIC OVERRIDE SITEMAPNODECOLLECTION GETCHILDNODES ( SITEMAPNODE )

        public override SiteMapNodeCollection GetChildNodes(SiteMapNode node)
        {
            var baseNode = _mapadosite.SelectSingleNode("//*[@id='" + node.Key + "']");
            if (baseNode == null) throw new Exception("node not found on base");

            if (_cache.Contains(HttpContext.Current, node))
                return _cache.GetValue(HttpContext.Current, node);

            _logger.LogTrace($"GetChildNodes: {node.Key}");
            SiteMapNodeCollection collection = new SiteMapNodeCollection();
            foreach (XmlNode baseChild in baseNode.ChildNodes)
            {
                SiteMapNode nodeChild = CreateNodeFromXml(baseChild);
                if (IsAccessibleToUser(HttpContext.Current, nodeChild))
                    collection.Add(nodeChild);
            }

            _cache.Add(HttpContext.Current, node, collection);
            return collection;
        }

        #endregion
        #region ABSTRACT - PROTECTED OVERRIDE SITEMAPNODE GET ROOTNODECORE ( )

        protected override SiteMapNode GetRootNodeCore()
        {
            var node = _mapadosite.SelectSingleNode("//*[@id='0']");
            if (node == null) throw new Exception("root node not found");

            return CreateNodeFromXml(node);
        }

        #endregion

        
    }

}