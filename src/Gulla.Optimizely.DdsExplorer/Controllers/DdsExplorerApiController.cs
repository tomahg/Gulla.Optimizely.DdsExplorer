using System;
using System.Collections.Generic;
using Gulla.Optimizely.DdsExplorer.Configuration;
using Gulla.Optimizely.DdsExplorer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gulla.Optimizely.DdsExplorer.Controllers
{
    /// <summary>
    /// The store name travels in the query string rather than in the path: names are free text
    /// (dots, slashes, spaces), and a catch-all segment can only ever be the last one.
    /// Every mutating action validates the antiforgery token the page renders.
    /// </summary>
    [Route(DdsExplorerController.RoutePrefix + "/api")]
    [Authorize(Policy = DdsExplorerAuthorizationPolicy.Default)]
    [ApiController]
    [TypeFilter(typeof(DdsExplorerApiExceptionFilter))]
    public class DdsExplorerApiController : ControllerBase
    {
        private readonly IDdsStoreService _service;

        public DdsExplorerApiController(IDdsStoreService service)
        {
            _service = service;
        }

        private string UserName => User?.Identity?.Name ?? "(anonymous)";

        [HttpGet("stores")]
        public IActionResult ListStores() => Ok(_service.ListStores());

        [HttpGet("store")]
        public IActionResult GetStore([FromQuery] string name)
        {
            var store = _service.GetStore(name);
            return store == null ? StoreNotFound(name) : Ok(store);
        }

        [HttpGet("items")]
        public IActionResult GetItems([FromQuery] string store, [FromQuery] int page = 1)
        {
            var items = _service.GetItems(store, page);
            return items == null ? StoreNotFound(store) : Ok(items);
        }

        [HttpGet("item")]
        public IActionResult GetItem([FromQuery] string store, [FromQuery] string id)
        {
            var item = _service.GetItem(store, id);
            return item == null ? NotFound(new { message = $"No item '{id}' in store '{store}'." }) : Ok(item);
        }

        [HttpPut("item")]
        [ValidateAntiForgeryToken]
        public IActionResult SaveItem([FromQuery] string store, [FromQuery] string id, [FromBody] SaveItemRequest request)
        {
            var result = _service.SaveItem(store, id, request?.Json, request?.Version, UserName);
            if (result.NotFound)
            {
                return NotFound(new { message = $"No item '{id}' in store '{store}'." });
            }

            if (result.Conflict)
            {
                return Conflict(result);
            }

            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpDelete("item")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteItem([FromQuery] string store, [FromQuery] string id)
        {
            return _service.DeleteItem(store, id, UserName)
                ? NoContent()
                : NotFound(new { message = $"No item '{id}' in store '{store}'." });
        }

        [HttpPost("items/delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteItems([FromQuery] string store, [FromBody] DeleteItemsRequest request)
        {
            var deleted = _service.DeleteItems(store, request?.Ids ?? [], UserName);
            return deleted == null ? StoreNotFound(store) : Ok(new { deleted });
        }

        [HttpPost("store/empty")]
        [ValidateAntiForgeryToken]
        public IActionResult EmptyStore([FromQuery] string name, [FromBody] ConfirmRequest request)
        {
            if (!Confirmed(name, request))
            {
                return BadRequest(new { message = "Type the store name exactly to confirm." });
            }

            var deleted = _service.EmptyStore(name, UserName);
            return deleted == null ? StoreNotFound(name) : Ok(new { deleted });
        }

        [HttpPost("store/delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteStore([FromQuery] string name, [FromBody] ConfirmRequest request)
        {
            if (!Confirmed(name, request))
            {
                return BadRequest(new { message = "Type the store name exactly to confirm." });
            }

            return _service.DeleteStore(name, UserName) ? NoContent() : StoreNotFound(name);
        }

        /// <summary>
        /// Checked on the server as well as in the page, so a stray request cannot wipe a store
        /// without naming it.
        /// </summary>
        private static bool Confirmed(string name, ConfirmRequest request) =>
            !string.IsNullOrEmpty(name) && string.Equals(name, request?.ConfirmName, StringComparison.Ordinal);

        private NotFoundObjectResult StoreNotFound(string name) => NotFound(new { message = $"No store named '{name}'." });

        public sealed class SaveItemRequest
        {
            /// <summary>The edited document as raw text, parsed on the server so no number is rounded on the way.</summary>
            public string Json { get; set; }

            /// <summary>The <see cref="Models.ItemDetails.Version"/> the edited copy was loaded with.</summary>
            public string Version { get; set; }
        }

        public sealed class DeleteItemsRequest
        {
            public List<string> Ids { get; set; }
        }

        public sealed class ConfirmRequest
        {
            public string ConfirmName { get; set; }
        }
    }
}
