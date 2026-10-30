using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebApplicationforCICD.Exceptions;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    // Dependency injection: ASP.NET Core creates this class and passes these in
    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    // Called automatically when an unhandled exception occurs
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // 1) Log full error (for you / ops) — never hide this in logs
        _logger.LogError(exception,
            "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        // 2) Decide HTTP status + user-friendly messages
        var (statusCode, title, detail) = MapException(exception);

        // 3) Build standard error body (RFC 7807 Problem Details)
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        // Helps match client error to server logs
        problem.Extensions["traceId"] =
            Activity.Current?.Id ?? httpContext.TraceIdentifier;

        // 4) Send JSON response
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        // true = "I handled it; don't use default error page"
        return true;
    }

    private (int StatusCode, string Title, string Detail) MapException(Exception exception)
    {
        // EF / SQL errors
        if (exception is DbUpdateException dbEx)
        {
            var sqlMessage = dbEx.InnerException?.Message ?? dbEx.Message;

            // Your Swagger POST with "id" case
            if (sqlMessage.Contains("identity column", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    StatusCodes.Status400BadRequest,
                    "Invalid create request",
                    "Do not send 'id' when creating a product. SQL Server assigns the id automatically.");
            }

            return (
                StatusCodes.Status400BadRequest,
                "Database update failed",
                _environment.IsDevelopment()
                    ? sqlMessage
                    : "Could not save changes. Check your data and try again.");
        }

        // Everything else
        return (
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred",
            _environment.IsDevelopment()
                ? exception.Message
                : "Something went wrong on the server.");
    }
}