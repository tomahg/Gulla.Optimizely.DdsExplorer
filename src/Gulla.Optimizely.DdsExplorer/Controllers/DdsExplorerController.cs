using Gulla.Optimizely.DdsExplorer.Configuration;
using Gulla.Optimizely.DdsExplorer.Services;
using Gulla.Optimizely.DdsExplorer.ViewModels;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Gulla.Optimizely.DdsExplorer.Controllers
{
    [Route(RoutePrefix)]
    [Authorize(Policy = DdsExplorerAuthorizationPolicy.Default)]
    public class DdsExplorerController : Controller
    {
        public const string RoutePrefix = "DdsExplorer";

        private readonly IAntiforgery _antiforgery;
        private readonly AntiforgeryOptions _antiforgeryOptions;
        private readonly SystemStoreClassifier _classifier;

        public DdsExplorerController(IAntiforgery antiforgery, IOptions<AntiforgeryOptions> antiforgeryOptions, SystemStoreClassifier classifier)
        {
            _antiforgery = antiforgery;
            _antiforgeryOptions = antiforgeryOptions.Value;
            _classifier = classifier;
        }

        [HttpGet("")]
        public IActionResult Index() => Page("Index", null);

        /// <summary>
        /// A sub-path of the menu item's URL, so <see cref="Menu.SubPathUrlMenuItem"/> still counts
        /// as selected here and the Settings rail loads. Catch-all so a store name may hold a slash.
        /// </summary>
        [HttpGet("store/{*name}")]
        public IActionResult Store(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return RedirectToAction(nameof(Index));
            }

            return Page("Store", name);
        }

        private IActionResult Page(string view, string storeName)
        {
            // The CMS shell's React chrome (<platform-navigation>) polls
            // /EPiServer/CMS/stores/notification via axios, which expects a XSRF-TOKEN cookie.
            // GetAndStoreTokens sets that cookie; without it the poll 400s.
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);

            return View(view, new DdsExplorerViewModel
            {
                StoreName = storeName,
                IsSystemStore = storeName != null && _classifier.IsSystem(storeName),
                AntiforgeryHeaderName = tokens.HeaderName ?? _antiforgeryOptions.HeaderName ?? "RequestVerificationToken",
                AntiforgeryToken = tokens.RequestToken
            });
        }
    }
}
