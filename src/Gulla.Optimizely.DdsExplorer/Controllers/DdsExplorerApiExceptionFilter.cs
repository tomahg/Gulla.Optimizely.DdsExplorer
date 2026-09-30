using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Gulla.Optimizely.DdsExplorer.Controllers
{
    /// <summary>
    /// Turns an unhandled exception in the API into a JSON 500 that names it. Without this the
    /// site's own error handling answers instead — typically a generic HTML page — and the only
    /// thing the explorer can show is "500 server error", with the cause buried in the log.
    /// The endpoints are admin-only, so naming the exception type and message is acceptable.
    /// </summary>
    public sealed class DdsExplorerApiExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<DdsExplorerApiExceptionFilter> _logger;

        public DdsExplorerApiExceptionFilter(ILogger<DdsExplorerApiExceptionFilter> logger)
        {
            _logger = logger;
        }

        public void OnException(ExceptionContext context)
        {
            var ex = context.Exception;
            _logger.LogError(ex, "DDS Explorer API request {Method} {Path} failed.", context.HttpContext.Request.Method, context.HttpContext.Request.Path);

            context.Result = new ObjectResult(new { message = ex.GetType().Name + ": " + ex.Message })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            context.ExceptionHandled = true;
        }
    }
}
